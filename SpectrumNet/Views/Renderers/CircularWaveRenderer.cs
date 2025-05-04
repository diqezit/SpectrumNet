#nullable enable

using static SpectrumNet.Views.Renderers.CircularWaveRenderer.Constants;
using static System.MathF;

namespace SpectrumNet.Views.Renderers;

public sealed class CircularWaveRenderer : EffectSpectrumRenderer
{
    public record Constants
    {
        public const string
            LOG_PREFIX = "CircularWaveRenderer";

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
            GLOW_RADIUS_LOW = 1.5f,
            GLOW_RADIUS_MEDIUM = 3.0f,
            GLOW_RADIUS_HIGH = 5.0f;


        public const int
            MAX_RING_COUNT = 32,
            POINTS_PER_CIRCLE_LOW = 32,
            POINTS_PER_CIRCLE_MEDIUM = 64,
            POINTS_PER_CIRCLE_HIGH = 128;
    }

    private static readonly Lazy<CircularWaveRenderer> _instance = new(() => new CircularWaveRenderer());

    private float _angle;
    private float _rotationSpeed = ROTATION_SPEED;

    private float[]? _ringMagnitudes;
    private SKPoint[]? _circlePoints;
    private SKPoint _center;

    private int _pointsPerCircle = POINTS_PER_CIRCLE_MEDIUM;
    private float _glowRadius = GLOW_RADIUS_MEDIUM;

    private CircularWaveRenderer() { }

    public static CircularWaveRenderer GetInstance() => _instance.Value;

    protected override void OnInitialize()
    {
        Safe(() =>
        {
            _angle = 0f;
            CreateCirclePointsCache();
        }, new ErrorHandlingOptions
        {
            Source = $"{LOG_PREFIX}.OnInitialize",
            ErrorMessage = "Failed during CircularWaveRenderer initialization"
        });
    }

    protected override void OnQualitySettingsApplied()
    {
        Safe(() =>
        {
            base.OnQualitySettingsApplied();
            switch (base.Quality)
            {
                case RenderQuality.Low:
                    _pointsPerCircle = POINTS_PER_CIRCLE_LOW;
                    _glowRadius = GLOW_RADIUS_LOW;
                    break;

                case RenderQuality.Medium:
                    _pointsPerCircle = POINTS_PER_CIRCLE_MEDIUM;
                    _glowRadius = GLOW_RADIUS_MEDIUM;
                    break;

                case RenderQuality.High:
                    _pointsPerCircle = POINTS_PER_CIRCLE_HIGH;
                    _glowRadius = GLOW_RADIUS_HIGH;
                    break;
            }

            CreateCirclePointsCache();
            Log(LogLevel.Debug, LOG_PREFIX, $"Quality set to {base.Quality}");
        }, new ErrorHandlingOptions
        {
            Source = $"{LOG_PREFIX}.OnQualitySettingsApplied",
            ErrorMessage = "Failed to apply circular wave specific quality settings"
        });
    }

    private void CreateCirclePointsCache()
    {
        Safe(() =>
        {
            _circlePoints = new SKPoint[_pointsPerCircle];
            float angleStep = 2 * MathF.PI / _pointsPerCircle;

            for (int i = 0; i < _pointsPerCircle; i++)
            {
                float angle = i * angleStep;
                _circlePoints[i] = new SKPoint(Cos(angle), Sin(angle));
            }
        }, new ErrorHandlingOptions
        {
            Source = $"{LOG_PREFIX}.CreateCirclePointsCache",
            ErrorMessage = "Failed to create circle points cache"
        });
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

        int ringCount = Math.Min(barCount, MAX_RING_COUNT);
        PrepareRingMagnitudes(spectrum, ringCount);

        RenderCircularWaves(canvas, info, paint, ringCount);
    }

    private void UpdateRotation(float[] spectrum)
    {
        if (spectrum.Length == 0) return;

        float avgIntensity = 0f;
        for (int i = 0; i < spectrum.Length; i++)
        {
            avgIntensity += spectrum[i];
        }
        avgIntensity /= spectrum.Length;

        _rotationSpeed = ROTATION_SPEED * (1f + avgIntensity * SPECTRUM_ROTATION_INFLUENCE);
        float deltaTime = MathF.Max(0, (float)(Now - _lastUpdateTime).TotalSeconds);
        _angle = (_angle + _rotationSpeed * deltaTime) % MathF.Tau;

        if (_angle < 0) _angle += MathF.Tau;
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
            int startIdx = i * spectrumLength / ringCount;
            int endIdx = (i + 1) * spectrumLength / ringCount;
            endIdx = Math.Min(endIdx, spectrumLength);

            float sum = 0f;
            int count = endIdx - startIdx;
            if (count > 0)
            {
                for (int j = startIdx; j < endIdx; j++)
                {
                    sum += spectrum[j];
                }
                _ringMagnitudes[i] = sum / count;
            }
            else
            {
                _ringMagnitudes[i] = 0f;
            }
        }
    }

    [MethodImpl(AggressiveInlining)]
    private void RenderCircularWaves(
        SKCanvas canvas,
        SKImageInfo info,
        SKPaint basePaint,
        int ringCount)
    {
        if (_ringMagnitudes == null || _circlePoints == null) return;

        float smallerDimension = MathF.Min(info.Width, info.Height);
        float maxRadius = smallerDimension * 0.45f;
        float radiusIncrement = (maxRadius - CENTER_CIRCLE_RADIUS) / ringCount;

        using var mainPaint = _paintPool!.Get();
        mainPaint.Color = basePaint.Color;
        mainPaint.IsAntialias = UseAntiAlias;
        mainPaint.Style = Stroke;

        for (int i = ringCount - 1; i >= 0; i--)
        {
            float magnitude = _ringMagnitudes[i];
            if (magnitude < MIN_MAGNITUDE_THRESHOLD) continue;

            float baseRadius = CENTER_CIRCLE_RADIUS + i * radiusIncrement;

            float waveOffset = Sin(_time * WAVE_SPEED + i * PHASE_OFFSET + _angle)
                * magnitude
                * radiusIncrement;

            float radius = baseRadius + waveOffset;

            if (radius <= 0 ||
                _center.X + radius < 0 || _center.X - radius > info.Width ||
                _center.Y + radius < 0 || _center.Y - radius > info.Height)
            {
                continue;
            }

            float distanceFactor = 1.0f - radius / maxRadius;
            distanceFactor = MathF.Max(0, MathF.Min(1, distanceFactor));
            float alphaFactor = MIN_ALPHA_FACTOR + (1.0f - MIN_ALPHA_FACTOR) * distanceFactor;
            byte alpha = (byte)MathF.Min(magnitude * ALPHA_MULTIPLIER * alphaFactor, 255);

            float strokeWidth = MIN_STROKE_WIDTH + magnitude * STROKE_WIDTH_FACTOR;
            strokeWidth = Clamp(strokeWidth, MIN_STROKE_WIDTH, MAX_STROKE_WIDTH);


            mainPaint.StrokeWidth = strokeWidth;
            mainPaint.Color = basePaint.Color.WithAlpha(alpha);

            if (UseAdvancedEffects && magnitude > 0.5f)
            {
                RenderRingWithGlow(canvas, radius, magnitude, mainPaint);
            }
            else
            {
                RenderRing(canvas, radius, mainPaint);
            }
        }
    }

    private void RenderRing(SKCanvas canvas, float radius, SKPaint paint)
    {
        if (_circlePoints == null) return;

        using var path = _pathPool!.Get();

        bool firstPoint = true;
        foreach (var point in _circlePoints)
        {
            float x = _center.X + point.X * radius;
            float y = _center.Y + point.Y * radius;

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

        path.Close();
        canvas.DrawPath(path, paint);
    }

    private void RenderRingWithGlow(
        SKCanvas canvas,
        float radius,
        float magnitude,
        SKPaint paint)
    {
        if (_circlePoints == null) return;

        using var glowPaint = _paintPool!.Get();
        glowPaint.Color = paint.Color.WithAlpha((byte)MathF.Min(paint.Color.Alpha * 0.7f, 255));
        glowPaint.IsAntialias = paint.IsAntialias;
        glowPaint.Style = paint.Style;
        glowPaint.StrokeWidth = paint.StrokeWidth * 1.5f;
        glowPaint.MaskFilter = SKMaskFilter.CreateBlur(Normal, _glowRadius * magnitude);

        using var path = _pathPool!.Get();

        bool firstPoint = true;
        foreach (var point in _circlePoints)
        {
            float x = _center.X + point.X * radius;
            float y = _center.Y + point.Y * radius;

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

        path.Close();

        canvas.DrawPath(path, glowPaint);
        canvas.DrawPath(path, paint);
    }

    protected override void OnDispose()
    {
        Safe(() =>
        {
            _ringMagnitudes = null;
            _circlePoints = null;
            base.OnDispose();
        }, new ErrorHandlingOptions
        {
            Source = $"{GetType().Name}.OnDispose",
            ErrorMessage = "Error during CircularWaveRenderer disposal"
        });
    }
}