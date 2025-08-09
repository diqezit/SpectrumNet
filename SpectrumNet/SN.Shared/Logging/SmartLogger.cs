#nullable enable

namespace SpectrumNet.SN.Shared.Logging;

public class SmartLogger : ISmartLogger
{
    private const string
        LogDirectoryPath = "logs",
        LatestLogFileName = "latest.log",
        OutputTemplate = "{Timestamp:HH:mm:ss} [{Level:u3}] [T:{ThreadId}] [{Source}] {Message}{NewLine}{Exception}";

    private const int
        MaxFileSizeMB = 5,
        RetainedFileCount = 10;

    private static readonly SmartLogger _instance = new();
    public static SmartLogger Instance => _instance;

    // Инициализация логгера
    public static void Initialize()
    {
        Instance.Safe(() =>
        {
            EnsureLogDirectoryExists();
            TryDeleteLatestLog();
            ConfigureLogger();
            LogStartupInfo();
        }, nameof(SmartLogger), "Error initializing logging");
    }

    #region ISmartLogger Implementation

    public void Log(LogLevel level, string source, string message, bool forceLog = false) =>
        ForContext("Source", source).Write(ToSerilogLevel(level), message);

    public void Debug(string source, string message) =>
        Log(LogLevel.Debug, source, message);

    public void Info(string source, string message) =>
        Log(LogLevel.Information, source, message);

    public void Warning(string source, string message) =>
        Log(LogLevel.Warning, source, message);

    public void Error(string source, string message) =>
        Log(LogLevel.Error, source, message);

    public void Error(string source, string message, Exception ex) =>
        ForContext("Source", source).Error(ex, message);

    public void Fatal(string source, string message) =>
        Log(LogLevel.Fatal, source, message);

    public bool Safe(Action action, string source, string errorMessage)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception ex)
        {
            Error(source, errorMessage, ex);
            return false;
        }
    }

    public async Task<bool> SafeAsync(Func<Task> asyncAction, string source, string errorMessage)
    {
        try
        {
            await asyncAction();
            return true;
        }
        catch (Exception ex)
        {
            Error(source, errorMessage, ex);
            return false;
        }
    }

    public T SafeResult<T>(Func<T> func, T defaultValue, string source, string errorMessage)
    {
        try
        {
            return func();
        }
        catch (Exception ex)
        {
            Error(source, errorMessage, ex);
            return defaultValue;
        }
    }

    #endregion

    #region Private Implementation Methods

    private static void ConfigureLogger()
    {
        Serilog.Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .Enrich.With(new ThreadEnricher())
            .WriteTo.File(
                Path.Combine(LogDirectoryPath, LatestLogFileName),
                fileSizeLimitBytes: MaxFileSizeMB * 1024 * 1024,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: RetainedFileCount,
                outputTemplate: OutputTemplate,
                rollingInterval: RollingInterval.Infinite
            )
            .CreateLogger();
    }

    private static void LogStartupInfo()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";
        _instance.Info(nameof(SmartLogger), $"Application 'SpectrumNet' version '{version}' started");
    }

    private static LogEventLevel ToSerilogLevel(LogLevel level) =>
        level switch
        {
            LogLevel.Trace => LogEventLevel.Verbose,
            LogLevel.Debug => LogEventLevel.Debug,
            LogLevel.Information => LogEventLevel.Information,
            LogLevel.Warning => LogEventLevel.Warning,
            LogLevel.Error => LogEventLevel.Error,
            LogLevel.Fatal => LogEventLevel.Fatal,
            _ => LogEventLevel.Information
        };

    private static void EnsureLogDirectoryExists()
    {
        if (!Directory.Exists(LogDirectoryPath))
            Directory.CreateDirectory(LogDirectoryPath);
    }

    private static void TryDeleteLatestLog()
    {
        var latestLogPath = Path.Combine(LogDirectoryPath, LatestLogFileName);
        if (File.Exists(latestLogPath))
            File.Delete(latestLogPath);
    }

    private class ThreadEnricher : ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            var threadId = CurrentManagedThreadId;
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("ThreadId", threadId));
        }
    }

    #endregion
}