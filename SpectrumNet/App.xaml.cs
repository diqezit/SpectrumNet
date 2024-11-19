using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using System.IO;
using System.Reflection;
using System.Windows;

#nullable enable

namespace SpectrumNet
{
    public partial class App : Application
    {
        private const string LogDirectoryPath = "logs";
        private const string LatestLogFileName = "latest.log";
        private const string MainLogFilePattern = "SpectrumNet_{0:yyyyMMdd_HHmmss}.log";
        private const int MaxFileSizeMB = 5;
        private const int RetainedFileCount = 10;
        private const string OutputTemplate = "{Timestamp:HH:mm:ss} [{Level:u3}] [Thread:{ThreadId}] {Message:lj}{NewLine}{Exception}";

        private static readonly string ApplicationVersion =
            Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";

        private static ILoggerFactory _loggerFactory = null!;
        public static ILoggerFactory LoggerFactory => _loggerFactory;

        protected override void OnStartup(StartupEventArgs e)
        {
            try
            {
                InitializeLogging();
                _loggerFactory = new LoggerFactory();
                _loggerFactory.AddSerilog(); // Добавляем Serilog как провайдер логгирования

                var logger = _loggerFactory.CreateLogger<App>();
                logger.LogInformation("Application '{Application}' version '{Version}' started", "SpectrumNet", ApplicationVersion);

                AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                {
                    logger.LogCritical(args.ExceptionObject as Exception, "Unhandled exception in application");
                    FlushToMainLog();
                };

                base.OnStartup(e);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Error initializing logging");
                Shutdown(-1);
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

            var mainLogFileName = string.Format(MainLogFilePattern, DateTime.Now);
            loggerConfig.WriteTo.File(
                Path.Combine(LogDirectoryPath, mainLogFileName),
                outputTemplate: OutputTemplate,
                fileSizeLimitBytes: MaxFileSizeMB * 1024 * 1024,
                retainedFileCountLimit: RetainedFileCount,
                buffered: false,
                flushToDiskInterval: TimeSpan.FromSeconds(1));

            loggerConfig.WriteTo.File(
                Path.Combine(LogDirectoryPath, LatestLogFileName),
                outputTemplate: OutputTemplate,
                fileSizeLimitBytes: MaxFileSizeMB * 1024 * 1024,
                buffered: false,
                flushToDiskInterval: TimeSpan.FromSeconds(1));
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

        private void FlushToMainLog()
        {
            int retryCount = 5;
            int delayInMilliseconds = 1000;
            string filePath = Path.Combine(LogDirectoryPath, LatestLogFileName);

            for (int i = 0; i < retryCount; i++)
            {
                try
                {
                    using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var reader = new StreamReader(fileStream))
                    {
                        string logContent = reader.ReadToEnd();
                        break;
                    }
                }
                catch (IOException ex)
                {
                    Log.Error(ex, $"Error transferring contents of {filePath}. Attempt {i + 1} of {retryCount}.");
                    if (i == retryCount - 1)
                    {
                        Log.Error("Maximum number of attempts to access file {filePath} exceeded.");
                    }
                    else
                    {
                        Thread.Sleep(delayInMilliseconds);
                    }
                }
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                Log.Information("Application '{Application}' version '{Version}' closed", "SpectrumNet", ApplicationVersion);
                FlushToMainLog();
            }
            finally
            {
                Log.CloseAndFlush();
                base.OnExit(e);
            }
        }
    }
}