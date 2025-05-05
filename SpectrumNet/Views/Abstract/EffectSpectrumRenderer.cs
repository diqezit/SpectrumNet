#nullable enable

namespace SpectrumNet.Views.Abstract;

public abstract class EffectSpectrumRenderer : BaseSpectrumRenderer
{
    private const int DefaultPoolSize = 5;
    private const string LOG_PREFIX = nameof(EffectSpectrumRenderer);

    protected readonly ObjectPool<SKPath> _pathPool = new(
        () => new SKPath(),
        path => path.Reset(),
        DefaultPoolSize);

    protected readonly ObjectPool<SKPaint> _paintPool = new(
        () => new SKPaint(),
        paint => paint.Reset(),
        DefaultPoolSize);

    private readonly object _renderLock = new();

    protected bool _isOverlayActive;
    protected float _time;
    protected DateTime _lastUpdateTime = Now;

    protected abstract void RenderEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint paint);

    public override void Initialize() => ExecuteSafely(
        InitializeRenderer,
        nameof(Initialize),
        "Failed to initialize renderer");

    private void InitializeRenderer()
    {
        base.Initialize();
        ApplyQualitySettings();
        OnInitialize();
        Log(LogLevel.Debug, LOG_PREFIX, "Initialized");
    }

    protected virtual void OnInitialize() { }

    public override void Configure(
        bool isOverlayActive,
        RenderQuality quality = RenderQuality.Medium) => ExecuteSafely(
        () => ConfigureRenderer(isOverlayActive, quality),
        nameof(Configure),
        "Failed to configure renderer");

    private void ConfigureRenderer(
        bool isOverlayActive,
        RenderQuality quality)
    {
        var configChanged = _isOverlayActive != isOverlayActive || Quality != quality;
        base.Configure(isOverlayActive, quality);
        _isOverlayActive = isOverlayActive;
        Quality = quality;
        if (configChanged)
            OnConfigurationChanged();
    }

    protected virtual void OnConfigurationChanged() { }

    public override void Render(
        SKCanvas? canvas,
        float[]? spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint? paint,
        Action<SKCanvas, SKImageInfo>? drawPerformanceInfo)
    {
        var (isValid, processed) = PrepareRender(
            canvas,
            spectrum,
            info,
            barCount,
            paint);
        if (!isValid)
        {
            drawPerformanceInfo?.Invoke(
                canvas!,
                info);
            return;
        }

        var cv = canvas!;
        var sp = spectrum!;
        var pr = processed!;
        var pt = paint!;

        ExecuteSafely(
            () => RenderLocked(
                cv,
                sp,
                pr,
                info,
                barWidth,
                barSpacing,
                pt),
            nameof(Render),
            "Error during rendering");

        drawPerformanceInfo?.Invoke(
            cv,
            info);
    }

    private void RenderLocked(
        SKCanvas canvas,
        float[] spectrum,
        float[] processedSpectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        SKPaint paint)
    {
        lock (_renderLock)
        {
            UpdateTiming();
            BeforeRender(
                canvas,
                spectrum,
                info,
                barWidth,
                barSpacing,
                processedSpectrum.Length,
                paint);
            RenderEffect(
                canvas,
                processedSpectrum,
                info,
                barWidth,
                barSpacing,
                processedSpectrum.Length,
                paint);
            AfterRender(
                canvas,
                processedSpectrum,
                info);
        }
    }

    private void UpdateTiming()
    {
        var now = Now;
        var delta = MathF.Max(
            0,
            (float)(now - _lastUpdateTime).TotalSeconds);
        _lastUpdateTime = now;
        _time += delta;
    }

    public override void Dispose()
    {
        if (!_disposed)
        {
            ExecuteSafely(
                () =>
                {
                    OnDispose();
                    _pathPool.Dispose();
                    _paintPool.Dispose();
                    base.Dispose();
                    _disposed = true;
                },
                nameof(Dispose),
                "Error during disposal");

            GC.SuppressFinalize(this);
        }
    }

    protected virtual void OnDispose() { }

    protected override void ApplyQualitySettings() => ExecuteSafely(
        () =>
        {
            (_useAntiAlias, _useAdvancedEffects) = QualityBasedSettings();
            _samplingOptions = QualityBasedSamplingOptions();
            OnQualitySettingsApplied();
        },
        nameof(ApplyQualitySettings),
        "Failed to apply quality settings");

    protected virtual void OnQualitySettingsApplied() { }

    protected override (bool useAntiAlias, bool useAdvancedEffects) QualityBasedSettings() =>
        Quality switch
        {
            RenderQuality.Low => (false, false),
            RenderQuality.Medium => (true, true),
            RenderQuality.High => (true, true),
            _ => (true, true)
        };

    protected override SKSamplingOptions QualityBasedSamplingOptions() =>
        Quality switch
        {
            RenderQuality.Low => new SKSamplingOptions(
                SKFilterMode.Nearest,
                SKMipmapMode.None),
            RenderQuality.Medium => new SKSamplingOptions(
                SKFilterMode.Linear,
                SKMipmapMode.Linear),
            RenderQuality.High => new SKSamplingOptions(
                SKFilterMode.Linear,
                SKMipmapMode.Linear),
            _ => new SKSamplingOptions(
                SKFilterMode.Linear,
                SKMipmapMode.Linear)
        };

    protected virtual void BeforeRender(
        SKCanvas? canvas,
        float[]? spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint? paint)
    { }

    protected virtual void AfterRender(
        SKCanvas canvas,
        float[] processedSpectrum,
        SKImageInfo info)
    { }

    protected SKPaint CreateStandardPaint(
        SKColor color) => InitPaint(
            color,
            SKPaintStyle.Fill,
            null);

    protected SKPaint CreateGlowPaint(
        SKColor color,
        float radius,
        byte alpha)
    {
        var blur = SKMaskFilter.CreateBlur(
            SKBlurStyle.Normal,
            radius);
        return InitPaint(
            color.WithAlpha(alpha),
            SKPaintStyle.Fill,
            blur);
    }

    private SKPaint InitPaint(
        SKColor color,
        SKPaintStyle style,
        SKMaskFilter? maskFilter)
    {
        var paint = _paintPool.Get();
        paint.Color = color;
        paint.Style = style;
        paint.IsAntialias = UseAntiAlias;
        if (maskFilter is not null)
            paint.MaskFilter = maskFilter;
        return paint;
    }

    protected void InvalidateCachedResources() => ExecuteSafely(
        OnInvalidateCachedResources,
        nameof(InvalidateCachedResources),
        "Failed to invalidate cached resources");

    protected virtual void OnInvalidateCachedResources() { }
}