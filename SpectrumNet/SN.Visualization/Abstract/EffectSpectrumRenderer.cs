#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract;

public abstract class EffectSpectrumRenderer : BaseSpectrumRenderer
{
    private readonly IResourceManager _resourceManager;
    private readonly IAnimationTimer _animationTimer;
    private readonly object _renderLock = new();
    private readonly Dictionary<string, IPaintConfig> _paintConfigs = new();

    protected EffectSpectrumRenderer(
        ISpectrumProcessingCoordinator? processingCoordinator = null,
        IQualityManager? qualityManager = null,
        IOverlayStateManager? overlayStateManager = null,
        IResourceManager? resourceManager = null,
        IAnimationTimer? animationTimer = null,
        IRenderingHelpers? renderingHelpers = null)
        : base(processingCoordinator, qualityManager, overlayStateManager, renderingHelpers)
    {
        _resourceManager = resourceManager ?? new ResourceManager();
        _animationTimer = animationTimer ?? new AnimationTimer();
    }

    protected override float GetAnimationTime() => _animationTimer.Time;
    protected override float GetAnimationDeltaTime() => _animationTimer.DeltaTime;

    protected void UpdateAnimation() => _animationTimer.Update();
    protected void ResetAnimation() => _animationTimer.Reset();

    protected SKPath GetPath() => _resourceManager.GetPath();
    protected void ReturnPath(SKPath path) => _resourceManager.ReturnPath(path);
    protected SKPaint GetPaint() => _resourceManager.GetPaint();
    protected void ReturnPaint(SKPaint paint) => _resourceManager.ReturnPaint(paint);

    protected void RegisterPaintConfig(string name, IPaintConfig config) =>
        _paintConfigs[name] = config;

    protected IPaintConfig GetPaintConfig(string name) =>
        _paintConfigs.TryGetValue(name, out var config) ? config : PaintConfig.Default;

    protected SKPaint CreatePaint(IPaintConfig config)
    {
        var paint = _resourceManager.GetPaint();
        paint.Color = config.Color;
        paint.Style = config.Style;
        paint.IsAntialias = UseAntiAlias;

        if (config.Style == SKPaintStyle.Stroke)
        {
            paint.StrokeWidth = config.StrokeWidth;
            paint.StrokeCap = config.StrokeCap;
            paint.StrokeJoin = config.StrokeJoin;
        }

        if (config.BlurRadius > 0)
            paint.ImageFilter = SKImageFilter.CreateBlur(config.BlurRadius, config.BlurRadius);

        if (config.MaskBlurRadius > 0)
            paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, config.MaskBlurRadius);

        return paint;
    }

    protected SKPaint CreatePaint(string configName) =>
        CreatePaint(GetPaintConfig(configName));

    protected SKPaint CreateStandardPaint(SKColor color)
    {
        var paint = _resourceManager.GetPaint();
        paint.Color = color;
        paint.Style = SKPaintStyle.Fill;
        paint.IsAntialias = UseAntiAlias;
        return paint;
    }

    protected SKPaint CreateStrokePaint(
        SKColor color,
        float strokeWidth,
        SKStrokeCap cap = SKStrokeCap.Round)
    {
        var paint = _resourceManager.GetPaint();
        paint.Color = color;
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = strokeWidth;
        paint.StrokeCap = cap;
        paint.IsAntialias = UseAntiAlias;
        return paint;
    }

    protected SKPaint CreateGlowPaint(SKColor color, float blurRadius) =>
        CreatePaint(PaintConfig.Glow(color, blurRadius));

    protected SKPaint CreateEdgePaint(SKColor color, float width, float blurRadius = 0) =>
        CreatePaint(PaintConfig.Edge(color, width, blurRadius));

    protected bool IsAreaVisible(SKCanvas? canvas, SKRect rect) =>
        IsRectVisible(canvas, rect);

    protected abstract void RenderEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint paint);

    protected virtual int GetMaxBarsForQuality() => Quality switch
    {
        RenderQuality.Low => 150,
        RenderQuality.Medium => 75,
        RenderQuality.High => 75,
        _ => 75
    };

    protected record RenderParameters(
        int EffectiveBarCount,
        float BarWidth,
        float BarSpacing,
        float StartOffset);

    protected virtual RenderParameters CalculateRenderParameters(
        SKImageInfo info,
        int requestedBarCount)
    {
        int maxBars = GetMaxBarsForQuality();
        int effectiveBarCount = Math.Min(requestedBarCount, maxBars);
        float totalBarSpace = info.Width / effectiveBarCount;

        return new RenderParameters(
            effectiveBarCount,
            totalBarSpace * 0.8f,
            totalBarSpace * 0.2f,
            0f);
    }

    public override void Initialize() =>
        SafeExecute(() =>
        {
            base.Initialize();
            OnInitialize();
            LogDebug("Initialized");
        }, "Failed to initialize renderer");

    protected virtual void RequestRedraw() => base.RequiresRedraw();

    protected override void OnConfigurationChanged()
    {
        base.OnConfigurationChanged();
        OnQualitySettingsApplied();
    }

    protected virtual void OnQualitySettingsApplied() { }

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
        if (!ValidateRenderParameters(
            canvas, spectrum, info, barWidth, barSpacing, barCount, paint))
        {
            drawPerformanceInfo?.Invoke(canvas!, info);
            return;
        }

        var renderParams = CalculateRenderParameters(info, barCount);
        var (isValid, processed) = PrepareSpectrum(
            spectrum,
            renderParams.EffectiveBarCount,
            spectrum!.Length);

        if (!isValid)
        {
            drawPerformanceInfo?.Invoke(canvas!, info);
            return;
        }

        SafeExecute(() =>
        {
            lock (_renderLock)
            {
                _animationTimer.Update();

                BeforeRender(
                    canvas!, spectrum!, info,
                    renderParams.BarWidth,
                    renderParams.BarSpacing,
                    renderParams.EffectiveBarCount,
                    paint!);

                RenderWithOverlay(canvas!, () =>
                    RenderEffect(
                        canvas!, processed!, info,
                        renderParams.BarWidth,
                        renderParams.BarSpacing,
                        renderParams.EffectiveBarCount,
                        paint!));

                AfterRender(canvas!, processed!, info);

                if (IsOverlayStateChanged())
                    ResetOverlayStateFlags();
            }
        }, "Error during rendering");

        drawPerformanceInfo?.Invoke(canvas!, info);
    }

    protected void RenderWithOverlay(SKCanvas canvas, Action renderAction)
    {
        if (IsOverlayActive)
        {
            using var overlayPaint = new SKPaint
            {
                Color = new SKColor(
                    255, 255, 255,
                    (byte)(255 * GetOverlayAlphaFactor()))
            };

            canvas.SaveLayer(overlayPaint);
            try { renderAction(); }
            finally { canvas.Restore(); }
        }
        else
        {
            renderAction();
        }
    }

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

    protected override void OnDispose()
    {
        _resourceManager?.Dispose();
        base.OnDispose();
    }
}