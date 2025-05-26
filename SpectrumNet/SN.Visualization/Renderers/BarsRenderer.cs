#nullable enable

using static SpectrumNet.SN.Visualization.Renderers.BarsRenderer.Constants;
using static System.MathF;

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class BarsRenderer : EffectSpectrumRenderer
{
    private static readonly Lazy<BarsRenderer> _instance =
        new(() => new BarsRenderer());

    private BarsRenderer() { }

    public static BarsRenderer GetInstance() => _instance.Value;

    public static class Constants
    {
        public const float
            MAX_CORNER_RADIUS = 125f,
            DEFAULT_CORNER_RADIUS_FACTOR = 0.5f,
            MIN_BAR_HEIGHT = 1f,
            GLOW_EFFECT_ALPHA = 0.25f,
            ALPHA_MULTIPLIER = 1.5f,
            HIGH_INTENSITY_THRESHOLD = 0.6f;

        public const int
            BATCH_SIZE = 32,
            MAX_BARS_LOW = 200,
            MAX_BARS_MEDIUM = 100,
            MAX_BARS_HIGH = 100;

        public static readonly Dictionary<RenderQuality, QualitySettings> QualityPresets = new()
        {
            [RenderQuality.Low] = new(
                UseGlow: false,
                UseEdge: false,
                GlowRadius: 1.0f,
                GlowAlpha: GLOW_EFFECT_ALPHA * 0.5f,
                IntensityThreshold: HIGH_INTENSITY_THRESHOLD * 1.2f,
                AlphaMultiplier: ALPHA_MULTIPLIER * 0.8f,
                EdgeStrokeWidth: 0f,
                EdgeBlurRadius: 0f
            ),
            [RenderQuality.Medium] = new(
                UseGlow: true,
                UseEdge: true,
                GlowRadius: 2.0f,
                GlowAlpha: GLOW_EFFECT_ALPHA * 0.8f,
                IntensityThreshold: HIGH_INTENSITY_THRESHOLD * 1.05f,
                AlphaMultiplier: ALPHA_MULTIPLIER,
                EdgeStrokeWidth: 1.5f,
                EdgeBlurRadius: 1f
            ),
            [RenderQuality.High] = new(
                UseGlow: true,
                UseEdge: true,
                GlowRadius: 3.0f,
                GlowAlpha: GLOW_EFFECT_ALPHA,
                IntensityThreshold: HIGH_INTENSITY_THRESHOLD,
                AlphaMultiplier: ALPHA_MULTIPLIER * 1.2f,
                EdgeStrokeWidth: 2.5f,
                EdgeBlurRadius: 2f
            )
        };

        public record QualitySettings(
            bool UseGlow,
            bool UseEdge,
            float GlowRadius,
            float GlowAlpha,
            float IntensityThreshold,
            float AlphaMultiplier,
            float EdgeStrokeWidth,
            float EdgeBlurRadius
        );
    }

    private QualitySettings _currentSettings = QualityPresets[RenderQuality.Medium];

    protected override int GetMaxBarsForQuality() => Quality switch
    {
        RenderQuality.Low => MAX_BARS_LOW,
        RenderQuality.Medium => MAX_BARS_MEDIUM,
        RenderQuality.High => MAX_BARS_HIGH,
        _ => MAX_BARS_MEDIUM
    };

    protected override void OnInitialize()
    {
        base.OnInitialize();
        LogDebug("Initialized");
    }

    protected override void OnQualitySettingsApplied()
    {
        _currentSettings = QualityPresets[Quality];
        LogDebug($"Quality changed to {Quality}");
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
        SafeExecute(
            () => RenderBars(canvas, spectrum, info, barWidth, barSpacing, barCount, paint),
            "Error during rendering"
        );
    }

    private void RenderBars(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint basePaint)
    {
        float canvasHeight = info.Height;
        float cornerRadius = MathF.Min(
            barWidth * DEFAULT_CORNER_RADIUS_FACTOR, MAX_CORNER_RADIUS);

        int spectrumLength = Min(barCount, spectrum.Length);

        for (int i = 0; i < spectrumLength; i += Constants.BATCH_SIZE)
        {
            int batchEnd = Min(i + Constants.BATCH_SIZE, spectrumLength);
            RenderBatch(
                canvas, spectrum, i, batchEnd, basePaint, barWidth,
                barSpacing, canvasHeight, cornerRadius);
        }
    }

    private void RenderBatch(
        SKCanvas canvas,
        float[] spectrum,
        int start,
        int end,
        SKPaint basePaint,
        float barWidth,
        float barSpacing,
        float canvasHeight,
        float cornerRadius)
    {
        for (int i = start; i < end; i++)
        {
            float magnitude = spectrum[i];
            if (magnitude < MIN_MAGNITUDE_THRESHOLD) continue;

            float x = i * (barWidth + barSpacing);
            if (!IsRenderAreaVisible(canvas, x, 0, barWidth, canvasHeight)) continue;

            RenderSingleBar(
                canvas, x, magnitude, barWidth, canvasHeight, cornerRadius, basePaint);
        }
    }

    private void RenderSingleBar(
        SKCanvas canvas,
        float x,
        float magnitude,
        float barWidth,
        float canvasHeight,
        float cornerRadius,
        SKPaint basePaint)
    {
        float barHeight = MathF.Max(magnitude * canvasHeight, MIN_BAR_HEIGHT);
        float y = canvasHeight - barHeight;
        var rect = new SKRect(x, y, x + barWidth, y + barHeight);

        byte alpha = (byte)MathF.Min(
            magnitude * _currentSettings.AlphaMultiplier * 255f, 255f);

        if (UseAdvancedEffects && _currentSettings.UseGlow &&
            magnitude > _currentSettings.IntensityThreshold)
        {
            using var glowPaint = CreateEffectPaint(
                SKColors.White.WithAlpha((byte)(magnitude * 255f * _currentSettings.GlowAlpha)),
                SKPaintStyle.Fill,
                createBlur: true,
                blurRadius: _currentSettings.GlowRadius
            );
            canvas.DrawRoundRect(rect, cornerRadius, cornerRadius, glowPaint);
        }

        using var barPaint = CreateEffectPaint(
            basePaint.Color.WithAlpha(alpha), SKPaintStyle.Fill);
        canvas.DrawRoundRect(rect, cornerRadius, cornerRadius, barPaint);

        if (UseAdvancedEffects && _currentSettings.UseEdge &&
            _currentSettings.EdgeStrokeWidth > 0)
        {
            using var edgePaint = CreateEffectPaint(
                SKColors.White.WithAlpha(alpha),
                SKPaintStyle.Stroke,
                strokeWidth: _currentSettings.EdgeStrokeWidth,
                createBlur: _currentSettings.EdgeBlurRadius > 0,
                blurRadius: _currentSettings.EdgeBlurRadius
            );
            canvas.DrawRoundRect(rect, cornerRadius, cornerRadius, edgePaint);
        }
    }

    private SKPaint CreateEffectPaint(
        SKColor color,
        SKPaintStyle style,
        float strokeWidth = 0,
        bool createBlur = false,
        float blurRadius = 0)
    {
        var paint = GetPaint();
        paint.Color = color;
        paint.Style = style;
        paint.IsAntialias = UseAntiAlias;

        if (style == SKPaintStyle.Stroke)
        {
            paint.StrokeWidth = strokeWidth;
            paint.StrokeCap = SKStrokeCap.Round;
            paint.StrokeJoin = SKStrokeJoin.Round;
        }

        if (createBlur && blurRadius > 0)
        {
            paint.ImageFilter = SKImageFilter.CreateBlur(blurRadius, blurRadius);
        }

        return paint;
    }

    protected override void OnDispose()
    {
        base.OnDispose();
        LogDebug("Disposed");
    }
}