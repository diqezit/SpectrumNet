#nullable enable

using static System.MathF;
using static SpectrumNet.SN.Visualization.Renderers.CircularBarsRenderer.Constants;

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class CircularBarsRenderer() : EffectSpectrumRenderer
{
    private static readonly Lazy<CircularBarsRenderer> _instance =
        new(() => new CircularBarsRenderer());

    public static CircularBarsRenderer GetInstance() => _instance.Value;

    public static class Constants
    {
        public const float
            RADIUS_PROPORTION = 0.8f,
            INNER_RADIUS_FACTOR = 0.9f,
            BAR_SPACING_FACTOR = 0.7f,
            MIN_STROKE_WIDTH = 2f,
            SPECTRUM_MULTIPLIER = 0.5f,
            CENTER_PROPORTION = 0.5f,
            GLOW_THRESHOLD = 0.6f,
            HIGHLIGHT_POSITION = 0.7f,
            HIGHLIGHT_THRESHOLD = 0.4f,
            INNER_CIRCLE_WIDTH_FACTOR = 0.5f,
            GLOW_WIDTH_FACTOR = 1.2f,
            HIGHLIGHT_WIDTH_FACTOR = 0.6f;

        public const byte INNER_CIRCLE_ALPHA = 80;

        public record QualitySettings(
            bool UseGlow,
            bool UseHighlight,
            float GlowRadius,
            float GlowIntensity,
            float HighlightIntensity,
            int MaxBars);

        public static readonly Dictionary<RenderQuality, QualitySettings> QualityPresets = new()
        {
            [RenderQuality.Low] = new(false, false, 1.5f, 0.2f, 0.3f, 64),
            [RenderQuality.Medium] = new(true, true, 3.0f, 0.4f, 0.5f, 128),
            [RenderQuality.High] = new(true, true, 6.0f, 0.6f, 0.7f, 256)
        };
    }

    private QualitySettings _currentSettings = QualityPresets[RenderQuality.Medium];
    private Vector2[]? _barVectors;

    protected override void OnInitialize() =>
        UpdatePaintConfigs();

    protected override void OnQualitySettingsApplied() =>
        UpdatePaintConfigs();

    private void UpdatePaintConfigs()
    {
        _currentSettings = QualityPresets[Quality];

        RegisterPaintConfig("inner",
            CreateStrokePaintConfig(SKColors.White, MIN_STROKE_WIDTH));

        RegisterPaintConfig("main",
            CreateStrokePaintConfig(SKColors.White, MIN_STROKE_WIDTH, SKStrokeCap.Round));

        RegisterPaintConfig("glow",
            CreateGlowPaintConfig(SKColors.White, _currentSettings.GlowRadius));

        RegisterPaintConfig("highlight",
            CreateStrokePaintConfig(SKColors.White, MIN_STROKE_WIDTH));
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
        var center = new SKPoint(info.Width * CENTER_PROPORTION, info.Height * CENTER_PROPORTION);
        float mainRadius = MathF.Min(center.X, center.Y) * RADIUS_PROPORTION;
        int effectiveBarCount = Min(barCount, _currentSettings.MaxBars);
        float adjustedBarWidth = CalculateOptimalBarWidth(effectiveBarCount, mainRadius);

        EnsureBarVectors(effectiveBarCount);

        RenderInnerCircle(canvas, center, mainRadius, adjustedBarWidth, paint.Color);

        if (UseAdvancedEffects && _currentSettings.UseGlow)
        {
            RenderGlowEffect(canvas, spectrum, center, mainRadius, adjustedBarWidth, paint.Color, effectiveBarCount);
        }

        RenderMainBars(canvas, spectrum, center, mainRadius, adjustedBarWidth, paint.Color, effectiveBarCount);

        if (UseAdvancedEffects && _currentSettings.UseHighlight)
        {
            RenderHighlights(canvas, spectrum, center, mainRadius, adjustedBarWidth, effectiveBarCount);
        }
    }

    private void RenderInnerCircle(SKCanvas canvas, SKPoint center, float radius, float barWidth, SKColor baseColor)
    {
        using var paint = CreatePaint("inner");
        paint.Color = ApplyAlpha(baseColor, INNER_CIRCLE_ALPHA / 255f);
        paint.StrokeWidth = barWidth * INNER_CIRCLE_WIDTH_FACTOR;

        canvas.DrawCircle(center.X, center.Y, radius * INNER_RADIUS_FACTOR, paint);
    }

    private void RenderGlowEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKPoint center,
        float mainRadius,
        float barWidth,
        SKColor baseColor,
        int barCount)
    {
        using var glowPaint = CreatePaint("glow");
        glowPaint.Color = ApplyAlpha(baseColor, _currentSettings.GlowIntensity);
        glowPaint.StrokeWidth = barWidth * GLOW_WIDTH_FACTOR;

        RenderBatch(canvas, path =>
        {
            for (int i = 0; i < barCount; i++)
            {
                if (spectrum[i] <= GLOW_THRESHOLD) continue;

                float radius = mainRadius + spectrum[i] * mainRadius * SPECTRUM_MULTIPLIER;
                AddBarToPath(path, i, center, mainRadius, radius);
            }
        }, glowPaint);
    }

    private void RenderMainBars(
        SKCanvas canvas,
        float[] spectrum,
        SKPoint center,
        float mainRadius,
        float barWidth,
        SKColor baseColor,
        int barCount)
    {
        using var paint = CreatePaint("main");
        paint.Color = baseColor;
        paint.StrokeWidth = barWidth;

        RenderBatch(canvas, path =>
        {
            for (int i = 0; i < barCount; i++)
            {
                if (spectrum[i] < MIN_MAGNITUDE_THRESHOLD) continue;

                float radius = mainRadius + spectrum[i] * mainRadius * SPECTRUM_MULTIPLIER;
                AddBarToPath(path, i, center, mainRadius, radius);
            }
        }, paint);
    }

    private void RenderHighlights(
        SKCanvas canvas,
        float[] spectrum,
        SKPoint center,
        float mainRadius,
        float barWidth,
        int barCount)
    {
        using var paint = CreatePaint("highlight");
        paint.Color = InterpolateColor(SKColors.White, _currentSettings.HighlightIntensity);
        paint.StrokeWidth = barWidth * HIGHLIGHT_WIDTH_FACTOR;

        RenderBatch(canvas, path =>
        {
            for (int i = 0; i < barCount; i++)
            {
                if (spectrum[i] <= HIGHLIGHT_THRESHOLD) continue;

                float radius = mainRadius + spectrum[i] * mainRadius * SPECTRUM_MULTIPLIER;
                float innerPoint = mainRadius + (radius - mainRadius) * HIGHLIGHT_POSITION;
                AddBarToPath(path, i, center, innerPoint, radius);
            }
        }, paint);
    }

    private void EnsureBarVectors(int barCount)
    {
        if (_barVectors?.Length != barCount)
        {
            _barVectors = CreateCircleVectors(barCount);
        }
    }

    private static float CalculateOptimalBarWidth(int barCount, float radius)
    {
        if (barCount <= 0) return MIN_STROKE_WIDTH;

        float circumference = 2 * MathF.PI * radius;
        float maxWidth = circumference / barCount * BAR_SPACING_FACTOR;
        return MathF.Max(MathF.Min(maxWidth, 20f), MIN_STROKE_WIDTH);
    }

    private void AddBarToPath(SKPath path, int index, SKPoint center, float innerRadius, float outerRadius)
    {
        if (_barVectors == null || index >= _barVectors.Length) return;

        var vector = _barVectors[index];
        var innerPoint = new SKPoint(
            center.X + innerRadius * vector.X,
            center.Y + innerRadius * vector.Y);
        var outerPoint = new SKPoint(
            center.X + outerRadius * vector.X,
            center.Y + outerRadius * vector.Y);

        path.MoveTo(innerPoint);
        path.LineTo(outerPoint);
    }

    protected override void OnDispose()
    {
        _barVectors = null;
        base.OnDispose();
    }
}