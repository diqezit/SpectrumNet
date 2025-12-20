using FftSharp;
using FftSharp.Windows;

namespace SpectrumNet.SN.Spectrum;

public sealed class SpectrumAnalyzer : ISpectralDataProvider
{
    private const int FftSize = 2048;

    private readonly Channel<(float[] Data, int Rate)> _channel;
    private readonly IGainParametersProvider _gain;
    private readonly CancellationTokenSource _cts = new();
    private readonly double[] _fftBuffer = new double[FftSize];
    private readonly object _lock = new();

    private double[] _window;
    private FftWindowType _winType = FftWindowType.Hann;
    private int _bufferPos;
    private volatile bool _disposed;
    private SpectralData _lastData;

    public event EventHandler<SpectralDataEventArgs>? SpectralDataReady;

    public SpectrumAnalyzer(IGainParametersProvider gain)
    {
        _gain = gain ?? throw new ArgumentNullException(nameof(gain));
        _window = MakeWindow(FftSize, _winType);
        _channel = Channel.CreateBounded<(float[], int)>(new BoundedChannelOptions(10)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropOldest
        });
        _ = ProcessLoopAsync(_cts.Token);
    }

    public FftWindowType WindowType
    {
        get => _winType;
        set
        {
            if (_winType == value) return;
            _winType = value;
            _window = MakeWindow(FftSize, value);
            ClearBuffer();
        }
    }

    public SpectrumScale ScaleType { get; set; } = SpectrumScale.Linear;
    public StereoMode StereoMode { get; set; } = StereoMode.Mid;

    public async Task AddSamplesAsync(ReadOnlyMemory<float> samples, int sampleRate, CancellationToken ct = default)
    {
        if (_disposed || samples.IsEmpty) return;
        await _channel.Writer.WriteAsync((samples.ToArray(), sampleRate), ct).ConfigureAwait(false);
    }

    public SpectralData? GetSpectrum()
    {
        lock (_lock)
            return _lastData.IsEmpty ? null : _lastData;
    }

    public void Reset()
    {
        ClearBuffer();
        Emit(SpectralData.Empty);
    }

    private void ClearBuffer()
    {
        _bufferPos = 0;
        Array.Clear(_fftBuffer);
    }

    private async Task ProcessLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach ((float[] samples, int rate) in _channel.Reader.ReadAllAsync(ct))
                ProcessChunk(samples, rate);
        }
        catch (OperationCanceledException) { }
    }

    private void ProcessChunk(float[] samples, int rate)
    {
        int pos = 0, len = samples.Length;

        while (pos < len)
        {
            int toCopy = Min(FftSize - _bufferPos, len - pos);

            for (int i = 0; i < toCopy; i++)
                _fftBuffer[_bufferPos + i] = samples[pos + i] * _window[_bufferPos + i];

            _bufferPos += toCopy;
            pos += toCopy;

            if (_bufferPos >= FftSize)
            {
                double[] mag = FFT.Magnitude(FFT.Forward(_fftBuffer));
                float[] spectrum = SpectrumMath.Convert(mag, rate, ScaleType, _gain);
                Emit(new SpectralData(spectrum, DateTime.UtcNow));
                ClearBuffer();
            }
        }
    }

    private void Emit(SpectralData data)
    {
        lock (_lock)
            _lastData = data;
        SpectralDataReady?.Invoke(this, new SpectralDataEventArgs(data));
    }

    private static double[] MakeWindow(int size, FftWindowType type) => type switch
    {
        FftWindowType.Hamming => new Hamming().Create(size),
        FftWindowType.Blackman => new Blackman().Create(size),
        FftWindowType.Bartlett => new Bartlett().Create(size),
        FftWindowType.Kaiser => new Kaiser().Create(size),
        FftWindowType.FlatTop => new FlatTop().Create(size),
        _ => new Hanning().Create(size)
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        _channel.Writer.TryComplete();
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }
}
