#nullable enable

namespace SpectrumNet.Controllers.Interfaces;

public interface ISpectrumConverter
{
    float[] ConvertToSpectrum(Complex[] fftResult, int sampleRate,
        SpectrumScale scale, CancellationToken cancellationToken = default);
}
