#nullable enable
using Serilog.Core;

namespace SpectrumNet
{
    public partial class App : Application
    {
        private const string LogDirectoryPath = "logs";
        private const string LatestLogFileName = "latest.log";
        private const int MaxFileSizeMB = 5;
        private const int RetainedFileCount = 10;
        private const string OutputTemplate = "{Timestamp:HH:mm:ss} [{Level:u3}] [Thread:{ThreadId}] {Message:lj}{NewLine}{Exception}";

        private static readonly string ApplicationVersion =
            Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";
        private static ILoggerFactory _loggerFactory = null!;

        public static ILoggerFactory LoggerFactory => _loggerFactory;

        public static int MaxMessagesPerSecond { get; set; } = 5;

        protected override void OnStartup(StartupEventArgs e)
        {
            try
            {
                InitializeLogging();
                _loggerFactory = new LoggerFactory();
                _loggerFactory.AddSerilog();

                var logger = _loggerFactory.CreateLogger<App>();
                logger.LogInformation("Application '{Application}' version '{Version}' started", "SpectrumNet", ApplicationVersion);

                AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                {
                    logger.LogCritical(args.ExceptionObject as Exception, "Unhandled exception in application");
                };

                base.OnStartup(e);
                SpectrumNet.CommonResources.InitialiseResources();
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
                Log.Information("Application '{Application}' version '{Version}' closed", "SpectrumNet", ApplicationVersion);
            }
            finally
            {
                Log.CloseAndFlush();
                base.OnExit(e);
            }
        }

        private void InitializeLogging()
        {
            EnsureLogDirectoryExists();
            DeleteLatestLogIfExists();

            var loggerConfig = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .Filter.With(new RateBasedFilter(App.MaxMessagesPerSecond))
                .Enrich.WithProperty("Application", "SpectrumNet")
                .Enrich.WithProperty("Version", ApplicationVersion)
                .Enrich.WithMachineName()
                .Enrich.WithEnvironmentUserName()
                .Enrich.WithThreadId();

            ConfigureLogOutputs(loggerConfig);
            Log.Logger = loggerConfig.CreateLogger();
        }

        private void ConfigureLogOutputs(LoggerConfiguration loggerConfig)
        {
            loggerConfig.WriteTo.Console(outputTemplate: OutputTemplate);
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
            Log.Information("Reconfiguring logging settings");

            Log.CloseAndFlush();

            var loggerConfig = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .Filter.With(new RateBasedFilter(App.MaxMessagesPerSecond))
                .Enrich.WithProperty("Application", "SpectrumNet")
                .Enrich.WithProperty("Version", ApplicationVersion)
                .Enrich.WithMachineName()
                .Enrich.WithEnvironmentUserName()
                .Enrich.WithThreadId();

            loggerConfig.WriteTo.Console(outputTemplate: OutputTemplate);
            loggerConfig.WriteTo.File(
                Path.Combine(LogDirectoryPath, LatestLogFileName),
                outputTemplate: OutputTemplate,
                fileSizeLimitBytes: MaxFileSizeMB * 1024 * 1024,
                retainedFileCountLimit: RetainedFileCount,
                buffered: false,
                flushToDiskInterval: TimeSpan.FromSeconds(1));

            Log.Logger = loggerConfig.CreateLogger();

            _loggerFactory = new LoggerFactory();
            _loggerFactory.AddSerilog();
        }

        private void EnsureLogDirectoryExists()
        {
            if (!Directory.Exists(LogDirectoryPath))
            {
                Directory.CreateDirectory(LogDirectoryPath);
            }
        }

        private void DeleteLatestLogIfExists()
        {
            var latestLogPath = Path.Combine(LogDirectoryPath, LatestLogFileName);
            try
            {
                if (File.Exists(latestLogPath))
                {
                    File.Delete(latestLogPath);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error deleting latest log, creating backup");
                var backupPath = Path.Combine(LogDirectoryPath, $"latest_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                File.Move(latestLogPath, backupPath);
            }
        }
    }

    public class RateBasedFilter : ILogEventFilter
    {
        private readonly ConcurrentDictionary<string, Queue<DateTime>> _messageTimestamps = new();
        private readonly int _maxMessagesPerSecond;
        private DateTime _lastCleanupTime = DateTime.UtcNow;

        private static readonly HashSet<string> ImportantKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "initialized", "started", "changed", "changing", "completed",
            "reset", "disposed", "closed", "registered", "pausing", "resuming"
        };

        public RateBasedFilter(int maxMessagesPerSecond)
        {
            _maxMessagesPerSecond = maxMessagesPerSecond;
        }

        public bool IsEnabled(LogEvent logEvent)
        {
            if (logEvent.Level == LogEventLevel.Error || logEvent.Level == LogEventLevel.Fatal)
                return true;

            if (logEvent.Level != LogEventLevel.Debug)
                return true;

            string message = logEvent.MessageTemplate.Text;
            if (ContainsImportantKeyword(message))
                return true;

            PeriodicCleanup();

            string source = GetSourceKey(logEvent);
            var now = DateTime.UtcNow;

            if (!_messageTimestamps.TryGetValue(source, out var timestamps))
            {
                timestamps = new Queue<DateTime>();
                _messageTimestamps[source] = timestamps;
            }

            while (timestamps.Count > 0 && (now - timestamps.Peek()).TotalSeconds > 1)
            {
                timestamps.Dequeue();
            }

            if (timestamps.Count >= _maxMessagesPerSecond)
            {
                return false;
            }

            timestamps.Enqueue(now);
            return true;
        }

        private bool ContainsImportantKeyword(string message)
        {
            foreach (var keyword in ImportantKeywords)
            {
                if (message.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private string GetSourceKey(LogEvent logEvent)
        {
            string threadId = GetThreadId(logEvent);
            string component = GetComponent(logEvent.MessageTemplate.Text);
            return $"{component}:{threadId}";
        }

        private string GetThreadId(LogEvent logEvent)
        {
            if (logEvent.Properties.TryGetValue("ThreadId", out var threadIdProp) &&
                threadIdProp is ScalarValue threadIdValue)
            {
                return threadIdValue.Value?.ToString() ?? "0";
            }
            return "0";
        }

        private string GetComponent(string message)
        {
            int start = message.IndexOf('[');
            if (start >= 0)
            {
                int end = message.IndexOf(']', start);
                if (end > start)
                {
                    return message.Substring(start + 1, end - start - 1);
                }
            }
            return "Unknown";
        }

        private void PeriodicCleanup()
        {
            var now = DateTime.UtcNow;
            if ((now - _lastCleanupTime).TotalMinutes < 5)
                return;

            _lastCleanupTime = now;

            var keysToRemove = new List<string>();
            foreach (var entry in _messageTimestamps)
            {
                if (entry.Value.Count == 0 || (now - entry.Value.Peek()).TotalSeconds > 10)
                {
                    keysToRemove.Add(entry.Key);
                }
            }

            foreach (var key in keysToRemove)
            {
                _messageTimestamps.TryRemove(key, out _);
            }
        }
    }
}