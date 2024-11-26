#nullable enable

namespace SpectrumNet
{
    public static class Constants
    {
        public const float DefaultAmplificationFactor = 0.55f;
        public const float DefaultMinDbValue = -100f;
        public const float DefaultMaxDbValue = -20f;
        public const float Epsilon = float.Epsilon;
        public const int DefaultFftSize = 2048;
    }

    public interface ISpectralDataProvider : IDisposable
    {
        event EventHandler<SpectralDataEventArgs> SpectralDataReady;
        SpectralData? GetCurrentSpectrum();
        Task AddSamplesAsync(float[] samples, int sampleRate, CancellationToken cancellationToken = default);
    }

    public interface IFftProcessor : IDisposable
    {
        event EventHandler<FftEventArgs> FftCalculated;
        Task AddSamplesAsync(float[] samples, int sampleRate);
    }

    public interface ISpectrumConverter
    {
        float[] ConvertToSpectrum(Complex[] fftResult, int sampleRate);
    }

    public interface IGainParametersProvider
    {
        float AmplificationFactor { get; }
        float MinDbValue { get; }
        float MaxDbValue { get; }
    }

    public record SpectralData(float[] Spectrum, DateTime Timestamp);

    public class SpectralDataEventArgs : EventArgs
    {
        public SpectralData Data { get; }
        public SpectralDataEventArgs(SpectralData data) => Data = data ?? throw new ArgumentNullException(nameof(data));
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

    public class GainParameters : IGainParametersProvider
    {
        public float AmplificationFactor { get; set; } = Constants.DefaultAmplificationFactor;
        public float MinDbValue { get; set; } = Constants.DefaultMinDbValue;
        public float MaxDbValue { get; set; } = Constants.DefaultMaxDbValue;
    }

    public class FftProcessor : IFftProcessor
    {
        private readonly Channel<(float[] Samples, int SampleRate)> _sampleChannel;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _processingTask;
        private readonly Complex[] _fftBuffer;
        private readonly int _fftSize;
        private int _sampleCount;

        public event EventHandler<FftEventArgs>? FftCalculated;

        public FftProcessor(int fftSize = Constants.DefaultFftSize)
        {
            ValidateFftSize(fftSize);
            _fftSize = fftSize;
            _fftBuffer = new Complex[fftSize];
            _sampleChannel = Channel.CreateUnbounded<(float[], int)>(new UnboundedChannelOptions { SingleReader = true });
            _processingTask = ProcessSamplesAsync(_cts.Token);
        }

        private static void ValidateFftSize(int fftSize)
        {
            if (fftSize <= 0 || (fftSize & (fftSize - 1)) != 0)
                throw new ArgumentException("FFT size must be a positive power of 2", nameof(fftSize));
        }

        public Task AddSamplesAsync(float[] samples, int sampleRate)
            => _sampleChannel.Writer.WriteAsync((samples, sampleRate)).AsTask();

        private async Task ProcessSamplesAsync(CancellationToken cancellationToken)
        {
            await foreach (var (samples, sampleRate) in _sampleChannel.Reader.ReadAllAsync(cancellationToken))
                ProcessSampleBatch(samples, sampleRate);
        }

        private void ProcessSampleBatch(float[] samples, int sampleRate)
        {
            int inputIndex = 0;
            while (inputIndex < samples.Length)
            {
                int samplesToCopy = Math.Min(_fftSize - _sampleCount, samples.Length - inputIndex);
                if (samplesToCopy <= 0) break;

                for (int i = 0; i < samplesToCopy; i++)
                {
                    _fftBuffer[_sampleCount + i] = new Complex { X = samples[inputIndex + i], Y = 0 };
                }

                inputIndex += samplesToCopy;
                _sampleCount += samplesToCopy;

                if (_sampleCount >= _fftSize)
                    ProcessFftBuffer(sampleRate);
            }
        }

        private void ProcessFftBuffer(int sampleRate)
        {
            var fftBufferCopy = new Complex[_fftSize];
            Array.Copy(_fftBuffer, fftBufferCopy, _fftSize);
            FastFourierTransform.FFT(true, (int)Math.Log(_fftSize, 2.0), fftBufferCopy);
            FftCalculated?.Invoke(this, new FftEventArgs(fftBufferCopy, sampleRate));
            Array.Clear(_fftBuffer, 0, _fftBuffer.Length);
            _sampleCount = 0;
        }

        public void Dispose()
        {
            _cts.Cancel();
            _processingTask.Wait();
            _cts.Dispose();
        }
    }

    public class SpectrumConverter : ISpectrumConverter
    {
        private readonly IGainParametersProvider _gainParameters;

        public SpectrumConverter(IGainParametersProvider gainParameters)
        {
            _gainParameters = gainParameters ?? throw new ArgumentNullException(nameof(gainParameters));
        }

        public float[] ConvertToSpectrum(Complex[] fftResult, int sampleRate)
        {
            int length = fftResult.Length / 2;
            var spectrum = new float[length];

            for (int i = 0; i < length; i++)
            {
                float magnitude = (float)Math.Sqrt(
                    fftResult[i].X * fftResult[i].X +
                    fftResult[i].Y * fftResult[i].Y
                );
                magnitude *= (i == 0 || i == length - 1) ? 1 : 2;

                float db = 20 * (float)Math.Log10(magnitude);
                spectrum[i] = Math.Clamp(
                    ((db - _gainParameters.MinDbValue) /
                    (_gainParameters.MaxDbValue - _gainParameters.MinDbValue)) *
                    _gainParameters.AmplificationFactor, 0, 1);
            }

            return spectrum;
        }
    }

    public class SpectrumAnalyzer : ISpectralDataProvider
    {
        private readonly IFftProcessor _fftProcessor;
        private readonly ISpectrumConverter _spectrumConverter;
        private SpectralData? _lastSpectralData;
        private bool _disposed;

        public event EventHandler<SpectralDataEventArgs>? SpectralDataReady;

        public SpectrumAnalyzer(IFftProcessor fftProcessor, ISpectrumConverter spectrumConverter)
        {
            _fftProcessor = fftProcessor ?? throw new ArgumentNullException(nameof(fftProcessor));
            _spectrumConverter = spectrumConverter ?? throw new ArgumentNullException(nameof(spectrumConverter));
            _fftProcessor.FftCalculated += OnFftCalculated;
        }

        public SpectralData? GetCurrentSpectrum()
        {
            ThrowIfDisposed();
            return _lastSpectralData;
        }

        public Task AddSamplesAsync(float[] samples, int sampleRate, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return samples == null || samples.Length == 0
                ? Task.CompletedTask
                : _fftProcessor.AddSamplesAsync(samples, sampleRate);
        }

        private void OnFftCalculated(object? sender, FftEventArgs e)
        {
            var spectrum = _spectrumConverter.ConvertToSpectrum(e.Result, e.SampleRate);
            var spectralData = new SpectralData(spectrum, DateTime.UtcNow);
            _lastSpectralData = spectralData;
            SpectralDataReady?.Invoke(this, new SpectralDataEventArgs(spectralData));
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SpectrumAnalyzer));
        }

        public void Dispose()
        {
            if (_disposed) return;

            _fftProcessor.FftCalculated -= OnFftCalculated;
            _fftProcessor.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}