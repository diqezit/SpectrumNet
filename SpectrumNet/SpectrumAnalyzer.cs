#nullable enable

namespace SpectrumNet
{
    #region Data Models

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

    #endregion

    #region Sample Processing

    public class SampleAggregator
    {
        private readonly Channel<(float[] Samples, int SampleRate)> _sampleChannel;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly Task _processingTask;
        private readonly NAudio.Dsp.Complex[] _fftBuffer;
        private readonly int _fftSize;
        private int _sampleCount;

        public event EventHandler<FftEventArgs>? FftCalculated;

        public SampleAggregator(int fftSize)
        {
            if (fftSize <= 0 || (fftSize & (fftSize - 1)) != 0)
            {
                Log.Error("[SampleAggregator] Invalid FFT size: {FftSize}", fftSize);
                throw new ArgumentException("FFT size must be a positive power of 2", nameof(fftSize));
            }

            _fftSize = fftSize;
            _fftBuffer = new NAudio.Dsp.Complex[fftSize];
            _sampleChannel = Channel.CreateUnbounded<(float[] Samples, int SampleRate)>(
                new UnboundedChannelOptions { SingleReader = true });
            _cancellationTokenSource = new CancellationTokenSource();
            _processingTask = ProcessSamplesAsync(_cancellationTokenSource.Token);
        }

        public async ValueTask AddSamplesAsync(float[] samples, int sampleRate)
        {
            try
            {
                await _sampleChannel.Writer.WriteAsync((samples, sampleRate));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[SampleAggregator] Error adding samples: {Message}", ex.Message);
                throw;
            }
        }

        private async Task ProcessSamplesAsync(CancellationToken cancellationToken)
        {
            try
            {
                await foreach (var (samples, sampleRate) in _sampleChannel.Reader.ReadAllAsync(cancellationToken))
                {
                    ProcessSampleBatch(samples, sampleRate);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[SampleAggregator] Error processing samples: {Message}", ex.Message);
                throw;
            }
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
                {
                    ProcessFftBuffer(sampleRate);
                }
            }
        }

        private void ProcessFftBuffer(int sampleRate)
        {
            try
            {
                var fftBufferCopy = new NAudio.Dsp.Complex[_fftSize];
                Array.Copy(_fftBuffer, fftBufferCopy, _fftSize);

                FastFourierTransform.FFT(true, (int)Math.Log(_fftSize, 2.0), fftBufferCopy);
                FftCalculated?.Invoke(this, new FftEventArgs(fftBufferCopy, sampleRate));

                Array.Clear(_fftBuffer, 0, _fftBuffer.Length);
                _sampleCount = 0;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[SampleAggregator] Error processing FFT buffer: {Message}", ex.Message);
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            _cancellationTokenSource.Cancel();
            await _processingTask;
            _cancellationTokenSource.Dispose();
        }
    }

    #endregion

    #region Spectrum Analysis

    public class SpectrumAnalyzer : IDisposable, INotifyPropertyChanged
    {
        private readonly SampleAggregator _sampleAggregator;
        private readonly SynchronizationContext? _synchronizationContext;
        private SpectralData? _lastSpectralData;
        private bool _disposed;

        // Spectrum settings with thread-safe access
        private volatile float _amplificationFactor = 0.55f;
        private volatile float _minDbValue = -100f;
        private volatile float _maxDbValue = -20f;

        public event EventHandler<SpectralDataEventArgs>? SpectralDataReady;
        public event PropertyChangedEventHandler? PropertyChanged;

        public float AmplificationFactor
        {
            get => _amplificationFactor;
            set
            {
                if (Math.Abs(_amplificationFactor - value) > float.Epsilon)
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
                if (Math.Abs(_minDbValue - value) > float.Epsilon)
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
                if (Math.Abs(_maxDbValue - value) > float.Epsilon)
                {
                    _maxDbValue = Math.Max(value, _minDbValue);
                    OnPropertyChanged(nameof(MaxDbValue));
                }
            }
        }

        public SpectrumAnalyzer(int fftSize = 1024)
        {
            _synchronizationContext = SynchronizationContext.Current;
            _sampleAggregator = new SampleAggregator(fftSize);
            _sampleAggregator.FftCalculated += OnFftCalculated;
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
            {
                Log.Warning("[SpectrumAnalyzer] Attempt to add empty or null samples");
                return;
            }

            try
            {
                await _sampleAggregator.AddSamplesAsync(samples, sampleRate);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[SpectrumAnalyzer] Critical error adding samples: {Message}", ex.Message);
                throw;
            }
        }

        public void UpdateGainParameters(float amplificationFactor, float minDbValue, float maxDbValue)
        {
            ThrowIfDisposed();
            AmplificationFactor = amplificationFactor;
            MinDbValue = minDbValue;
            MaxDbValue = maxDbValue;
        }

        private void OnFftCalculated(object? sender, FftEventArgs e)
        {
            if (e.Result == null || e.Result.Length == 0) return;

            try
            {
                var spectrum = ConvertToSpectrum(e.Result, e.SampleRate);
                var spectralData = new SpectralData(spectrum, DateTime.UtcNow);
                _lastSpectralData = spectralData;

                if (_synchronizationContext != null)
                {
                    _synchronizationContext.Post(_ =>
                        SpectralDataReady?.Invoke(this, new SpectralDataEventArgs(spectralData)), null);
                }
                else
                {
                    SpectralDataReady?.Invoke(this, new SpectralDataEventArgs(spectralData));
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[SpectrumAnalyzer] Error processing FFT data: {Message}", ex.Message);
                throw;
            }
        }

        private float[] ConvertToSpectrum(NAudio.Dsp.Complex[] fftResult, int sampleRate)
        {
            int length = fftResult.Length / 2;
            var spectrum = new float[length];

            Parallel.For(0, length, i =>
            {
                float magnitude = (float)Math.Sqrt(fftResult[i].X * fftResult[i].X + fftResult[i].Y * fftResult[i].Y);
                if (i != 0 && i != length - 1)
                {
                    magnitude *= 2;
                }

                float db = 20 * (float)Math.Log10(magnitude);
                spectrum[i] = Math.Clamp(
                    ((db - _minDbValue) / (_maxDbValue - _minDbValue)) * _amplificationFactor,
                    0,
                    1
                );
            });

            return spectrum;
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            if (_synchronizationContext != null)
            {
                _synchronizationContext.Post(_ =>
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)), null);
            }
            else
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(SpectrumAnalyzer));
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            _sampleAggregator.FftCalculated -= OnFftCalculated;
            (_sampleAggregator as IAsyncDisposable)?.DisposeAsync().AsTask().Wait();
            _disposed = true;

            GC.SuppressFinalize(this);
        }
    }

    #endregion
}