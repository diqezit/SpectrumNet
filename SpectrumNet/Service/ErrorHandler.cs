namespace SpectrumNet
{
    /// <summary>
    /// Уровни логирования
    /// </summary>
    public enum LogLevel
    {
        Trace,
        Debug,
        Information,
        Warning,
        Error,
        Fatal
    }

    public class SmartLogger
    {
        // Константы перенесены из App
        const string LogDirectoryPath = "logs";
        const string LatestLogFileName = "latest.log";
        const int MaxFileSizeMB = 5, RetainedFileCount = 10;
        const string ApplicationName = "SpectrumNet";
        const string OutputTemplate = "{Timestamp:HH:mm:ss} [{Level:u3}] [Thread:{ThreadId}] {Message:lj}{NewLine}{Exception}";
        static readonly string ApplicationVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";
        static ILoggerFactory _loggerFactory = null!;

        public static ILoggerFactory LoggerFactory => _loggerFactory;

        public static void Initialize()
        {
            try
            {
                InitializeLogging();
                _loggerFactory = new LoggerFactory().AddSerilog();
                var logger = _loggerFactory.CreateLogger(typeof(SmartLogger));
                logger.LogInformation("Application '{Application}' version '{Version}' started", ApplicationName, ApplicationVersion);
            }
            catch (Exception ex)
            {
                Serilog.Log.Fatal(ex, "Error initializing logging");
                throw;
            }
        }

        #region Logging Methods

        public static void Trace(string source, string message) => Log(LogLevel.Trace, source, message);
        public static void Debug(string source, string message) => Log(LogLevel.Debug, source, message);
        public static void Info(string source, string message) => Log(LogLevel.Information, source, message);
        public static void Warning(string source, string message) => Log(LogLevel.Warning, source, message);
        public static void Error(string source, string message) => Log(LogLevel.Error, source, message);
        public static void Fatal(string source, string message) => Log(LogLevel.Fatal, source, message);

        public static void Error(string source, string message, Exception ex)
        {
            string formattedMessage = $"{source} {message}";
            Serilog.Log.Error(ex, formattedMessage);
        }

        public static void Fatal(string source, string message, Exception ex)
        {
            string formattedMessage = $"{source} {message}";
            Serilog.Log.Fatal(ex, formattedMessage);
        }

        public static void Log(LogLevel level, string source, string message, bool forceLog = false)
        {
            // Записываем все сообщения без игнорирования
            string formattedMessage = $"[{source}] {message}";

            switch (level)
            {
                case LogLevel.Trace:
                    Serilog.Log.Verbose(formattedMessage);
                    break;
                case LogLevel.Debug:
                    Serilog.Log.Debug(formattedMessage);
                    break;
                case LogLevel.Information:
                    Serilog.Log.Information(formattedMessage);
                    break;
                case LogLevel.Warning:
                    Serilog.Log.Warning(formattedMessage);
                    break;
                case LogLevel.Error:
                    Serilog.Log.Error(formattedMessage);
                    break;
                case LogLevel.Fatal:
                    Serilog.Log.Fatal(formattedMessage);
                    break;
            }
        }

        #endregion

        #region Simplified API for Error Handling

        /// <summary>
        /// Безопасно выполняет указанное действие, логируя любые исключения
        /// </summary>
        /// <param name="action">Действие для выполнения</param>
        /// <param name="source">Источник лога (по умолчанию имя вызывающего метода)</param>
        /// <param name="errorMessage">Сообщение об ошибке (по умолчанию "Operation failed")</param>
        /// <param name="logLevel">Уровень логирования для ошибок (по умолчанию Error)</param>
        /// <returns>true если операция выполнена успешно, false в противном случае</returns>
        public static bool Safe(Action action, string? source = null, string? errorMessage = null, LogLevel logLevel = LogLevel.Error)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception ex)
            {
                source ??= GetCallerName();
                errorMessage ??= "Operation failed";

                if (logLevel == LogLevel.Error || logLevel == LogLevel.Fatal)
                {
                    string formattedMessage = $"[{source}] {errorMessage}";
                    Serilog.Log.Error(ex, formattedMessage);
                }
                else
                {
                    Log(logLevel, source, $"{errorMessage}: {ex.Message}");
                }

                return false;
            }
        }

        /// <summary>
        /// Безопасно выполняет указанную функцию, логируя любые исключения
        /// </summary>
        /// <typeparam name="T">Тип возвращаемого значения</typeparam>
        /// <param name="func">Функция для выполнения</param>
        /// <param name="defaultValue">Значение по умолчанию в случае ошибки</param>
        /// <param name="source">Источник лога (по умолчанию имя вызывающего метода)</param>
        /// <param name="errorMessage">Сообщение об ошибке (по умолчанию "Operation failed")</param>
        /// <param name="logLevel">Уровень логирования для ошибок (по умолчанию Error)</param>
        /// <returns>Результат функции или значение по умолчанию в случае ошибки</returns>
        public static T Safe<T>(Func<T> func, T defaultValue = default!, string? source = null, string? errorMessage = null, LogLevel logLevel = LogLevel.Error)
        {
            try
            {
                return func();
            }
            catch (Exception ex)
            {
                source ??= GetCallerName();
                errorMessage ??= "Operation failed";

                if (logLevel == LogLevel.Error || logLevel == LogLevel.Fatal)
                {
                    string formattedMessage = $"[{source}] {errorMessage}";
                    Serilog.Log.Error(ex, formattedMessage);
                }
                else
                {
                    Log(logLevel, source, $"{errorMessage}: {ex.Message}");
                }

                return defaultValue;
            }
        }

        /// <summary>
        /// Безопасно выполняет указанное асинхронное действие, логируя любые исключения
        /// </summary>
        /// <param name="action">Асинхронное действие для выполнения</param>
        /// <param name="source">Источник лога (по умолчанию имя вызывающего метода)</param>
        /// <param name="errorMessage">Сообщение об ошибке (по умолчанию "Async operation failed")</param>
        /// <param name="logLevel">Уровень логирования для ошибок (по умолчанию Error)</param>
        /// <returns>true если операция выполнена успешно, false в противном случае</returns>
        public static async Task<bool> SafeAsync(Func<Task> action, string? source = null, string? errorMessage = null, LogLevel logLevel = LogLevel.Error)
        {
            try
            {
                await action();
                return true;
            }
            catch (Exception ex)
            {
                source ??= GetCallerName();
                errorMessage ??= "Async operation failed";

                if (logLevel == LogLevel.Error || logLevel == LogLevel.Fatal)
                {
                    string formattedMessage = $"[{source}] {errorMessage}";
                    Serilog.Log.Error(ex, formattedMessage);
                }
                else
                {
                    Log(logLevel, source, $"{errorMessage}: {ex.Message}");
                }

                return false;
            }
        }

        /// <summary>
        /// Безопасно выполняет указанную асинхронную функцию, логируя любые исключения
        /// </summary>
        /// <typeparam name="T">Тип возвращаемого значения</typeparam>
        /// <param name="func">Асинхронная функция для выполнения</param>
        /// <param name="defaultValue">Значение по умолчанию в случае ошибки</param>
        /// <param name="source">Источник лога (по умолчанию имя вызывающего метода)</param>
        /// <param name="errorMessage">Сообщение об ошибке (по умолчанию "Async operation failed")</param>
        /// <param name="logLevel">Уровень логирования для ошибок (по умолчанию Error)</param>
        /// <returns>Результат функции или значение по умолчанию в случае ошибки</returns>
        public static async Task<T> SafeAsync<T>(Func<Task<T>> func, T defaultValue = default!, string? source = null, string? errorMessage = null, LogLevel logLevel = LogLevel.Error)
        {
            try
            {
                return await func();
            }
            catch (Exception ex)
            {
                source ??= GetCallerName();
                errorMessage ??= "Async operation failed";

                if (logLevel == LogLevel.Error || logLevel == LogLevel.Fatal)
                {
                    string formattedMessage = $"[{source}] {errorMessage}";
                    Serilog.Log.Error(ex, formattedMessage);
                }
                else
                {
                    Log(logLevel, source, $"{errorMessage}: {ex.Message}");
                }

                return defaultValue;
            }
        }

        /// <summary>
        /// Безопасно освобождает ресурс, реализующий IDisposable
        /// </summary>
        /// <param name="resource">Ресурс для освобождения</param>
        /// <param name="resourceName">Название ресурса для логирования</param>
        /// <param name="source">Источник лога (по умолчанию имя вызывающего метода)</param>
        public static void SafeDispose(IDisposable? resource, string resourceName, string? source = null)
        {
            if (resource == null) return;

            try
            {
                resource.Dispose();
            }
            catch (Exception ex)
            {
                source ??= GetCallerName();
                string formattedMessage = $"[{source}] Error disposing {resourceName}";
                Serilog.Log.Error(ex, formattedMessage);
            }
        }

        private static string GetCallerName()
        {
            // Получаем имя вызывающего метода через StackTrace
            // Пропускаем первые два фрейма (текущий метод и метод Safe)
            var stackFrame = new System.Diagnostics.StackFrame(2, false);
            var method = stackFrame.GetMethod();
            if (method == null) return "Unknown";

            var className = method.DeclaringType?.Name ?? "Unknown";
            var methodName = method.Name;

            return $"{className}.{methodName}";
        }

        #endregion

        public static void Shutdown(int exitCode)
        {
            try
            {
                Log(LogLevel.Information, "App",
                    $"Application '{ApplicationName}' version '{ApplicationVersion}' closed with exit code: {exitCode}");
            }
            finally
            {
                Serilog.Log.CloseAndFlush();
            }
        }

        public static void ReconfigureLogging()
        {
            Log(LogLevel.Information, "App", "Reconfiguring logging settings");
            Serilog.Log.CloseAndFlush();

            InitializeLogging();

            _loggerFactory = new LoggerFactory().AddSerilog();
            Log(LogLevel.Information, "App", "Logging reconfigured successfully");
        }

        private static void InitializeLogging()
        {
            EnsureLogDirectoryExists();
            DeleteLatestLogIfExists();
            var loggerConfig = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .Enrich.WithProperty("Application", ApplicationName)
                .Enrich.WithProperty("Version", ApplicationVersion)
                .Enrich.WithMachineName()
                .Enrich.WithEnvironmentUserName()
                .Enrich.WithThreadId();

            loggerConfig.WriteTo.File(
                Path.Combine(LogDirectoryPath, LatestLogFileName),
                outputTemplate: OutputTemplate,
                fileSizeLimitBytes: MaxFileSizeMB * 1024 * 1024,
                retainedFileCountLimit: RetainedFileCount,
                buffered: false,
                flushToDiskInterval: TimeSpan.FromSeconds(1));

            Serilog.Log.Logger = loggerConfig.CreateLogger();
        }

        private static void EnsureLogDirectoryExists()
        {
            if (!Directory.Exists(LogDirectoryPath))
                Directory.CreateDirectory(LogDirectoryPath);
        }

        private static void DeleteLatestLogIfExists()
        {
            var latestLogPath = Path.Combine(LogDirectoryPath, LatestLogFileName);
            try
            {
                if (File.Exists(latestLogPath))
                    File.Delete(latestLogPath);
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Error deleting latest log, creating backup");
                var backupPath = Path.Combine(LogDirectoryPath, $"latest_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                File.Move(latestLogPath, backupPath);
            }
        }
    }

    /// <summary>
    /// Дополнения для компактного кода
    /// </summary>
    public static class Extensions
    {
        public static void ForEach<T>(this IEnumerable<T> source, Action<T> action)
        {
            foreach (var item in source)
                action(item);
        }

        public static T With<T>(this T obj, Action<T> action)
        {
            action(obj);
            return obj;
        }
    }
}