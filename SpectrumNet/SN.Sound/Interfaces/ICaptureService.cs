#nullable enable

namespace SpectrumNet.SN.Sound.Interfaces;

public interface ICaptureService : IAsyncDisposable
{
    bool IsRecording { get; }
    bool IsInitializing { get; }
    SpectrumAnalyzer? GetAnalyzer();
    Task StartCaptureAsync();
    Task StopCaptureAsync(bool force = false);
    Task ReinitializeCaptureAsync();
}