using Complex = NAudio.Dsp.Complex;
using Vector = System.Numerics.Vector;

#nullable enable

namespace SpectrumNet
{
    public static class Constants
    {
        public const float DefaultAmplificationFactor = 0.5f;
        public const float DefaultMaxDbValue = 0f;           // Максимально громкий сигнал отображается до
        public const float DefaultMinDbValue = -130f;          // Игнорируем шум ниже 
        public const int DefaultFftSize = 2048;
        public const float Epsilon = float.Epsilon;
    }

    public interface IFftProcessor
    {
        event EventHandler<FftEventArgs>? FftCalculated;
        ValueTask AddSamplesAsync(Memory<float> samples, int sampleRate);
        ValueTask DisposeAsync();
        FftWindowType WindowType { get; set; }
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
        Task AddSamplesAsync(float[] samples, int sampleRate, CancellationToken cancellationToken = default);
    }

    public interface ISpectrumConverter
    {
        float[] ConvertToSpectrum(Complex[] fftResult, int sampleRate);
    }

    public readonly struct FftEventArgs
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
        public SpectralDataEventArgs(SpectralData data) => Data = data ?? throw new ArgumentNullException(nameof(data));
    }

    public record SpectralData(float[] Spectrum, DateTime Timestamp);

    public sealed class GainParameters : IGainParametersProvider, INotifyPropertyChanged
    {
        private float _amp = Constants.DefaultAmplificationFactor;
        private float _min = Constants.DefaultMinDbValue;
        private float _max = Constants.DefaultMaxDbValue;
        private readonly SynchronizationContext? _context;

        public event PropertyChangedEventHandler? PropertyChanged;

        public GainParameters(SynchronizationContext? context = null) => _context = context;

        public float AmplificationFactor
        {
            get => _amp;
            set => UpdateProperty(ref _amp, Math.Max(0.1f, value));
        }

        public float MaxDbValue
        {
            get => _max;
            set => UpdateProperty(ref _max, Math.Max(value, _min));
        }

        public float MinDbValue
        {
            get => _min;
            set => UpdateProperty(ref _min, Math.Min(value, _max));
        }

        private void UpdateProperty(ref float field, float value, [CallerMemberName] string? propertyName = null)
        {
            if (Math.Abs(field - value) > Constants.Epsilon)
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }

    public class SpectrumAnalyzer : ISpectralDataProvider, IDisposable
    {
        #region Fields
        private readonly IFftProcessor _fftProcessor;
        private readonly ISpectrumConverter _converter;
        private readonly SynchronizationContext? _context;
        private SpectralData? _lastData;
        private bool _disposed;
        #endregion

        #region Constructor
        public SpectrumAnalyzer(IFftProcessor fftProcessor, ISpectrumConverter converter, SynchronizationContext? context = null)
        {
            try
            {
                _fftProcessor = fftProcessor ?? throw new ArgumentNullException(nameof(fftProcessor));
                _converter = converter ?? throw new ArgumentNullException(nameof(converter));
                _context = context;
                _fftProcessor.FftCalculated += OnFftCalculated;
            }
            catch (Exception ex)
            {
                Log.Error($"[SpectrumAnalyzer] Error during constructor initialization: {ex}");
                throw;
            }
        }
        #endregion

        #region Properties
        public IFftProcessor FftProcessor => _fftProcessor;
        #endregion

        #region Events
        public event EventHandler<SpectralDataEventArgs>? SpectralDataReady;
        #endregion

        #region Public Methods
        public SpectralData? GetCurrentSpectrum()
        {
            try
            {
                if (_disposed) throw new ObjectDisposedException(nameof(SpectrumAnalyzer));
                return _lastData;
            }
            catch (Exception ex)
            {
                Log.Error($"[SpectrumAnalyzer] Error getting current spectrum: {ex}");
                throw;
            }
        }

        public async Task AddSamplesAsync(float[] samples, int sampleRate, CancellationToken cancellationToken = default)
        {
            try
            {
                if (_disposed) throw new ObjectDisposedException(nameof(SpectrumAnalyzer));
                if (samples?.Length > 0)
                    await _fftProcessor.AddSamplesAsync(samples, sampleRate).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Error($"[SpectrumAnalyzer] Error adding samples: {ex}");
                throw;
            }
        }
        #endregion

        #region Private Methods
        private void OnFftCalculated(object? sender, FftEventArgs e)
        {
            try
            {
                if (e.Result?.Length > 0)
                {
                    var spectrum = _converter.ConvertToSpectrum(e.Result, e.SampleRate);
                    var data = new SpectralData(spectrum, DateTime.UtcNow);
                    _lastData = data;

                    if (_context != null)
                        _context.Post(_ => SpectralDataReady?.Invoke(this, new SpectralDataEventArgs(data)), null);
                    else
                        SpectralDataReady?.Invoke(this, new SpectralDataEventArgs(data));
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[SpectrumAnalyzer] Error processing FFT result: {ex}");
            }
        }
        #endregion

        #region Dispose Method
        public void Dispose()
        {
            try
            {
                if (!_disposed)
                {
                    _fftProcessor.FftCalculated -= OnFftCalculated;
                    if (_fftProcessor is IAsyncDisposable disposable)
                        disposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    _disposed = true;
                }
                GC.SuppressFinalize(this);
            }
            catch (Exception ex)
            {
                Log.Error($"[SpectrumAnalyzer] Error during disposal: {ex}");
            }
        }
        #endregion
    }

    public enum FftWindowType
    {
        Hann,
        Hamming,
        Blackman,
        Bartlett,
        Kaiser
    }

    public sealed class FftProcessor : IFftProcessor, IAsyncDisposable
    {
        #region Constants
        private const float TWO_PI = 2f * MathF.PI;
        private const float KAISER_BETA = 5f;
        private const float BESSEL_EPSILON = 1e-10f;
        #endregion

        #region Fields
        private readonly int _fftSize;
        private readonly Complex[] _buffer;
        private readonly float[] _window;
        private readonly Channel<(float[] Samples, int SampleRate)> _channel;
        private readonly CancellationTokenSource _cts;
        private readonly ArrayPool<float> _pool;
        private readonly Task _processTask;
        private readonly int _vecSize;
        private readonly float[] _cosCache;
        private readonly float[] _sinCache;
        private readonly ParallelOptions _parallelOpts;

        private int _sampleCount;
        private bool _disposed;
        #endregion

        #region Constructor
        public FftProcessor(int fftSize = Constants.DefaultFftSize)
        {
            ValidateFftSize(fftSize);

            _fftSize = fftSize;
            _buffer = new Complex[fftSize];
            _pool = ArrayPool<float>.Shared;
            _vecSize = Vector<float>.Count;
            _parallelOpts = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };

            (_cosCache, _sinCache) = PrecomputeTrigonometricValues(fftSize);
            _window = GenerateWindow(fftSize);

            _channel = CreateUnboundedChannel();
            _cts = new CancellationTokenSource();
            _processTask = Task.Run(ProcessAsync);
        }

        private static void ValidateFftSize(int fftSize)
        {
            if (!BitOperations.IsPow2(fftSize) || fftSize <= 0)
                throw new ArgumentException("FFT size must be a positive power of 2.");
        }

        private static Channel<(float[], int)> CreateUnboundedChannel()
        {
            return Channel.CreateUnbounded<(float[], int)>(new UnboundedChannelOptions
            {
                SingleReader = true,
                AllowSynchronousContinuations = true
            });
        }
        #endregion

        #region Events and Properties
        public event EventHandler<FftEventArgs>? FftCalculated;
        public FftWindowType WindowType { get; set; } = FftWindowType.Hann;
        #endregion

        #region Public Methods
        public ValueTask AddSamplesAsync(Memory<float> samples, int sampleRate)
        {
            ValidateInput(samples, sampleRate);

            return _channel.Writer.TryWrite((samples.ToArray(), sampleRate))
                ? ValueTask.CompletedTask
                : new ValueTask(_channel.Writer.WriteAsync((samples.ToArray(), sampleRate)).AsTask());
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                _cts.Cancel();
                _channel.Writer.Complete();
                await _processTask.ConfigureAwait(false);
            }
            finally
            {
                _cts.Dispose();
                ClearArrays();
            }
        }
        #endregion

        #region Private Helper Methods

        private void ValidateInput(Memory<float> samples, int sampleRate)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(FftProcessor));
            ArgumentNullException.ThrowIfNull(samples);
            if (sampleRate <= 0)
                throw new ArgumentException($"Invalid sample rate: {sampleRate}", nameof(sampleRate));
        }

        private void ClearArrays()
        {
            Array.Clear(_buffer, 0, _buffer.Length);
            Array.Clear(_window, 0, _window.Length);
            ArrayPool<float>.Shared.Return(_cosCache, clearArray: true);
            ArrayPool<float>.Shared.Return(_sinCache, clearArray: true);
        }

        private static (float[] cos, float[] sin) PrecomputeTrigonometricValues(int size)
        {
            var cos = ArrayPool<float>.Shared.Rent(size);
            var sin = ArrayPool<float>.Shared.Rent(size);
            float invSize = TWO_PI / (size - 1);

            ComputeTrigonometricValuesVectorized(cos, sin, invSize, size);
            return (cos, sin);
        }

        private static void ComputeTrigonometricValuesVectorized(float[] cos, float[] sin, float invSize, int size)
        {
            int vecSize = Vector<float>.Count;
            int vecCount = size / vecSize;
            var indexVector = new Vector<float>(Enumerable.Range(0, vecSize).Select(i => (float)i).ToArray());

            Parallel.For(0, vecCount, i =>
            {
                int offset = i * vecSize;
                var indices = indexVector + new Vector<float>(offset);
                var angles = indices * invSize;

                for (int j = 0; j < vecSize; j++)
                {
                    cos[offset + j] = MathF.Cos(angles[j]);
                    sin[offset + j] = MathF.Sin(angles[j]);
                }
            });

            for (int i = vecCount * vecSize; i < size; i++)
            {
                float angle = invSize * i;
                cos[i] = MathF.Cos(angle);
                sin[i] = MathF.Sin(angle);
            }
        }

        private float[] GenerateWindow(int size)
        {
            var window = ArrayPool<float>.Shared.Rent(size);
            var windowFunc = GetWindowFunction(size);

            ApplyWindowFunction(window, size, windowFunc);
            return window;
        }

        private Func<int, float> GetWindowFunction(int size)
        {
            return WindowType switch
            {
                FftWindowType.Hann => i => 0.5f * (1f - _cosCache[i]),
                FftWindowType.Hamming => i => 0.54f - 0.46f * _cosCache[i],
                FftWindowType.Blackman => i => 0.42f - 0.5f * _cosCache[i] + 0.08f * MathF.Cos(TWO_PI * 2 * i / (size - 1)),
                FftWindowType.Bartlett => i => 2f / (size - 1) * ((size - 1) / 2f - MathF.Abs(i - (size - 1) / 2f)),
                FftWindowType.Kaiser => i => KaiserWindow(i, size, KAISER_BETA),
                _ => throw new NotSupportedException($"Window type {WindowType} is not supported.")
            };
        }

        private void ApplyWindowFunction(float[] window, int size, Func<int, float> windowFunc)
        {
            int vecSize = Vector<float>.Count;
            int vecCount = size / vecSize;

            Parallel.For(0, vecCount, i =>
            {
                int offset = i * vecSize;
                var indices = new Vector<float>(_cosCache.AsSpan(offset, vecSize));
                var result = new Vector<float>(Enumerable.Range(offset, vecSize).Select(windowFunc).ToArray());
                result.CopyTo(window, offset);
            });

            for (int i = vecCount * vecSize; i < size; i++)
            {
                window[i] = windowFunc(i);
            }
        }

        private static float KaiserWindow(int i, int size, float beta)
        {
            float a = (size - 1) / 2f;
            float t = (i - a) / a;
            return BesselI0(beta * MathF.Sqrt(1 - t * t)) / BesselI0(beta);
        }

        private static float BesselI0(float x)
        {
            if (x == 0f) return 1f;
            float sum = 1f, term = (x * x) / 4f;
            for (int k = 1; term > BESSEL_EPSILON; k++)
            {
                sum += term;
                term *= (x * x) / (4f * k * k);
            }
            return sum;
        }

        #endregion

        #region Processing Methods
        private async Task ProcessAsync()
        {
            try
            {
                await foreach (var (samples, rate) in _channel.Reader.ReadAllAsync(_cts.Token))
                {
                    ProcessBatch(samples, rate);
                }
            }
            catch (OperationCanceledException) { }
        }

        private void ProcessBatch(float[] samples, int rate)
        {
            int pos = 0;
            while (pos < samples.Length)
            {
                int count = Math.Min(_fftSize - _sampleCount, samples.Length - pos);
                if (count <= 0) break;

                ProcessChunk(samples.AsSpan(pos, count));
                pos += count;
                _sampleCount += count;

                if (_sampleCount >= _fftSize)
                {
                    PerformFftCalculation(rate);
                    _sampleCount = 0;
                }
            }
        }

        private void ProcessChunk(ReadOnlySpan<float> chunk)
        {
            var temp = ArrayPool<float>.Shared.Rent(_vecSize);
            try
            {
                int vecCount = chunk.Length / _vecSize;
                ProcessVectorized(chunk, temp, vecCount);
                ProcessRemaining(chunk, vecCount * _vecSize, chunk.Length % _vecSize);
            }
            finally
            {
                ArrayPool<float>.Shared.Return(temp);
            }
        }

        private void ProcessVectorized(ReadOnlySpan<float> data, float[] temp, int vecCount)
        {
            for (int i = 0; i < vecCount; i++)
            {
                int offset = i * _vecSize;
                data.Slice(offset, _vecSize).CopyTo(temp);
                var sampleVec = new Vector<float>(temp);
                var windowVec = new Vector<float>(_window.AsSpan(_sampleCount + offset, _vecSize));
                var resultVec = sampleVec * windowVec;

                for (int j = 0; j < _vecSize; j++)
                {
                    _buffer[_sampleCount + offset + j] = new Complex { X = resultVec[j] };
                }
            }
        }

        private void ProcessRemaining(ReadOnlySpan<float> data, int start, int count)
        {
            for (int i = 0; i < count; i++)
            {
                int idx = _sampleCount + start + i;
                _buffer[idx] = new Complex { X = data[start + i] * _window[idx] };
            }
        }

        private void PerformFftCalculation(int rate)
        {
            if (FftCalculated != null)
            {
                FastFourierTransform.FFT(true, (int)Math.Log2(_fftSize), _buffer);
                FftCalculated?.Invoke(this, new FftEventArgs(_buffer, rate));
            }
        }
        #endregion
    }

    public sealed class SpectrumConverter : ISpectrumConverter
    {
        #region Constants
        private const float INV_LOG10 = 0.43429448190325182765f;
        private const float MIN_FREQ = 20f;
        private const float MAX_FREQ = 24000f;
        #endregion

        #region Fields
        private readonly IGainParametersProvider _params;
        private readonly ArrayPool<float> _pool;
        private readonly int _vectorSize;
        private readonly Vector<float> _epsilonVec;
        private readonly Vector<float> _tenVec;
        private readonly Vector<float> _oneVec;
        private readonly Vector<float> _zeroVec;
        private readonly Vector<float> _invLog10Vec;
        private readonly ParallelOptions _parallelOpts;
        #endregion

        #region Constructor
        public SpectrumConverter(IGainParametersProvider parameters)
        {
            _params = parameters ?? throw new ArgumentNullException(nameof(parameters));
            _pool = ArrayPool<float>.Shared;
            _vectorSize = Vector<float>.Count;

            // Initialize vector constants
            _epsilonVec = new Vector<float>(float.Epsilon);
            _tenVec = new Vector<float>(10f);
            _oneVec = Vector<float>.One;
            _zeroVec = Vector<float>.Zero;
            _invLog10Vec = new Vector<float>(INV_LOG10);

            _parallelOpts = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };
        }
        #endregion

        #region Public Methods
        public float[] ConvertToSpectrum(Complex[] fft, int sampleRate)
        {
            ValidateInput(fft, sampleRate);

            int fftSize = fft.Length;
            SpectrumRange range = CalculateSpectrumRange(fftSize, sampleRate);

            var spectrum = _pool.Rent(range.Length);
            try
            {
                ProcessSpectrum(fft, sampleRate, spectrum, range);

                var result = new float[range.Length];
                Array.Copy(spectrum, result, range.Length);
                return result;
            }
            finally
            {
                _pool.Return(spectrum);
            }
        }
        #endregion

        #region Private Helper Methods
        private void ValidateInput(Complex[] fft, int sampleRate)
        {
            ArgumentNullException.ThrowIfNull(fft);
            if (sampleRate <= 0) throw new ArgumentException("Invalid sample rate", nameof(sampleRate));
        }

        private SpectrumRange CalculateSpectrumRange(int fftSize, int sampleRate)
        {
            int len = fftSize / 2;
            int minIdx = Math.Clamp((int)(MIN_FREQ * fftSize / sampleRate), 0, len - 1);
            int maxIdx = Math.Clamp((int)(MAX_FREQ * fftSize / sampleRate), 0, len - 1);

            if (minIdx > maxIdx)
            {
                throw new ArgumentException("Invalid frequency range: min index exceeds max index.");
            }

            return new SpectrumRange(minIdx, maxIdx);
        }

        private void ProcessSpectrum(Complex[] fft, int sampleRate, float[] spectrum, SpectrumRange range)
        {
            float minDb = _params.MinDbValue;
            float dbRange = _params.MaxDbValue - minDb;
            float amp = _params.AmplificationFactor;

            int vectorEnd = range.MinIndex + ((range.MaxIndex - range.MinIndex) / _vectorSize) * _vectorSize;

            // Vectorized parallel processing
            ProcessVectorizedSpectrum(fft, spectrum, range, minDb, dbRange, amp, vectorEnd);

            // Process remaining elements
            ProcessRemainingSpectrum(fft, spectrum, range, minDb, dbRange, amp, vectorEnd);
        }

        private void ProcessVectorizedSpectrum(Complex[] fft, float[] spectrum, SpectrumRange range,
            float minDb, float dbRange, float amp, int vectorEnd)
        {
            Parallel.For(range.MinIndex, vectorEnd, _parallelOpts, i =>
            {
                if (i % _vectorSize == 0)
                {
                    ProcessSpectrumVector(fft, spectrum, i, range.MinIndex, minDb, dbRange, amp);
                }
            });
        }

        private void ProcessRemainingSpectrum(Complex[] fft, float[] spectrum, SpectrumRange range,
            float minDb, float dbRange, float amp, int vectorEnd)
        {
            Parallel.For(vectorEnd, range.MaxIndex + 1, i =>
            {
                float mag = CalculateMagnitude(fft[i]);
                spectrum[i - range.MinIndex] = CalculateSpectrumValue(mag, minDb, dbRange, amp);
            });
        }

        private void ProcessSpectrumVector(Complex[] fft, float[] spectrum, int index, int minIdx,
            float minDb, float range, float amp)
        {
            var realVec = CreateVectorFromComplex(fft, index, (c) => c.X);
            var imagVec = CreateVectorFromComplex(fft, index, (c) => c.Y);

            var magVec = realVec * realVec + imagVec * imagVec;
            magVec = Vector.ConditionalSelect(Vector.Equals(magVec, _zeroVec), _epsilonVec, magVec);

            var logMagVec = CreateLogMagnitudeVector(magVec);

            var dbVec = CalculateNormalizedDecibels(logMagVec, minDb, range, amp);

            dbVec.CopyTo(spectrum, index - minIdx);
        }

        private Vector<float> CreateLogMagnitudeVector(Vector<float> magVec)
        {
            var values = new float[Vector<float>.Count];
            for (int j = 0; j < values.Length; j++)
            {
                values[j] = MathF.Log(magVec[j]);
            }
            return new Vector<float>(values);
        }

        private Vector<float> CreateVectorFromComplex(Complex[] fft, int index, Func<Complex, float> selector)
        {
            Span<float> values = stackalloc float[Vector<float>.Count];
            for (int j = 0; j < values.Length; j++)
            {
                values[j] = selector(fft[index + j]);
            }
            return new Vector<float>(values);
        }

        private Vector<float> CalculateNormalizedDecibels(Vector<float> logMagVec, float minDb, float range, float amp)
        {
            // Convert to decibels
            var dbVec = _tenVec * _invLog10Vec * logMagVec;

            // Normalize and clamp
            var minDbVec = new Vector<float>(minDb);
            var rangeVec = new Vector<float>(range);
            var ampVec = new Vector<float>(amp);

            return Vector.Min(
                Vector.Max(
                    Vector.Divide(dbVec - minDbVec, rangeVec) * ampVec,
                    _zeroVec
                ),
                _oneVec
            );
        }

        private static float CalculateMagnitude(Complex complex) => complex.X * complex.X + complex.Y * complex.Y;

        private static float CalculateSpectrumValue(float magnitude, float minDb, float range, float amp)
        {
            float db = 10 * INV_LOG10 * MathF.Log(magnitude == 0 ? float.Epsilon : magnitude);
            return ((db - minDb) / range * amp).Clamp(0, 1);
        }
        #endregion

        #region Supporting Types
        private record SpectrumRange(int MinIndex, int MaxIndex)
        {
            public int Length => MaxIndex - MinIndex + 1;
        }
        #endregion
    }

    public static class Extensions
    {
        public static float Clamp(this float value, float min, float max) =>
            value < min ? min : value > max ? max : value;
    }
}