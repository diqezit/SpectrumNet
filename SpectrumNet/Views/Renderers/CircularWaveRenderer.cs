#nullable enable

using static SpectrumNet.Views.Renderers.CircularWaveRenderer.Constants;
using static SpectrumNet.Views.Renderers.CircularWaveRenderer.Constants.Quality;

namespace SpectrumNet.Views.Renderers;

public sealed class CircularWaveRenderer : EffectSpectrumRenderer
{
    private static readonly Lazy<CircularWaveRenderer> _instance = new(() => new CircularWaveRenderer());
    private const string LOG_PREFIX = "CircularWaveRenderer";

    private CircularWaveRenderer() { }

    public static CircularWaveRenderer GetInstance() => _instance.Value;

    public record Constants
    {
        public const string LOG_PREFIX = "CircularWaveRenderer";

        public const float
            TIME_STEP = 0.016f,
            ROTATION_SPEED = 0.5f,
            SPECTRUM_ROTATION_INFLUENCE = 0.3f,
            WAVE_SPEED = 2.0f,
            PHASE_OFFSET = 0.1f,
            CENTER_CIRCLE_RADIUS = 30.0f,
            MIN_RADIUS_INCREMENT = 15.0f,
            MAX_RADIUS_INCREMENT = 40.0f,
            MIN_STROKE_WIDTH = 1.5f,
            MAX_STROKE_WIDTH = 8.0f,
            STROKE_WIDTH_FACTOR = 6.0f,
            ALPHA_MULTIPLIER = 255.0f,
            MIN_ALPHA_FACTOR = 0.3f,
            WAVE_RADIUS_INFLUENCE = 1.0f,
            MIN_MAGNITUDE_THRESHOLD_WAVE = 0.01f,
            CENTER_PROPORTION = 0.5f,
            MAX_RENDER_AREA_PROPORTION = 0.45f,
            BASE_ROTATION_FACTOR = 1f,
            ADVANCED_EFFECTS_THRESHOLD = 0.5f,
            GLOW_ALPHA_MULTIPLIER = 0.7f,
            GLOW_STROKE_WIDTH_MULTIPLIER = 1.5f;

        public const int
            MAX_RING_COUNT = 32,
            POINTS_PER_CIRCLE_LOW = 16,
            POINTS_PER_CIRCLE_MEDIUM = 64,
            POINTS_PER_CIRCLE_HIGH = 128;

        public const byte MAX_ALPHA_BYTE = 255;

        public static class Quality
        {
            public const int
                LOW_POINTS_PER_CIRCLE = POINTS_PER_CIRCLE_LOW,
                MEDIUM_POINTS_PER_CIRCLE = POINTS_PER_CIRCLE_MEDIUM,
                HIGH_POINTS_PER_CIRCLE = POINTS_PER_CIRCLE_HIGH;

            public const float
                LOW_GLOW_RADIUS = 0.5f,
                MEDIUM_GLOW_RADIUS = 3.0f,
                HIGH_GLOW_RADIUS = 8.0f;

            public const bool
                LOW_USE_ADVANCED_EFFECTS = false,
                LOW_USE_ANTI_ALIAS = false,
                MEDIUM_USE_ADVANCED_EFFECTS = true,
                MEDIUM_USE_ANTI_ALIAS = true,
                HIGH_USE_ADVANCED_EFFECTS = true,
                HIGH_USE_ANTI_ALIAS = true;
        }
    }

    private float _angle;
    private float _rotationSpeed = ROTATION_SPEED;
    private float[]? _ringMagnitudes;
    private SKPoint[]? _circlePoints;
    private SKPoint _center;
    private int _pointsPerCircle;
    private float _glowRadius;
    private bool _useGlowEffect;

    protected override void OnInitialize()
    {
        base.OnInitialize();
        CreateCirclePointsCache();
        Log(LogLevel.Debug, LOG_PREFIX, "Initialized");
    }

    protected override void OnConfigurationChanged()
    {
        Log(LogLevel.Information, LOG_PREFIX,
            $"Configuration changed. New Quality: {Quality}, AntiAlias: {_useAntiAlias}, " +
            $"AdvancedEffects: {_useAdvancedEffects}, PointsPerCircle: {_pointsPerCircle}, " +
            $"GlowRadius: {_glowRadius}");
    }

    protected override void OnQualitySettingsApplied()
    {
        switch (Quality)
        {
            case RenderQuality.Low:
                LowQualitySettings();
                break;
            case RenderQuality.Medium:
                MediumQualitySettings();
                break;
            case RenderQuality.High:
                HighQualitySettings();
                break;
        }

        CreateCirclePointsCache();

        Log(LogLevel.Information, LOG_PREFIX,
            $"Quality settings applied: {Quality}, AntiAlias: {_useAntiAlias}, " +
            $"AdvancedEffects: {_useAdvancedEffects}, PointsPerCircle: {_pointsPerCircle}, " +
            $"GlowRadius: {_glowRadius}");
    }

    private void HighQualitySettings()
    {
        _useAntiAlias = HIGH_USE_ANTI_ALIAS;
        _useAdvancedEffects = HIGH_USE_ADVANCED_EFFECTS;
        _pointsPerCircle = HIGH_POINTS_PER_CIRCLE;
        _glowRadius = HIGH_GLOW_RADIUS;
        _useGlowEffect = true;
    }

    private void MediumQualitySettings()
    {
        _useAntiAlias = MEDIUM_USE_ANTI_ALIAS;
        _useAdvancedEffects = MEDIUM_USE_ADVANCED_EFFECTS;
        _pointsPerCircle = MEDIUM_POINTS_PER_CIRCLE;
        _glowRadius = MEDIUM_GLOW_RADIUS;
        _useGlowEffect = true;
    }

    private void LowQualitySettings()
    {
        _useAntiAlias = LOW_USE_ANTI_ALIAS;
        _useAdvancedEffects = LOW_USE_ADVANCED_EFFECTS;
        _pointsPerCircle = LOW_POINTS_PER_CIRCLE;
        _glowRadius = LOW_GLOW_RADIUS;
        _useGlowEffect = false;
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
        ExecuteSafely(
            () =>
            {
                UpdateState(spectrum, info, barCount);
                RenderFrame(canvas, info, paint, barWidth, barSpacing);
            },
            nameof(RenderEffect),
            "Error during rendering"
        );
    }

    private void UpdateState(
        float[] spectrum,
        SKImageInfo info,
        int barCount)
    {
        UpdateRotation(spectrum);
        _center = new SKPoint(info.Width * CENTER_PROPORTION, info.Height * CENTER_PROPORTION);
        int ringCount = Min(barCount, MAX_RING_COUNT);
        PrepareRingMagnitudes(spectrum, ringCount);
    }

    private void RenderFrame(
        SKCanvas canvas,
        SKImageInfo info,
        SKPaint basePaint,
        float barWidth,
        float barSpacing)
    {
        if (_ringMagnitudes == null || _circlePoints == null)
        {
            Log(LogLevel.Error, LOG_PREFIX, "RenderFrame called with null state (magnitudes or points)");
            return;
        }

        float smallerDimension = Min(info.Width, info.Height);
        float maxPossibleRadius = CalculateMaxPossibleRadius(
            _ringMagnitudes.Length,
            barWidth,
            barSpacing,
            smallerDimension);

        using var mainPaint = _paintPool.Get();
        mainPaint.Color = basePaint.Color;
        mainPaint.IsAntialias = _useAntiAlias;
        mainPaint.Style = SKPaintStyle.Stroke;

        RenderRings(canvas, info, mainPaint, barWidth, barSpacing, maxPossibleRadius);
    }

    private void RenderRings(
        SKCanvas canvas,
        SKImageInfo info,
        SKPaint mainPaint,
        float barWidth,
        float barSpacing,
        float maxPossibleRadius)
    {
        if (_ringMagnitudes == null) return;

        for (int i = _ringMagnitudes.Length - 1; i >= 0; i--)
        {
            float magnitude = _ringMagnitudes[i];
            if (magnitude < MIN_MAGNITUDE_THRESHOLD_WAVE) continue;

            if (TryCalculateRingVisualProperties(
                    i,
                    magnitude,
                    barWidth,
                    barSpacing,
                    maxPossibleRadius,
                    info.Width,
                    info.Height,
                    out float radius,
                    out byte alpha,
                    out float strokeWidth))
            {
                ApplyRingVisualProperties(mainPaint, alpha, strokeWidth);
                RenderSingleRingVisuals(canvas, radius, magnitude, mainPaint);
            }
        }
    }

    private void UpdateRotation(float[] spectrum)
    {
        if (spectrum.Length == 0) return;

        float avgIntensity = CalculateAverageIntensity(spectrum);

        _rotationSpeed = ROTATION_SPEED * (BASE_ROTATION_FACTOR + avgIntensity * SPECTRUM_ROTATION_INFLUENCE);
        float deltaTime = Max(0, (float)(Now - _lastUpdateTime).TotalSeconds);
        _angle = (float)((_angle + _rotationSpeed * deltaTime) % Tau);

        if (_angle < 0) _angle += MathF.Tau;
    }

    private static float CalculateAverageIntensity(float[] spectrum)
    {
        if (spectrum.Length == 0) return 0f;

        float sum = 0f;
        for (int i = 0; i < spectrum.Length; i++)
        {
            sum += spectrum[i];
        }
        return sum / spectrum.Length;
    }

    private void PrepareRingMagnitudes(float[] spectrum, int ringCount)
    {
        if (_ringMagnitudes == null || _ringMagnitudes.Length != ringCount)
        {
            _ringMagnitudes = new float[ringCount];
        }

        int spectrumLength = spectrum.Length;

        for (int i = 0; i < ringCount; i++)
        {
            _ringMagnitudes[i] = CalculateRingMagnitude(spectrum, i, ringCount, spectrumLength);
        }
    }

    private static float CalculateRingMagnitude(
        float[] spectrum,
        int ringIndex,
        int ringCount,
        int spectrumLength)
    {
        int startIdx = ringIndex * spectrumLength / ringCount;
        int endIdx = (ringIndex + 1) * spectrumLength / ringCount;
        endIdx = Min(endIdx, spectrumLength);

        float sum = 0f;
        int count = endIdx - startIdx;
        if (count > 0)
        {
            for (int j = startIdx; j < endIdx; j++)
            {
                sum += spectrum[j];
            }
            return sum / count;
        }
        else
        {
            return 0f;
        }
    }

    private void CreateCirclePointsCache()
    {
        ExecuteSafely(
            () =>
            {
                if (_circlePoints != null
                    && _circlePoints.Length == _pointsPerCircle)
                    return;

                _circlePoints = new SKPoint[_pointsPerCircle];
                float angleStep = MathF.Tau / _pointsPerCircle;

                for (int i = 0; i < _pointsPerCircle; i++)
                {
                    float angle = i * angleStep;
                    _circlePoints[i] = new SKPoint(MathF.Cos(angle), MathF.Sin(angle));
                }
                Log(LogLevel.Debug,
                    LOG_PREFIX,
                    $"Circle points cache created with {_pointsPerCircle} points.");
            },
            nameof(CreateCirclePointsCache),
            "Failed to create circle points cache"
        );
    }

    private static float CalculateMaxPossibleRadius(
        int ringCount,
        float barWidth,
        float barSpacing,
        float smallerDimension)
    {
        float maxBaseRadius = CENTER_CIRCLE_RADIUS + (ringCount - 1) * (barWidth + barSpacing);
        float maxWaveOffset = WAVE_RADIUS_INFLUENCE * (barWidth + barSpacing);
        float estimatedMaxRadius = maxBaseRadius + maxWaveOffset;

        return Min(estimatedMaxRadius, smallerDimension * MAX_RENDER_AREA_PROPORTION);
    }

    private bool TryCalculateRingVisualProperties(
        int index,
        float magnitude,
        float barWidth,
        float barSpacing,
        float maxPossibleRadius,
        int infoWidth,
        int infoHeight,
        out float radius,
        out byte alpha,
        out float strokeWidth)
    {
        radius = CalculateRingRadius(index, magnitude, barWidth, barSpacing);

        if (!IsRingInBounds(radius, _center, infoWidth, infoHeight))
        {
            alpha = 0;
            strokeWidth = 0;
            return false;
        }

        alpha = CalculateRingAlpha(magnitude, radius, maxPossibleRadius);
        strokeWidth = CalculateRingStrokeWidth(barWidth, magnitude);

        return true;
    }

    private float CalculateRingRadius(
        int ringIndex,
        float magnitude,
        float barWidth,
        float barSpacing)
    {
        float baseRadius = CalculateBaseRingRadius(ringIndex, barWidth, barSpacing);
        float waveOffset = CalculateRingWaveOffset(ringIndex, magnitude, barWidth + barSpacing);
        return baseRadius + waveOffset;
    }

    private static float CalculateBaseRingRadius(
        int ringIndex,
        float baseRingThickness,
        float ringSpacing) =>
        CENTER_CIRCLE_RADIUS + ringIndex * (baseRingThickness + ringSpacing);

    private float CalculateRingWaveOffset(
        int ringIndex,
        float magnitude,
        float ringStepSize) =>
        MathF.Sin(_time * WAVE_SPEED + ringIndex * PHASE_OFFSET + _angle)
            * magnitude
            * ringStepSize
            * WAVE_RADIUS_INFLUENCE;

    private static byte CalculateRingAlpha(
        float magnitude,
        float currentRingRadius,
        float maxPossibleRadius)
    {
        float distanceFactor = BASE_ROTATION_FACTOR - currentRingRadius / maxPossibleRadius;
        distanceFactor = Max(0, Min(BASE_ROTATION_FACTOR, distanceFactor));
        float alphaFactor = MIN_ALPHA_FACTOR + (BASE_ROTATION_FACTOR - MIN_ALPHA_FACTOR) * distanceFactor;
        return (byte)Min(magnitude * ALPHA_MULTIPLIER * alphaFactor, MAX_ALPHA_BYTE);
    }

    private static float CalculateRingStrokeWidth(float baseRingThickness, float magnitude)
    {
        float strokeWidth = baseRingThickness + magnitude * STROKE_WIDTH_FACTOR;
        return Clamp(strokeWidth, MIN_STROKE_WIDTH, MAX_STROKE_WIDTH);
    }

    private static bool IsRingInBounds(
        float radius,
        SKPoint center,
        int infoWidth,
        int infoHeight) =>
        radius > 0 &&
        center.X + radius >= 0 && center.X - radius <= infoWidth &&
        center.Y + radius >= 0 && center.Y - radius <= infoHeight;

    private static void ApplyRingVisualProperties(
        SKPaint paint,
        byte alpha,
        float strokeWidth)
    {
        paint.StrokeWidth = strokeWidth;
        paint.Color = paint.Color.WithAlpha(alpha);
    }

    private void RenderSingleRingVisuals(
        SKCanvas canvas,
        float radius,
        float magnitude,
        SKPaint mainPaint)
    {
        if (_useAdvancedEffects
            && _useGlowEffect
            && magnitude > ADVANCED_EFFECTS_THRESHOLD)
        {
            RenderRingWithGlow(canvas, radius, magnitude, mainPaint);
        }
        else
        {
            RenderRing(canvas, radius, mainPaint);
        }
    }

    [MethodImpl(AggressiveInlining)]
    private void RenderRing(
        SKCanvas canvas,
        float radius,
        SKPaint paint) =>
        DrawRingPath(canvas, radius, paint);

    [MethodImpl(AggressiveInlining)]
    private void RenderRingWithGlow(
        SKCanvas canvas,
        float radius,
        float magnitude,
        SKPaint paint)
    {
        using var glowPaint = _paintPool.Get();
        glowPaint.Color = paint.Color.WithAlpha((byte)Min(paint.Color.Alpha * GLOW_ALPHA_MULTIPLIER,
            MAX_ALPHA_BYTE));
        glowPaint.IsAntialias = paint.IsAntialias;
        glowPaint.Style = paint.Style;
        glowPaint.StrokeWidth = paint.StrokeWidth * GLOW_STROKE_WIDTH_MULTIPLIER;
        glowPaint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, _glowRadius * magnitude);

        DrawRingPath(canvas, radius, glowPaint);
        DrawRingPath(canvas, radius, paint);
    }

    [MethodImpl(AggressiveInlining)]
    private void DrawRingPath(
        SKCanvas canvas,
        float radius,
        SKPaint paint)
    {
        if (_circlePoints == null)
        {
            Log(LogLevel.Error, LOG_PREFIX, "DrawRingPath called with null _circlePoints");
            return;
        }

        using var path = _pathPool.Get();
        AppendRingPathPoints(path, radius, _center, _circlePoints);
        path.Close();
        canvas.DrawPath(path, paint);
    }

    private static void AppendRingPathPoints(
        SKPath path,
        float radius,
        SKPoint center,
        SKPoint[] circlePoints)
    {
        bool firstPoint = true;
        foreach (var point in circlePoints)
        {
            float x = center.X + point.X * radius;
            float y = center.Y + point.Y * radius;

            if (firstPoint)
            {
                path.MoveTo(x, y);
                firstPoint = false;
            }
            else
            {
                path.LineTo(x, y);
            }
        }
    }

    protected override void OnInvalidateCachedResources()
    {
        base.OnInvalidateCachedResources();
        _ringMagnitudes = null;
        _circlePoints = null;
        Log(LogLevel.Debug, LOG_PREFIX, "Cached resources invalidated");
    }

    protected override void OnDispose()
    {
        _ringMagnitudes = null;
        _circlePoints = null;
        base.OnDispose();
        Log(LogLevel.Debug, LOG_PREFIX, "Disposed");
    }
}