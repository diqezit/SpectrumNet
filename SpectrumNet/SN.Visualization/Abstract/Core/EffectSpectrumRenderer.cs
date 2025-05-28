#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Core;

public abstract class EffectSpectrumRenderer : PaintSpectrumRenderer
{
    private const int LowQualityBarLimit = 150;
    private const int MediumQualityBarLimit = 75;
    private const int HighQualityBarLimit = 75;

    private readonly object _renderLock = new();
    private IPathBatchRenderer? _pathBatchRenderer;
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
            bandProcessor,
            animationTimer,
            resourceManager)
    {
        _peakTracker = new PeakTracker();
        _gradientManager = new GradientManager();
        _commonEffects = new CommonEffects(resourceManager ?? new ResourceManager());
    }

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

    protected void CreateSpectrumGradient(
        float height,
        SKColor[] colors,
        float[]? positions = null)
    {
        _gradientManager.CreateLinearGradient(height, colors, positions);
    }

    protected SKShader? GetSpectrumGradient() => _gradientManager.CurrentGradient;

    protected void InvalidateGradientIfNeeded(float height) =>
        _gradientManager.InvalidateIfHeightChanged(height);

    protected void RenderGlow(
        SKCanvas canvas,
        SKRect rect,
        SKColor color,
        float radius,
        float alpha) =>
        _commonEffects.RenderGlow(canvas, rect, color, radius, alpha);

    protected void RenderGlow(
        SKCanvas canvas,
        SKPath path,
        SKColor color,
        float radius,
        float alpha) =>
        _commonEffects.RenderGlow(canvas, path, color, radius, alpha);

    protected bool IsAreaVisible(SKCanvas? canvas, SKRect rect) =>
        IsRectVisible(canvas, rect);

    protected IPathBatchRenderer GetPathBatchRenderer()
    {
        _pathBatchRenderer ??= new PathBatchRenderer(
            new ResourceManager());
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
        GetPathBatchRenderer().RenderFiltered(
            canvas,
            items,
            filter,
            addToPath,
            paint);

    protected void RenderRects(
        SKCanvas canvas,
        IEnumerable<SKRect> rects,
        SKPaint paint,
        float cornerRadius = 0) =>
        GetPathBatchRenderer().RenderRects(
            canvas,
            rects,
            paint,
            cornerRadius);

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
        GetPathBatchRenderer().RenderPolygon(
            canvas,
            points,
            paint,
            close);

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
                UpdateAnimation();

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
            try
            {
                renderAction();
            }
            finally
            {
                canvas.Restore();
            }
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

    public override bool RequiresRedraw() =>
        base.RequiresRedraw() || HasActivePeaks();

    protected override void CleanupUnusedResources()
    {
        base.CleanupUnusedResources();
    }

    protected override void OnDispose()
    {
        _pathBatchRenderer?.Dispose();
        _gradientManager?.Dispose();
        base.OnDispose();
    }
}