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
        const string LogDirectoryPath = "logs",
                     LatestLogFileName = "latest.log",
                     ApplicationName = "SpectrumNet",
                     OutputTemplate = "{Timestamp:HH:mm:ss} [{Level:u3}] [Thread:{ThreadId}] {Message:lj}{NewLine}{Exception}";
        const int MaxFileSizeMB = 5, RetainedFileCount = 10;
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

        public static void Error(string source, string message, Exception ex) =>
            Serilog.Log.Error(ex, $"{source} {message}");

        public static void Fatal(string source, string message, Exception ex) =>
            Serilog.Log.Fatal(ex, $"{source} {message}");

        public static void Log(LogLevel level, string source, string message, bool forceLog = false)
        {
            string formattedMessage = $"[{source}] {message}";

            switch (level)
            {
                case LogLevel.Trace: Serilog.Log.Verbose(formattedMessage); break;
                case LogLevel.Debug: Serilog.Log.Debug(formattedMessage); break;
                case LogLevel.Information: Serilog.Log.Information(formattedMessage); break;
                case LogLevel.Warning: Serilog.Log.Warning(formattedMessage); break;
                case LogLevel.Error: Serilog.Log.Error(formattedMessage); break;
                case LogLevel.Fatal: Serilog.Log.Fatal(formattedMessage); break;
            }
        }

        #endregion

        #region Improved API for Error Handling

        /// <summary>
        /// Опции для настройки обработки ошибок
        /// </summary>
        public class ErrorHandlingOptions
        {
            /// <summary>
            /// Уровень логирования для ошибок
            /// </summary>
            public LogLevel LogLevel { get; set; } = LogLevel.Error;

            /// <summary>
            /// Сообщение об ошибке
            /// </summary>
            public string? ErrorMessage { get; set; }

            /// <summary>
            /// Источник лога (если null, будет использовано имя вызывающего метода)
            /// </summary>
            public string? Source { get; set; }

            /// <summary>
            /// Включить расширенную диагностическую информацию
            /// </summary>
            public bool EnableDiagnostics { get; set; } = false;

            /// <summary>
            /// Функция для обработки исключений
            /// </summary>
            public Action<Exception>? ExceptionHandler { get; set; }

            /// <summary>
            /// Должен ли метод повторить попытку в случае ошибки
            /// </summary>
            public bool Retry { get; set; } = false;

            /// <summary>
            /// Количество повторных попыток
            /// </summary>
            public int RetryCount { get; set; } = 1;

            /// <summary>
            /// Задержка между повторными попытками в миллисекундах
            /// </summary>
            public int RetryDelayMs { get; set; } = 100;

            /// <summary>
            /// Типы исключений, которые следует игнорировать (не логировать)
            /// </summary>
            public Type[]? IgnoreExceptions { get; set; }
        }

        /// <summary>
        /// Результат выполнения безопасной операции
        /// </summary>
        /// <typeparam name="T">Тип результата</typeparam>
        public class OperationResult<T>
        {
            /// <summary>
            /// Успешность выполнения операции
            /// </summary>
            public bool Success { get; set; }

            /// <summary>
            /// Результат операции (если операция успешна)
            /// </summary>
            public T? Result { get; set; }

            /// <summary>
            /// Исключение, возникшее при выполнении операции (если операция не успешна)
            /// </summary>
            public Exception? Exception { get; set; }

            /// <summary>
            /// Затраченное время на выполнение операции в миллисекундах
            /// </summary>
            public long ElapsedMilliseconds { get; set; }

            /// <summary>
            /// Количество выполненных попыток
            /// </summary>
            public int Attempts { get; set; } = 1;

            /// <summary>
            /// Диагностическая информация
            /// </summary>
            public Dictionary<string, object>? Diagnostics { get; set; }
        }

        /// <summary>
        /// Безопасно выполняет указанное действие, логируя любые исключения
        /// </summary>
        /// <param name="action">Действие для выполнения</param>
        /// <param name="options">Опции обработки ошибок</param>
        /// <returns>Результат операции</returns>
        public static OperationResult<bool> Safe(Action action, ErrorHandlingOptions? options = null)
        {
            options ??= new ErrorHandlingOptions();
            var stopwatch = new System.Diagnostics.Stopwatch();
            var result = new OperationResult<bool> { Success = false, Result = false };

            if (options.EnableDiagnostics)
            {
                result.Diagnostics = new Dictionary<string, object>
                {
                    ["StartTime"] = DateTime.Now,
                    ["ThreadId"] = Thread.CurrentThread.ManagedThreadId
                };
            }

            options.Source ??= GetCallerInfo();
            options.ErrorMessage ??= "Operation failed";

            for (int attempt = 0; attempt <= (options.Retry ? options.RetryCount : 0); attempt++)
            {
                if (attempt > 0)
                {
                    Log(LogLevel.Debug, options.Source, $"Retry attempt {attempt} of {options.RetryCount}");
                    Task.Delay(options.RetryDelayMs).Wait();
                }

                result.Attempts = attempt + 1;
                stopwatch.Restart();

                try
                {
                    action();
                    stopwatch.Stop();
                    result.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
                    result.Success = true;
                    result.Result = true;

                    if (options.EnableDiagnostics && result.Diagnostics != null)
                    {
                        result.Diagnostics["Succeeded"] = true;
                        result.Diagnostics["EndTime"] = DateTime.Now;
                    }

                    return result;
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    result.ElapsedMilliseconds += stopwatch.ElapsedMilliseconds;
                    result.Exception = ex;

                    bool shouldIgnore = ShouldIgnoreException(ex, options.IgnoreExceptions);

                    if (!shouldIgnore && attempt == (options.Retry ? options.RetryCount : 0))
                    {
                        LogException(ex, options);
                        options.ExceptionHandler?.Invoke(ex);
                    }

                    if (options.EnableDiagnostics && result.Diagnostics != null)
                    {
                        result.Diagnostics["Exception"] = ex.ToString();
                        result.Diagnostics["ExceptionType"] = ex.GetType().Name;
                        result.Diagnostics["StackTrace"] = ex.StackTrace;
                        result.Diagnostics["Ignored"] = shouldIgnore;
                    }

                    if (attempt == (options.Retry ? options.RetryCount : 0))
                        return result;
                }
            }

            return result;
        }

        /// <summary>
        /// Безопасно выполняет указанную функцию, логируя любые исключения
        /// </summary>
        /// <typeparam name="T">Тип возвращаемого значения</typeparam>
        /// <param name="func">Функция для выполнения</param>
        /// <param name="defaultValue">Значение по умолчанию в случае ошибки</param>
        /// <param name="options">Опции обработки ошибок</param>
        /// <returns>Результат операции</returns>
        public static OperationResult<T> Safe<T>(Func<T> func, T defaultValue = default!, ErrorHandlingOptions? options = null)
        {
            options ??= new ErrorHandlingOptions();
            var stopwatch = new System.Diagnostics.Stopwatch();
            var result = new OperationResult<T> { Success = false, Result = defaultValue };

            if (options.EnableDiagnostics)
            {
                result.Diagnostics = new Dictionary<string, object>
                {
                    ["StartTime"] = DateTime.Now,
                    ["ThreadId"] = Thread.CurrentThread.ManagedThreadId
                };
            }

            options.Source ??= GetCallerInfo();
            options.ErrorMessage ??= "Operation failed";

            for (int attempt = 0; attempt <= (options.Retry ? options.RetryCount : 0); attempt++)
            {
                if (attempt > 0)
                {
                    Log(LogLevel.Debug, options.Source, $"Retry attempt {attempt} of {options.RetryCount}");
                    Task.Delay(options.RetryDelayMs).Wait();
                }

                result.Attempts = attempt + 1;
                stopwatch.Restart();

                try
                {
                    var funcResult = func();
                    stopwatch.Stop();
                    result.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
                    result.Success = true;
                    result.Result = funcResult;

                    if (options.EnableDiagnostics && result.Diagnostics != null)
                    {
                        result.Diagnostics["Succeeded"] = true;
                        result.Diagnostics["EndTime"] = DateTime.Now;
                        result.Diagnostics["ResultType"] = funcResult?.GetType().Name ?? "null";
                    }

                    return result;
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    result.ElapsedMilliseconds += stopwatch.ElapsedMilliseconds;
                    result.Exception = ex;

                    bool shouldIgnore = ShouldIgnoreException(ex, options.IgnoreExceptions);

                    if (!shouldIgnore && attempt == (options.Retry ? options.RetryCount : 0))
                    {
                        LogException(ex, options);
                        options.ExceptionHandler?.Invoke(ex);
                    }

                    if (options.EnableDiagnostics && result.Diagnostics != null)
                    {
                        result.Diagnostics["Exception"] = ex.ToString();
                        result.Diagnostics["ExceptionType"] = ex.GetType().Name;
                        result.Diagnostics["StackTrace"] = ex.StackTrace;
                        result.Diagnostics["Ignored"] = shouldIgnore;
                    }

                    if (attempt == (options.Retry ? options.RetryCount : 0))
                        return result;
                }
            }

            return result;
        }

        /// <summary>
        /// Безопасно выполняет указанное асинхронное действие, логируя любые исключения
        /// </summary>
        /// <param name="action">Асинхронное действие для выполнения</param>
        /// <param name="options">Опции обработки ошибок</param>
        /// <returns>Результат операции</returns>
        public static async Task<OperationResult<bool>> SafeAsync(Func<Task> action, ErrorHandlingOptions? options = null)
        {
            options ??= new ErrorHandlingOptions();
            var stopwatch = new System.Diagnostics.Stopwatch();
            var result = new OperationResult<bool> { Success = false, Result = false };

            if (options.EnableDiagnostics)
            {
                result.Diagnostics = new Dictionary<string, object>
                {
                    ["StartTime"] = DateTime.Now,
                    ["ThreadId"] = Thread.CurrentThread.ManagedThreadId
                };
            }

            options.Source ??= GetCallerInfo();
            options.ErrorMessage ??= "Async operation failed";

            for (int attempt = 0; attempt <= (options.Retry ? options.RetryCount : 0); attempt++)
            {
                if (attempt > 0)
                {
                    Log(LogLevel.Debug, options.Source, $"Retry attempt {attempt} of {options.RetryCount}");
                    await Task.Delay(options.RetryDelayMs);
                }

                result.Attempts = attempt + 1;
                stopwatch.Restart();

                try
                {
                    await action();
                    stopwatch.Stop();
                    result.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
                    result.Success = true;
                    result.Result = true;

                    if (options.EnableDiagnostics && result.Diagnostics != null)
                    {
                        result.Diagnostics["Succeeded"] = true;
                        result.Diagnostics["EndTime"] = DateTime.Now;
                    }

                    return result;
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    result.ElapsedMilliseconds += stopwatch.ElapsedMilliseconds;
                    result.Exception = ex;

                    bool shouldIgnore = ShouldIgnoreException(ex, options.IgnoreExceptions);

                    if (!shouldIgnore && attempt == (options.Retry ? options.RetryCount : 0))
                    {
                        LogException(ex, options);
                        options.ExceptionHandler?.Invoke(ex);
                    }

                    if (options.EnableDiagnostics && result.Diagnostics != null)
                    {
                        result.Diagnostics["Exception"] = ex.ToString();
                        result.Diagnostics["ExceptionType"] = ex.GetType().Name;
                        result.Diagnostics["StackTrace"] = ex.StackTrace;
                        result.Diagnostics["Ignored"] = shouldIgnore;
                    }

                    if (attempt == (options.Retry ? options.RetryCount : 0))
                        return result;
                }
            }

            return result;
        }

        /// <summary>
        /// Безопасно выполняет указанную асинхронную функцию, логируя любые исключения
        /// </summary>
        /// <typeparam name="T">Тип возвращаемого значения</typeparam>
        /// <param name="func">Асинхронная функция для выполнения</param>
        /// <param name="defaultValue">Значение по умолчанию в случае ошибки</param>
        /// <param name="options">Опции обработки ошибок</param>
        /// <returns>Результат операции</returns>
        public static async Task<OperationResult<T>> SafeAsync<T>(Func<Task<T>> func, T defaultValue = default!, ErrorHandlingOptions? options = null)
        {
            options ??= new ErrorHandlingOptions();
            var stopwatch = new System.Diagnostics.Stopwatch();
            var result = new OperationResult<T> { Success = false, Result = defaultValue };

            if (options.EnableDiagnostics)
            {
                result.Diagnostics = new Dictionary<string, object>
                {
                    ["StartTime"] = DateTime.Now,
                    ["ThreadId"] = Thread.CurrentThread.ManagedThreadId
                };
            }

            options.Source ??= GetCallerInfo();
            options.ErrorMessage ??= "Async operation failed";

            for (int attempt = 0; attempt <= (options.Retry ? options.RetryCount : 0); attempt++)
            {
                if (attempt > 0)
                {
                    Log(LogLevel.Debug, options.Source, $"Retry attempt {attempt} of {options.RetryCount}");
                    await Task.Delay(options.RetryDelayMs);
                }

                result.Attempts = attempt + 1;
                stopwatch.Restart();

                try
                {
                    var funcResult = await func();
                    stopwatch.Stop();
                    result.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
                    result.Success = true;
                    result.Result = funcResult;

                    if (options.EnableDiagnostics && result.Diagnostics != null)
                    {
                        result.Diagnostics["Succeeded"] = true;
                        result.Diagnostics["EndTime"] = DateTime.Now;
                        result.Diagnostics["ResultType"] = funcResult?.GetType().Name ?? "null";
                    }

                    return result;
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    result.ElapsedMilliseconds += stopwatch.ElapsedMilliseconds;
                    result.Exception = ex;

                    bool shouldIgnore = ShouldIgnoreException(ex, options.IgnoreExceptions);

                    if (!shouldIgnore && attempt == (options.Retry ? options.RetryCount : 0))
                    {
                        LogException(ex, options);
                        options.ExceptionHandler?.Invoke(ex);
                    }

                    if (options.EnableDiagnostics && result.Diagnostics != null)
                    {
                        result.Diagnostics["Exception"] = ex.ToString();
                        result.Diagnostics["ExceptionType"] = ex.GetType().Name;
                        result.Diagnostics["StackTrace"] = ex.StackTrace;
                        result.Diagnostics["Ignored"] = shouldIgnore;
                    }

                    if (attempt == (options.Retry ? options.RetryCount : 0))
                        return result;
                }
            }

            return result;
        }

        /// <summary>
        /// Безопасно освобождает ресурс, реализующий IDisposable
        /// </summary>
        /// <param name="resource">Ресурс для освобождения</param>
        /// <param name="resourceName">Название ресурса для логирования</param>
        /// <param name="options">Опции обработки ошибок</param>
        public static OperationResult<bool> SafeDispose(IDisposable? resource, string resourceName, ErrorHandlingOptions? options = null)
        {
            if (resource == null)
                return new OperationResult<bool> { Success = true, Result = true, ElapsedMilliseconds = 0 };

            options ??= new ErrorHandlingOptions();
            options.Source ??= GetCallerInfo();
            options.ErrorMessage ??= $"Error disposing {resourceName}";

            return Safe(() => resource.Dispose(), options);
        }

        /// <summary>
        /// Получает информацию о вызывающем методе
        /// </summary>
        /// <param name="skipFrames">Количество пропускаемых фреймов стека (по умолчанию 2)</param>
        /// <returns>Строка с информацией о вызывающем методе</returns>
        private static string GetCallerInfo(int skipFrames = 2)
        {
            var stackFrame = new System.Diagnostics.StackFrame(skipFrames, true);
            var method = stackFrame.GetMethod();
            if (method == null) return "Unknown";

            var className = method.DeclaringType?.Name ?? "Unknown";
            var methodName = method.Name;

            var fileName = stackFrame.GetFileName();
            var lineNumber = stackFrame.GetFileLineNumber();

            return (!string.IsNullOrEmpty(fileName) && lineNumber > 0)
                ? $"{className}.{methodName} ({Path.GetFileName(fileName)}:{lineNumber})"
                : $"{className}.{methodName}";
        }

        /// <summary>
        /// Определяет, нужно ли игнорировать исключение
        /// </summary>
        /// <param name="exception">Исключение</param>
        /// <param name="ignoreExceptionTypes">Типы исключений, которые следует игнорировать</param>
        /// <returns>true, если исключение следует игнорировать</returns>
        private static bool ShouldIgnoreException(Exception exception, Type[]? ignoreExceptionTypes) =>
            ignoreExceptionTypes?.Any(type => type.IsAssignableFrom(exception.GetType())) ?? false;

        /// <summary>
        /// Логирует исключение в соответствии с настройками
        /// </summary>
        /// <param name="ex">Исключение</param>
        /// <param name="options">Опции обработки ошибок</param>
        private static void LogException(Exception ex, ErrorHandlingOptions options)
        {
            string formattedMessage = $"[{options.Source}] {options.ErrorMessage}";

            switch (options.LogLevel)
            {
                case LogLevel.Fatal: Serilog.Log.Fatal(ex, formattedMessage); break;
                case LogLevel.Error: Serilog.Log.Error(ex, formattedMessage); break;
                case LogLevel.Warning: Serilog.Log.Warning(ex, formattedMessage); break;
                case LogLevel.Information: Serilog.Log.Information(ex, formattedMessage); break;
                case LogLevel.Debug: Serilog.Log.Debug(ex, formattedMessage); break;
                case LogLevel.Trace: Serilog.Log.Verbose(ex, formattedMessage); break;
            }
        }

        /// <summary>
        /// Создает предварительно настроенные опции обработки ошибок
        /// </summary>
        /// <returns>Предварительно настроенные опции обработки ошибок</returns>
        public static ErrorHandlingOptions CreateOptions() =>
            new() { Source = GetCallerInfo(2) };

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