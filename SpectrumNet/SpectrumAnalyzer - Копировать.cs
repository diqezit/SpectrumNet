using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;
using Serilog;
using System.Buffers;
using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.InteropServices;

#nullable enable

namespace SpectrumNet
{
    public record SpectralData(float[] Spectrum, DateTime Timestamp);

    public class FFTProcessor
    {
        private readonly int _fftSize;
        private readonly float[] _window;
        private readonly ArrayPool<Complex> _fftBufferPool = ArrayPool<Complex>.Shared;
        private readonly float[] _frequencyWeights;
        private readonly float[] _previousMagnitudes;
        private const float SMOOTHING_FACTOR = 0.7f;
        private readonly int _sampleRate;
        public int FftSize => _fftSize;

        public FFTProcessor(int fftSize, int sampleRate = 44100)
        {
            _fftSize = fftSize;
            _sampleRate = sampleRate;
            _window = Window.Hann(fftSize).Select(Convert.ToSingle).ToArray();
            _previousMagnitudes = new float[fftSize / 2];
            _frequencyWeights = Enumerable.Range(0, fftSize / 2)
                .Select(i => CalculatePerceptualWeight(i * sampleRate / (float)fftSize))
                .ToArray();
        }

        private static float CalculatePerceptualWeight(float frequency) => frequency switch
        {
            < 20 or > 20000 => 0,
            _ => Math.Clamp((float)(Math.Log10(frequency) - 1) / 3, 0, 1)
        };

        public float[] ComputeSpectrum(float[] samples)
        {
            var fftBuffer = _fftBufferPool.Rent(_fftSize);
            try
            {
                Array.Clear(fftBuffer, 0, _fftSize);
                for (int i = 0; i < _fftSize; i++)
                {
                    fftBuffer[i] = i < samples.Length ? new Complex(samples[i] * _window[i], 0) : Complex.Zero;
                }

                Fourier.Forward(fftBuffer, FourierOptions.NoScaling);

                return CalculateMagnitudes(fftBuffer);
            }
            finally
            {
                _fftBufferPool.Return(fftBuffer);
            }
        }

        private float[] CalculateMagnitudes(Complex[] fftBuffer)
        {
            var magnitudes = new float[_fftSize / 2];
            for (int i = 0; i < magnitudes.Length; i++)
            {
                var magnitude = (float)Complex.Abs(fftBuffer[i]) * _frequencyWeights[i];
                magnitudes[i] = _previousMagnitudes[i] * SMOOTHING_FACTOR + magnitude * (1 - SMOOTHING_FACTOR);
                _previousMagnitudes[i] = magnitudes[i];
            }
            return NormalizeMagnitudes(magnitudes);
        }

        private static float[] NormalizeMagnitudes(float[] magnitudes)
        {
            float maxMagnitude = magnitudes.Max();
            return maxMagnitude > 0
                ? magnitudes.Select(m => (float)Math.Pow(m / maxMagnitude, 0.5)).ToArray()
                : magnitudes;
        }

        public float[] GroupFrequencyBands(float[] magnitudes, int bandCount)
        {
            int samplesPerBand = magnitudes.Length / bandCount;
            return Enumerable.Range(0, bandCount)
                .Select(i => magnitudes.Skip(i * samplesPerBand).Take(samplesPerBand).Average())
                .ToArray();
        }
    }

    public class AudioDataProcessor
    {
        private readonly ConcurrentQueue<float> _samples = new();
        private readonly int _fftSize;
        private readonly float _threshold;

        public AudioDataProcessor(int fftSize, float threshold)
        {
            _fftSize = fftSize;
            _threshold = threshold;
            InitializeBuffer();
        }

        private void InitializeBuffer()
        {
            for (int i = 0; i < _fftSize; i++)
                _samples.Enqueue(0f);
        }

        public void ProcessNewSamples(byte[] buffer, int bytesRecorded)
        {
            var samples = MemoryMarshal.Cast<byte, float>(buffer.AsSpan(0, bytesRecorded));
            foreach (var sample in samples)
            {
                var processedSample = Math.Clamp(sample / 32768f, -_threshold, _threshold);
                _samples.Enqueue(processedSample);
                if (_samples.Count > _fftSize)
                    _samples.TryDequeue(out _);
            }
        }

        public float[] GetSamples()
        {
            var samples = _samples.ToArray();
            if (samples.Length < _fftSize)
            {
                var paddedSamples = new float[_fftSize];
                Array.Copy(samples, paddedSamples, samples.Length);
                Log.Debug("[AudioDataProcessor] Padding buffer with zeros. Current size: {CurrentSize}, Required size: {RequiredSize}",
                          samples.Length, _fftSize);
                return paddedSamples;
            }
            return samples;
        }
    }

    public sealed class SpectrumAnalyzer : IDisposable
    {
        private const int DEFAULT_FFT_SIZE = 512;
        private const float DEFAULT_THRESHOLD = 0.8f;
        private const int MINIMUM_SAMPLES_RATIO = 1;

        private readonly AudioDataProcessor _audioProcessor;
        private readonly FFTProcessor _fftProcessor;
        private readonly SemaphoreSlim _processingLock = new(1, 1);
        private volatile bool _disposed;

        public float Threshold { get; set; } = DEFAULT_THRESHOLD;
        public bool IsProcessing { get; private set; }
        public int SampleCount => _audioProcessor.GetSamples().Length;
        public int MinimumRequiredSamples => _fftProcessor.FftSize / MINIMUM_SAMPLES_RATIO;

        public event EventHandler<SpectralDataEventArgs>? SpectralDataReady;

        public SpectrumAnalyzer(int fftSize = DEFAULT_FFT_SIZE)
        {
            Log.Information("[SpectrumAnalyzer] Initializing SpectrumAnalyzer with fftSize: {fftSize}", fftSize);
            _fftProcessor = new FFTProcessor(fftSize);
            _audioProcessor = new AudioDataProcessor(fftSize, DEFAULT_THRESHOLD);
        }

        public async Task AddSamplesAsync(byte[] buffer, int bytesRecorded, CancellationToken cancellationToken = default)
        {
            ValidateBuffer(buffer, bytesRecorded);
            ThrowIfDisposed();
            await Task.Run(() => _audioProcessor.ProcessNewSamples(buffer, bytesRecorded), cancellationToken).ConfigureAwait(false);
        }

        public async Task<SpectralData> GetSpectrumAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (!await _processingLock.WaitAsync(TimeSpan.FromMilliseconds(100), cancellationToken))
            {
                Log.Warning("[SpectrumAnalyzer] Failed to acquire lock for spectrum processing");
                return new SpectralData(new float[_fftProcessor.FftSize / 2], DateTime.UtcNow);
            }

            try
            {
                IsProcessing = true;
                var samples = _audioProcessor.GetSamples();
                if (samples.Length == 0) return EmptySpectralData();

                var spectrum = await Task.Run(() => _fftProcessor.ComputeSpectrum(samples), cancellationToken)
                    .ConfigureAwait(false);

                var result = new SpectralData(spectrum, DateTime.UtcNow);
                SpectralDataReady?.Invoke(this, new SpectralDataEventArgs(result));
                return result;
            }
            finally
            {
                IsProcessing = false;
                _processingLock.Release();
            }
        }

        private static SpectralData EmptySpectralData() =>
            new(new float[DEFAULT_FFT_SIZE / 2], DateTime.UtcNow);

        // Добавлено свойство IsDisposed для проверки состояния _disposed
        public bool IsDisposed => _disposed;

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SpectrumAnalyzer));
        }

        private static void ValidateBuffer(byte[] buffer, int bytesRecorded)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (bytesRecorded <= 0 || bytesRecorded > buffer.Length) throw new ArgumentOutOfRangeException(nameof(bytesRecorded));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _processingLock.Dispose();
            _disposed = true;
            Log.Information("[SpectrumAnalyzer] SpectrumAnalyzer object has been disposed.");
            GC.SuppressFinalize(this);
        }
    }

    public class SpectrumAnalyzerException : Exception
    {
        public SpectrumAnalyzerException() { }
        public SpectrumAnalyzerException(string message) : base(message) { }
        public SpectrumAnalyzerException(string message, Exception innerException) : base(message, innerException) { }
    }

    public class SpectralDataEventArgs : EventArgs
    {
        public SpectralData Data { get; }

        public SpectralDataEventArgs(SpectralData data) => Data = data ?? throw new ArgumentNullException(nameof(data));
    }
}