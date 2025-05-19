#nullable enable

namespace SpectrumNet.Service.ErrorHandlers;

public interface ISmartLogger
{
    // Базовые методы логирования
    void Log(LogLevel level, string source, string message, bool forceLog = false);
    void Debug(string source, string message);
    void Info(string source, string message);
    void Warning(string source, string message);
    void Error(string source, string message);
    void Error(string source, string message, Exception ex);
    void Fatal(string source, string message);

    // Методы для безопасного выполнения кода
    bool Safe(Action action, string source, string errorMessage);
    Task<bool> SafeAsync(Func<Task> asyncAction, string source, string errorMessage);
    T SafeResult<T>(Func<T> func, T defaultValue, string source, string errorMessage);
}