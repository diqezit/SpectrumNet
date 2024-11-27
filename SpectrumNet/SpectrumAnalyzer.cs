using Vector = System.Numerics.Vector;
using Complex = NAudio.Dsp.Complex;

#nullable enable

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
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _amp;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => UpdateProperty(ref _amp, Math.Max(0.1f, value), nameof(AmplificationFactor));
        }

        public float MaxDbValue
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _max;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => UpdateProperty(ref _max, Math.Max(value, _min), nameof(MaxDbValue));
        }

        public float MinDbValue
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _min;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => UpdateProperty(ref _min, Math.Min(value, _max), nameof(MinDbValue));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
        private readonly Channel<(float[] Samples, int SampleRate)> _channel;
        private readonly CancellationTokenSource _cts;
        private readonly Complex[] _buffer;
        private readonly Task _processingTask;
        private readonly int _fftSize;
        private readonly AutoResetEvent _dataReadyEvent = new(false);
        private int _sampleCount;

        public event EventHandler<FftEventArgs>? FftCalculated;

        public FftProcessor(int fftSize = Constants.DefaultFftSize)
        {
            if (fftSize <= 0 || (fftSize & (fftSize - 1)) != 0)
                throw new ArgumentException("FFT size must be a positive power of 2");

            _fftSize = fftSize;
            _buffer = _bufferPool.Rent(fftSize);
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
            _dataReadyEvent.Set();
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _dataReadyEvent.Set();
            await _processingTask.ConfigureAwait(false);

            _cts.Dispose();
            _bufferPool.Return(_buffer);
            _dataReadyEvent.Dispose();
        }

        private async Task ProcessSamplesAsync(CancellationToken ct)
        {
            try
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
                        _dataReadyEvent.WaitOne();
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
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
            Span<Complex> bufferSpan = _buffer.AsSpan(bufferOffset, samples.Length);

            int i = 0;
            int simdLength = Vector<float>.Count;

            while (i <= samples.Length - simdLength)
            {
                var vector = new Vector<float>(samples.Slice(i, simdLength));
                Vector.Widen(vector, out var low, out var high);

                for (int j = 0; j < simdLength / 2; j++)
                {
                    bufferSpan[i + j].X = (float)low[j];
                    bufferSpan[i + j].Y = 0;
                    bufferSpan[i + j + simdLength / 2].X = (float)high[j];
                    bufferSpan[i + j + simdLength / 2].Y = 0;
                }

                i += simdLength;
            }

            for (; i < samples.Length; i++)
            {
                bufferSpan[i].X = samples[i];
                bufferSpan[i].Y = 0;
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
            float maxDbValueMinusMinDbValue = _params.MaxDbValue - minDb;
            float amplificationFactor = _params.AmplificationFactor;

            const float logBase10Multiplier = 10f;
            const float fourMultiplier = 4f;
            const float epsilon = float.Epsilon;

            fixed (Complex* ptrInput = fftResult)
            fixed (float* ptrSpectrum = spectrum)
            {
                int vectorSize = Vector<float>.Count;
                int vectorizedLength = length - (length % vectorSize);

                for (int i = 0; i < vectorizedLength; i += vectorSize)
                {
                    ProcessVectorizedSpectrum(
                        ptrInput + i,
                        ptrSpectrum + i,
                        minDb,
                        maxDbValueMinusMinDbValue,
                        amplificationFactor,
                        logBase10Multiplier,
                        fourMultiplier,
                        vectorSize
                    );
                }

                for (int i = vectorizedLength; i < length; i++)
                {
                    ProcessScalarSpectrum(
                        ptrInput[i],
                        ptrSpectrum + i,
                        minDb,
                        maxDbValueMinusMinDbValue,
                        amplificationFactor,
                        logBase10Multiplier,
                        fourMultiplier,
                        i,
                        length,
                        epsilon
                    );
                }
            }

            return spectrum;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void ProcessVectorizedSpectrum(
            Complex* inputPtr,
            float* outputPtr,
            float minDb,
            float maxDbValueMinusMinDbValue,
            float amplificationFactor,
            float logBase10Multiplier,
            float fourMultiplier,
            int vectorSize)
        {
            Span<float> realParts = MemoryMarshal.Cast<Complex, float>(new Span<Complex>(inputPtr, vectorSize));
            Span<float> imagParts = realParts.Slice(1, vectorSize);

            var xVector = new Vector<float>(realParts);
            var yVector = new Vector<float>(imagParts);

            var magnitudeVector = Vector.Multiply(xVector, xVector) + Vector.Multiply(yVector, yVector);
            magnitudeVector = Vector.Multiply(magnitudeVector, new Vector<float>(fourMultiplier));

            var dbVector = Vector.Multiply(new Vector<float>(logBase10Multiplier), VectorLog10(magnitudeVector));
            var valueVector = Vector.Divide(
                Vector.Multiply(
                    Vector.Subtract(dbVector, new Vector<float>(minDb)),
                    new Vector<float>(amplificationFactor)
                ),
                new Vector<float>(maxDbValueMinusMinDbValue)
            );

            var clampedVector = Vector.Max(Vector.Min(valueVector, new Vector<float>(1f)), Vector<float>.Zero);
            clampedVector.CopyTo(new Span<float>(outputPtr, vectorSize));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void ProcessScalarSpectrum(
            Complex input,
            float* outputPtr,
            float minDb,
            float maxDbValueMinusMinDbValue,
            float amplificationFactor,
            float logBase10Multiplier,
            float fourMultiplier,
            int index,
            int length,
            float epsilon)
        {
            float x = input.X;
            float y = input.Y;
            float magnitude = x * x + y * y;

            if (index != 0 && index != length - 1)
                magnitude *= fourMultiplier;

            if (magnitude < epsilon)
                *outputPtr = 0;
            else
            {
                float db = logBase10Multiplier * MathF.Log10(magnitude);
                float value = ((db - minDb) / maxDbValueMinusMinDbValue) * amplificationFactor;
                *outputPtr = Math.Clamp(value, 0, 1);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector<float> VectorLog10(Vector<float> x) =>
            VectorLog(x) * new Vector<float>(0.43429448190325f);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector<float> VectorLog(Vector<float> x)
        {
            float[] values = new float[Vector<float>.Count];
            x.CopyTo(values);

            for (int i = 0; i < values.Length; i++)
                values[i] = MathF.Log(values[i]);

            return new Vector<float>(values);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector<float> VectorMin(Vector<float> a, Vector<float> b)
        {
            float[] result = new float[Vector<float>.Count];
            for (int i = 0; i < result.Length; i++)
                result[i] = MathF.Min(a[i], b[i]);
            return new Vector<float>(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector<float> VectorMax(Vector<float> a, Vector<float> b)
        {
            float[] result = new float[Vector<float>.Count];
            for (int i = 0; i < result.Length; i++)
                result[i] = MathF.Max(a[i], b[i]);
            return new Vector<float>(result);
        }
    }
}