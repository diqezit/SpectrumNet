#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Core;

public abstract class CoreSpectrumRenderer : BaseSpectrumRenderer
{
    private readonly ObjectPool<SKPaint> _paintPool;
    private readonly ObjectPool<SKPath> _pathPool;
    private readonly object _spectrumLock = new();
    private float _smoothingFactor = 0.3f;
    private float[]? _previousSpectrum;
    private bool _needsRedraw;
    private bool _useAntiAlias = true;

    protected CoreSpectrumRenderer()
    {
        _paintPool = new ObjectPool<SKPaint>(
            () => new SKPaint(),
            paint => paint.Reset());
        _pathPool = new ObjectPool<SKPath>(
            () => new SKPath(),
            path => path.Reset());
    }

    protected bool UseAntiAlias => _useAntiAlias;

    public float SmoothingFactor
    {
        get => _smoothingFactor;
        protected set => _smoothingFactor = Math.Clamp(value, 0f, 1f);
    }

    public override bool RequiresRedraw() => _needsRedraw || IsOverlayActive;

    protected void RequestRedraw() => _needsRedraw = true;

    protected SKPath GetPath() => _pathPool.Get();
    protected void ReturnPath(SKPath path) => _pathPool.Return(path);
    protected SKPaint GetPaint() => _paintPool.Get();
    protected void ReturnPaint(SKPaint paint) => _paintPool.Return(paint);

    protected (bool isValid, float[]? processedSpectrum) ProcessSpectrum(
        float[]? spectrum,
        int targetCount,
        float? customSmoothingFactor = null,
        bool applyTemporalSmoothing = true)
    {
        if (!ValidateSpectrumInput(spectrum, targetCount)) 
            return (false, null);

        var scaledSpectrum = ScaleSpectrum(spectrum!, targetCount);

        if (applyTemporalSmoothing)
        {
            var smoothedSpectrum = ApplyTemporalSmoothing(
                scaledSpectrum,
                targetCount,
                customSmoothingFactor);
            return (true, smoothedSpectrum);
        }

        return (true, scaledSpectrum);
    }

    private static bool ValidateSpectrumInput(float[]? spectrum, int targetCount) =>
        spectrum != null && spectrum.Length > 0 && targetCount > 0;

    private static float[] ScaleSpectrum(
        float[] spectrum,
        int targetCount)
    {
        var result = new float[targetCount];
        float blockSize = CalculateBlockSize(spectrum.Length, targetCount);

        for (int i = 0; i < targetCount; i++)
            result[i] = CalculateScaledValue(spectrum, i, blockSize);

        return result;
    }

    private static float CalculateBlockSize(int currentSpectrumLength, int targetCount)
    {
        if (targetCount <= 0) return 0;
        float blockSize = currentSpectrumLength / (float)targetCount;
        return blockSize < 1 ? 1 : blockSize;
    }

    private static float CalculateScaledValue(
        float[] spectrum,
        int index,
        float blockSize)
    {
        int start = (int)(index * blockSize);
        int end = Min((int)((index + 1) * blockSize), spectrum.Length);

        if (end > start)
            return CalculateBlockAverage(spectrum, start, end);

        if (start < spectrum.Length)
            return spectrum[start];

        return 0f;
    }

    private static float CalculateBlockAverage(float[] spectrum, int start, int end)
    {
        float sum = 0f;
        int count = 0;

        for (int j = start; j < end && j < spectrum.Length; j++)
        {
            sum += spectrum[j];
            count++;
        }

        return count > 0 ? sum / count : 0f;
    }

    private float[] ApplyTemporalSmoothing(
        float[] scaledSpectrum,
        int targetCount,
        float? customSmoothingFactor)
    {
        lock (_spectrumLock)
        {
            EnsurePreviousSpectrumInitialized(scaledSpectrum, targetCount);
            return SmoothSpectrum(scaledSpectrum, targetCount, customSmoothingFactor);
        }
    }

    private void EnsurePreviousSpectrumInitialized(float[] scaledSpectrum, int targetCount)
    {
        if (_previousSpectrum == null || _previousSpectrum.Length != targetCount)
        {
            _previousSpectrum = new float[targetCount];
            Array.Copy(scaledSpectrum, _previousSpectrum, Math.Min(scaledSpectrum.Length, targetCount));
        }
    }

    private float[] SmoothSpectrum(
        float[] scaledSpectrum,
        int targetCount,
        float? customSmoothingFactor)
    {
        float factor = customSmoothingFactor ?? _smoothingFactor;
        float invFactor = 1 - factor;

        for (int i = 0; i < targetCount; i++)
        {
            _previousSpectrum![i] = (_previousSpectrum[i] * invFactor) + (scaledSpectrum[i] * factor);
            scaledSpectrum[i] = _previousSpectrum[i];
        }

        return scaledSpectrum;
    }

    protected void SetProcessingSmoothingFactor(float factor) =>
        SmoothingFactor = factor;

    protected float GetOverlayAlphaFactor() =>
        IsOverlayActive ? OverlayAlpha : 1f;

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();
        _useAntiAlias = Quality != RenderQuality.Low;
        SmoothingFactor = IsOverlayActive ? 0.5f : 0.3f;
    }

    protected override void OnOverlayTransparencyChanged(float alpha)
    {
        base.OnOverlayTransparencyChanged(alpha);
        _needsRedraw = true;
    }

    protected override void CleanupUnusedResources()
    {
        base.CleanupUnusedResources();
        _paintPool.Clear();
        _pathPool.Clear();
    }

    protected override void OnDispose()
    {
        lock (_spectrumLock)
        {
            _previousSpectrum = null;
        }
        _paintPool.Dispose();
        _pathPool.Dispose();
        base.OnDispose();
    }
}