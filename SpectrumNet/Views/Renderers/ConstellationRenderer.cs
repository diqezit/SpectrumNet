#nullable enable

using static SpectrumNet.Views.Renderers.ConstellationRenderer.Constants;
using static SpectrumNet.Views.Renderers.ConstellationRenderer.Constants.QualityConfig;
using static System.MathF;

namespace SpectrumNet.Views.Renderers;

public sealed class ConstellationRenderer : EffectSpectrumRenderer
{
    private const string LogPrefix = nameof(ConstellationRenderer);

    private static readonly Lazy<ConstellationRenderer> _instance =
      new(() => new ConstellationRenderer());

    public static ConstellationRenderer GetInstance() => _instance.Value;

    public record Constants
    {
        public const float
          TIME_STEP = 0.016f,
          BASE_STAR_SIZE = 1.5f,
          MAX_STAR_SIZE = 12.0f,
          STAR_SIZE_RANGE = MAX_STAR_SIZE - BASE_STAR_SIZE,
          MIN_BRIGHTNESS = 0.2f,
          DEFAULT_BRIGHTNESS = 0.8f,
          BRIGHTNESS_VARIATION = 0.3f,
          TWINKLE_SPEED = 2.0f,
          FADE_IN_SPEED = 4.0f,
          MIN_LIFETIME = 3.0f,
          MAX_LIFETIME = 12.0f,
          MOVEMENT_FACTOR = 25.0f,
          SPAWN_THRESHOLD = 0.05f,
          SPECTRUM_SENSITIVITY = 18.0f,
          SMOOTHING_FACTOR = 0.1f,
          BRIGHTNESS_SPECTRUM_BOOST = 0.5f,
          ENERGY_FACTOR_BOOST = 0.8f,
          ADVANCED_EFFECTS_SIZE_MULTIPLIER = 1.5f,
          ADVANCED_EFFECTS_SPECTRUM_THRESHOLD = 0.5f,
          MIN_TOTAL_MAGNITUDE_THRESHOLD = 0.01f,
          MID_ANGLE_MULTIPLIER = 1.3f,
          HIGH_ANGLE_MULTIPLIER = 1.8f,
          ANGLE_PHASE_SHIFT_PI_OVER_2 = MathF.PI / 2f,
          MID_SPECTRUM_VELOCITY_WEIGHT = 0.8f,
          HIGH_SPECTRUM_VELOCITY_WEIGHT = 0.5f,
          DENSITY_ANGLE_MULTIPLIER_X = 1.2f,
          DENSITY_ANGLE_MULTIPLIER_Y = 1.5f,
          DENSITY_VELOCITY_WEIGHT = 0.6f,
          DIRECTION_VARIANCE_MULTIPLIER = 5f,
          OPACITY_LIFETIME_PHASE_END = 0.2f,
          OPACITY_SPECTRUM_BASE = 0.8f,
          OPACITY_SPECTRUM_BOOST = 0.4f,
          BRIGHTNESS_SPECTRUM_BASE = 1.0f,
          BRIGHTNESS_CREATE_STAR_BASE = 0.7f,
          BRIGHTNESS_CLAMP_MAX = 1.5f,
          STAR_SIZE_ENERGY_MULTIPLIER = 0.9f,
          STAR_SIZE_CLAMP_MIN = 0.7f,
          STAR_SIZE_CLAMP_MAX = 1.4f,
          TWINKLE_SPEED_BASE_RAND = 0.8f,
          TWINKLE_SPEED_RAND_RANGE = 0.4f,
          SPEED_VARIATION_BASE = 0.5f,
          SPEED_VARIATION_RAND_RANGE = 1.5f,
          SPECTRAL_SPEED_CLAMP_MIN = 0.2f,
          SPECTRAL_SPEED_CLAMP_MAX = 2.5f,
          CHAOS_FREQUENCY_BASE = 1.0f,
          CHAOS_FREQUENCY_RAND_RANGE = 3.0f;

        public const int
          DEFAULT_STAR_COUNT = 360,
          OVERLAY_STAR_COUNT = 120,
          SPAWN_RATE_PER_SECOND = 20,
          ALPHA_MIN_RENDER_THRESHOLD = 10,
          ADVANCED_EFFECTS_ALPHA_THRESHOLD = 180,
          ADVANCED_EFFECTS_ALPHA_DIVISOR = 5,
          SPAWN_MAX_PER_FRAME = 5;

        public static class QualityConfig
        {
            public const bool
              LOW_USE_ANTI_ALIAS = true, MEDIUM_USE_ANTI_ALIAS = true, HIGH_USE_ANTI_ALIAS = true;
        }
    }

    private float[]? _cachedSin;
    private float[]? _cachedCos;
    private const int CACHE_SIZE = 360;

    private Star[]? _stars;
    private float _lowSpectrum, _midSpectrum, _highSpectrum, _spectrumEnergy, _spawnAccumulator;
    private int _starCount;
    private readonly Random _random = new();

    private ConstellationRenderer()
    {
        InitializeTrigonometricCache();
    }

    private void InitializeTrigonometricCache()
    {
        _cachedSin = new float[CACHE_SIZE];
        _cachedCos = new float[CACHE_SIZE];
        for (int i = 0; i < CACHE_SIZE; i++)
        {
            float angle = i * (2 * MathF.PI / CACHE_SIZE);
            _cachedSin[i] = Sin(angle);
            _cachedCos[i] = Cos(angle);
        }
    }

    private static int GetCachedTrigonometricIndex(float angle) =>
        (int)(angle * CACHE_SIZE / (2 * MathF.PI)) % CACHE_SIZE switch
        {
            var index when index < 0 => index + CACHE_SIZE,
            var index => index
        };

    private float FastSin(float angle) => _cachedSin![GetCachedTrigonometricIndex(angle)];
    private float FastCos(float angle) => _cachedCos![GetCachedTrigonometricIndex(angle)];

    protected override void OnInitialize()
    {
        base.OnInitialize();
        ResetAndInitializeStars();
        _logger.Log(LogLevel.Debug, LogPrefix, "Initialized");
    }

    protected override void OnConfigurationChanged()
    {
        _starCount = _isOverlayActive ? OVERLAY_STAR_COUNT : DEFAULT_STAR_COUNT;
        ResetAndInitializeStars();
        _logger.Log(LogLevel.Debug, LogPrefix, $"Configuration changed. Quality: {Quality}, Stars: {_starCount}");
    }

    private void ApplyQualityPreset(
      bool useAA,
      bool useAdvFx)
    {
        _useAntiAlias = useAA;
        _useAdvancedEffects = useAdvFx;
    }

    protected override void OnQualitySettingsApplied()
    {
        switch (Quality)
        {
            case RenderQuality.Low: ApplyQualityPreset(LOW_USE_ANTI_ALIAS, false); break;
            case RenderQuality.Medium: ApplyQualityPreset(MEDIUM_USE_ANTI_ALIAS, true); break;
            case RenderQuality.High: ApplyQualityPreset(HIGH_USE_ANTI_ALIAS, true); break;
        }
        _logger.Log(LogLevel.Debug, LogPrefix, $"Quality settings applied: {Quality}");
    }

    protected override void RenderEffect(
      SKCanvas canvas,
      float[] spectrum,
      SKImageInfo info,
      float barWidth,
      float barSpacing,
      int barCount,
      SKPaint paint) =>
      _logger.Safe(() =>
      {
          UpdateSimulationState(spectrum, info, TIME_STEP);
          RenderStarfield(canvas, info);
      },
        LogPrefix,
        "Error during rendering constellatiion effect");

    private void UpdateSimulationState(
      float[] spectrum,
      SKImageInfo info,
      float deltaTime)
    {
        UpdateSpectrumAnalysis(spectrum);
        UpdateAllStarsState(deltaTime, info);
        TrySpawnNewStars(info);
    }

    private void UpdateSpectrumAnalysis(float[] spectrum)
    {
        if (!_spectrumSemaphore.Wait(0)) return;
        try
        {
            if (spectrum.Length == 0)
            {
                ResetSpectrumValues();
                return;
            }
            if (spectrum.Length < 3)
            {
                CalculateSimpleAverageSpectrum(spectrum);
                return;
            }
            CalculateDetailedSpectrumValues(spectrum);
        }
        finally
        {
            _spectrumSemaphore.Release();
        }
    }

    private void ResetSpectrumValues() =>
        _lowSpectrum = _midSpectrum = _highSpectrum = _spectrumEnergy = 0f;

    private void CalculateSimpleAverageSpectrum(float[] spectrum)
    {
        float sum = 0f;
        for (int k = 0; k < spectrum.Length; k++)
        {
            sum += spectrum[k];
        }
        float avg = sum / (spectrum.Length > 0 ? spectrum.Length : 1f);
        _lowSpectrum = avg;
        _midSpectrum = avg;
        _highSpectrum = avg;
        _spectrumEnergy = avg;
    }

    private void CalculateDetailedSpectrumValues(float[] spectrum)
    {
        var (lowAvg, midAvg, highAvg) = GetAverageBandValues(spectrum);
        ApplySmoothingToSpectrum(lowAvg, midAvg, highAvg);
        CalculateOverallSpectrumEnergy();
    }

    private static (float low, float mid, float high) GetAverageBandValues(float[] spectrum)
    {
        int bandLength = spectrum.Length / 3;
        if (bandLength == 0) return (0, 0, 0);

        float lowSum = 0f, midSum = 0f, highSum = 0f;
        int i = 0;
        for (; i < bandLength; i++) lowSum += spectrum[i];
        for (; i < 2 * bandLength; i++) midSum += spectrum[i];
        int highBandActualLength = spectrum.Length - (2 * bandLength);
        for (; i < spectrum.Length; i++) highSum += spectrum[i];

        return (
          lowSum / bandLength,
          midSum / bandLength,
          highBandActualLength > 0 ? highSum / highBandActualLength : 0f
        );
    }

    private void ApplySmoothingToSpectrum(
      float lowAvg,
      float midAvg,
      float highAvg)
    {
        _lowSpectrum = Lerp(_lowSpectrum, lowAvg, SMOOTHING_FACTOR);
        _midSpectrum = Lerp(_midSpectrum, midAvg, SMOOTHING_FACTOR);
        _highSpectrum = Lerp(_highSpectrum, highAvg, SMOOTHING_FACTOR);
    }
    private static float Lerp(float current, float target, float amount) =>
        current * (1f - amount) + target * amount;

    private void CalculateOverallSpectrumEnergy() =>
        _spectrumEnergy = (_lowSpectrum + _midSpectrum + _highSpectrum) / 3f;

    private void UpdateAllStarsState(
      float deltaTime,
      SKImageInfo info)
    {
        if (_stars == null) return;
        AccumulateSpawnEnergy(deltaTime);
        ProcessEachStar(deltaTime, info);
    }

    private void AccumulateSpawnEnergy(float deltaTime) =>
      _spawnAccumulator += _spectrumEnergy * SPAWN_RATE_PER_SECOND * deltaTime;

    private void ProcessEachStar(
      float deltaTime,
      SKImageInfo info)
    {
        if (_stars == null) return;
        for (int i = 0; i < _stars.Length; i++)
        {
            UpdateSingleStar(ref _stars[i], deltaTime, info);
        }
    }

    private void UpdateSingleStar(
        ref Star star,
        float deltaTime,
        SKImageInfo info)
    {
        if (UpdateStarLifetime(ref star, deltaTime))
        {
            UpdateStarMovement(ref star, deltaTime, info);
            UpdateStarVisuals(ref star);
        }
    }

    private static bool UpdateStarLifetime(ref Star star, float deltaTime)
    {
        if (!star.IsActive) return false;
        star.Lifetime -= deltaTime;
        return star.Lifetime > 0;
    }

    private void UpdateStarMovement(
      ref Star star,
      float deltaTime,
      SKImageInfo info)
    {
        float totalMagnitude = _lowSpectrum + _midSpectrum + _highSpectrum;
        if (totalMagnitude >= MIN_TOTAL_MAGNITUDE_THRESHOLD)
        {
            var (velocityX, velocityY) = CalculateTotalStarVelocity(in star, deltaTime, totalMagnitude);
            ApplyVelocityToStar(ref star, velocityX, velocityY);
        }
        ClampStarPosition(ref star, info.Width, info.Height);
    }

    private static void ApplyVelocityToStar(ref Star star, float velocityX, float velocityY)
    {
        star.X += velocityX;
        star.Y += velocityY;
    }

    private (float velX, float velY) CalculateTotalStarVelocity(
      in Star star,
      float deltaTime,
      float totalMagnitude)
    {
        float spectralDensity = totalMagnitude * SPECTRUM_SENSITIVITY;
        float time = _time;

        var bassVel = CalculateBassVelocity(time, star.ChaosFrequencyX);
        var midVel = CalculateMidVelocity(time, star.ChaosFrequencyY);
        var highVel = CalculateHighVelocity(time, star.TwinkleFactor);
        var densityVel = CalculateDensityVelocity(time, star.TwinkleFactor, spectralDensity);
        var directionVel = CalculateDirectionVelocity(
            time,
            star.ChaosFrequencyX,
            star.ChaosFrequencyY);

        float totalVelocityX = bassVel.velX
                             + midVel.velX
                             + highVel.velX
                             + densityVel.velX
                             + directionVel.velX;
        float totalVelocityY = bassVel.velY
                             + midVel.velY
                             + highVel.velY
                             + densityVel.velY
                             + directionVel.velY;
        float movementScale = MOVEMENT_FACTOR * star.SpeedFactor * deltaTime;

        return (totalVelocityX * movementScale, totalVelocityY * movementScale);
    }

    private (float velX, float velY) CalculateBassVelocity(float time, float chaosFrequencyX)
    {
        float bassAngle = time * chaosFrequencyX;
        float bassX = _lowSpectrum * FastSin(bassAngle);
        float bassY = _lowSpectrum * FastCos(bassAngle);
        return (bassX, bassY);
    }

    private (float velX, float velY) CalculateMidVelocity(float time, float chaosFrequencyY)
    {
        float midAngle = time * chaosFrequencyY * MID_ANGLE_MULTIPLIER;
        float midX = _midSpectrum * FastSin(midAngle) * MID_SPECTRUM_VELOCITY_WEIGHT;
        float midY = _midSpectrum * FastCos(midAngle) * MID_SPECTRUM_VELOCITY_WEIGHT;
        return (midX, midY);
    }

    private (float velX, float velY) CalculateHighVelocity(float time, float twinkleFactor)
    {
        float highAngle = time * twinkleFactor * HIGH_ANGLE_MULTIPLIER;
        float highX = _highSpectrum * FastSin(highAngle) * HIGH_SPECTRUM_VELOCITY_WEIGHT;
        float highY = _highSpectrum * FastCos(highAngle + ANGLE_PHASE_SHIFT_PI_OVER_2)
                                  * HIGH_SPECTRUM_VELOCITY_WEIGHT;
        return (highX, highY);
    }

    private (float velX, float velY) CalculateDensityVelocity(float time, float twinkleFactor, float spectralDensity)
    {
        float densityX = FastSin(time * DENSITY_ANGLE_MULTIPLIER_X + twinkleFactor) * spectralDensity * DENSITY_VELOCITY_WEIGHT;
        float densityY = FastCos(time * DENSITY_ANGLE_MULTIPLIER_Y + twinkleFactor) * spectralDensity * DENSITY_VELOCITY_WEIGHT;
        return (densityX, densityY);
    }

    private (float velX, float velY) CalculateDirectionVelocity(
        float time,
        float chaosFrequencyX,
        float chaosFrequencyY)
    {
        float directionVariance = (_highSpectrum - _lowSpectrum) * DIRECTION_VARIANCE_MULTIPLIER;
        float directionX = FastSin(time + chaosFrequencyX) * directionVariance;
        float directionY = FastCos(time + chaosFrequencyY) * directionVariance;
        return (directionX, directionY);
    }

    private static void ClampStarPosition(
      ref Star star,
      float canvasWidth,
      float canvasHeight)
    {
        star.X = Clamp(star.X, 0, canvasWidth);
        star.Y = Clamp(star.Y, 0, canvasHeight);
    }

    private void UpdateStarVisuals(ref Star star)
    {
        UpdateStarOpacity(ref star);
        UpdateStarBrightnessValue(ref star);
    }

    private void UpdateStarOpacity(ref Star star)
    {
        float baseOpacity = CalculateBaseOpacity(in star);
        star.Opacity = Clamp(baseOpacity * (OPACITY_SPECTRUM_BASE + _spectrumEnergy * OPACITY_SPECTRUM_BOOST), 0f, 2f);
    }

    private static float CalculateBaseOpacity(in Star star) =>
        star.Lifetime / star.MaxLifetime < OPACITY_LIFETIME_PHASE_END
            ? star.Lifetime / OPACITY_LIFETIME_PHASE_END
            : MathF.Min(1.0f, star.Opacity + FADE_IN_SPEED * TIME_STEP);

    private void UpdateStarBrightnessValue(ref Star star)
    {
        float baseBrightness = DEFAULT_BRIGHTNESS
                           * (_spectrumEnergy * BRIGHTNESS_SPECTRUM_BOOST
                            + BRIGHTNESS_SPECTRUM_BASE);
        float twinkleAdjustment = CalculateTwinkleAdjustment(in star);
        star.Brightness = Clamp(baseBrightness + twinkleAdjustment, MIN_BRIGHTNESS, BRIGHTNESS_CLAMP_MAX);
    }

    private float CalculateTwinkleAdjustment(in Star star)
    {
        float twinklePhase = _time * TWINKLE_SPEED * star.TwinkleSpeed;
        return FastSin(twinklePhase + star.TwinkleFactor) * BRIGHTNESS_VARIATION;
    }

    private void TrySpawnNewStars(
      SKImageInfo info)
    {
        if (_stars == null) return;
        if (_spawnAccumulator >= 1.0f && _spectrumEnergy >= SPAWN_THRESHOLD)
        {
            int starsToSpawn = DetermineStarsToSpawn();
            if (starsToSpawn > 0)
            {
                _spawnAccumulator -= starsToSpawn;
                SpawnStars(starsToSpawn, info);
            }
        }
    }

    private int DetermineStarsToSpawn() =>
        Min((int)_spawnAccumulator, SPAWN_MAX_PER_FRAME);

    private void SpawnStars(
      int count,
      SKImageInfo info)
    {
        if (_stars == null) return;
        for (int i = 0; i < count; i++)
        {
            int freeIndex = FindNextAvailableStarSlot();
            if (freeIndex == -1) break;
            InitializeNewStar(ref _stars[freeIndex], info);
        }
    }

    private int FindNextAvailableStarSlot()
    {
        if (_stars == null) return -1;
        for (int i = 0; i < _stars.Length; i++)
        {
            if (!_stars[i].IsActive) return i;
        }
        return -1;
    }

    private float GetRandomFloat() => (float)_random.NextDouble();

    private static float GetRandomFloatInRange(float min, float max) =>
        min + (float)new Random().NextDouble() * (max - min);

    private void InitializeNewStar(
        ref Star star,
        SKImageInfo info)
    {
        float energyFactor = 1f + _spectrumEnergy * ENERGY_FACTOR_BOOST;

        star.X = GetRandomFloat() * info.Width;
        star.Y = GetRandomFloat() * info.Height;
        star.Lifetime = CalculateStarLifetime(energyFactor);
        star.MaxLifetime = star.Lifetime;
        star.Size = CalculateStarSize(energyFactor);
        star.Brightness = CalculateInitialStarBrightness();
        star.TwinkleFactor = GetRandomFloat() * MathF.PI * 2f;
        star.TwinkleSpeed = CalculateStarTwinkleSpeed(energyFactor);
        star.Color = GenerateRandomStarColor();
        star.IsActive = true;
        star.Opacity = 0.01f;
        star.SpeedFactor = CalculateStarSpeedFactor(energyFactor);
        star.ChaosFrequencyX = GetRandomFloatInRange(CHAOS_FREQUENCY_BASE, CHAOS_FREQUENCY_BASE + CHAOS_FREQUENCY_RAND_RANGE);
        star.ChaosFrequencyY = GetRandomFloatInRange(CHAOS_FREQUENCY_BASE, CHAOS_FREQUENCY_BASE + CHAOS_FREQUENCY_RAND_RANGE);
    }

    private static float CalculateStarLifetime(float energyFactor) =>
        GetRandomFloatInRange(MIN_LIFETIME, MAX_LIFETIME) * energyFactor;

    private float CalculateStarSize(float energyFactor)
    {
        float sizeEnergyMultiplier = Clamp(energyFactor * STAR_SIZE_ENERGY_MULTIPLIER, STAR_SIZE_CLAMP_MIN, STAR_SIZE_CLAMP_MAX);
        return (BASE_STAR_SIZE + GetRandomFloat() * STAR_SIZE_RANGE) * sizeEnergyMultiplier;
    }

    private float CalculateInitialStarBrightness() =>
        DEFAULT_BRIGHTNESS * (_spectrumEnergy * BRIGHTNESS_SPECTRUM_BOOST + BRIGHTNESS_SPECTRUM_BASE);

    private float CalculateStarTwinkleSpeed(float energyFactor) =>
        (TWINKLE_SPEED_BASE_RAND + GetRandomFloat() * TWINKLE_SPEED_RAND_RANGE) * energyFactor;

    private static float CalculateStarSpeedFactor(float energyFactor) =>
        Clamp(energyFactor * GetRandomFloatInRange(
            SPEED_VARIATION_BASE,
            SPEED_VARIATION_BASE + SPEED_VARIATION_RAND_RANGE),
                SPECTRAL_SPEED_CLAMP_MIN,
                SPECTRAL_SPEED_CLAMP_MAX);

    private SKColor GenerateRandomStarColor()
    {
        byte RndByte(int baseVal, int range) => (byte)(baseVal + _random.Next(range));
        if (_lowSpectrum > _midSpectrum && _lowSpectrum > _highSpectrum)
            return new SKColor(RndByte(200, 55), RndByte(100, 100), RndByte(100, 100));
        if (_highSpectrum > _lowSpectrum && _highSpectrum > _midSpectrum)
            return new SKColor(RndByte(100, 100), RndByte(100, 100), RndByte(200, 55));
        return new SKColor(RndByte(150, 105), RndByte(150, 105), RndByte(150, 105));
    }

    private void RenderStarfield(
      SKCanvas canvas,
      SKImageInfo _)
    {
        if (_stars == null) return;
        using var starPaint = _paintPool.Get();
        starPaint.IsAntialias = _useAntiAlias;

        foreach (var star in _stars)
        {
            if (ShouldRenderStar(in star))
            {
                RenderSingleStar(canvas, in star, starPaint);
            }
        }
    }

    private static bool ShouldRenderStar(in Star star) => star.IsActive && CalculateFinalRenderAlpha(in star) >= ALPHA_MIN_RENDER_THRESHOLD;

    private void RenderSingleStar(
      SKCanvas canvas,
      in Star star,
      SKPaint starPaint)
    {
        if (Quality == RenderQuality.Low)
        {
            DrawSimpleStar(canvas, in star, starPaint);
        }
        else
        {
            DrawDetailedStar(canvas, in star, starPaint);
        }
    }

    private static void DrawSimpleStar(
      SKCanvas canvas,
      in Star star,
      SKPaint paint)
    {
        int finalAlpha = CalculateFinalRenderAlpha(in star);
        paint.Color = star.Color.WithAlpha((byte)finalAlpha);
        canvas.DrawCircle(star.X, star.Y, star.Size, paint);
    }

    private static int CalculateFinalRenderAlpha(in Star star) => Clamp((int)(255 * star.Brightness * star.Opacity), 0, 255);
    private float CalculateDynamicRenderSize(in Star star) => star.Size * (1f + _spectrumEnergy * OPACITY_SPECTRUM_BOOST);

    private void DrawDetailedStar(
      SKCanvas canvas,
      in Star star,
      SKPaint starPaint)
    {
        float dynamicSize = CalculateDynamicRenderSize(in star);
        int finalAlpha = CalculateFinalRenderAlpha(in star);
        SKColor baseColor = star.Color.WithAlpha((byte)finalAlpha);

        RenderStarCore(canvas, star.X, star.Y, dynamicSize, baseColor, starPaint);
        MaybeRenderAdvancedEffects(canvas, in star, dynamicSize, finalAlpha, starPaint);
    }

    private static void RenderStarCore(
      SKCanvas canvas,
      float x,
      float y,
      float size,
      SKColor color,
      SKPaint paint)
    {
        paint.Color = color;
        canvas.DrawCircle(x, y, size, paint);
    }

    private void MaybeRenderAdvancedEffects(
      SKCanvas canvas,
      in Star star,
      float dynamicSize,
      int finalAlpha,
      SKPaint starPaint)
    {
        if (_useAdvancedEffects && finalAlpha > ADVANCED_EFFECTS_ALPHA_THRESHOLD && _spectrumEnergy > ADVANCED_EFFECTS_SPECTRUM_THRESHOLD)
        {
            starPaint.Color = star.Color.WithAlpha(
              (byte)Clamp(finalAlpha / ADVANCED_EFFECTS_ALPHA_DIVISOR, 0, 255));
            canvas.DrawCircle(star.X, star.Y, dynamicSize * ADVANCED_EFFECTS_SIZE_MULTIPLIER, starPaint);
        }
    }

    private void ResetAndInitializeStars()
    {
        _stars = new Star[_starCount];
        for (int i = 0; i < _starCount; i++)
            _stars[i] = new Star { IsActive = false };
    }

    private struct Star
    {
        public float X, Y, Size, Brightness, TwinkleFactor, TwinkleSpeed, Lifetime, MaxLifetime, Opacity, SpeedFactor, ChaosFrequencyX, ChaosFrequencyY;
        public SKColor Color;
        public bool IsActive;
    }

    protected override void OnDispose()
    {
        _stars = null;
        base.OnDispose();
        _logger.Log(LogLevel.Debug, LogPrefix, "Disposed");
    }
}