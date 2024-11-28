using Vector = System.Numerics.Vector;
using Complex = NAudio.Dsp.Complex;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics;

#nullable enable

namespace SpectrumNet
{
    // Constants
    public static class Constants
    {
        public const float DefaultAmplificationFactor = 0.75f;
        public const float DefaultMaxDbValue = 0f;
        public const float DefaultMinDbValue = -200f;
        public const int DefaultFftSize = 2048;
        public const float Epsilon = float.Epsilon;
    }

    // Interfaces
    public interface IFftProcessor
    {
        event EventHandler<FftEventArgs>? FftCalculated;
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
        event EventHandler<SpectralDataEventArgs>? SpectralDataReady;
        SpectralData? GetCurrentSpectrum();
        Task AddSamplesAsync(float[] samples, int sampleRate, CancellationToken cancellationToken = default);
    }

    public interface ISpectrumConverter
    {
        float[] ConvertToSpectrum(Complex[] fftResult, int sampleRate);
    }

    // Event Arguments
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

        public SpectralDataEventArgs(SpectralData data) => Data = data ?? throw new ArgumentNullException(nameof(data));
    }

    // Data Records
    public record SpectralData(float[] Spectrum, DateTime Timestamp);

    // Implementation Classes
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

        public event EventHandler<SpectralDataEventArgs>? SpectralDataReady;

        public SpectrumAnalyzer(IFftProcessor fftProcessor, ISpectrumConverter converter, SynchronizationContext? context = null)
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
            if (e?.Result?.Length > 0)
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

    public sealed class FftProcessor : IFftProcessor, IAsyncDisposable
    {
        private readonly ArrayPool<Complex> _bufferPool = ArrayPool<Complex>.Shared;
        private readonly ArrayPool<float> _windowPool = ArrayPool<float>.Shared;
        private readonly Channel<(float[] Samples, int SampleRate)> _channel;
        private readonly CancellationTokenSource _cts;
        private readonly Complex[] _buffer;
        private readonly float[] _window;
        private readonly Task _processingTask;
        private readonly int _fftSize;
        private readonly int _log2FftSize;
        private readonly AutoResetEvent _dataReadyEvent = new(false);
        private int _sampleCount;

        public event EventHandler<FftEventArgs>? FftCalculated;

        public FftProcessor(int fftSize = Constants.DefaultFftSize)
        {
            if (fftSize <= 0 || (fftSize & (fftSize - 1)) != 0)
                throw new ArgumentException("FFT size must be a positive power of 2");

            _fftSize = fftSize;
            _log2FftSize = (int)Math.Log2(fftSize);
            _buffer = _bufferPool.Rent(fftSize);
            _window = _windowPool.Rent(fftSize);
            InitializeHannWindow();

            _channel = Channel.CreateUnbounded<(float[] Samples, int SampleRate)>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = true
                });

            _cts = new CancellationTokenSource();
            _processingTask = Task.Run(ProcessSamplesAsync);
        }

        private void InitializeHannWindow()
        {
            double multiplier = 2.0 * Math.PI / (_fftSize - 1);
            for (int i = 0; i < _fftSize; i++)
            {
                _window[i] = (float)(0.5 * (1.0 - Math.Cos(i * multiplier)));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValueTask AddSamplesAsync(float[] samples, int sampleRate)
        {
            if (_channel.Writer.TryWrite((samples, sampleRate)))
            {
                _dataReadyEvent.Set();
                return default;
            }
            return new ValueTask(_channel.Writer.WriteAsync((samples, sampleRate)).AsTask());
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _dataReadyEvent.Set();
            await _processingTask.ConfigureAwait(false);
            _cts.Dispose();
            _bufferPool.Return(_buffer);
            _windowPool.Return(_window);
            _dataReadyEvent.Dispose();
        }

        private async Task ProcessSamplesAsync()
        {
            try
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    if (_channel.Reader.TryRead(out var item))
                    {
                        ProcessSampleBlock(item.Samples, item.SampleRate);
                    }
                    else
                    {
                        await Task.Run(() => _dataReadyEvent.WaitOne());
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

        private void ProcessSampleBlock(float[] samples, int sampleRate)
        {
            int index = 0;
            while (index < samples.Length)
            {
                int copyCount = Math.Min(_fftSize - _sampleCount, samples.Length - index);
                if (copyCount <= 0) break;

                CopySamplesToBuffer(
                    samples.AsSpan(index, copyCount),
                    _buffer.AsSpan(_sampleCount, copyCount),
                    _window.AsSpan(_sampleCount, copyCount));

                index += copyCount;
                _sampleCount += copyCount;

                if (_sampleCount >= _fftSize)
                {
                    FastFourierTransform.FFT(true, _log2FftSize, _buffer);
                    FftCalculated?.Invoke(this, new FftEventArgs(_buffer, sampleRate));
                    _sampleCount = 0;
                }
            }
        }

        private void CopySamplesToBuffer(ReadOnlySpan<float> samples, Span<Complex> buffer, ReadOnlySpan<float> window)
        {
            if (Vector.IsHardwareAccelerated && samples.Length >= Vector<float>.Count)
            {
                int vectorSize = Vector<float>.Count;
                int i = 0;

                for (; i <= samples.Length - vectorSize; i += vectorSize)
                {
                    var sampleVector = new Vector<float>(samples.Slice(i, vectorSize));
                    var windowVector = new Vector<float>(window.Slice(i, vectorSize));
                    var multipliedVector = sampleVector * windowVector;

                    for (int j = 0; j < vectorSize; j++)
                    {
                        buffer[i + j].X = multipliedVector[j];
                        buffer[i + j].Y = 0;
                    }
                }

                for (; i < samples.Length; i++)
                {
                    buffer[i].X = samples[i] * window[i];
                    buffer[i].Y = 0;
                }
            }
            else
            {
                for (int i = 0; i < samples.Length; i++)
                {
                    buffer[i].X = samples[i] * window[i];
                    buffer[i].Y = 0;
                }
            }
        }
    }

    public sealed class SpectrumConverter : ISpectrumConverter
    {
        private readonly IGainParametersProvider _params;

        public SpectrumConverter(IGainParametersProvider parameters) =>
            _params = parameters ?? throw new ArgumentNullException(nameof(parameters));

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public unsafe float[] ConvertToSpectrum(Complex[] fftResult, int sampleRate)
        {
            int length = fftResult.Length / 2;
            var spectrum = GC.AllocateUninitializedArray<float>(length, pinned: true);

            float minDb = _params.MinDbValue;
            float maxDbRange = _params.MaxDbValue - minDb;
            float amp = _params.AmplificationFactor;

            const float log10Mult = 10f, fourMult = 4f;

            fixed (Complex* ptrIn = fftResult)
            fixed (float* ptrOut = spectrum)
            {
                if (Avx.IsSupported && length >= 8)
                    ProcessAvx(ptrIn, ptrOut, length, minDb, maxDbRange, amp, log10Mult, fourMult);
                else if (Sse.IsSupported && length >= 4)
                    ProcessSse(ptrIn, ptrOut, length, minDb, maxDbRange, amp, log10Mult, fourMult);
                else
                    for (int i = 0; i < length; i++)
                        ProcessScalar(ptrIn[i], ptrOut + i, minDb, maxDbRange, amp, log10Mult, fourMult, i, length);
            }
            return spectrum;
        }

        private static unsafe void ProcessAvx(Complex* input, float* output, int len, float minDb, float maxDbRange,
            float amp, float log10Mult, float fourMult)
        {
            int i = 0, aligned = len - (len % 8);
            var vectors = new
            {
                MinDb = Vector256.Create(minDb),
                Amp = Vector256.Create(amp),
                MaxRange = Vector256.Create(maxDbRange),
                FourMult = Vector256.Create(fourMult),
                Log10 = Vector256.Create(log10Mult),
                Ones = Vector256.Create(1.0f),
                Zeros = Vector256<float>.Zero
            };

            for (; i < aligned; i += 8)
            {
                var complex0 = Avx.LoadVector256((float*)(input + i));
                var complex1 = Avx.LoadVector256((float*)(input + i + 4));
                var mags = Avx.Multiply(Avx.Add(Avx.Multiply(complex0, complex0),
                                              Avx.Multiply(complex1, complex1)),
                                      vectors.FourMult);

                float* magPtr = (float*)&mags;
                var logVals = Vector256.Create(MathF.Log(magPtr[0]), MathF.Log(magPtr[1]),
                    MathF.Log(magPtr[2]), MathF.Log(magPtr[3]), MathF.Log(magPtr[4]),
                    MathF.Log(magPtr[5]), MathF.Log(magPtr[6]), MathF.Log(magPtr[7]));

                var normalized = Avx.Multiply(Avx.Divide(Avx.Subtract(Avx.Multiply(vectors.Log10, logVals),
                    vectors.MinDb), vectors.MaxRange), vectors.Amp);
                Avx.Store(output + i, Avx.Min(Avx.Max(normalized, vectors.Zeros), vectors.Ones));
            }

            for (; i < len; i++)
                ProcessScalar(input[i], output + i, minDb, maxDbRange, amp, log10Mult, fourMult, i, len);
        }

        private static unsafe void ProcessSse(Complex* input, float* output, int len, float minDb, float maxDbRange,
            float amp, float log10Mult, float fourMult)
        {
            int i = 0, aligned = len - (len % 4);
            var vectors = new
            {
                MinDb = Vector128.Create(minDb),
                Amp = Vector128.Create(amp),
                MaxRange = Vector128.Create(maxDbRange),
                FourMult = Vector128.Create(fourMult),
                Log10 = Vector128.Create(log10Mult),
                Ones = Vector128.Create(1.0f),
                Zeros = Vector128<float>.Zero
            };

            for (; i < aligned; i += 4)
            {
                var complex = Sse.LoadVector128((float*)(input + i));
                var mags = Sse.Multiply(Sse.Add(Sse.Multiply(complex, complex),
                                              Sse.Multiply(complex, complex)),
                                      vectors.FourMult);

                float* magPtr = (float*)&mags;
                var logVals = Vector128.Create(
                    MathF.Log(magPtr[0]),
                    MathF.Log(magPtr[1]),
                    MathF.Log(magPtr[2]),
                    MathF.Log(magPtr[3])
                );

                var normalized = Sse.Multiply(
                    Sse.Divide(
                        Sse.Subtract(
                            Sse.Multiply(vectors.Log10, logVals),
                            vectors.MinDb
                        ),
                        vectors.MaxRange
                    ),
                    vectors.Amp
                );

                Sse.Store(output + i,
                    Sse.Min(
                        Sse.Max(normalized, vectors.Zeros),
                        vectors.Ones
                    )
                );
            }

            for (; i < len; i++)
                ProcessScalar(input[i], output + i, minDb, maxDbRange, amp, log10Mult, fourMult, i, len);
        }

        private static unsafe void ProcessScalar(Complex input, float* output, float minDb, float maxDbRange,
            float amp, float log10Mult, float fourMult, int idx, int len)
        {
            float mag = input.X * input.X + input.Y * input.Y;
            if (idx != 0 && idx != len - 1) mag *= fourMult;

            if (mag < float.Epsilon) *output = 0;
            else
            {
                float db = log10Mult * MathF.Log10(mag);
                *output = Math.Clamp(((db - minDb) / maxDbRange) * amp, 0, 1);
            }
        }
    }
}