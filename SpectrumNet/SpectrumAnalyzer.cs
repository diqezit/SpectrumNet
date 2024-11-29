using Complex = NAudio.Dsp.Complex;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics;
using MathNet.Numerics.IntegralTransforms;
using MathNet.Numerics;

#nullable enable

namespace SpectrumNet
{
    public static class Constants
    {
        public const float DefaultAmplificationFactor = 0.25f;
        public const float DefaultMaxDbValue = -30f;
        public const float DefaultMinDbValue = -130f;
        public const int DefaultFftSize = 2048;
        public const float Epsilon = float.Epsilon;
    }

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

    public sealed class FftProcessor : IFftProcessor, IAsyncDisposable, IDisposable
    {
        private readonly ArrayPool<Complex> _bufferPool = ArrayPool<Complex>.Shared;
        private readonly Channel<(float[] Samples, int SampleRate)> _channel;
        private readonly CancellationTokenSource _cts = new();
        private readonly Complex[] _buffer;
        private readonly float[] _hannWindow;
        private readonly Task _processingTask;
        private readonly int _fftSize;
        private int _sampleCount;
        private static readonly bool _avxSupported = Avx.IsSupported;

        public event EventHandler<FftEventArgs>? FftCalculated;

        public FftProcessor(int fftSize = Constants.DefaultFftSize)
        {
            if (fftSize <= 0 || (fftSize & (fftSize - 1)) != 0)
                throw new ArgumentException("FFT size must be a positive power of 2.");

            _fftSize = fftSize;
            _buffer = _bufferPool.Rent(fftSize);
            _hannWindow = GenerateHannWindow(fftSize); // Предвычисляем окно Hann
            _channel = Channel.CreateUnbounded<(float[] Samples, int SampleRate)>(
                new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
            _processingTask = Task.Run(ProcessSamplesAsync, _cts.Token);
        }

        public async ValueTask AddSamplesAsync(float[] samples, int sampleRate)
        {
            if (samples == null || sampleRate <= 0 || samples.Any(float.IsNaN))
                throw new ArgumentException("Invalid samples or sample rate.");

            if (!_channel.Writer.TryWrite((samples, sampleRate)))
            {
                await _channel.Writer.WriteAsync((samples, sampleRate)).ConfigureAwait(false);
            }
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _channel.Writer.TryComplete();
            await _processingTask.ConfigureAwait(false);
            DisposeResources();
        }

        public void Dispose()
        {
            _cts.Cancel();
            DisposeResources();
        }

        private void DisposeResources()
        {
            _cts.Dispose();
            if (_buffer != null)
            {
                try
                {
                    _bufferPool.Return(_buffer);
                }
                catch (ArgumentException ex)
                {
                    Log.Warning($"Attempted to return an invalid buffer: {ex.Message}");
                }
            }
        }

        private async Task ProcessSamplesAsync()
        {
            try
            {
                while (await _channel.Reader.WaitToReadAsync(_cts.Token).ConfigureAwait(false))
                {
                    while (_channel.Reader.TryRead(out var item))
                        ProcessSampleBlock(item.Samples, item.SampleRate);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.Error($"[FftProcessor] Error: {ex}");
            }
        }

        private void ProcessSampleBlock(float[] samples, int sampleRate)
        {
            int index = 0;

            while (index < samples.Length)
            {
                int copyCount = Math.Min(_fftSize - _sampleCount, samples.Length - index);
                if (copyCount <= 0) break;

                ApplyWindowAndCopyWithAvx(samples, index, copyCount);

                index += copyCount;
                _sampleCount += copyCount;

                if (_sampleCount >= _fftSize)
                {
                    // Преобразуем для Math.NET FFT
                    var mathNetBuffer = new Complex32[_fftSize];
                    for (int i = 0; i < _fftSize; i++)
                    {
                        mathNetBuffer[i] = new Complex32((float)_buffer[i].X, (float)_buffer[i].Y);
                    }

                    // FFT
                    Fourier.Forward(mathNetBuffer, FourierOptions.Matlab);

                    // Обратно в _buffer
                    for (int i = 0; i < _fftSize; i++)
                    {
                        _buffer[i].X = mathNetBuffer[i].Real;
                        _buffer[i].Y = mathNetBuffer[i].Imaginary;
                    }

                    // Выполняем событие только после обработки блока
                    FftCalculated?.Invoke(this, new FftEventArgs(_buffer, sampleRate));
                    _sampleCount = 0;
                }
            }
        }

        private void ApplyWindowAndCopyWithAvx(float[] samples, int startIndex, int count)
        {
            int i = 0;
            if (_avxSupported)
            {
                unsafe
                {
                    fixed (float* samplesPtr = &samples[startIndex])
                    fixed (float* windowPtr = &_hannWindow[_sampleCount])
                    {
                        while (i + Vector256<float>.Count <= count)
                        {
                            var input = Avx.LoadVector256(samplesPtr + i);
                            var window = Avx.LoadVector256(windowPtr + i);
                            var result = Avx.Multiply(input, window);

                            for (int j = 0; j < Vector256<float>.Count; j++)
                            {
                                _buffer[_sampleCount + i + j].X = result.GetElement(j);
                                _buffer[_sampleCount + i + j].Y = 0;
                            }

                            i += Vector256<float>.Count;
                        }
                    }
                }
            }

            // Обработка оставшихся элементов без SIMD
            for (; i < count; i++)
            {
                int bufferIndex = _sampleCount + i;
                _buffer[bufferIndex].X = samples[startIndex + i] * _hannWindow[bufferIndex];
                _buffer[bufferIndex].Y = 0;
            }
        }

        private static float[] GenerateHannWindow(int size)
        {
            float[] window = new float[size];
            for (int i = 0; i < size; i++)
                window[i] = (float)(0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (size - 1))));
            return window;
        }
    }

    public sealed class SpectrumConverter : ISpectrumConverter
    {
        private readonly IGainParametersProvider _p;
        private const float L10M = 10f, F4M = 4f, Z = 0f, O = 1f;

        public SpectrumConverter(IGainParametersProvider p) => _p = p ?? throw new ArgumentNullException(nameof(p));

        public unsafe float[] ConvertToSpectrum(Complex[] fft, int sr)
        {
            int len = fft.Length / 2;
            var s = GC.AllocateUninitializedArray<float>(len, pinned: true);
            float minDb = _p.MinDbValue, range = _p.MaxDbValue - minDb, amp = _p.AmplificationFactor;

            fixed (Complex* inPtr = fft)
            fixed (float* outPtr = s)
            {
                if (Avx.IsSupported && len >= 8)
                    ProcAvx(inPtr, outPtr, len, minDb, range, amp);
                else if (Sse.IsSupported && len >= 4)
                    ProcSse(inPtr, outPtr, len, minDb, range, amp);
                else
                    for (int i = 0; i < len; i++)
                        ProcScalar(inPtr[i], outPtr + i, minDb, range, amp, i, len);
            }
            return s;
        }

        private static unsafe void ProcAvx(Complex* inPtr, float* outPtr, int len, float minDb, float range, float amp)
        {
            int i = 0, aligned = len - (len % 8);
            var v = new
            {
                MinDb = Vector256.Create(minDb),
                Amp = Vector256.Create(amp),
                Range = Vector256.Create(range),
                F4 = Vector256.Create(F4M),
                L10 = Vector256.Create(L10M),
                O = Vector256.Create(O),
                Z = Vector256<float>.Zero
            };

            for (; i < aligned; i += 8)
            {
                var c0 = Avx.LoadVector256((float*)(inPtr + i));
                var c1 = Avx.LoadVector256((float*)(inPtr + i + 4));
                var mags = Avx.Multiply(Avx.Add(Avx.Multiply(c0, c0), Avx.Multiply(c1, c1)), v.F4);

                float* m = (float*)&mags;
                Vector256<float> logs = Vector256<float>.Zero;

                for (int j = 0; j < 8; j++)
                {
                    m[j] = m[j] < float.Epsilon ? 0f : (L10M * (float)MathF.Log(m[j]) - minDb) / range * amp;
                    m[j] = Math.Min(Math.Max(m[j], 0f), 1f);
                }

                logs = Avx.LoadVector256(m);
                Avx.Store(outPtr + i, logs);
            }

            for (; i < len; i++)
                ProcScalar(inPtr[i], outPtr + i, minDb, range, amp, i, len);
        }

        private static unsafe void ProcSse(Complex* inPtr, float* outPtr, int len, float minDb, float range, float amp)
        {
            int i = 0, aligned = len - (len % 4);
            var v = new
            {
                MinDb = Vector128.Create(minDb),
                Amp = Vector128.Create(amp),
                Range = Vector128.Create(range),
                F4 = Vector128.Create(F4M),
                L10 = Vector128.Create(L10M),
                O = Vector128.Create(O),
                Z = Vector128<float>.Zero
            };

            for (; i < aligned; i += 4)
            {
                var c = Sse.LoadVector128((float*)(inPtr + i));
                var mags = Sse.Multiply(Sse.Add(Sse.Multiply(c, c), Sse.Multiply(c, c)), v.F4);

                float* m = (float*)&mags;
                Vector128<float> logs = Vector128<float>.Zero;

                for (int j = 0; j < 4; j++)
                {
                    m[j] = m[j] < float.Epsilon ? 0f : (L10M * (float)MathF.Log(m[j]) - minDb) / range * amp;
                    m[j] = Math.Min(Math.Max(m[j], 0f), 1f);
                }

                logs = Sse.LoadVector128(m);
                Sse.Store(outPtr + i, logs);
            }

            for (; i < len; i++)
                ProcScalar(inPtr[i], outPtr + i, minDb, range, amp, i, len);
        }

        private static unsafe void ProcScalar(Complex inPtr, float* outPtr, float minDb, float range, float amp, int i, int len)
        {
            float mag = inPtr.X * inPtr.X + inPtr.Y * inPtr.Y;
            if (i != 0 && i != len - 1) mag *= F4M;
            *outPtr = mag < float.Epsilon ? Z : (L10M * (float)MathF.Log10(mag) - minDb) / range * amp;
            *outPtr = Math.Clamp(*outPtr, Z, O);
        }
    }
}