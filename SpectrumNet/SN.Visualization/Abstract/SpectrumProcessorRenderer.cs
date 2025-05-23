#nullable enable

using static System.MathF;

namespace SpectrumNet.SN.Visualization.Abstract;

public abstract class SpectrumProcessor : IDisposable
{
    private const string LogPrefix = nameof(SpectrumProcessor);
    private const int PARALLEL_BATCH_SIZE = 32;

    protected const float
        DEFAULT_SMOOTHING_FACTOR = 0.3f,
        OVERLAY_SMOOTHING_FACTOR = 0.5f,
        MIN_MAGNITUDE_THRESHOLD = 0.01f;

    protected readonly ISmartLogger _logger = Instance;
    protected readonly SemaphoreSlim _spectrumSemaphore = new(1, 1);
    protected readonly object _spectrumLock = new();

    protected float[]? _previousSpectrum;
    protected float[]? _processedSpectrum;
    protected float _smoothingFactor = DEFAULT_SMOOTHING_FACTOR;
    protected bool _disposed;

    private static readonly bool _isHardwareAcceleratedCached = IsHardwareAccelerated;
    protected static bool IsHardwareAccelerated => _isHardwareAcceleratedCached;

    public virtual void SetSmoothingFactor(float factor)
    {
        if (MathF.Abs(_smoothingFactor - factor) > float.Epsilon)
        {
            _smoothingFactor = factor;
            OnSmoothingFactorChanged();
        }
    }

    protected virtual void OnSmoothingFactorChanged() { }

    public (bool isValid, float[]? processedSpectrum) PrepareSpectrum(
        float[]? spectrum,
        int targetCount,
        int spectrumLength)
    {
        if (spectrum == null || spectrum.Length == 0 || targetCount <= 0)
            return (false, null);

        float[] processed = ProcessSpectrum(spectrum, targetCount, spectrumLength);
        return (true, processed);
    }

    protected float[] ProcessSpectrum(
        float[] spectrum,
        int targetCount,
        int spectrumLength)
    {
        bool locked = false;
        try
        {
            locked = _spectrumSemaphore.Wait(0);
            if (locked)
                PerformSpectrumProcessing(spectrum, targetCount, spectrumLength);

            lock (_spectrumLock)
                return GetProcessedSpectrum(spectrum, targetCount, spectrumLength);
        }
        finally
        {
            if (locked)
                _spectrumSemaphore.Release();
        }
    }

    private void PerformSpectrumProcessing(
        float[] spectrum,
        int count,
        int length)
    {
        var scaled = ScaleSpectrum(spectrum, count, length);
        _processedSpectrum = SmoothSpectrum(scaled, count);
    }

    private float[] GetProcessedSpectrum(
        float[] spectrum,
        int count,
        int length)
    {
        if (_processedSpectrum != null && _processedSpectrum.Length == count)
            return _processedSpectrum;

        var scaled = ScaleSpectrum(spectrum, count, length);
        _processedSpectrum = SmoothSpectrum(scaled, count);
        return _processedSpectrum;
    }

    protected static float[] ScaleSpectrum(
        float[] spectrum,
        int targetCount,
        int spectrumLength)
    {
        var result = new float[targetCount];
        float blockSize = spectrumLength / (float)targetCount;

        if (targetCount >= PARALLEL_BATCH_SIZE && IsHardwareAccelerated)
            ScaleSpectrumParallel(spectrum, result, targetCount, spectrumLength, blockSize);
        else
            ScaleSpectrumSequential(spectrum, result, targetCount, spectrumLength, blockSize);

        return result;
    }

    private static void ScaleSpectrumParallel(
        float[] spectrum,
        float[] target,
        int count,
        int length,
        float blockSize)
    {
        const int MIN_PARALLEL_SIZE = 32;

        if (count < MIN_PARALLEL_SIZE)
        {
            ScaleSpectrumSequential(spectrum, target, count, length, blockSize);
            return;
        }

        Parallel.For(0, count, i =>
        {
            int start = (int)(i * blockSize);
            int end = Min((int)((i + 1) * blockSize), length);
            target[i] = end > start
                ? CalculateBlockAverage(spectrum, start, end)
                : 0;
        });
    }

    private static void ScaleSpectrumSequential(
        float[] spectrum,
        float[] target,
        int count,
        int length,
        float blockSize)
    {
        for (int i = 0; i < count; i++)
        {
            int start = (int)(i * blockSize);
            int end = Min((int)((i + 1) * blockSize), length);
            target[i] = end > start
                ? CalculateBlockAverage(spectrum, start, end)
                : 0;
        }
    }

    protected float[] SmoothSpectrum(
        float[] spectrum,
        int targetCount,
        float? customFactor = null)
    {
        float factor = customFactor ?? _smoothingFactor;
        if (_previousSpectrum == null || _previousSpectrum.Length != targetCount)
        {
            _previousSpectrum = new float[targetCount];
            if (spectrum.Length >= targetCount)
                Array.Copy(spectrum, _previousSpectrum, targetCount);
        }

        var smoothed = new float[targetCount];
        if (IsHardwareAccelerated && targetCount >= Vector<float>.Count)
            SmoothSpectrumVectorized(spectrum, smoothed, targetCount, factor);
        else
            SmoothSpectrumSequential(spectrum, smoothed, targetCount, factor);

        return smoothed;
    }

    private void SmoothSpectrumVectorized(
        float[] spectrum,
        float[] smoothed,
        int count,
        float smoothing)
    {
        var previous = _previousSpectrum ?? throw new InvalidOperationException(
            "Previous spectrum not initialized");

        int vecSize = Vector<float>.Count;
        int limit = count - count % vecSize;

        for (int i = 0; i < limit; i += vecSize)
        {
            var curr = new Vector<float>(spectrum, i);
            var prev = new Vector<float>(previous, i);
            var blend = prev * (1 - smoothing) + curr * smoothing;
            blend.CopyTo(smoothed, i);
            blend.CopyTo(previous, i);
        }

        SmoothSpectrumSequential(spectrum, smoothed, count, smoothing, limit);
    }

    private void SmoothSpectrumSequential(
        float[] spectrum,
        float[] smoothed,
        int count,
        float smoothing,
        int startIndex = 0)
    {
        var previous = _previousSpectrum ?? throw new InvalidOperationException(
            "Previous spectrum not initialized");

        for (int i = startIndex; i < count; i++)
        {
            float current = spectrum[i];
            float prevValue = previous[i];
            float result = prevValue * (1 - smoothing) + current * smoothing;
            smoothed[i] = result;
            previous[i] = result;
        }
    }

    [MethodImpl(AggressiveInlining)]
    private static float CalculateBlockAverage(
        float[] spectrum,
        int start,
        int end)
    {
        float sum = 0;
        for (int i = start; i < end; i++)
            sum += spectrum[i];
        return sum / (end - start);
    }

    public virtual void Dispose()
    {
        if (!_disposed)
        {
            _logger.Safe(() => HandleDispose(),
                      LogPrefix,
                      "Error during spectrum processor disposal");
        }
    }

    protected virtual void HandleDispose()
    {
        _spectrumSemaphore.Dispose();
        _previousSpectrum = null;
        _processedSpectrum = null;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    protected virtual void OnDispose() { }
}