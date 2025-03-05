#nullable enable
namespace SpectrumNet
{
    public static class Constants
    {
        public const float DefaultAmplificationFactor = 0.5f;
        public const float DefaultMaxDbValue = 0f;
        public const float DefaultMinDbValue = -130f;
        public const float Epsilon = float.Epsilon;
        public const float TwoPi = 2f * MathF.PI;
        public const float KaiserBeta = 5f;
        public const float BesselEpsilon = 1e-10f;
        public const float InvLog10 = 0.43429448190325182765f;
        public const int DefaultFftSize = 2048;
    }

    public enum FftWindowType { Hann, Hamming, Blackman, Bartlett, Kaiser }
    public enum SpectrumScale { Linear, Logarithmic, Mel, Bark, ERB }

    public interface IFftProcessor
    {
        event EventHandler<FftEventArgs>? FftCalculated;
        ValueTask AddSamplesAsync(ReadOnlyMemory<float> samples, int sampleRate);
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
        Task AddSamplesAsync(ReadOnlyMemory<float> samples, int sampleRate, CancellationToken cancellationToken = default);
    }

    public interface ISpectrumConverter
    {
        float[] ConvertToSpectrum(Complex[] fftResult, int sampleRate, SpectrumScale scale);
    }

    public class FftEventArgs : EventArgs
    {
        public Complex[] Result { get; }
        public int SampleRate { get; }
        public FftEventArgs(Complex[] result, int sampleRate)
        {
            Result = result ?? throw new ArgumentNullException(nameof(result));
            SampleRate = sampleRate;
        }
    }

    public class SpectralDataEventArgs : EventArgs
    {
        public SpectralData Data { get; }
        public SpectralDataEventArgs(SpectralData data) => Data = data;
    }

    public record SpectralData(float[] Spectrum, DateTime Timestamp);

    public struct SpectrumParameters
    {
        public float MinDb { get; }
        public float DbRange { get; }
        public float AmplificationFactor { get; }
        public SpectrumParameters(float minDb, float dbRange, float amplificationFactor) =>
            (MinDb, DbRange, AmplificationFactor) = (minDb, dbRange, amplificationFactor);
        public static SpectrumParameters FromProvider(IGainParametersProvider? provider)
        {
            if (provider is null)
                throw new ArgumentNullException(nameof(provider));

            return new SpectrumParameters(
                provider.MinDbValue,
                Math.Max(provider.MaxDbValue - provider.MinDbValue, Constants.Epsilon),
                provider.AmplificationFactor
            );
        }
    }

    public sealed class GainParameters : IGainParametersProvider, INotifyPropertyChanged
    {
        private float _amp = Constants.DefaultAmplificationFactor;
        private float _min = Constants.DefaultMinDbValue;
        private float _max = Constants.DefaultMaxDbValue;
        private readonly SynchronizationContext? _context;
        public event PropertyChangedEventHandler? PropertyChanged;

        public GainParameters(
            SynchronizationContext? context = null,
            float minDbValue = Constants.DefaultMinDbValue,
            float maxDbValue = Constants.DefaultMaxDbValue,
            float amplificationFactor = Constants.DefaultAmplificationFactor)
        {
            if (minDbValue > maxDbValue)
                throw new ArgumentException("MinDbValue cannot be greater than MaxDbValue.");
            _context = context;
            _min = minDbValue;
            _max = maxDbValue;
            _amp = amplificationFactor;
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

        private void UpdateProperty(ref float field, float value, [CallerMemberName] string? propertyName = null)
        {
            if (Math.Abs(field - value) > Constants.Epsilon)
            {
                field = value;
                if (_context != null)
                    _context.Post(_ => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)), null);
                else
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }

    public sealed class SpectrumAnalyzer : ISpectralDataProvider, IDisposable, IComponent
    {
        private readonly IFftProcessor _fftProcessor;
        private readonly ISpectrumConverter _converter;
        private readonly SynchronizationContext? _context;
        private SpectralData? _lastData;
        private SpectrumScale _scaleType = SpectrumScale.Linear;
        private bool _disposed;
        private readonly object _lock = new();

        public event EventHandler<SpectralDataEventArgs>? SpectralDataReady;
        public event EventHandler? Disposed;
        public ISite? Site { get; set; }

        public SpectrumAnalyzer(
            IFftProcessor fftProcessor,
            ISpectrumConverter converter,
            SynchronizationContext? context = null)
        {
            _fftProcessor = fftProcessor ?? throw new ArgumentNullException(nameof(fftProcessor));
            _converter = converter ?? throw new ArgumentNullException(nameof(converter));
            _context = context;
            _fftProcessor.FftCalculated += OnFftCalculated;
        }

        public SpectrumScale ScaleType
        {
            get => _scaleType;
            set
            {
                lock (_lock)
                {
                    if (_scaleType != value)
                    {
                        _scaleType = value;
                        ResetSpectrum();
                    }
                }
            }
        }

        public void SetWindowType(FftWindowType windowType)
        {
            lock (_lock)
            {
                if (_fftProcessor.WindowType != windowType)
                {
                    _fftProcessor.WindowType = windowType;
                    _fftProcessor.ResetFftState();
                    ResetSpectrum();
                }
            }
        }

        public void SetScaleType(SpectrumScale scaleType) => ScaleType = scaleType;

        public void UpdateSettings(FftWindowType windowType, SpectrumScale scaleType)
        {
            lock (_lock)
            {
                bool changed = false;
                if (_scaleType != scaleType)
                {
                    _scaleType = scaleType;
                    changed = true;
                }
                if (_fftProcessor.WindowType != windowType)
                {
                    _fftProcessor.WindowType = windowType;
                    _fftProcessor.ResetFftState();
                    changed = true;
                }
                if (changed)
                    ResetSpectrum();
            }
        }

        public SpectralData? GetCurrentSpectrum()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SpectrumAnalyzer));
            return _lastData;
        }

        public async Task AddSamplesAsync(ReadOnlyMemory<float> samples, int sampleRate, CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SpectrumAnalyzer));
            if (samples.Length == 0)
                return;
            try
            {
                await _fftProcessor.AddSamplesAsync(samples, sampleRate);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, "[SpectrumAnalyzer]", $"Error adding samples: {ex}");
                throw;
            }
        }

        private void ResetSpectrum()
        {
            _lastData = null;
            Helpers.InvokeEvent(SpectralDataReady, this,
                new SpectralDataEventArgs(new SpectralData(Array.Empty<float>(), DateTime.UtcNow)),
                _context);
        }

        // Public wrapper method for safe spectrum reset.
        public void SafeReset() => ResetSpectrum();

        private void OnFftCalculated(object? sender, FftEventArgs e)
        {
            if (e.Result.Length == 0)
                return;
            float[] spectrum;
            try
            {
                spectrum = _converter.ConvertToSpectrum(e.Result, e.SampleRate, _scaleType);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, "[SpectrumAnalyzer]", $"Error converting FFT to spectrum: {ex}");
                return;
            }
            var data = new SpectralData(spectrum, DateTime.UtcNow);
            lock (_lock)
            {
                _lastData = data;
            }
            Helpers.InvokeEvent(SpectralDataReady, this, new SpectralDataEventArgs(data), _context);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _fftProcessor.FftCalculated -= OnFftCalculated;
            if (_fftProcessor is IAsyncDisposable ad)
            {
                try
                {
                    ad.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    SmartLogger.Log(LogLevel.Error, "[SpectrumAnalyzer]", $"Error disposing FFT processor: {ex}");
                }
            }
            _disposed = true;
            Helpers.InvokeEvent(Disposed, this, EventArgs.Empty, _context);
            GC.SuppressFinalize(this);
        }
    }

    internal static class Helpers
    {
        private const string LogPrefix = "[Helpers] ";
        public static void InvokeEvent<T>(EventHandler<T>? handler, object sender, T args, SynchronizationContext? context = null)
        {
            try
            {
                if (context != null)
                    context.Post(_ => handler?.Invoke(sender, args), null);
                else
                    handler?.Invoke(sender, args);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error invoking event: {ex}");
            }
        }

        public static void InvokeEvent(EventHandler? handler, object sender, EventArgs args, SynchronizationContext? context = null)
        {
            try
            {
                if (context != null)
                    context.Post(_ => handler?.Invoke(sender, args), null);
                else
                    handler?.Invoke(sender, args);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error invoking event: {ex}");
            }
        }
    }

    public static class TrigonometricTables
    {
        private const string LogPrefix = "[TrigonometricTables] ";
        private static readonly ConcurrentDictionary<int, (float[] Cos, float[] Sin)> _tables = new();
        public static (float[] Cos, float[] Sin) Get(int size)
        {
            if (size <= 0)
                throw new ArgumentException("Size must be positive", nameof(size));
            return _tables.GetOrAdd(size, s =>
            {
                var cos = new float[s];
                var sin = new float[s];
                Parallel.For(0, s, i =>
                {
                    float a = Constants.TwoPi * i / s;
                    cos[i] = MathF.Cos(a);
                    sin[i] = MathF.Sin(a);
                });
                return (cos, sin);
            });
        }
    }

    public sealed class FftProcessor : IFftProcessor, IAsyncDisposable
    {
        private readonly int _fftSize;
        private readonly Complex[] _buffer;
        private readonly Channel<(ReadOnlyMemory<float> Samples, int SampleRate)> _channel;
        private readonly CancellationTokenSource _cts = new();
        private readonly Dictionary<FftWindowType, float[]> _windows;
        private readonly float[] _cosCache;
        private readonly float[] _sinCache;
        private readonly int _vecSize = Vector<float>.Count;
        private readonly ParallelOptions _parallelOpts;
        private float[] _window;
        private int _sampleCount;
        private bool _disposed;
        private FftWindowType _windowType = FftWindowType.Hann;
        public event EventHandler<FftEventArgs>? FftCalculated;

        public FftProcessor(int fftSize = Constants.DefaultFftSize)
        {
            if (!BitOperations.IsPow2(fftSize) || fftSize <= 0)
                throw new ArgumentException("FFT size must be a positive power of 2", nameof(fftSize));
            _fftSize = fftSize;
            _buffer = new Complex[fftSize];
            (_cosCache, _sinCache) = TrigonometricTables.Get(fftSize);
            _windows = new Dictionary<FftWindowType, float[]>();
            foreach (FftWindowType type in Enum.GetValues(typeof(FftWindowType)))
            {
                _windows[type] = GenerateWindow(fftSize, type);
            }
            _window = _windows[_windowType];
            _channel = Channel.CreateUnbounded<(ReadOnlyMemory<float>, int)>(
                new UnboundedChannelOptions { SingleReader = true });
            _parallelOpts = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };
            Task.Run(ProcessAsync);
        }

        public FftWindowType WindowType
        {
            get => _windowType;
            set
            {
                if (_windowType == value)
                    return;
                _windowType = value;
                if (!_windows.TryGetValue(value, out var window) || window is null)
                    throw new InvalidOperationException($"Window type {value} not found or is null.");
                _window = window;
                ResetFftState();
            }
        }

        public ValueTask AddSamplesAsync(ReadOnlyMemory<float> samples, int sampleRate)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(FftProcessor));
            if (sampleRate <= 0)
                throw new ArgumentException("Sample rate must be greater than zero.", nameof(sampleRate));
            if (samples.Length == 0)
                return ValueTask.CompletedTask;
            return _channel.Writer.TryWrite((samples, sampleRate))
                ? ValueTask.CompletedTask
                : new ValueTask(_channel.Writer.WriteAsync((samples, sampleRate)).AsTask());
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
            _cts.Cancel();
            _channel.Writer.Complete();
            try
            {
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, "[FftProcessor]", $"Error during disposal: {ex}");
            }
            finally
            {
                _cts.Dispose();
            }
        }

        private float[] GenerateWindow(int size, FftWindowType type)
        {
            float[] w = new float[size];
            Action<int> set = type switch
            {
                FftWindowType.Hann => i => w[i] = 0.5f * (1f - _cosCache[i]),
                FftWindowType.Hamming => i => w[i] = 0.54f - 0.46f * _cosCache[i],
                FftWindowType.Blackman => i => w[i] = 0.42f - 0.5f * _cosCache[i] + 0.08f * MathF.Cos(Constants.TwoPi * 2 * i / (size - 1)),
                FftWindowType.Bartlett => i => w[i] = 2f / (size - 1) * (((size - 1) / 2f) - MathF.Abs(i - (size - 1) / 2f)),
                FftWindowType.Kaiser => i => w[i] = KaiserWindow(i, size, Constants.KaiserBeta),
                _ => throw new NotSupportedException($"Unsupported window type: {type}")
            };
            Parallel.For(0, size, set);
            return w;
        }

        private static float KaiserWindow(int i, int size, float beta)
        {
            float a = (size - 1) / 2f;
            float t = (i - a) / a;
            return BesselI0(beta * MathF.Sqrt(1 - t * t)) / BesselI0(beta);
        }

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

        private async Task ProcessAsync()
        {
            try
            {
                await foreach (var (samples, rate) in _channel.Reader.ReadAllAsync(_cts.Token))
                {
                    if (samples.Length > 0)
                        ProcessBatch(samples, rate);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, "[FftProcessor]", $"Error in FFT processing loop: {ex}");
            }
        }

        private void ProcessBatch(ReadOnlyMemory<float> samples, int rate)
        {
            int pos = 0;
            while (pos < samples.Length)
            {
                int count = Math.Min(_fftSize - _sampleCount, samples.Length - pos);
                if (count <= 0)
                    break;
                ProcessChunk(samples.Slice(pos, count));
                pos += count;
                _sampleCount += count;
                if (_sampleCount >= _fftSize)
                {
                    try
                    {
                        // FastFourierTransform.FFT реализован через NAudio.Dsp
                        FastFourierTransform.FFT(true, (int)Math.Log2(_fftSize), _buffer);
                    }
                    catch (Exception ex)
                    {
                        SmartLogger.Log(LogLevel.Error, "[FftProcessor]", $"FFT calculation failed: {ex}");
                        ResetFftState();
                        return;
                    }
                    Helpers.InvokeEvent(FftCalculated, this, new FftEventArgs(_buffer, rate));
                    ResetFftState();
                }
            }
        }

        private void ProcessChunk(ReadOnlyMemory<float> chunk)
        {
            int offset = _sampleCount;
            int len = chunk.Length;
            int vecEnd = len - len % _vecSize;
            Span<float> temp = stackalloc float[_vecSize];
            for (int i = 0; i < vecEnd; i += _vecSize)
            {
                chunk.Span.Slice(i, _vecSize).CopyTo(temp);
                Vector<float> s = new(temp);
                Vector<float> w = new(_window, offset + i);
                (s * w).CopyTo(temp);
                for (int j = 0; j < _vecSize; j++)
                    _buffer[offset + i + j] = new Complex { X = temp[j] };
            }
            for (int i = vecEnd; i < len; i++)
            {
                _buffer[offset + i] = new Complex { X = chunk.Span[i] * _window[offset + i] };
            }
        }
    }

    public sealed class SpectrumConverter : ISpectrumConverter
    {
        private readonly IGainParametersProvider _params;
        private readonly ParallelOptions _parallelOpts = new() { MaxDegreeOfParallelism = Environment.ProcessorCount };

        public SpectrumConverter(IGainParametersProvider? parameters) =>
            _params = parameters ?? throw new ArgumentNullException(nameof(parameters));

        public float[] ConvertToSpectrum(Complex[] fft, int sampleRate, SpectrumScale scale)
        {
            if (fft is null)
                throw new ArgumentNullException(nameof(fft));
            if (sampleRate <= 0)
                throw new ArgumentException("Invalid sample rate", nameof(sampleRate));

            int nBins = fft.Length / 2 + 1;
            float[] spectrum = new float[nBins];

            SpectrumParameters spectrumParams = SpectrumParameters.FromProvider(_params);

            switch (scale)
            {
                case SpectrumScale.Linear:
                    ProcessLinear(fft, spectrum, nBins, spectrumParams);
                    break;
                case SpectrumScale.Logarithmic:
                    ProcessScale(fft, spectrum, nBins, sampleRate,
                                                    minDomain: MathF.Log10(1f),
                                                    maxDomain: MathF.Log10(sampleRate / 2f),
                                                    domainToFreq: x => MathF.Pow(10, x),
                                                    spectrumParams);
                    break;
                case SpectrumScale.Mel:
                    ProcessScale(fft, spectrum, nBins, sampleRate,
                                                    minDomain: FreqToMel(1f),
                                                    maxDomain: FreqToMel(sampleRate / 2f),
                                                    domainToFreq: MelToFreq,
                                                    spectrumParams);
                    break;
                case SpectrumScale.Bark:
                    ProcessScale(fft, spectrum, nBins, sampleRate,
                                                    minDomain: FreqToBark(1f),
                                                    maxDomain: FreqToBark(sampleRate / 2f),
                                                    domainToFreq: BarkToFreq,
                                                    spectrumParams);
                    break;
                case SpectrumScale.ERB:
                    ProcessScale(fft, spectrum, nBins, sampleRate,
                                                    minDomain: FreqToERB(1f),
                                                    maxDomain: FreqToERB(sampleRate / 2f),
                                                    domainToFreq: ERBToFreq,
                                                    spectrumParams);
                    break;
                default:
                    ProcessLinear(fft, spectrum, nBins, spectrumParams);
                    break;
            }
            return spectrum;
        }

        /// <summary>
        /// Linear processing: simple interpolation by indices from 0 to nBins-1.
        /// </summary>
        private void ProcessLinear(Complex[] fft, float[] spectrum, int nBins, SpectrumParameters spectrumParams)
        {
            if (nBins < 100)
            {
                for (int i = 0; i < nBins; i++)
                {
                    spectrum[i] = InterpolateSpectrumValue(fft, i, nBins, spectrumParams);
                }
            }
            else
            {
                Parallel.For(0, nBins, _parallelOpts, i =>
                {
                    spectrum[i] = InterpolateSpectrumValue(fft, i, nBins, spectrumParams);
                });
            }
        }

        /// <summary>
        /// Processing of non-linear scales (Log, Mel, Bark, ERB).
        /// Converts the domain value to frequency, then normalizes it to the index range [0, nBins-1].
        /// </summary>
        private void ProcessScale(
            Complex[] fft,
            float[] spectrum,
            int nBins,
            int sampleRate,
            float minDomain,
            float maxDomain,
            Func<float, float> domainToFreq,
            SpectrumParameters spectrumParams)
        {
            float step = (maxDomain - minDomain) / (nBins - 1);
            float halfSampleRate = sampleRate * 0.5f;
            Parallel.For(0, nBins, _parallelOpts, i =>
            {
                float domainValue = minDomain + i * step;
                float freq = domainToFreq(domainValue);
                float binFloat = (freq / halfSampleRate) * (nBins - 1);
                int bin = (int)MathF.Round(binFloat);
                bin = Math.Clamp(bin, 0, nBins - 1);

                float mag = Magnitude(fft[bin]);
                spectrum[i] = CalcValue(mag, spectrumParams);
            });
        }

        /// <summary>
        /// Amplitude interpolation: averages the value of the current, left, and right bins for smoothing.
        /// </summary>
        private float InterpolateSpectrumValue(Complex[] fft, int index, int nBins, SpectrumParameters spectrumParams)
        {
            float centerMag = Magnitude(fft[index]);
            float leftMag = index > 0 ? Magnitude(fft[index - 1]) : centerMag;
            float rightMag = index < nBins - 1 ? Magnitude(fft[index + 1]) : centerMag;
            float interpolatedMag = (leftMag + centerMag + rightMag) / 3f;
            if (interpolatedMag <= 0)
                return 0f;
            // Calculate the coefficient for conversion to dB
            float dBFactor = 10f * Constants.InvLog10;
            float db = dBFactor * MathF.Log(interpolatedMag);
            float norm = Math.Clamp((db - spectrumParams.MinDb) / spectrumParams.DbRange, 0f, 1f);
            return norm < 1e-6f ? 0f : MathF.Pow(norm, spectrumParams.AmplificationFactor);
        }

        /// <summary>
        /// Converts magnitude (amplitude) to dB, normalizes it, and applies a gain factor.
        /// </summary>
        private static float CalcValue(float mag, SpectrumParameters spectrumParams)
        {
            if (mag <= 0f)
                return 0f;
            float dBFactor = 10f * Constants.InvLog10;
            float db = dBFactor * MathF.Log(mag);
            float norm = Math.Clamp((db - spectrumParams.MinDb) / spectrumParams.DbRange, 0f, 1f);
            return norm < 1e-6f ? 0f : MathF.Pow(norm, spectrumParams.AmplificationFactor);
        }

        /// <summary>
        /// Calculates the magnitude (squared modulus) of a complex number.
        /// </summary>
        private static float Magnitude(Complex c) => c.X * c.X + c.Y * c.Y;

        // ---------------- Frequency conversion methods (Mel, Bark, ERB) ----------------
        private static float FreqToMel(float freq) => 2595f * MathF.Log10(1 + freq / 700f);
        private static float MelToFreq(float mel) => 700f * (MathF.Pow(10, mel / 2595f) - 1);
        private static float FreqToBark(float freq) => 13f * MathF.Atan(0.00076f * freq) + 3.5f * MathF.Atan(MathF.Pow(freq / 7500f, 2));
        private static float BarkToFreq(float bark) => 1960f * (bark + 0.53f) / (26.28f - bark);
        private static float FreqToERB(float freq) => 21.4f * MathF.Log10(0.00437f * freq + 1);
        private static float ERBToFreq(float erb) => (MathF.Pow(10, erb / 21.4f) - 1) / 0.00437f;
    }
}