#nullable enable

using static SpectrumNet.Views.Renderers.ConstellationRenderer.Constants;
using static SpectrumNet.Views.Renderers.ConstellationRenderer.Constants.Quality;
using static System.MathF;

namespace SpectrumNet.Views.Renderers;

public sealed class ConstellationRenderer : EffectSpectrumRenderer
{
    private static readonly Lazy<ConstellationRenderer> _instance =
        new(() => new ConstellationRenderer());

    private ConstellationRenderer() { }

    public static ConstellationRenderer GetInstance() => _instance.Value;

    public record Constants
    {
        public const string LOG_PREFIX = "ConstellationRenderer";

        public const float
            TIME_STEP = 0.016f,
            BASE_STAR_SIZE = 1.5f,
            MAX_STAR_SIZE = 12.0f,
            STAR_SIZE_RANGE = MAX_STAR_SIZE - BASE_STAR_SIZE,
            MIN_BRIGHTNESS = 0.2f,
            DEFAULT_BRIGHTNESS = 0.8f,
            BRIGHTNESS_VARIATION = 0.4f,
            TWINKLE_SPEED = 3.0f,
            FADE_IN_SPEED = 4.0f,
            FADE_OUT_SPEED = 2.5f,
            MIN_LIFETIME = 3.0f,
            MAX_LIFETIME = 12.0f,
            MOVEMENT_FACTOR = 25.0f,
            SPAWN_THRESHOLD = 0.05f,
            GLOW_THRESHOLD = 0.3f,
            SPECTRUM_SENSITIVITY = 18.0f;

        public const int
            DEFAULT_STAR_COUNT = 180,
            OVERLAY_STAR_COUNT = 120,
            SPAWN_RATE_PER_SECOND = 30,
            RENDERER_BATCH_SIZE = 96;

        public const byte
            MIN_STAR_COLOR = 140,
            MAX_STAR_COLOR = 255;

        public const float SMOOTHING_FACTOR = 0.1f;

        public static class Quality
        {
            public const bool
                LOW_USE_GLOW_EFFECT = true,
                MEDIUM_USE_GLOW_EFFECT = true,
                HIGH_USE_GLOW_EFFECT = true,
                LOW_USE_ANTI_ALIAS = true,
                MEDIUM_USE_ANTI_ALIAS = true,
                HIGH_USE_ANTI_ALIAS = true;

            public const float
                LOW_GLOW_RADIUS = 2.0f,
                MEDIUM_GLOW_RADIUS = 4.0f,
                HIGH_GLOW_RADIUS = 6.0f;
        }
    }

    private static readonly SKColor[] _gradientColors =
        [SKColors.White, SKColors.Transparent];
    private static readonly float[] _gradientPositions =
        [0.0f, 1.0f];

    private Star[]? _stars;

    private float 
        _lowSpectrum,
        _midSpectrum,
        _highSpectrum,
        _spectrumEnergy,
        _spawnAccumulator;

    private int _starCount;
    private bool _useGlowEffect;
    private float _glowRadius;
    private volatile bool _isConfiguring;
    private readonly Random _random = new();

    protected override void OnInitialize()
    {
        ExecuteSafely(
            () =>
            {
                base.OnInitialize();
                InitializeRendererState();
                Log(LogLevel.Debug, LOG_PREFIX, "Initialized");
            },
            nameof(OnInitialize),
            "Failed to initialize renderer"
        );
    }

    private void InitializeRendererState()
    {
        _starCount = _isOverlayActive
            ? OVERLAY_STAR_COUNT
            : DEFAULT_STAR_COUNT;
        InitializeStars();
        ApplyQualitySettings();
    }

    public override void Configure(
        bool isOverlayActive,
        RenderQuality quality)
    {
        ExecuteSafely(
            () =>
            {
                if (_isConfiguring) return;

                try
                {
                    _isConfiguring = true;
                    ProcessConfiguration(isOverlayActive, quality);
                }
                finally
                {
                    _isConfiguring = false;
                }
            },
            nameof(Configure),
            "Failed to configure renderer"
        );
    }

    private void ProcessConfiguration(
        bool isOverlayActive,
        RenderQuality quality)
    {
        bool configChanged = IsConfigurationChanged(
            isOverlayActive, quality);
        ApplyConfiguration(isOverlayActive, quality);

        if (configChanged)
        {
            HandleConfigurationChange();
        }
    }

    private bool IsConfigurationChanged(
        bool isOverlayActive,
        RenderQuality quality) =>
        _isOverlayActive != isOverlayActive || Quality != quality;

    private void ApplyConfiguration(
        bool isOverlayActive,
        RenderQuality quality)
    {
        _isOverlayActive = isOverlayActive;
        Quality = quality;
        _smoothingFactor = isOverlayActive ? 0.15f : 0.1f;
        _starCount = isOverlayActive
            ? OVERLAY_STAR_COUNT
            : DEFAULT_STAR_COUNT;
    }

    private void HandleConfigurationChange()
    {
        base.ApplyQualitySettings();
        ApplyQualitySettings();
        InitializeStars();
        OnConfigurationChanged();
    }

    protected override void OnConfigurationChanged()
    {
        ExecuteSafely(
            () =>
            {
                Log(LogLevel.Debug,
                    LOG_PREFIX,
                    $"Configuration changed. Quality: {Quality}, Stars: {_starCount}");
            },
            nameof(OnConfigurationChanged),
            "Failed to handle configuration change"
        );
    }

    protected override void ApplyQualitySettings()
    {
        ExecuteSafely(
            ProcessQualitySettings,
            nameof(ApplyQualitySettings),
            "Failed to apply quality settings"
        );
    }

    private void ProcessQualitySettings()
    {
        if (_isConfiguring) return;

        try
        {
            _isConfiguring = true;
            base.ApplyQualitySettings();
            SetQualityLevelSettings();
            Log(LogLevel.Debug, LOG_PREFIX, $"Quality changed to {Quality}");
        }
        finally
        {
            _isConfiguring = false;
        }
    }

    private void SetQualityLevelSettings()
    {
        switch (Quality)
        {
            case RenderQuality.Low:
                ApplyLowQualitySettings();
                break;

            case RenderQuality.Medium:
                ApplyMediumQualitySettings();
                break;

            case RenderQuality.High:
                ApplyHighQualitySettings();
                break;
        }

        _samplingOptions = QualityBasedSamplingOptions();
    }

    private void ApplyLowQualitySettings()
    {
        _useAntiAlias = LOW_USE_ANTI_ALIAS;
        _useAdvancedEffects = false;
        _useGlowEffect = LOW_USE_GLOW_EFFECT;
        _glowRadius = LOW_GLOW_RADIUS;
    }

    private void ApplyMediumQualitySettings()
    {
        _useAntiAlias = MEDIUM_USE_ANTI_ALIAS;
        _useAdvancedEffects = true;
        _useGlowEffect = MEDIUM_USE_GLOW_EFFECT;
        _glowRadius = MEDIUM_GLOW_RADIUS;
    }

    private void ApplyHighQualitySettings()
    {
        _useAntiAlias = HIGH_USE_ANTI_ALIAS;
        _useAdvancedEffects = true;
        _useGlowEffect = HIGH_USE_GLOW_EFFECT;
        _glowRadius = HIGH_GLOW_RADIUS;
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
        if (!ValidateRenderParameters(
            canvas, spectrum, info, paint, barCount))
            return;

        ExecuteSafely(
            () => ExecuteRenderProcess(
                canvas, spectrum, info, barWidth, barSpacing),
            nameof(RenderEffect),
            "Error during rendering"
        );
    }

    private void ExecuteRenderProcess(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float _,
        float __)
    {
        UpdateRenderer(spectrum, info);
        RenderStars(canvas, info);
    }

    private void UpdateRenderer(
        float[] spectrum,
        SKImageInfo info)
    {
        UpdateState(spectrum);
        UpdateStars(TIME_STEP, info.Width, info.Height);
    }

    private bool ValidateRenderParameters(
        SKCanvas? canvas,
        float[]? spectrum,
        SKImageInfo info,
        SKPaint? paint,
        int barCount) =>
        ValidateCanvas(canvas) &&
        ValidateSpectrum(spectrum) &&
        ValidatePaint(paint) &&
        ValidateDimensions(info) &&
        ValidateBarCount(barCount) &&
        !IsDisposed();

    private static bool ValidateCanvas(SKCanvas? canvas)
    {
        if (canvas != null) return true;
        Log(LogLevel.Error, LOG_PREFIX, "Canvas is null");
        return false;
    }

    private static bool ValidateSpectrum(float[]? spectrum)
    {
        if (spectrum != null && spectrum.Length > 0) return true;
        Log(LogLevel.Error, LOG_PREFIX, "Spectrum is null or empty");
        return false;
    }

    private static bool ValidatePaint(SKPaint? paint)
    {
        if (paint != null) return true;
        Log(LogLevel.Error, LOG_PREFIX, "Paint is null");
        return false;
    }

    private static bool ValidateDimensions(SKImageInfo info)
    {
        if (info.Width > 0 && info.Height > 0) return true;
        Log(LogLevel.Error, LOG_PREFIX, $"Invalid dimensions: {info.Width}x{info.Height}");
        return false;
    }

    private static bool ValidateBarCount(int barCount)
    {
        if (barCount > 0) return true;
        Log(LogLevel.Error, LOG_PREFIX, "Invalid bar count");
        return false;
    }

    private bool IsDisposed()
    {
        if (!_disposed) return false;
        Log(LogLevel.Error, LOG_PREFIX, "Renderer is disposed");
        return true;
    }

    private void UpdateState(float[] spectrum)
    {
        bool semaphoreAcquired = false;
        try
        {
            semaphoreAcquired = _spectrumSemaphore.Wait(0);
            if (semaphoreAcquired)
            {
                ProcessSpectrum(spectrum);
            }
        }
        finally
        {
            if (semaphoreAcquired)
                _spectrumSemaphore.Release();
        }
    }

    private void ProcessSpectrum(float[] spectrum)
    {
        if (spectrum.Length < 3) return;

        int bandLength = spectrum.Length / 3;

        (float lowAvg, float midAvg, float highAvg) =
            CalculateSpectralAverages(spectrum, bandLength);

        float smoothingSpeed = SMOOTHING_FACTOR;
        _lowSpectrum = _lowSpectrum * (1f - smoothingSpeed) +
            lowAvg * smoothingSpeed;
        _midSpectrum = _midSpectrum * (1f - smoothingSpeed) +
            midAvg * smoothingSpeed;
        _highSpectrum = _highSpectrum * (1f - smoothingSpeed) +
            highAvg * smoothingSpeed;

        _spectrumEnergy = (_lowSpectrum + _midSpectrum + _highSpectrum) * 0.333f;
    }

    private static (float low, float mid, float high) CalculateSpectralAverages(
        float[] spectrum,
        int bandLength)
    {
        float lowSum = 0f, midSum = 0f, highSum = 0f;

        for (int i = 0; i < bandLength; i++)
            lowSum += spectrum[i];

        for (int i = bandLength; i < 2 * bandLength; i++)
            midSum += spectrum[i];

        for (int i = 2 * bandLength; i < spectrum.Length; i++)
            highSum += spectrum[i];

        float lowAvg = lowSum / bandLength;
        float midAvg = midSum / bandLength;
        float highAvg = highSum / (spectrum.Length - 2 * bandLength);

        return (lowAvg, midAvg, highAvg);
    }

    private void UpdateStars(
        float deltaTime,
        float canvasWidth,
        float canvasHeight)
    {
        if (_stars == null) return;

        UpdateSpawnAccumulator();
        ProcessStarsInBatches(deltaTime, canvasWidth, canvasHeight);
        SpawnNewStars(canvasWidth, canvasHeight);
    }

    private void UpdateSpawnAccumulator() =>
        _spawnAccumulator += _spectrumEnergy * SPAWN_RATE_PER_SECOND * TIME_STEP;

    private void ProcessStarsInBatches(
        float deltaTime,
        float canvasWidth,
        float canvasHeight)
    {
        if (_stars == null) return;

        int batchCount = (_stars.Length + RENDERER_BATCH_SIZE - 1) /
            RENDERER_BATCH_SIZE;

        for (int batch = 0; batch < batchCount; batch++)
        {
            int start = batch * RENDERER_BATCH_SIZE;
            int end = Min(start + RENDERER_BATCH_SIZE, _stars.Length);
            UpdateStarsBatch(start, end, deltaTime, canvasWidth, canvasHeight);
        }
    }

    private void UpdateStarsBatch(
        int start,
        int end,
        float deltaTime,
        float canvasWidth,
        float canvasHeight)
    {
        if (_stars == null) return;

        for (int i = start; i < end; i++)
        {
            if (!_stars[i].IsActive) continue;

            UpdateSingleStar(i, deltaTime, canvasWidth, canvasHeight);
        }
    }

    private void UpdateSingleStar(
        int index,
        float deltaTime,
        float canvasWidth,
        float canvasHeight)
    {
        if (_stars == null) return;

        UpdateStarLifetime(index, deltaTime);
        if (!_stars[index].IsActive) return;

        UpdateStarPosition(index, deltaTime);
        ConstrainStarPosition(index, canvasWidth, canvasHeight);
        UpdateStarOpacity(index);
        UpdateStarBrightness(index);
    }

    private void UpdateStarLifetime(int index, float deltaTime)
    {
        if (_stars == null) return;

        _stars[index].Lifetime -= deltaTime;

        if (_stars[index].Lifetime <= 0)
        {
            _stars[index].IsActive = false;
        }
    }

    private void UpdateStarPosition(int index, float deltaTime)
    {
        if (_stars == null) return;

        float totalMagnitude = _lowSpectrum + _midSpectrum + _highSpectrum;
        if (totalMagnitude < 0.01f) return;

        float spectralDensity = totalMagnitude * SPECTRUM_SENSITIVITY;

        float bassAngle = _time * _stars[index].ChaosFrequencyX;
        float midAngle = _time * _stars[index].ChaosFrequencyY * 1.3f;
        float highAngle = _time * _stars[index].TwinkleFactor * 1.8f;

        float bassX = _lowSpectrum * Sin(bassAngle);
        float bassY = _lowSpectrum * Cos(bassAngle);

        float midX = _midSpectrum * Sin(midAngle) * 0.8f;
        float midY = _midSpectrum * Cos(midAngle) * 0.8f;

        float highX = _highSpectrum * Sin(highAngle) * 0.5f;
        float highY = _highSpectrum * Cos(highAngle + 1.57f) * 0.5f;

        float densityX = Sin(_time * 1.2f + _stars[index].TwinkleFactor) *
            spectralDensity * 0.6f;
        float densityY = Cos(_time * 1.5f + _stars[index].TwinkleFactor) *
            spectralDensity * 0.6f;

        float directionVariance = (_highSpectrum - _lowSpectrum) * 5f;
        float directionX = Sin(_time + _stars[index].ChaosFrequencyX) * directionVariance;
        float directionY = Cos(_time + _stars[index].ChaosFrequencyY) * directionVariance;

        float velocityX = (bassX + midX + highX + densityX + directionX) *
            MOVEMENT_FACTOR * _stars[index].SpeedFactor * deltaTime;
        float velocityY = (bassY + midY + highY + densityY + directionY) *
            MOVEMENT_FACTOR * _stars[index].SpeedFactor * deltaTime;

        _stars[index].X += velocityX;
        _stars[index].Y += velocityY;
    }

    private void ConstrainStarPosition(
        int index,
        float canvasWidth,
        float canvasHeight)
    {
        if (_stars == null) return;

        _stars[index].X = Clamp(_stars[index].X, 0, canvasWidth);
        _stars[index].Y = Clamp(_stars[index].Y, 0, canvasHeight);
    }

    private void UpdateStarOpacity(int index)
    {
        if (_stars == null) return;

        float lifetimeRatio = _stars[index].Lifetime / _stars[index].MaxLifetime;

        if (lifetimeRatio < 0.2f)
        {
            _stars[index].Opacity = lifetimeRatio / 0.2f;
        }
        else if (_stars[index].Opacity < 1.0f)
        {
            _stars[index].Opacity = MathF.Min(1.0f,
                _stars[index].Opacity + FADE_IN_SPEED * TIME_STEP);
        }

        _stars[index].Opacity *= (0.8f + _spectrumEnergy * 0.4f);
    }

    private void UpdateStarBrightness(int index)
    {
        if (_stars == null) return;

        float energyContribution = _spectrumEnergy * 0.8f;
        float bassContribution = _lowSpectrum * 0.4f;
        float midContribution = _midSpectrum * 0.6f;
        float highContribution = _highSpectrum * 0.5f;

        float twinklePhase = _time * TWINKLE_SPEED * _stars[index].TwinkleSpeed +
            _stars[index].TwinkleFactor;
        float twinkleFactor = Sin(twinklePhase) * BRIGHTNESS_VARIATION;

        float totalContribution = energyContribution + bassContribution +
            midContribution + highContribution;
        float baseBrightness = DEFAULT_BRIGHTNESS;

        _stars[index].Brightness = Clamp(
            baseBrightness * (1f + totalContribution) + twinkleFactor,
            MIN_BRIGHTNESS,
            1.5f
        );
    }

    private void SpawnNewStars(float canvasWidth, float canvasHeight)
    {
        if (!ShouldSpawnStars()) return;

        int starsToSpawn = CalculateStarsToSpawn();
        CreateNewStars(starsToSpawn, canvasWidth, canvasHeight);
    }

    private bool ShouldSpawnStars() =>
        _stars != null &&
        _spawnAccumulator >= 1.0f &&
        _spectrumEnergy >= SPAWN_THRESHOLD;

    private int CalculateStarsToSpawn()
    {
        int starsToSpawn = (int)_spawnAccumulator;
        _spawnAccumulator -= starsToSpawn;
        return starsToSpawn;
    }

    private void CreateNewStars(
        int count,
        float canvasWidth,
        float canvasHeight)
    {
        if (_stars == null) return;

        for (int i = 0; i < count && i < _stars.Length; i++)
        {
            int freeIndex = FindFreeStarSlot();
            if (freeIndex < 0) break;

            _stars[freeIndex] = CreateNewStar(canvasWidth, canvasHeight);
        }
    }

    private int FindFreeStarSlot()
    {
        if (_stars == null) return -1;

        for (int i = 0; i < _stars.Length; i++)
        {
            if (!_stars[i].IsActive) return i;
        }
        return -1;
    }

    private Star CreateNewStar(float canvasWidth, float canvasHeight)
    {
        float energyFactor = 1f + _spectrumEnergy * 0.8f;

        float lifetime = (MIN_LIFETIME + (MAX_LIFETIME - MIN_LIFETIME) *
            (float)_random.NextDouble()) * energyFactor;
        float size = (BASE_STAR_SIZE + STAR_SIZE_RANGE *
            (float)_random.NextDouble()) *
            Math.Clamp(energyFactor * 0.9f, 0.7f, 1.4f);

        SKColor color = GenerateSpectralStarColor(_random);
        (float x, float y) = GenerateStarPosition(_random, canvasWidth, canvasHeight);
        (float twinkleFactor, float twinkleSpeed) = GenerateStarTwinkle(_random);

        float speedVariation = 0.5f + (float)_random.NextDouble() * 1.5f;
        float spectralSpeedFactor = Math.Clamp(energyFactor * speedVariation,
            0.2f, 2.5f);

        float chaosFreqX = 1.0f + (float)_random.NextDouble() * 3.0f;
        float chaosFreqY = 1.0f + (float)_random.NextDouble() * 3.0f;

        return new Star
        {
            X = x,
            Y = y,
            Size = size,
            Brightness = DEFAULT_BRIGHTNESS * (0.7f + _spectrumEnergy * 0.5f),
            TwinkleFactor = twinkleFactor,
            TwinkleSpeed = twinkleSpeed * energyFactor,
            Color = color,
            IsActive = true,
            Lifetime = lifetime,
            MaxLifetime = lifetime,
            Opacity = 0.01f,
            SpeedFactor = spectralSpeedFactor,
            ChaosFrequencyX = chaosFreqX,
            ChaosFrequencyY = chaosFreqY
        };
    }

    private static (float x, float y) GenerateStarPosition(
        Random random,
        float width,
        float height)
    {
        float x = (float)random.NextDouble() * width;
        float y = (float)random.NextDouble() * height;

        return (x, y);
    }

    private static (float factor, float speed) GenerateStarTwinkle(Random random)
    {
        float factor = (float)random.NextDouble() * MathF.PI * 2f;
        float speed = 0.8f + (float)random.NextDouble() * 0.4f;

        return (factor, speed);
    }

    private SKColor GenerateSpectralStarColor(Random random)
    {
        float energy = _spectrumEnergy;
        float bassWeight = _lowSpectrum;
        float midWeight = _midSpectrum;
        float highWeight = _highSpectrum;

        byte r, g, b;

        if (energy > 0.8f && (float)random.NextDouble() < 0.4f)
        {
            r = 255;
            g = (byte)(170 + random.Next(60));
            b = (byte)(90 + random.Next(120));
        }
        else if (bassWeight > 0.6f && (float)random.NextDouble() < 0.35f)
        {
            r = (byte)(140 + random.Next(90));
            g = (byte)(80 + random.Next(70));
            b = (byte)(190 + random.Next(65));
        }
        else if (highWeight > 0.5f && (float)random.NextDouble() < 0.35f)
        {
            r = (byte)(80 + random.Next(120));
            g = (byte)(170 + random.Next(85));
            b = 255;
        }
        else
        {
            r = (byte)random.Next(MIN_STAR_COLOR, MAX_STAR_COLOR + 1);
            g = (byte)random.Next(MIN_STAR_COLOR, MAX_STAR_COLOR + 1);
            b = (byte)random.Next(MIN_STAR_COLOR, MAX_STAR_COLOR + 1);
        }

        return new SKColor(r, g, b);
    }

    private void RenderStars(SKCanvas canvas, SKImageInfo _)
    {
        if (_stars == null) return;

        using var starPaint = CreateStarPaint();
        using var glowPaint = CreateGlowPaint();

        RenderAllStars(canvas, starPaint, glowPaint);
    }

    private SKPaint CreateStarPaint()
    {
        var paint = _paintPool.Get();
        paint.IsAntialias = _useAntiAlias;
        return paint;
    }

    private SKPaint? CreateGlowPaint()
    {
        if (!ShouldUseGlowEffect()) return null;

        var paint = _paintPool.Get();
        paint.IsAntialias = _useAntiAlias;
        paint.BlendMode = SKBlendMode.Plus;
        paint.Shader = SKShader.CreateRadialGradient(
            new SKPoint(0, 0),
            1.0f,
            _gradientColors,
            _gradientPositions,
            SKShaderTileMode.Clamp);
        paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, _glowRadius);
        return paint;
    }

    private bool ShouldUseGlowEffect() =>
        _useGlowEffect && _useAdvancedEffects;

    private void RenderAllStars(SKCanvas canvas, SKPaint starPaint, SKPaint? glowPaint)
    {
        if (_stars == null) return;

        for (int i = 0; i < _stars.Length; i++)
        {
            if (ShouldRenderStar(_stars[i]))
            {
                RenderSingleStar(canvas, _stars[i], starPaint, glowPaint);
            }
        }
    }

    private static bool ShouldRenderStar(in Star star) =>
        star.IsActive && star.Opacity > 0.01f;

    private void RenderSingleStar(
        SKCanvas canvas,
        in Star star,
        SKPaint starPaint,
        SKPaint? glowPaint)
    {
        float lifetimeRatio = star.Lifetime / star.MaxLifetime;
        float sizeModifier = 0.8f + lifetimeRatio * 0.2f;
        float spectralSizeBoost = _spectrumEnergy * 0.6f;
        float size = star.Size * sizeModifier * (1f + spectralSizeBoost);

        float brightnessModifier = 1f + (_spectrumEnergy * 0.4f);
        float adjustedBrightness = star.Brightness * brightnessModifier;
        int alpha = Math.Clamp((int)(255 * adjustedBrightness * star.Opacity), 0, 255);

        if (alpha < 10) return;

        SKColor baseColor = new(
            star.Color.Red,
            star.Color.Green,
            star.Color.Blue,
            (byte)alpha);

        if (glowPaint != null && star.Brightness > GLOW_THRESHOLD &&
            star.Opacity > 0.5f)
        {
            canvas.Save();
            canvas.Translate(star.X, star.Y);

            float glowIntensity = adjustedBrightness;
            int glowAlpha = Math.Clamp(
                (int)(alpha * (0.1f + glowIntensity * 0.3f)), 0, 255);
            glowPaint.Color = baseColor.WithAlpha((byte)glowAlpha);

            float glowSize = size * (1.5f + glowIntensity * 0.5f);
            canvas.Scale(glowSize, glowSize);
            canvas.DrawOval(0, 0, 1, 1, glowPaint);

            glowPaint.Color = baseColor.WithAlpha((byte)(glowAlpha / 4));
            canvas.Scale(2.0f, 2.0f);
            canvas.DrawOval(0, 0, 1, 1, glowPaint);

            canvas.Restore();
        }

        starPaint.Color = baseColor;
        canvas.DrawCircle(star.X, star.Y, size, starPaint);

        if (_useAdvancedEffects && alpha > 180 && _spectrumEnergy > 0.5f)
        {
            starPaint.Color = star.Color.WithAlpha((byte)(alpha / 5));
            canvas.DrawCircle(star.X, star.Y, size * 1.2f, starPaint);
        }
    }

    private void InitializeStars()
    {
        _stars = new Star[_starCount];
        for (int i = 0; i < _starCount; i++)
        {
            _stars[i] = new Star { IsActive = false };
        }
    }

    private struct Star
    {
        public float 
            X,
            Y,
            Size,
            Brightness,
            TwinkleFactor,
            TwinkleSpeed,
            Lifetime,
            MaxLifetime,
            Opacity,
            SpeedFactor,
            ChaosFrequencyX,
            ChaosFrequencyY;

        public SKColor Color;
        public bool IsActive;
    }

    public override void Dispose()
    {
        if (_disposed) return;

        ExecuteSafely(
            () => OnDispose(),
            nameof(Dispose),
            "Error during disposal"
        );

        _disposed = true;
        base.Dispose();
        GC.SuppressFinalize(this);

        Log(LogLevel.Debug, LOG_PREFIX, "Disposed");
    }

    protected override void OnDispose()
    {
        ExecuteSafely(
            () =>
            {
                _stars = null;
                base.OnDispose();
            },
            nameof(OnDispose),
            "Error during specific disposal"
        );
    }
}