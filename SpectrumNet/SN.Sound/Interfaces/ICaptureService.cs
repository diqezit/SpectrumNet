// SN.Sound/Interfaces/ICaptureService.cs
#nullable enable

namespace SpectrumNet.SN.Sound.Interfaces;

public interface ICaptureService : IAsyncDisposable
{
    bool IsRecording { get; }
    bool IsInitializing { get; }
    bool IsDeviceAvailable { get; }
    SpectrumAnalyzer? GetAnalyzer();
    Task StartCaptureAsync();
    Task StopCaptureAsync(bool force = false);
    Task ReinitializeCaptureAsync();
}