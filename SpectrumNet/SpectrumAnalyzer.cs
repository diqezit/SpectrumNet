﻿using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
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
        private readonly Complex[] _buffer;
        private readonly float[] _window;
        private readonly Complex32[] _mathNetBuffer;
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
            _buffer = ArrayPool<Complex>.Shared.Rent(fftSize);
            _window = GenerateWindow(fftSize);
            _mathNetBuffer = new Complex32[fftSize];
            _processingChannel = Channel.CreateUnbounded<(float[], int)>(new UnboundedChannelOptions { SingleReader = true });
            _cts = new CancellationTokenSource();
            _processingTask = Task.Run(ProcessSamplesAsync);
        }

        private float[] GenerateWindow(int size)
        {
            var window = new float[size];
            Func<int, float> windowFunc = WindowType switch
            {
                FftWindowType.Hann => i => (float)(0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (size - 1)))),
                FftWindowType.Hamming => i => (float)(0.54 - 0.46 * Math.Cos(2.0 * Math.PI * i / (size - 1))),
                FftWindowType.Blackman => i => (float)(0.42 - 0.5 * Math.Cos(2.0 * Math.PI * i / (size - 1)) + 0.08 * Math.Cos(4.0 * Math.PI * i / (size - 1))),
                _ => _ => 1.0f
            };

            if (Avx.IsSupported && size >= Vector256<float>.Count)
            {
                unsafe
                {
                    fixed (float* windowPtr = window)
                    {
                        for (int i = 0; i <= size - Vector256<float>.Count; i += Vector256<float>.Count)
                        {
                            Avx.Store(windowPtr + i, Vector256.Create(
                                windowFunc(i), windowFunc(i + 1), windowFunc(i + 2), windowFunc(i + 3),
                                windowFunc(i + 4), windowFunc(i + 5), windowFunc(i + 6), windowFunc(i + 7)));
                        }
                    }
                }
                for (int i = size - size % Vector256<float>.Count; i < size; i++)
                    window[i] = windowFunc(i);
            }
            else
            {
                for (int i = 0; i < size; i++)
                    window[i] = windowFunc(i);
            }
            return window;
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

        private unsafe void ApplyWindowAndCopy(Span<float> samples)
        {
            if (Avx.IsSupported && samples.Length >= Vector256<float>.Count)
            {
                fixed (float* windowPtr = &_window[_sampleCount])
                fixed (float* samplesPtr = &MemoryMarshal.GetReference(samples))
                fixed (Complex* bufferPtr = &_buffer[_sampleCount])
                {
                    int i = 0;
                    for (; i <= samples.Length - Vector256<float>.Count; i += Vector256<float>.Count)
                    {
                        var result = Avx.Multiply(
                            Avx.LoadVector256(samplesPtr + i),
                            Avx.LoadVector256(windowPtr + i));

                        for (int j = 0; j < Vector256<float>.Count; j++)
                        {
                            bufferPtr[i + j].X = result.GetElement(j);
                            bufferPtr[i + j].Y = 0;
                        }
                    }
                    for (; i < samples.Length; i++)
                    {
                        int bufferIndex = _sampleCount + i;
                        _buffer[bufferIndex] = new Complex { X = samples[i] * _window[bufferIndex], Y = 0 };
                    }
                }
            }
            else
            {
                for (int i = 0; i < samples.Length; i++)
                {
                    int bufferIndex = _sampleCount + i;
                    _buffer[bufferIndex] = new Complex { X = samples[i] * _window[bufferIndex], Y = 0 };
                }
            }
        }

        private void ProcessFft(int sampleRate)
        {
            for (int i = 0; i < _fftSize; i++)
                _mathNetBuffer[i] = new Complex32((float)_buffer[i].X, (float)_buffer[i].Y);

            Fourier.Forward(_mathNetBuffer, FourierOptions.Matlab);

            for (int i = 0; i < _fftSize; i++)
                _buffer[i] = new Complex { X = _mathNetBuffer[i].Real, Y = _mathNetBuffer[i].Imaginary };

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
                // Освобождаем ресурсы
                if (_buffer != null)
                {
                    ArrayPool<Complex>.Shared.Return(_buffer);
                }

                _cts?.Dispose();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(FftProcessor));
        }
    }

    public sealed class SpectrumConverter : ISpectrumConverter
    {
        private readonly IGainParametersProvider _p;
        private const float L10M = 10f, F4M = 4f, Z = 0f, O = 1f;

        public SpectrumConverter(IGainParametersProvider p)
        {
            _p = p ?? throw new ArgumentNullException(nameof(p));
        }

        public unsafe float[] ConvertToSpectrum(Complex[] fft, int sr)
        {
            try
            {
                int len = fft.Length / 2;
                var s = GC.AllocateUninitializedArray<float>(len, pinned: true);
                float minDb = _p.MinDbValue, range = _p.MaxDbValue - minDb, amp = _p.AmplificationFactor;

                // Определяем индексы для 20 Гц и 20 кГц
                int minIndex = (int)(20 * (fft.Length / (float)sr));
                int maxIndex = (int)(20000 * (fft.Length / (float)sr));

                fixed (Complex* inPtr = fft)
                fixed (float* outPtr = s)
                {
                    if (Avx.IsSupported && len >= 8)
                        ProcessAvx(inPtr, outPtr, len, minDb, range, amp, minIndex, maxIndex);
                    else if (Sse.IsSupported && len >= 4)
                        ProcessSse(inPtr, outPtr, len, minDb, range, amp, minIndex, maxIndex);
                    else
                        ProcessScalar(inPtr, outPtr, len, minDb, range, amp, minIndex, maxIndex);
                }
                return s;
            }
            catch (ArgumentNullException ex)
            {
                Log.Error(ex, "ArgumentNullException in ConvertToSpectrum: {Message}", ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Unexpected error in ConvertToSpectrum: {Message}", ex.Message);
                throw;
            }
        }

        private static unsafe void ProcessAvx(Complex* inPtr, float* outPtr, int len, float minDb, float range, float amp, int minIndex, int maxIndex)
        {
            try
            {
                int i = 0, aligned = len - (len % 8);

                for (; i < aligned; i += 8)
                {
                    if (i < minIndex || i > maxIndex)
                        continue;

                    var c0 = Avx.LoadVector256((float*)(inPtr + i));
                    var c1 = Avx.LoadVector256((float*)(inPtr + i + 4));
                    var mags = Avx.Multiply(Avx.Add(Avx.Multiply(c0, c0), Avx.Multiply(c1, c1)), Vector256.Create(F4M));

                    float* m = (float*)&mags;
                    for (int j = 0; j < 8; j++)
                    {
                        m[j] = m[j] < float.Epsilon ? 0f : (L10M * MathF.Log(m[j]) - minDb) / range * amp;
                        m[j] = Math.Clamp(m[j], Z, O);
                    }

                    Avx.Store(outPtr + i, Avx.LoadVector256(m));
                }

                ProcessScalar(inPtr + i, outPtr + i, len - i, minDb, range, amp, minIndex, maxIndex);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in ProcessAvx: {Message}", ex.Message);
                throw;
            }
        }

        private static unsafe void ProcessSse(Complex* inPtr, float* outPtr, int len, float minDb, float range, float amp, int minIndex, int maxIndex)
        {
            try
            {
                int i = 0, aligned = len - (len % 4);

                for (; i < aligned; i += 4)
                {
                    if (i < minIndex || i > maxIndex)
                        continue;

                    var c = Sse.LoadVector128((float*)(inPtr + i));
                    var mags = Sse.Multiply(Sse.Add(Sse.Multiply(c, c), Sse.Multiply(c, c)), Vector128.Create(F4M));

                    float* m = (float*)&mags;
                    for (int j = 0; j < 4; j++)
                    {
                        m[j] = m[j] < float.Epsilon ? 0f : (L10M * MathF.Log(m[j]) - minDb) / range * amp;
                        m[j] = Math.Clamp(m[j], Z, O);
                    }

                    Sse.Store(outPtr + i, Sse.LoadVector128(m));
                }

                ProcessScalar(inPtr + i, outPtr + i, len - i, minDb, range, amp, minIndex, maxIndex);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in ProcessSse: {Message}", ex.Message);
                throw;
            }
        }

        private static unsafe void ProcessScalar(Complex* inPtr, float* outPtr, int len, float minDb, float range, float amp, int minIndex, int maxIndex)
        {
            try
            {
                for (int i = 0; i < len; i++)
                {
                    if (i < minIndex || i > maxIndex)
                        continue;

                    float mag = inPtr[i].X * inPtr[i].X + inPtr[i].Y * inPtr[i].Y;
                    if (i != 0 && i != len - 1) mag *= F4M;
                    outPtr[i] = mag < float.Epsilon ? Z : Math.Clamp((L10M * MathF.Log10(mag) - minDb) / range * amp, Z, O);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in ProcessScalar: {Message}", ex.Message);
                throw;
            }
        }
    }

    public enum FftWindowType
    {
        Hann,
        Hamming,
        Blackman,
    }
}