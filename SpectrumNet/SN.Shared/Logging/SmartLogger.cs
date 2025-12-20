namespace SpectrumNet.SN.Shared.Logging;

public interface ISmartLogger
{
    void Log(LogLevel level, string source, string message, bool forceLog = false);
    void Debug(string source, string message);
    void Info(string source, string message);
    void Warning(string source, string message);
    void Error(string source, string message);
    void Error(string source, string message, Exception ex);
    void Fatal(string source, string message);

    bool Safe(Action action, string source, string errorMessage);
    Task<bool> SafeAsync(Func<Task> action, string source, string errorMessage);
    T SafeResult<T>(Func<T> func, T defaultValue, string source, string errorMessage);
}

public sealed class SmartLogger : ISmartLogger
{
    private const string LogDir = "logs";
    private const string LatestLog = "latest.log";
    private const string Template = "{Timestamp:HH:mm:ss} [{Level:u3}] [T:{ThreadId}] [{Source}] {Message}{NewLine}{Exception}";
    private const int MaxSizeMB = 5;
    private const int RetainCount = 10;

    private static readonly Lazy<SmartLogger> _lazy = new(() =>
    {
        var logger = new SmartLogger();
        logger.Init();
        return logger;
    });

    public static SmartLogger Instance => _lazy.Value;

    private SmartLogger() { }

    private void Init()
    {
        try
        {
            EnsureLogDir();
            DeleteLatest();
            ConfigureLogger();
            LogStartup();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Logger init failed: {ex.Message}");
        }
    }

    public void Log(LogLevel level, string source, string message, bool forceLog = false) =>
        ForContext("Source", source).Write(ToSerilog(level), message);

    public void Debug(string source, string message) => Log(LogLevel.Debug, source, message);
    public void Info(string source, string message) => Log(LogLevel.Information, source, message);
    public void Warning(string source, string message) => Log(LogLevel.Warning, source, message);
    public void Error(string source, string message) => Log(LogLevel.Error, source, message);
    public void Fatal(string source, string message) => Log(LogLevel.Fatal, source, message);

    public void Error(string source, string message, Exception ex) =>
        ForContext("Source", source).Error(ex, message);

    public bool Safe(Action action, string source, string errorMessage)
    {
        try { action(); return true; }
        catch (Exception ex) { Error(source, errorMessage, ex); return false; }
    }

    public async Task<bool> SafeAsync(Func<Task> action, string source, string errorMessage)
    {
        try { await action(); return true; }
        catch (Exception ex) { Error(source, errorMessage, ex); return false; }
    }

    public T SafeResult<T>(Func<T> func, T defaultValue, string source, string errorMessage)
    {
        try { return func(); }
        catch (Exception ex) { Error(source, errorMessage, ex); return defaultValue; }
    }

    private static void ConfigureLogger()
    {
        Serilog.Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .Enrich.With(new ThreadEnricher())
            .WriteTo.File(
                Path.Combine(LogDir, LatestLog),
                fileSizeLimitBytes: MaxSizeMB * 1024 * 1024,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: RetainCount,
                outputTemplate: Template,
                rollingInterval: RollingInterval.Infinite)
            .CreateLogger();
    }

    private void LogStartup()
    {
        string ver = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";
        Info(nameof(SmartLogger), $"Application 'SpectrumNet' version '{ver}' started");
    }

    private static LogEventLevel ToSerilog(LogLevel level) => level switch
    {
        LogLevel.Trace => LogEventLevel.Verbose,
        LogLevel.Debug => LogEventLevel.Debug,
        LogLevel.Information => LogEventLevel.Information,
        LogLevel.Warning => LogEventLevel.Warning,
        LogLevel.Error => LogEventLevel.Error,
        LogLevel.Fatal => LogEventLevel.Fatal,
        _ => LogEventLevel.Information
    };

    private static void EnsureLogDir()
    {
        if (!Directory.Exists(LogDir))
            Directory.CreateDirectory(LogDir);
    }

    private static void DeleteLatest()
    {
        string path = Path.Combine(LogDir, LatestLog);
        if (File.Exists(path))
            File.Delete(path);
    }

    private sealed class ThreadEnricher : ILogEventEnricher
    {
        public void Enrich(LogEvent evt, ILogEventPropertyFactory factory) =>
            evt.AddPropertyIfAbsent(factory.CreateProperty("ThreadId", CurrentManagedThreadId));
    }
}
