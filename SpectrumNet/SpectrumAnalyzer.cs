using Complex = NAudio.Dsp.Complex;

#nullable enable

namespace SpectrumNet
{
    public static class Constants
    {
        public const float DefaultAmplificationFactor = 0.25f;
        public const float DefaultMaxDbValue = -20f;
        public const float DefaultMinDbValue = -110f;
        public const int DefaultFftSize = 2048;
        public const float Epsilon = float.Epsilon;
    }

    public interface IFftProcessor
    {
        event EventHandler<FftEventArgs>? FftCalculated;
        ValueTask AddSamplesAsync(float[] samples, int sampleRate);
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
        private readonly IFftProcessor _fftProcessor;
        private readonly ISpectrumConverter _converter;
        private readonly SynchronizationContext? _context;
        private SpectralData? _lastData;
        private bool _disposed;
        public IFftProcessor FftProcessor => _fftProcessor;

        public event EventHandler<SpectralDataEventArgs>? SpectralDataReady;

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
    }

    public sealed class FftProcessor : IFftProcessor, IAsyncDisposable
    {
        private readonly int _fftSize;
        private readonly Complex[] _buffer; // Используем NAudio.Dsp.Complex
        private readonly float[] _window;
        private readonly Channel<(float[] Samples, int SampleRate)> _processingChannel;
        private readonly CancellationTokenSource _cts;
        private readonly Task _processingTask;
        private int _sampleCount;
        private bool _isDisposed;

        public event EventHandler<FftEventArgs>? FftCalculated;
        public FftWindowType WindowType { get; set; } = FftWindowType.Hann;

        public FftProcessor(int fftSize = Constants.DefaultFftSize)
        {
            if (!BitOperations.IsPow2(fftSize) || fftSize <= 0)
                throw new ArgumentException("FFT size must be a positive power of 2.");

            _fftSize = fftSize;
            _buffer = new Complex[fftSize]; // Используем NAudio.Dsp.Complex
            _window = GenerateWindow(fftSize);
            _processingChannel = Channel.CreateUnbounded<(float[], int)>(new UnboundedChannelOptions { SingleReader = true });
            _cts = new CancellationTokenSource();
            _processingTask = Task.Run(ProcessSamplesAsync);
        }

        private float[] GenerateWindow(int size)
        {
            // Карта оконных функций для удобства добавления новых
            var windowFunctions = new Dictionary<FftWindowType, Func<int, int, float>>
        {
            { FftWindowType.Hann, (i, n) => 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / (n - 1))) },
            { FftWindowType.Hamming, (i, n) => 0.54f - 0.46f * MathF.Cos(2f * MathF.PI * i / (n - 1)) },
            { FftWindowType.Blackman, (i, n) => 0.42f - 0.5f * MathF.Cos(2f * MathF.PI * i / (n - 1)) + 0.08f * MathF.Cos(4f * MathF.PI * i / (n - 1)) },
            { FftWindowType.Bartlett, (i, n) => 2f / (n - 1) * ((n - 1) / 2f - MathF.Abs(i - (n - 1) / 2f)) },
            { FftWindowType.Kaiser, (i, n) => KaiserWindow(i, n, beta: 5f) }
        };

            if (!windowFunctions.TryGetValue(WindowType, out var windowFunc))
                throw new NotSupportedException($"Window type {WindowType} is not supported.");

            var window = new float[size];
            for (int i = 0; i < size; i++)
            {
                window[i] = windowFunc(i, size);
            }

            return window;
        }

        private static float KaiserWindow(int i, int n, float beta)
        {
            // Реализация окна Кайзера
            float alpha = (n - 1) / 2f;
            float t = (i - alpha) / alpha;
            return BesselI0(beta * MathF.Sqrt(1 - t * t)) / BesselI0(beta);
        }

        private static float BesselI0(float x)
        {
            // Аппроксимация модифицированной функции Бесселя I0
            float sum = 1f;
            float y = x * x / 4f;
            float term = y;
            for (int k = 1; term > 1e-10; k++)
            {
                sum += term;
                term *= y / (k * k);
            }
            return sum;
        }

        public ValueTask AddSamplesAsync(float[] samples, int sampleRate)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(FftProcessor));

            ArgumentNullException.ThrowIfNull(samples);
            if (sampleRate <= 0)
                throw new ArgumentException("Invalid sample rate", nameof(sampleRate));

            if (_processingChannel.Writer.TryWrite((samples, sampleRate)))
            {
                return ValueTask.CompletedTask;
            }

            return new ValueTask(_processingChannel.Writer.WriteAsync((samples, sampleRate)).AsTask());
        }

        private async Task ProcessSamplesAsync()
        {
            try
            {
                await foreach (var (samples, sampleRate) in _processingChannel.Reader.ReadAllAsync(_cts.Token))
                {
                    int index = 0;
                    while (index < samples.Length)
                    {
                        int copyCount = Math.Min(_fftSize - _sampleCount, samples.Length - index);
                        if (copyCount <= 0) break;

                        ApplyWindowAndCopy(samples.AsSpan(index, copyCount));
                        index += copyCount;
                        _sampleCount += copyCount;

                        if (_sampleCount >= _fftSize)
                        {
                            ProcessFft(sampleRate);
                            _sampleCount = 0;
                        }
                    }
                }
            }
            catch (OperationCanceledException) { /* ignore */ }
        }

        private void ApplyWindowAndCopy(Span<float> samples)
        {
            for (int i = 0; i < samples.Length; i++)
            {
                int bufferIndex = _sampleCount + i;
                _buffer[bufferIndex] = new Complex
                {
                    X = samples[i] * _window[bufferIndex], // X: действительная часть
                    Y = 0 // Y: мнимая часть
                };
            }
        }

        private void ProcessFft(int sampleRate)
        {
            // Выполняем FFT с использованием NAudio
            FastFourierTransform.FFT(true, (int)Math.Log2(_fftSize), _buffer);

            FftCalculated?.Invoke(this, new FftEventArgs(_buffer, sampleRate));
        }

        public async ValueTask DisposeAsync()
        {
            if (_isDisposed)
                return;

            try
            {
                _isDisposed = true;

                // Отменяем обработку и ждем завершения задачи
                if (!_cts.IsCancellationRequested)
                {
                    _cts.Cancel();
                    _processingChannel.Writer.Complete();
                    if (_processingTask != null)
                    {
                        await _processingTask.ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                _cts?.Dispose();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(FftProcessor));
        }
    }

    public enum FftWindowType
    {
        Hann,
        Hamming,
        Blackman,
        Bartlett,
        Kaiser
    }

    public sealed class SpectrumConverter : ISpectrumConverter
    {
        private readonly IGainParametersProvider _p;

        public SpectrumConverter(IGainParametersProvider p)
        {
            _p = p ?? throw new ArgumentNullException(nameof(p));
        }

        public float[] ConvertToSpectrum(Complex[] fft, int sampleRate)
        {
            int len = fft.Length / 2;
            var spectrum = new float[len];
            float minDb = _p.MinDbValue, range = _p.MaxDbValue - minDb, amp = _p.AmplificationFactor;

            int minIndex = Math.Max((int)(20 * (fft.Length / (float)sampleRate)), 0);
            int maxIndex = Math.Min((int)(20000 * (fft.Length / (float)sampleRate)), len - 1);

            for (int i = 0; i < len; i++)
            {
                if (i < minIndex || i > maxIndex)
                {
                    spectrum[i] = 0;
                    continue;
                }

                float magnitude = fft[i].X * fft[i].X + fft[i].Y * fft[i].Y;
                magnitude = magnitude == 0 ? float.Epsilon : magnitude;
                float db = 10 * MathF.Log10(magnitude);
                spectrum[i] = Math.Clamp((db - minDb) / range * amp, 0, 1);
            }

            return spectrum;
        }
    }
}