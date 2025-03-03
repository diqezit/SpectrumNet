using Complex = NAudio.Dsp.Complex;
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
                           InvLog10 = 0.43429448190325182765f,
                           MinFreq = 20f,
                           MaxFreq = 24000f;
        public const int DefaultFftSize = 2048;
    }

    public enum FftWindowType { Hann, Hamming, Blackman, Bartlett, Kaiser }
    public enum SpectrumScale { Linear, Logarithmic }

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
        float[] ConvertToSpectrum(Complex[] fftResult, int sampleRate, SpectrumScale scale);
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

    public readonly struct SpectrumParameters
    {
        public float MinDb { get; }
        public float DbRange { get; }
        public float AmplificationFactor { get; }
        public SpectrumParameters(float minDb, float maxDb, float ampFactor)
        {
            MinDb = minDb;
            DbRange = MathF.Abs(maxDb - minDb) > Constants.Epsilon ? maxDb - minDb : 1f;
            AmplificationFactor = ampFactor;
        }
        public static SpectrumParameters FromProvider(IGainParametersProvider provider) =>
            new(provider.MinDbValue, provider.MaxDbValue, provider.AmplificationFactor);
    }

    public sealed class GainParameters : IGainParametersProvider, INotifyPropertyChanged
    {
        float _amp = Constants.DefaultAmplificationFactor, _min = Constants.DefaultMinDbValue, _max = Constants.DefaultMaxDbValue;
        readonly SynchronizationContext? _context;
        public event PropertyChangedEventHandler? PropertyChanged;
        public GainParameters(SynchronizationContext? context = null,
                              float minDbValue = Constants.DefaultMinDbValue,
                              float maxDbValue = Constants.DefaultMaxDbValue,
                              float amplificationFactor = Constants.DefaultAmplificationFactor)
        {
            _context = context;
            _min = minDbValue;
            _max = maxDbValue;
            _amp = amplificationFactor;
        }
        public float AmplificationFactor { get => _amp; set => UpdateProperty(ref _amp, MathF.Max(0.1f, value)); }
        public float MaxDbValue { get => _max; set => UpdateProperty(ref _max, MathF.Max(value, _min)); }
        public float MinDbValue { get => _min; set => UpdateProperty(ref _min, MathF.Min(value, _max)); }
        void UpdateProperty(ref float field, float value, [CallerMemberName] string? propertyName = null)
        {
            if (MathF.Abs(field - value) > Constants.Epsilon)
            {
                field = value;
                if (_context != null)
                    _context.Post(_ => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)), null);
                else PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }

    public sealed class SpectrumAnalyzer : ISpectralDataProvider, IDisposable, IComponent
    {
        const string LogPrefix = "[SpectrumAnalyzer] ";
        public event EventHandler? Disposed;
        public ISite? Site { get; set; }
        public event EventHandler<SpectralDataEventArgs>? SpectralDataReady;
        readonly IFftProcessor _fftProcessor;
        readonly ISpectrumConverter _converter;
        readonly SynchronizationContext? _context;
        SpectralData? _lastData;
        bool _disposed;
        readonly object _scaleLock = new();
        SpectrumScale _scaleType = SpectrumScale.Linear;
        public SpectrumAnalyzer(IFftProcessor fftProcessor, ISpectrumConverter converter, SynchronizationContext? context = null)
        {
            _fftProcessor = fftProcessor ?? throw new ArgumentNullException(nameof(fftProcessor));
            _converter = converter ?? throw new ArgumentNullException(nameof(converter));
            _context = context;
            _fftProcessor.FftCalculated += OnFftCalculated;
        }
        public IFftProcessor FftProcessor => _fftProcessor;
        public SpectrumScale ScaleType
        {
            get { lock (_scaleLock) { return _scaleType; } }
            set
            {
                lock (_scaleLock)
                {
                    if (_scaleType == value)
                        return;
                    SmartLogger.Log(LogLevel.Debug, LogPrefix, $"Setting scale type: {value}", forceLog: true);
                    _scaleType = value;
                }
                ResetSpectrum();
            }
        }
        public SpectralData? GetCurrentSpectrum()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SpectrumAnalyzer));
            return _lastData;
        }
        public void ResetSpectrum()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SpectrumAnalyzer));
            _lastData = null;
            if (_context != null)
            {
                var emptyData = new SpectralData(Array.Empty<float>(), DateTime.UtcNow);
                _context.Post(_ => SpectralDataReady?.Invoke(this, new SpectralDataEventArgs(emptyData)), null);
            }
            SmartLogger.Log(LogLevel.Debug, LogPrefix, $"Spectrum data reset due to scale change to {ScaleType}", forceLog: true);
        }
        public async Task AddSamplesAsync(float[] samples, int sampleRate, CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SpectrumAnalyzer));
            if (samples is null || samples.Length == 0)
                return;
            try
            {
                await _fftProcessor.AddSamplesAsync(samples, sampleRate).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error adding samples: {ex}");
                throw;
            }
        }
        void OnFftCalculated(object? sender, FftEventArgs e)
        {
            try
            {
                if (e.Result?.Length > 0)
                {
                    SpectrumScale currentScale;
                    lock (_scaleLock) { currentScale = _scaleType; }
                    SmartLogger.Log(LogLevel.Debug, LogPrefix, $"Processing FFT data with scale type: {currentScale}", throttleRatio: 50);
                    var spectrum = _converter.ConvertToSpectrum(e.Result, e.SampleRate, currentScale);
                    var data = new SpectralData(spectrum, DateTime.UtcNow);
                    _lastData = data;
                    SmartLogger.Log(LogLevel.Debug, LogPrefix, $"Spectrum calculated: {spectrum.Length} points, scale: {currentScale}", throttleRatio: 50);
                    if (_context != null)
                        _context.Post(_ => SpectralDataReady?.Invoke(this, new SpectralDataEventArgs(data)), null);
                    else
                        SpectralDataReady?.Invoke(this, new SpectralDataEventArgs(data));
                }
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"FFT callback error: {ex}");
            }
        }
        public void Dispose()
        {
            if (!_disposed)
            {
                _fftProcessor.FftCalculated -= OnFftCalculated;
                try
                {
                    if (_fftProcessor is IAsyncDisposable asyncDisp)
                        asyncDisp.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    SmartLogger.Log(LogLevel.Error, LogPrefix, $"Dispose error: {ex}");
                }
                _disposed = true;
                Disposed?.Invoke(this, EventArgs.Empty);
                GC.SuppressFinalize(this);
            }
        }
    }

    public sealed class FftProcessor : IFftProcessor, IAsyncDisposable
    {
        const string LogPrefix = "[FftProcessor] ";
        readonly int _fftSize;
        readonly ConcurrentDictionary<(int, FftWindowType), float[]> _windowCache = new();
        readonly Complex[] _buffer;
        readonly Channel<(float[] Samples, int SampleRate)> _channel;
        readonly CancellationTokenSource _cts;
        readonly ArrayPool<float> _pool;
        readonly Task _processTask;
        readonly int _vecSize;
        readonly float[] _cosCache, _sinCache;
        readonly ParallelOptions _parallelOpts;
        float[] _window;
        int _sampleCount;
        bool _disposed;
        FftWindowType _windowType = FftWindowType.Hann;

        public FftProcessor(int fftSize = Constants.DefaultFftSize)
        {
            if (!System.Numerics.BitOperations.IsPow2(fftSize) || fftSize <= 0)
                throw new ArgumentException("FFT size must be a positive power of 2");
            _fftSize = fftSize;
            _buffer = new Complex[fftSize];
            _pool = ArrayPool<float>.Shared;
            _vecSize = Vector<float>.Count;
            _parallelOpts = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };

            (_cosCache, _sinCache) = PrecomputeTrigonometricValues(fftSize);
            _window = GenerateWindow(fftSize, _windowType);
            _channel = Channel.CreateUnbounded<(float[] Samples, int SampleRate)>(new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = true });
            _cts = new CancellationTokenSource();
            _processTask = Task.Run(ProcessAsync);
            SmartLogger.Log(LogLevel.Debug, LogPrefix, $"Initialized with FFT size: {fftSize}, window: {_windowType}", forceLog: true);
        }

        public FftWindowType WindowType
        {
            get => _windowType;
            set
            {
                if (_windowType != value)
                {
                    SmartLogger.Log(LogLevel.Debug, LogPrefix, $"Changing window type from {_windowType} to {value}", forceLog: true);
                    _windowType = value;
                    _window = GenerateWindow(_fftSize, value);
                    _sampleCount = 0;
                }
            }
        }

        public event EventHandler<FftEventArgs>? FftCalculated;

        public ValueTask AddSamplesAsync(Memory<float> samples, int sampleRate)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(FftProcessor));
            if (sampleRate <= 0)
                throw new ArgumentException($"Invalid sample rate: {sampleRate}", nameof(sampleRate));
            if (samples.IsEmpty)
                return ValueTask.CompletedTask;
            float[] sampleArray = samples.ToArray();
            return _channel.Writer.TryWrite((sampleArray, sampleRate))
                ? ValueTask.CompletedTask
                : new ValueTask(_channel.Writer.WriteAsync((sampleArray, sampleRate)).AsTask());
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;
            _disposed = true;
            try
            {
                _cts.Cancel();
                _channel.Writer.Complete();
                await _processTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Dispose error: {ex}");
            }
            finally
            {
                _cts.Dispose();
                Array.Clear(_buffer, 0, _buffer.Length);
                Array.Clear(_window, 0, _window.Length);
                _pool.Return(_cosCache, true);
                _pool.Return(_sinCache, true);
            }
        }

        (float[] cos, float[] sin) PrecomputeTrigonometricValues(int size)
        {
            float[] angles = _pool.Rent(size);
            float step = Constants.TwoPi / (size - 1);
            Parallel.For(0, size, i => angles[i] = i * step);
            float[] cos = _pool.Rent(size), sin = _pool.Rent(size);
            Parallel.For(0, size, i =>
            {
                cos[i] = MathF.Cos(angles[i]);
                sin[i] = MathF.Sin(angles[i]);
            });
            _pool.Return(angles, true);
            return (cos, sin);
        }

        float[] GenerateWindow(int size, FftWindowType type)
        {
            if (_windowCache.TryGetValue((size, type), out var cached))
                return cached;
            float[] window = _pool.Rent(size);
            Func<int, float> func = type switch
            {
                FftWindowType.Hann => i => 0.5f * (1f - _cosCache[i]),
                FftWindowType.Hamming => i => 0.54f - 0.46f * _cosCache[i],
                FftWindowType.Blackman => i => 0.42f - 0.5f * _cosCache[i] + 0.08f * MathF.Cos(Constants.TwoPi * 2 * i / (size - 1)),
                FftWindowType.Bartlett => i => 2f / (size - 1) * (((size - 1) / 2f) - MathF.Abs(i - (size - 1) / 2f)),
                FftWindowType.Kaiser => i => KaiserWindow(i, size, Constants.KaiserBeta),
                _ => throw new NotSupportedException($"Window type {type} is not supported")
            };
            Action<int> act = i => window[i] = func(i);
            if (size < 4096)
                for (int i = 0; i < size; i++) act(i);
            else
                Parallel.For(0, size, act);
            _windowCache[(size, type)] = window;
            return window;
        }

        static float KaiserWindow(int i, int size, float beta)
        {
            float a = (size - 1) / 2f;
            float t = (i - a) / a;
            return BesselI0(beta * MathF.Sqrt(1 - t * t)) / BesselI0(beta);
        }

        static float BesselI0(float x)
        {
            if (x == 0f)
                return 1f;
            float sum = 1f, term = (x * x) / 4f;
            for (int k = 1; term > Constants.BesselEpsilon; k++)
            {
                sum += term;
                term *= (x * x) / (4f * k * k);
            }
            return sum;
        }

        async Task ProcessAsync()
        {
            try
            {
                await foreach (var (samples, rate) in _channel.Reader.ReadAllAsync(_cts.Token))
                {
                    if (samples.Length > 0)
                        try { ProcessBatch(samples, rate); }
                        catch (Exception ex) { SmartLogger.Log(LogLevel.Error, LogPrefix, $"Batch error: {ex}"); }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { SmartLogger.Log(LogLevel.Error, LogPrefix, $"ProcessAsync error: {ex}"); }
        }

        void ProcessBatch(float[] samples, int rate)
        {
            int pos = 0;
            while (pos < samples.Length)
            {
                int count = Math.Min(_fftSize - _sampleCount, samples.Length - pos);
                if (count <= 0)
                    break;
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

        void ProcessChunk(ReadOnlySpan<float> chunk)
        {
            int vecCount = chunk.Length / _vecSize;
            Span<float> temp = stackalloc float[_vecSize];
            for (int i = 0; i < vecCount; i++)
            {
                int offset = i * _vecSize;
                chunk.Slice(offset, _vecSize).CopyTo(temp);
                var sampleVec = new Vector<float>(temp);
                var windowVec = new Vector<float>(_window.AsSpan(_sampleCount + offset, _vecSize));
                var resultVec = sampleVec * windowVec;
                for (int j = 0; j < _vecSize; j++)
                    _buffer[_sampleCount + offset + j] = new Complex { X = resultVec[j] };
            }
            int remaining = chunk.Length % _vecSize, start = vecCount * _vecSize;
            for (int i = 0; i < remaining; i++)
                _buffer[_sampleCount + start + i] = new Complex { X = chunk[start + i] * _window[_sampleCount + start + i] };
        }

        void PerformFftCalculation(int rate)
        {
            if (FftCalculated == null)
                return;
            try
            {
                SmartLogger.Log(LogLevel.Debug, LogPrefix, $"Performing FFT calculation for sampling rate: {rate}", throttleRatio: 50);
                FastFourierTransform.FFT(true, (int)Math.Log2(_fftSize), _buffer);
                FftCalculated?.Invoke(this, new FftEventArgs(_buffer, rate));
            }
            catch (Exception ex) { SmartLogger.Log(LogLevel.Error, LogPrefix, $"FFT error: {ex}"); }
        }
    }

    public sealed class SpectrumConverter : ISpectrumConverter
    {
        const string LogPrefix = "[SpectrumConverter] ";
        readonly IGainParametersProvider _params;
        readonly ArrayPool<float> _pool;
        readonly int _vectorSize;
        readonly ParallelOptions _parallelOpts;
        static readonly Vector<float> _zeroVec = Vector<float>.Zero, _oneVec = Vector<float>.One,
            _tenVec = new(10f), _invLog10Vec = new(Constants.InvLog10), _epsilonVec = new(float.Epsilon);

        record SpectrumRange(int MinIndex, int MaxIndex)
        {
            public int Length => MaxIndex - MinIndex + 1;
        }

        public SpectrumConverter(IGainParametersProvider parameters)
        {
            _params = parameters ?? throw new ArgumentNullException(nameof(parameters));
            _pool = ArrayPool<float>.Shared;
            _vectorSize = Vector<float>.Count;
            _parallelOpts = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };
        }

        public float[] ConvertToSpectrum(Complex[] fft, int sampleRate, SpectrumScale scale)
        {
            if (fft is null)
                throw new ArgumentNullException(nameof(fft));
            if (sampleRate <= 0)
                throw new ArgumentException("Invalid sample rate", nameof(sampleRate));
            SmartLogger.Log(LogLevel.Debug, LogPrefix, $"ConvertToSpectrum called with scale: {scale}", throttleRatio: 50);
            var range = CalculateSpectrumRange(fft.Length, sampleRate);
            float[] spectrum = _pool.Rent(range.Length);
            try
            {
                if (scale == SpectrumScale.Linear)
                    ProcessSpectrumLinear(fft, spectrum, range);
                else
                    ProcessSpectrumLogarithmic(fft, spectrum, range, sampleRate);
                return spectrum.AsSpan(0, range.Length).ToArray();
            }
            finally { _pool.Return(spectrum); }
        }

        static SpectrumRange CalculateSpectrumRange(int fftSize, int sampleRate)
        {
            int len = fftSize / 2;
            int minIdx = Math.Clamp((int)(Constants.MinFreq * fftSize / sampleRate), 0, len - 1);
            int maxIdx = Math.Clamp((int)(Constants.MaxFreq * fftSize / sampleRate), 0, len - 1);
            if (minIdx > maxIdx)
                throw new ArgumentException("Invalid frequency range");
            return new SpectrumRange(minIdx, maxIdx);
        }

        void ProcessSpectrumLinear(Complex[] fft, float[] spectrum, SpectrumRange range)
        {
            SpectrumParameters spParams = SpectrumParameters.FromProvider(_params);
            int vectorEnd = range.MinIndex + ((range.MaxIndex - range.MinIndex) / _vectorSize) * _vectorSize;
            Parallel.ForEach(Partitioner.Create(range.MinIndex, vectorEnd, _vectorSize), _parallelOpts, part =>
            {
                for (int i = part.Item1; i < part.Item2; i += _vectorSize)
                    ProcessSpectrumVector(fft, spectrum, i, range.MinIndex, spParams);
            });
            Parallel.For(vectorEnd, range.MaxIndex + 1, i =>
            {
                float mag = Mag(fft[i]);
                spectrum[i - range.MinIndex] = CalculateSpectrumValue(mag, spParams);
            });
        }

        void ProcessSpectrumLogarithmic(Complex[] fft, float[] spectrum, SpectrumRange range, int sampleRate)
        {
            SpectrumParameters spParams = SpectrumParameters.FromProvider(_params);
            int len = range.Length;
            float logMin = MathF.Log10(Constants.MinFreq), logMax = MathF.Log10(Constants.MaxFreq);
            float logStep = (logMax - logMin) / (len - 1);
            SmartLogger.Log(LogLevel.Debug, LogPrefix, $"Logarithmic parameters: logMin={logMin}, logMax={logMax}, len={len}", throttleRatio: 50);
            Parallel.For(0, len, i =>
            {
                float freq = MathF.Pow(10, logMin + i * logStep);
                int binIndex = (int)(freq * fft.Length / sampleRate);
                spectrum[i] = (binIndex >= 0 && binIndex < fft.Length)
                    ? CalculateSpectrumValue(Mag(fft[binIndex]), spParams)
                    : 0;
            });
        }

        static void ProcessSpectrumVector(Complex[] fft, float[] spectrum, int index, int minIdx, SpectrumParameters sp)
        {
            Span<float> temp = stackalloc float[Vector<float>.Count];
            for (int i = 0; i < Vector<float>.Count; i++)
                temp[i] = fft[index + i].X;
            Vector<float> realVec = new(temp);
            for (int i = 0; i < Vector<float>.Count; i++)
                temp[i] = fft[index + i].Y;
            Vector<float> imagVec = new(temp);
            Vector<float> magVec = realVec * realVec + imagVec * imagVec;
            magVec = Vector.ConditionalSelect(Vector.Equals(magVec, _zeroVec), _epsilonVec, magVec);
            for (int j = 0; j < Vector<float>.Count; j++)
                temp[j] = MathF.Log(magVec[j]);
            Vector<float> dbVec = _tenVec * _invLog10Vec * new Vector<float>(temp);
            float dbRange = sp.DbRange > Constants.Epsilon ? sp.DbRange : 1f;
            Vector<float> normalized = Vector.Divide(dbVec - new Vector<float>(sp.MinDb), new Vector<float>(dbRange));
            normalized = Vector.Min(Vector.Max(normalized, _zeroVec), _oneVec);
            normalized.CopyTo(temp);
            for (int j = 0; j < temp.Length; j++)
                temp[j] = ApplyAmplification(temp[j], sp.AmplificationFactor);
            new Vector<float>(temp).CopyTo(spectrum, index - minIdx);
        }

        static float CalculateSpectrumValue(float magnitude, SpectrumParameters sp)
        {
            if (magnitude == 0f)
                magnitude = float.Epsilon;
            float db = 10f * Constants.InvLog10 * MathF.Log(magnitude);
            float range = sp.DbRange > Constants.Epsilon ? sp.DbRange : 1f;
            return Math.Clamp(ApplyAmplification((db - sp.MinDb) / range, sp.AmplificationFactor), 0f, 1f);
        }

        static float ApplyAmplification(float value, float ampFactor) =>
            value < 1e-6f ? 0f : MathF.Pow(value, ampFactor);

        static float Mag(Complex c) => c.X * c.X + c.Y * c.Y;
    }
}