#nullable enable
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

    public static class SmartLogger
    {
        private const string LogDirectoryPath = "logs",
                             LatestLogFileName = "latest.log",
                             ApplicationName = "SpectrumNet",
                             OutputTemplate = "{Timestamp:HH:mm:ss} [{Level:u3}] [Thread:{ThreadId}] {Message:lj}{NewLine}{Exception}";
        private const int MaxFileSizeMB = 5, RetainedFileCount = 10;
        private static readonly string ApplicationVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";
        private static ILoggerFactory _loggerFactory = null!;

        public static ILoggerFactory LoggerFactory => _loggerFactory;

        public static void Initialize() =>
            Safe(() =>
            {
                InitializeLogging();
                _loggerFactory = new LoggerFactory().AddSerilog();
                var logger = _loggerFactory.CreateLogger(typeof(SmartLogger));
                logger.LogInformation("Application '{Application}' version '{Version}' started", ApplicationName, ApplicationVersion);
            }, new ErrorHandlingOptions
            {
                Source = nameof(SmartLogger),
                ErrorMessage = "Error initializing logging",
                LogLevel = LogLevel.Fatal
            });

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

        #region Error Handling API

        public class ErrorHandlingOptions
        {
            public LogLevel LogLevel { get; set; } = LogLevel.Error;
            public string? ErrorMessage { get; set; }
            public string? Source { get; set; }
            public bool EnableDiagnostics { get; set; } = false;
            public Action<Exception>? ExceptionHandler { get; set; }
            public bool Retry { get; set; } = false;
            public int RetryCount { get; set; } = 1;
            public int RetryDelayMs { get; set; } = 100;
            public Type[]? IgnoreExceptions { get; set; }
        }

        public class OperationResult<T>
        {
            public bool Success { get; set; }
            public T? Result { get; set; }
            public Exception? Exception { get; set; }
            public long ElapsedMilliseconds { get; set; }
            public int Attempts { get; set; } = 1;
            public Dictionary<string, object>? Diagnostics { get; set; }
        }

        // Синхронные методы
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
                        result.Diagnostics["StackTrace"] = ex.StackTrace ?? string.Empty;
                        result.Diagnostics["Ignored"] = shouldIgnore;
                    }

                    if (attempt == (options.Retry ? options.RetryCount : 0))
                        return result;
                }
            }

            return result;
        }

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
                        result.Diagnostics["StackTrace"] = ex.StackTrace ?? string.Empty;
                        result.Diagnostics["Ignored"] = shouldIgnore;
                    }

                    if (attempt == (options.Retry ? options.RetryCount : 0))
                        return result;
                }
            }

            return result;
        }

        public static async Task<OperationResult<bool>> SafeAsync(Func<Task> asyncAction, ErrorHandlingOptions? options = null)
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
                    await asyncAction();
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
                        result.Diagnostics["StackTrace"] = ex.StackTrace ?? string.Empty;
                        result.Diagnostics["Ignored"] = shouldIgnore;
                    }

                    if (attempt == (options.Retry ? options.RetryCount : 0))
                        return result;
                }
            }

            return result;
        }

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
                        result.Diagnostics["StackTrace"] = ex.StackTrace ?? string.Empty;
                        result.Diagnostics["Ignored"] = shouldIgnore;
                    }

                    if (attempt == (options.Retry ? options.RetryCount : 0))
                        return result;
                }
            }

            return result;
        }

        public static async Task<OperationResult<bool>> SafeValueTaskAsync(Func<ValueTask> asyncAction, ErrorHandlingOptions? options = null)
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
            options.ErrorMessage ??= "ValueTask operation failed";

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
                    await asyncAction();
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
                        result.Diagnostics["StackTrace"] = ex.StackTrace ?? string.Empty;
                        result.Diagnostics["Ignored"] = shouldIgnore;
                    }

                    if (attempt == (options.Retry ? options.RetryCount : 0))
                        return result;
                }
            }

            return result;
        }

        public static async Task<OperationResult<T>> SafeValueTaskAsync<T>(Func<ValueTask<T>> func, T defaultValue = default!, ErrorHandlingOptions? options = null)
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
            options.ErrorMessage ??= "ValueTask<T> operation failed";

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
                        result.Diagnostics["StackTrace"] = ex.StackTrace ?? string.Empty;
                        result.Diagnostics["Ignored"] = shouldIgnore;
                    }

                    if (attempt == (options.Retry ? options.RetryCount : 0))
                        return result;
                }
            }

            return result;
        }

        // Методы для безопасного освобождения ресурсов
        public static OperationResult<bool> SafeDispose(IDisposable? resource, string resourceName, ErrorHandlingOptions? options = null)
        {
            if (resource == null)
                return new OperationResult<bool> { Success = true, Result = true };

            options ??= new ErrorHandlingOptions();
            options.Source ??= GetCallerInfo();
            options.ErrorMessage ??= $"Error disposing {resourceName}";

            return Safe(() => resource.Dispose(), options);
        }

        public static async Task<OperationResult<bool>> SafeDisposeAsync(IAsyncDisposable? resource, string resourceName, ErrorHandlingOptions? options = null)
        {
            if (resource == null)
                return new OperationResult<bool> { Success = true, Result = true };

            options ??= new ErrorHandlingOptions();
            options.Source ??= GetCallerInfo();
            options.ErrorMessage ??= $"Error disposing {resourceName} asynchronously";

            return await SafeValueTaskAsync(async () => await resource.DisposeAsync(), options);
        }

        public static T SafeResult<T>(Func<T> func, T defaultValue = default!, ErrorHandlingOptions? options = null)
        {
            return Safe(func, defaultValue, options).Result;
        }

        public static async Task<T> SafeResultAsync<T>(Func<Task<T>> func, T defaultValue = default!, ErrorHandlingOptions? options = null)
        {
            return (await SafeAsync(func, defaultValue, options)).Result;
        }

        public static async Task<T> SafeValueTaskResultAsync<T>(Func<ValueTask<T>> func, T defaultValue = default!, ErrorHandlingOptions? options = null)
        {
            return (await SafeValueTaskAsync(func, defaultValue, options)).Result;
        }

        public static void SafeExecute(Action action, ErrorHandlingOptions? options = null)
        {
            Safe(action, options);
        }

        public static async Task SafeExecuteAsync(Func<Task> action, ErrorHandlingOptions? options = null)
        {
            await SafeAsync(action, options);
        }

        public static async Task SafeExecuteValueTaskAsync(Func<ValueTask> action, ErrorHandlingOptions? options = null)
        {
            await SafeValueTaskAsync(action, options);
        }

        #endregion

        #region Utility Methods

        public static ErrorHandlingOptions CreateOptions() =>
            new() { Source = GetCallerInfo(2) };

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

        private static bool ShouldIgnoreException(Exception exception, Type[]? ignoreExceptionTypes) =>
            ignoreExceptionTypes?.Any(type => type.IsAssignableFrom(exception.GetType())) ?? false;

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

        #endregion

        #region Application Lifecycle Methods

        public static void Shutdown(int exitCode) =>
            Safe(() =>
            {
                Log(LogLevel.Information, "App",
                    $"Application '{ApplicationName}' version '{ApplicationVersion}' closed with exit code: {exitCode}");
                Serilog.Log.CloseAndFlush();
            }, new ErrorHandlingOptions
            {
                Source = nameof(SmartLogger),
                ErrorMessage = "Error during shutdown",
                LogLevel = LogLevel.Fatal
            });

        public static void ReconfigureLogging() =>
            Safe(() =>
            {
                Log(LogLevel.Information, "App", "Reconfiguring logging settings");
                Serilog.Log.CloseAndFlush();

                InitializeLogging();

                _loggerFactory = new LoggerFactory().AddSerilog();
                Log(LogLevel.Information, "App", "Logging reconfigured successfully");
            }, new ErrorHandlingOptions
            {
                Source = nameof(SmartLogger),
                ErrorMessage = "Error reconfiguring logging",
                LogLevel = LogLevel.Fatal
            });

        private static void InitializeLogging() =>
            Safe(() =>
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
            }, new ErrorHandlingOptions
            {
                Source = nameof(SmartLogger),
                ErrorMessage = "Error initializing logging configuration",
                LogLevel = LogLevel.Fatal
            });

        private static void EnsureLogDirectoryExists() =>
            Safe(() =>
            {
                if (!Directory.Exists(LogDirectoryPath))
                    Directory.CreateDirectory(LogDirectoryPath);
            }, new ErrorHandlingOptions
            {
                Source = nameof(SmartLogger),
                ErrorMessage = "Error creating log directory",
                LogLevel = LogLevel.Fatal
            });

        private static void DeleteLatestLogIfExists() =>
            Safe(() =>
            {
                var latestLogPath = Path.Combine(LogDirectoryPath, LatestLogFileName);
                if (File.Exists(latestLogPath))
                    File.Delete(latestLogPath);
            }, new ErrorHandlingOptions
            {
                Source = nameof(SmartLogger),
                ErrorMessage = "Error deleting latest log, creating backup",
                LogLevel = LogLevel.Warning,
                ExceptionHandler = ex =>
                {
                    Safe(() =>
                    {
                        var latestLogPath = Path.Combine(LogDirectoryPath, LatestLogFileName);
                        var backupPath = Path.Combine(LogDirectoryPath, $"latest_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                        File.Move(latestLogPath, backupPath);
                    }, new ErrorHandlingOptions
                    {
                        Source = nameof(SmartLogger),
                        ErrorMessage = "Error creating log backup",
                        LogLevel = LogLevel.Error
                    });
                }
            });

        #endregion
    }

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

        public static async Task<T> WithAsync<T>(this T obj, Func<T, Task> action)
        {
            await action(obj);
            return obj;
        }

        public static bool IsOneOf<T>(this T value, params T[] values) =>
            values.Contains(value);

        public static T? AsOrNull<T>(this object obj) where T : class =>
            obj as T;
    }
}