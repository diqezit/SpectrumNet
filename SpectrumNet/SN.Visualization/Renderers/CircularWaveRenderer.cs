#nullable enable

using static System.MathF;
using static SpectrumNet.SN.Visualization.Renderers.CircularWaveRenderer.Constants;

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class CircularWaveRenderer() : EffectSpectrumRenderer
{
    private static readonly Lazy<CircularWaveRenderer> _instance =
        new(() => new CircularWaveRenderer());

    public static CircularWaveRenderer GetInstance() => _instance.Value;

    public static class Constants
    {
        public const float
            ROTATION_SPEED = 0.5f,
            WAVE_SPEED = 2.0f,
            CENTER_RADIUS = 30f,
            MAX_RADIUS_FACTOR = 0.45f,
            MIN_STROKE = 1.5f,
            MAX_STROKE = 8f,
            WAVE_INFLUENCE = 1f,
            GLOW_THRESHOLD = 0.5f,
            GLOW_FACTOR = 0.7f,
            GLOW_WIDTH_FACTOR = 1.5f,
            ROTATION_INTENSITY_FACTOR = 0.3f,
            WAVE_PHASE_OFFSET = 0.1f,
            STROKE_CLAMP_FACTOR = 6f;

        public const int MAX_RINGS = 32;

        public static readonly Dictionary<RenderQuality, QualitySettings> QualityPresets = new()
        {
            [RenderQuality.Low] = new(
                PointsPerCircle: 16,
                UseGlow: false,
                GlowRadius: 0f),
            [RenderQuality.Medium] = new(
                PointsPerCircle: 64,
                UseGlow: true,
                GlowRadius: 3f),
            [RenderQuality.High] = new(
                PointsPerCircle: 128,
                UseGlow: true,
                GlowRadius: 8f)
        };

        public record QualitySettings(
            int PointsPerCircle,
            bool UseGlow,
            float GlowRadius);
    }

    private QualitySettings _currentSettings = QualityPresets[RenderQuality.Medium];
    private float _angle;
    private SKPoint[]? _circlePoints;
    private SKPoint _center;

    protected override void OnInitialize()
    {
        base.OnInitialize();
        CreateCirclePoints();
    }

    protected override void OnQualitySettingsApplied()
    {
        _currentSettings = QualityPresets[Quality];
        CreateCirclePoints();
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
        UpdateRotation(spectrum);
        _center = new SKPoint(info.Width * 0.5f, info.Height * 0.5f);

        int ringCount = Min(barCount, MAX_RINGS);
        float maxRadius = Min(info.Width, info.Height) * MAX_RADIUS_FACTOR;
        float ringStep = barWidth + barSpacing;

        var wavePaint = GetPaint();
        wavePaint.Color = paint.Color;
        wavePaint.IsAntialias = UseAntiAlias;
        wavePaint.Style = SKPaintStyle.Stroke;

        for (int i = ringCount - 1; i >= 0; i--)
        {
            RenderRing(canvas, spectrum, i, ringCount, ringStep, maxRadius, wavePaint);
        }

        ReturnPaint(wavePaint);
    }

    private void RenderRing(
        SKCanvas canvas,
        float[] spectrum,
        int index,
        int totalRings,
        float ringStep,
        float maxRadius,
        SKPaint paint)
    {
        float magnitude = GetRingMagnitude(spectrum, index, totalRings);
        if (magnitude < MIN_MAGNITUDE_THRESHOLD) return;

        float baseRadius = CENTER_RADIUS + index * ringStep;
        float waveOffset = Sin(GetAnimationTime() * WAVE_SPEED +
            index * WAVE_PHASE_OFFSET + _angle) *
            magnitude * ringStep * WAVE_INFLUENCE;
        float radius = baseRadius + waveOffset;

        if (radius <= 0 || radius > maxRadius) return;

        byte alpha = CalculateAlpha(magnitude * (1f - radius / maxRadius));
        float strokeWidth = Clamp(
            MIN_STROKE + magnitude * STROKE_CLAMP_FACTOR,
            MIN_STROKE,
            MAX_STROKE);

        paint.Color = paint.Color.WithAlpha(alpha);
        paint.StrokeWidth = strokeWidth;

        if (ShouldRenderGlow(magnitude))
            RenderGlowRing(canvas, radius, magnitude, paint);

        DrawCircle(canvas, radius, paint);
    }

    private void RenderGlowRing(
        SKCanvas canvas,
        float radius,
        float magnitude,
        SKPaint paint)
    {
        var glowPaint = GetPaint();
        glowPaint.Color = paint.Color.WithAlpha(
            (byte)(paint.Color.Alpha * GLOW_FACTOR));
        glowPaint.IsAntialias = paint.IsAntialias;
        glowPaint.Style = paint.Style;
        glowPaint.StrokeWidth = paint.StrokeWidth * GLOW_WIDTH_FACTOR;
        glowPaint.MaskFilter = SKMaskFilter.CreateBlur(
            SKBlurStyle.Normal,
            _currentSettings.GlowRadius * magnitude);

        DrawCircle(canvas, radius, glowPaint);
        ReturnPaint(glowPaint);
    }

    private void DrawCircle(SKCanvas canvas, float radius, SKPaint paint)
    {
        if (_circlePoints == null) return;

        RenderBatch(canvas, path =>
        {
            bool first = true;
            foreach (var point in _circlePoints)
            {
                float x = _center.X + point.X * radius;
                float y = _center.Y + point.Y * radius;

                if (first)
                {
                    path.MoveTo(x, y);
                    first = false;
                }
                else
                {
                    path.LineTo(x, y);
                }
            }
            path.Close();
        }, paint);
    }

    private void UpdateRotation(float[] spectrum)
    {
        float avgIntensity = spectrum.Length > 0
            ? GetAverageInRange(spectrum, 0, spectrum.Length)
            : 0f;

        _angle = (_angle + ROTATION_SPEED *
            (1f + avgIntensity * ROTATION_INTENSITY_FACTOR) *
            GetAnimationDeltaTime()) % MathF.Tau;
    }

    private float GetRingMagnitude(
        float[] spectrum,
        int ringIndex,
        int ringCount)
    {
        int start = ringIndex * spectrum.Length / ringCount;
        int end = Min((ringIndex + 1) * spectrum.Length / ringCount, spectrum.Length);

        return start >= end
            ? 0f
            : GetAverageInRange(spectrum, start, end);
    }

    private void CreateCirclePoints()
    {
        int points = _currentSettings.PointsPerCircle;
        _circlePoints = CreateCirclePoints(points, 1f, SKPoint.Empty);
    }

    private bool ShouldRenderGlow(float magnitude) =>
        UseAdvancedEffects &&
        _currentSettings.UseGlow &&
        magnitude > GLOW_THRESHOLD;

    protected override void OnDispose()
    {
        _circlePoints = null;
        base.OnDispose();
    }
}