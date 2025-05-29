//#nullable enable

//using static System.MathF;
//using static SpectrumNet.SN.Visualization.Renderers.RaindropsRenderer.Constants;

//namespace SpectrumNet.SN.Visualization.Renderers;

//public sealed class RaindropsRenderer : EffectSpectrumRenderer
//{
//    private const string LogPrefix = nameof(RaindropsRenderer);

//    private static readonly Lazy<RaindropsRenderer> _instance =
//        new(() => new RaindropsRenderer());

//    private RaindropsRenderer() { }

//    public static RaindropsRenderer GetInstance() => _instance.Value;

//    public static class Constants
//    {
//        public const float
//            TARGET_DELTA_TIME = 0.016f,
//            SMOOTH_FACTOR = 0.2f,
//            TRAIL_LENGTH_MULTIPLIER = 0.15f,
//            TRAIL_LENGTH_SIZE_FACTOR = 5f,
//            TRAIL_STROKE_MULTIPLIER = 0.6f,
//            TRAIL_OPACITY_MULTIPLIER = 150f,
//            TRAIL_INTENSITY_THRESHOLD = 0.3f,
//            GRAVITY = 9.8f,
//            LIFETIME_DECAY = 0.4f,
//            SPLASH_REBOUND = 0.5f,
//            SPLASH_VELOCITY_THRESHOLD = 1.0f,
//            SPAWN_INTERVAL = 0.05f,
//            FALLSPEED_THRESHOLD_MULTIPLIER = 1.5f,
//            RAINDROP_SIZE_THRESHOLD_MULTIPLIER = 0.9f,
//            RAINDROP_SIZE_HIGHLIGHT_THRESHOLD = 0.8f,
//            INTENSITY_HIGHLIGHT_THRESHOLD = 0.4f,
//            HIGHLIGHT_SIZE_MULTIPLIER = 0.4f,
//            HIGHLIGHT_OFFSET_MULTIPLIER = 0.2f,
//            PARTICLE_VELOCITY_BASE_MULTIPLIER = 0.7f,
//            PARTICLE_VELOCITY_INTENSITY_MULTIPLIER = 0.3f,
//            SPLASH_UPWARD_BASE_MULTIPLIER = 0.8f,
//            SPLASH_UPWARD_INTENSITY_MULTIPLIER = 0.2f,
//            SPLASH_PARTICLE_SIZE_BASE_MULTIPLIER = 0.7f,
//            SPLASH_PARTICLE_SIZE_RANDOM_MULTIPLIER = 0.6f,
//            SPLASH_PARTICLE_SIZE_INTENSITY_MULTIPLIER = 0.5f,
//            SPLASH_PARTICLE_SIZE_INTENSITY_OFFSET = 0.8f,
//            LOUDNESS_SCALE = 4.0f,
//            SPLASH_MIN_INTENSITY = 0.2f,
//            DROP_ALPHA_BASE = 0.7f,
//            DROP_ALPHA_INTENSITY = 0.3f,
//            HIGHLIGHT_ALPHA_MULTIPLIER = 150f,
//            DROP_SIZE_MULTIPLIER = 1.5f,
//            FALL_SPEED_DAMPING = 0.25f;

//        public const int
//            INITIAL_DROP_COUNT = 30,
//            SPLASH_PARTICLE_COUNT_MIN = 3,
//            SPLASH_PARTICLE_COUNT_MAX = 8,
//            MAX_SPAWNS_PER_FRAME = 3;

//        public const byte MAX_ALPHA_BYTE = 255;

//        public static readonly Dictionary<RenderQuality, QualitySettings> QualityPresets = new()
//        {
//            [RenderQuality.Low] = new(false, 4),
//            [RenderQuality.Medium] = new(true, 3),
//            [RenderQuality.High] = new(true, 2)
//        };

//        public record QualitySettings(bool UseAdvancedEffects, int EffectsThreshold);
//    }

//    private static readonly SKColor[] TrailGradientColors = new SKColor[2];
//    private static readonly float[] TrailGradientPositions = [0f, 1f];

//    private QualitySettings _currentSettings = QualityPresets[RenderQuality.Medium];
//    private RenderCache _renderCache = new(1, 1, false);
//    private float _timeSinceLastSpawn;
//    private bool _firstRender = true;
//    private float _actualDeltaTime = TARGET_DELTA_TIME;
//    private float _averageLoudness = 0f;
//    private int _frameCounter = 0;
//    private bool _cacheNeedsUpdate = true;

//    private readonly SKPath _trailPath = new();
//    private readonly Random _random = new();
//    private readonly Stopwatch _frameTimer = new();
//    private readonly ISettings _settings = Settings.Settings.Instance;

//    private Raindrop[] _raindrops = null!;
//    private int _raindropCount;
//    private float[] _smoothedSpectrumCache = null!;
//    private ParticleBuffer _particleBuffer = null!;

//    protected override void OnInitialize()
//    {
//        InitializeArrays();
//        _particleBuffer = new ParticleBuffer(_settings.Particles.MaxParticles, 1);
//        _settings.PropertyChanged += OnSettingsChanged;
//        _frameTimer.Start();
//        LogDebug("Initialized");
//    }

//    private void InitializeArrays()
//    {
//        _raindrops = new Raindrop[_settings.Raindrops.MaxRaindrops];
//        _smoothedSpectrumCache = new float[_settings.Raindrops.MaxRaindrops];
//    }

//    protected override void OnQualitySettingsApplied()
//    {
//        _currentSettings = QualityPresets[Quality];
//        LogDebug($"Quality changed to {Quality}");
//    }

//    protected override void RenderEffect(
//        SKCanvas canvas,
//        float[] spectrum,
//        SKImageInfo info,
//        float barWidth,
//        float barSpacing,
//        int barCount,
//        SKPaint paint) =>
//        RenderRainEffect(canvas, spectrum, info, barCount, paint);

//    private void RenderRainEffect(
//        SKCanvas canvas,
//        float[] spectrum,
//        SKImageInfo info,
//        int barCount,
//        SKPaint paint)
//    {
//        UpdateTiming();
//        UpdateCache(info);
//        InitializeFirstFrame(barCount);
//        UpdateSimulation(spectrum, barCount);
//        RenderFrame(canvas, spectrum, paint);
//    }

//    private void UpdateTiming()
//    {
//        float elapsed = (float)_frameTimer.Elapsed.TotalSeconds;
//        _frameTimer.Restart();
//        _actualDeltaTime = Clamp(
//            TARGET_DELTA_TIME * (elapsed / TARGET_DELTA_TIME),
//            _settings.Raindrops.MinTimeStep,
//            _settings.Raindrops.MaxTimeStep
//        );
//        _timeSinceLastSpawn += _actualDeltaTime;
//        _frameCounter = (_frameCounter + 1) % 2;
//    }

//    private void UpdateCache(SKImageInfo info)
//    {
//        bool needsUpdate = _cacheNeedsUpdate ||
//            _renderCache.Width != info.Width ||
//            _renderCache.Height != info.Height ||
//            _renderCache.IsOverlay != IsOverlayActive;

//        if (!needsUpdate) return;

//        _renderCache = new RenderCache(info.Width, info.Height, IsOverlayActive);
//        _particleBuffer.UpdateBounds(_renderCache.UpperBound, _renderCache.LowerBound);
//        _cacheNeedsUpdate = false;
//    }

//    private void InitializeFirstFrame(int barCount)
//    {
//        if (!_firstRender) return;

//        _raindropCount = 0;
//        int initialCount = Min(INITIAL_DROP_COUNT, _settings.Raindrops.MaxRaindrops);

//        for (int i = 0; i < initialCount; i++)
//            _raindrops[_raindropCount++] = CreateInitialRaindrop(
//                barCount,
//                _renderCache.Width,
//                _renderCache.Height
//            );

//        _firstRender = false;
//    }

//    private Raindrop CreateInitialRaindrop(
//        int barCount,
//        float width,
//        float height) =>
//        new(
//            X: width * (float)_random.NextDouble(),
//            Y: _renderCache.UpperBound + (float)_random.NextDouble() * height * 0.5f,
//            FallSpeed: (_settings.Raindrops.BaseFallSpeed +
//                (float)(_random.NextDouble() * _settings.Raindrops.SpeedVariation)) * FALL_SPEED_DAMPING,
//            Size: _settings.Raindrops.RaindropSize * DROP_SIZE_MULTIPLIER * (0.7f + (float)_random.NextDouble() * 0.6f),
//            Intensity: 0.3f + (float)_random.NextDouble() * 0.3f,
//            SpectrumIndex: _random.Next(barCount)
//        );

//    private void UpdateSimulation(float[] spectrum, int barCount)
//    {
//        ProcessSpectrum(spectrum);
//        UpdateRaindrops(spectrum);

//        if (_frameCounter == 0)
//            _particleBuffer.UpdateParticles(_actualDeltaTime * 2);

//        if (_timeSinceLastSpawn >= SPAWN_INTERVAL)
//        {
//            SpawnNewDrops(spectrum, barCount);
//            _timeSinceLastSpawn = 0;
//        }
//    }

//    private void ProcessSpectrum(float[] spectrum)
//    {
//        float sum = 0f;
//        float blockSize = spectrum.Length / (float)_smoothedSpectrumCache.Length;

//        for (int i = 0; i < _smoothedSpectrumCache.Length; i++)
//        {
//            int start = (int)(i * blockSize);
//            int end = Min((int)((i + 1) * blockSize), spectrum.Length);

//            if (end > start)
//            {
//                float value = CalculateAverageValue(spectrum, start, end);
//                _smoothedSpectrumCache[i] = _smoothedSpectrumCache[i] * (1 - SMOOTH_FACTOR) +
//                    value * SMOOTH_FACTOR;
//                sum += _smoothedSpectrumCache[i];
//            }
//        }

//        _averageLoudness = Clamp(sum / _smoothedSpectrumCache.Length * LOUDNESS_SCALE, 0f, 1f);
//    }

//    private static float CalculateAverageValue(float[] spectrum, int start, int end)
//    {
//        float value = 0f;
//        for (int j = start; j < end; j++)
//            value += spectrum[j];
//        return value / (end - start);
//    }

//    private void UpdateRaindrops(float[] spectrum)
//    {
//        int writeIdx = 0;

//        for (int i = 0; i < _raindropCount; i++)
//        {
//            var drop = _raindrops[i];
//            float normalizedY = (drop.Y - _renderCache.UpperBound) / _renderCache.Height;
//            float acceleration = 1f + normalizedY * 0.3f;
//            float newY = drop.Y + drop.FallSpeed * _actualDeltaTime * _settings.Raindrops.TimeScaleFactor * acceleration;

//            if (newY < _renderCache.LowerBound)
//                _raindrops[writeIdx++] = drop with { Y = newY };
//            else
//                HandleRaindropSplash(drop, spectrum);
//        }

//        _raindropCount = writeIdx;
//    }

//    private void HandleRaindropSplash(Raindrop drop, float[] spectrum)
//    {
//        float intensity = drop.SpectrumIndex < spectrum.Length ?
//            spectrum[drop.SpectrumIndex] : drop.Intensity;

//        if (intensity > SPLASH_MIN_INTENSITY)
//            _particleBuffer.CreateSplashParticles(drop.X, _renderCache.LowerBound, intensity);
//    }

//    private void SpawnNewDrops(float[] spectrum, int barCount)
//    {
//        if (barCount <= 0 || spectrum.Length == 0) return;

//        float stepWidth = _renderCache.Width / barCount;
//        float threshold = IsOverlayActive ?
//            _settings.Particles.SpawnThresholdOverlay : _settings.Particles.SpawnThresholdNormal;
//        float spawnBoost = 1.0f + _averageLoudness * 2.0f;
//        int spawns = 0;

//        for (int i = 0; i < Min(barCount, spectrum.Length) &&
//             spawns < MAX_SPAWNS_PER_FRAME &&
//             _raindropCount < _raindrops.Length; i++)
//        {
//            if (ShouldSpawnDrop(spectrum[i], threshold, spawnBoost))
//            {
//                _raindrops[_raindropCount++] = CreateRaindrop(i, spectrum[i], stepWidth);
//                spawns++;
//            }
//        }
//    }

//    private bool ShouldSpawnDrop(float intensity, float threshold, float spawnBoost) =>
//        intensity > threshold &&
//        _random.NextDouble() < _settings.Particles.SpawnProbability * intensity * spawnBoost;

//    private Raindrop CreateRaindrop(int index, float intensity, float stepWidth)
//    {
//        float x = index * stepWidth + stepWidth * 0.5f +
//            (float)(_random.NextDouble() * stepWidth * 0.5f - stepWidth * 0.25f);

//        return new Raindrop(
//            X: x,
//            Y: _renderCache.UpperBound,
//            FallSpeed: _settings.Raindrops.BaseFallSpeed *
//                (1f + intensity * _settings.Raindrops.IntensitySpeedMultiplier) *
//                FALL_SPEED_DAMPING +
//                (float)(_random.NextDouble() * _settings.Raindrops.SpeedVariation),
//            Size: _settings.Raindrops.RaindropSize * DROP_SIZE_MULTIPLIER *
//                (0.8f + intensity * 0.4f) *
//                (0.9f + (float)_random.NextDouble() * 0.2f),
//            Intensity: intensity,
//            SpectrumIndex: index
//        );
//    }

//    private void RenderFrame(SKCanvas canvas, float[] spectrum, SKPaint paint)
//    {
//        _particleBuffer.RenderParticles(canvas, paint);

//        if (ShouldRenderTrails())
//            RenderTrails(canvas, spectrum, paint);

//        RenderDrops(canvas, spectrum, paint);
//    }

//    private bool ShouldRenderTrails() =>
//        _currentSettings.EffectsThreshold < 3 && _averageLoudness > 0.3f;

//    private void RenderTrails(SKCanvas canvas, float[] spectrum, SKPaint basePaint)
//    {
//        using var trailPaint = CreateTrailPaint(basePaint);

//        for (int i = 0; i < _raindropCount; i++)
//        {
//            var drop = _raindrops[i];

//            if (!IsTrailEligible(drop)) continue;

//            float intensity = drop.SpectrumIndex < spectrum.Length ?
//                spectrum[drop.SpectrumIndex] : drop.Intensity;

//            if (intensity < TRAIL_INTENSITY_THRESHOLD) continue;

//            RenderSingleTrail(canvas, drop, intensity, trailPaint);
//        }
//    }

//    private void RenderSingleTrail(
//        SKCanvas canvas,
//        Raindrop drop,
//        float intensity,
//        SKPaint trailPaint)
//    {
//        float trailLength = MathF.Min(
//            drop.FallSpeed * TRAIL_LENGTH_MULTIPLIER * intensity,
//            drop.Size * TRAIL_LENGTH_SIZE_FACTOR
//        );

//        trailPaint.Color = trailPaint.Color.WithAlpha(
//            (byte)(TRAIL_OPACITY_MULTIPLIER * intensity)
//        );
//        trailPaint.StrokeWidth = drop.Size * TRAIL_STROKE_MULTIPLIER;

//        _trailPath.Reset();
//        _trailPath.MoveTo(drop.X, drop.Y);
//        _trailPath.LineTo(drop.X, drop.Y - trailLength);

//        if (!canvas.QuickReject(_trailPath.Bounds))
//            canvas.DrawPath(_trailPath, trailPaint);
//    }

//    private SKPaint CreateTrailPaint(SKPaint basePaint)
//    {
//        var paint = GetPaint();
//        paint.Style = SKPaintStyle.Stroke;
//        paint.StrokeCap = SKStrokeCap.Round;
//        paint.IsAntialias = UseAntiAlias;
//        paint.Color = basePaint.Color;

//        if (UseAdvancedEffects)
//        {
//            TrailGradientColors[0] = basePaint.Color;
//            TrailGradientColors[1] = basePaint.Color.WithAlpha(0);
//            paint.Shader = SKShader.CreateLinearGradient(
//                new SKPoint(0, 0),
//                new SKPoint(0, 10),
//                TrailGradientColors,
//                TrailGradientPositions,
//                SKShaderTileMode.Clamp
//            );
//        }

//        return paint;
//    }

//    private bool IsTrailEligible(Raindrop drop) =>
//        drop.FallSpeed >= _settings.Raindrops.BaseFallSpeed * FALLSPEED_THRESHOLD_MULTIPLIER &&
//        drop.Size >= _settings.Raindrops.RaindropSize * RAINDROP_SIZE_THRESHOLD_MULTIPLIER;

//    private void RenderDrops(SKCanvas canvas, float[] spectrum, SKPaint basePaint)
//    {
//        using var dropPaint = CreateStandardPaint(basePaint.Color);
//        using var highlightPaint = _currentSettings.EffectsThreshold < 2 ?
//            CreateStandardPaint(SKColors.White) : null;

//        for (int i = 0; i < _raindropCount; i++)
//            RenderSingleDrop(canvas, _raindrops[i], spectrum, dropPaint, highlightPaint);
//    }

//    private void RenderSingleDrop(
//        SKCanvas canvas,
//        Raindrop drop,
//        float[] spectrum,
//        SKPaint dropPaint,
//        SKPaint? highlightPaint)
//    {
//        float intensity = drop.SpectrumIndex < spectrum.Length ?
//            spectrum[drop.SpectrumIndex] * 0.7f + drop.Intensity * 0.3f :
//            drop.Intensity;

//        byte alpha = (byte)(MAX_ALPHA_BYTE * MathF.Min(
//            DROP_ALPHA_BASE + intensity * DROP_ALPHA_INTENSITY,
//            1.0f
//        ));
//        dropPaint.Color = dropPaint.Color.WithAlpha(alpha);

//        var dropRect = new SKRect(
//            drop.X - drop.Size,
//            drop.Y - drop.Size,
//            drop.X + drop.Size,
//            drop.Y + drop.Size
//        );

//        if (canvas.QuickReject(dropRect)) return;

//        canvas.DrawCircle(drop.X, drop.Y, drop.Size, dropPaint);

//        if (highlightPaint != null && ShouldRenderHighlight(drop, intensity))
//            RenderDropHighlight(canvas, drop, intensity, highlightPaint);
//    }

//    private static void RenderDropHighlight(
//        SKCanvas canvas,
//        Raindrop drop,
//        float intensity,
//        SKPaint highlightPaint)
//    {
//        highlightPaint.Color = SKColors.White.WithAlpha(
//            (byte)(HIGHLIGHT_ALPHA_MULTIPLIER * intensity)
//        );

//        float highlightSize = drop.Size * HIGHLIGHT_SIZE_MULTIPLIER;
//        float highlightX = drop.X - drop.Size * HIGHLIGHT_OFFSET_MULTIPLIER;
//        float highlightY = drop.Y - drop.Size * HIGHLIGHT_OFFSET_MULTIPLIER;

//        canvas.DrawCircle(highlightX, highlightY, highlightSize, highlightPaint);
//    }

//    private bool ShouldRenderHighlight(Raindrop drop, float intensity) =>
//        drop.Size > _settings.Raindrops.RaindropSize * RAINDROP_SIZE_HIGHLIGHT_THRESHOLD &&
//        intensity > INTENSITY_HIGHLIGHT_THRESHOLD;

//    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
//    {
//        switch (e.PropertyName)
//        {
//            case nameof(ISettings.Raindrops):
//                HandleRaindropSettingsChange();
//                break;
//            case nameof(ISettings.Particles):
//                HandleParticleSettingsChange();
//                break;
//        }
//    }

//    private void HandleRaindropSettingsChange()
//    {
//        if (_settings.Raindrops.MaxRaindrops != _raindrops.Length)
//            ResizeRaindropArrays();
//        _cacheNeedsUpdate = true;
//    }

//    private void HandleParticleSettingsChange()
//    {
//        if (_settings.Particles.MaxParticles != _particleBuffer._capacity)
//            _particleBuffer.ResizeBuffer(_settings.Particles.MaxParticles);

//        if (MathF.Abs(_settings.Particles.OverlayHeightMultiplier - _renderCache.OverlayHeightMultiplier) > 0.001f)
//            _cacheNeedsUpdate = true;
//    }

//    private void ResizeRaindropArrays()
//    {
//        var newRaindrops = new Raindrop[_settings.Raindrops.MaxRaindrops];
//        var newSmoothedCache = new float[_settings.Raindrops.MaxRaindrops];

//        int copyCount = Min(_raindropCount, _settings.Raindrops.MaxRaindrops);
//        if (copyCount > 0)
//            Array.Copy(_raindrops, newRaindrops, copyCount);

//        _raindrops = newRaindrops;
//        _smoothedSpectrumCache = newSmoothedCache;
//        _raindropCount = copyCount;
//        _cacheNeedsUpdate = true;
//    }

//    protected override void OnConfigurationChanged()
//    {
//        _cacheNeedsUpdate = true;
//        RequestRedraw();
//    }

//    protected override void CleanupUnusedResources()
//    {
//        if (_raindropCount == 0 && _particleBuffer._count == 0)
//            _firstRender = true;
//    }

//    protected override void OnDispose()
//    {
//        _settings.PropertyChanged -= OnSettingsChanged;
//        _trailPath?.Dispose();
//        LogDebug("Disposed");
//    }

//    private sealed class RenderCache
//    {
//        private readonly ISettings _settings = Settings.Settings.Instance;

//        public float Width { get; }
//        public float Height { get; }
//        public bool IsOverlay { get; }
//        public float StepSize { get; }
//        public float OverlayHeightMultiplier { get; }
//        public float OverlayHeight { get; }
//        public float UpperBound { get; }
//        public float LowerBound { get; }

//        public RenderCache(float width, float height, bool isOverlay)
//        {
//            Width = width;
//            Height = height;
//            IsOverlay = isOverlay;
//            StepSize = width / _settings.Raindrops.MaxRaindrops;
//            OverlayHeightMultiplier = _settings.Particles.OverlayHeightMultiplier;
//            OverlayHeight = isOverlay ? height * _settings.Particles.OverlayHeightMultiplier : 0f;

//            UpperBound = 0f;
//            LowerBound = height;
//        }
//    }

//    private readonly record struct Raindrop(
//        float X,
//        float Y,
//        float FallSpeed,
//        float Size,
//        float Intensity,
//        int SpectrumIndex);

//    private struct Particle(
//        float x,
//        float y,
//        float velocityX,
//        float velocityY,
//        float size,
//        bool isSplash)
//    {
//        public float X = x;
//        public float Y = y;
//        public float VelocityX = velocityX;
//        public float VelocityY = velocityY;
//        public float Lifetime = 1.0f;
//        public float Size = size;
//        public readonly bool IsSplash = isSplash;

//        public bool Update(float deltaTime, float upperBound, float lowerBound)
//        {
//            X += VelocityX * deltaTime;
//            Y += VelocityY * deltaTime;
//            VelocityY += deltaTime * GRAVITY;

//            if (IsSplash && Y >= lowerBound - 1)
//            {
//                Y = lowerBound - 1;
//                VelocityY *= -SPLASH_REBOUND;
//                if (MathF.Abs(VelocityY) < SPLASH_VELOCITY_THRESHOLD)
//                    VelocityY = 0;
//            }

//            Lifetime -= deltaTime * LIFETIME_DECAY;
//            return Lifetime > 0 && Y < lowerBound + 50;
//        }
//    }

//    private sealed class ParticleBuffer(int capacity, float lowerBound)
//    {
//        private Particle[] _particles = new Particle[capacity];
//        internal int _count;
//        internal int _capacity = capacity;
//        private float _upperBound;
//        private float _lowerBound = lowerBound;
//        private readonly ISettings _settings = Settings.Settings.Instance;
//        private readonly Random _random = Random.Shared;

//        public void UpdateBounds(float upperBound, float lowerBound) =>
//            (_upperBound, _lowerBound) = (upperBound, lowerBound);

//        public void ResizeBuffer(int newCapacity)
//        {
//            if (newCapacity <= 0) return;

//            var newParticles = new Particle[newCapacity];
//            int copyCount = Min(_count, newCapacity);

//            if (copyCount > 0)
//                Array.Copy(_particles, newParticles, copyCount);

//            _particles = newParticles;
//            _capacity = newCapacity;
//            _count = copyCount;
//        }

//        public void UpdateParticles(float deltaTime)
//        {
//            int writeIndex = 0;

//            for (int i = 0; i < _count; i++)
//            {
//                if (_particles[i].Update(deltaTime, _upperBound, _lowerBound))
//                {
//                    if (writeIndex != i)
//                        _particles[writeIndex] = _particles[i];
//                    writeIndex++;
//                }
//            }

//            _count = writeIndex;
//        }

//        public void CreateSplashParticles(float x, float y, float intensity)
//        {
//            int count = CalculateSplashCount();
//            if (count <= 0) return;

//            float velocityMax = CalculateVelocityMax(intensity);
//            float upwardForce = CalculateUpwardForce(intensity);

//            for (int i = 0; i < count; i++)
//                _particles[_count++] = CreateSplashParticle(
//                    x, y - 2, intensity, velocityMax, upwardForce);
//        }

//        private int CalculateSplashCount() =>
//            Min(_random.Next(SPLASH_PARTICLE_COUNT_MIN, SPLASH_PARTICLE_COUNT_MAX),
//                _particles.Length - _count);

//        private float CalculateVelocityMax(float intensity) =>
//            _settings.Particles.ParticleVelocityMax *
//            (PARTICLE_VELOCITY_BASE_MULTIPLIER +
//             intensity * PARTICLE_VELOCITY_INTENSITY_MULTIPLIER);

//        private float CalculateUpwardForce(float intensity) =>
//            _settings.Raindrops.SplashUpwardForce *
//            (SPLASH_UPWARD_BASE_MULTIPLIER +
//             intensity * SPLASH_UPWARD_INTENSITY_MULTIPLIER);

//        private Particle CreateSplashParticle(
//            float x,
//            float y,
//            float intensity,
//            float velocityMax,
//            float upwardForce)
//        {
//            float angle = (float)(_random.NextDouble() * MathF.PI * 2);
//            float speed = (float)(_random.NextDouble() * velocityMax);
//            float size = CalculateParticleSize(intensity);

//            return new Particle(
//                x,
//                y,
//                Cos(angle) * speed,
//                Sin(angle) * speed - upwardForce,
//                size,
//                true
//            );
//        }

//        private float CalculateParticleSize(float intensity) =>
//            _settings.Raindrops.SplashParticleSize *
//            (SPLASH_PARTICLE_SIZE_BASE_MULTIPLIER +
//             (float)_random.NextDouble() * SPLASH_PARTICLE_SIZE_RANDOM_MULTIPLIER) *
//            (intensity * SPLASH_PARTICLE_SIZE_INTENSITY_MULTIPLIER +
//             SPLASH_PARTICLE_SIZE_INTENSITY_OFFSET);

//        public void RenderParticles(SKCanvas canvas, SKPaint basePaint)
//        {
//            if (_count == 0) return;

//            using var paint = CreateParticlePaint(basePaint);

//            for (int i = 0; i < _count; i++)
//                RenderSingleParticle(
//                    canvas,
//                    ref _particles[i],
//                    paint,
//                    basePaint.Color);
//        }

//        private static SKPaint CreateParticlePaint(SKPaint basePaint)
//        {
//            var paint = basePaint.Clone();
//            paint.Style = SKPaintStyle.Fill;
//            paint.IsAntialias = true;
//            return paint;
//        }

//        private static void RenderSingleParticle(
//            SKCanvas canvas,
//            ref Particle p,
//            SKPaint paint,
//            SKColor baseColor)
//        {
//            float lifetime = Clamp(p.Lifetime, 0f, 1f);
//            paint.Color = baseColor.WithAlpha(CalculateParticleAlpha(lifetime));
//            canvas.DrawCircle(p.X, p.Y, CalculateRenderSize(p.Size, lifetime), paint);
//        }

//        private static byte CalculateParticleAlpha(float lifetime) =>
//            (byte)(MAX_ALPHA_BYTE * lifetime * lifetime);

//        private static float CalculateRenderSize(float baseSize, float lifetime) =>
//            baseSize * (0.8f + 0.2f * lifetime);
//    }
//}