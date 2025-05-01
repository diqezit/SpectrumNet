#nullable enable

namespace SpectrumNet.Service.ErrorHandlers;

public class FileLoggerProvider(
    string filePath,
    FileLoggerOptions options) 
    : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => 
        new FileLogger(filePath, options);

    public void Dispose() { }
}
