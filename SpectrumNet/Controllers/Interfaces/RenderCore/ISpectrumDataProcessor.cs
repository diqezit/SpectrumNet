#nullable enable

namespace SpectrumNet.Controllers.Interfaces.RenderCore;

public interface ISpectrumDataProcessor
{
    float[] ProcessSpectrum(float[] spectrum, int targetCount);
    SpectralData? GetCurrentSpectrum();
    bool RequiresRedraw();
    void Configure(bool isOverlayActive);
}