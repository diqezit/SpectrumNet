#nullable enable

using static SpectrumNet.SN.Visualization.Renderers.RainbowRenderer.Constants;
using static System.MathF;

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class RainbowRenderer : EffectSpectrumRenderer
{
    private const string LogPrefix = nameof(RainbowRenderer);

    private static readonly Lazy<RainbowRenderer> _instance =
        new(() => new RainbowRenderer());

    private RainbowRenderer() { }

    public static RainbowRenderer GetInstance() => _instance.Value;

    public static class Constants
    {
        public const float
            ALPHA_MULTIPLIER = 1.7f,
            SMOOTHING_BASE = 0.3f,
            SMOOTHING_OVERLAY = 0.5f,
            CORNER_RADIUS = 8f,
            GLOW_INTENSITY = 0.3f,
            GLOW_RADIUS = 5f,
            HIGHLIGHT_ALPHA = 0.5f,
            REFLECTION_OPACITY = 0.2f,
            REFLECTION_FACTOR = 0.3f,
            HUE_START = 240f,
            HUE_RANGE = 240f,
            GLOW_THRESHOLD = 0.4f,
            REFLECTION_THRESHOLD = 0.2f,
            HIGHLIGHT_WIDTH_FACTOR = 0.6f,
            HIGHLIGHT_X_OFFSET_FACTOR = 0.2f,
            HIGHLIGHT_MAX_HEIGHT = 10f,
            REFLECTION_HEIGHT_FACTOR = 0.1f,
            BRIGHTNESS_BASE = 90f,
            BRIGHTNESS_RANGE = 10f,
            SATURATION = 100f,
            HUE_WRAP = 360f;

        public const byte
            GLOW_ALPHA_BYTE = (byte)(GLOW_INTENSITY * 255);
    }

    private readonly SKPaint _barPaint = new() { Style = SKPaintStyle.Fill };
    private readonly SKPaint _glowPaint = new()
    {
        Style = SKPaintStyle.Fill,
        IsAntialias = true,
        ImageFilter = SKImageFilter.CreateBlur(GLOW_RADIUS, GLOW_RADIUS)
    };

    protected override void OnInitialize()
    {
        base.OnInitialize();
        _logger.Log(LogLevel.Debug, LogPrefix, "Initialized");
    }

    protected override void OnConfigurationChanged()
    {
        _processingCoordinator.SetSmoothingFactor(
            IsOverlayActive ? SMOOTHING_OVERLAY : SMOOTHING_BASE);
        RequestRedraw();
    }

    protected override void RenderEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint paint)
    {
        var renderParams = CalculateRenderParameters(info, barCount);
        float totalBarWidth = renderParams.BarWidth + renderParams.BarSpacing;
        float startX = (info.Width - spectrum.Length * totalBarWidth + renderParams.BarSpacing) / 2f;

        for (int i = 0; i < spectrum.Length; i++)
        {
            float magnitude = Clamp(spectrum[i], 0f, 1f);
            if (magnitude < MIN_MAGNITUDE_THRESHOLD) continue;

            RenderBar(
                canvas,
                i,
                magnitude,
                startX,
                renderParams,
                info.Height
            );
        }
    }

    private void RenderBar(
        SKCanvas canvas,
        int index,
        float magnitude,
        float startX,
        RenderParameters renderParams,
        float canvasHeight)
    {
        float x = startX + index * (renderParams.BarWidth + renderParams.BarSpacing);
        float barHeight = magnitude * canvasHeight;
        float y = canvasHeight - barHeight;

        var barRect = new SKRect(
            x,
            y,
            x + renderParams.BarWidth,
            canvasHeight
        );

        if (canvas.QuickReject(barRect)) return;

        SKColor barColor = GetRainbowColor(magnitude);
        byte alpha = (byte)(magnitude * ALPHA_MULTIPLIER * 255);

        if (UseAdvancedEffects && magnitude > GLOW_THRESHOLD)
        {
            _glowPaint.Color = barColor.WithAlpha(GLOW_ALPHA_BYTE);
            canvas.DrawRoundRect(barRect, CORNER_RADIUS, CORNER_RADIUS, _glowPaint);
        }

        _barPaint.Color = barColor.WithAlpha(alpha);
        canvas.DrawRoundRect(barRect, CORNER_RADIUS, CORNER_RADIUS, _barPaint);

        if (UseAdvancedEffects && barHeight > CORNER_RADIUS * 2)
        {
            DrawHighlight(canvas, x, y, renderParams.BarWidth, magnitude);

            if (magnitude > REFLECTION_THRESHOLD)
                DrawReflection(
                    canvas,
                    x,
                    canvasHeight,
                    renderParams.BarWidth,
                    barHeight,
                    barColor,
                    magnitude
                );
        }
    }

    private void DrawHighlight(
        SKCanvas canvas,
        float x,
        float y,
        float barWidth,
        float magnitude)
    {
        using var highlightPaint = _resourceManager.GetPaint();
        highlightPaint.Color = SKColors.White.WithAlpha(
            (byte)(magnitude * HIGHLIGHT_ALPHA * 255)
        );

        canvas.DrawRect(
            x + barWidth * HIGHLIGHT_X_OFFSET_FACTOR,
            y,
            barWidth * HIGHLIGHT_WIDTH_FACTOR,
            MathF.Min(HIGHLIGHT_MAX_HEIGHT, CORNER_RADIUS),
            highlightPaint
        );
    }

    private void DrawReflection(
        SKCanvas canvas,
        float x,
        float canvasHeight,
        float barWidth,
        float barHeight,
        SKColor barColor,
        float magnitude)
    {
        using var reflectionPaint = _resourceManager.GetPaint();
        reflectionPaint.Color = barColor.WithAlpha(
            (byte)(magnitude * REFLECTION_OPACITY * 255)
        );
        reflectionPaint.BlendMode = SKBlendMode.SrcOver;

        float reflectHeight = MathF.Min(
            barHeight * REFLECTION_FACTOR,
            canvasHeight * REFLECTION_HEIGHT_FACTOR
        );

        canvas.DrawRect(
            x,
            canvasHeight,
            barWidth,
            reflectHeight,
            reflectionPaint
        );
    }

    private static SKColor GetRainbowColor(float normalizedValue)
    {
        float hue = HUE_START - HUE_RANGE * normalizedValue;
        if (hue < 0) hue += HUE_WRAP;
        float brightness = BRIGHTNESS_BASE + normalizedValue * BRIGHTNESS_RANGE;
        return SKColor.FromHsv(hue, SATURATION, brightness);
    }

    protected override void OnDispose()
    {
        _barPaint?.Dispose();
        _glowPaint?.Dispose();
        base.OnDispose();
        _logger.Log(LogLevel.Debug, LogPrefix, "Disposed");
    }
}