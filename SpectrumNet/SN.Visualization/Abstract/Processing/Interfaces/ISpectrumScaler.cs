#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Processing.Interfaces;

public interface ISpectrumScaler
{
    float[] ScaleSpectrum(float[] spectrum, int targetCount, int spectrumLength);
}

