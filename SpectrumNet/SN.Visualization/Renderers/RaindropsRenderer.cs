#nullable enable

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class RaindropsRenderer : EffectSpectrumRenderer<RaindropsRenderer.QualitySettings>
{
    private static readonly Lazy<RaindropsRenderer> _instance =
        new(() => new RaindropsRenderer());

    public static RaindropsRenderer GetInstance() => _instance.Value;

    private const float TARGET_DELTA_TIME = 0.016f,
        SMOOTH_FACTOR = 0.2f,
        TRAIL_LENGTH_MULTIPLIER = 0.15f,
        TRAIL_LENGTH_SIZE_FACTOR = 5f,
        TRAIL_STROKE_MULTIPLIER = 0.6f,
        TRAIL_OPACITY_MULTIPLIER = 150f,
        TRAIL_INTENSITY_THRESHOLD = 0.3f,
        GRAVITY = 9.8f,
        LIFETIME_DECAY = 0.4f,
        SPLASH_REBOUND = 0.5f,
        SPLASH_VELOCITY_THRESHOLD = 1.0f,
        SPAWN_INTERVAL = 0.05f,
        FALLSPEED_THRESHOLD_MULTIPLIER = 1.5f,
        RAINDROP_SIZE_THRESHOLD_MULTIPLIER = 0.9f,
        RAINDROP_SIZE_HIGHLIGHT_THRESHOLD = 0.8f,
        INTENSITY_HIGHLIGHT_THRESHOLD = 0.4f,
        HIGHLIGHT_SIZE_MULTIPLIER = 0.4f,
        HIGHLIGHT_OFFSET_MULTIPLIER = 0.2f,
        PARTICLE_VELOCITY_BASE_MULTIPLIER = 0.7f,
        PARTICLE_VELOCITY_INTENSITY_MULTIPLIER = 0.3f,
        SPLASH_UPWARD_BASE_MULTIPLIER = 0.8f,
        SPLASH_UPWARD_INTENSITY_MULTIPLIER = 0.2f,
        SPLASH_PARTICLE_SIZE_BASE_MULTIPLIER = 0.7f,
        SPLASH_PARTICLE_SIZE_RANDOM_MULTIPLIER = 0.6f,
        SPLASH_PARTICLE_SIZE_INTENSITY_MULTIPLIER = 0.5f,
        SPLASH_PARTICLE_SIZE_INTENSITY_OFFSET = 0.8f,
        LOUDNESS_SCALE = 4.0f,
        SPLASH_MIN_INTENSITY = 0.2f,
        DROP_ALPHA_BASE = 0.7f,
        DROP_ALPHA_INTENSITY = 0.3f,
        HIGHLIGHT_ALPHA_MULTIPLIER = 150f,
        DROP_SIZE_MULTIPLIER = 1.5f,
        FALL_SPEED_DAMPING = 0.25f,
        BASE_FALL_SPEED = 200f,
        SPEED_VARIATION = 100f,
        INTENSITY_SPEED_MULTIPLIER = 2f,
        RAINDROP_SIZE = 3f,
        SPLASH_UPWARD_FORCE = 150f,
        SPLASH_PARTICLE_SIZE = 2f,
        PARTICLE_VELOCITY_MAX = 100f,
        SPAWN_THRESHOLD_NORMAL = 0.3f,
        SPAWN_THRESHOLD_OVERLAY = 0.4f,
        SPAWN_PROBABILITY = 0.5f,
        MIN_TIME_STEP = 0.001f,
        MAX_TIME_STEP = 0.033f,
        LOUDNESS_THRESHOLD = 0.3f,
        DROP_ACCELERATION_FACTOR = 0.3f,
        SPAWN_BOOST_MULTIPLIER = 2.0f,
        SPAWN_POSITION_VARIANCE = 0.5f,
        SPAWN_POSITION_OFFSET = 0.25f,
        INITIAL_DROP_HEIGHT_FACTOR = 0.5f,
        INITIAL_DROP_SIZE_MIN = 0.7f,
        INITIAL_DROP_SIZE_RANGE = 0.6f,
        INITIAL_DROP_INTENSITY_MIN = 0.3f,
        INITIAL_DROP_INTENSITY_RANGE = 0.3f,
        DROP_SIZE_INTENSITY_FACTOR = 0.4f,
        DROP_SIZE_BASE = 0.8f,
        DROP_SIZE_RANDOM_RANGE = 0.2f,
        DROP_SIZE_RANDOM_BASE = 0.9f,
        SPLASH_Y_OFFSET = 2f,
        PARTICLE_BOUNDARY_OFFSET = 50f,
        PARTICLE_LIFETIME_SQUARED_FACTOR = 0.8f,
        PARTICLE_LIFETIME_OFFSET = 0.2f,
        SPLASH_BOUNDARY_OFFSET = 1f;

    private const int INITIAL_DROP_COUNT = 30,
        SPLASH_PARTICLE_COUNT_MIN = 3,
        SPLASH_PARTICLE_COUNT_MAX = 8,
        MAX_SPAWNS_PER_FRAME = 3,
        MAX_RAINDROPS = 200,
        MAX_PARTICLES = 500,
        FRAME_COUNTER_MODULO = 2;

    private const byte MAX_ALPHA_BYTE = 255;

    private static readonly SKColor[] _trailGradientColors = [SKColors.Transparent, SKColors.Transparent];
    private static readonly float[] _trailGradientPositions = [0f, 1f];

    private Raindrop[] _raindrops = new Raindrop[MAX_RAINDROPS];
    private int _raindropCount;
    private float[] _smoothedSpectrumCache = new float[MAX_RAINDROPS];
    private readonly ParticleBuffer _particleBuffer = new(MAX_PARTICLES);
    private RenderCache? _renderCache;
    private float _timeSinceLastSpawn;
    private bool _firstRender = true;
    private float _actualDeltaTime = TARGET_DELTA_TIME;
    private float _averageLoudness;
    private int _frameCounter;
    private readonly Stopwatch _frameTimer = new();
    private readonly Random _random = new();

    public sealed class QualitySettings
    {
        public bool UseTrails { get; init; }
        public bool UseHighlights { get; init; }
        public bool UseGradientTrails { get; init; }
        public int EffectsThreshold { get; init; }
        public float TrailOpacity { get; init; }
        public float HighlightIntensity { get; init; }
    }

    protected override IReadOnlyDictionary<RenderQuality, QualitySettings>
        QualitySettingsPresets
    { get; } = new Dictionary<RenderQuality, QualitySettings>
    {
        [RenderQuality.Low] = new()
        {
            UseTrails = false,
            UseHighlights = false,
            UseGradientTrails = false,
            EffectsThreshold = 4,
            TrailOpacity = 0.3f,
            HighlightIntensity = 0.5f
        },
        [RenderQuality.Medium] = new()
        {
            UseTrails = true,
            UseHighlights = true,
            UseGradientTrails = false,
            EffectsThreshold = 3,
            TrailOpacity = 0.5f,
            HighlightIntensity = 0.7f
        },
        [RenderQuality.High] = new()
        {
            UseTrails = true,
            UseHighlights = true,
            UseGradientTrails = true,
            EffectsThreshold = 2,
            TrailOpacity = 0.7f,
            HighlightIntensity = 1.0f
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

        var renderData = CalculateRenderData(
            processedSpectrum,
            info,
            renderParams);

        if (!ValidateRenderData(renderData))
            return;

        RenderRainVisualization(
            canvas,
            renderData,
            passedInPaint);
    }

    private RenderData CalculateRenderData(
        float[] spectrum,
        SKImageInfo info,
        RenderParameters renderParams)
    {
        UpdateSimulationState(info, renderParams.EffectiveBarCount);
        ProcessSpectrumData(spectrum);
        UpdatePhysics(spectrum, renderParams.EffectiveBarCount);

        return new RenderData(
            Raindrops: [.. _raindrops.Take(_raindropCount)],
            Particles: _particleBuffer.GetActiveParticles(),
            AverageLoudness: _averageLoudness,
            RenderCache: _renderCache!);
    }

    private void UpdateSimulationState(SKImageInfo info, int barCount)
    {
        UpdateTiming();
        UpdateCache(info);
        InitializeFirstFrame(barCount);
    }

    private void UpdatePhysics(float[] spectrum, int barCount)
    {
        UpdateRaindrops(spectrum);
        UpdateParticles();
        UpdateSpawning(spectrum, barCount);
    }

    private void UpdateParticles()
    {
        if (_frameCounter == 0)
            _particleBuffer.UpdateParticles(_actualDeltaTime * 2);
    }

    private void UpdateSpawning(float[] spectrum, int barCount)
    {
        if (_timeSinceLastSpawn >= SPAWN_INTERVAL)
        {
            SpawnNewDrops(spectrum, barCount);
            _timeSinceLastSpawn = 0;
        }
    }

    private static bool ValidateRenderData(RenderData data) =>
        data.RenderCache != null &&
        (data.Raindrops.Length > 0 || data.Particles.Length > 0);

    private void RenderRainVisualization(
        SKCanvas canvas,
        RenderData data,
        SKPaint basePaint)
    {
        var settings = CurrentQualitySettings!;

        RenderWithOverlay(canvas, () =>
        {
            _particleBuffer.RenderParticles(canvas, basePaint);

            if (ShouldRenderTrails(data, settings))
                RenderTrailLayer(canvas, data, basePaint, settings);

            RenderDropLayer(canvas, data, basePaint, settings);
        });
    }

    private bool ShouldRenderTrails(RenderData data, QualitySettings settings) =>
        UseAdvancedEffects &&
        settings.UseTrails &&
        data.AverageLoudness > LOUDNESS_THRESHOLD;

    private void UpdateTiming()
    {
        float elapsed = (float)_frameTimer.Elapsed.TotalSeconds;
        _frameTimer.Restart();

        _actualDeltaTime = CalculateDeltaTime(elapsed);
        _timeSinceLastSpawn += _actualDeltaTime;
        _frameCounter = (_frameCounter + 1) % FRAME_COUNTER_MODULO;
    }

    private static float CalculateDeltaTime(float elapsed) =>
        Clamp(
            TARGET_DELTA_TIME * (elapsed / TARGET_DELTA_TIME),
            MIN_TIME_STEP,
            MAX_TIME_STEP);

    private void UpdateCache(SKImageInfo info)
    {
        if (IsCacheValid(info))
            return;

        _renderCache = new RenderCache(info.Width, info.Height, IsOverlayActive);
        _particleBuffer.UpdateBounds(_renderCache.UpperBound, _renderCache.LowerBound);
    }

    private bool IsCacheValid(SKImageInfo info) =>
        _renderCache != null &&
        _renderCache.Width == info.Width &&
        _renderCache.Height == info.Height &&
        _renderCache.IsOverlay == IsOverlayActive;

    private void InitializeFirstFrame(int barCount)
    {
        if (!_firstRender) return;

        InitializeRaindrops(barCount);
        _firstRender = false;
        _frameTimer.Start();
    }

    private void InitializeRaindrops(int barCount)
    {
        _raindropCount = 0;
        int initialCount = Math.Min(INITIAL_DROP_COUNT, MAX_RAINDROPS);

        for (int i = 0; i < initialCount; i++)
        {
            _raindrops[_raindropCount++] = CreateInitialRaindrop(barCount);
        }
    }

    private Raindrop CreateInitialRaindrop(int barCount)
    {
        var (x, y) = CalculateInitialPosition();
        var (speed, size, intensity) = CalculateInitialProperties();

        return new Raindrop(
            X: x,
            Y: y,
            FallSpeed: speed,
            Size: size,
            Intensity: intensity,
            SpectrumIndex: _random.Next(barCount));
    }

    private (float x, float y) CalculateInitialPosition() =>
        (
            x: _renderCache!.Width * (float)_random.NextDouble(),
            y: _renderCache.UpperBound +
               (float)_random.NextDouble() * _renderCache.Height * INITIAL_DROP_HEIGHT_FACTOR
        );

    private (float speed, float size, float intensity) CalculateInitialProperties() =>
        (
            speed: CalculateInitialSpeed(),
            size: CalculateInitialSize(),
            intensity: CalculateInitialIntensity()
        );

    private float CalculateInitialSpeed() =>
        (BASE_FALL_SPEED + (float)(_random.NextDouble() * SPEED_VARIATION)) *
        FALL_SPEED_DAMPING;

    private float CalculateInitialSize() =>
        RAINDROP_SIZE * DROP_SIZE_MULTIPLIER *
        (INITIAL_DROP_SIZE_MIN + (float)_random.NextDouble() * INITIAL_DROP_SIZE_RANGE);

    private float CalculateInitialIntensity() =>
        INITIAL_DROP_INTENSITY_MIN +
        (float)_random.NextDouble() * INITIAL_DROP_INTENSITY_RANGE;

    private void ProcessSpectrumData(float[] spectrum)
    {
        float sum = ProcessSpectrumBlocks(spectrum);
        _averageLoudness = CalculateAverageLoudness(sum);
    }

    private float ProcessSpectrumBlocks(float[] spectrum)
    {
        float sum = 0f;
        float blockSize = spectrum.Length / (float)_smoothedSpectrumCache.Length;

        for (int i = 0; i < _smoothedSpectrumCache.Length; i++)
        {
            sum += ProcessSingleBlock(spectrum, i, blockSize);
        }

        return sum;
    }

    private float ProcessSingleBlock(float[] spectrum, int index, float blockSize)
    {
        int start = (int)(index * blockSize);
        int end = Math.Min((int)((index + 1) * blockSize), spectrum.Length);

        if (end <= start)
            return _smoothedSpectrumCache[index];

        float value = CalculateAverageValue(spectrum, start, end);
        _smoothedSpectrumCache[index] = Lerp(
            _smoothedSpectrumCache[index],
            value,
            SMOOTH_FACTOR);

        return _smoothedSpectrumCache[index];
    }

    private static float CalculateAverageValue(float[] spectrum, int start, int end)
    {
        float value = 0f;
        for (int j = start; j < end; j++)
            value += spectrum[j];
        return value / (end - start);
    }

    private static float CalculateAverageLoudness(float sum) =>
        Clamp(sum / MAX_RAINDROPS * LOUDNESS_SCALE, 0f, 1f);

    private void UpdateRaindrops(float[] spectrum)
    {
        int writeIdx = 0;

        for (int i = 0; i < _raindropCount; i++)
        {
            var drop = _raindrops[i];
            var updatedDrop = UpdateSingleRaindrop(drop);

            if (IsRaindropActive(updatedDrop))
            {
                _raindrops[writeIdx++] = updatedDrop;
            }
            else
            {
                HandleRaindropSplash(drop, spectrum);
            }
        }

        _raindropCount = writeIdx;
    }

    private Raindrop UpdateSingleRaindrop(Raindrop drop)
    {
        float acceleration = CalculateDropAcceleration(drop.Y);
        float newY = CalculateNewPosition(drop.Y, drop.FallSpeed, acceleration);

        return drop with { Y = newY };
    }

    private float CalculateDropAcceleration(float y)
    {
        float normalizedY = (y - _renderCache!.UpperBound) / _renderCache.Height;
        return 1f + normalizedY * DROP_ACCELERATION_FACTOR;
    }

    private float CalculateNewPosition(float currentY, float fallSpeed, float acceleration) =>
        currentY + fallSpeed * _actualDeltaTime * acceleration;

    private bool IsRaindropActive(Raindrop drop) =>
        drop.Y < _renderCache!.LowerBound;

    private void HandleRaindropSplash(Raindrop drop, float[] spectrum)
    {
        float intensity = GetDropIntensity(drop, spectrum);

        if (intensity > SPLASH_MIN_INTENSITY)
        {
            CreateSplashEffect(drop.X, intensity);
        }
    }

    private static float GetDropIntensity(Raindrop drop, float[] spectrum) =>
        drop.SpectrumIndex < spectrum.Length ?
        spectrum[drop.SpectrumIndex] :
        drop.Intensity;

    private void CreateSplashEffect(float x, float intensity) =>
        _particleBuffer.CreateSplashParticles(
            x,
            _renderCache!.LowerBound,
            intensity);

    private void SpawnNewDrops(float[] spectrum, int barCount)
    {
        if (!CanSpawnDrops(barCount, spectrum))
            return;

        var (stepWidth, threshold, spawnBoost) = CalculateSpawnParameters(barCount);
        SpawnDropsWithLimit(spectrum, stepWidth, threshold, spawnBoost);
    }

    private static bool CanSpawnDrops(int barCount, float[] spectrum) =>
        barCount > 0 && spectrum.Length > 0;

    private (float stepWidth, float threshold, float spawnBoost) CalculateSpawnParameters(
        int barCount) =>
        (
            stepWidth: _renderCache!.Width / barCount,
            threshold: IsOverlayActive ? SPAWN_THRESHOLD_OVERLAY : SPAWN_THRESHOLD_NORMAL,
            spawnBoost: 1.0f + _averageLoudness * SPAWN_BOOST_MULTIPLIER
        );

    private void SpawnDropsWithLimit(
        float[] spectrum,
        float stepWidth,
        float threshold,
        float spawnBoost)
    {
        int spawns = 0;
        int maxIndex = Math.Min(stepWidth > 0 ? spectrum.Length : 0, spectrum.Length);

        for (int i = 0; i < maxIndex && CanSpawnMore(spawns); i++)
        {
            if (ShouldSpawnDrop(spectrum[i], threshold, spawnBoost))
            {
                SpawnSingleDrop(i, spectrum[i], stepWidth);
                spawns++;
            }
        }
    }

    private bool CanSpawnMore(int currentSpawns) =>
        currentSpawns < MAX_SPAWNS_PER_FRAME &&
        _raindropCount < _raindrops.Length;

    private bool ShouldSpawnDrop(float intensity, float threshold, float spawnBoost) =>
        intensity > threshold &&
        _random.NextDouble() < SPAWN_PROBABILITY * intensity * spawnBoost;

    private void SpawnSingleDrop(int index, float intensity, float stepWidth) =>
        _raindrops[_raindropCount++] = CreateRaindrop(index, intensity, stepWidth);

    private Raindrop CreateRaindrop(int index, float intensity, float stepWidth)
    {
        float x = CalculateDropX(index, stepWidth);
        var (speed, size) = CalculateDropProperties(intensity);

        return new Raindrop(
            X: x,
            Y: _renderCache!.UpperBound,
            FallSpeed: speed,
            Size: size,
            Intensity: intensity,
            SpectrumIndex: index);
    }

    private float CalculateDropX(int index, float stepWidth)
    {
        float baseX = index * stepWidth + stepWidth * 0.5f;
        float variance = (float)(_random.NextDouble() * stepWidth * SPAWN_POSITION_VARIANCE -
                                stepWidth * SPAWN_POSITION_OFFSET);
        return baseX + variance;
    }

    private (float speed, float size) CalculateDropProperties(float intensity) =>
        (
            speed: CalculateDropSpeed(intensity),
            size: CalculateDropSize(intensity)
        );

    private float CalculateDropSpeed(float intensity) =>
        BASE_FALL_SPEED *
        (1f + intensity * INTENSITY_SPEED_MULTIPLIER) *
        FALL_SPEED_DAMPING +
        (float)(_random.NextDouble() * SPEED_VARIATION);

    private float CalculateDropSize(float intensity) =>
        RAINDROP_SIZE *
        DROP_SIZE_MULTIPLIER *
        (DROP_SIZE_BASE + intensity * DROP_SIZE_INTENSITY_FACTOR) *
        (DROP_SIZE_RANDOM_BASE + (float)_random.NextDouble() * DROP_SIZE_RANDOM_RANGE);

    private void RenderTrailLayer(
        SKCanvas canvas,
        RenderData data,
        SKPaint basePaint,
        QualitySettings settings)
    {
        var trailPaint = CreateTrailPaint(basePaint.Color, settings);

        try
        {
            RenderAllTrails(canvas, data.Raindrops, trailPaint, settings);
        }
        finally
        {
            ReturnPaint(trailPaint);
        }
    }

    private SKPaint CreateTrailPaint(SKColor baseColor, QualitySettings settings)
    {
        var paint = CreatePaint(baseColor, SKPaintStyle.Stroke);
        paint.StrokeCap = SKStrokeCap.Round;

        if (settings.UseGradientTrails)
        {
            ApplyTrailGradient(paint, baseColor);
        }

        return paint;
    }

    private static void ApplyTrailGradient(SKPaint paint, SKColor baseColor)
    {
        _trailGradientColors[0] = baseColor;
        _trailGradientColors[1] = baseColor.WithAlpha(0);

        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(0, 10),
            _trailGradientColors,
            _trailGradientPositions,
            SKShaderTileMode.Clamp);

        paint.Shader = shader;
    }

    private void RenderAllTrails(
        SKCanvas canvas,
        Raindrop[] raindrops,
        SKPaint trailPaint,
        QualitySettings settings)
    {
        foreach (var drop in raindrops)
        {
            if (ShouldRenderTrail(drop))
            {
                RenderSingleTrail(canvas, drop, trailPaint, settings);
            }
        }
    }

    private static bool ShouldRenderTrail(Raindrop drop) =>
        IsTrailEligible(drop) &&
        drop.Intensity >= TRAIL_INTENSITY_THRESHOLD;

    private static bool IsTrailEligible(Raindrop drop) =>
        drop.FallSpeed >= BASE_FALL_SPEED * FALLSPEED_THRESHOLD_MULTIPLIER &&
        drop.Size >= RAINDROP_SIZE * RAINDROP_SIZE_THRESHOLD_MULTIPLIER;

    private void RenderSingleTrail(
        SKCanvas canvas,
        Raindrop drop,
        SKPaint trailPaint,
        QualitySettings settings)
    {
        var (length, alpha, strokeWidth) = CalculateTrailParameters(drop, settings);
        ConfigureTrailPaint(trailPaint, alpha, strokeWidth);
        DrawTrailPath(canvas, drop, length, trailPaint);
    }

    private static (float length, byte alpha, float strokeWidth) CalculateTrailParameters(
        Raindrop drop,
        QualitySettings settings) =>
        (
            length: CalculateTrailLength(drop),
            alpha: CalculateTrailAlpha(drop.Intensity, settings.TrailOpacity),
            strokeWidth: drop.Size * TRAIL_STROKE_MULTIPLIER
        );

    private static float CalculateTrailLength(Raindrop drop) =>
        Math.Min(
            drop.FallSpeed * TRAIL_LENGTH_MULTIPLIER * drop.Intensity,
            drop.Size * TRAIL_LENGTH_SIZE_FACTOR);

    private static byte CalculateTrailAlpha(float intensity, float opacity) =>
        (byte)(TRAIL_OPACITY_MULTIPLIER * intensity * opacity);

    private static void ConfigureTrailPaint(
        SKPaint paint,
        byte alpha,
        float strokeWidth)
    {
        paint.Color = paint.Color.WithAlpha(alpha);
        paint.StrokeWidth = strokeWidth;
    }

    private void DrawTrailPath(
        SKCanvas canvas,
        Raindrop drop,
        float trailLength,
        SKPaint paint)
    {
        RenderPath(canvas, path =>
        {
            path.MoveTo(drop.X, drop.Y);
            path.LineTo(drop.X, drop.Y - trailLength);
        }, paint);
    }

    private void RenderDropLayer(
        SKCanvas canvas,
        RenderData data,
        SKPaint basePaint,
        QualitySettings settings)
    {
        var dropPaint = CreatePaint(basePaint.Color, SKPaintStyle.Fill);

        try
        {
            foreach (var drop in data.Raindrops)
            {
                RenderSingleDrop(canvas, drop, dropPaint, settings);
            }
        }
        finally
        {
            ReturnPaint(dropPaint);
        }
    }

    private void RenderSingleDrop(
        SKCanvas canvas,
        Raindrop drop,
        SKPaint dropPaint,
        QualitySettings settings)
    {
        var dropRect = CalculateDropBounds(drop);

        if (!IsAreaVisible(canvas, dropRect))
            return;

        DrawDrop(canvas, drop, dropPaint);

        if (ShouldRenderHighlight(drop, settings))
        {
            RenderDropHighlight(canvas, drop, settings);
        }
    }

    private static SKRect CalculateDropBounds(Raindrop drop) =>
        new(
            drop.X - drop.Size,
            drop.Y - drop.Size,
            drop.X + drop.Size,
            drop.Y + drop.Size);

    private static void DrawDrop(SKCanvas canvas, Raindrop drop, SKPaint paint)
    {
        byte alpha = CalculateDropAlpha(drop.Intensity);
        paint.Color = paint.Color.WithAlpha(alpha);
        canvas.DrawCircle(drop.X, drop.Y, drop.Size, paint);
    }

    private static byte CalculateDropAlpha(float intensity) =>
        CalculateAlpha(DROP_ALPHA_BASE + intensity * DROP_ALPHA_INTENSITY);

    private bool ShouldRenderHighlight(Raindrop drop, QualitySettings settings) =>
        UseAdvancedEffects &&
        settings.UseHighlights &&
        IsHighlightEligible(drop);

    private static bool IsHighlightEligible(Raindrop drop) =>
        drop.Size > RAINDROP_SIZE * RAINDROP_SIZE_HIGHLIGHT_THRESHOLD &&
        drop.Intensity > INTENSITY_HIGHLIGHT_THRESHOLD;

    private void RenderDropHighlight(
        SKCanvas canvas,
        Raindrop drop,
        QualitySettings settings)
    {
        var highlightPaint = CreatePaint(SKColors.White, SKPaintStyle.Fill);

        try
        {
            var (x, y, size, alpha) = CalculateHighlightParameters(drop, settings);
            DrawHighlight(canvas, x, y, size, alpha, highlightPaint);
        }
        finally
        {
            ReturnPaint(highlightPaint);
        }
    }

    private static (float x, float y, float size, byte alpha) CalculateHighlightParameters(
        Raindrop drop,
        QualitySettings settings) =>
        (
            x: drop.X - drop.Size * HIGHLIGHT_OFFSET_MULTIPLIER,
            y: drop.Y - drop.Size * HIGHLIGHT_OFFSET_MULTIPLIER,
            size: drop.Size * HIGHLIGHT_SIZE_MULTIPLIER,
            alpha: (byte)(HIGHLIGHT_ALPHA_MULTIPLIER * drop.Intensity * settings.HighlightIntensity)
        );

    private static void DrawHighlight(
        SKCanvas canvas,
        float x,
        float y,
        float size,
        byte alpha,
        SKPaint paint)
    {
        paint.Color = SKColors.White.WithAlpha(alpha);
        canvas.DrawCircle(x, y, size, paint);
    }

    protected override int GetMaxBarsForQuality() => Quality switch
    {
        RenderQuality.Low => 50,
        RenderQuality.Medium => 100,
        RenderQuality.High => 150,
        _ => 100
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
        {
            smoothingFactor *= 1.2f;
        }

        SetProcessingSmoothingFactor(smoothingFactor);
        RequestRedraw();
    }

    protected override void OnDispose()
    {
        _raindrops = null!;
        _smoothedSpectrumCache = null!;
        _renderCache = null;
        _frameTimer.Stop();
        base.OnDispose();
    }

    private record RenderData(
        Raindrop[] Raindrops,
        Particle[] Particles,
        float AverageLoudness,
        RenderCache RenderCache);

    private record RenderCache(float Width, float Height, bool IsOverlay)
    {
        public float UpperBound => 0f;
        public float LowerBound => Height;
    }

    private readonly record struct Raindrop(
        float X,
        float Y,
        float FallSpeed,
        float Size,
        float Intensity,
        int SpectrumIndex);

    private struct Particle(
        float x,
        float y,
        float velocityX,
        float velocityY,
        float size,
        bool isSplash)
    {
        public float X = x;
        public float Y = y;
        public float VelocityX = velocityX;
        public float VelocityY = velocityY;
        public float Lifetime = 1.0f;
        public float Size = size;
        public readonly bool IsSplash = isSplash;

        public bool Update(float deltaTime, float _, float lowerBound)
        {
            UpdatePosition(deltaTime);
            UpdateVelocity(deltaTime);
            HandleBoundaryCollision(lowerBound);
            UpdateLifetime(deltaTime);

            return IsAlive(lowerBound);
        }

        private void UpdatePosition(float deltaTime)
        {
            X += VelocityX * deltaTime;
            Y += VelocityY * deltaTime;
        }

        private void UpdateVelocity(float deltaTime)
        {
            VelocityY += deltaTime * GRAVITY;
        }

        private void HandleBoundaryCollision(float lowerBound)
        {
            if (!IsSplash || Y < lowerBound - SPLASH_BOUNDARY_OFFSET)
                return;

            Y = lowerBound - SPLASH_BOUNDARY_OFFSET;
            VelocityY *= -SPLASH_REBOUND;

            if (Math.Abs(VelocityY) < SPLASH_VELOCITY_THRESHOLD)
                VelocityY = 0;
        }

        private void UpdateLifetime(float deltaTime)
        {
            Lifetime -= deltaTime * LIFETIME_DECAY;
        }

        private readonly bool IsAlive(float lowerBound) =>
            Lifetime > 0 && Y < lowerBound + PARTICLE_BOUNDARY_OFFSET;
    }

    private sealed class ParticleBuffer(int capacity)
    {
        private readonly Particle[] _particles = new Particle[capacity];
        private int _count;
        private float _upperBound;
        private float _lowerBound;
        private readonly Random _random = Random.Shared;

        public void UpdateBounds(float upperBound, float lowerBound)
        {
            _upperBound = upperBound;
            _lowerBound = lowerBound;
        }

        public void UpdateParticles(float deltaTime)
        {
            int writeIndex = 0;

            for (int i = 0; i < _count; i++)
            {
                if (_particles[i].Update(deltaTime, _upperBound, _lowerBound))
                {
                    if (writeIndex != i)
                        _particles[writeIndex] = _particles[i];
                    writeIndex++;
                }
            }

            _count = writeIndex;
        }

        public void CreateSplashParticles(float x, float y, float intensity)
        {
            int count = CalculateSplashCount();
            if (count <= 0) return;

            var (velocityMax, upwardForce) = CalculateSplashParameters(intensity);
            CreateParticles(x, y - SPLASH_Y_OFFSET, count, velocityMax, upwardForce);
        }

        private int CalculateSplashCount() =>
            Math.Min(
                _random.Next(SPLASH_PARTICLE_COUNT_MIN, SPLASH_PARTICLE_COUNT_MAX),
                _particles.Length - _count);

        private static (float velocityMax, float upwardForce) CalculateSplashParameters(
            float intensity) =>
            (
                velocityMax: CalculateVelocityMax(intensity),
                upwardForce: CalculateUpwardForce(intensity)
            );

        private static float CalculateVelocityMax(float intensity) =>
            PARTICLE_VELOCITY_MAX *
            (PARTICLE_VELOCITY_BASE_MULTIPLIER +
             intensity * PARTICLE_VELOCITY_INTENSITY_MULTIPLIER);

        private static float CalculateUpwardForce(float intensity) =>
            SPLASH_UPWARD_FORCE *
            (SPLASH_UPWARD_BASE_MULTIPLIER +
             intensity * SPLASH_UPWARD_INTENSITY_MULTIPLIER);

        private void CreateParticles(
            float x,
            float y,
            int count,
            float velocityMax,
            float upwardForce)
        {
            for (int i = 0; i < count && _count < _particles.Length; i++)
            {
                _particles[_count++] = CreateSingleParticle(
                    x,
                    y,
                    velocityMax,
                    upwardForce);
            }
        }

        private Particle CreateSingleParticle(
            float x,
            float y,
            float velocityMax,
            float upwardForce)
        {
            var (velocityX, velocityY) = CalculateParticleVelocity(velocityMax, upwardForce);
            float size = CalculateParticleSize();

            return new Particle(
                x,
                y,
                velocityX,
                velocityY,
                size,
                true);
        }

        private (float x, float y) CalculateParticleVelocity(
            float velocityMax,
            float upwardForce)
        {
            float angle = (float)(_random.NextDouble() * Math.PI * 2);
            float speed = (float)(_random.NextDouble() * velocityMax);

            return (
                x: MathF.Cos(angle) * speed,
                y: MathF.Sin(angle) * speed - upwardForce
            );
        }

        private float CalculateParticleSize()
        {
            float randomFactor = SPLASH_PARTICLE_SIZE_BASE_MULTIPLIER +
                               (float)_random.NextDouble() * SPLASH_PARTICLE_SIZE_RANDOM_MULTIPLIER;

            float intensityFactor = _random.Next(0, 2) * SPLASH_PARTICLE_SIZE_INTENSITY_MULTIPLIER +
                                  SPLASH_PARTICLE_SIZE_INTENSITY_OFFSET;

            return SPLASH_PARTICLE_SIZE * randomFactor * intensityFactor;
        }

        public void RenderParticles(SKCanvas canvas, SKPaint basePaint)
        {
            if (_count == 0) return;

            var paint = RaindropsRenderer.GetInstance().CreatePaint(
                basePaint.Color,
                SKPaintStyle.Fill);

            try
            {
                RenderAllParticles(canvas, paint);
            }
            finally
            {
                RaindropsRenderer.GetInstance().ReturnPaint(paint);
            }
        }

        private void RenderAllParticles(SKCanvas canvas, SKPaint paint)
        {
            for (int i = 0; i < _count; i++)
            {
                RenderSingleParticle(canvas, ref _particles[i], paint);
            }
        }

        private static void RenderSingleParticle(
            SKCanvas canvas,
            ref Particle particle,
            SKPaint paint)
        {
            var (alpha, size) = CalculateParticleRenderParameters(particle);
            DrawParticle(canvas, particle, alpha, size, paint);
        }

        private static (byte alpha, float size) CalculateParticleRenderParameters(
            Particle particle)
        {
            float lifetime = Clamp(particle.Lifetime, 0f, 1f);

            return (
                alpha: CalculateParticleAlpha(lifetime),
                size: CalculateRenderSize(particle.Size, lifetime)
            );
        }

        private static byte CalculateParticleAlpha(float lifetime) =>
            (byte)(MAX_ALPHA_BYTE * lifetime * lifetime);

        private static float CalculateRenderSize(float baseSize, float lifetime) =>
            baseSize * (PARTICLE_LIFETIME_SQUARED_FACTOR + PARTICLE_LIFETIME_OFFSET * lifetime);

        private static void DrawParticle(
            SKCanvas canvas,
            Particle particle,
            byte alpha,
            float size,
            SKPaint paint)
        {
            paint.Color = paint.Color.WithAlpha(alpha);
            canvas.DrawCircle(particle.X, particle.Y, size, paint);
        }

        public Particle[] GetActiveParticles() =>
            _particles[.._count];
    }
}