#nullable enable

namespace SpectrumNet.Service.ErrorHandlers;

public static class SmartLogger
{
    private const string
        LogDirectoryPath = "logs",
        LatestLogFileName = "latest.log",
        OutputTemplate = "{Timestamp:HH:mm:ss} [{Level:u3}] [T:{ThreadId}] [{Source}] {Message}{NewLine}{Exception}";

    private const int
        MaxFileSizeMB = 5,
        RetainedFileCount = 10;

    public static void Initialize() =>
        Safe(() =>
        {
            EnsureLogDirectoryExists();
            TryDeleteLatestLog();
            ConfigureLoggerSettings();
            LogStartupInfo();
        }, nameof(SmartLogger), "Error initializing logging", LogLevel.Fatal);

    public static void Log(LogLevel level, string source, string message, bool forceLog = false) =>
        ForContext("Source", source).Write(ConvertToSerilogLevel(level), message);

    public static void Error(string source, string message) =>
        Log(LogLevel.Error, source, message);

    public static void Error(string source, string message, Exception ex) =>
        ForContext("Source", source).Error(ex, message);

    public static void Fatal(string source, string message) =>
        Log(LogLevel.Fatal, source, message);

    public static bool Safe(Action action, string source, string errorMessage,
        LogLevel logLevel = LogLevel.Error, Type[]? ignoreExceptions = null)
    {
        return Safe(action, new ErrorHandlingOptions
        {
            Source = source,
            ErrorMessage = errorMessage,
            LogLevel = logLevel,
            IgnoreExceptions = ignoreExceptions
        });
    }

    public static async Task<bool> SafeAsync(Func<Task> asyncAction, string source, string errorMessage,
        LogLevel logLevel = LogLevel.Error, Type[]? ignoreExceptions = null)
    {
        return await SafeAsync(asyncAction, new ErrorHandlingOptions
        {
            Source = source,
            ErrorMessage = errorMessage,
            LogLevel = logLevel,
            IgnoreExceptions = ignoreExceptions
        });
    }

    public static T SafeResult<T>(Func<T> func, T defaultValue, string source, string errorMessage,
        LogLevel logLevel = LogLevel.Error, Type[]? ignoreExceptions = null)
    {
        return SafeResult(func, defaultValue, new ErrorHandlingOptions
        {
            Source = source,
            ErrorMessage = errorMessage,
            LogLevel = logLevel,
            IgnoreExceptions = ignoreExceptions
        });
    }

    public static bool Safe(Action action, ErrorHandlingOptions? options = null)
    {
        options ??= new();
        options.Source ??= GetCallerInfo();
        options.ErrorMessage ??= "Operation failed";

        try
        {
            action();
            return true;
        }
        catch (Exception ex)
        {
            HandleException(ex, options);
            return false;
        }
    }

    public static T SafeResult<T>(
        Func<T> func,
        T defaultValue = default!,
        ErrorHandlingOptions? options = null)
    {
        options ??= new();
        options.Source ??= GetCallerInfo();
        options.ErrorMessage ??= "Operation failed";

        try
        {
            return func();
        }
        catch (Exception ex)
        {
            HandleException(ex, options);
            return defaultValue;
        }
    }

    public static async Task<bool> SafeAsync(
        Func<Task> asyncAction,
        ErrorHandlingOptions? options = null)
    {
        options ??= new();
        options.Source ??= GetCallerInfo();
        options.ErrorMessage ??= "Async operation failed";

        try
        {
            await asyncAction();
            return true;
        }
        catch (Exception ex)
        {
            HandleException(ex, options);
            return false;
        }
    }

    public static bool SafeDispose(
        IDisposable? resource,
        string resourceName,
        ErrorHandlingOptions? options = null)
    {
        if (resource == null)
            return true;

        options ??= new();
        options.Source ??= GetCallerInfo();
        options.ErrorMessage ??= $"Error disposing {resourceName}";

        return Safe(() => resource.Dispose(), options);
    }

    private static void ConfigureLoggerSettings()
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
        Log(LogLevel.Information, nameof(SmartLogger),
            $"Application 'SpectrumNet' version '{version}' started");
    }

    private static void HandleException(Exception ex, ErrorHandlingOptions options)
    {
        if (ShouldIgnoreException(ex, options.IgnoreExceptions))
            return;

        Error(options.Source!, options.ErrorMessage!, ex);
        options.ExceptionHandler?.Invoke(ex);
    }

    private static LogEventLevel ConvertToSerilogLevel(LogLevel level) =>
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

    private static string GetCallerInfo(int skipFrames = 2)
    {
        var stackFrame = new StackFrame(skipFrames, true);
        var method = stackFrame.GetMethod();
        if (method == null) return "Unknown";

        var className = method.DeclaringType?.Name ?? "Unknown";
        var methodName = method.Name;

        if (stackFrame.GetFileName() is string fileName
            && stackFrame.GetFileLineNumber() is int lineNumber
            && lineNumber > 0)
            return $"{className}.{methodName} ({Path.GetFileName(fileName)}:{lineNumber})";

        return $"{className}.{methodName}";
    }

    private static bool ShouldIgnoreException(Exception exception, Type[]? ignoreExceptionTypes) =>
        ignoreExceptionTypes?.Any(type => type.IsAssignableFrom(exception.GetType())) ?? false;

    private class ThreadEnricher : ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            var threadId = Environment.CurrentManagedThreadId;
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("ThreadId", threadId));
        }
    }

    public class ErrorHandlingOptions
    {
        public LogLevel LogLevel { get; set; } = LogLevel.Error;
        public string? ErrorMessage { get; set; }
        public string? Source { get; set; }
        public Action<Exception>? ExceptionHandler { get; set; }
        public Type[]? IgnoreExceptions { get; set; }
    }
}