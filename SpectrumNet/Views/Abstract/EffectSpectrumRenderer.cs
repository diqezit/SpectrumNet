#nullable enable

namespace SpectrumNet.Views.Abstract;

public abstract class EffectSpectrumRenderer : BaseSpectrumRenderer
{
    private const int DEFAULT_POOL_SIZE = 5;
    private const string LOG_PREFIX = nameof(EffectSpectrumRenderer);

    protected const float 
        DEFAULT_OVERLAY_ALPHA_FACTOR = RendererTransparencyManager.Constants.INACTIVE_TRANSPARENCY,
        HOVER_OVERLAY_ALPHA_FACTOR = RendererTransparencyManager.Constants.ACTIVE_TRANSPARENCY;

    protected readonly ObjectPool<SKPath> _pathPool = new(
        () => new SKPath(),
        path => path.Reset(),
        DEFAULT_POOL_SIZE);

    protected readonly ObjectPool<SKPaint> _paintPool = new(
        () => new SKPaint(),
        paint => paint.Reset(),
        DEFAULT_POOL_SIZE);

    private readonly object _renderLock = new();
    protected float _time;
    protected DateTime _lastUpdateTime = Now;

    protected float _overlayAlphaFactor = DEFAULT_OVERLAY_ALPHA_FACTOR;
    protected bool _overlayStateChanged;
    protected bool _overlayStateChangeRequested;

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

    public override void SetOverlayTransparency(float level)
    {
        if (Math.Abs(_overlayAlphaFactor - level) > float.Epsilon)
        {
            _overlayAlphaFactor = level;
            _overlayStateChanged = true;
            _overlayStateChangeRequested = true;
            RequestRedraw();
        }
    }

    protected virtual void RequestRedraw()
    {
        _overlayStateChangeRequested = true;
    }

    protected override void OnInitialize() { }

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
        bool overlayChanged = _isOverlayActive != isOverlayActive;
        bool qualityChanged = _quality != quality;

        _isOverlayActive = isOverlayActive;
        Quality = quality;

        _smoothingFactor = isOverlayActive ? 0.5f : 0.3f;

        if (overlayChanged)
        {
            _overlayAlphaFactor = isOverlayActive ? HOVER_OVERLAY_ALPHA_FACTOR : DEFAULT_OVERLAY_ALPHA_FACTOR;
            _overlayStateChanged = true;
            _overlayStateChangeRequested = true;
        }

        if (overlayChanged || qualityChanged)
        {
            OnConfigurationChanged();
        }
    }

    protected override void OnConfigurationChanged() { }

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
            drawPerformanceInfo?.Invoke(canvas!, info);
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

        drawPerformanceInfo?.Invoke(cv, info);
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
            if (_overlayStateChangeRequested)
            {
                _overlayStateChangeRequested = false;
                _overlayStateChanged = true;
            }

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

            if (_overlayStateChanged)
            {
                _overlayStateChanged = false;
            }
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

    public override bool RequiresRedraw()
    {
        return _overlayStateChanged ||
               _overlayStateChangeRequested ||
               _isOverlayActive;
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
            if (_isApplyingQuality) return;

            try
            {
                _isApplyingQuality = true;

                base.ApplyQualitySettings();
                OnQualitySettingsApplied();
            }
            finally
            {
                _isApplyingQuality = false;
            }
        },
        nameof(ApplyQualitySettings),
        "Failed to apply quality settings");

    protected virtual void OnQualitySettingsApplied() { }

    protected override (bool useAntiAlias, bool useAdvancedEffects) QualityBasedSettings() =>
        base.QualityBasedSettings();

    protected override SKSamplingOptions QualityBasedSamplingOptions() =>
        base.QualityBasedSamplingOptions();

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

    protected void RenderWithOverlay(SKCanvas canvas, Action renderAction)
    {
        if (_isOverlayActive)
        {
            RenderOverlayMode(canvas, renderAction);
        }
        else
        {
            RenderNormalMode(renderAction);
        }
    }

    private void RenderOverlayMode(SKCanvas canvas, Action renderAction)
    {
        using var overlayPaint = new SKPaint
        {
            Color = new SKColor(255, 255, 255, (byte)(255 * _overlayAlphaFactor))
        };

        canvas.SaveLayer(overlayPaint);
        try
        {
            renderAction();
        }
        finally
        {
            canvas.Restore();
        }
    }

    private void RenderNormalMode(Action renderAction) =>
        renderAction();
}