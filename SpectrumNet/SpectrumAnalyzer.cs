namespace SpectrumNet
{
    public static class Constants
    {
        public const float DefaultAmplificationFactor = 0.55f;
        public const float DefaultMinDbValue = -100f;
        public const float DefaultMaxDbValue = -20f;
        public const float Epsilon = float.Epsilon;
        public const int DefaultFftSize = 1024;
    }

    public interface ISpectralDataProvider
    {
        event EventHandler<SpectralDataEventArgs> SpectralDataReady;
        SpectralData GetCurrentSpectrum();
        Task AddSamplesAsync(float[] samples, int sampleRate, CancellationToken cancellationToken = default);
    }

    public interface IFftProcessor
    {
        event EventHandler<FftEventArgs> FftCalculated;
        ValueTask AddSamplesAsync(float[] samples, int sampleRate);
        ValueTask DisposeAsync();
    }

    public interface ISpectrumConverter
    {
        float[] ConvertToSpectrum(NAudio.Dsp.Complex[] fftResult, int sampleRate);
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
        public NAudio.Dsp.Complex[] Result { get; }
        public int SampleRate { get; }
        public FftEventArgs(NAudio.Dsp.Complex[] result, int sampleRate)
        {
            Result = result ?? throw new ArgumentNullException(nameof(result));
            SampleRate = sampleRate;
        }
    }

    public class FftProcessor : IFftProcessor
    {
        private readonly Channel<(float[] Samples, int SampleRate)> _sampleChannel;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly Task _processingTask;
        private readonly NAudio.Dsp.Complex[] _fftBuffer;
        private readonly int _fftSize;
        private int _sampleCount;

        public event EventHandler<FftEventArgs> FftCalculated;

        public FftProcessor(int fftSize = Constants.DefaultFftSize)
        {
            ValidateFftSize(fftSize);
            _fftSize = fftSize;
            _fftBuffer = new NAudio.Dsp.Complex[fftSize];
            _sampleChannel = Channel.CreateUnbounded<(float[] Samples, int SampleRate)>(new UnboundedChannelOptions { SingleReader = true });
            _cancellationTokenSource = new CancellationTokenSource();
            _processingTask = ProcessSamplesAsync(_cancellationTokenSource.Token);
        }

        private static void ValidateFftSize(int fftSize)
        {
            if (fftSize <= 0 || (fftSize & (fftSize - 1)) != 0)
                throw new ArgumentException("FFT size must be a positive power of 2", nameof(fftSize));
        }

        public async ValueTask AddSamplesAsync(float[] samples, int sampleRate)
        {
            await _sampleChannel.Writer.WriteAsync((samples, sampleRate));
        }

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
                    _fftBuffer[_sampleCount + i].X = samples[inputIndex + i];
                    _fftBuffer[_sampleCount + i].Y = 0;
                }
                inputIndex += samplesToCopy;
                _sampleCount += samplesToCopy;
                if (_sampleCount >= _fftSize)
                    ProcessFftBuffer(sampleRate);
            }
        }

        private void ProcessFftBuffer(int sampleRate)
        {
            var fftBufferCopy = new NAudio.Dsp.Complex[_fftSize];
            Array.Copy(_fftBuffer, fftBufferCopy, _fftSize);
            FastFourierTransform.FFT(true, (int)Math.Log(_fftSize, 2.0), fftBufferCopy);
            FftCalculated?.Invoke(this, new FftEventArgs(fftBufferCopy, sampleRate));
            Array.Clear(_fftBuffer, 0, _fftBuffer.Length);
            _sampleCount = 0;
        }

        public async ValueTask DisposeAsync()
        {
            _cancellationTokenSource.Cancel();
            await _processingTask;
            _cancellationTokenSource.Dispose();
        }
    }

    public class GainParameters : IGainParametersProvider, INotifyPropertyChanged
    {
        private float _amplificationFactor = Constants.DefaultAmplificationFactor;
        private float _minDbValue = Constants.DefaultMinDbValue;
        private float _maxDbValue = Constants.DefaultMaxDbValue;
        private readonly SynchronizationContext? _synchronizationContext;

        public event PropertyChangedEventHandler PropertyChanged;

        public GainParameters(SynchronizationContext? synchronizationContext = null)
        {
            _synchronizationContext = synchronizationContext;
        }

        public float AmplificationFactor
        {
            get => _amplificationFactor;
            set
            {
                if (Math.Abs(_amplificationFactor - value) > Constants.Epsilon)
                {
                    _amplificationFactor = Math.Max(0.1f, value);
                    OnPropertyChanged(nameof(AmplificationFactor));
                }
            }
        }

        public float MinDbValue
        {
            get => _minDbValue;
            set
            {
                if (Math.Abs(_minDbValue - value) > Constants.Epsilon)
                {
                    _minDbValue = Math.Min(value, _maxDbValue);
                    OnPropertyChanged(nameof(MinDbValue));
                }
            }
        }

        public float MaxDbValue
        {
            get => _maxDbValue;
            set
            {
                if (Math.Abs(_maxDbValue - value) > Constants.Epsilon)
                {
                    _maxDbValue = Math.Max(value, _minDbValue);
                    OnPropertyChanged(nameof(MaxDbValue));
                }
            }
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            if (_synchronizationContext != null)
                _synchronizationContext.Post(_ => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)), null);
            else
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class SpectrumConverter : ISpectrumConverter
    {
        private readonly IGainParametersProvider _gainParameters;

        public SpectrumConverter(IGainParametersProvider gainParameters)
        {
            _gainParameters = gainParameters ?? throw new ArgumentNullException(nameof(gainParameters));
        }

        public float[] ConvertToSpectrum(NAudio.Dsp.Complex[] fftResult, int sampleRate)
        {
            int length = fftResult.Length / 2;
            var spectrum = new float[length];
            Parallel.For(0, length, i =>
            {
                float magnitude = CalculateMagnitude(fftResult[i], i == 0 || i == length - 1);
                float db = 20 * (float)Math.Log10(magnitude);
                spectrum[i] = NormalizeDb(db);
            });
            return spectrum;
        }

        private static float CalculateMagnitude(NAudio.Dsp.Complex value, bool isEndpoint)
        {
            float magnitude = (float)Math.Sqrt(value.X * value.X + value.Y * value.Y);
            return isEndpoint ? magnitude : magnitude * 2;
        }

        private float NormalizeDb(float db)
        {
            return Math.Clamp(((db - _gainParameters.MinDbValue) / (_gainParameters.MaxDbValue - _gainParameters.MinDbValue)) * _gainParameters.AmplificationFactor, 0, 1);
        }
    }

    public class SpectrumAnalyzer : ISpectralDataProvider, IDisposable
    {
        private readonly IFftProcessor _fftProcessor;
        private readonly ISpectrumConverter _spectrumConverter;
        private readonly SynchronizationContext? _synchronizationContext;
        private SpectralData? _lastSpectralData;
        private bool _disposed;

        public event EventHandler<SpectralDataEventArgs> SpectralDataReady;

        public SpectrumAnalyzer(IFftProcessor fftProcessor, ISpectrumConverter spectrumConverter, SynchronizationContext? synchronizationContext = null)
        {
            _fftProcessor = fftProcessor ?? throw new ArgumentNullException(nameof(fftProcessor));
            _spectrumConverter = spectrumConverter ?? throw new ArgumentNullException(nameof(spectrumConverter));
            _synchronizationContext = synchronizationContext;
            _fftProcessor.FftCalculated += OnFftCalculated;
        }

        public SpectralData? GetCurrentSpectrum()
        {
            ThrowIfDisposed();
            return _lastSpectralData;
        }

        public async Task AddSamplesAsync(float[] samples, int sampleRate, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (samples == null || samples.Length == 0)
                return;
            await _fftProcessor.AddSamplesAsync(samples, sampleRate);
        }

        private void OnFftCalculated(object? sender, FftEventArgs e)
        {
            if (e.Result == null || e.Result.Length == 0)
                return;
            var spectrum = _spectrumConverter.ConvertToSpectrum(e.Result, e.SampleRate);
            var spectralData = new SpectralData(spectrum, DateTime.UtcNow);
            _lastSpectralData = spectralData;
            if (_synchronizationContext != null)
                _synchronizationContext.Post(_ => SpectralDataReady?.Invoke(this, new SpectralDataEventArgs(spectralData)), null);
            else
                SpectralDataReady?.Invoke(this, new SpectralDataEventArgs(spectralData));
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SpectrumAnalyzer));
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _fftProcessor.FftCalculated -= OnFftCalculated;
            (_fftProcessor as IAsyncDisposable)?.DisposeAsync().AsTask().Wait();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}