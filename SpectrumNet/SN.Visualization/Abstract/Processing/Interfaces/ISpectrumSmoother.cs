#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Processing.Interfaces;

public interface ISpectrumSmoother
{
    float SmoothingFactor { get; set; }
    float[] SmoothSpectrum(float[] spectrum, int targetCount, float? customFactor = null);
    void Reset();
}
