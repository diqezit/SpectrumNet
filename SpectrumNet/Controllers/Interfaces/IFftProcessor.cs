#nullable enable

namespace SpectrumNet.Controllers.Interfaces;

public interface IFftProcessor
{
    event EventHandler<FftEventArgs>? FftCalculated;
    ValueTask AddSamplesAsync(ReadOnlyMemory<float> samples, int sampleRate,
        CancellationToken cancellationToken = default);
    ValueTask DisposeAsync();
    FftWindowType WindowType { get; set; }
    void ResetFftState();
}
