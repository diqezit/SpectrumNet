#nullable enable

using static System.MathF;
using static SpectrumNet.SN.Visualization.Renderers.RaindropsRenderer.Constants;

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class RaindropsRenderer : EffectSpectrumRenderer
{
    private const string LogPrefix = nameof(RaindropsRenderer);

    private static readonly Lazy<RaindropsRenderer> _instance =
        new(() => new RaindropsRenderer());

    private RaindropsRenderer() { }

    public static RaindropsRenderer GetInstance() => _instance.Value;

    public static class Constants
    {
        public const float
            TARGET_DELTA_TIME = 0.016f,
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
            HIGHLIGHT_ALPHA_MULTIPLIER = 150f;

        public const int
            INITIAL_DROP_COUNT = 30,
            SPLASH_PARTICLE_COUNT_MIN = 3,
            SPLASH_PARTICLE_COUNT_MAX = 8,
            MAX_SPAWNS_PER_FRAME = 3;

        public const byte MAX_ALPHA_BYTE = 255;

        public static readonly Dictionary<RenderQuality, QualitySettings> QualityPresets = new()
        {
            [RenderQuality.Low] = new(
                UseAdvancedEffects: false,
                EffectsThreshold: 4
            ),
            [RenderQuality.Medium] = new(
                UseAdvancedEffects: true,
                EffectsThreshold: 3
            ),
            [RenderQuality.High] = new(
                UseAdvancedEffects: true,
                EffectsThreshold: 2
            )
        };

        public record QualitySettings(
            bool UseAdvancedEffects,
            int EffectsThreshold
        );
    }

    private static readonly SKColor[] TrailGradientColors = new SKColor[2];
    private static readonly float[] TrailGradientPositions = [0f, 1f];

    private QualitySettings _currentSettings = QualityPresets[RenderQuality.Medium];
    private RenderCache _renderCache = new(1, 1, false);
    private float _timeSinceLastSpawn;
    private bool _firstRender = true;
    private float _actualDeltaTime = TARGET_DELTA_TIME;
    private float _averageLoudness = 0f;
    private int _frameCounter = 0;
    private bool _cacheNeedsUpdate;

    private readonly SKPath _trailPath = new();
    private readonly Random _random = new();
    private readonly Stopwatch _frameTimer = new();
    private readonly ISettings _settings = Settings.Settings.Instance;

    private Raindrop[] _raindrops = null!;
    private int _raindropCount;
    private float[] _smoothedSpectrumCache = null!;
    private ParticleBuffer _particleBuffer = null!;

    protected override void OnInitialize()
    {
        InitializeArrays();
        _particleBuffer = new ParticleBuffer(_settings.MaxParticles, 1);
        _settings.PropertyChanged += OnSettingsChanged;
        _frameTimer.Start();
        _logger.Log(LogLevel.Debug, LogPrefix, "Initialized");
    }

    private void InitializeArrays()
    {
        _raindrops = new Raindrop[_settings.MaxRaindrops];
        _smoothedSpectrumCache = new float[_settings.MaxRaindrops];
    }

    protected override void OnQualitySettingsApplied()
    {
        _currentSettings = QualityPresets[Quality];
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
        RenderRainEffect(canvas, spectrum, info, barCount, paint);
    }

    private void RenderRainEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        int barCount,
        SKPaint paint)
    {
        UpdateTiming();
        UpdateCache(info);
        InitializeFirstFrame(barCount);
        UpdateSimulation(spectrum, barCount);
        RenderFrame(canvas, spectrum, paint);
    }

    private void UpdateTiming()
    {
        float elapsed = (float)_frameTimer.Elapsed.TotalSeconds;
        _frameTimer.Restart();
        _actualDeltaTime = Clamp(
            TARGET_DELTA_TIME * (elapsed / TARGET_DELTA_TIME),
            _settings.MinTimeStep,
            _settings.MaxTimeStep
        );
        _timeSinceLastSpawn += _actualDeltaTime;
        _frameCounter = (_frameCounter + 1) % 2;
    }

    private void UpdateCache(SKImageInfo info)
    {
        if (!_cacheNeedsUpdate &&
            _renderCache.Width == info.Width &&
            _renderCache.Height == info.Height) return;

        _renderCache = new RenderCache(info.Width, info.Height, IsOverlayActive);
        _particleBuffer.UpdateLowerBound(_renderCache.LowerBound);
        _cacheNeedsUpdate = false;
    }

    private void InitializeFirstFrame(int barCount)
    {
        if (!_firstRender) return;

        _raindropCount = 0;
        int initialCount = Min(INITIAL_DROP_COUNT, _settings.MaxRaindrops);

        for (int i = 0; i < initialCount; i++)
        {
            _raindrops[_raindropCount++] = CreateInitialRaindrop(
                barCount,
                _renderCache.Width,
                _renderCache.Height
            );
        }

        _firstRender = false;
    }

    private Raindrop CreateInitialRaindrop(int barCount, float width, float height)
    {
        return new Raindrop(
            X: width * (float)_random.NextDouble(),
            Y: height * (float)_random.NextDouble() * 0.5f,
            FallSpeed: _settings.BaseFallSpeed +
                (float)(_random.NextDouble() * _settings.SpeedVariation),
            Size: _settings.RaindropSize * (0.7f + (float)_random.NextDouble() * 0.6f),
            Intensity: 0.3f + (float)_random.NextDouble() * 0.3f,
            SpectrumIndex: _random.Next(barCount)
        );
    }

    private void UpdateSimulation(float[] spectrum, int barCount)
    {
        ProcessSpectrum(spectrum);
        UpdateRaindrops(spectrum);

        if (_frameCounter == 0)
            _particleBuffer.UpdateParticles(_actualDeltaTime * 2);

        if (_timeSinceLastSpawn >= SPAWN_INTERVAL)
        {
            SpawnNewDrops(spectrum, barCount);
            _timeSinceLastSpawn = 0;
        }
    }

    private void ProcessSpectrum(float[] spectrum)
    {
        float sum = 0f;
        float blockSize = spectrum.Length / (float)_smoothedSpectrumCache.Length;

        for (int i = 0; i < _smoothedSpectrumCache.Length; i++)
        {
            int start = (int)(i * blockSize);
            int end = Min((int)((i + 1) * blockSize), spectrum.Length);

            if (end > start)
            {
                float value = 0f;
                for (int j = start; j < end; j++)
                    value += spectrum[j];

                value /= (end - start);
                _smoothedSpectrumCache[i] = _smoothedSpectrumCache[i] * (1 - SMOOTH_FACTOR) +
                    value * SMOOTH_FACTOR;
                sum += _smoothedSpectrumCache[i];
            }
        }

        _averageLoudness = Clamp(sum / _smoothedSpectrumCache.Length * LOUDNESS_SCALE, 0f, 1f);
    }

    private void UpdateRaindrops(float[] spectrum)
    {
        int writeIdx = 0;

        for (int i = 0; i < _raindropCount; i++)
        {
            var drop = _raindrops[i];
            float newY = drop.Y + drop.FallSpeed * _actualDeltaTime;

            if (newY < _renderCache.LowerBound)
            {
                _raindrops[writeIdx++] = drop with { Y = newY };
            }
            else
            {
                float intensity = drop.SpectrumIndex < spectrum.Length ?
                    spectrum[drop.SpectrumIndex] : drop.Intensity;

                if (intensity > SPLASH_MIN_INTENSITY)
                    _particleBuffer.CreateSplashParticles(drop.X, _renderCache.LowerBound, intensity);
            }
        }

        _raindropCount = writeIdx;
    }

    private void SpawnNewDrops(float[] spectrum, int barCount)
    {
        if (barCount <= 0 || spectrum.Length == 0) return;

        float stepWidth = _renderCache.Width / barCount;
        float threshold = IsOverlayActive ?
            _settings.SpawnThresholdOverlay : _settings.SpawnThresholdNormal;
        float spawnBoost = 1.0f + _averageLoudness * 2.0f;
        int spawns = 0;

        for (int i = 0; i < Min(barCount, spectrum.Length) &&
             spawns < MAX_SPAWNS_PER_FRAME &&
             _raindropCount < _raindrops.Length; i++)
        {
            if (spectrum[i] > threshold &&
                _random.NextDouble() < _settings.SpawnProbability * spectrum[i] * spawnBoost)
            {
                _raindrops[_raindropCount++] = CreateRaindrop(i, spectrum[i], stepWidth);
                spawns++;
            }
        }
    }

    private Raindrop CreateRaindrop(int index, float intensity, float stepWidth)
    {
        float x = index * stepWidth + stepWidth * 0.5f +
            (float)(_random.NextDouble() * stepWidth * 0.5f - stepWidth * 0.25f);

        return new Raindrop(
            X: x,
            Y: _renderCache.UpperBound,
            FallSpeed: _settings.BaseFallSpeed *
                (1f + intensity * _settings.IntensitySpeedMultiplier) +
                (float)(_random.NextDouble() * _settings.SpeedVariation),
            Size: _settings.RaindropSize * (0.8f + intensity * 0.4f) *
                (0.9f + (float)_random.NextDouble() * 0.2f),
            Intensity: intensity,
            SpectrumIndex: index
        );
    }

    private void RenderFrame(SKCanvas canvas, float[] spectrum, SKPaint paint)
    {
        _particleBuffer.RenderParticles(canvas, paint);

        if (ShouldRenderTrails())
            RenderTrails(canvas, spectrum, paint);

        RenderDrops(canvas, spectrum, paint);
    }

    private bool ShouldRenderTrails() =>
        _currentSettings.EffectsThreshold < 3 && _averageLoudness > 0.3f;

    private void RenderTrails(SKCanvas canvas, float[] spectrum, SKPaint basePaint)
    {
        using var trailPaint = CreateTrailPaint(basePaint);

        for (int i = 0; i < _raindropCount; i++)
        {
            var drop = _raindrops[i];

            if (!IsTrailEligible(drop)) continue;

            float intensity = drop.SpectrumIndex < spectrum.Length ?
                spectrum[drop.SpectrumIndex] : drop.Intensity;

            if (intensity < TRAIL_INTENSITY_THRESHOLD) continue;

            float trailLength = MathF.Min(
                drop.FallSpeed * TRAIL_LENGTH_MULTIPLIER * intensity,
                drop.Size * TRAIL_LENGTH_SIZE_FACTOR
            );

            trailPaint.Color = basePaint.Color.WithAlpha(
                (byte)(TRAIL_OPACITY_MULTIPLIER * intensity)
            );
            trailPaint.StrokeWidth = drop.Size * TRAIL_STROKE_MULTIPLIER;

            _trailPath.Reset();
            _trailPath.MoveTo(drop.X, drop.Y);
            _trailPath.LineTo(drop.X, drop.Y - trailLength);

            if (!canvas.QuickReject(_trailPath.Bounds))
                canvas.DrawPath(_trailPath, trailPaint);
        }
    }

    private SKPaint CreateTrailPaint(SKPaint basePaint)
    {
        var paint = _resourceManager.GetPaint();
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeCap = SKStrokeCap.Round;
        paint.IsAntialias = UseAntiAlias;
        paint.Color = basePaint.Color;

        if (UseAdvancedEffects)
        {
            TrailGradientColors[0] = basePaint.Color;
            TrailGradientColors[1] = basePaint.Color.WithAlpha(0);
            paint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(0, 10),
                TrailGradientColors,
                TrailGradientPositions,
                SKShaderTileMode.Clamp
            );
        }

        return paint;
    }

    private bool IsTrailEligible(Raindrop drop) =>
        drop.FallSpeed >= _settings.BaseFallSpeed * FALLSPEED_THRESHOLD_MULTIPLIER &&
        drop.Size >= _settings.RaindropSize * RAINDROP_SIZE_THRESHOLD_MULTIPLIER;

    private void RenderDrops(SKCanvas canvas, float[] spectrum, SKPaint basePaint)
    {
        using var dropPaint = CreateStandardPaint(basePaint.Color);
        using var highlightPaint = _currentSettings.EffectsThreshold < 2 ?
            CreateStandardPaint(SKColors.White) : null;

        for (int i = 0; i < _raindropCount; i++)
        {
            var drop = _raindrops[i];
            float intensity = drop.SpectrumIndex < spectrum.Length ?
                spectrum[drop.SpectrumIndex] * 0.7f + drop.Intensity * 0.3f :
                drop.Intensity;

            byte alpha = (byte)(MAX_ALPHA_BYTE * MathF.Min(
                DROP_ALPHA_BASE + intensity * DROP_ALPHA_INTENSITY,
                1.0f
            ));
            dropPaint.Color = basePaint.Color.WithAlpha(alpha);

            var dropRect = new SKRect(
                drop.X - drop.Size,
                drop.Y - drop.Size,
                drop.X + drop.Size,
                drop.Y + drop.Size
            );

            if (!canvas.QuickReject(dropRect))
            {
                canvas.DrawCircle(drop.X, drop.Y, drop.Size, dropPaint);

                if (highlightPaint != null && ShouldRenderHighlight(drop, intensity))
                {
                    highlightPaint.Color = SKColors.White.WithAlpha(
                        (byte)(HIGHLIGHT_ALPHA_MULTIPLIER * intensity)
                    );

                    float highlightSize = drop.Size * HIGHLIGHT_SIZE_MULTIPLIER;
                    float highlightX = drop.X - drop.Size * HIGHLIGHT_OFFSET_MULTIPLIER;
                    float highlightY = drop.Y - drop.Size * HIGHLIGHT_OFFSET_MULTIPLIER;

                    canvas.DrawCircle(highlightX, highlightY, highlightSize, highlightPaint);
                }
            }
        }
    }

    private bool ShouldRenderHighlight(Raindrop drop, float intensity) =>
        drop.Size > _settings.RaindropSize * RAINDROP_SIZE_HIGHLIGHT_THRESHOLD &&
        intensity > INTENSITY_HIGHLIGHT_THRESHOLD;

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(_settings.MaxRaindrops):
                ResizeRaindropArrays();
                break;
            case nameof(_settings.MaxParticles):
                _particleBuffer.ResizeBuffer(_settings.MaxParticles);
                break;
            case nameof(_settings.OverlayHeightMultiplier):
                _cacheNeedsUpdate = true;
                break;
        }
    }

    private void ResizeRaindropArrays()
    {
        var newRaindrops = new Raindrop[_settings.MaxRaindrops];
        var newSmoothedCache = new float[_settings.MaxRaindrops];

        int copyCount = Min(_raindropCount, _settings.MaxRaindrops);
        if (copyCount > 0)
            Array.Copy(_raindrops, newRaindrops, copyCount);

        _raindrops = newRaindrops;
        _smoothedSpectrumCache = newSmoothedCache;
        _raindropCount = copyCount;
        _cacheNeedsUpdate = true;
    }

    protected override void OnConfigurationChanged()
    {
        _cacheNeedsUpdate = true;
        RequestRedraw();
    }

    protected override void CleanupUnusedResources()
    {
        if (_raindropCount == 0 && _particleBuffer._count == 0)
        {
            _firstRender = true;
        }
    }

    protected override void OnDispose()
    {
        _settings.PropertyChanged -= OnSettingsChanged;
        _trailPath?.Dispose();
        _logger.Log(LogLevel.Debug, LogPrefix, "Disposed");
    }

    private sealed class RenderCache(float width, float height, bool isOverlay)
    {
        public float Width { get; } = width;
        public float Height { get; } = height;
        public float StepSize { get; } = width / Settings.Settings.Instance.MaxRaindrops;
        public float OverlayHeight { get; } = isOverlay ?
            height * Settings.Settings.Instance.OverlayHeightMultiplier : 0f;
        public float UpperBound { get; } = isOverlay ?
            height - height * Settings.Settings.Instance.OverlayHeightMultiplier : 0f;
        public float LowerBound { get; } = height;
    }

    private readonly record struct Raindrop(
        float X,
        float Y,
        float FallSpeed,
        float Size,
        float Intensity,
        int SpectrumIndex
    );

    private struct Particle(
        float x,
        float y,
        float velocityX,
        float velocityY,
        float size,
        bool isSplash)
    {
        public float
            X = x,
            Y = y,
            VelocityX = velocityX,
            VelocityY = velocityY,
            Lifetime = 1.0f,
            Size = size;

        public bool IsSplash = isSplash;

        public bool Update(float deltaTime, float lowerBound)
        {
            X += VelocityX * deltaTime;
            Y += VelocityY * deltaTime;
            VelocityY += deltaTime * GRAVITY;

            if (IsSplash && Y >= lowerBound)
            {
                Y = lowerBound;
                VelocityY = -VelocityY * SPLASH_REBOUND;
                if (MathF.Abs(VelocityY) < SPLASH_VELOCITY_THRESHOLD)
                    VelocityY = 0;
            }

            Lifetime -= deltaTime * LIFETIME_DECAY;
            return Lifetime > 0;
        }
    }

    private sealed class ParticleBuffer(int capacity, float lowerBound)
    {
        private Particle[] _particles = new Particle[capacity];
        internal int _count = 0;
        private float _lowerBound = lowerBound;

        public void UpdateLowerBound(float lowerBound) => _lowerBound = lowerBound;

        public void ResizeBuffer(int newCapacity)
        {
            if (newCapacity <= 0) return;

            var newParticles = new Particle[newCapacity];
            int copyCount = Min(_count, newCapacity);

            if (copyCount > 0)
                Array.Copy(_particles, newParticles, copyCount);

            _particles = newParticles;
            _count = copyCount;
        }

        public void UpdateParticles(float deltaTime)
        {
            int writeIndex = 0;

            for (int i = 0; i < _count; i++)
            {
                if (_particles[i].Update(deltaTime, _lowerBound))
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
            int count = Min(
                Random.Shared.Next(SPLASH_PARTICLE_COUNT_MIN, SPLASH_PARTICLE_COUNT_MAX),
                _particles.Length - _count
            );

            if (count <= 0) return;

            float velocityMax = Settings.Settings.Instance.ParticleVelocityMax *
                (PARTICLE_VELOCITY_BASE_MULTIPLIER +
                 intensity * PARTICLE_VELOCITY_INTENSITY_MULTIPLIER);

            float upwardForce = Settings.Settings.Instance.SplashUpwardForce *
                (SPLASH_UPWARD_BASE_MULTIPLIER +
                 intensity * SPLASH_UPWARD_INTENSITY_MULTIPLIER);

            for (int i = 0; i < count; i++)
            {
                float angle = (float)(Random.Shared.NextDouble() * MathF.PI * 2);
                float speed = (float)(Random.Shared.NextDouble() * velocityMax);
                float size = Settings.Settings.Instance.SplashParticleSize *
                    (SPLASH_PARTICLE_SIZE_BASE_MULTIPLIER +
                     (float)Random.Shared.NextDouble() * SPLASH_PARTICLE_SIZE_RANDOM_MULTIPLIER) *
                    (intensity * SPLASH_PARTICLE_SIZE_INTENSITY_MULTIPLIER +
                     SPLASH_PARTICLE_SIZE_INTENSITY_OFFSET);

                _particles[_count++] = new Particle(
                    x,
                    y,
                    Cos(angle) * speed,
                    Sin(angle) * speed - upwardForce,
                    size,
                    true
                );
            }
        }

        public void RenderParticles(SKCanvas canvas, SKPaint basePaint)
        {
            if (_count == 0) return;

            using var paint = basePaint.Clone();
            paint.Style = SKPaintStyle.Fill;
            paint.IsAntialias = true;

            for (int i = 0; i < _count; i++)
            {
                ref var p = ref _particles[i];
                float lifetime = Clamp(p.Lifetime, 0f, 1f);

                paint.Color = basePaint.Color.WithAlpha(
                    (byte)(MAX_ALPHA_BYTE * lifetime * lifetime)
                );

                float sizeMultiplier = 0.8f + 0.2f * lifetime;
                canvas.DrawCircle(p.X, p.Y, p.Size * sizeMultiplier, paint);
            }
        }
    }
}