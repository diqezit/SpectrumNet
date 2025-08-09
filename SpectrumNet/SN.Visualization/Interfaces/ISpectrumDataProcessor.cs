#nullable enable

namespace SpectrumNet.SN.Visualization.Interfaces;

public interface ISpectrumDataProcessor
{
    float[] ProcessSpectrum(float[] spectrum, int targetCount);
    SpectralData? GetCurrentSpectrum();
    bool RequiresRedraw();
    void Configure(bool isOverlayActive);
}