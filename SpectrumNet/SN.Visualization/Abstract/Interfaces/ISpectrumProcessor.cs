#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Interfaces;

public interface ISpectrumProcessor : IDisposable
{
    float SmoothingFactor { get; set; }

    (bool isValid, float[]? processedSpectrum) ProcessSpectrum(
        float[]? spectrum,
        int targetCount,
        int spectrumLength,
        float? customSmoothingFactor = null);

    float[] ProcessBands(
        float[] spectrum,
        int bandCount);
}
