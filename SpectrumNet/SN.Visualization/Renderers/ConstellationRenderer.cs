#nullable enable

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class ConstellationRenderer : EffectSpectrumRenderer<ConstellationRenderer.QualitySettings>
{
    private static readonly Lazy<ConstellationRenderer> _instance =
        new(() => new ConstellationRenderer());

    public static ConstellationRenderer GetInstance() => _instance.Value;

    private const float BASE_STAR_SIZE = 1.5f,
        MAX_STAR_SIZE = 12f,
        MIN_BRIGHTNESS = 0.2f,
        TWINKLE_SPEED = 2f,
        MOVEMENT_FACTOR = 25f,
        SPAWN_THRESHOLD = 0.05f,
        SPECTRUM_SENSITIVITY = 18f,
        STAR_LIFETIME_MIN = 5f,
        STAR_LIFETIME_MAX = 15f,
        STAR_SPEED_MIN = 0.5f,
        STAR_SPEED_MAX = 2f,
        TWINKLE_SPEED_MIN = 0.8f,
        TWINKLE_SPEED_MAX = 0.4f,
        TWINKLE_AMPLITUDE = 0.3f,
        ENERGY_BRIGHTNESS_FACTOR = 0.5f,
        ENERGY_SIZE_FACTOR = 0.5f,
        FADE_IN_DURATION = 2f,
        FADE_OUT_DURATION = 2f,
        GLOW_ALPHA_THRESHOLD = 180,
        GLOW_ALPHA_DIVISOR = 5,
        MIN_ALPHA_THRESHOLD = 10,
        SPAWN_RATE_BASE = 5f,
        SPAWN_RATE_ENERGY_MULTIPLIER = 15f,
        DELTA_TIME = 0.016f;

    private const int DEFAULT_STAR_COUNT = 360,
        OVERLAY_STAR_COUNT = 120,
        MAX_SPAWN_PER_FRAME = 3,
        SPECTRUM_BANDS = 3;

    private static readonly SKColor[] _lowSpectrumColors =
        [new(200, 100, 100), new(255, 200, 200)];
    private static readonly SKColor[] _highSpectrumColors =
        [new(100, 100, 200), new(200, 200, 255)];
    private static readonly SKColor[] _neutralColors =
        [new(150, 150, 150), new(255, 255, 255)];

    private Star[] _stars = [];
    private float _spawnAccumulator;
    private float _lowSpectrum;
    private float _midSpectrum;
    private float _highSpectrum;
    private float _energy;
    private readonly Random _random = new();

    public sealed class QualitySettings
    {
        public bool UseGlow { get; init; }
        public float GlowSize { get; init; }
        public float SmoothingFactor { get; init; }
    }

    protected override IReadOnlyDictionary<RenderQuality, QualitySettings>
        QualitySettingsPresets
    { get; } = new Dictionary<RenderQuality, QualitySettings>
    {
        [RenderQuality.Low] = new()
        {
            UseGlow = false,
            GlowSize = 1f,
            SmoothingFactor = 0.4f
        },
        [RenderQuality.Medium] = new()
        {
            UseGlow = true,
            GlowSize = 1.3f,
            SmoothingFactor = 0.3f
        },
        [RenderQuality.High] = new()
        {
            UseGlow = true,
            GlowSize = 1.5f,
            SmoothingFactor = 0.25f
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
            SPECTRUM_BANDS,
            applyTemporalSmoothing: true);

        if (!isValid || processedSpectrum == null)
            return;

        var starData = CalculateStarData(processedSpectrum, info);

        if (!ValidateStarData(starData))
            return;

        RenderStarVisualization(
            canvas,
            starData,
            renderParams,
            passedInPaint);
    }

    private StarData CalculateStarData(float[] spectrum, SKImageInfo info)
    {
        UpdateSpectrumValues(spectrum);
        UpdateStars(info);

        return new StarData(
            ActiveStars: CollectActiveStars(),
            Energy: _energy);
    }

    private static bool ValidateStarData(StarData data) =>
        data.ActiveStars.Count > 0;

    private void RenderStarVisualization(
        SKCanvas canvas,
        StarData data,
        RenderParameters renderParams,
        SKPaint basePaint)
    {
        var settings = CurrentQualitySettings!;

        RenderWithOverlay(canvas, () =>
        {
            if (UseAdvancedEffects && settings.UseGlow)
                RenderGlowLayer(canvas, data, basePaint, settings);

            RenderStarLayer(canvas, data, basePaint);
        });
    }

    private void UpdateSpectrumValues(float[] spectrum)
    {
        _lowSpectrum = spectrum.Length > 0 ? spectrum[0] : 0f;
        _midSpectrum = spectrum.Length > 1 ? spectrum[1] : 0f;
        _highSpectrum = spectrum.Length > 2 ? spectrum[2] : 0f;
        _energy = CalculateAverageEnergy();
    }

    private float CalculateAverageEnergy() =>
        (_lowSpectrum + _midSpectrum + _highSpectrum) / 3f;

    private void UpdateStars(SKImageInfo info)
    {
        UpdateSpawnAccumulator();
        UpdateExistingStars(info);
        TrySpawnNewStars(info);
    }

    private void UpdateSpawnAccumulator() =>
        _spawnAccumulator += CalculateSpawnRate() * DELTA_TIME;

    private float CalculateSpawnRate() =>
        SPAWN_RATE_BASE + _energy * SPAWN_RATE_ENERGY_MULTIPLIER;

    private void UpdateExistingStars(SKImageInfo info)
    {
        for (int i = 0; i < _stars.Length; i++)
        {
            ref var star = ref _stars[i];
            if (!star.Active) continue;

            UpdateStarLifetime(ref star);
            if (!star.Active) continue;

            UpdateStarPosition(ref star, info);
            UpdateStarVisuals(ref star);
        }
    }

    private static void UpdateStarLifetime(ref Star star)
    {
        star.Lifetime -= DELTA_TIME;
        star.AnimationTime += DELTA_TIME;

        if (star.Lifetime <= 0)
            star.Active = false;
    }

    private void UpdateStarPosition(ref Star star, SKImageInfo info)
    {
        float angle = star.AnimationTime * star.Speed;
        var velocity = CalculateVelocity(angle);

        star.X = ClampPosition(star.X + velocity.X * DELTA_TIME * MOVEMENT_FACTOR, info.Width);
        star.Y = ClampPosition(star.Y + velocity.Y * DELTA_TIME * MOVEMENT_FACTOR, info.Height);
    }

    private (float X, float Y) CalculateVelocity(float angle) =>
        (CalculateVelocityX(angle) * SPECTRUM_SENSITIVITY,
         CalculateVelocityY(angle) * SPECTRUM_SENSITIVITY);

    private float CalculateVelocityX(float angle) =>
        _lowSpectrum * MathF.Sin(angle) +
        _midSpectrum * MathF.Cos(angle * 1.3f) +
        _highSpectrum * MathF.Sin(angle * 1.8f);

    private float CalculateVelocityY(float angle) =>
        _lowSpectrum * MathF.Cos(angle) +
        _midSpectrum * MathF.Sin(angle * 1.3f) +
        _highSpectrum * MathF.Cos(angle * 1.8f);

    private static float ClampPosition(float position, float max) =>
        Clamp(position, 0, max);

    private void UpdateStarVisuals(ref Star star)
    {
        star.Opacity = CalculateStarOpacity(star);
        star.Brightness = CalculateStarBrightness(star, _energy);
    }

    private static float CalculateStarOpacity(in Star star)
    {
        float fadeInRatio = star.AnimationTime / FADE_IN_DURATION;
        float fadeOutRatio = star.Lifetime / FADE_OUT_DURATION;

        return MathF.Min(MathF.Min(fadeInRatio, fadeOutRatio), 1f);
    }

    private static float CalculateStarBrightness(in Star star, float energy)
    {
        float twinkle = CalculateTwinkle(star);
        return Clamp(0.8f + twinkle + energy * ENERGY_BRIGHTNESS_FACTOR, MIN_BRIGHTNESS, 1.5f);
    }

    private static float CalculateTwinkle(in Star star) =>
        MathF.Sin(star.AnimationTime * TWINKLE_SPEED * star.TwinkleSpeed + star.Phase) * TWINKLE_AMPLITUDE;

    private void TrySpawnNewStars(SKImageInfo info)
    {
        if (!CanSpawnStars()) return;

        int toSpawn = CalculateSpawnCount();
        _spawnAccumulator -= toSpawn;

        SpawnStars(toSpawn, info);
    }

    private bool CanSpawnStars() =>
        _spawnAccumulator >= 1f && _energy >= SPAWN_THRESHOLD;

    private int CalculateSpawnCount() =>
        Math.Min((int)_spawnAccumulator, MAX_SPAWN_PER_FRAME);

    private void SpawnStars(int count, SKImageInfo info)
    {
        for (int i = 0; i < count; i++)
        {
            int index = FindInactiveStar();
            if (index >= 0)
                InitializeStar(ref _stars[index], info);
        }
    }

    private int FindInactiveStar()
    {
        for (int i = 0; i < _stars.Length; i++)
            if (!_stars[i].Active)
                return i;
        return -1;
    }

    private void InitializeStar(ref Star star, SKImageInfo info)
    {
        star.Active = true;
        star.X = GenerateRandomPosition(info.Width);
        star.Y = GenerateRandomPosition(info.Height);
        star.Lifetime = star.MaxLifetime = GenerateStarLifetime();
        star.Size = GenerateStarSize();
        star.Speed = GenerateStarSpeed();
        star.TwinkleSpeed = GenerateTwinkleSpeed();
        star.Phase = GeneratePhase();
        star.Opacity = 0f;
        star.Brightness = 0.8f;
        star.AnimationTime = 0f;
        star.Color = GetStarColor();
    }

    private float GenerateRandomPosition(float max) =>
        _random.NextSingle() * max;

    private float GenerateStarLifetime() =>
        STAR_LIFETIME_MIN + _random.NextSingle() * (STAR_LIFETIME_MAX - STAR_LIFETIME_MIN);

    private float GenerateStarSize() =>
        BASE_STAR_SIZE + _random.NextSingle() * (MAX_STAR_SIZE - BASE_STAR_SIZE);

    private float GenerateStarSpeed() =>
        STAR_SPEED_MIN + _random.NextSingle() * STAR_SPEED_MAX;

    private float GenerateTwinkleSpeed() =>
        TWINKLE_SPEED_MIN + _random.NextSingle() * TWINKLE_SPEED_MAX;

    private float GeneratePhase() =>
        _random.NextSingle() * MathF.Tau;

    private SKColor GetStarColor()
    {
        if (IsLowSpectrumDominant())
            return GenerateColorFromPalette(_lowSpectrumColors);

        if (IsHighSpectrumDominant())
            return GenerateColorFromPalette(_highSpectrumColors);

        return GenerateColorFromPalette(_neutralColors);
    }

    private bool IsLowSpectrumDominant() =>
        _lowSpectrum > MathF.Max(_midSpectrum, _highSpectrum);

    private bool IsHighSpectrumDominant() =>
        _highSpectrum > MathF.Max(_lowSpectrum, _midSpectrum);

    private SKColor GenerateColorFromPalette(SKColor[] palette) =>
        InterpolateColor(palette, _random.NextSingle());

    private static SKColor InterpolateColor(SKColor[] colors, float t)
    {
        if (colors.Length < 2) return colors[0];

        var from = colors[0];
        var to = colors[1];

        return new SKColor(
            (byte)Lerp(from.Red, to.Red, t),
            (byte)Lerp(from.Green, to.Green, t),
            (byte)Lerp(from.Blue, to.Blue, t));
    }

    private List<ActiveStar> CollectActiveStars()
    {
        var activeStars = new List<ActiveStar>(_stars.Length);

        foreach (ref readonly var star in _stars.AsSpan())
        {
            if (!IsStarVisible(star)) continue;

            byte alpha = CalculateStarAlpha(star);
            if (alpha < MIN_ALPHA_THRESHOLD) continue;

            activeStars.Add(CreateActiveStar(star, alpha));
        }

        return activeStars;
    }

    private static bool IsStarVisible(in Star star) =>
        star.Active;

    private byte CalculateStarAlpha(in Star star) =>
        CalculateAlpha(star.Brightness * star.Opacity);

    private static ActiveStar CreateActiveStar(in Star star, byte alpha) =>
        new(
            Position: new SKPoint(star.X, star.Y),
            Size: star.Size,
            Color: star.Color,
            Alpha: alpha);

    private void RenderGlowLayer(
        SKCanvas canvas,
        StarData data,
        SKPaint basePaint,
        QualitySettings settings)
    {
        var glowStars = FilterGlowStars(data.ActiveStars);
        if (glowStars.Count == 0) return;

        var glowPaint = CreatePaint(basePaint.Color, SKPaintStyle.Fill);

        try
        {
            RenderGlowStars(canvas, glowStars, glowPaint, settings.GlowSize);
        }
        finally
        {
            ReturnPaint(glowPaint);
        }
    }

    private static List<ActiveStar> FilterGlowStars(List<ActiveStar> stars)
    {
        var glowStars = new List<ActiveStar>();
        foreach (var star in stars)
            if (star.Alpha >= GLOW_ALPHA_THRESHOLD)
                glowStars.Add(star);
        return glowStars;
    }

    private static void RenderGlowStars(SKCanvas canvas,
        List<ActiveStar> stars,
        SKPaint paint,
        float glowSize)
    {
        foreach (var star in stars)
        {
            byte glowAlpha = CalculateGlowAlpha(star.Alpha);
            paint.Color = star.Color.WithAlpha(glowAlpha);

            float glowRadius = star.Size * glowSize;
            canvas.DrawCircle(star.Position, glowRadius, paint);
        }
    }

    private static byte CalculateGlowAlpha(byte starAlpha) =>
        (byte)(starAlpha / GLOW_ALPHA_DIVISOR);

    private void RenderStarLayer(
        SKCanvas canvas,
        StarData data,
        SKPaint basePaint)
    {
        var starPaint = CreatePaint(basePaint.Color, SKPaintStyle.Fill);

        try
        {
            RenderStars(canvas, data.ActiveStars, starPaint, data.Energy);
        }
        finally
        {
            ReturnPaint(starPaint);
        }
    }

    private static void RenderStars(
        SKCanvas canvas,
        List<ActiveStar> stars,
        SKPaint paint,
        float energy)
    {
        float sizeFactor = CalculateSizeFactor(energy);

        foreach (var star in stars)
        {
            paint.Color = star.Color.WithAlpha(star.Alpha);
            float adjustedSize = star.Size * sizeFactor;
            canvas.DrawCircle(star.Position, adjustedSize, paint);
        }
    }

    private static float CalculateSizeFactor(float energy) =>
        1f + energy * ENERGY_SIZE_FACTOR;

    private void InitializeStars()
    {
        int count = GetStarCount();
        _stars = new Star[count];
        ResetState();
    }

    private int GetStarCount() =>
        IsOverlayActive ? OVERLAY_STAR_COUNT : DEFAULT_STAR_COUNT;

    private void ResetState()
    {
        _spawnAccumulator = 0f;
        _lowSpectrum = 0f;
        _midSpectrum = 0f;
        _highSpectrum = 0f;
        _energy = 0f;
    }

    protected override int GetMaxBarsForQuality() => Quality switch
    {
        RenderQuality.Low => 32,
        RenderQuality.Medium => 64,
        RenderQuality.High => 128,
        _ => 64
    };

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();

        ApplyQualitySettings();

        if (ShouldReinitializeStars())
            InitializeStars();

        RequestRedraw();
    }

    private void ApplyQualitySettings() =>
        SetProcessingSmoothingFactor(CurrentQualitySettings!.SmoothingFactor);

    private bool ShouldReinitializeStars() =>
        _stars.Length == 0 || IsStarCountMismatch();

    private bool IsStarCountMismatch() =>
        (IsOverlayActive && _stars.Length != OVERLAY_STAR_COUNT) ||
        (!IsOverlayActive && _stars.Length != DEFAULT_STAR_COUNT);

    protected override void OnDispose()
    {
        _stars = [];
        ResetState();
        base.OnDispose();
    }

    private record StarData(
        List<ActiveStar> ActiveStars,
        float Energy);

    private record ActiveStar(
        SKPoint Position,
        float Size,
        SKColor Color,
        byte Alpha);

    private struct Star
    {
        public float X, Y, Size, Brightness, Speed, TwinkleSpeed,
                     Lifetime, MaxLifetime, Opacity, Phase, AnimationTime;
        public SKColor Color;
        public bool Active;
    }
}