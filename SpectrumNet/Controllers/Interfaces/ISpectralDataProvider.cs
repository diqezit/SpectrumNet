// ISpectralDataProvider.cs
#nullable enable

namespace SpectrumNet.Controllers.Interfaces;

public interface ISpectralDataProvider
{
    event EventHandler<SpectralDataEventArgs>? SpectralDataReady;
    SpectralData? GetCurrentSpectrum();
    Task AddSamplesAsync(ReadOnlyMemory<float> samples, int sampleRate,
        CancellationToken cancellationToken = default);
}