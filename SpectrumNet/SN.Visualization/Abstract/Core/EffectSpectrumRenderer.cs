#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Core;

public abstract class EffectSpectrumRenderer : BaseSpectrumRenderer
{
    private const int
        LowQualityBarLimit = 150,
        MediumQualityBarLimit = 75,
        HighQualityBarLimit = 75;

    private readonly IResourceManager _resourceManager;
    private readonly IAnimationTimer _animationTimer;
    private readonly object _renderLock = new();
    private readonly Dictionary<string, IPaintConfig> _paintConfigs = new();
    private IPathBatchRenderer? _pathBatchRenderer;
    private readonly IValueAnimatorArray _valueAnimator;
    private readonly IPeakTracker _peakTracker;
    private readonly IGradientManager _gradientManager;
    private readonly ICommonEffects _commonEffects;

    protected EffectSpectrumRenderer(
        ISpectrumProcessingCoordinator? processingCoordinator = null,
        IQualityManager? qualityManager = null,
        IOverlayStateManager? overlayStateManager = null,
        IResourceManager? resourceManager = null,
        IAnimationTimer? animationTimer = null,
        IRenderingHelpers? renderingHelpers = null,
        IBufferManager? bufferManager = null,
        ISpectrumBandProcessor? bandProcessor = null) : base(
            processingCoordinator,
            qualityManager,
            overlayStateManager,
            renderingHelpers,
            bufferManager,
            bandProcessor)
    {
        _resourceManager = resourceManager ?? new ResourceManager();
        _animationTimer = animationTimer ?? new AnimationTimer();
        _valueAnimator = new ValueAnimatorArray();
        _peakTracker = new PeakTracker();
        _gradientManager = new GradientManager();
        _commonEffects = new CommonEffects(_resourceManager);
    }

    protected override float GetAnimationTime() => _animationTimer.Time;
    protected override float GetAnimationDeltaTime() => _animationTimer.DeltaTime;

    protected void UpdateAnimation() => _animationTimer.Update();
    protected void ResetAnimation() => _animationTimer.Reset();

    // Resource management
    protected SKPath GetPath() => _resourceManager.GetPath();
    protected void ReturnPath(SKPath path) => _resourceManager.ReturnPath(path);
    protected SKPaint GetPaint() => _resourceManager.GetPaint();
    protected void ReturnPaint(SKPaint paint) => _resourceManager.ReturnPaint(paint);

    protected void ReleasePaints(params SKPaint[] paints)
    {
        foreach (var paint in paints)
        {
            if (paint != null)
                ReturnPaint(paint);
        }
    }

    protected void ReleasePaths(params SKPath[] paths)
    {
        foreach (var path in paths)
        {
            if (path != null)
                ReturnPath(path);
        }
    }

    // для анимации
    protected void AnimateValues(float[] targets, float speed)
    {
        _valueAnimator.EnsureSize(targets.Length);
        _valueAnimator.Update(targets, speed);
    }

    protected float[] GetAnimatedValues() => _valueAnimator.Values;

    // для пиков
    protected void UpdatePeaks(float[] values, float deltaTime)
    {
        _peakTracker.EnsureSize(values.Length);
        for (int i = 0; i < values.Length; i++)
            _peakTracker.Update(i, values[i], deltaTime);
    }

    protected float GetPeak(int index) => _peakTracker.GetPeak(index);

    protected void ConfigurePeaks(float holdTime, float fallSpeed) =>
        _peakTracker.Configure(holdTime, fallSpeed);

    protected bool HasActivePeaks() => _peakTracker.HasActivePeaks();

    // для градиентов
    protected void CreateSpectrumGradient(float height, SKColor[] colors, float[]? positions = null)
    {
        _gradientManager.CreateLinearGradient(height, colors, positions);
    }

    protected SKShader? GetSpectrumGradient() => _gradientManager.CurrentGradient;

    protected void InvalidateGradientIfNeeded(float height) =>
        _gradientManager.InvalidateIfHeightChanged(height);

    // для эффектов
    protected void RenderGlow(SKCanvas canvas, SKRect rect, SKColor color, float radius, float alpha) =>
        _commonEffects.RenderGlow(canvas, rect, color, radius, alpha);

    protected void RenderGlow(SKCanvas canvas, SKPath path, SKColor color, float radius, float alpha) =>
        _commonEffects.RenderGlow(canvas, path, color, radius, alpha);

    // PaintConfig management
    protected void RegisterPaintConfig(string name, IPaintConfig config) =>
        _paintConfigs[name] = config;

    protected IPaintConfig GetPaintConfig(string name) =>
        _paintConfigs.TryGetValue(name, out var config) ? config : PaintConfig.Default;

    protected void RemovePaintConfig(string name) =>
        _paintConfigs.Remove(name);

    protected void ClearPaintConfigs() =>
        _paintConfigs.Clear();

    protected IPaintConfig CreateDefaultPaintConfig(SKColor color) =>
        new PaintConfig(color);

    protected IPaintConfig CreateStrokePaintConfig(
        SKColor color,
        float strokeWidth,
        SKStrokeCap cap = SKStrokeCap.Round,
        SKStrokeJoin join = SKStrokeJoin.Round) =>
        new PaintConfig(color, Stroke, strokeWidth, cap, join);

    protected IPaintConfig CreateGlowPaintConfig(SKColor color, float blurRadius) =>
        PaintConfig.Glow(color, blurRadius);

    protected IPaintConfig CreateEdgePaintConfig(SKColor color, float width, float blurRadius = 0) =>
        PaintConfig.Edge(color, width, blurRadius);

    // Paint creation methods
    protected SKPaint CreatePaint(IPaintConfig config)
    {
        var paint = _resourceManager.GetPaint();
        paint.Color = config.Color;
        paint.Style = config.Style;
        paint.IsAntialias = UseAntiAlias;

        if (config.Style == Stroke)
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
        paint.Style = Fill;
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
        paint.Style = Stroke;
        paint.StrokeWidth = strokeWidth;
        paint.StrokeCap = cap;
        paint.IsAntialias = UseAntiAlias;
        return paint;
    }

    protected SKPaint CreateGlowPaint(SKColor color, float blurRadius) =>
        CreatePaint(PaintConfig.Glow(color, blurRadius));

    protected SKPaint CreateEdgePaint(SKColor color, float width, float blurRadius = 0) =>
        CreatePaint(PaintConfig.Edge(color, width, blurRadius));

    // Visibility helpers
    protected bool IsAreaVisible(SKCanvas? canvas, SKRect rect) =>
        IsRectVisible(canvas, rect);

    // PathBatchRenderer methods
    protected IPathBatchRenderer GetPathBatchRenderer()
    {
        _pathBatchRenderer ??= new PathBatchRenderer(_resourceManager);
        return _pathBatchRenderer;
    }

    protected void RenderBatch(
        SKCanvas canvas,
        Action<SKPath> buildPath,
        SKPaint paint) =>
        GetPathBatchRenderer().RenderBatch(canvas, buildPath, paint);

    protected void RenderFiltered<T>(
        SKCanvas canvas,
        IEnumerable<T> items,
        Func<T, bool> filter,
        Action<SKPath, T> addToPath,
        SKPaint paint) =>
        GetPathBatchRenderer().RenderFiltered(canvas, items, filter, addToPath, paint);

    protected void RenderRects(
        SKCanvas canvas,
        IEnumerable<SKRect> rects,
        SKPaint paint,
        float cornerRadius = 0) =>
        GetPathBatchRenderer().RenderRects(canvas, rects, paint, cornerRadius);

    protected void RenderCircles(
        SKCanvas canvas,
        IEnumerable<(SKPoint center, float radius)> circles,
        SKPaint paint) =>
        GetPathBatchRenderer().RenderCircles(canvas, circles, paint);

    protected void RenderLines(
        SKCanvas canvas,
        IEnumerable<(SKPoint start, SKPoint end)> lines,
        SKPaint paint) =>
        GetPathBatchRenderer().RenderLines(canvas, lines, paint);

    protected void RenderPolygon(
        SKCanvas canvas,
        SKPoint[] points,
        SKPaint paint,
        bool close = true) =>
        GetPathBatchRenderer().RenderPolygon(canvas, points, paint, close);

    // Abstract method for effect implementation
    protected abstract void RenderEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint paint);

    // Quality and render parameters
    protected virtual int GetMaxBarsForQuality() => Quality switch
    {
        RenderQuality.Low => LowQualityBarLimit,
        RenderQuality.Medium => MediumQualityBarLimit,
        RenderQuality.High => HighQualityBarLimit,
        _ => HighQualityBarLimit
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
        int effectiveBarCount = Min(requestedBarCount, maxBars);
        float totalBarSpace = info.Width / effectiveBarCount;

        return new RenderParameters(
            effectiveBarCount,
            totalBarSpace * 0.8f,
            totalBarSpace * 0.2f,
            0f);
    }

    // Initialization
    public override void Initialize() =>
        SafeExecute(() =>
        {
            base.Initialize();
            OnInitialize();
            LogDebug("Initialized");
        }, "Failed to initialize renderer");

    protected override void OnConfigurationChanged()
    {
        base.OnConfigurationChanged();
        OnQualitySettingsApplied();
    }

    protected virtual void OnQualitySettingsApplied() { }

    // Main render method
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

    // Overlay rendering
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

    // Virtual methods for customization
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

    //  если есть пики то нужно будет преопределить
    public override bool RequiresRedraw() =>
        base.RequiresRedraw() || HasActivePeaks();

    // Cleanup
    protected override void CleanupUnusedResources() => base.CleanupUnusedResources();

    protected override void OnDispose()
    {
        _pathBatchRenderer?.Dispose();
        _resourceManager?.Dispose();
        _gradientManager?.Dispose();
        base.OnDispose();
    }
}