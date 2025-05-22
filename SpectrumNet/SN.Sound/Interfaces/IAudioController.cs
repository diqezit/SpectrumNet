// IAudioController.cs
namespace SpectrumNet.SN.Sound.Interfaces;

public interface IAudioController
{
    // Методы управления захватом
    Task StartCaptureAsync();
    Task StopCaptureAsync();
    Task ToggleCaptureAsync();

    // Свойства состояния
    bool IsRecording { get; set; }
    bool CanStartCapture { get; }
    bool IsTransitioning { get; set; }

    // Аудио свойства
    FftWindowType WindowType { get; set; }
    float MinDbLevel { get; set; }
    float MaxDbLevel { get; set; }
    float AmplificationFactor { get; set; }
    IGainParametersProvider GainParameters { get; }
    SpectrumAnalyzer? GetCurrentAnalyzer();
}