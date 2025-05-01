// IFftProcessor.cs
#nullable enable

namespace SpectrumNet.Controllers.Interfaces;

public interface IFftProcessor : IAsyncDisposable
{
    event EventHandler<FftEventArgs>? FftCalculated;
    ValueTask AddSamplesAsync(ReadOnlyMemory<float> samples, int sampleRate,
        CancellationToken cancellationToken = default);
    FftWindowType WindowType { get; set; }
    void ResetFftState();
}