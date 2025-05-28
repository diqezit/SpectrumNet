#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Processing.Interfaces;

// процесс обработки спектра
public interface ISpectrumProcessingCoordinator : IDisposable
{
    (bool isValid, float[]? processedSpectrum) PrepareSpectrum(
        float[]? spectrum,
        int targetCount,
        int spectrumLength);
    void SetSmoothingFactor(float factor);
}
