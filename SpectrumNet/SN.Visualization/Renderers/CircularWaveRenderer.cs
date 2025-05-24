#nullable enable

using static System.MathF;
using static SpectrumNet.SN.Visualization.Renderers.CircularWaveRenderer.Constants;

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class CircularWaveRenderer : EffectSpectrumRenderer
{
    private const string LogPrefix = nameof(CircularWaveRenderer);

    private static readonly Lazy<CircularWaveRenderer> _instance =
        new(() => new CircularWaveRenderer());

    private CircularWaveRenderer() { }

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
            GLOW_THRESHOLD = 0.5f;

        public const int MAX_RINGS = 32;

        public static readonly Dictionary<RenderQuality, QualitySettings> QualityPresets = new()
        {
            [RenderQuality.Low] = new(
                PointsPerCircle: 16,
                UseGlow: false,
                GlowRadius: 0f
            ),
            [RenderQuality.Medium] = new(
                PointsPerCircle: 64,
                UseGlow: true,
                GlowRadius: 3f
            ),
            [RenderQuality.High] = new(
                PointsPerCircle: 128,
                UseGlow: true,
                GlowRadius: 8f
            )
        };

        public record QualitySettings(
            int PointsPerCircle,
            bool UseGlow,
            float GlowRadius
        );
    }

    private QualitySettings _currentSettings = QualityPresets[RenderQuality.Medium];
    private float _angle;
    private SKPoint[]? _circlePoints;
    private SKPoint _center;

    protected override void OnInitialize()
    {
        base.OnInitialize();
        CreateCirclePoints();
        _logger.Log(LogLevel.Debug, LogPrefix, "Initialized");
    }

    protected override void OnQualitySettingsApplied()
    {
        _currentSettings = QualityPresets[Quality];
        CreateCirclePoints();
        _logger.Log(LogLevel.Debug, LogPrefix, $"Quality changed to {Quality}");
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
        _logger.Safe(
            () => RenderWaves(
                canvas,
                spectrum,
                info,
                barWidth,
                barSpacing,
                barCount,
                paint),
            LogPrefix,
            "Error during rendering"
        );
    }

    private void RenderWaves(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint basePaint)
    {
        UpdateRotation(spectrum);
        _center = new SKPoint(info.Width * 0.5f, info.Height * 0.5f);

        int ringCount = Min(barCount, MAX_RINGS);
        float maxRadius = Min(info.Width, info.Height) * MAX_RADIUS_FACTOR;
        float ringStep = barWidth + barSpacing;

        using var paint = _resourceManager.GetPaint();
        paint.Color = basePaint.Color;
        paint.IsAntialias = UseAntiAlias;
        paint.Style = SKPaintStyle.Stroke;

        for (int i = ringCount - 1; i >= 0; i--)
        {
            RenderRing(
                canvas,
                spectrum,
                i,
                ringCount,
                ringStep,
                maxRadius,
                paint);
        }
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
        float waveOffset = Sin(_animationTimer.Time * WAVE_SPEED + index * 0.1f + _angle) *
                          magnitude * ringStep * WAVE_INFLUENCE;
        float radius = baseRadius + waveOffset;

        if (radius <= 0 || radius > maxRadius) return;

        byte alpha = (byte)(magnitude * 255f * (1f - radius / maxRadius));
        float strokeWidth = Clamp(MIN_STROKE + magnitude * 6f, MIN_STROKE, MAX_STROKE);

        paint.Color = paint.Color.WithAlpha(alpha);
        paint.StrokeWidth = strokeWidth;

        if (UseAdvancedEffects && _currentSettings.UseGlow && magnitude > GLOW_THRESHOLD)
        {
            RenderGlowRing(canvas, radius, magnitude, paint);
        }

        DrawCircle(canvas, radius, paint);
    }

    private void RenderGlowRing(
        SKCanvas canvas,
        float radius,
        float magnitude,
        SKPaint paint)
    {
        using var glowPaint = _resourceManager.GetPaint();
        glowPaint.Color = paint.Color.WithAlpha((byte)(paint.Color.Alpha * 0.7f));
        glowPaint.IsAntialias = paint.IsAntialias;
        glowPaint.Style = paint.Style;
        glowPaint.StrokeWidth = paint.StrokeWidth * 1.5f;
        glowPaint.MaskFilter = SKMaskFilter.CreateBlur(
            SKBlurStyle.Normal,
            _currentSettings.GlowRadius * magnitude);

        DrawCircle(canvas, radius, glowPaint);
    }

    private void DrawCircle(
        SKCanvas canvas,
        float radius,
        SKPaint paint)
    {
        if (_circlePoints == null) return;

        var path = _resourceManager.GetPath();
        try
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
            canvas.DrawPath(path, paint);
        }
        finally
        {
            _resourceManager.ReturnPath(path);
        }
    }

    private void UpdateRotation(float[] spectrum)
    {
        float avgIntensity = spectrum.Length > 0 ? spectrum.Average() : 0f;
        _angle = (_angle + ROTATION_SPEED * (1f + avgIntensity * 0.3f) * _animationTimer.DeltaTime) % MathF.Tau;
    }

    private static float GetRingMagnitude(
        float[] spectrum,
        int ringIndex,
        int ringCount)
    {
        int start = ringIndex * spectrum.Length / ringCount;
        int end = Min((ringIndex + 1) * spectrum.Length / ringCount, spectrum.Length);

        if (start >= end) return 0f;

        float sum = 0f;
        for (int i = start; i < end; i++)
        {
            sum += spectrum[i];
        }
        return sum / (end - start);
    }

    private void CreateCirclePoints()
    {
        int points = _currentSettings.PointsPerCircle;
        _circlePoints = new SKPoint[points];
        float angleStep = MathF.Tau / points;

        for (int i = 0; i < points; i++)
        {
            float angle = i * angleStep;
            _circlePoints[i] = new SKPoint(Cos(angle), Sin(angle));
        }
    }

    protected override void OnDispose()
    {
        _circlePoints = null;
        base.OnDispose();
        _logger.Log(LogLevel.Debug, LogPrefix, "Disposed");
    }
}