#nullable enable

namespace SpectrumNet.Service.ErrorHandlers;

public static class FileLoggerExtensions
{
    public static ILoggingBuilder AddFile(
        this ILoggingBuilder builder,
        string filePath,
        Action<FileLoggerOptions>? configure = null)
    {
        var options = new FileLoggerOptions();
        configure?.Invoke(options);

        builder.Services.AddSingleton<ILoggerProvider>(new FileLoggerProvider(filePath, options));
        return builder;
    }
}
