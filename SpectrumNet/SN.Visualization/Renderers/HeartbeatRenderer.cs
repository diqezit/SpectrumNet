#nullable enable

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class HeartbeatRenderer : EffectSpectrumRenderer<HeartbeatRenderer.QualitySettings>
{
    private static readonly Lazy<HeartbeatRenderer> _instance =
        new(() => new HeartbeatRenderer());

    public static HeartbeatRenderer GetInstance() => _instance.Value;

    private const float
        MIN_MAGNITUDE_THRESHOLD = 0.01f,
        PULSE_FREQUENCY = 4f,
        PULSE_AMPLITUDE = 0.15f,
        HEART_SCALE = 1.3f,
        RADIUS_START_FACTOR = 0.02f,
        RADIUS_END_FACTOR = 0.95f,
        GLOW_INTENSITY = 0.6f,
        GLOW_INTENSITY_OVERLAY = 0.4f,
        ANIMATION_SPEED = 0.02f,
        SIZE_MULTIPLIER = 20f,
        SIZE_MULTIPLIER_OVERLAY = 15f,
        Y_OFFSET_FACTOR = 0.05f,
        SPIRAL_ROTATIONS = 6f,
        SPIRAL_EXPANSION_RATE = 0.25f,
        MIN_HEART_SPACING = 1.5f,
        SIZE_DECAY_FACTOR = 0.3f,
        EXPONENTIAL_PROGRESS_POWER = 0.85f,
        MAGNITUDE_RADIUS_FACTOR = 0.15f,
        SIMPLIFIED_HEART_RADIUS_FACTOR = 0.5f,
        FALLBACK_MAGNITUDE_BASE = 0.3f,
        FALLBACK_MAGNITUDE_VARIATION = 0.1f;

    private const int FALLBACK_MAGNITUDE_MODULO = 3;

    private float _animationTime;
    private readonly List<HeartElement> _heartCache = [];

    public sealed class QualitySettings
    {
        public bool UseGlow { get; init; }
        public bool UseSmoothing { get; init; }
        public float GlowRadius { get; init; }
        public byte GlowAlpha { get; init; }
        public float PulseIntensity { get; init; }
        public int SimplificationLevel { get; init; }
    }

    protected override IReadOnlyDictionary<RenderQuality, QualitySettings>
        QualitySettingsPresets
    { get; } = new Dictionary<RenderQuality, QualitySettings>
    {
        [RenderQuality.Low] = new()
        {
            UseGlow = false,
            UseSmoothing = false,
            GlowRadius = 0f,
            GlowAlpha = 0,
            PulseIntensity = 0.5f,
            SimplificationLevel = 2
        },
        [RenderQuality.Medium] = new()
        {
            UseGlow = true,
            UseSmoothing = true,
            GlowRadius = 8f,
            GlowAlpha = 25,
            PulseIntensity = 0.75f,
            SimplificationLevel = 1
        },
        [RenderQuality.High] = new()
        {
            UseGlow = true,
            UseSmoothing = true,
            GlowRadius = 12f,
            GlowAlpha = 35,
            PulseIntensity = 1f,
            SimplificationLevel = 0
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

        var heartData = CalculateHeartData(
            processedSpectrum,
            info,
            renderParams);

        if (!ValidateHeartData(heartData))
            return;

        RenderHeartVisualization(
            canvas,
            heartData,
            renderParams,
            passedInPaint);
    }

    private HeartData CalculateHeartData(
        float[] spectrum,
        SKImageInfo info,
        RenderParameters renderParams)
    {
        UpdateAnimation();

        var center = new SKPoint(info.Width * 0.5f, info.Height * 0.5f);
        float maxRadius = Min(info.Width, info.Height) * 0.48f;
        float sizeMultiplier = GetAdaptiveSizeMultiplier();

        _heartCache.Clear();
        int heartCount = renderParams.EffectiveBarCount;

        for (int i = 0; i < heartCount; i++)
        {
            float magnitude = GetMagnitudeForIndex(i, spectrum);

            if (ShouldSkipHeart(i, magnitude, spectrum.Length))
                continue;

            var element = CreateSpiralHeartElement(
                i,
                heartCount,
                magnitude,
                center,
                maxRadius,
                sizeMultiplier);

            if (IsHeartSpacingSufficient(element))
                _heartCache.Add(element);
        }

        return new HeartData(
            Hearts: [.. _heartCache],
            Center: center,
            AnimationPhase: _animationTime);
    }

    private static bool ValidateHeartData(HeartData data) =>
        data.Hearts.Count > 0;

    private void RenderHeartVisualization(
        SKCanvas canvas,
        HeartData data,
        RenderParameters renderParams,
        SKPaint passedInPaint)
    {
        var settings = CurrentQualitySettings!;

        RenderWithOverlay(canvas, () =>
        {
            if (UseAdvancedEffects && settings.UseGlow)
                RenderGlowLayer(canvas, data, passedInPaint, settings);

            RenderHeartLayer(canvas, data, passedInPaint, settings);
        });
    }

    private void UpdateAnimation()
    {
        _animationTime += ANIMATION_SPEED;
        if (_animationTime > MathF.Tau)
            _animationTime -= MathF.Tau;
    }

    private static float GetMagnitudeForIndex(int index, float[] spectrum)
    {
        if (index < spectrum.Length)
            return spectrum[index];

        return FALLBACK_MAGNITUDE_BASE +
               (index % FALLBACK_MAGNITUDE_MODULO) * FALLBACK_MAGNITUDE_VARIATION;
    }

    private static bool ShouldSkipHeart(int index, float magnitude, int spectrumLength) =>
        magnitude < MIN_MAGNITUDE_THRESHOLD && index < spectrumLength;

    private HeartElement CreateSpiralHeartElement(
        int index,
        int totalCount,
        float magnitude,
        SKPoint center,
        float maxRadius,
        float sizeMultiplier)
    {
        float progress = (float)index / totalCount;

        var spiralPosition = CalculateSpiralPosition(
            progress,
            center,
            maxRadius,
            magnitude);

        var (sizeBase, pulseFactor) = CalculateHeartSize(
            progress,
            magnitude,
            sizeMultiplier,
            spiralPosition.Angle);

        return new HeartElement(
            Position: spiralPosition.Position,
            Size: sizeBase,
            Magnitude: magnitude,
            Angle: spiralPosition.Angle,
            PulseFactor: pulseFactor);
    }

    private static SpiralPosition CalculateSpiralPosition(
        float progress,
        SKPoint center,
        float maxRadius,
        float magnitude)
    {
        float angle = CalculateSpiralAngle(progress);
        float radius = CalculateSpiralRadius(progress, maxRadius, magnitude);

        var position = CalculatePositionFromPolar(center, radius, angle);

        return new SpiralPosition(
            Position: position,
            Angle: angle);
    }

    private static float CalculateSpiralAngle(float progress) =>
        progress * MathF.Tau * SPIRAL_ROTATIONS;

    private static float CalculateSpiralRadius(
        float progress,
        float maxRadius,
        float magnitude)
    {
        float radiusStart = maxRadius * RADIUS_START_FACTOR;
        float radiusEnd = maxRadius * RADIUS_END_FACTOR;

        float exponentialProgress = MathF.Pow(progress, EXPONENTIAL_PROGRESS_POWER);
        float baseRadius = Lerp(radiusStart, radiusEnd, exponentialProgress);

        baseRadius += progress * maxRadius * SPIRAL_EXPANSION_RATE;
        baseRadius *= (1f + magnitude * MAGNITUDE_RADIUS_FACTOR);

        return baseRadius;
    }

    private static SKPoint CalculatePositionFromPolar(
        SKPoint center,
        float radius,
        float angle)
    {
        float x = radius * MathF.Cos(angle);
        float y = radius * MathF.Sin(angle) * (1f - Y_OFFSET_FACTOR);

        return new SKPoint(center.X + x, center.Y + y);
    }

    private (float Size, float PulseFactor) CalculateHeartSize(
        float progress,
        float magnitude,
        float sizeMultiplier,
        float angle)
    {
        float pulseFactor = CalculatePulseFactor(magnitude, angle);
        float sizeDecay = CalculateSizeDecay(progress);
        float size = magnitude * sizeMultiplier * pulseFactor * sizeDecay;

        return (size, pulseFactor);
    }

    private float CalculatePulseFactor(float magnitude, float angle)
    {
        float pulsePhase = _animationTime * PULSE_FREQUENCY + angle;
        return 1f + MathF.Sin(pulsePhase) * PULSE_AMPLITUDE * magnitude;
    }

    private static float CalculateSizeDecay(float progress) =>
        1f - progress * SIZE_DECAY_FACTOR;

    private bool IsHeartSpacingSufficient(HeartElement newHeart)
    {
        if (_heartCache.Count == 0)
            return true;

        float minSpacing = newHeart.Size * MIN_HEART_SPACING;

        return !_heartCache.Any(existingHeart =>
            CalculateDistance(newHeart.Position, existingHeart.Position) < minSpacing);
    }

    private static float CalculateDistance(SKPoint p1, SKPoint p2)
    {
        float dx = p2.X - p1.X;
        float dy = p2.Y - p1.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private void RenderGlowLayer(
        SKCanvas canvas,
        HeartData data,
        SKPaint basePaint,
        QualitySettings settings)
    {
        if (settings.GlowAlpha == 0) return;

        float glowIntensity = GetAdaptiveGlowIntensity();
        var glowColor = basePaint.Color.WithAlpha(settings.GlowAlpha);
        var glowPaint = CreatePaint(glowColor, SKPaintStyle.Fill);

        using var blurFilter = SKMaskFilter.CreateBlur(
            SKBlurStyle.Normal,
            settings.GlowRadius);
        glowPaint.MaskFilter = blurFilter;

        try
        {
            RenderGlowHearts(canvas, data.Hearts, glowIntensity, glowPaint, settings);
        }
        finally
        {
            ReturnPaint(glowPaint);
        }
    }

    private void RenderGlowHearts(
        SKCanvas canvas,
        List<HeartElement> hearts,
        float glowIntensity,
        SKPaint glowPaint,
        QualitySettings settings)
    {
        foreach (var heart in hearts)
        {
            float glowSize = heart.Size * (1f + glowIntensity);
            RenderHeart(canvas, heart.Position, glowSize, glowPaint, settings);
        }
    }

    private void RenderHeartLayer(
        SKCanvas canvas,
        HeartData data,
        SKPaint basePaint,
        QualitySettings settings)
    {
        var heartPaint = CreatePaint(basePaint.Color, SKPaintStyle.Fill);

        try
        {
            RenderHearts(canvas, data.Hearts, basePaint.Color, heartPaint, settings);
        }
        finally
        {
            ReturnPaint(heartPaint);
        }
    }

    private void RenderHearts(
        SKCanvas canvas,
        List<HeartElement> hearts,
        SKColor baseColor,
        SKPaint heartPaint,
        QualitySettings settings)
    {
        foreach (var heart in hearts)
        {
            byte alpha = CalculateAlpha(heart.Magnitude);
            heartPaint.Color = baseColor.WithAlpha(alpha);

            RenderHeart(canvas, heart.Position, heart.Size, heartPaint, settings);
        }
    }

    private void RenderHeart(
        SKCanvas canvas,
        SKPoint position,
        float size,
        SKPaint paint,
        QualitySettings settings)
    {
        if (settings.SimplificationLevel > 0)
        {
            RenderSimplifiedHeart(
                canvas,
                position,
                size,
                paint,
                settings.SimplificationLevel);
        }
        else
        {
            RenderDetailedHeart(canvas, position, size, paint);
        }
    }

    private void RenderSimplifiedHeart(
        SKCanvas canvas,
        SKPoint position,
        float size,
        SKPaint paint,
        int simplificationLevel)
    {
        float radius = size * SIMPLIFIED_HEART_RADIUS_FACTOR;

        if (simplificationLevel >= 2)
        {
            canvas.DrawCircle(position, radius, paint);
        }
        else
        {
            RenderPath(canvas, path =>
            {
                CreateSimplifiedHeartPath(path, position, radius);
            }, paint);
        }
    }

    private void RenderDetailedHeart(
        SKCanvas canvas,
        SKPoint position,
        float size,
        SKPaint paint)
    {
        RenderPath(canvas, path =>
        {
            CreateDetailedHeartPath(path, position, size);
        }, paint);
    }

    private static void CreateSimplifiedHeartPath(
        SKPath path,
        SKPoint center,
        float radius)
    {
        float x = center.X;
        float y = center.Y;
        float r = radius;

        path.MoveTo(x, y + r);
        path.CubicTo(
            x - r, y,
            x - r, y - r * 0.5f,
            x, y);
        path.CubicTo(
            x + r, y - r * 0.5f,
            x + r, y,
            x, y + r);
        path.Close();
    }

    private static void CreateDetailedHeartPath(
        SKPath path,
        SKPoint center,
        float size)
    {
        float scale = size * HEART_SCALE;
        float x = center.X;
        float y = center.Y;

        path.MoveTo(x, y + 0.3f * scale);

        path.CubicTo(
            x - 0.5f * scale, y - 0.3f * scale,
            x - 1f * scale, y + 0.1f * scale,
            x - 1f * scale, y + 0.5f * scale);

        path.CubicTo(
            x - 1f * scale, y + 0.9f * scale,
            x - 0.5f * scale, y + 1.3f * scale,
            x, y + 1.8f * scale);

        path.CubicTo(
            x + 0.5f * scale, y + 1.3f * scale,
            x + 1f * scale, y + 0.9f * scale,
            x + 1f * scale, y + 0.5f * scale);

        path.CubicTo(
            x + 1f * scale, y + 0.1f * scale,
            x + 0.5f * scale, y - 0.3f * scale,
            x, y + 0.3f * scale);

        path.Close();
    }

    private float GetAdaptiveSizeMultiplier() =>
        IsOverlayActive ? SIZE_MULTIPLIER_OVERLAY : SIZE_MULTIPLIER;

    private float GetAdaptiveGlowIntensity() =>
        IsOverlayActive ? GLOW_INTENSITY_OVERLAY : GLOW_INTENSITY;

    protected override int GetMaxBarsForQuality() => Quality switch
    {
        RenderQuality.Low => 100,
        RenderQuality.Medium => 200,
        RenderQuality.High => 300,
        _ => 200
    };

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();

        float smoothingFactor = Quality switch
        {
            RenderQuality.Low => 0.4f,
            RenderQuality.Medium => 0.3f,
            RenderQuality.High => 0.25f,
            _ => 0.3f
        };

        if (IsOverlayActive)
            smoothingFactor *= 1.2f;

        SetProcessingSmoothingFactor(smoothingFactor);

        _heartCache.Clear();

        RequestRedraw();
    }

    protected override void OnDispose()
    {
        _heartCache.Clear();
        _animationTime = 0f;
        base.OnDispose();
    }

    protected override void CleanupUnusedResources()
    {
        base.CleanupUnusedResources();

        if (_heartCache.Count > GetMaxBarsForQuality() * 2)
        {
            _heartCache.Clear();
        }
    }

    private record HeartData(
        List<HeartElement> Hearts,
        SKPoint Center,
        float AnimationPhase);

    private record HeartElement(
        SKPoint Position,
        float Size,
        float Magnitude,
        float Angle,
        float PulseFactor);

    private record SpiralPosition(
        SKPoint Position,
        float Angle);
}