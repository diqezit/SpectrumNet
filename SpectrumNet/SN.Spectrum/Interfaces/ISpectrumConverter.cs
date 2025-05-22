#nullable enable

namespace SpectrumNet.SN.Spectrum.Interfaces;

public interface ISpectrumConverter
{
    float[] ConvertToSpectrum(
        Complex[] fftResult,
        int sampleRate,
        SpectrumScale scale,
        CancellationToken cancellationToken = default);
}