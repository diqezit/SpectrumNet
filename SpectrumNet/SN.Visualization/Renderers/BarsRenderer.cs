#nullable enable

using static SpectrumNet.SN.Visualization.Renderers.BarsRenderer.Constants;

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class BarsRenderer : EffectSpectrumRenderer
{
    private static readonly Lazy<BarsRenderer> _instance = 
        new(() => new BarsRenderer());

    public static BarsRenderer GetInstance() => _instance.Value;

    public static class Constants
    {
        public const float
            DEFAULT_CORNER_RADIUS_FACTOR = 0.5f,
            MIN_BAR_HEIGHT = 1f,
            MAX_CORNER_RADIUS = 125f,
            GLOW_EFFECT_ALPHA = 0.25f,
            ALPHA_MULTIPLIER = 1.5f,
            HIGH_INTENSITY_THRESHOLD = 0.6f;

        public static readonly Dictionary<RenderQuality, QualitySettings> QualityPresets = new()
        {
            [RenderQuality.Low] = new(
                GlowRadius: 1.0f,
                EdgeStrokeWidth: 0f,
                EdgeBlurRadius: 0f
            ),
            [RenderQuality.Medium] = new(
                GlowRadius: 2.0f,
                EdgeStrokeWidth: 1.5f,
                EdgeBlurRadius: 1f
            ),
            [RenderQuality.High] = new(
                GlowRadius: 3.0f,
                EdgeStrokeWidth: 2.5f,
                EdgeBlurRadius: 2f
            )
        };

        public record QualitySettings(
            float GlowRadius,
            float EdgeStrokeWidth,
            float EdgeBlurRadius
        );
    }

    private QualitySettings _currentSettings = QualityPresets[RenderQuality.Medium];

    protected override void OnInitialize() =>
        UpdatePaintConfigs();

    protected override void OnConfigurationChanged() =>
        UpdatePaintConfigs();

    private void UpdatePaintConfigs()
    {
        _currentSettings = QualityPresets[Quality];

        RegisterPaintConfig(
            "glow",
            PaintConfig.Glow(SKColors.White, _currentSettings.GlowRadius));

        RegisterPaintConfig(
            "edge",
            PaintConfig.Edge(
                SKColors.White,
                _currentSettings.EdgeStrokeWidth,
                _currentSettings.EdgeBlurRadius));
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
        float cornerRadius = Min(
            barWidth * DEFAULT_CORNER_RADIUS_FACTOR,
            MAX_CORNER_RADIUS);

        int spectrumLength = Min(barCount, spectrum.Length);

        if (UseAdvancedEffects)
        {
            using SKPaint glowPaint = GlowEffects(canvas, spectrum, info, 
                barWidth, barSpacing, cornerRadius, spectrumLength);
        }

        for (int i = 0; i < spectrumLength; i++)
        {
            bool flowControl = MainBars(canvas, spectrum, info, barWidth, 
                barSpacing, paint, cornerRadius, i);

            if (!flowControl)
            {
                continue;
            }
        }

        if (UseAdvancedEffects && _currentSettings.EdgeStrokeWidth > 0)
        {
            using SKPaint edgePaint = Edge(canvas, spectrum, info, barWidth,
                barSpacing, cornerRadius, spectrumLength);
        }
    }

    private SKPaint GlowEffects(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        float cornerRadius,
        int spectrumLength)
    {
        var glowPaint = CreatePaint("glow");

        for (int i = 0; i < spectrumLength; i++)
        {
            float magnitude = spectrum[i];
            if (magnitude < HIGH_INTENSITY_THRESHOLD) continue;

            float x = i * (barWidth + barSpacing);
            var rect = GetBarRect(
                x,
                magnitude,
                barWidth,
                info.Height,
                MIN_BAR_HEIGHT);

            if (!IsAreaVisible(canvas, rect)) continue;

            glowPaint.Color = ApplyAlpha(
                glowPaint.Color,
                magnitude * GLOW_EFFECT_ALPHA);

            canvas.DrawRoundRect(
                rect,
                cornerRadius,
                cornerRadius,
                glowPaint);
        }

        return glowPaint;
    }

    private bool MainBars(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        SKPaint paint,
        float cornerRadius,
        int i)
    {
        float magnitude = spectrum[i];
        if (magnitude < MIN_MAGNITUDE_THRESHOLD) return false;

        float x = i * (barWidth + barSpacing);
        var rect = GetBarRect(
            x,
            magnitude,
            barWidth,
            info.Height,
            MIN_BAR_HEIGHT);

        if (!IsAreaVisible(canvas, rect)) return false;

        paint.Color = ApplyAlpha(
            paint.Color,
            magnitude,
            ALPHA_MULTIPLIER);

        canvas.DrawRoundRect(
            rect,
            cornerRadius,
            cornerRadius,
            paint);
        return true;
    }

    private SKPaint Edge(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        float cornerRadius,
        int spectrumLength)
    {
        var edgePaint = CreatePaint("edge");

        for (int i = 0; i < spectrumLength; i++)
        {
            float magnitude = spectrum[i];
            if (magnitude < MIN_MAGNITUDE_THRESHOLD) continue;

            float x = i * (barWidth + barSpacing);
            var rect = GetBarRect(
                x,
                magnitude,
                barWidth,
                info.Height,
                MIN_BAR_HEIGHT);

            if (!IsAreaVisible(canvas, rect)) continue;

            edgePaint.Color = InterpolateColor(
                SKColors.White,
                magnitude,
                0.3f,
                1f);

            canvas.DrawRoundRect(
                rect,
                cornerRadius,
                cornerRadius,
                edgePaint);
        }

        return edgePaint;
    }
}