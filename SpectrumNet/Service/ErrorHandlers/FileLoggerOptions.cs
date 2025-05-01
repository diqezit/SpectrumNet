#nullable enable

namespace SpectrumNet.Service.ErrorHandlers;

public class FileLoggerOptions
{
    public long FileSizeLimitBytes { get; set; } = 10 * 1024 * 1024; // 10MB по умолчанию
    public int MaxRollingFiles { get; set; } = 5;
    public string OutputTemplate { get; set; } = 
        "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level}] {Message}{NewLine}{Exception}";
}