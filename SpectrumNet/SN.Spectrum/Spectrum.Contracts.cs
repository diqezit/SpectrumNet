namespace SpectrumNet.SN.Spectrum;

public enum FftWindowType { Hann, Hamming, Blackman, Bartlett, Kaiser, FlatTop }
public enum SpectrumScale { Linear, Logarithmic, Mel, Bark, ERB }
public enum StereoMode { Mid, Left, Right, Max, RMS }

public readonly record struct SpectralData(float[] Spectrum, DateTime Timestamp)
{
    public static readonly SpectralData Empty = new([], DateTime.MinValue);
    public bool IsEmpty => Spectrum is null || Spectrum.Length == 0;
}

public sealed class SpectralDataEventArgs(SpectralData data) : EventArgs
{
    public SpectralData Data { get; } = data;
}

public interface ISpectralDataProvider : IDisposable
{
    event EventHandler<SpectralDataEventArgs>? SpectralDataReady;
    FftWindowType WindowType { get; set; }
    SpectrumScale ScaleType { get; set; }
    Task AddSamplesAsync(ReadOnlyMemory<float> samples, int sampleRate, CancellationToken ct = default);
    SpectralData? GetSpectrum();
    void Reset();
}

public interface IGainParametersProvider
{
    float AmplificationFactor { get; set; }
    float MaxDbValue { get; set; }
    float MinDbValue { get; set; }
}
