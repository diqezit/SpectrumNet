#nullable enable

namespace SpectrumNet.Controllers.Interfaces.AudioCore;

public interface ICaptureService : IAsyncDisposable
{
    bool IsRecording { get; }
    bool IsInitializing { get; }
    SpectrumAnalyzer? GetAnalyzer();
    Task StartCaptureAsync();
    Task StopCaptureAsync(bool force = false);
    Task ReinitializeCaptureAsync();
}