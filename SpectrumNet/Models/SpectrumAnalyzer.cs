#nullable enable
namespace SpectrumNet
{
    public static class Constants
    {
        public const float DefaultAmplificationFactor = 0.5f,
            DefaultMaxDbValue = 0f,
            DefaultMinDbValue = -130f,
            Epsilon = float.Epsilon,
            TwoPi = 2f * MathF.PI,
            KaiserBeta = 5f,
            BesselEpsilon = 1e-10f,
            InvLog10 = 0.43429448190325182765f;
        public const int DefaultFftSize = 2048,
            DefaultChannelCapacity = 10;
    }

    public enum FftWindowType
    {
        Hann,
        Hamming,
        Blackman,
        Bartlett,
        Kaiser
    }

    public enum SpectrumScale
    {
        Linear,
        Logarithmic,
        Mel,
        Bark,
        ERB
    }

    public interface IFftProcessor
    {
        event EventHandler<FftEventArgs>? FftCalculated;
        ValueTask AddSamplesAsync(ReadOnlyMemory<float> samples, int sampleRate,
            CancellationToken cancellationToken = default);
        ValueTask DisposeAsync();
        FftWindowType WindowType { get; set; }
        void ResetFftState();
    }

    public interface IGainParametersProvider
    {
        float AmplificationFactor { get; }
        float MaxDbValue { get; }
        float MinDbValue { get; }
    }

    public interface ISpectralDataProvider
    {
        event EventHandler<SpectralDataEventArgs>? SpectralDataReady;
        SpectralData? GetCurrentSpectrum();
        Task AddSamplesAsync(ReadOnlyMemory<float> samples, int sampleRate,
            CancellationToken cancellationToken = default);
    }

    public interface ISpectrumConverter
    {
        float[] ConvertToSpectrum(Complex[] fftResult, int sampleRate,
            SpectrumScale scale, CancellationToken cancellationToken = default);
    }

    public record FftEventArgs(Complex[] Result, int SampleRate);
    public record SpectralDataEventArgs(SpectralData Data);
    public record SpectralData(float[] Spectrum, DateTime Timestamp);

    public record SpectrumParameters(
        float MinDb,
        float DbRange,
        float AmplificationFactor
    )
    {
        public static SpectrumParameters FromProvider(IGainParametersProvider? provider) =>
            provider is null
                ? throw new ArgumentNullException(nameof(provider))
                : new SpectrumParameters(
                    provider.MinDbValue,
                    Math.Max(provider.MaxDbValue - provider.MinDbValue, Constants.Epsilon),
                    provider.AmplificationFactor
                );
    }

    public sealed class GainParameters : IGainParametersProvider, INotifyPropertyChanged
    {
        private const string LogSource = nameof(GainParameters);
        private float _amp = Constants.DefaultAmplificationFactor,
            _min = Constants.DefaultMinDbValue,
            _max = Constants.DefaultMaxDbValue;
        private readonly SynchronizationContext? _context;
        public event PropertyChangedEventHandler? PropertyChanged;

        public GainParameters(
            SynchronizationContext? context = null,
            float minDbValue = Constants.DefaultMinDbValue,
            float maxDbValue = Constants.DefaultMaxDbValue,
            float amplificationFactor = Constants.DefaultAmplificationFactor
        )
        {
            if (minDbValue > maxDbValue)
                throw new ArgumentException("MinDbValue cannot be greater than MaxDbValue.");
            (_context, _min, _max, _amp) =
                (context, minDbValue, maxDbValue, amplificationFactor);
        }

        public float AmplificationFactor
        {
            get => _amp;
            set => UpdateProperty(ref _amp, Math.Max(0.1f, value));
        }

        public float MaxDbValue
        {
            get => _max;
            set => UpdateProperty(ref _max, value < _min ? _min : value);
        }

        public float MinDbValue
        {
            get => _min;
            set => UpdateProperty(ref _min, value > _max ? _max : value);
        }

        private void UpdateProperty(ref float field, float value,
            [CallerMemberName] string? propertyName = null)
        {
            if (Math.Abs(field - value) <= Constants.Epsilon)
                return;
            field = value;
            SmartLogger.SafeExecute(() => {
                if (_context != null)
                    _context.Post(_ =>
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)),
                        null);
                else
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            },
            new SmartLogger.ErrorHandlingOptions
            {
                Source = LogSource,
                ErrorMessage = $"Error notifying property change: {propertyName}"
            });
        }
    }

    public sealed class SpectrumAnalyzer : ISpectralDataProvider, IDisposable, IComponent
    {
        private const string LogSource = nameof(SpectrumAnalyzer);
        private readonly IFftProcessor _fftProcessor;
        private readonly ISpectrumConverter _converter;
        private readonly SynchronizationContext? _context;
        private readonly object _lock = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Channel<(Complex[] Fft, int SampleRate)> _processingChannel;
        private readonly ArrayPool<float> _floatArrayPool = ArrayPool<float>.Shared;
        private SpectralData? _lastData;
        private SpectrumScale _scaleType = SpectrumScale.Linear;
        private bool _disposed; 

        public event EventHandler<SpectralDataEventArgs>? SpectralDataReady;
        public event EventHandler? Disposed;
        public ISite? Site { get; set; }
        public bool IsDisposed => _disposed;

        public SpectrumAnalyzer(
            IFftProcessor fftProcessor,
            ISpectrumConverter converter,
            SynchronizationContext? context = null,
            int channelCapacity = Constants.DefaultChannelCapacity
        )
        {
            _fftProcessor = fftProcessor
                ?? throw new ArgumentNullException(nameof(fftProcessor));
            _converter = converter
                ?? throw new ArgumentNullException(nameof(converter));
            _context = context;
            var options = new BoundedChannelOptions(channelCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true
            };
            _processingChannel = Channel.CreateBounded<(Complex[] Fft, int SampleRate)>(options);
            Task.Run(ProcessFftResultsAsync);
            _fftProcessor.FftCalculated += OnFftCalculated;
        }

        public SpectrumScale ScaleType
        {
            get => _scaleType;
            set
            {
                lock (_lock)
                {
                    if (_scaleType == value)
                        return;
                    _scaleType = value;
                    ResetSpectrum();
                }
            }
        }

        public void SetWindowType(FftWindowType windowType) =>
            SmartLogger.SafeExecute(() => {
                lock (_lock)
                {
                    if (_fftProcessor.WindowType == windowType)
                        return;
                    _fftProcessor.WindowType = windowType;
                    _fftProcessor.ResetFftState();
                    ResetSpectrum();
                }
            },
            new SmartLogger.ErrorHandlingOptions
            {
                Source = LogSource,
                ErrorMessage = $"Error setting window type: {windowType}"
            });

        public void SetScaleType(SpectrumScale scaleType) => ScaleType = scaleType;

        public void UpdateSettings(FftWindowType windowType, SpectrumScale scaleType) =>
            SmartLogger.SafeExecute(() => {
                lock (_lock)
                {
                    bool changed = false;
                    if (_fftProcessor.WindowType != windowType)
                    {
                        _fftProcessor.WindowType = windowType;
                        _fftProcessor.ResetFftState();
                        changed = true;
                    }
                    if (_scaleType != scaleType)
                    {
                        _scaleType = scaleType;
                        changed = true;
                    }
                    if (changed)
                        ResetSpectrum();
                }
            },
            new SmartLogger.ErrorHandlingOptions
            {
                Source = LogSource,
                ErrorMessage = "Error updating settings"
            });

        public SpectralData? GetCurrentSpectrum() =>
            _disposed ? throw new ObjectDisposedException(nameof(SpectrumAnalyzer))
                      : _lastData;

        public async Task AddSamplesAsync(ReadOnlyMemory<float> samples, int sampleRate,
            CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SpectrumAnalyzer));
            if (samples.Length == 0)
                return;
            using var linkedCts =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
            try
            {
                await _fftProcessor.AddSamplesAsync(samples, sampleRate,
                    linkedCts.Token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                SmartLogger.Log(LogLevel.Error, LogSource,
                    $"Error adding samples: {ex}");
                throw;
            }
        }

        public void SafeReset() => ResetSpectrum();

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _fftProcessor.FftCalculated -= OnFftCalculated;
            _cts.Cancel();
            _processingChannel.Writer.Complete();
            if (_fftProcessor is IAsyncDisposable ad)
                SmartLogger.SafeExecute(() =>
                    ad.DisposeAsync().AsTask().GetAwaiter().GetResult(),
                    new SmartLogger.ErrorHandlingOptions
                    {
                        Source = LogSource,
                        ErrorMessage = "Error disposing processor"
                    });
            SmartLogger.SafeExecute(() => {
                if (_context != null)
                    _context.Post(_ => Disposed?.Invoke(this, EventArgs.Empty),
                        null);
                else
                    Disposed?.Invoke(this, EventArgs.Empty);
            },
            new SmartLogger.ErrorHandlingOptions
            {
                Source = LogSource,
                ErrorMessage = "Error invoking Disposed event"
            });
            _cts.Dispose();
            GC.SuppressFinalize(this);
        }

        private async Task ProcessFftResultsAsync() =>
            await SmartLogger.SafeExecuteAsync(async () => {
                await foreach (var (fft, sampleRate) in
                    _processingChannel.Reader.ReadAllAsync(_cts.Token))
                {
                    if (_cts.IsCancellationRequested)
                        break;
                    try
                    {
                        var currentScale = _scaleType;
                        var spectrum = await Task.Run(() => {
                            _cts.Token.ThrowIfCancellationRequested();
                            return _converter.ConvertToSpectrum(
                                fft, sampleRate, currentScale, _cts.Token);
                        }, _cts.Token);
                        var data = new SpectralData(spectrum, DateTime.UtcNow);
                        lock (_lock)
                            _lastData = data;
                        if (_context != null)
                            _context.Post(_ => SpectralDataReady?.Invoke(
                                this, new SpectralDataEventArgs(data)), null);
                        else
                            SpectralDataReady?.Invoke(
                                this, new SpectralDataEventArgs(data));
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        SmartLogger.Log(LogLevel.Error, LogSource,
                            $"Error processing FFT result: {ex}");
                    }
                }
            },
            new SmartLogger.ErrorHandlingOptions
            {
                Source = LogSource,
                ErrorMessage = "Error in FFT results processing loop",
                IgnoreExceptions = new[] { typeof(OperationCanceledException) }
            });

        private void ResetSpectrum() =>
            SmartLogger.SafeExecute(() => {
                _lastData = null;
                var emptyData = new SpectralData(Array.Empty<float>(), DateTime.UtcNow);
                if (_context != null)
                    _context.Post(_ => SpectralDataReady?.Invoke(
                        this, new SpectralDataEventArgs(emptyData)), null);
                else
                    SpectralDataReady?.Invoke(
                        this, new SpectralDataEventArgs(emptyData));
            },
            new SmartLogger.ErrorHandlingOptions
            {
                Source = LogSource,
                ErrorMessage = "Error resetting spectrum"
            });

        private void OnFftCalculated(object? sender, FftEventArgs e) =>
            SmartLogger.SafeExecute(() => {
                if (_disposed || e.Result.Length == 0 || _cts.IsCancellationRequested)
                    return;
                if (!_processingChannel.Writer.TryWrite((e.Result, e.SampleRate)))
                    SmartLogger.Log(LogLevel.Warning, LogSource,
                        "Processing channel is full, dropping FFT result");
            },
            new SmartLogger.ErrorHandlingOptions
            {
                Source = LogSource,
                ErrorMessage = "Error handling FFT calculation event"
            });
    }

    public static class TrigonometricTables
    {
        private const string LogPrefix = "[TrigonometricTables]";
        private static readonly ConcurrentDictionary<int, (float[] Cos, float[] Sin)> _tables =
            new();

        public static (float[] Cos, float[] Sin) Get(int size) =>
            size <= 0
                ? throw new ArgumentException("Size must be positive", nameof(size))
                : _tables.GetOrAdd(size, CreateTrigTables);

        private static (float[] Cos, float[] Sin) CreateTrigTables(int size) =>
            SmartLogger.SafeResult(() => {
                var cos = new float[size];
                var sin = new float[size];
                Parallel.For(0, size, i => {
                    float angle = Constants.TwoPi * i / size;
                    cos[i] = MathF.Cos(angle);
                    sin[i] = MathF.Sin(angle);
                });
                return (cos, sin);
            },
            defaultValue: (new float[0], new float[0]),
            options: new SmartLogger.ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error creating trig tables"
            });
    }

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
                MaxDegreeOfParallelism = Environment.ProcessorCount
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
            await SmartLogger.SafeValueTaskResultAsync<bool>(async () => {
                _cts.Cancel();
                _channel.Writer.Complete();
                _threadLocalBuffer.Dispose();
                await Task.CompletedTask;
                return true;
            },
            defaultValue: false,
            options: new SmartLogger.ErrorHandlingOptions
            {
                Source = LogSource,
                ErrorMessage = "Error during disposal"
            });
            _cts.Dispose();
        }

        private float[] GenerateWindow(int size, FftWindowType type) =>
            SmartLogger.SafeResult(() => {
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
                        i => w[i] = 2f / (size - 1) * (((size - 1) / 2f) -
                            MathF.Abs(i - (size - 1) / 2f)),
                    FftWindowType.Kaiser =>
                        i => w[i] = BesselI0(Constants.KaiserBeta *
                            MathF.Sqrt(1 - MathF.Pow((2f * i / (size - 1) - 1), 2))) /
                            BesselI0(Constants.KaiserBeta),
                    _ => throw new NotSupportedException($"Unsupported window type: {type}")
                };
                Parallel.For(0, size, setWindow);
                return w;
            },
            defaultValue: new float[size],
            options: new SmartLogger.ErrorHandlingOptions
            {
                Source = LogSource,
                ErrorMessage = "Error generating window"
            });

        private static float BesselI0(float x)
        {
            float sum = 1f, term = (x * x) / 4f;
            for (int k = 1; term > Constants.BesselEpsilon; k++)
            {
                sum += term;
                term *= (x * x) / (4f * k * k);
            }
            return sum;
        }

        private async Task ProcessAsync() =>
            await SmartLogger.SafeExecuteAsync(async () => {
                await foreach (var (samples, rate, token) in
                    _channel.Reader.ReadAllAsync(_cts.Token))
                {
                    if (token.IsCancellationRequested)
                        continue;
                    if (samples.Length > 0)
                        await Task.Run(() => ProcessBatch(samples, rate, token), token);
                }
            },
            new SmartLogger.ErrorHandlingOptions
            {
                Source = LogSource,
                ErrorMessage = "Error in FFT processing loop",
                IgnoreExceptions = new[] { typeof(OperationCanceledException) }
            });

        private void ProcessBatch(ReadOnlyMemory<float> samples, int rate,
            CancellationToken cancellationToken) =>
            SmartLogger.SafeExecute(() => {
                int pos = 0;
                while (pos < samples.Length)
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;
                    int count = Math.Min(_fftSize - _sampleCount,
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
            new SmartLogger.ErrorHandlingOptions
            {
                Source = LogSource,
                ErrorMessage = "Error processing batch"
            });

        private void PerformFftAndNotify(int rate,
            CancellationToken cancellationToken) =>
            SmartLogger.SafeExecute(() => {
                Complex[] fftBuffer = _complexArrayPool.Rent(_fftSize);
                Array.Copy(_buffer, fftBuffer, _fftSize);
                Task.Run(() => {
                    try
                    {
                        if (cancellationToken.IsCancellationRequested)
                            return;
                        FastFourierTransform.FFT(true,
                            (int)Math.Log2(_fftSize), fftBuffer);
                        if (!cancellationToken.IsCancellationRequested &&
                            FftCalculated != null)
                            FftCalculated(this,
                                new FftEventArgs(fftBuffer, rate));
                    }
                    catch (Exception ex)
                    {
                        SmartLogger.Log(LogLevel.Error, LogSource,
                            $"FFT calculation failed: {ex}");
                    }
                    finally
                    {
                        _complexArrayPool.Return(fftBuffer);
                    }
                }, cancellationToken);
            },
            new SmartLogger.ErrorHandlingOptions
            {
                Source = LogSource,
                ErrorMessage = "Error preparing FFT"
            });

        private void ProcessChunk(ReadOnlyMemory<float> chunk) =>
            SmartLogger.SafeExecute(() => {
                int offset = _sampleCount;
                int len = chunk.Length;
                if (len > 1024)
                {
                    int chunkSize = Math.Max(1024, len / Environment.ProcessorCount);
                    Parallel.For(0, (len + chunkSize - 1) / chunkSize, _parallelOpts, i => {
                        int start = i * chunkSize;
                        int end = Math.Min(start + chunkSize, len);
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
            new SmartLogger.ErrorHandlingOptions
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

    public sealed class SpectrumConverter : ISpectrumConverter
    {
        private const string LogSource = nameof(SpectrumConverter);
        private readonly IGainParametersProvider _params;
        private readonly ParallelOptions _parallelOpts = new()
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount
        };
        private readonly ArrayPool<float> _floatArrayPool = ArrayPool<float>.Shared;

        public SpectrumConverter(IGainParametersProvider? parameters) =>
            _params = parameters
                ?? throw new ArgumentNullException(nameof(parameters));

        public float[] ConvertToSpectrum(Complex[] fft, int sampleRate,
            SpectrumScale scale, CancellationToken cancellationToken = default)
        {
            if (fft is null)
                throw new ArgumentNullException(nameof(fft));
            if (sampleRate <= 0)
                throw new ArgumentException("Invalid sample rate", nameof(sampleRate));
            int nBins = fft.Length / 2 + 1;
            float[] spectrum = _floatArrayPool.Rent(nBins);
            Array.Clear(spectrum, 0, nBins);
            SpectrumParameters spectrumParams =
                SpectrumParameters.FromProvider(_params);
            _parallelOpts.CancellationToken = cancellationToken;
            try
            {
                return scale switch
                {
                    SpectrumScale.Linear =>
                        ProcessLinear(fft, spectrum, nBins, spectrumParams),
                    SpectrumScale.Logarithmic =>
                        ProcessScale(fft, spectrum, nBins, sampleRate,
                            MathF.Log10(1f), MathF.Log10(sampleRate / 2f),
                            x => MathF.Pow(10, x), spectrumParams),
                    SpectrumScale.Mel =>
                        ProcessScale(fft, spectrum, nBins, sampleRate,
                            FreqToMel(1f), FreqToMel(sampleRate / 2f),
                            MelToFreq, spectrumParams),
                    SpectrumScale.Bark =>
                        ProcessScale(fft, spectrum, nBins, sampleRate,
                            FreqToBark(1f), FreqToBark(sampleRate / 2f),
                            BarkToFreq, spectrumParams),
                    SpectrumScale.ERB =>
                        ProcessScale(fft, spectrum, nBins, sampleRate,
                            FreqToERB(1f), FreqToERB(sampleRate / 2f),
                            ERBToFreq, spectrumParams),
                    _ => ProcessLinear(fft, spectrum, nBins, spectrumParams)
                };
            }
            catch
            {
                _floatArrayPool.Return(spectrum);
                throw;
            }
        }

        private float[] ProcessLinear(
            Complex[] fft,
            float[] spectrum,
            int nBins,
            SpectrumParameters spectrumParams
        ) =>
            SmartLogger.SafeResult(() => {
                if (nBins < 100)
                {
                    for (int i = 0; i < nBins; i++)
                    {
                        if (_parallelOpts.CancellationToken.IsCancellationRequested)
                            break;
                        spectrum[i] = InterpolateSpectrumValue(
                            fft, i, nBins, spectrumParams);
                    }
                }
                else
                {
                    Parallel.For(0, nBins, _parallelOpts, i =>
                        spectrum[i] = InterpolateSpectrumValue(
                            fft, i, nBins, spectrumParams));
                }
                float[] result = new float[nBins];
                Array.Copy(spectrum, result, nBins);
                _floatArrayPool.Return(spectrum);
                return result;
            },
            defaultValue: Array.Empty<float>(),
            options: new SmartLogger.ErrorHandlingOptions
            {
                Source = LogSource,
                ErrorMessage = "Error processing linear spectrum",
                IgnoreExceptions = new[] { typeof(OperationCanceledException) }
            });

        private float[] ProcessScale(
            Complex[] fft,
            float[] spectrum,
            int nBins,
            int sampleRate,
            float minDomain,
            float maxDomain,
            Func<float, float> domainToFreq,
            SpectrumParameters spectrumParams
        ) =>
            SmartLogger.SafeResult(() => {
                float step = (maxDomain - minDomain) / (nBins - 1);
                float halfSampleRate = sampleRate * 0.5f;
                Parallel.For(0, nBins, _parallelOpts, i => {
                    if (_parallelOpts.CancellationToken.IsCancellationRequested)
                        return;
                    float domainValue = minDomain + i * step;
                    float freq = domainToFreq(domainValue);
                    int bin = Math.Clamp(
                        (int)MathF.Round((freq / halfSampleRate) * (nBins - 1)),
                        0, nBins - 1);
                    spectrum[i] = CalcValue(Magnitude(fft[bin]), spectrumParams);
                });
                float[] result = new float[nBins];
                Array.Copy(spectrum, result, nBins);
                _floatArrayPool.Return(spectrum);
                return result;
            },
            defaultValue: Array.Empty<float>(),
            options: new SmartLogger.ErrorHandlingOptions
            {
                Source = LogSource,
                ErrorMessage = "Error processing non-linear spectrum",
                IgnoreExceptions = new[] { typeof(OperationCanceledException) }
            });

        private float InterpolateSpectrumValue(
            Complex[] fft,
            int index,
            int nBins,
            SpectrumParameters spectrumParams
        )
        {
            float centerMag = Magnitude(fft[index]);
            float leftMag = index > 0 ? Magnitude(fft[index - 1]) : centerMag;
            float rightMag = index < nBins - 1 ? Magnitude(fft[index + 1]) : centerMag;
            float interpolatedMag = (leftMag + centerMag + rightMag) / 3f;
            return interpolatedMag <= 0
                ? 0f
                : NormalizeDb(interpolatedMag, spectrumParams);
        }

        private static float CalcValue(
            float mag,
            SpectrumParameters spectrumParams
        ) =>
            mag <= 0f ? 0f : NormalizeDb(mag, spectrumParams);

        private static float NormalizeDb(
            float magnitude,
            SpectrumParameters spectrumParams
        )
        {
            float db = 10f * Constants.InvLog10 * MathF.Log(magnitude);
            float norm = Math.Clamp(
                (db - spectrumParams.MinDb) / spectrumParams.DbRange, 0f, 1f);
            return norm < 1e-6f ? 0f : MathF.Pow(norm, spectrumParams.AmplificationFactor);
        }

        private static float Magnitude(Complex c) =>
            c.X * c.X + c.Y * c.Y;

        private static float FreqToMel(float freq) =>
            2595f * MathF.Log10(1 + freq / 700f);

        private static float MelToFreq(float mel) =>
            700f * (MathF.Pow(10, mel / 2595f) - 1);

        private static float FreqToBark(float freq) =>
            13f * MathF.Atan(0.00076f * freq) +
            3.5f * MathF.Atan(MathF.Pow(freq / 7500f, 2));

        private static float BarkToFreq(float bark) =>
            1960f * (bark + 0.53f) / (26.28f - bark);

        private static float FreqToERB(float freq) =>
            21.4f * MathF.Log10(0.00437f * freq + 1);

        private static float ERBToFreq(float erb) =>
            (MathF.Pow(10, erb / 21.4f) - 1) / 0.00437f;
    }
}