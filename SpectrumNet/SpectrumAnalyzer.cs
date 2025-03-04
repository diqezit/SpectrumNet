// SpectrumAnalyser.cs (Complete Code without Regions)

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Complex = NAudio.Dsp.Complex;
#nullable enable

namespace SpectrumNet
{

    public static class Constants
    {
        public const float DefaultAmplificationFactor = 0.5f, DefaultMaxDbValue = 0f, DefaultMinDbValue = -130f,
                           Epsilon = float.Epsilon, TwoPi = 2f * MathF.PI, KaiserBeta = 5f, BesselEpsilon = 1e-10f,
                           InvLog10 = 0.43429448190325182765f, MinFreq = 20f, MaxFreq = 24000f;
        public const int DefaultFftSize = 2048;
    }

    public enum FftWindowType { Hann, Hamming, Blackman, Bartlett, Kaiser }

    public enum SpectrumScale
    {
        Linear,      // Linear scale
        Logarithmic, // Logarithmic scale
        Mel,         // Mel scale (psychoacoustic)
        Bark,        // Bark scale (critical bands)
        ERB          // Equivalent Rectangular Bandwidth
    }

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
            Result = result;
            SampleRate = sampleRate;
        }
    }

    public class SpectralDataEventArgs : EventArgs
    {
        public SpectralData Data { get; }
        public SpectralDataEventArgs(SpectralData data)
        {
            Data = data;
        }
    }

    public record SpectralData(float[] Spectrum, DateTime Timestamp);

    public struct SpectrumParameters
    {
        public float MinDb { get; }
        public float DbRange { get; }
        public float AmplificationFactor { get; }

        public SpectrumParameters(float minDb, float dbRange, float amplificationFactor)
        {
            MinDb = minDb;
            DbRange = dbRange;
            AmplificationFactor = amplificationFactor;
        }

        public static SpectrumParameters FromProvider(IGainParametersProvider p) =>
            new SpectrumParameters(p.MinDbValue, Math.Max(p.MaxDbValue - p.MinDbValue, Constants.Epsilon), p.AmplificationFactor);
    }

    public sealed class GainParameters : IGainParametersProvider, INotifyPropertyChanged
    {
        private const string LogPrefix = "[GainParameters] ";
        private float _amp = Constants.DefaultAmplificationFactor;
        private float _min = Constants.DefaultMinDbValue;
        private float _max = Constants.DefaultMaxDbValue;
        private readonly SynchronizationContext? _context;
        public event PropertyChangedEventHandler? PropertyChanged;

        public GainParameters(SynchronizationContext? context = null, float minDbValue = Constants.DefaultMinDbValue,
                              float maxDbValue = Constants.DefaultMaxDbValue, float amplificationFactor = Constants.DefaultAmplificationFactor)
        {
            try
            {
                if (minDbValue > maxDbValue)
                    throw new ArgumentException("MinDbValue cannot be greater than MaxDbValue.");
                _context = context;
                _min = minDbValue;
                _max = maxDbValue;
                _amp = amplificationFactor;
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Initialization error: {ex}");
                throw;
            }
        }

        public float AmplificationFactor
        {
            get => _amp;
            set
            {
                try
                {
                    UpdateProperty(ref _amp, Math.Max(0.1f, value));
                }
                catch (Exception ex)
                {
                    SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error setting AmplificationFactor: {ex}");
                }
            }
        }

        public float MaxDbValue
        {
            get => _max;
            set
            {
                try
                {
                    if (value < _min)
                    {
                        SmartLogger.Log(LogLevel.Warning, LogPrefix, $"MaxDbValue cannot be less than MinDbValue. Set to {_min}.");
                        value = _min;
                    }
                    UpdateProperty(ref _max, value);
                }
                catch (Exception ex)
                {
                    SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error setting MaxDbValue: {ex}");
                }
            }
        }

        public float MinDbValue
        {
            get => _min;
            set
            {
                try
                {
                    if (value > _max)
                    {
                        SmartLogger.Log(LogLevel.Warning, LogPrefix, $"MinDbValue cannot be greater than MaxDbValue. Set to {_max}.");
                        value = _max;
                    }
                    UpdateProperty(ref _min, value);
                }
                catch (Exception ex)
                {
                    SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error setting MinDbValue: {ex}");
                }
            }
        }

        private void UpdateProperty(ref float field, float value, [CallerMemberName] string? propertyName = null)
        {
            if (Math.Abs(field - value) > Constants.Epsilon)
            {
                field = value;
                if (_context != null)
                    _context.Post(s => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)), null);
                else
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }

    public sealed class SpectrumAnalyzer : ISpectralDataProvider, IDisposable, IComponent
    {
        private const string LogPrefix = "[SpectrumAnalyzer] ";
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
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error initializing spectrum analyzer: {ex}");
                throw;
            }
        }

        public SpectrumScale ScaleType
        {
            get => _scaleType;
            set
            {
                try
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
                catch (Exception ex)
                {
                    SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error setting scale type: {ex}");
                }
            }
        }

        public void SetWindowType(FftWindowType windowType)
        {
            try
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
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error setting window type: {ex}");
            }
        }

        public void SetScaleType(SpectrumScale scaleType)
        {
            try
            {
                ScaleType = scaleType;
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error in SetScaleType: {ex}");
            }
        }

        public void UpdateSettings(FftWindowType windowType, SpectrumScale scaleType)
        {
            try
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
                    {
                        ResetSpectrum();
                    }
                }
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error updating settings: {ex}");
            }
        }

        public SpectralData? GetCurrentSpectrum()
        {
            try
            {
                if (_disposed) throw new ObjectDisposedException(nameof(SpectrumAnalyzer));
                return _lastData;
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error getting current spectrum: {ex}");
                throw;
            }
        }

        public async Task AddSamplesAsync(ReadOnlyMemory<float> samples, int sampleRate, CancellationToken cancellationToken = default)
        {
            try
            {
                if (_disposed) throw new ObjectDisposedException(nameof(SpectrumAnalyzer));
                if (samples.Length == 0) return;
                await _fftProcessor.AddSamplesAsync(samples, sampleRate);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error adding samples: {ex}");
                throw;
            }
        }

        internal void ResetSpectrum()
        {
            try
            {
                _lastData = null;
                Helpers.InvokeEvent(SpectralDataReady, this, new SpectralDataEventArgs(new SpectralData(Array.Empty<float>(), DateTime.UtcNow)), _context);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error resetting spectrum: {ex}");
            }
        }

        private void OnFftCalculated(object? sender, FftEventArgs e)
        {
            try
            {
                if (e.Result.Length == 0) return;
                float[] spectrum = _converter.ConvertToSpectrum(e.Result, e.SampleRate, _scaleType);
                var data = new SpectralData(spectrum, DateTime.UtcNow);
                lock (_lock) _lastData = data;
                Helpers.InvokeEvent(SpectralDataReady, this, new SpectralDataEventArgs(data), _context);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error processing FFT results: {ex}");
            }
        }

        public void Dispose()
        {
            try
            {
                if (_disposed) return;
                _fftProcessor.FftCalculated -= OnFftCalculated;
                if (_fftProcessor is IAsyncDisposable ad) ad.DisposeAsync().AsTask().GetAwaiter().GetResult();
                _disposed = true;
                Helpers.InvokeEvent(Disposed, this, EventArgs.Empty, _context);
                GC.SuppressFinalize(this);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error disposing spectrum analyzer: {ex}");
            }
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
                    context.Post(s => handler?.Invoke(sender, args), null);
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
                    context.Post(s => handler?.Invoke(sender, args), null);
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
        private static readonly ConcurrentDictionary<int, (float[] cos, float[] sin)> _tables = new();

        public static (float[] cos, float[] sin) Get(int size)
        {
            try
            {
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
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error generating trigonometric tables: {ex}");
                throw;
            }
        }
    }

    public sealed class FftProcessor : IFftProcessor, IAsyncDisposable
    {
        private const string LogPrefix = "[FftProcessor] ";
        private readonly int _fftSize;
        private readonly Complex[] _buffer;
        private readonly Channel<(ReadOnlyMemory<float> Samples, int SampleRate)> _channel;
        private readonly CancellationTokenSource _cts = new();
        private readonly ArrayPool<float> _pool = ArrayPool<float>.Shared;
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
            try
            {
                if (!BitOperations.IsPow2(fftSize) || fftSize <= 0) throw new ArgumentException("FFT size must be a positive power of 2");
                _fftSize = fftSize;
                _buffer = new Complex[fftSize];
                (_cosCache, _sinCache) = TrigonometricTables.Get(fftSize);
                _windows = Enum.GetValues<FftWindowType>().ToDictionary(t => t, t => GenerateWindow(fftSize, t));
                _window = _windows[_windowType];
                _channel = Channel.CreateUnbounded<(ReadOnlyMemory<float>, int)>(new UnboundedChannelOptions { SingleReader = true });
                _parallelOpts = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };
                Task.Run(ProcessAsync);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error initializing FFT processor: {ex}");
                throw;
            }
        }

        public FftWindowType WindowType
        {
            get => _windowType;
            set
            {
                try
                {
                    if (_windowType == value) return;
                    _windowType = value;
                    _window = _windows[value];
                    ResetFftState();
                }
                catch (Exception ex)
                {
                    SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error setting window type: {ex}");
                }
            }
        }

        public ValueTask AddSamplesAsync(ReadOnlyMemory<float> samples, int sampleRate)
        {
            try
            {
                if (_disposed) throw new ObjectDisposedException(nameof(FftProcessor));
                if (sampleRate <= 0 || samples.Length == 0) return ValueTask.CompletedTask;
                return _channel.Writer.TryWrite((samples, sampleRate)) ? ValueTask.CompletedTask
                    : new ValueTask(_channel.Writer.WriteAsync((samples, sampleRate)).AsTask());
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error adding samples: {ex}");
                throw;
            }
        }

        public void ResetFftState()
        {
            try
            {
                _sampleCount = 0;
                Array.Clear(_buffer, 0, _fftSize);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error resetting FFT state: {ex}");
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_disposed) return;
                _disposed = true;
                _cts.Cancel();
                _channel.Writer.Complete();
                await Task.Run(() => { }).ConfigureAwait(false);
                _cts.Dispose();
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error disposing FFT processor: {ex}");
            }
        }

        private float[] GenerateWindow(int size, FftWindowType type)
        {
            float[] w = new float[size];
            try
            {
                Action<int> set = type switch
                {
                    FftWindowType.Hann => i => w[i] = 0.5f * (1f - _cosCache[i]),
                    FftWindowType.Hamming => i => w[i] = 0.54f - 0.46f * _cosCache[i],
                    FftWindowType.Blackman => i => w[i] = 0.42f - 0.5f * _cosCache[i] + 0.08f * MathF.Cos(Constants.TwoPi * 2 * i / (size - 1)),
                    FftWindowType.Bartlett => i => w[i] = 2f / (size - 1) * ((size - 1) / 2f - MathF.Abs(i - (size - 1) / 2f)),
                    FftWindowType.Kaiser => i => w[i] = KaiserWindow(i, size, Constants.KaiserBeta),
                    _ => throw new NotSupportedException($"Unsupported window type: {type}")
                };
                Parallel.For(0, size, set);
                return w;
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error generating window: {ex}");
                throw;
            }
        }

        private static float KaiserWindow(int i, int size, float beta)
        {
            float a = (size - 1) / 2f, t = (i - a) / a;
            return BesselI0(beta * MathF.Sqrt(1 - t * t)) / BesselI0(beta);
        }

        private static float BesselI0(float x)
        {
            float sum = 1f, term = (x * x) / 4f;
            for (int k = 1; term > Constants.BesselEpsilon; k++) { sum += term; term *= (x * x) / (4f * k * k); }
            return sum;
        }

        private async Task ProcessAsync()
        {
            try
            {
                await foreach (var (samples, rate) in _channel.Reader.ReadAllAsync(_cts.Token))
                {
                    if (samples.Length > 0) ProcessBatch(samples, rate);
                }
            }
            catch (OperationCanceledException)
            {
                // Operation canceled, expected behavior on disposal
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error in FFT processing loop: {ex}");
            }
        }

        private void ProcessBatch(ReadOnlyMemory<float> samples, int rate)
        {
            try
            {
                int pos = 0;
                while (pos < samples.Length)
                {
                    int count = Math.Min(_fftSize - _sampleCount, samples.Length - pos);
                    if (count <= 0) break;
                    ProcessChunk(samples.Slice(pos, count));
                    pos += count;
                    _sampleCount += count;
                    if (_sampleCount >= _fftSize)
                    {
                        FastFourierTransform.FFT(true, (int)Math.Log2(_fftSize), _buffer);
                        Helpers.InvokeEvent(FftCalculated, this, new FftEventArgs(_buffer, rate));
                        ResetFftState();
                    }
                }
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error processing batch: {ex}");
            }
        }

        private void ProcessChunk(ReadOnlyMemory<float> chunk)
        {
            try
            {
                int offset = _sampleCount, len = chunk.Length, vecEnd = len - len % _vecSize;
                Span<float> temp = stackalloc float[_vecSize];
                for (int i = 0; i < vecEnd; i += _vecSize)
                {
                    chunk.Span.Slice(i, _vecSize).CopyTo(temp);
                    Vector<float> s = new Vector<float>(temp);
                    Vector<float> w = new Vector<float>(_window, offset + i);
                    Vector<float> result = s * w;
                    result.CopyTo(temp);
                    for (int j = 0; j < _vecSize; j++)
                        _buffer[offset + i + j] = new Complex { X = temp[j] };
                }
                for (int i = vecEnd; i < len; i++)
                    _buffer[offset + i] = new Complex { X = chunk.Span[i] * _window[offset + i] };
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error processing chunk: {ex}");
            }
        }
    }

    public sealed class SpectrumConverter : ISpectrumConverter
    {
        private const string LogPrefix = "[SpectrumConverter] ";
        private readonly IGainParametersProvider _params;
        private readonly ParallelOptions _parallelOpts = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };

        public SpectrumConverter(IGainParametersProvider parameters)
        {
            try
            {
                _params = parameters ?? throw new ArgumentNullException(nameof(parameters));
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error initializing spectrum converter: {ex}");
                throw;
            }
        }

        public float[] ConvertToSpectrum(Complex[] fft, int sampleRate, SpectrumScale scale)
        {
            try
            {
                if (fft == null) throw new ArgumentNullException(nameof(fft));
                if (sampleRate <= 0) throw new ArgumentException("Invalid sample rate", nameof(sampleRate));

                int minIndex = (int)(Constants.MinFreq * fft.Length / sampleRate);
                int maxIndex = Math.Min((int)(Constants.MaxFreq * fft.Length / sampleRate), fft.Length / 2);
                int spectrumLength = maxIndex - minIndex + 1;
                float[] spectrum = new float[spectrumLength];
                SpectrumParameters sp = SpectrumParameters.FromProvider(_params);

                switch (scale)
                {
                    case SpectrumScale.Linear:
                        ProcessLinear(fft, spectrum, minIndex, maxIndex, sp);
                        break;
                    case SpectrumScale.Logarithmic:
                        ProcessLogarithmic(fft, spectrum, minIndex, maxIndex, sampleRate, sp);
                        break;
                    case SpectrumScale.Mel:
                        ProcessMelScale(fft, spectrum, sampleRate, sp);
                        break;
                    case SpectrumScale.Bark:
                        ProcessBarkScale(fft, spectrum, sampleRate, sp);
                        break;
                    case SpectrumScale.ERB:
                        ProcessERBScale(fft, spectrum, sampleRate, sp);
                        break;
                    default:
                        ProcessLinear(fft, spectrum, minIndex, maxIndex, sp);
                        break;
                }
                return spectrum;
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error converting spectrum: {ex}");
                throw;
            }
        }

        private void ProcessLinear(Complex[] fft, float[] spectrum, int minIndex, int maxIndex, SpectrumParameters sp)
        {
            try
            {
                if (spectrum.Length < 100)
                {
                    for (int i = 0; i < spectrum.Length; i++)
                    {
                        int fftIndex = minIndex + i;
                        if (fftIndex < fft.Length)
                            spectrum[i] = InterpolateSpectrumValue(fft, fftIndex, sp);
                    }
                }
                else
                {
                    Parallel.For(0, spectrum.Length, i =>
                    {
                        int fftIndex = minIndex + i;
                        if (fftIndex < fft.Length)
                            spectrum[i] = InterpolateSpectrumValue(fft, fftIndex, sp);
                    });
                }
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error processing linear spectrum: {ex}");
            }
        }

        private void ProcessMelScale(Complex[] fft, float[] spectrum, int sampleRate, SpectrumParameters sp)
        {
            try
            {
                float minMel = FreqToMel(Constants.MinFreq);
                float maxMel = FreqToMel(Constants.MaxFreq);
                float melStep = (maxMel - minMel) / (spectrum.Length - 1);

                Parallel.For(0, spectrum.Length, i =>
                {
                    float mel = minMel + i * melStep;
                    float freq = MelToFreq(mel);
                    int bin = (int)(freq * fft.Length / sampleRate);
                    spectrum[i] = (bin >= 0 && bin < fft.Length / 2) ? CalcValue(Mag(fft[bin]), sp) : 0;
                });
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error processing Mel scale spectrum: {ex}");
            }
        }

        private void ProcessBarkScale(Complex[] fft, float[] spectrum, int sampleRate, SpectrumParameters sp)
        {
            try
            {
                float minBark = FreqToBark(Constants.MinFreq);
                float maxBark = FreqToBark(Constants.MaxFreq);
                float barkStep = (maxBark - minBark) / (spectrum.Length - 1);

                Parallel.For(0, spectrum.Length, i =>
                {
                    float bark = minBark + i * barkStep;
                    float freq = BarkToFreq(bark);
                    int bin = (int)(freq * fft.Length / sampleRate);
                    spectrum[i] = (bin >= 0 && bin < fft.Length / 2) ? CalcValue(Mag(fft[bin]), sp) : 0;
                });
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error processing Bark scale spectrum: {ex}");
            }
        }

        private void ProcessERBScale(Complex[] fft, float[] spectrum, int sampleRate, SpectrumParameters sp)
        {
            try
            {
                float minERB = FreqToERB(Constants.MinFreq);
                float maxERB = FreqToERB(Constants.MaxFreq);
                float erbStep = (maxERB - minERB) / (spectrum.Length - 1);

                Parallel.For(0, spectrum.Length, i =>
                {
                    float erb = minERB + i * erbStep;
                    float freq = ERBToFreq(erb);
                    int bin = (int)(freq * fft.Length / sampleRate);
                    spectrum[i] = (bin >= 0 && bin < fft.Length / 2) ? CalcValue(Mag(fft[bin]), sp) : 0;
                });
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error processing ERB scale spectrum: {ex}");
            }
        }

        private void ProcessLogarithmic(Complex[] fft, float[] spectrum, int minIndex, int maxIndex, int sampleRate, SpectrumParameters sp)
        {
            try
            {
                float logMin = MathF.Log10(Constants.MinFreq);
                float logMax = MathF.Log10(Constants.MaxFreq);
                float logStep = (logMax - logMin) / (spectrum.Length - 1);

                Parallel.For(0, spectrum.Length, i =>
                {
                    float freq = MathF.Pow(10, logMin + i * logStep);
                    int bin = (int)(freq * fft.Length / sampleRate);
                    spectrum[i] = (bin >= 0 && bin < fft.Length / 2) ? CalcValue(Mag(fft[bin]), sp) : 0;
                });
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error processing logarithmic spectrum: {ex}");
            }
        }

        private static float FreqToMel(float freq) => 2595f * MathF.Log10(1 + freq / 700f);
        private static float MelToFreq(float mel) => 700f * (MathF.Pow(10, mel / 2595f) - 1);

        private static float FreqToBark(float freq) => 13f * MathF.Atan(0.00076f * freq) + 3.5f * MathF.Atan(MathF.Pow(freq / 7500f, 2));
        private static float BarkToFreq(float bark) => 1960f * (bark + 0.53f) / (26.28f - bark);

        private static float FreqToERB(float freq) => 21.4f * MathF.Log10(0.00437f * freq + 1);
        private static float ERBToFreq(float erb) => (MathF.Pow(10, erb / 21.4f) - 1) / 0.00437f;

        private float InterpolateSpectrumValue(Complex[] fft, int index, SpectrumParameters sp)
        {
            try
            {
                float centerMag = Mag(fft[index]);
                float leftMag = index > 0 ? Mag(fft[index - 1]) : centerMag;
                float rightMag = index < fft.Length / 2 - 1 ? Mag(fft[index + 1]) : centerMag;
                float interpolatedMag = (leftMag + centerMag + rightMag) / 3f;
                if (interpolatedMag <= 0) return 0;
                float db = 10f * Constants.InvLog10 * MathF.Log(interpolatedMag);
                float norm = Math.Clamp((db - sp.MinDb) / sp.DbRange, 0, 1);
                return norm < 1e-6f ? 0 : MathF.Pow(norm, sp.AmplificationFactor);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error interpolating spectrum value: {ex}");
                return 0;
            }
        }

        private static float CalcValue(float mag, SpectrumParameters sp)
        {
            if (mag == 0) return 0;
            float db = 10f * Constants.InvLog10 * MathF.Log(mag);
            float norm = Math.Clamp((db - sp.MinDb) / sp.DbRange, 0, 1);
            return norm < 1e-6f ? 0 : MathF.Pow(norm, sp.AmplificationFactor);
        }

        private static float Mag(Complex c) => c.X * c.X + c.Y * c.Y;
    }
}