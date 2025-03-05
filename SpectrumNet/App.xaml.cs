#nullable enable

using Serilog.Core;

namespace SpectrumNet;

public enum LogLevel { Debug, Information, Warning, Error }

public static class SmartLogger
{
    const string LogPrefix = "[SmartLogger] ";
    static readonly ConcurrentDictionary<string, DateTime> _messageLastLoggedTimes = new();
    static readonly ConcurrentDictionary<string, int> _highFrequencyMessageCounters = new();
    static DateTime _lastCacheCleanupTime = DateTime.UtcNow;
    static readonly TimeSpan MinTimeBetweenLogs = TimeSpan.FromMilliseconds(500);
    static int _defaultHighFrequencyLogRatio = 20;
    public static bool IsAnyTransitionActive { get; set; } = false;

    public static void SetHighFrequencyLogRatio(int ratio)
    {
        if (ratio > 0)
            _defaultHighFrequencyLogRatio = ratio;
    }

    public static void Log(LogLevel level, string source, string message, bool forceLog = false, int highFrequencyLogRatio = 0, bool ignoreTransitionState = false)
    {
        // Раскомментируйте для активации throttling:
        // if (!LoggingThrottler.ShouldLogMessage(ref level, source, message, forceLog, highFrequencyLogRatio, ignoreTransitionState)) return;

        // Базовое логирование
        switch (level)
        {
            case LogLevel.Debug:
                Serilog.Log.Debug(message);
                break;
            case LogLevel.Information:
                Serilog.Log.Information(message);
                break;
            case LogLevel.Warning:
                Serilog.Log.Warning(message);
                break;
            case LogLevel.Error:
                Serilog.Log.Error(message);
                break;
        }

        CleanupLoggingCache();
    }

    static void CleanupLoggingCache()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastCacheCleanupTime).TotalMinutes < 5)
            return;
        _lastCacheCleanupTime = now;
        var keysToRemove = new List<string>();
        foreach (var entry in _messageLastLoggedTimes)
            if ((now - entry.Value).TotalSeconds > 60)
                keysToRemove.Add(entry.Key);
        foreach (var key in keysToRemove)
        {
            _messageLastLoggedTimes.TryRemove(key, out _);
            _highFrequencyMessageCounters.TryRemove(key, out _);
        }
#if DEBUG
        Serilog.Log.Debug($"{LogPrefix}Cache cleanup: removed {keysToRemove.Count} entries, current size: {_messageLastLoggedTimes.Count}");
#endif
    }

    public static void Reset()
    {
        _messageLastLoggedTimes.Clear();
        _highFrequencyMessageCounters.Clear();
        _lastCacheCleanupTime = DateTime.UtcNow;
        IsAnyTransitionActive = false;
    }

    private static class LoggingThrottler
    {
        public static bool ShouldLogMessage(ref LogLevel level, string source, string message, bool forceLog, int highFrequencyLogRatio, bool ignoreTransitionState)
        {
            if (highFrequencyLogRatio <= 0)
                highFrequencyLogRatio = _defaultHighFrequencyLogRatio;

            if (!forceLog)
            {
                if (level == LogLevel.Warning && IsAnyTransitionActive && !ignoreTransitionState)
                    level = LogLevel.Debug;

                string key = $"{source}:{message.GetHashCode()}";

                if (_messageLastLoggedTimes.TryGetValue(key, out DateTime lastTime))
                {
                    if ((DateTime.UtcNow - lastTime) < MinTimeBetweenLogs && !HasImportantKeyword(message))
                        return false;
                }

                if (IsFrequentMessagePattern(message))
                {
                    int count = _highFrequencyMessageCounters.AddOrUpdate(key, 1, (_, c) => c + 1);
                    if (count % highFrequencyLogRatio != 0)
                        return false;
                }

                _messageLastLoggedTimes[key] = DateTime.UtcNow;
            }
            return true;
        }

        private static bool HasImportantKeyword(string message)
        {
            foreach (var keyword in App.ImportantKeywords)
                if (message.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static bool IsFrequentMessagePattern(string message)
        {
            string[] patterns = {
                "Performing FFT calculation",
                "Spectrum calculated",
                "Processing FFT data",
                "ConvertToSpectrum",
                "LINEAR processing",
                "LOGARITHMIC processing"
            };
            foreach (var pattern in patterns)
                if (message.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}

public partial class App : Application
{
    const string LogDirectoryPath = "logs";
    const string LatestLogFileName = "latest.log";
    const int MaxFileSizeMB = 5, RetainedFileCount = 10;
    const string ApplicationName = "SpectrumNet";
    const string OutputTemplate = "{Timestamp:HH:mm:ss} [{Level:u3}] [Thread:{ThreadId}] {Message:lj}{NewLine}{Exception}";
    static readonly string ApplicationVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";
    static ILoggerFactory _loggerFactory = null!;
    public static ILoggerFactory LoggerFactory => _loggerFactory;
    public static int MaxMessagesPerSecond { get; set; } = 40;
    public static readonly HashSet<string> ImportantKeywords = new(StringComparer.OrdinalIgnoreCase) {
        "initialized", "started", "changed", "changing", "completed", "synchronized",
        "reset", "disposed", "closed", "registered", "pausing", "resuming", "error",
        "exception", "failed", "initializing", "starting", "stopping", "success",
        "state updated", "renderer synchronized", "window type change", "restarting",
        "reinitialized", "reconnecting", "transitioning"
    };

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            InitializeLogging();
            _loggerFactory = new LoggerFactory().AddSerilog();
            var logger = _loggerFactory.CreateLogger<App>();
            logger.LogInformation("Application '{Application}' version '{Version}' started", "SpectrumNet", ApplicationVersion);
#if DEBUG
            SmartLogger.SetHighFrequencyLogRatio(10);
#else
            SmartLogger.SetHighFrequencyLogRatio(50);
#endif
            AppDomain.CurrentDomain.UnhandledException += (_, args) => {
                logger.LogCritical(args.ExceptionObject as Exception, "Unhandled exception in application");
            };
            base.OnStartup(e);
            SpectrumNet.CommonResources.InitialiseResources();
            Current.Exit += (_, _) => SmartLogger.Log(LogLevel.Information, "App",
                $"Application '{ApplicationVersion}' is shutting down normally", forceLog: true);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Error initializing logging");
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            SmartLogger.Log(LogLevel.Information, "App",
                $"Application '{ApplicationName}' version '{ApplicationVersion}' closed with exit code: {e.ApplicationExitCode}", forceLog: true);
        }
        finally
        {
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }

    void InitializeLogging()
    {
        EnsureLogDirectoryExists();
        DeleteLatestLogIfExists();
        var loggerConfig = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            // Раскомментируйте для активации MessageRateLimiter:
            // .Filter.With(new MessageRateLimiter(App.MaxMessagesPerSecond))
            .Enrich.WithProperty("Application", "SpectrumNet")
            .Enrich.WithProperty("Version", ApplicationVersion)
            .Enrich.WithMachineName()
            .Enrich.WithEnvironmentUserName()
            .Enrich.WithThreadId();
        ConfigureLogOutputs(loggerConfig);
        Log.Logger = loggerConfig.CreateLogger();
    }

    void ConfigureLogOutputs(LoggerConfiguration loggerConfig)
    {
#if DEBUG
        loggerConfig.WriteTo.Console(outputTemplate: OutputTemplate);
#endif
        loggerConfig.WriteTo.File(
            Path.Combine(LogDirectoryPath, LatestLogFileName),
            outputTemplate: OutputTemplate,
            fileSizeLimitBytes: MaxFileSizeMB * 1024 * 1024,
            retainedFileCountLimit: RetainedFileCount,
            buffered: false,
            flushToDiskInterval: TimeSpan.FromSeconds(1));
    }

    public static void ReconfigureLogging()
    {
        SmartLogger.Log(LogLevel.Information, "App", "Reconfiguring logging settings", forceLog: true);
        Log.CloseAndFlush();
        var loggerConfig = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            // Раскомментируйте для активации MessageRateLimiter:
            // .Filter.With(new MessageRateLimiter(App.MaxMessagesPerSecond))
            .Enrich.WithProperty("Application", "SpectrumNet")
            .Enrich.WithProperty("Version", ApplicationVersion)
            .Enrich.WithMachineName()
            .Enrich.WithEnvironmentUserName()
            .Enrich.WithThreadId();
#if DEBUG
        loggerConfig.WriteTo.Console(outputTemplate: OutputTemplate);
#endif
        loggerConfig.WriteTo.File(
            Path.Combine(LogDirectoryPath, LatestLogFileName),
            outputTemplate: OutputTemplate,
            fileSizeLimitBytes: MaxFileSizeMB * 1024 * 1024,
            retainedFileCountLimit: RetainedFileCount,
            buffered: false,
            flushToDiskInterval: TimeSpan.FromSeconds(1));
        Log.Logger = loggerConfig.CreateLogger();
        _loggerFactory = new LoggerFactory().AddSerilog();
        SmartLogger.Reset();
        SmartLogger.Log(LogLevel.Information, "App", "Logging reconfigured successfully", forceLog: true);
    }

    void EnsureLogDirectoryExists()
    {
        if (!Directory.Exists(LogDirectoryPath))
            Directory.CreateDirectory(LogDirectoryPath);
    }

    void DeleteLatestLogIfExists()
    {
        var latestLogPath = Path.Combine(LogDirectoryPath, LatestLogFileName);
        try
        {
            if (File.Exists(latestLogPath))
                File.Delete(latestLogPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error deleting latest log, creating backup");
            var backupPath = Path.Combine(LogDirectoryPath, $"latest_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            File.Move(latestLogPath, backupPath);
        }
    }
}

public class MessageRateLimiter : ILogEventFilter
{
    readonly ConcurrentDictionary<string, Queue<DateTime>> _messageTimestamps = new();
    readonly int _maxMessagesPerSecond;
    DateTime _lastCleanupTime = DateTime.UtcNow;

    public MessageRateLimiter(int maxMessagesPerSecond) => _maxMessagesPerSecond = maxMessagesPerSecond;

    public bool IsEnabled(LogEvent logEvent)
    {
        if (logEvent.Level == LogEventLevel.Error || logEvent.Level == LogEventLevel.Fatal)
            return true;
        if (logEvent.Level != LogEventLevel.Debug)
            return true;
        string message = logEvent.MessageTemplate.Text;
        if (HasImportantKeyword(message))
            return true;
        CleanupTimestamps();
        string source = GetSourceKey(logEvent);
        var now = DateTime.UtcNow;
        if (!_messageTimestamps.TryGetValue(source, out var timestamps))
        {
            timestamps = new Queue<DateTime>();
            _messageTimestamps[source] = timestamps;
        }
        while (timestamps.Count > 0 && (now - timestamps.Peek()).TotalSeconds > 1)
            timestamps.Dequeue();
        if (timestamps.Count >= _maxMessagesPerSecond)
            return false;
        timestamps.Enqueue(now);
        return true;
    }

    bool HasImportantKeyword(string message)
    {
        foreach (var keyword in App.ImportantKeywords)
            if (message.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    string GetSourceKey(LogEvent logEvent)
    {
        string threadId = GetThreadId(logEvent);
        string component = GetComponent(logEvent.MessageTemplate.Text);
        return $"{component}:{threadId}";
    }

    string GetThreadId(LogEvent logEvent)
    {
        if (logEvent.Properties.TryGetValue("ThreadId", out var threadIdProp) &&
            threadIdProp is ScalarValue threadIdValue)
            return threadIdValue.Value?.ToString() ?? "0";
        return "0";
    }

    string GetComponent(string message)
    {
        int start = message.IndexOf('[');
        if (start >= 0)
        {
            int end = message.IndexOf(']', start);
            if (end > start)
                return message.Substring(start + 1, end - start - 1);
        }
        return "Unknown";
    }

    void CleanupTimestamps()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastCleanupTime).TotalMinutes < 5)
            return;
        _lastCleanupTime = now;
        var keysToRemove = new List<string>();
        foreach (var entry in _messageTimestamps)
            if (entry.Value.Count == 0 || (now - entry.Value.Peek()).TotalSeconds > 10)
                keysToRemove.Add(entry.Key);
        foreach (var key in keysToRemove)
            _messageTimestamps.TryRemove(key, out _);
    }
}