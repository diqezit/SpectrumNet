#nullable enable

using System.Buffers;

namespace SpectrumNet
{
    // Constants
    public static class Constants
    {
        public const float DefaultAmplificationFactor = 0.55f;
        public const float DefaultMaxDbValue = -20f;
        public const float DefaultMinDbValue = -100f;
        public const int DefaultFftSize = 2048;
        public const float Epsilon = float.Epsilon;
    }

    //Interfaces
    public interface IFftProcessor
    {
        event EventHandler<FftEventArgs> FftCalculated;
        ValueTask AddSamplesAsync(float[] samples, int sampleRate);
        ValueTask DisposeAsync();
    }

    public interface IGainParametersProvider
    {
        float AmplificationFactor { get; }
        float MaxDbValue { get; }
        float MinDbValue { get; }
    }

    public interface ISpectralDataProvider
    {
        event EventHandler<SpectralDataEventArgs> SpectralDataReady;
        SpectralData? GetCurrentSpectrum();
        Task AddSamplesAsync(float[] samples, int sampleRate, CancellationToken cancellationToken = default);
    }

    public interface ISpectrumConverter
    {
        float[] ConvertToSpectrum(NAudio.Dsp.Complex[] fftResult, int sampleRate);
    }

    // Event Arguments
    public class FftEventArgs : EventArgs
    {
        public NAudio.Dsp.Complex[] Result { get; }
        public int SampleRate { get; }

        public FftEventArgs(NAudio.Dsp.Complex[] result, int sampleRate)
        {
            Result = result;
            SampleRate = sampleRate;
        }
    }

    public class SpectralDataEventArgs : EventArgs
    {
        public SpectralData Data { get; }

        public SpectralDataEventArgs(SpectralData data) => Data = data;
    }

    // Data Records
    public record SpectralData(float[] Spectrum, DateTime Timestamp);

    // Implementation Classes

    public class GainParameters : IGainParametersProvider, INotifyPropertyChanged
    {
        private float _amp = Constants.DefaultAmplificationFactor;
        private float _min = Constants.DefaultMinDbValue;
        private float _max = Constants.DefaultMaxDbValue;
        private readonly SynchronizationContext? _context;

        public event PropertyChangedEventHandler? PropertyChanged = delegate { };

        public GainParameters(SynchronizationContext? context = null) => _context = context;

        public float AmplificationFactor
        {
            get => _amp;
            set => UpdateProperty(ref _amp, Math.Max(0.1f, value), nameof(AmplificationFactor));
        }

        public float MaxDbValue
        {
            get => _max;
            set => UpdateProperty(ref _max, Math.Max(value, _min), nameof(MaxDbValue));
        }

        public float MinDbValue
        {
            get => _min;
            set => UpdateProperty(ref _min, Math.Min(value, _max), nameof(MinDbValue));
        }

        private void UpdateProperty(ref float field, float value, [CallerMemberName] string propertyName = "")
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

        public event EventHandler<SpectralDataEventArgs> SpectralDataReady = delegate { };

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

        public SpectralData? GetCurrentSpectrum()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SpectrumAnalyzer));
            return _lastData;
        }

        public async Task AddSamplesAsync(float[] samples, int sampleRate, CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SpectrumAnalyzer));
            if (samples?.Length > 0)
                await _fftProcessor.AddSamplesAsync(samples, sampleRate).ConfigureAwait(false);
        }

        private void OnFftCalculated(object? sender, FftEventArgs e)
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

        public void Dispose()
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
    }

    public class FftProcessor : IFftProcessor, IAsyncDisposable
    {
        private readonly ArrayPool<NAudio.Dsp.Complex> _bufferPool = ArrayPool<NAudio.Dsp.Complex>.Shared;
        private readonly Channel<(float[] Samples, int SampleRate)> _channel;
        private readonly CancellationTokenSource _cts;
        private readonly NAudio.Dsp.Complex[] _buffer;
        private readonly Task _processingTask;
        private readonly int _fftSize;
        private int _sampleCount;

        public event EventHandler<FftEventArgs> FftCalculated = delegate { };

        public FftProcessor(int fftSize = Constants.DefaultFftSize)
        {
            if (fftSize <= 0 || (fftSize & (fftSize - 1)) != 0)
                throw new ArgumentException("FFT size must be a positive power of 2");

            _fftSize = fftSize;
            _buffer = _bufferPool.Rent(fftSize); // берем буфер из пула
            _channel = Channel.CreateUnbounded<(float[] Samples, int SampleRate)>(
                new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
            _cts = new CancellationTokenSource();
            _processingTask = Task.Run(() => ProcessSamplesAsync(_cts.Token));
        }

        public async ValueTask AddSamplesAsync(float[] samples, int sampleRate)
        {
            if (!_channel.Writer.TryWrite((samples, sampleRate)))
            {
                await _channel.Writer.WriteAsync((samples, sampleRate)).ConfigureAwait(false);
            }
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            await _processingTask.ConfigureAwait(false);
            _cts.Dispose();
            _bufferPool.Return(_buffer);
        }

        private async Task ProcessSamplesAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                if (_channel.Reader.TryRead(out var item))
                {
                    var (samples, sampleRate) = item;
                    ProcessSampleBlock(samples, sampleRate);
                }
                else
                {
                    // Добавляем небольшую задержку для предотвращения busy-waiting
                    await Task.Delay(1).ConfigureAwait(false);
                }
            }
        }

        private void ProcessSampleBlock(float[] samples, int sampleRate)
        {
            int index = 0;
            while (index < samples.Length)
            {
                int copyCount = Math.Min(_fftSize - _sampleCount, samples.Length - index);
                if (copyCount <= 0) break;

                CopySamplesToBuffer(samples.AsSpan(index, copyCount), _sampleCount);

                index += copyCount;
                _sampleCount += copyCount;

                if (_sampleCount >= _fftSize)
                {
                    FastFourierTransform.FFT(true, (int)Math.Log2(_fftSize), _buffer);
                    FftCalculated?.Invoke(this, new FftEventArgs(_buffer, sampleRate));
                    _sampleCount = 0;
                }
            }
        }

        private void CopySamplesToBuffer(ReadOnlySpan<float> samples, int bufferOffset)
        {
            Span<NAudio.Dsp.Complex> bufferSpan = _buffer.AsSpan(bufferOffset, samples.Length);

            int i = 0;
            int simdLength = Vector<float>.Count;

            // Обрабатываем данные с помощью SIMD
            while (i <= samples.Length - simdLength)
            {
                var vector = new Vector<float>(samples.Slice(i, simdLength));
                for (int j = 0; j < simdLength; j++)
                {
                    bufferSpan[i + j].X = vector[j];
                    bufferSpan[i + j].Y = 0;
                }
                i += simdLength;
            }

            // Обычная обработка оставшихся данных
            for (; i < samples.Length; i++)
            {
                bufferSpan[i].X = samples[i];
                bufferSpan[i].Y = 0;
            }
        }
    }

    public class SpectrumConverter : ISpectrumConverter
    {
        private readonly IGainParametersProvider _params;

        public SpectrumConverter(IGainParametersProvider parameters) =>
            _params = parameters ?? throw new ArgumentNullException(nameof(parameters));

        public unsafe float[] ConvertToSpectrum(NAudio.Dsp.Complex[] fftResult, int sampleRate)
        {
            int length = fftResult.Length / 2;
            var spectrum = new float[length];
            float minDb = _params.MinDbValue;
            float maxDbValueMinusMinDbValue = _params.MaxDbValue - minDb;
            float amplificationFactor = _params.AmplificationFactor;

            float[] realParts = fftResult.Select(c => c.X).ToArray();
            float[] imagParts = fftResult.Select(c => c.Y).ToArray();

            fixed (float* ptrReal = realParts, ptrImag = imagParts, ptrSpectrum = spectrum)
            {
                int vectorSize = Vector<float>.Count;
                int vectorizedLength = length - (length % vectorSize);

                for (int i = 0; i < vectorizedLength; i += vectorSize)
                {
                    Vector<float> xVector = new Vector<float>(realParts, i);
                    Vector<float> yVector = new Vector<float>(imagParts, i);
                    Vector<float> magnitudeVector = xVector * xVector + yVector * yVector;

                    magnitudeVector *= new Vector<float>(4f);
                    Vector<float> dbVector = new Vector<float>(10f) * VectorLog10(magnitudeVector);
                    Vector<float> valueVector = (dbVector - new Vector<float>(minDb)) / new Vector<float>(maxDbValueMinusMinDbValue) * new Vector<float>(amplificationFactor);

                    valueVector = VectorMax(VectorMin(valueVector, new Vector<float>(1f)), Vector<float>.Zero);
                    valueVector.CopyTo(spectrum, i);
                }

                for (int i = vectorizedLength; i < length; i++)
                {
                    float x = ptrReal[i];
                    float y = ptrImag[i];
                    float magnitude = x * x + y * y;
                    if (i != 0 && i != length - 1)
                        magnitude *= 4;

                    if (magnitude < float.Epsilon)
                        ptrSpectrum[i] = 0;
                    else
                    {
                        float db = 10 * MathF.Log10(magnitude);
                        float value = ((db - minDb) / maxDbValueMinusMinDbValue) * amplificationFactor;
                        ptrSpectrum[i] = value < 0 ? 0 : value > 1 ? 1 : value;
                    }
                }
            }

            return spectrum;
        }

        private static Vector<float> VectorLog10(Vector<float> x)
        {
            return VectorLog(x) * new Vector<float>(0.43429448190325f); // 1 / ln(10)
        }

        private static Vector<float> VectorLog(Vector<float> x)
        {
            float[] values = new float[Vector<float>.Count];
            x.CopyTo(values);

            for (int i = 0; i < values.Length; i++)
                values[i] = MathF.Log(values[i]);

            return new Vector<float>(values);
        }

        private static Vector<float> VectorMin(Vector<float> a, Vector<float> b)
        {
            float[] result = new float[Vector<float>.Count];
            for (int i = 0; i < result.Length; i++)
                result[i] = MathF.Min(a[i], b[i]);
            return new Vector<float>(result);
        }

        private static Vector<float> VectorMax(Vector<float> a, Vector<float> b)
        {
            float[] result = new float[Vector<float>.Count];
            for (int i = 0; i < result.Length; i++)
                result[i] = MathF.Max(a[i], b[i]);
            return new Vector<float>(result);
        }
    }
}