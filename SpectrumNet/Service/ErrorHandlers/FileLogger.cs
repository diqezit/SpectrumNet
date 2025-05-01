#nullable enable

namespace SpectrumNet.Service.ErrorHandlers;

public class FileLogger(
    string filePath,
    FileLoggerOptions options) 
    : ILogger
{
    private static readonly object _lock = new();

    public IDisposable BeginScope<TState>(TState state) => NullScope.Instance;

    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

    public void Log<TState>(
        Microsoft.Extensions.Logging.LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception);
        var timestamp = Now;
        var threadId = CurrentManagedThreadId;

        var logEntry = options.OutputTemplate
            .Replace("{Timestamp:yyyy-MM-dd HH:mm:ss}", timestamp.ToString("yyyy-MM-dd HH:mm:ss"))
            .Replace("{Timestamp:HH:mm:ss}", timestamp.ToString("HH:mm:ss"))
            .Replace("{Level}", logLevel.ToString())
            .Replace("{Level:u3}", logLevel.ToString().Substring(0, Min(3, logLevel.ToString().Length)))
            .Replace("{Message}", message)
            .Replace("{ThreadId}", threadId.ToString())
            .Replace("{NewLine}", NewLine);

        if (exception != null)
            logEntry = logEntry.Replace("{Exception}", exception.ToString());
        else
            logEntry = logEntry.Replace("{Exception}", string.Empty);

        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                File.AppendAllText(filePath, logEntry);
                RotateLogFileIfNeeded();
            }
            catch
            {
                // Игнорируем ошибки при записи в файл
            }
        }
    }

    private void RotateLogFileIfNeeded()
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Exists && fileInfo.Length > options.FileSizeLimitBytes)
            {
                var directory = Path.GetDirectoryName(filePath)!;
                var fileName = Path.GetFileNameWithoutExtension(filePath);
                var extension = Path.GetExtension(filePath);

                var timestamp = Now.ToString("yyyyMMdd_HHmmss");
                var newFilePath = Path.Combine(directory, $"{fileName}_{timestamp}{extension}");

                File.Move(filePath, newFilePath);

                var logFiles = Directory.GetFiles(directory, $"{fileName}_*{extension}")
                                       .OrderByDescending(f => f)
                                       .Skip(options.MaxRollingFiles - 1);

                foreach (var oldFile in logFiles)
                {
                    try { File.Delete(oldFile); } catch { }
                }
            }
        }
        catch
        {
            // Игнорируем ошибки ротации
        }
    }
}
