#nullable enable

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class DotsRenderer : EffectSpectrumRenderer<DotsRenderer.QualitySettings>
{
    private static readonly Lazy<DotsRenderer> _instance =
        new(() => new DotsRenderer());

    public static DotsRenderer GetInstance() => _instance.Value;

    private const float BASE_DOT_RADIUS = 4.0f,
        MIN_DOT_RADIUS = 1.5f,
        MAX_DOT_RADIUS = 8.0f,
        DOT_RADIUS_SCALE_FACTOR = 0.8f,
        DOT_SPEED_BASE = 80.0f,
        DOT_SPEED_SCALE = 120.0f,
        DOT_VELOCITY_DAMPING = 0.95f,
        SPECTRUM_INFLUENCE_FACTOR = 2.0f,
        SPECTRUM_VELOCITY_FACTOR = 1.5f,
        ALPHA_BASE = 0.85f,
        GLOW_RADIUS_FACTOR = 0.3f,
        BASE_GLOW_ALPHA = 0.6f,
        BOUNDARY_DAMPING = 0.5f,
        CENTER_GRAVITY_OFFSET = 0.5f,
        GLOBAL_RADIUS_MIN = 0.5f,
        GLOBAL_RADIUS_MAX = 2.0f,
        ALPHA_FACTOR_MIN = 0.3f,
        ALPHA_FACTOR_MAX = 1.0f,
        ALPHA_FACTOR_OFFSET = 0.3f,
        ANIMATION_DELTA_TIME = 0.016f,
        GLOW_BLUR_SIGMA = 2.0f,
        MIN_VISIBLE_RADIUS = 0.5f;

    private const int DOTS_BATCH_SIZE = 64,
        COLOR_R_MIN = 180,
        COLOR_R_MAX = 255,
        COLOR_G_MIN = 100,
        COLOR_G_MAX = 180,
        COLOR_B_MIN = 50,
        COLOR_B_MAX = 100;

    private static readonly Random _random = new();

    private Dot[] _dots = [];
    private SKImageInfo _lastImageInfo;
    private float _maxSpectrum;
    private float _globalRadiusMultiplier = 1.0f;
    private readonly List<int> _visibleDotsIndices = [];

    public sealed class QualitySettings
    {
        public int DotCount { get; init; }
        public bool UseGlow { get; init; }
        public float GlowRadius { get; init; }
        public float GlowAlpha { get; init; }
        public float DotSpeedFactor { get; init; }
    }

    protected override IReadOnlyDictionary<RenderQuality, QualitySettings>
        QualitySettingsPresets
    { get; } = new Dictionary<RenderQuality, QualitySettings>
    {
        [RenderQuality.Low] = new()
        {
            DotCount = 75,
            UseGlow = false,
            GlowRadius = 0.2f,
            GlowAlpha = 0.4f,
            DotSpeedFactor = 0.7f
        },
        [RenderQuality.Medium] = new()
        {
            DotCount = 150,
            UseGlow = true,
            GlowRadius = GLOW_RADIUS_FACTOR,
            GlowAlpha = BASE_GLOW_ALPHA,
            DotSpeedFactor = 1.0f
        },
        [RenderQuality.High] = new()
        {
            DotCount = 300,
            UseGlow = true,
            GlowRadius = 0.5f,
            GlowAlpha = 0.8f,
            DotSpeedFactor = 1.3f
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

        var dotsData = CalculateDotsData(
            processedSpectrum,
            info,
            renderParams);

        if (!ValidateDotsData(dotsData))
            return;

        RenderDotsVisualization(
            canvas,
            dotsData,
            renderParams,
            passedInPaint);
    }

    private DotsData CalculateDotsData(
        float[] spectrum,
        SKImageInfo info,
        RenderParameters renderParams)
    {
        EnsureDotsInitialized(info);

        _maxSpectrum = CalculateMaxSpectrum(spectrum);
        _globalRadiusMultiplier = CalculateGlobalRadiusMultiplier(_maxSpectrum);

        UpdateDotsPhysics(spectrum, info);
        UpdateVisibleDotsIndices();

        return new DotsData(
            VisibleIndices: [.. _visibleDotsIndices],
            MaxSpectrum: _maxSpectrum,
            GlobalRadiusMultiplier: _globalRadiusMultiplier);
    }

    private static bool ValidateDotsData(DotsData data) =>
        data.VisibleIndices.Count > 0;

    private void RenderDotsVisualization(
        SKCanvas canvas,
        DotsData data,
        RenderParameters renderParams,
        SKPaint passedInPaint)
    {
        var settings = CurrentQualitySettings!;

        RenderWithOverlay(canvas, () =>
        {
            byte alpha = CalculateDotsAlpha(data.MaxSpectrum);

            if (UseAdvancedEffects && settings.UseGlow)
                RenderGlowLayer(canvas, data, alpha, passedInPaint, settings);

            RenderDotsLayer(canvas, data, alpha, passedInPaint);
        });
    }

    private void EnsureDotsInitialized(SKImageInfo info)
    {
        bool needsReset = _lastImageInfo.Width != info.Width ||
                         _lastImageInfo.Height != info.Height ||
                         _dots.Length != CurrentQualitySettings!.DotCount;

        if (needsReset)
            ResetDots(info);
    }

    private void ResetDots(SKImageInfo info)
    {
        _lastImageInfo = info;
        int dotCount = CurrentQualitySettings!.DotCount;
        _dots = new Dot[dotCount];

        for (int i = 0; i < dotCount; i++)
            _dots[i] = CreateRandomDot(info);

        _visibleDotsIndices.Clear();
        _visibleDotsIndices.Capacity = dotCount;
    }

    private Dot CreateRandomDot(SKImageInfo info)
    {
        var position = GenerateRandomPosition(info);
        var velocity = GenerateRandomVelocity();
        float baseRadius = GenerateRandomRadius();
        var color = GenerateRandomColor();

        return new Dot(
            Position: position,
            Velocity: velocity,
            Radius: baseRadius,
            BaseRadius: baseRadius,
            Color: color);
    }

    private static SKPoint GenerateRandomPosition(SKImageInfo info) =>
        new(_random.NextSingle() * info.Width,
            _random.NextSingle() * info.Height);

    private SKPoint GenerateRandomVelocity()
    {
        float speedFactor = CurrentQualitySettings!.DotSpeedFactor;
        float vx = (_random.NextSingle() - 0.5f) * DOT_SPEED_BASE * speedFactor;
        float vy = (_random.NextSingle() - 0.5f) * DOT_SPEED_BASE * speedFactor;
        return new SKPoint(vx, vy);
    }

    private static float GenerateRandomRadius() =>
        Lerp(MIN_DOT_RADIUS, MAX_DOT_RADIUS, _random.NextSingle());

    private static SKColor GenerateRandomColor()
    {
        byte r = (byte)_random.Next(COLOR_R_MIN, COLOR_R_MAX);
        byte g = (byte)_random.Next(COLOR_G_MIN, COLOR_G_MAX);
        byte b = (byte)_random.Next(COLOR_B_MIN, COLOR_B_MAX);
        return new SKColor(r, g, b);
    }

    private static float CalculateMaxSpectrum(float[] spectrum) =>
        spectrum.Length > 0 ? spectrum.Max() : 0f;

    private static float CalculateGlobalRadiusMultiplier(float maxSpectrum) =>
        Clamp(
            1.0f + maxSpectrum * DOT_RADIUS_SCALE_FACTOR,
            GLOBAL_RADIUS_MIN,
            GLOBAL_RADIUS_MAX);

    private void UpdateDotsPhysics(float[] spectrum, SKImageInfo info)
    {
        int spectrumLength = spectrum.Length;
        int dotsCount = _dots.Length;

        for (int i = 0; i < dotsCount; i++)
        {
            int spectrumIndex = GetSpectrumIndexForDot(i, dotsCount, spectrumLength);
            float spectrumValue = spectrum[spectrumIndex];
            _dots[i] = UpdateDotPhysics(_dots[i], spectrumValue, info);
        }
    }

    private static int GetSpectrumIndexForDot(int dotIndex, int totalDots, int spectrumLength) =>
        Min(dotIndex * spectrumLength / totalDots, spectrumLength - 1);

    private Dot UpdateDotPhysics(Dot dot, float spectrumValue, SKImageInfo info)
    {
        var forces = CalculateForces(dot, spectrumValue, info);
        var newVelocity = ApplyForces(dot.Velocity, forces);
        var newPosition = IntegratePosition(dot.Position, newVelocity);

        (newPosition, newVelocity) = ApplyBoundaryConstraints(
            newPosition,
            newVelocity,
            info);

        float newRadius = CalculateDotRadius(
            dot.BaseRadius,
            spectrumValue,
            _globalRadiusMultiplier);

        return dot with
        {
            Position = newPosition,
            Velocity = newVelocity,
            Radius = newRadius
        };
    }

    private Forces CalculateForces(Dot dot, float spectrumValue, SKImageInfo info)
    {
        var normalizedPos = NormalizePosition(dot.Position, info);
        var gravity = CalculateGravityForce(normalizedPos);
        var spectrum = CalculateSpectrumForce(normalizedPos, spectrumValue);

        return new Forces(
            Total: new SKPoint(
                gravity.X + spectrum.X,
                gravity.Y + spectrum.Y));
    }

    private static SKPoint NormalizePosition(SKPoint position, SKImageInfo info) =>
        new(position.X / info.Width, position.Y / info.Height);

    private SKPoint CalculateGravityForce(SKPoint normalizedPos)
    {
        float speedFactor = CurrentQualitySettings!.DotSpeedFactor;
        float fx = (CENTER_GRAVITY_OFFSET - normalizedPos.X) * DOT_SPEED_BASE * speedFactor;
        float fy = (CENTER_GRAVITY_OFFSET - normalizedPos.Y) * DOT_SPEED_BASE * speedFactor;
        return new SKPoint(fx, fy);
    }

    private static SKPoint CalculateSpectrumForce(SKPoint normalizedPos, float spectrumValue)
    {
        float factor = spectrumValue * SPECTRUM_INFLUENCE_FACTOR * DOT_SPEED_SCALE;
        float fx = (normalizedPos.X - CENTER_GRAVITY_OFFSET) * factor;
        float fy = (normalizedPos.Y - CENTER_GRAVITY_OFFSET) * factor;
        return new SKPoint(fx, fy);
    }

    private static SKPoint ApplyForces(SKPoint velocity, Forces forces)
    {
        float vx = (velocity.X + forces.Total.X * ANIMATION_DELTA_TIME) * DOT_VELOCITY_DAMPING;
        float vy = (velocity.Y + forces.Total.Y * ANIMATION_DELTA_TIME) * DOT_VELOCITY_DAMPING;
        return new SKPoint(vx, vy);
    }

    private static SKPoint IntegratePosition(SKPoint position, SKPoint velocity) =>
        new(
            position.X + velocity.X * ANIMATION_DELTA_TIME,
            position.Y + velocity.Y * ANIMATION_DELTA_TIME);

    private static (SKPoint position, SKPoint velocity) ApplyBoundaryConstraints(
        SKPoint position,
        SKPoint velocity,
        SKImageInfo info)
    {
        var (x, vx) = ClampPosition(position.X, velocity.X, 0, info.Width);
        var (y, vy) = ClampPosition(position.Y, velocity.Y, 0, info.Height);

        return (new SKPoint(x, y), new SKPoint(vx, vy));
    }

    private static (float position, float velocity) ClampPosition(
        float position,
        float velocity,
        float min,
        float max)
    {
        if (position < min)
            return (min, -velocity * BOUNDARY_DAMPING);
        if (position > max)
            return (max, -velocity * BOUNDARY_DAMPING);
        return (position, velocity);
    }

    private static float CalculateDotRadius(
        float baseRadius,
        float spectrumValue,
        float globalMultiplier) =>
        baseRadius * (1.0f + spectrumValue * SPECTRUM_VELOCITY_FACTOR) * globalMultiplier;

    private void UpdateVisibleDotsIndices()
    {
        _visibleDotsIndices.Clear();

        for (int i = 0; i < _dots.Length; i++)
        {
            if (_dots[i].Radius >= MIN_VISIBLE_RADIUS)
                _visibleDotsIndices.Add(i);
        }
    }

    private static byte CalculateDotsAlpha(float maxSpectrum)
    {
        float alphaFactor = Clamp(
            maxSpectrum + ALPHA_FACTOR_OFFSET,
            ALPHA_FACTOR_MIN,
            ALPHA_FACTOR_MAX);

        return CalculateAlpha(ALPHA_BASE * alphaFactor);
    }

    private void RenderGlowLayer(
        SKCanvas canvas,
        DotsData data,
        byte alpha,
        SKPaint basePaint,
        QualitySettings settings)
    {
        if (settings.GlowAlpha == 0) return;

        var glowAlpha = (byte)(settings.GlowAlpha * alpha);

        using var blurFilter = SKMaskFilter.CreateBlur(
            SKBlurStyle.Normal,
            BASE_DOT_RADIUS * settings.GlowRadius * GLOW_BLUR_SIGMA);

        for (int i = 0; i < data.VisibleIndices.Count; i += DOTS_BATCH_SIZE)
        {
            int batchEnd = Min(i + DOTS_BATCH_SIZE, data.VisibleIndices.Count);
            RenderGlowBatch(canvas, data.VisibleIndices, i, batchEnd, glowAlpha, blurFilter);
        }
    }

    private void RenderGlowBatch(
        SKCanvas canvas,
        List<int> indices,
        int start,
        int end,
        byte glowAlpha,
        SKMaskFilter blurFilter)
    {
        for (int i = start; i < end; i++)
        {
            var dot = _dots[indices[i]];
            var glowPaint = CreatePaint(dot.Color.WithAlpha(glowAlpha), SKPaintStyle.Fill);
            glowPaint.MaskFilter = blurFilter;

            try
            {
                float glowRadius = dot.Radius * (1.0f + CurrentQualitySettings!.GlowRadius);
                canvas.DrawCircle(dot.Position, glowRadius, glowPaint);
            }
            finally
            {
                ReturnPaint(glowPaint);
            }
        }
    }

    private void RenderDotsLayer(
        SKCanvas canvas,
        DotsData data,
        byte alpha,
        SKPaint basePaint)
    {
        for (int i = 0; i < data.VisibleIndices.Count; i += DOTS_BATCH_SIZE)
        {
            int batchEnd = Min(i + DOTS_BATCH_SIZE, data.VisibleIndices.Count);
            RenderDotsBatch(canvas, data.VisibleIndices, i, batchEnd, alpha);
        }
    }

    private void RenderDotsBatch(
        SKCanvas canvas,
        List<int> indices,
        int start,
        int end,
        byte alpha)
    {
        for (int i = start; i < end; i++)
        {
            var dot = _dots[indices[i]];
            var dotPaint = CreatePaint(dot.Color.WithAlpha(alpha), SKPaintStyle.Fill);

            try
            {
                canvas.DrawCircle(dot.Position, dot.Radius, dotPaint);
            }
            finally
            {
                ReturnPaint(dotPaint);
            }
        }
    }

    protected override int GetMaxBarsForQuality() => Quality switch
    {
        RenderQuality.Low => 75,
        RenderQuality.Medium => 150,
        RenderQuality.High => 300,
        _ => 150
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

        if (_lastImageInfo.Width > 0 && _lastImageInfo.Height > 0)
            ResetDots(_lastImageInfo);

        RequestRedraw();
    }

    protected override void OnDispose()
    {
        _dots = [];
        _visibleDotsIndices.Clear();
        _lastImageInfo = default;
        _maxSpectrum = 0f;
        _globalRadiusMultiplier = 1.0f;
        base.OnDispose();
    }

    protected override void CleanupUnusedResources()
    {
        base.CleanupUnusedResources();

        if (_dots.Length > GetMaxBarsForQuality() * 2)
        {
            ResetDots(_lastImageInfo);
        }

        if (_visibleDotsIndices.Capacity > GetMaxBarsForQuality() * 3)
        {
            _visibleDotsIndices.TrimExcess();
        }
    }

    private record DotsData(
        List<int> VisibleIndices,
        float MaxSpectrum,
        float GlobalRadiusMultiplier);

    private record struct Dot(
        SKPoint Position,
        SKPoint Velocity,
        float Radius,
        float BaseRadius,
        SKColor Color);

    private record Forces(
        SKPoint Total);
}