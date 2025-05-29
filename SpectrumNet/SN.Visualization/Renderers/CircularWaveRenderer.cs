#nullable enable

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class CircularWaveRenderer : EffectSpectrumRenderer<CircularWaveRenderer.QualitySettings>
{
    private static readonly Lazy<CircularWaveRenderer> _instance =
        new(() => new CircularWaveRenderer());

    public static CircularWaveRenderer GetInstance() => _instance.Value;

    private float _angle;
    private float _waveTime;
    private SKPoint[]? _circlePoints;

    public sealed class QualitySettings
    {
        public int PointsPerCircle { get; init; }
        public bool UseGlow { get; init; }
        public float GlowRadius { get; init; }
        public float MaxStroke { get; init; }
        public int MaxRings { get; init; }
        public float RotationSpeed { get; init; }
        public float WaveSpeed { get; init; }
        public float CenterRadius { get; init; }
        public float MaxRadiusFactor { get; init; }
        public float MinStroke { get; init; }
        public float WaveInfluence { get; init; }
        public float GlowThreshold { get; init; }
        public float GlowFactor { get; init; }
        public float GlowWidthFactor { get; init; }
        public float RotationIntensityFactor { get; init; }
        public float WavePhaseOffset { get; init; }
        public float StrokeClampFactor { get; init; }
        public float MinMagnitudeThreshold { get; init; }
    }

    protected override IReadOnlyDictionary<RenderQuality, QualitySettings>
        QualitySettingsPresets
    { get; } = new Dictionary<RenderQuality, QualitySettings>
    {
        [RenderQuality.Low] = new()
        {
            PointsPerCircle = 16,
            UseGlow = false,
            GlowRadius = 0f,
            MaxStroke = 6f,
            MaxRings = 16,
            RotationSpeed = 0.5f,
            WaveSpeed = 2.0f,
            CenterRadius = 30f,
            MaxRadiusFactor = 0.45f,
            MinStroke = 1.5f,
            WaveInfluence = 0.8f,
            GlowThreshold = 0.5f,
            GlowFactor = 0.7f,
            GlowWidthFactor = 1.5f,
            RotationIntensityFactor = 0.3f,
            WavePhaseOffset = 0.1f,
            StrokeClampFactor = 5f,
            MinMagnitudeThreshold = 0.01f
        },
        [RenderQuality.Medium] = new()
        {
            PointsPerCircle = 64,
            UseGlow = true,
            GlowRadius = 3f,
            MaxStroke = 7f,
            MaxRings = 24,
            RotationSpeed = 0.5f,
            WaveSpeed = 2.0f,
            CenterRadius = 30f,
            MaxRadiusFactor = 0.45f,
            MinStroke = 1.5f,
            WaveInfluence = 1f,
            GlowThreshold = 0.5f,
            GlowFactor = 0.7f,
            GlowWidthFactor = 1.5f,
            RotationIntensityFactor = 0.3f,
            WavePhaseOffset = 0.1f,
            StrokeClampFactor = 6f,
            MinMagnitudeThreshold = 0.01f
        },
        [RenderQuality.High] = new()
        {
            PointsPerCircle = 128,
            UseGlow = true,
            GlowRadius = 8f,
            MaxStroke = 8f,
            MaxRings = 32,
            RotationSpeed = 0.5f,
            WaveSpeed = 2.0f,
            CenterRadius = 30f,
            MaxRadiusFactor = 0.45f,
            MinStroke = 1.5f,
            WaveInfluence = 1f,
            GlowThreshold = 0.5f,
            GlowFactor = 0.7f,
            GlowWidthFactor = 1.5f,
            RotationIntensityFactor = 0.3f,
            WavePhaseOffset = 0.1f,
            StrokeClampFactor = 6f,
            MinMagnitudeThreshold = 0.01f
        }
    };

    protected override void RenderEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        RenderParameters renderParams,
        SKPaint passedInPaint)
    {
        if (CurrentQualitySettings == null || renderParams.EffectiveBarCount <= 0)
            return;

        var (isValid, processedSpectrum) = ProcessSpectrum(
            spectrum,
            renderParams.EffectiveBarCount,
            applyTemporalSmoothing: true);

        if (!isValid || processedSpectrum == null)
            return;

        var center = new SKPoint(info.Width * 0.5f, info.Height * 0.5f);
        float maxRadius = Min(info.Width, info.Height) * CurrentQualitySettings.MaxRadiusFactor;

        UpdateAnimation(processedSpectrum);
        EnsureCirclePoints();

        RenderWaveVisualization(
            canvas,
            processedSpectrum,
            center,
            maxRadius,
            renderParams,
            passedInPaint);
    }

    private void RenderWaveVisualization(
        SKCanvas canvas,
        float[] spectrum,
        SKPoint center,
        float maxRadius,
        RenderParameters renderParams,
        SKPaint basePaint)
    {
        var settings = CurrentQualitySettings!;

        RenderWithOverlay(canvas, () =>
        {
            int ringCount = Min(renderParams.EffectiveBarCount, settings.MaxRings);
            float ringStep = renderParams.BarWidth + renderParams.BarSpacing;

            for (int i = ringCount - 1; i >= 0; i--)
            {
                RenderRing(
                    canvas,
                    spectrum,
                    i,
                    ringCount,
                    ringStep,
                    center,
                    maxRadius,
                    basePaint,
                    settings);
            }
        });
    }

    private void RenderRing(
        SKCanvas canvas,
        float[] spectrum,
        int index,
        int totalRings,
        float ringStep,
        SKPoint center,
        float maxRadius,
        SKPaint basePaint,
        QualitySettings settings)
    {
        float magnitude = GetRingMagnitude(spectrum, index, totalRings);
        if (magnitude < settings.MinMagnitudeThreshold)
            return;

        float radius = CalculateRingRadius(index, ringStep, magnitude, settings);
        if (radius <= 0 || radius > maxRadius)
            return;

        byte alpha = CalculateRingAlpha(magnitude, radius, maxRadius);
        float strokeWidth = CalculateStrokeWidth(magnitude, settings);

        if (UseAdvancedEffects && settings.UseGlow && magnitude > settings.GlowThreshold)
            RenderGlowLayer(canvas, center, radius, magnitude, basePaint, settings, alpha, strokeWidth);

        RenderMainRing(canvas, center, radius, basePaint, alpha, strokeWidth);
    }

    private void RenderGlowLayer(
        SKCanvas canvas,
        SKPoint center,
        float radius,
        float magnitude,
        SKPaint basePaint,
        QualitySettings settings,
        byte alpha,
        float strokeWidth)
    {
        byte glowAlpha = (byte)(alpha * settings.GlowFactor);
        var glowPaint = CreatePaint(
            basePaint.Color.WithAlpha(glowAlpha),
            SKPaintStyle.Stroke);

        glowPaint.StrokeWidth = strokeWidth * settings.GlowWidthFactor;

        using var blurFilter = SKMaskFilter.CreateBlur(
            SKBlurStyle.Normal,
            settings.GlowRadius * magnitude);
        glowPaint.MaskFilter = blurFilter;

        try
        {
            DrawCircle(canvas, center, radius, glowPaint);
        }
        finally
        {
            ReturnPaint(glowPaint);
        }
    }

    private void RenderMainRing(
        SKCanvas canvas,
        SKPoint center,
        float radius,
        SKPaint basePaint,
        byte alpha,
        float strokeWidth)
    {
        var ringPaint = CreatePaint(
            basePaint.Color.WithAlpha(alpha),
            SKPaintStyle.Stroke);

        ringPaint.StrokeWidth = strokeWidth;

        try
        {
            DrawCircle(canvas, center, radius, ringPaint);
        }
        finally
        {
            ReturnPaint(ringPaint);
        }
    }

    private void DrawCircle(
        SKCanvas canvas,
        SKPoint center,
        float radius,
        SKPaint paint)
    {
        if (_circlePoints == null)
            return;

        RenderPath(canvas, path =>
        {
            bool first = true;
            foreach (var point in _circlePoints)
            {
                float x = center.X + point.X * radius;
                float y = center.Y + point.Y * radius;

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

    private void UpdateAnimation(float[] spectrum)
    {
        var settings = CurrentQualitySettings!;
        float avgIntensity = GetAverageIntensity(spectrum);
        const float deltaTime = 0.016f;

        _angle = (_angle
            + settings.RotationSpeed
            * (1f + avgIntensity * settings.RotationIntensityFactor)
            * deltaTime) % MathF.Tau;

        _waveTime += settings.WaveSpeed * deltaTime;
    }

    private void EnsureCirclePoints()
    {
        if (_circlePoints != null)
            return;

        var settings = CurrentQualitySettings!;
        _circlePoints = CreateCirclePoints(settings.PointsPerCircle);
    }

    private float CalculateRingRadius(
        int index,
        float ringStep,
        float magnitude,
        QualitySettings settings)
    {
        float baseRadius = settings.CenterRadius + index * ringStep;
        float waveOffset = MathF.Sin(_waveTime + index * settings.WavePhaseOffset + _angle) *
            magnitude * ringStep * settings.WaveInfluence;

        return baseRadius + waveOffset;
    }

    private static byte CalculateRingAlpha(
        float magnitude,
        float radius,
        float maxRadius)
    {
        float distanceFactor = 1f - radius / maxRadius;
        return (byte)(255 * magnitude * distanceFactor);
    }

    private static float CalculateStrokeWidth(
        float magnitude,
        QualitySettings settings) =>
        Clamp(
            settings.MinStroke + magnitude * settings.StrokeClampFactor,
            settings.MinStroke,
            settings.MaxStroke);

    private static float GetAverageIntensity(float[] spectrum)
    {
        if (spectrum.Length == 0)
            return 0f;

        float sum = 0f;
        for (int i = 0; i < spectrum.Length; i++)
        {
            sum += spectrum[i];
        }
        return sum / spectrum.Length;
    }

    private static float GetRingMagnitude(
        float[] spectrum,
        int ringIndex,
        int ringCount)
    {
        int start = ringIndex * spectrum.Length / ringCount;
        int end = Min((ringIndex + 1) * spectrum.Length / ringCount, spectrum.Length);

        if (start >= end)
            return 0f;

        float sum = 0f;
        for (int i = start; i < end; i++)
        {
            sum += spectrum[i];
        }
        return sum / (end - start);
    }

    private static SKPoint[] CreateCirclePoints(int pointCount)
    {
        var points = new SKPoint[pointCount];
        float angleStep = MathF.Tau / pointCount;

        for (int i = 0; i < pointCount; i++)
        {
            float angle = i * angleStep;
            points[i] = new SKPoint(
                MathF.Cos(angle),
                MathF.Sin(angle));
        }

        return points;
    }

    protected override int GetMaxBarsForQuality() => Quality switch
    {
        RenderQuality.Low => 16,
        RenderQuality.Medium => 24,
        RenderQuality.High => 32,
        _ => 24
    };

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();

        _circlePoints = null;

        float smoothingFactor = Quality switch
        {
            RenderQuality.Low => 0.4f,
            RenderQuality.Medium => 0.3f,
            RenderQuality.High => 0.25f,
            _ => 0.3f
        };

        SetProcessingSmoothingFactor(smoothingFactor);
        RequestRedraw();
    }

    protected override void OnDispose()
    {
        _circlePoints = null;
        base.OnDispose();
    }
}