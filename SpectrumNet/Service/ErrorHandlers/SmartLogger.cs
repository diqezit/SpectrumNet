#nullable enable

namespace SpectrumNet.Service.ErrorHandlers;

public static class SmartLogger
{
    #region Constants and Fields

    private const string LogDirectoryPath = "logs";
    private const string LatestLogFileName = "latest.log";
    private const string ApplicationName = "SpectrumNet";
    private const string OutputTemplate = "{Timestamp:HH:mm:ss} [{Level:u3}] [Thread:{ThreadId}] {Message}{NewLine}{Exception}";
    private const int MaxFileSizeMB = 5;
    private const int RetainedFileCount = 10;

    private static readonly string ApplicationVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";
    private static ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;
    private static ILogger _logger = NullLogger.Instance;

    #endregion

    #region Properties

    public static ILoggerFactory LoggerFactory => _loggerFactory;

    #endregion

    #region Initialization

    public static void Initialize() =>
        Safe(() =>
        {
            var serviceCollection = new ServiceCollection();
            ConfigureLogging(serviceCollection);
            var serviceProvider = serviceCollection.BuildServiceProvider();

            _loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            _logger = _loggerFactory.CreateLogger(nameof(SmartLogger));

            _logger.LogInformation("Application '{0}' version '{1}' started", ApplicationName, ApplicationVersion);
        }, new ErrorHandlingOptions
        {
            Source = nameof(SmartLogger),
            ErrorMessage = "Error initializing logging",
            LogLevel = LogLevel.Fatal
        });

    private static void ConfigureLogging(IServiceCollection services) =>
        services.AddLogging(builder =>
        {
            EnsureLogDirectoryExists();
            DeleteLatestLogIfExists();

            builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug)
                   .AddConsole();
            builder.AddFile(Path.Combine(LogDirectoryPath, LatestLogFileName), fileOptions =>
            {
                fileOptions.FileSizeLimitBytes = MaxFileSizeMB * 1024 * 1024;
                fileOptions.MaxRollingFiles = RetainedFileCount;
                fileOptions.OutputTemplate = OutputTemplate;
            });
        });

    #endregion

    #region Logging Methods

    public static void Debug(string source, string message) => Log(LogLevel.Debug, source, message);

    public static void Error(string source, string message) => Log(LogLevel.Error, source, message);

    public static void Error(string source, string message, Exception ex) =>
        _logger.LogError(ex, "[{0}] {1}", source, message);

    public static void Fatal(string source, string message) => Log(LogLevel.Fatal, source, message);

    public static void Fatal(string source, string message, Exception ex) =>
        _logger.LogCritical(ex, "[{0}] {1}", source, message);

    public static void Info(string source, string message) => Log(LogLevel.Information, source, message);

    public static void Log(LogLevel level, string source, string message, bool forceLog = false)
    {
        var msLogLevel = ConvertLogLevel(level);
        _logger.Log(msLogLevel, "[{0}] {1}", source, message);
    }

    public static void Trace(string source, string message) => Log(LogLevel.Trace, source, message);

    public static void Warning(string source, string message) => Log(LogLevel.Warning, source, message);

    private static Microsoft.Extensions.Logging.LogLevel ConvertLogLevel(LogLevel level) => level switch
    {
        LogLevel.Trace => Microsoft.Extensions.Logging.LogLevel.Trace,
        LogLevel.Debug => Microsoft.Extensions.Logging.LogLevel.Debug,
        LogLevel.Information => Microsoft.Extensions.Logging.LogLevel.Information,
        LogLevel.Warning => Microsoft.Extensions.Logging.LogLevel.Warning,
        LogLevel.Error => Microsoft.Extensions.Logging.LogLevel.Error,
        LogLevel.Fatal => Microsoft.Extensions.Logging.LogLevel.Critical,
        _ => Microsoft.Extensions.Logging.LogLevel.Information
    };

    private static void LogException(Exception ex, ErrorHandlingOptions options)
    {
        var msLogLevel = ConvertLogLevel(options.LogLevel);
        _logger.Log(msLogLevel, ex, "[{0}] {1}", options.Source, options.ErrorMessage);
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
        var stopwatch = new Stopwatch();
        var result = new OperationResult<bool> { Success = false, Result = false };

        if (options.EnableDiagnostics)
        {
            result.Diagnostics = new Dictionary<string, object>
            {
                ["StartTime"] = Now,
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
                    result.Diagnostics["EndTime"] = Now;
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
        var stopwatch = new Stopwatch();
        var result = new OperationResult<T> { Success = false, Result = defaultValue };

        if (options.EnableDiagnostics)
        {
            result.Diagnostics = new Dictionary<string, object>
            {
                ["StartTime"] = Now,
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
                    result.Diagnostics["EndTime"] = Now;
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
        var stopwatch = new Stopwatch();
        var result = new OperationResult<bool> { Success = false, Result = false };

        if (options.EnableDiagnostics)
        {
            result.Diagnostics = new Dictionary<string, object>
            {
                ["StartTime"] = Now,
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
                    result.Diagnostics["EndTime"] = Now;
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
        var stopwatch = new Stopwatch();
        var result = new OperationResult<T> { Success = false, Result = defaultValue };

        if (options.EnableDiagnostics)
        {
            result.Diagnostics = new Dictionary<string, object>
            {
                ["StartTime"] = Now,
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
                    result.Diagnostics["EndTime"] = Now;
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
        var stopwatch = new Stopwatch();
        var result = new OperationResult<bool> { Success = false, Result = false };

        if (options.EnableDiagnostics)
        {
            result.Diagnostics = new Dictionary<string, object>
            {
                ["StartTime"] = Now,
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
                    result.Diagnostics["EndTime"] = Now;
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
        var stopwatch = new Stopwatch();
        var result = new OperationResult<T> { Success = false, Result = defaultValue };

        if (options.EnableDiagnostics)
        {
            result.Diagnostics = new Dictionary<string, object>
            {
                ["StartTime"] = Now,
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
                    result.Diagnostics["EndTime"] = Now;
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

    // Упрощенные методы для удобства использования
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

    public static ErrorHandlingOptions CreateOptions() => new() { Source = GetCallerInfo(2) };

    private static bool ShouldIgnoreException(Exception exception, Type[]? ignoreExceptionTypes) =>
        ignoreExceptionTypes?.Any(type => type.IsAssignableFrom(exception.GetType())) ?? false;

    #endregion

    #region Utility Methods

    private static string GetCallerInfo(int skipFrames = 2)
    {
        var stackFrame = new StackFrame(skipFrames, true);
        var method = stackFrame.GetMethod();
        if (method == null) return "Unknown";

        var className = method.DeclaringType?.Name ?? "Unknown";
        var methodName = method.Name;
        var fileName = stackFrame.GetFileName();
        var lineNumber = stackFrame.GetFileLineNumber();

        return !string.IsNullOrEmpty(fileName) && lineNumber > 0
            ? $"{className}.{methodName} ({Path.GetFileName(fileName)}:{lineNumber})"
            : $"{className}.{methodName}";
    }

    #endregion

    #region Application Lifecycle Methods

    public static void ReconfigureLogging() =>
        Safe(() =>
        {
            _logger.LogInformation("Reconfiguring logging settings");

            var serviceCollection = new ServiceCollection();
            ConfigureLogging(serviceCollection);
            var serviceProvider = serviceCollection.BuildServiceProvider();

            _loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            _logger = _loggerFactory.CreateLogger(nameof(SmartLogger));

            _logger.LogInformation("Logging reconfigured successfully");
        }, new ErrorHandlingOptions
        {
            Source = nameof(SmartLogger),
            ErrorMessage = "Error reconfiguring logging",
            LogLevel = LogLevel.Fatal
        });

    public static void Shutdown(int exitCode) =>
        Safe(() =>
        {
            _logger.LogInformation("Application '{0}' version '{1}' closed with exit code: {2}", ApplicationName, ApplicationVersion, exitCode);
        }, new ErrorHandlingOptions
        {
            Source = nameof(SmartLogger),
            ErrorMessage = "Error during shutdown",
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
                    var backupPath = Path.Combine(LogDirectoryPath, $"latest_{Now:yyyyMMdd_HHmmss}.log");
                    File.Move(latestLogPath, backupPath);
                }, new ErrorHandlingOptions
                {
                    Source = nameof(SmartLogger),
                    ErrorMessage = "Error creating log backup",
                    LogLevel = LogLevel.Error
                });
            }
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

    #endregion
}
