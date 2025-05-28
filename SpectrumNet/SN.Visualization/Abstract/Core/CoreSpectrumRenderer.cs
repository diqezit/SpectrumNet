#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Core;

public abstract class CoreSpectrumRenderer(
    ISpectrumProcessor? spectrumProcessor = null,
    IResourcePool? resourcePool = null) : BaseSpectrumRenderer()
{
    protected const float MIN_MAGNITUDE_THRESHOLD = 0.01f;

    private readonly ISpectrumProcessor _spectrumProcessor =
        spectrumProcessor ?? new DefaultSpectrumProcessor();
    private readonly IResourcePool _resourcePool =
        resourcePool ?? new DefaultResourcePool();

    private float[] _animationValues = [];
    private float _animationTime;
    private DateTime _lastUpdateTime = DateTime.Now;
    private float _deltaTime;

    private bool _needsRedraw;
    private bool _useAntiAlias = true;

    protected ISpectrumProcessor SpectrumProcessor => _spectrumProcessor;
    protected IResourcePool ResourcePool => _resourcePool;
    protected bool UseAntiAlias => _useAntiAlias;

    protected float AnimationTime => _animationTime;
    protected float DeltaTime => _deltaTime;
    protected float[] AnimatedValues => _animationValues;

    public override bool RequiresRedraw() => _needsRedraw || IsOverlayActive;

    protected void RequestRedraw() => _needsRedraw = true;

    protected void UpdateAnimation()
    {
        var now = DateTime.Now;
        _deltaTime = MathF.Max(0, (float)(now - _lastUpdateTime).TotalSeconds);
        _lastUpdateTime = now;
        _animationTime += _deltaTime;
    }

    protected float GetAnimationTime() => AnimationTime;
    protected float GetAnimationDeltaTime() => DeltaTime;
    protected float[] GetAnimatedValues() => AnimatedValues;

    protected void AnimateValues(float[] targets, float speed)
    {
        EnsureAnimationSize(targets.Length);
        int count = Math.Min(_animationValues.Length, targets.Length);
        float clampedSpeed = Math.Clamp(speed, 0f, 1f);

        for (int i = 0; i < count; i++)
            _animationValues[i] += (targets[i] - _animationValues[i]) * clampedSpeed;
    }

    private void EnsureAnimationSize(int size)
    {
        if (_animationValues.Length < size)
            Array.Resize(ref _animationValues, size);
    }

    protected (bool isValid, float[]? processedSpectrum) ProcessSpectrum(
        float[]? spectrum,
        int targetCount,
        int spectrumLength) =>
        _spectrumProcessor.ProcessSpectrum(spectrum, targetCount, spectrumLength);

    protected (bool isValid, float[]? processedSpectrum) PrepareSpectrum(
        float[]? spectrum,
        int targetCount,
        int spectrumLength) =>
        ProcessSpectrum(spectrum, targetCount, spectrumLength);

    protected float[] ProcessSpectrumBands(float[] spectrum, int bandCount) =>
        _spectrumProcessor.ProcessBands(spectrum, bandCount);

    protected void SetProcessingSmoothingFactor(float factor) =>
        _spectrumProcessor.SmoothingFactor = factor;

    protected SKPath GetPath() => _resourcePool.GetPath();
    protected void ReturnPath(SKPath path) => _resourcePool.ReturnPath(path);
    protected SKPaint GetPaint() => _resourcePool.GetPaint();
    protected void ReturnPaint(SKPaint paint) => _resourcePool.ReturnPaint(paint);

    protected float GetOverlayAlphaFactor() => IsOverlayActive ? OverlayAlpha : 1f;

    protected override void OnConfigurationChanged()
    {
        base.OnConfigurationChanged();
        _useAntiAlias = Quality != RenderQuality.Low;
        _spectrumProcessor.SmoothingFactor = IsOverlayActive ? 0.5f : 0.3f;
        OnQualitySettingsApplied();
    }

    protected override void OnOverlayTransparencyChanged(float alpha)
    {
        base.OnOverlayTransparencyChanged(alpha);
        _needsRedraw = true;
    }

    protected override void OnCleanup()
    {
        base.OnCleanup();
        _resourcePool.CleanupUnused();
    }

    protected override void OnDispose()
    {
        _animationValues = [];
        _spectrumProcessor.Dispose();
        _resourcePool.Dispose();
        base.OnDispose();
    }
}