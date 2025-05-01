#nullable enable

namespace SpectrumNet.Controllers.SpectrumCore;

public sealed class FftProcessor : IFftProcessor, IAsyncDisposable
{
    private const string LogSource = nameof(FftProcessor);
    private readonly int _fftSize;
    private readonly Complex[] _buffer;
    private readonly Channel<(ReadOnlyMemory<float> Samples, int SampleRate,
        CancellationToken Token)> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Dictionary<FftWindowType, float[]> _windows = new();
    private readonly float[] _cosCache, _sinCache;
    private readonly int _vecSize = Vector<float>.Count;
    private readonly ParallelOptions _parallelOpts;
    private readonly ThreadLocal<Complex[]> _threadLocalBuffer;
    private readonly ArrayPool<Complex> _complexArrayPool = ArrayPool<Complex>.Shared;
    private readonly ArrayPool<float> _floatArrayPool = ArrayPool<float>.Shared;
    private float[] _window;
    private int _sampleCount;
    private bool _disposed;
    private FftWindowType _windowType = FftWindowType.Hann;

    public event EventHandler<FftEventArgs>? FftCalculated;

    public FftProcessor(
        int fftSize = Constants.DefaultFftSize,
        int channelCapacity = Constants.DefaultChannelCapacity
    )
    {
        if (!BitOperations.IsPow2(fftSize) || fftSize <= 0)
            throw new ArgumentException("FFT size must be a positive power of 2",
                nameof(fftSize));
        _fftSize = fftSize;
        _buffer = new Complex[fftSize];
        (_cosCache, _sinCache) = TrigonometricTables.Get(fftSize);
        foreach (FftWindowType type in Enum.GetValues(typeof(FftWindowType)))
            _windows[type] = GenerateWindow(fftSize, type);
        _window = _windows[_windowType];
        var options = new BoundedChannelOptions(channelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        };
        _channel = Channel.CreateBounded<(ReadOnlyMemory<float>, int, CancellationToken)>(
            options);
        _parallelOpts = new ParallelOptions
        {
            MaxDegreeOfParallelism = ProcessorCount
        };
        _threadLocalBuffer = new ThreadLocal<Complex[]>(() => new Complex[fftSize]);
        Task.Run(ProcessAsync);
    }

    public FftWindowType WindowType
    {
        get => _windowType;
        set
        {
            if (_windowType == value)
                return;
            if (!_windows.TryGetValue(value, out var window) || window is null)
                throw new InvalidOperationException($"Unsupported window type: {value}");
            _windowType = value;
            _window = window;
            ResetFftState();
        }
    }

    public ValueTask AddSamplesAsync(ReadOnlyMemory<float> samples, int sampleRate,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(FftProcessor));
        if (sampleRate <= 0)
            throw new ArgumentException("Sample rate must be positive",
                nameof(sampleRate));
        if (samples.Length == 0)
            return ValueTask.CompletedTask;
        using var linkedCts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
        return _channel.Writer.TryWrite((samples, sampleRate, linkedCts.Token))
            ? ValueTask.CompletedTask
            : new ValueTask(
                _channel.Writer.WriteAsync((samples, sampleRate, linkedCts.Token),
                    linkedCts.Token).AsTask());
    }

    public void ResetFftState()
    {
        _sampleCount = 0;
        Array.Clear(_buffer, 0, _fftSize);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        await SafeValueTaskResultAsync(async () =>
        {
            _cts.Cancel();
            _channel.Writer.Complete();
            _threadLocalBuffer.Dispose();
            await Task.CompletedTask;
            return true;
        },
        defaultValue: false,
        options: new ErrorHandlingOptions
        {
            Source = LogSource,
            ErrorMessage = "Error during disposal"
        });
        _cts.Dispose();
    }

    private float[] GenerateWindow(int size, FftWindowType type) =>
        SafeResult(() =>
        {
            float[] w = new float[size];
            Action<int> setWindow = type switch
            {
                FftWindowType.Hann =>
                    i => w[i] = 0.5f * (1f - _cosCache[i]),
                FftWindowType.Hamming =>
                    i => w[i] = 0.54f - 0.46f * _cosCache[i],
                FftWindowType.Blackman =>
                    i => w[i] = 0.42f - 0.5f * _cosCache[i] +
                        0.08f * MathF.Cos(Constants.TwoPi * 2 * i / (size - 1)),
                FftWindowType.Bartlett =>
                    i => w[i] = 2f / (size - 1) * ((size - 1) / 2f -
                        MathF.Abs(i - (size - 1) / 2f)),
                FftWindowType.Kaiser =>
                    i => w[i] = BesselI0(Constants.KaiserBeta *
                        MathF.Sqrt(1 - MathF.Pow(2f * i / (size - 1) - 1, 2))) /
                        BesselI0(Constants.KaiserBeta),
                _ => throw new NotSupportedException($"Unsupported window type: {type}")
            };
            Parallel.For(0, size, setWindow);
            return w;
        },
        defaultValue: new float[size],
        options: new ErrorHandlingOptions
        {
            Source = LogSource,
            ErrorMessage = "Error generating window"
        });

    private static float BesselI0(float x)
    {
        float sum = 1f, term = x * x / 4f;
        for (int k = 1; term > Constants.BesselEpsilon; k++)
        {
            sum += term;
            term *= x * x / (4f * k * k);
        }
        return sum;
    }

    private async Task ProcessAsync() =>
        await SafeExecuteAsync(async () =>
        {
            await foreach (var (samples, rate, token) in
                _channel.Reader.ReadAllAsync(_cts.Token))
            {
                if (token.IsCancellationRequested)
                    continue;
                if (samples.Length > 0)
                    await Task.Run(() => ProcessBatch(samples, rate, token), token);
            }
        },
        new ErrorHandlingOptions
        {
            Source = LogSource,
            ErrorMessage = "Error in FFT processing loop",
            IgnoreExceptions = new[] { typeof(OperationCanceledException) }
        });

    private void ProcessBatch(ReadOnlyMemory<float> samples, int rate,
        CancellationToken cancellationToken) =>
        SafeExecute(() =>
        {
            int pos = 0;
            while (pos < samples.Length)
            {
                if (cancellationToken.IsCancellationRequested)
                    return;
                int count = Min(_fftSize - _sampleCount,
                    samples.Length - pos);
                if (count <= 0)
                    break;
                ProcessChunk(samples.Slice(pos, count));
                pos += count;
                _sampleCount += count;
                if (_sampleCount >= _fftSize)
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;
                    PerformFftAndNotify(rate, cancellationToken);
                    ResetFftState();
                }
            }
        },
        new ErrorHandlingOptions
        {
            Source = LogSource,
            ErrorMessage = "Error processing batch"
        });

    private void PerformFftAndNotify(int rate,
        CancellationToken cancellationToken) =>
        SafeExecute(() =>
        {
            Complex[] fftBuffer = _complexArrayPool.Rent(_fftSize);
            Array.Copy(_buffer, fftBuffer, _fftSize);
            Task.Run(() =>
            {
                try
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;
                    FastFourierTransform.FFT(true,
                        (int)Log2(_fftSize), fftBuffer);
                    if (!cancellationToken.IsCancellationRequested &&
                        FftCalculated != null)
                        FftCalculated(this,
                            new FftEventArgs(fftBuffer, rate));
                }
                catch (Exception ex)
                {
                    Log(LogLevel.Error, LogSource,
                        $"FFT calculation failed: {ex}");
                }
                finally
                {
                    _complexArrayPool.Return(fftBuffer);
                }
            }, cancellationToken);
        },
        new ErrorHandlingOptions
        {
            Source = LogSource,
            ErrorMessage = "Error preparing FFT"
        });

    private void ProcessChunk(ReadOnlyMemory<float> chunk) =>
        SafeExecute(() =>
        {
            int offset = _sampleCount;
            int len = chunk.Length;
            if (len > 1024)
            {
                int chunkSize = Max(1024, len / ProcessorCount);
                Parallel.For(0, (len + chunkSize - 1) / chunkSize, _parallelOpts, i =>
                {
                    int start = i * chunkSize;
                    int end = Min(start + chunkSize, len);
                    if (_parallelOpts.CancellationToken.IsCancellationRequested)
                        return;
                    ApplyWindow(chunk.Slice(start, end - start), offset + start);
                });
            }
            else
            {
                ApplyWindow(chunk, offset);
            }
        },
        new ErrorHandlingOptions
        {
            Source = LogSource,
            ErrorMessage = "Error processing chunk"
        });

    private void ApplyWindow(ReadOnlyMemory<float> data, int offset)
    {
        int len = data.Length;
        int vecEnd = len - len % _vecSize;
        Span<float> temp = stackalloc float[_vecSize];
        for (int i = 0; i < vecEnd; i += _vecSize)
        {
            data.Span.Slice(i, _vecSize).CopyTo(temp);
            Vector<float> s = new(temp);
            Vector<float> w = new(_window, offset + i);
            (s * w).CopyTo(temp);
            for (int j = 0; j < _vecSize; j++)
                _buffer[offset + i + j] = new Complex { X = temp[j] };
        }
        for (int i = vecEnd; i < len; i++)
            _buffer[offset + i] = new Complex { X = data.Span[i] * _window[offset + i] };
    }
}
