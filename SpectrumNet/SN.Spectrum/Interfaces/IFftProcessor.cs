#nullable enable

namespace SpectrumNet.SN.Spectrum.Interfaces;

public interface IFftProcessor
{
    event EventHandler<FftEventArgs>? FftCalculated;
    ValueTask AddSamplesAsync(
        ReadOnlyMemory<float> samples,
        int sampleRate,
        CancellationToken cancellationToken = default);
    FftWindowType WindowType { get; set; }
    void ResetFftState();
}