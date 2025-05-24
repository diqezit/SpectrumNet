#nullable enable

using static SpectrumNet.SN.Visualization.Renderers.RippleRenderer.Constants;
using static System.MathF;

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class RippleRenderer : EffectSpectrumRenderer
{
    private const string LogPrefix = nameof(RippleRenderer);

    private static readonly Lazy<RippleRenderer> _instance =
        new(() => new RippleRenderer());

    private RippleRenderer() { }

    public static RippleRenderer GetInstance() => _instance.Value;

    public static class Constants
    {
        public const float
                BASE_RIPPLE_RADIUS = 300f,
                RIPPLE_SPEED = 150f,
                BASE_RIPPLE_SPAWN_RATE = 0.1f,
                BASE_RIPPLE_WIDTH = 3f,
                FADE_DISTANCE = 50f,
                COLOR_ROTATION_SPEED = 0.5f,
                MIN_SPAWN_MAGNITUDE = 0.15f,
                MAGNITUDE_RADIUS_SCALE = 200f,
                BAND_ANGLE_STEP = 45f,
                GLOW_MAGNITUDE_THRESHOLD = 0.5f,
                GLOW_BLUR_RADIUS = 5f,
                GLOW_WIDTH_MULTIPLIER = 2f,
                GLOW_ALPHA_FACTOR = 100f,
                SPAWN_DISTANCE_BASE = 100f,
                STROKE_WIDTH_SCALE = 1f,
                SATURATION_BASE = 80f,
                SATURATION_RANGE = 20f,
                BRIGHTNESS_BASE = 70f,
                BRIGHTNESS_RANGE = 30f,
                HUE_DEGREES = 360f,
                COLOR_WRAP = 1f,
                BAR_WIDTH_RADIUS_INFLUENCE = 3f,
                BAR_SPACING_RADIUS_INFLUENCE = 8f,
                BAR_WIDTH_SPAWN_INFLUENCE = 1f,
                BAR_SPACING_SPAWN_INFLUENCE = 0.5f,
                MAX_SIMULTANEOUS_SPAWNS = 12f,
                SPEED_MULTIPLIER_MAX = 3f,
                SPAWN_RATE_REDUCTION_MAX = 0.9f,
                STROKE_WIDTH_REDUCTION_MAX = 0.7f,
                RADIUS_GROWTH_FACTOR = 300f,
                DISTANCE_GROWTH_FACTOR = 200f,
                RIPPLES_GROWTH_FACTOR = 3f,
                MIN_STROKE_WIDTH = 0.3f,
                CLEANUP_THRESHOLD = 1.5f,
                BAR_COUNT_RADIUS_MULTIPLIER = 0.5f,
                BAR_COUNT_DISTANCE_MULTIPLIER = 0.8f;

        public const int
            BASE_MAX_RIPPLES_LOW = 30,
            BASE_MAX_RIPPLES_MEDIUM = 60,
            BASE_MAX_RIPPLES_HIGH = 100,
            SPECTRUM_BANDS = 8,
            MIN_RIPPLES = 10,
            MAX_RIPPLES_ABSOLUTE = 300,
                MIN_BAR_COUNT = 10,
                MAX_BAR_COUNT_REFERENCE = 500;

        public const byte 
            GLOW_ALPHA_BASE = 100;

        public static readonly Dictionary<RenderQuality, QualitySettings> QualityPresets = new()
        {
            [RenderQuality.Low] = new(
                BaseMaxRipples: BASE_MAX_RIPPLES_LOW,
                UseGlow: false,
                GlowRadius: GLOW_BLUR_RADIUS * 0.5f,
                GlowAlpha: GLOW_ALPHA_FACTOR * 0.5f,
                StrokeWidth: BASE_RIPPLE_WIDTH * 0.8f,
                SpawnRate: BASE_RIPPLE_SPAWN_RATE * 1.5f,
                FadeDistance: FADE_DISTANCE * 1.2f,
                MagnitudeThreshold: GLOW_MAGNITUDE_THRESHOLD * 1.2f
            ),
            [RenderQuality.Medium] = new(
                BaseMaxRipples: BASE_MAX_RIPPLES_MEDIUM,
                UseGlow: true,
                GlowRadius: GLOW_BLUR_RADIUS,
                GlowAlpha: GLOW_ALPHA_FACTOR,
                StrokeWidth: BASE_RIPPLE_WIDTH,
                SpawnRate: BASE_RIPPLE_SPAWN_RATE,
                FadeDistance: FADE_DISTANCE,
                MagnitudeThreshold: GLOW_MAGNITUDE_THRESHOLD
            ),
            [RenderQuality.High] = new(
                BaseMaxRipples: BASE_MAX_RIPPLES_HIGH,
                UseGlow: true,
                GlowRadius: GLOW_BLUR_RADIUS * 1.5f,
                GlowAlpha: GLOW_ALPHA_FACTOR * 1.2f,
                StrokeWidth: BASE_RIPPLE_WIDTH * 1.2f,
                SpawnRate: BASE_RIPPLE_SPAWN_RATE * 0.8f,
                FadeDistance: FADE_DISTANCE * 0.8f,
                MagnitudeThreshold: GLOW_MAGNITUDE_THRESHOLD * 0.8f
            )
        };

        public record QualitySettings(
            int BaseMaxRipples,
            bool UseGlow,
            float GlowRadius,
            float GlowAlpha,
            float StrokeWidth,
            float SpawnRate,
            float FadeDistance,
            float MagnitudeThreshold
        );
    }

    private readonly List<Ripple> _ripples = new(MAX_RIPPLES_ABSOLUTE);
    private readonly float[] _bandMagnitudes = new float[SPECTRUM_BANDS];
    private float _lastSpawnTime;
    private float _colorRotation;
    private QualitySettings _currentSettings = QualityPresets[RenderQuality.Medium];
    private RenderContext? _currentContext;

    private readonly SKPaint _ripplePaint = new()
    {
        Style = SKPaintStyle.Stroke,
        StrokeWidth = BASE_RIPPLE_WIDTH,
        IsAntialias = true
    };

    private readonly SKPaint _glowPaint = new()
    {
        Style = SKPaintStyle.Stroke,
        IsAntialias = true
    };

    protected override void OnInitialize()
    {
        base.OnInitialize();
        _logger.Log(LogLevel.Debug, LogPrefix, "Initialized");
    }

    protected override void OnQualitySettingsApplied()
    {
        _currentSettings = QualityPresets[Quality];
        _ripplePaint.StrokeWidth = _currentSettings.StrokeWidth;
        _ripplePaint.IsAntialias = UseAntiAlias;
        _glowPaint.IsAntialias = UseAntiAlias;
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
        _currentContext = new RenderContext(barWidth, barSpacing, barCount, info);

        float deltaTime = _animationTimer.DeltaTime;
        float currentTime = _animationTimer.Time;

        UpdateBandMagnitudes(spectrum, barCount);
        UpdateRipples(deltaTime);
        SpawnNewRipples(currentTime, paint);
        RenderRipples(canvas, paint);

        _colorRotation += deltaTime * COLOR_ROTATION_SPEED;
        if (_colorRotation > COLOR_WRAP) _colorRotation -= COLOR_WRAP;
    }

    private void UpdateBandMagnitudes(float[] spectrum, int barCount)
    {
        if (spectrum.Length == 0) return;

        int effectiveLength = Min(spectrum.Length, barCount);
        int bandSize = Max(1, effectiveLength / SPECTRUM_BANDS);

        for (int i = 0; i < SPECTRUM_BANDS; i++)
        {
            int start = i * bandSize;
            int end = Min((i + 1) * bandSize, effectiveLength);

            float sum = 0;
            for (int j = start; j < end; j++)
                sum += spectrum[j];

            _bandMagnitudes[i] = sum / Max(1, end - start);
        }
    }

    private void UpdateRipples(float deltaTime)
    {
        float speedMultiplier = GetSpeedMultiplier();

        for (int i = _ripples.Count - 1; i >= 0; i--)
        {
            var ripple = _ripples[i];
            ripple.Radius += RIPPLE_SPEED * deltaTime * speedMultiplier;

            if (ripple.Radius > ripple.MaxRadius)
                _ripples.RemoveAt(i);
        }
    }

    private float GetNormalizedBarCount()
    {
        if (_currentContext == null) return 0f;
        return Clamp((float)(_currentContext.BarCount - MIN_BAR_COUNT) 
            / (MAX_BAR_COUNT_REFERENCE - MIN_BAR_COUNT), 0f, 1f);
    }

    private float GetSpeedMultiplier()
    {
        float normalized = GetNormalizedBarCount();
        return 1f + Pow(normalized, 0.7f) * (SPEED_MULTIPLIER_MAX - 1f);
    }

    private void SpawnNewRipples(float currentTime, SKPaint basePaint)
    {
        if (_currentContext == null || !CanSpawnRipple(currentTime)) return;

        int spawnCount = CalculateSpawnCount();
        float centerX = _currentContext.Info.Width / 2f;
        float centerY = _currentContext.Info.Height / 2f;

        for (int spawn = 0; spawn < spawnCount; spawn++)
        {
            for (int i = 0; i < SPECTRUM_BANDS; i++)
            {
                if (_bandMagnitudes[i] < MIN_SPAWN_MAGNITUDE) continue;
                if (_ripples.Count >= GetMaxRipples()) break;

                var ripple = CreateRippleForBand(i, centerX, centerY, basePaint);
                _ripples.Add(ripple);
            }
        }

        _lastSpawnTime = currentTime;
    }

    private int CalculateSpawnCount()
    {
        if (_currentContext == null) return 1;

        float normalized = GetNormalizedBarCount();
        float barInfluence = Pow(normalized, 0.7f) * 5f;

        float magnitudeSum = 0;
        for (int i = 0; i < SPECTRUM_BANDS; i++)
            magnitudeSum += _bandMagnitudes[i];

        float magnitudeInfluence = magnitudeSum / SPECTRUM_BANDS;
        int baseCount = 1 + (int)(barInfluence + magnitudeInfluence * 4);

        return Min(baseCount, (int)MAX_SIMULTANEOUS_SPAWNS);
    }

    private bool CanSpawnRipple(float currentTime)
    {
        float adjustedSpawnRate = GetAdjustedSpawnRate();
        return currentTime - _lastSpawnTime >= adjustedSpawnRate &&
               _ripples.Count < GetMaxRipples();
    }

    private float GetAdjustedSpawnRate()
    {
        if (_currentContext == null) return _currentSettings.SpawnRate;

        float normalized = GetNormalizedBarCount();
        float barCountFactor = 1f - (Pow(normalized, 0.8f) * SPAWN_RATE_REDUCTION_MAX);
        return _currentSettings.SpawnRate * MathF.Max(0.01f, barCountFactor);
    }

    private int GetMaxRipples()
    {
        if (_currentContext == null) return _currentSettings.BaseMaxRipples;

        float normalized = GetNormalizedBarCount();
        float multiplier = 1f + Pow(normalized, 0.6f) * RIPPLES_GROWTH_FACTOR;
        int calculatedMax = (int)(_currentSettings.BaseMaxRipples * multiplier);

        return Min(calculatedMax, MAX_RIPPLES_ABSOLUTE);
    }

    private Ripple CreateRippleForBand(
        int bandIndex,
        float centerX,
        float centerY,
        SKPaint basePaint)
    {
        if (_currentContext == null) throw new InvalidOperationException("Context is null");

        float angle = bandIndex * BAND_ANGLE_STEP * MathF.PI / 180f;
        float distance = CalculateSpawnDistance(bandIndex);
        float maxRadius = CalculateMaxRadius(bandIndex);

        return new Ripple
        {
            X = centerX + Cos(angle) * distance,
            Y = centerY + Sin(angle) * distance,
            Radius = 0,
            MaxRadius = maxRadius,
            Magnitude = _bandMagnitudes[bandIndex],
            ColorHue = (_colorRotation + bandIndex / (float)SPECTRUM_BANDS) % COLOR_WRAP,
            BaseColor = basePaint.Color
        };
    }

    private float CalculateSpawnDistance(int bandIndex)
    {
        if (_currentContext == null) return SPAWN_DISTANCE_BASE;

        float normalized = GetNormalizedBarCount();
        float barCountMultiplier = 1f + Pow(normalized, 0.8f) * BAR_COUNT_DISTANCE_MULTIPLIER;

        float baseDist = SPAWN_DISTANCE_BASE * barCountMultiplier;
        float magnitudeDist = _bandMagnitudes[bandIndex] * SPAWN_DISTANCE_BASE * 0.5f;
        float widthInfluence = _currentContext.BarWidth * BAR_WIDTH_SPAWN_INFLUENCE;
        float spacingInfluence = _currentContext.BarSpacing * BAR_SPACING_SPAWN_INFLUENCE;
        float barCountInfluence = Sqrt(normalized) * DISTANCE_GROWTH_FACTOR;

        return baseDist + magnitudeDist + widthInfluence + spacingInfluence + barCountInfluence;
    }

    private float CalculateMaxRadius(int bandIndex)
    {
        if (_currentContext == null) return BASE_RIPPLE_RADIUS;

        float normalized = GetNormalizedBarCount();
        float barCountMultiplier = 1f + Pow(normalized, 0.6f) * BAR_COUNT_RADIUS_MULTIPLIER;

        float baseRadius = BASE_RIPPLE_RADIUS * barCountMultiplier;
        float magnitudeBonus = _bandMagnitudes[bandIndex] * MAGNITUDE_RADIUS_SCALE;
        float widthInfluence = _currentContext.BarWidth * BAR_WIDTH_RADIUS_INFLUENCE;
        float spacingInfluence = _currentContext.BarSpacing * BAR_SPACING_RADIUS_INFLUENCE;
        float barCountInfluence = Pow(normalized, 0.5f) * RADIUS_GROWTH_FACTOR;

        return baseRadius + magnitudeBonus + widthInfluence + spacingInfluence + barCountInfluence;
    }

    private void RenderRipples(SKCanvas canvas, SKPaint basePaint)
    {
        foreach (var ripple in _ripples)
        {
            float alpha = CalculateRippleAlpha(ripple);
            if (alpha <= 0) continue;

            RenderSingleRipple(canvas, ripple, alpha, basePaint);
        }
    }

    private void RenderSingleRipple(
        SKCanvas canvas,
        Ripple ripple,
        float alpha,
        SKPaint basePaint)
    {
        SKColor color = BlendColors(
            GetRippleColor(ripple.ColorHue, ripple.Magnitude),
            ripple.BaseColor,
            0.7f
        );

        float strokeWidth = CalculateStrokeWidth(ripple);

        _ripplePaint.Color = color.WithAlpha((byte)(alpha * 255));
        _ripplePaint.StrokeWidth = strokeWidth;

        canvas.DrawCircle(ripple.X, ripple.Y, ripple.Radius, _ripplePaint);

        if (ShouldRenderGlow(ripple.Magnitude))
            RenderRippleGlow(canvas, ripple, color, alpha);
    }

    private float CalculateStrokeWidth(Ripple ripple)
    {
        if (_currentContext == null) return _currentSettings.StrokeWidth;

        float normalized = GetNormalizedBarCount();
        float widthScale = 1f - (Pow(normalized, 1.2f) * STROKE_WIDTH_REDUCTION_MAX);

        float baseWidth = _currentSettings.StrokeWidth * widthScale;
        float magnitudeScale = STROKE_WIDTH_SCALE + ripple.Magnitude * 0.5f;
        float barInfluence = 1f + (_currentContext.BarWidth / 150f);

        return MathF.Max(MIN_STROKE_WIDTH, baseWidth * magnitudeScale * barInfluence);
    }

    private bool ShouldRenderGlow(float magnitude) =>
        UseAdvancedEffects && _currentSettings.UseGlow &&
        magnitude > _currentSettings.MagnitudeThreshold;

    private void RenderRippleGlow(
        SKCanvas canvas,
        Ripple ripple,
        SKColor color,
        float alpha)
    {
        _glowPaint.Color = color.WithAlpha((byte)(alpha * _currentSettings.GlowAlpha));
        _glowPaint.StrokeWidth = _ripplePaint.StrokeWidth * GLOW_WIDTH_MULTIPLIER;

        using var filter = SKMaskFilter.CreateBlur(
            SKBlurStyle.Normal,
            _currentSettings.GlowRadius
        );
        _glowPaint.MaskFilter = filter;
        canvas.DrawCircle(ripple.X, ripple.Y, ripple.Radius, _glowPaint);
        _glowPaint.MaskFilter = null;
    }

    private float CalculateRippleAlpha(Ripple ripple)
    {
        float progress = ripple.Radius / ripple.MaxRadius;
        float fadeStart = 1f - (_currentSettings.FadeDistance / ripple.MaxRadius);

        if (progress < fadeStart)
            return ripple.Magnitude;

        float fadeProgress = (progress - fadeStart) / (1f - fadeStart);
        return ripple.Magnitude * (1f - fadeProgress);
    }

    private static SKColor GetRippleColor(float hue, float magnitude)
    {
        float saturation = SATURATION_BASE + magnitude * SATURATION_RANGE;
        float brightness = BRIGHTNESS_BASE + magnitude * BRIGHTNESS_RANGE;
        return SKColor.FromHsv(hue * HUE_DEGREES, saturation, brightness);
    }

    private static SKColor BlendColors(SKColor color1, SKColor color2, float factor)
    {
        byte r = (byte)(color1.Red * factor + color2.Red * (1 - factor));
        byte g = (byte)(color1.Green * factor + color2.Green * (1 - factor));
        byte b = (byte)(color1.Blue * factor + color2.Blue * (1 - factor));
        return new SKColor(r, g, b);
    }

    protected override void BeforeRender(
        SKCanvas? canvas,
        float[]? spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint? paint)
    {
        int maxRipples = GetMaxRipples();
        if (_ripples.Count > maxRipples * CLEANUP_THRESHOLD)
        {
            int removeCount = _ripples.Count - maxRipples;
            _ripples.RemoveRange(0, removeCount);
        }
    }

    protected override void AfterRender(
        SKCanvas canvas,
        float[] processedSpectrum,
        SKImageInfo info)
    {
        _currentContext = null;
    }

    protected override void CleanupUnusedResources()
    {
        _ripples.RemoveAll(r => r.Radius > r.MaxRadius * CLEANUP_THRESHOLD);
    }

    protected override void OnDispose()
    {
        _ripplePaint?.Dispose();
        _glowPaint?.Dispose();
        _ripples.Clear();
        _currentContext = null;
        base.OnDispose();
        _logger.Log(LogLevel.Debug, LogPrefix, "Disposed");
    }

    protected override void OnConfigurationChanged()
    {
        base.OnConfigurationChanged();

        if (Quality == RenderQuality.Low && _ripples.Count > _currentSettings.BaseMaxRipples)
        {
            int targetCount = _currentSettings.BaseMaxRipples / 2;
            if (_ripples.Count > targetCount)
            {
                _ripples.RemoveRange(targetCount, _ripples.Count - targetCount);
            }
        }
    }

    private class Ripple
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Radius { get; set; }
        public float MaxRadius { get; set; }
        public float Magnitude { get; set; }
        public float ColorHue { get; set; }
        public SKColor BaseColor { get; set; }
    }

    private record RenderContext(
        float BarWidth,
        float BarSpacing,
        int BarCount,
        SKImageInfo Info
    );
}