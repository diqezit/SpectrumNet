#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract;

public abstract class EffectSpectrumRenderer(
    ISpectrumProcessingCoordinator? processingCoordinator = null,
    IQualityManager? qualityManager = null,
    IOverlayStateManager? overlayStateManager = null,
    IResourceManager? resourceManager = null,
    IAnimationTimer? animationTimer = null)
    : BaseSpectrumRenderer(processingCoordinator, qualityManager, overlayStateManager)
{
    private const string LogPrefix = nameof(EffectSpectrumRenderer);

    protected readonly IResourceManager _resourceManager = resourceManager ?? new ResourceManager();
    protected readonly IAnimationTimer _animationTimer = animationTimer ?? new AnimationTimer();
    private readonly object _renderLock = new();

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
        float StartOffset
    );

    protected virtual RenderParameters CalculateRenderParameters(
        SKImageInfo info,
        int requestedBarCount)
    {
        int maxBars = GetMaxBarsForQuality();
        int effectiveBarCount = Math.Min(requestedBarCount, maxBars);
        float totalWidth = info.Width;

        float totalBarSpace = totalWidth / effectiveBarCount;
        float barWidth = totalBarSpace * 0.8f;
        float barSpacing = totalBarSpace * 0.2f;

        return new RenderParameters(
            effectiveBarCount,
            barWidth,
            barSpacing,
            0f
        );
    }

    public override void Initialize()
    {
        _logger.Safe(() => HandleInitialize(),
                  LogPrefix,
                  "Failed to initialize renderer");
    }

    protected override void HandleInitialize()
    {
        base.HandleInitialize();
        OnInitialize();
        _logger.Log(LogLevel.Debug, LogPrefix, "Initialized");
    }

    protected virtual void RequestRedraw() => _needsRedraw = true;

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
        if (!ValidateRenderParameters(canvas, spectrum, info, barWidth, barSpacing, barCount, paint))
        {
            drawPerformanceInfo?.Invoke(canvas!, info);
            return;
        }

        var renderParams = CalculateRenderParameters(info, barCount);

        var (isValid, processed) = _processingCoordinator.PrepareSpectrum(
            spectrum,
            renderParams.EffectiveBarCount,
            spectrum!.Length);

        if (!isValid)
        {
            drawPerformanceInfo?.Invoke(canvas!, info);
            return;
        }

        var cv = canvas!;
        var sp = spectrum!;
        var pr = processed!;
        var pt = paint!;

        _logger.Safe(() => RenderLocked(cv, sp, pr, info, renderParams, pt),
                 LogPrefix,
                 "Error during rendering");

        drawPerformanceInfo?.Invoke(cv, info);
    }

    private void RenderLocked(
        SKCanvas canvas,
        float[] spectrum,
        float[] processedSpectrum,
        SKImageInfo info,
        RenderParameters renderParams,
        SKPaint paint)
    {
        lock (_renderLock)
        {
            _animationTimer.Update();

            BeforeRender(
                canvas,
                spectrum,
                info,
                renderParams.BarWidth,
                renderParams.BarSpacing,
                renderParams.EffectiveBarCount,
                paint);

            RenderWithOverlay(canvas, () =>
                RenderEffect(
                    canvas,
                    processedSpectrum,
                    info,
                    renderParams.BarWidth,
                    renderParams.BarSpacing,
                    renderParams.EffectiveBarCount,
                    paint));

            AfterRender(
                canvas,
                processedSpectrum,
                info);

            if (_overlayStateManager.StateChanged)
            {
                _overlayStateManager.ResetStateFlags();
                _needsRedraw = false;
            }
        }
    }

    protected void RenderWithOverlay(SKCanvas canvas, Action renderAction)
    {
        if (_overlayStateManager.IsOverlayActive)
        {
            using var overlayPaint = new SKPaint
            {
                Color = new SKColor(255, 255, 255,
                    (byte)(255 * _overlayStateManager.OverlayAlphaFactor))
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

    protected SKPaint CreateStandardPaint(SKColor color)
    {
        var paint = _resourceManager.GetPaint();
        paint.Color = color;
        paint.Style = SKPaintStyle.Fill;
        paint.IsAntialias = UseAntiAlias;
        return paint;
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

    protected override void HandleDispose()
    {
        if (!_disposed)
        {
            OnDispose();
            _resourceManager?.Dispose();
            base.HandleDispose();
        }
    }
}