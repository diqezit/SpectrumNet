#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Core;

public abstract class EffectSpectrumRenderer() : ResourceSpectrumRenderer()
{
    private const int
        LowQualityBarLimit = 150,
        MediumQualityBarLimit = 75,
        HighQualityBarLimit = 75;

    private SKShader? _gradient;
    private float _lastGradientHeight;
    private bool _useAdvancedEffects = true;

    protected bool UseAdvancedEffects => _useAdvancedEffects;

    protected void CreateSpectrumGradient(
        float height,
        SKColor[] colors,
        float[]? positions = null)
    {
        _gradient?.Dispose();
        _lastGradientHeight = height;

        _gradient = SKShader.CreateLinearGradient(
            new SKPoint(0, height),
            new SKPoint(0, 0),
            colors,
            positions ?? CreateUniformPositions(colors.Length),
            SKShaderTileMode.Clamp
        );
    }

    protected SKShader? GetSpectrumGradient() => _gradient;

    protected void InvalidateGradientIfNeeded(float height)
    {
        if (Math.Abs(_lastGradientHeight - height) > 0.1f)
        {
            _gradient?.Dispose();
            _gradient = null;
        }
    }

    private static float[] CreateUniformPositions(int count)
    {
        if (count <= 0) return [];

        var positions = new float[count];
        for (int i = 0; i < count; i++)
            positions[i] = i / (float)(count - 1);
        return positions;
    }

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
        int effectiveBarCount = Math.Min(requestedBarCount, maxBars);
        if (effectiveBarCount <= 0)
            effectiveBarCount = 1;

        float totalBarSpace = info.Width / (float)effectiveBarCount;

        return new RenderParameters(
            effectiveBarCount,
            totalBarSpace * 0.8f,
            totalBarSpace * 0.2f,
            0f);
    }

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

        var (isValid, processed) = ProcessSpectrum(
            spectrum,
            renderParams.EffectiveBarCount,
            spectrum!.Length);

        if (!isValid || processed == null)
        {
            drawPerformanceInfo?.Invoke(canvas!, info);
            return;
        }

        UpdateAnimation();

        BeforeRender(canvas!, spectrum!, info, barWidth, barSpacing, barCount, paint!);

        RenderWithOverlay(canvas!, () =>
            RenderEffect(
                canvas!,
                processed,
                info,
                renderParams.BarWidth,
                renderParams.BarSpacing,
                renderParams.EffectiveBarCount,
                paint!));

        AfterRender(canvas!, processed, info);

        drawPerformanceInfo?.Invoke(canvas!, info);
    }

    protected void RenderWithOverlay(SKCanvas canvas, Action renderAction)
    {
        if (IsOverlayActive)
        {
            using var overlayPaint = GetPaint();
            overlayPaint.Color = new SKColor(255, 255, 255, (byte)(255 * OverlayAlpha));
            overlayPaint.Shader = null;
            overlayPaint.MaskFilter = null;
            overlayPaint.Style = SKPaintStyle.Fill;

            int saveCount = canvas.SaveLayer(overlayPaint);
            try
            {
                renderAction();
            }
            finally
            {
                canvas.RestoreToCount(saveCount);
                ReturnPaint(overlayPaint);
            }
        }
        else
        {
            renderAction();
        }
    }

    protected virtual void BeforeRender(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        RenderParameters renderParams,
        SKPaint paint)
    { }

    protected virtual void BeforeRender(
        SKCanvas? canvas,
        float[]? spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint? paint)
    {
        if (canvas != null && spectrum != null && paint != null)
        {
            BeforeRender(
                canvas,
                spectrum,
                info,
                new RenderParameters(barCount, barWidth, barSpacing, 0),
                paint);
        }
    }

    protected virtual void AfterRender(
        SKCanvas canvas,
        float[] processedSpectrum,
        SKImageInfo info)
    { }

    protected abstract void RenderEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint paint);

    protected override void OnConfigurationChanged()
    {
        base.OnConfigurationChanged();
        _useAdvancedEffects = Quality != RenderQuality.Low;
    }

    protected override void OnDispose()
    {
        _gradient?.Dispose();
        _gradient = null;
        base.OnDispose();
    }
}