#nullable enable

using static SpectrumNet.Views.Renderers.RaindropsRenderer.Constants;
using static System.MathF;
using Vector = System.Numerics.Vector;

namespace SpectrumNet.Views.Renderers;

public sealed class RaindropsRenderer : EffectSpectrumRenderer
{
    private static readonly Lazy<RaindropsRenderer> _instance = new(() => new RaindropsRenderer());

#pragma warning disable CS8618 // ill use in InitializeFields(); - so its ok here
    private RaindropsRenderer()
#pragma warning restore CS8618 
    {
        InitializeFields();
        SubscribeToSettingsChanges();
    }

    public static RaindropsRenderer GetInstance() => _instance.Value;

    public record Constants
    {
        public const string LOG_PREFIX = "RaindropsRenderer";

        public const float
            TARGET_DELTA_TIME = 0.016f,
            SMOOTH_FACTOR = 0.2f,
            TRAIL_LENGTH_MULTIPLIER = 0.15f,
            TRAIL_LENGTH_SIZE_FACTOR = 5f,
            TRAIL_STROKE_MULTIPLIER = 0.6f,
            TRAIL_OPACITY_MULTIPLIER = 150f,
            TRAIL_INTENSITY_THRESHOLD = 0.3f;

        public const int INITIAL_DROP_COUNT = 30;

        public const float
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
            HIGHLIGHT_OFFSET_MULTIPLIER = 0.2f;

        public const int
            SPLASH_PARTICLE_COUNT_MIN = 3,
            SPLASH_PARTICLE_COUNT_MAX = 8;

        public const float
            PARTICLE_VELOCITY_BASE_MULTIPLIER = 0.7f,
            PARTICLE_VELOCITY_INTENSITY_MULTIPLIER = 0.3f,
            SPLASH_UPWARD_BASE_MULTIPLIER = 0.8f,
            SPLASH_UPWARD_INTENSITY_MULTIPLIER = 0.2f,
            SPLASH_PARTICLE_SIZE_BASE_MULTIPLIER = 0.7f,
            SPLASH_PARTICLE_SIZE_RANDOM_MULTIPLIER = 0.6f,
            SPLASH_PARTICLE_SIZE_INTENSITY_MULTIPLIER = 0.5f,
            SPLASH_PARTICLE_SIZE_INTENSITY_OFFSET = 0.8f;

        public static class Quality
        {
            public const bool 
                LOW_USE_ADVANCED_EFFECTS = false,
                MEDIUM_USE_ADVANCED_EFFECTS = true,
                HIGH_USE_ADVANCED_EFFECTS = true;

            public const int 
                LOW_EFFECTS_THRESHOLD = 4,
                MEDIUM_EFFECTS_THRESHOLD = 3,
                HIGH_EFFECTS_THRESHOLD = 2;
        }
    }

    private readonly struct RenderCache(float width, float height, bool isOverlay)
    {
        public readonly float 
            Width = width, 
            Height = height, 
            LowerBound = isOverlay ? height * Settings.Instance.OverlayHeightMultiplier : height, 
            UpperBound = 0f, 
            StepSize = width / Settings.Instance.MaxRaindrops;
    }

    private readonly struct Raindrop
    {
        public readonly float X, Y, FallSpeed, Size, Intensity;
        public readonly int SpectrumIndex;

        public Raindrop(
            float x,
            float y,
            float fallSpeed,
            float size,
            float intensity,
            int spectrumIndex) => 
            (X, Y, FallSpeed, Size, Intensity, SpectrumIndex) = (x, y, fallSpeed, size, intensity, spectrumIndex);

        public Raindrop WithNewY(float newY) => 
            new(X, newY, FallSpeed, Size, Intensity, SpectrumIndex);
    }

    private struct Particle
    {
        public float X, Y, VelocityX, VelocityY, Lifetime, Size;
        public bool IsSplash;

        public Particle(
            float x,
            float y,
            float velocityX,
            float velocityY,
            float size,
            bool isSplash) => 
            (X, Y, VelocityX, VelocityY, Lifetime, Size, IsSplash) = (x, y, velocityX, velocityY, 1.0f, size, isSplash);

        public bool Update(float deltaTime, float lowerBound)
        {
            UpdatePosition(deltaTime);
            ApplyGravity(deltaTime);
            HandleSplashCollision(lowerBound);
            return IsAlive();
        }

        private void UpdatePosition(float deltaTime)
        {
            X += VelocityX * deltaTime;
            Y += VelocityY * deltaTime;
        }

        private void ApplyGravity(float deltaTime) => 
            VelocityY += deltaTime * GRAVITY;

        private void HandleSplashCollision(float lowerBound)
        {
            if (!IsSplash || Y < lowerBound) return;
            Y = lowerBound;
            VelocityY = -VelocityY * SPLASH_REBOUND;
            if (MathF.Abs(VelocityY) < SPLASH_VELOCITY_THRESHOLD) VelocityY = 0;
        }

        private readonly bool IsAlive() => Lifetime > 0;

        public void DecayLifetime(float deltaTime) => 
            Lifetime -= deltaTime * LIFETIME_DECAY;
    }

    private sealed class ParticleBuffer(
        int capacity,
        float lowerBound)
    {
        private Particle[] _particles = new Particle[capacity];
        private int _count = 0;
        private float _lowerBound = lowerBound;
        private readonly Random _random = new();

        public void UpdateLowerBound(float lowerBound) => 
            _lowerBound = lowerBound;

        public void ResizeBuffer(int newCapacity) => 
            AdjustBufferSize(newCapacity);

        private void AdjustBufferSize(int newCapacity)
        {
            if (newCapacity <= 0) return;
            var newParticles = new Particle[newCapacity];
            int copyCount = Min(_count, newCapacity);
            if (copyCount > 0) Array.Copy(_particles, newParticles, copyCount);
            _particles = newParticles;
            _count = copyCount;
        }

        public void AddParticle(in Particle particle)
        {
            if (_count < _particles.Length) _particles[_count++] = particle;
        }

        public void Clear() => _count = 0;

        public void UpdateParticles(float deltaTime) => 
            FilterAndUpdateParticles(deltaTime);

        private void FilterAndUpdateParticles(float deltaTime)
        {
            int writeIndex = 0;
            for (int i = 0; i < _count; i++)
            {
                ref Particle p = ref _particles[i];
                p.DecayLifetime(deltaTime);
                if (p.Update(deltaTime, _lowerBound))
                {
                    if (writeIndex != i) _particles[writeIndex] = p;
                    writeIndex++;
                }
            }
            _count = writeIndex;
        }

        public void RenderParticles(SKCanvas canvas, SKPaint basePaint) => 
            DrawParticles(canvas, basePaint);

        private void DrawParticles(SKCanvas canvas, SKPaint basePaint)
        {
            if (_count == 0) return;
            using var splashPaint = ConfigureSplashPaint(basePaint);
            for (int i = 0; i < _count; i++)
                RenderSingleParticle(canvas, splashPaint, ref _particles[i], basePaint);
        }

        private static SKPaint ConfigureSplashPaint(SKPaint basePaint)
        {
            var paint = basePaint.Clone();
            paint.Style = SKPaintStyle.Fill;
            paint.IsAntialias = true;
            return paint;
        }

        private static void RenderSingleParticle(SKCanvas canvas,
                                         SKPaint paint,
                                         ref Particle p,
                                         SKPaint basePaint)
        {
            float clampedLifetime = Clamp(p.Lifetime, 0f, 1f);
            paint.Color = basePaint.Color.WithAlpha(CalculateParticleAlpha(clampedLifetime));
            float sizeMultiplier = CalculateParticleSizeMultiplier(clampedLifetime);
            canvas.DrawCircle(p.X, p.Y, p.Size * sizeMultiplier, paint);
        }

        private static byte CalculateParticleAlpha(float clampedLifetime)
            => (byte)(255 * clampedLifetime * clampedLifetime);

        private static float CalculateParticleSizeMultiplier(float clampedLifetime)
            => 0.8f + 0.2f * clampedLifetime;

        public void CreateSplashParticles(float x, float y, float intensity, Random random)
            => GenerateSplashParticles(x, y, intensity, random);

        private void GenerateSplashParticles(float x, float y, float intensity, Random random)
        {
            int count = CalculateSplashParticleCount(random, intensity);
            if (count <= 0) return;
            float velocityMax = CalculateParticleVelocityMax(intensity);
            float upwardForce = CalculateUpwardForce(intensity);
            for (int i = 0; i < count; i++)
                AddSplashParticle(x, y, velocityMax, upwardForce, intensity, random);
        }

        private int CalculateSplashParticleCount(Random random, float _) // intensity
            => Min(random.Next(SPLASH_PARTICLE_COUNT_MIN, SPLASH_PARTICLE_COUNT_MAX),
                   Settings.Instance.MaxParticles - _count);

        private void AddSplashParticle(
            float x,
            float y,
            float velocityMax,
            float upwardForce,
            float intensity,
            Random random)
        {
            float angle = GenerateParticleAngle(random);
            float speed = GenerateParticleSpeed(random, velocityMax);
            float size = CalculateParticleSize(intensity, random);
            AddParticle(CreateParticle(x, y, angle, speed, upwardForce, size));
        }

        private static float GenerateParticleAngle(Random random)
            => (float)(random.NextDouble() * MathF.PI * 2);

        private static float GenerateParticleSpeed(Random random, float velocityMax)
            => (float)(random.NextDouble() * velocityMax);

        private static Particle CreateParticle(
            float x,
            float y,
            float angle,
            float speed,
            float upwardForce,
            float size) => 
            new(x,
                y,
                Cos(angle) * speed,
                Sin(angle) * speed - upwardForce,
                size,
                true);

        private static float CalculateParticleVelocityMax(float intensity)
            => Settings.Instance.ParticleVelocityMax *
               (PARTICLE_VELOCITY_BASE_MULTIPLIER + intensity * PARTICLE_VELOCITY_INTENSITY_MULTIPLIER);

        private static float CalculateUpwardForce(float intensity)
            => Settings.Instance.SplashUpwardForce *
               (SPLASH_UPWARD_BASE_MULTIPLIER + intensity * SPLASH_UPWARD_INTENSITY_MULTIPLIER);

        private static float CalculateParticleSize(float intensity, Random random)
            => Settings.Instance.SplashParticleSize *
               (SPLASH_PARTICLE_SIZE_BASE_MULTIPLIER +
                (float)random.NextDouble() * SPLASH_PARTICLE_SIZE_RANDOM_MULTIPLIER) *
               (intensity * SPLASH_PARTICLE_SIZE_INTENSITY_MULTIPLIER +
                SPLASH_PARTICLE_SIZE_INTENSITY_OFFSET);
    }

    private readonly SKPath _trailPath = new();
    private RenderCache _renderCache;
    private Raindrop[] _raindrops;
    private int _raindropCount;
    private readonly Random _random = new();
    private float[] _smoothedSpectrumCache;
    private float _timeSinceLastSpawn;
    private bool _firstRender = true;
    private readonly Stopwatch _frameTimer = new();
    private float _actualDeltaTime = TARGET_DELTA_TIME;
    private float _averageLoudness = 0f;
    private ParticleBuffer _particleBuffer;
    private int _frameCounter = 0;
    private readonly int _particleUpdateSkip = 1;
    private int _effectsThreshold = Constants.Quality.MEDIUM_EFFECTS_THRESHOLD;
    private new bool _useAntiAlias = true;
    private new SKSamplingOptions _samplingOptions = new(SKFilterMode.Linear, SKMipmapMode.Linear);
    private new bool _useAdvancedEffects = true;
    private new bool _isOverlayActive;
    private bool _cacheNeedsUpdate;
    private readonly float[] _emptySpectrum = [];

    private void InitializeFields()
    {
        _raindrops = new Raindrop[Settings.Instance.MaxRaindrops];
        _smoothedSpectrumCache = new float[Settings.Instance.MaxRaindrops];
        _particleBuffer = new ParticleBuffer(Settings.Instance.MaxParticles, 1);
        _renderCache = new RenderCache(1, 1, false);
        _frameTimer.Start();
    }

    private void SubscribeToSettingsChanges() => Settings.Instance.PropertyChanged += OnSettingsChanged;

    public override void Initialize() => ExecuteSafely(PerformInitialization,
                                                      "Initialize",
                                                      "Failed to initialize renderer");

    private void PerformInitialization()
    {
        base.Initialize();
        if (_disposed) ResetRendererState();
        _firstRender = true;
        Log(LogLevel.Debug, LOG_PREFIX, "Initialized");
    }

    private void ResetRendererState()
    {
        _raindropCount = 0;
        _particleBuffer.Clear();
        _disposed = false;
    }

    public override void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium)
        => ExecuteSafely(() => PerformConfiguration(isOverlayActive, quality),
                         "Configure",
                         "Failed to configure renderer");

    private void PerformConfiguration(bool isOverlayActive, RenderQuality quality)
    {
        base.Configure(isOverlayActive, quality);
        if (_isOverlayActive != isOverlayActive) UpdateOverlayState(isOverlayActive);
        if (_quality != quality) ApplyNewQuality(quality);
    }

    private void UpdateOverlayState(bool isOverlayActive)
    {
        _isOverlayActive = isOverlayActive;
        _cacheNeedsUpdate = true;
        _firstRender = true;
        _raindropCount = 0;
        _particleBuffer.Clear();
    }

    private void ApplyNewQuality(RenderQuality quality)
    {
        _quality = quality;
        ApplyQualitySettings();
    }

    protected override void ApplyQualitySettings()
        => ExecuteSafely(ConfigureQualitySettings,
                         "ApplyQualitySettings",
                         "Failed to apply quality settings");

    private void ConfigureQualitySettings()
    {
        base.ApplyQualitySettings();
        switch (_quality)
        {
            case RenderQuality.Low:
                SetLowQualitySettings();
                break;
            case RenderQuality.Medium:
                SetMediumQualitySettings();
                break;
            case RenderQuality.High:
                SetHighQualitySettings();
                break;
        }
    }

    private void SetLowQualitySettings()
    {
        _useAntiAlias = false;
        _samplingOptions = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);
        _useAdvancedEffects = Constants.Quality.LOW_USE_ADVANCED_EFFECTS;
        _effectsThreshold = Constants.Quality.LOW_EFFECTS_THRESHOLD;
    }

    private void SetMediumQualitySettings()
    {
        _useAntiAlias = true;
        _samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
        _useAdvancedEffects = Constants.Quality.MEDIUM_USE_ADVANCED_EFFECTS;
        _effectsThreshold = Constants.Quality.MEDIUM_EFFECTS_THRESHOLD;
    }

    private void SetHighQualitySettings()
    {
        _useAntiAlias = true;
        _samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
        _useAdvancedEffects = Constants.Quality.HIGH_USE_ADVANCED_EFFECTS;
        _effectsThreshold = Constants.Quality.HIGH_EFFECTS_THRESHOLD;
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
        if (!AreRenderParametersValid(canvas, spectrum, info, paint)) return;
        ExecuteSafely(() => PerformRenderEffect(canvas, spectrum, info, barCount, paint),
                      "RenderEffect",
                      "Error during rendering");
    }

    private void PerformRenderEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        int barCount,
        SKPaint paint)
    {
        UpdateFrameState(info);
        IncrementSpawnTimer();
        UpdateSimulation(spectrum, barCount);
        RenderFullScene(canvas, spectrum, paint);
    }

    private void IncrementSpawnTimer() => _timeSinceLastSpawn += _actualDeltaTime;

    public override void Render(
        SKCanvas? canvas,
        float[]? spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint? paint,
        Action<SKCanvas, SKImageInfo>? drawPerformanceInfo)
    {
        if (!AreRenderParametersValid(canvas, spectrum, info, paint))
        {
            drawPerformanceInfo?.Invoke(canvas!, info);
            return;
        }
        if (IsCanvasClipped(canvas!, info))
        {
            drawPerformanceInfo?.Invoke(canvas!, info);
            return;
        }
        ExecuteSafely(() => ProcessAndRender(
            canvas!,
            spectrum!,
            info,
            barCount,
            paint!,
            drawPerformanceInfo),
                      "Render",
                      "Error during rendering");
        drawPerformanceInfo?.Invoke(canvas!, info);
    }

    private static bool IsCanvasClipped(SKCanvas canvas, SKImageInfo info)
        => canvas.QuickReject(new SKRect(0, 0, info.Width, info.Height));

    private void ProcessAndRender(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        int barCount,
        SKPaint paint,
        Action<SKCanvas, SKImageInfo>? _) // drawPerformanceInfo
    {
        float[] renderSpectrum = AcquireRenderSpectrum(spectrum, barCount);
        UpdateFrameCounter();
        UpdateAndRenderScene(canvas, renderSpectrum, info, barCount, paint);
    }

    private float[] AcquireRenderSpectrum(float[] spectrum, int barCount)
    {
        bool semaphoreAcquired = TryAcquireSemaphore();
        try
        {
            if (semaphoreAcquired) ProcessSpectrum(spectrum, barCount);
            return GetProcessedSpectrum(spectrum, barCount);
        }
        finally
        {
            if (semaphoreAcquired) ReleaseSemaphore();
        }
    }

    private bool TryAcquireSemaphore() => 
        _spectrumSemaphore.Wait(0);

    private void ReleaseSemaphore() => 
        _spectrumSemaphore.Release();

    private float[] GetProcessedSpectrum(float[] spectrum, int barCount)
    {
        lock (_spectrumLock)
            return _processedSpectrum ?? ProcessSpectrumSynchronously(spectrum, barCount);
    }

    private void UpdateFrameCounter() => 
        _frameCounter = (_frameCounter + 1) % (_particleUpdateSkip + 1);

    private void UpdateAndRenderScene(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        int barCount,
        SKPaint basePaint)
        => ExecuteSafely(() => PerformSceneUpdateAndRender(canvas, spectrum, info, barCount, basePaint),
                         "UpdateAndRenderScene",
                         "Error during scene update and rendering");

    private void PerformSceneUpdateAndRender(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        int barCount,
        SKPaint basePaint)
    {
        UpdateRenderCacheIfNeeded(info);
        InitializeDropsOnFirstRender(barCount);
        IncrementSpawnTimer();
        UpdateSimulation(spectrum, barCount);
        RenderParticles(canvas, basePaint);
        RenderScene(canvas, spectrum, basePaint);
    }

    private void UpdateFrameState(SKImageInfo info)
    {
        UpdateDeltaTime();
        UpdateRenderCacheIfNeeded(info);
    }

    private void UpdateRenderCacheIfNeeded(SKImageInfo info)
    {
        if (NeedsCacheUpdate(info)) UpdateRenderCache(info);
    }

    private bool NeedsCacheUpdate(SKImageInfo info)
        => _cacheNeedsUpdate || _renderCache.Width != info.Width || _renderCache.Height != info.Height;

    private void UpdateRenderCache(SKImageInfo info)
    {
        _renderCache = new RenderCache(info.Width, info.Height, _isOverlayActive);
        _particleBuffer.UpdateLowerBound(_renderCache.LowerBound);
        _cacheNeedsUpdate = false;
    }

    private void UpdateDeltaTime() => 
        _actualDeltaTime = CalculateDeltaTime();

    private float CalculateDeltaTime()
    {
        float elapsed = (float)_frameTimer.Elapsed.TotalSeconds;
        _frameTimer.Restart();
        float speedMultiplier = elapsed / TARGET_DELTA_TIME;
        return Clamp(TARGET_DELTA_TIME * speedMultiplier,
                     Settings.Instance.MinTimeStep,
                     Settings.Instance.MaxTimeStep);
    }

    private bool AreRenderParametersValid(
        SKCanvas? canvas,
        float[]? spectrum,
        SKImageInfo info,
        SKPaint? paint)
    {
        if (!IsCanvasValid(canvas)) return false;
        if (!IsSpectrumValid(spectrum)) return false;
        if (!IsPaintValid(paint)) return false;
        if (!AreDimensionsValid(info)) return false;
        if (IsDisposed()) return false;
        return true;
    }

    private static bool IsCanvasValid(SKCanvas? canvas)
    {
        if (canvas != null) return true;
        Log(LogLevel.Error, LOG_PREFIX, "Canvas is null");
        return false;
    }

    private static bool IsSpectrumValid(float[]? spectrum)
    {
        if (spectrum != null && spectrum.Length > 0) return true;
        Log(LogLevel.Error, LOG_PREFIX, "Spectrum is null or empty");
        return false;
    }

    private static bool IsPaintValid(SKPaint? paint)
    {
        if (paint != null) return true;
        Log(LogLevel.Error, LOG_PREFIX, "Paint is null");
        return false;
    }

    private static bool AreDimensionsValid(SKImageInfo info)
    {
        if (info.Width > 0 && info.Height > 0) return true;
        Log(LogLevel.Error,
            LOG_PREFIX,
            $"Invalid image dimensions: {info.Width}x{info.Height}");
        return false;
    }

    private bool IsDisposed()
    {
        if (!_disposed) return false;
        Log(LogLevel.Error, LOG_PREFIX, "Renderer is disposed");
        return true;
    }

    private void RenderFullScene(SKCanvas canvas, float[] spectrum, SKPaint paint)
    {
        RenderParticles(canvas, paint);
        RenderScene(canvas, spectrum, paint);
    }

    private void RenderParticles(SKCanvas canvas, SKPaint paint) => 
        _particleBuffer.RenderParticles(canvas, paint);

    private void RenderScene(SKCanvas canvas, float[] spectrum, SKPaint paint)
    {
        if (ShouldRenderTrails()) RenderRaindropTrails(canvas, spectrum, paint);
        RenderRaindrops(canvas, spectrum, paint);
    }

    private bool ShouldRenderTrails() => 
        _effectsThreshold < 3 && _averageLoudness > 0.3f;

    private void ProcessSpectrum(float[] spectrum, int barCount)
        => ExecuteSafely(() => ProcessSpectrumDataIfValid(spectrum, barCount),
                         "ProcessSpectrum",
                         "Error processing spectrum data");

    private void ProcessSpectrumDataIfValid(float[] spectrum, int barCount)
    {
        if (barCount <= 0) return;
        ProcessSpectrumData(spectrum.AsSpan(0, Min(spectrum.Length, barCount)),
                            _smoothedSpectrumCache.AsSpan(0, barCount));
        _processedSpectrum = _smoothedSpectrumCache;
    }

    private float[] ProcessSpectrumSynchronously(float[] spectrum, int barCount)
        => barCount <= 0 ? [] : ComputeSynchronousSpectrum(spectrum, barCount);

    private float[] ComputeSynchronousSpectrum(float[] spectrum, int barCount)
    {
        Span<float> result = stackalloc float[barCount];
        ProcessSpectrumData(spectrum.AsSpan(0, Min(spectrum.Length, barCount)), result);
        float[] output = new float[barCount];
        result.CopyTo(output.AsSpan(0, barCount));
        return output;
    }

    private void ProcessSpectrumData(ReadOnlySpan<float> src, Span<float> dst)
    {
        if (src.IsEmpty || dst.IsEmpty) return;
        float sum = ProcessSpectrumBlocks(src, dst);
        UpdateAverageLoudness(sum, dst.Length);
    }

    private void UpdateAverageLoudness(float sum, int length)
        => _averageLoudness = Clamp(sum / length * 4.0f, 0f, 1f);

    private static float ProcessSpectrumBlocks(ReadOnlySpan<float> src, Span<float> dst)
        => ComputeSpectrumBlocks(src, dst);

    private static float ComputeSpectrumBlocks(ReadOnlySpan<float> src, Span<float> dst)
    {
        float sum = 0f;
        float blockSize = src.Length / (float)dst.Length;
        for (int i = 0; i < dst.Length; i++)
            sum += ProcessSingleSpectrumBlock(src, dst, i, blockSize);
        return sum;
    }

    private static float ProcessSingleSpectrumBlock(
        ReadOnlySpan<float> src,
        Span<float> dst,
        int index,
        float blockSize)
    {
        int start = (int)(index * blockSize);
        int end = (int)MathF.Min((index + 1) * blockSize, src.Length);
        if (end <= start) return SmoothEmptyBlock(dst, index);
        return SmoothSpectrumBlock(src, dst, index, start, end - start);
    }

    private static float SmoothEmptyBlock(Span<float> dst, int index)
    {
        dst[index] *= (1 - SMOOTH_FACTOR);
        return dst[index];
    }

    private static float SmoothSpectrumBlock(
        ReadOnlySpan<float> src,
        Span<float> dst,
        int index,
        int start,
        int count)
    {
        float value = ProcessSpectrumBlock(src, start, count);
        dst[index] = dst[index] * (1 - SMOOTH_FACTOR) + (value / count) * SMOOTH_FACTOR;
        return dst[index];
    }

    private static float ProcessSpectrumBlock(ReadOnlySpan<float> src, int start, int count)
        => count >= Vector<float>.Count && Vector.IsHardwareAccelerated
            ? ProcessSpectrumBlockVectorized(src, start, count)
            : ProcessSpectrumBlockSequential(src, start, count);

    private static float ProcessSpectrumBlockVectorized(ReadOnlySpan<float> src, int start, int count)
        => ComputeVectorizedSum(src, start, count);

    private static float ComputeVectorizedSum(ReadOnlySpan<float> src, int start, int count)
    {
        int simdLength = count - count % Vector<float>.Count;
        Vector<float> vSum = Vector<float>.Zero;
        for (int j = 0; j < simdLength; j += Vector<float>.Count)
            vSum += ReadVector(src, start + j);
        float value = SumVector(vSum);
        value += SumRemainingElements(src, start + simdLength, count - simdLength);
        return value;
    }

    private static Vector<float> ReadVector(ReadOnlySpan<float> src, int start)
        => MemoryMarshal.Read<Vector<float>>(MemoryMarshal.AsBytes(src.Slice(start, Vector<float>.Count)));

    private static float SumVector(Vector<float> vSum)
    {
        float value = 0f;
        for (int k = 0; k < Vector<float>.Count; k++) value += vSum[k];
        return value;
    }

    private static float SumRemainingElements(ReadOnlySpan<float> src, int start, int count)
    {
        float value = 0f;
        for (int j = 0; j < count; j++) value += src[start + j];
        return value;
    }

    private static float ProcessSpectrumBlockSequential(ReadOnlySpan<float> src, int start, int count)
        => ComputeSequentialSum(src, start, count);

    private static float ComputeSequentialSum(ReadOnlySpan<float> src, int start, int count)
    {
        float value = 0f;
        for (int j = 0; j < count; j++) value += src[start + j];
        return value;
    }

    private void InitializeDropsOnFirstRender(int barCount)
    {
        if (!_firstRender) return;
        InitializeInitialDrops(barCount);
        _firstRender = false;
    }

    private void InitializeInitialDrops(int barCount)
        => ExecuteSafely(() => CreateInitialDrops(barCount),
                         "InitializeInitialDrops",
                         "Error during initializing initial drops");

    private void CreateInitialDrops(int barCount)
    {
        _raindropCount = 0;
        int initialCount = Min(INITIAL_DROP_COUNT, Settings.Instance.MaxRaindrops);
        for (int i = 0; i < initialCount; i++)
            AddInitialDrop(barCount, _renderCache.Width, _renderCache.Height);
    }

    private void AddInitialDrop(int barCount, float width, float height)
    {
        int spectrumIndex = GenerateRandomIndex(barCount);
        float x = GenerateInitialX(width);
        float y = GenerateInitialY(height);
        float fallSpeed = CalculateInitialFallSpeed();
        float size = CalculateInitialSize();
        float intensity = GenerateInitialIntensity();
        _raindrops[_raindropCount++] = new Raindrop(x, y, fallSpeed, size, intensity, spectrumIndex);
    }

    private int GenerateRandomIndex(int barCount) => 
        _random.Next(barCount);

    private float GenerateInitialX(float width) => 
        width * (float)_random.NextDouble();

    private float GenerateInitialY(float height) => 
        height * (float)_random.NextDouble() * 0.5f;

    private float CalculateInitialFallSpeed()
    {
        float speedVariation = (float)(_random.NextDouble() * Settings.Instance.SpeedVariation);
        return Settings.Instance.BaseFallSpeed + speedVariation;
    }

    private float CalculateInitialSize()
        => Settings.Instance.RaindropSize * (0.7f + (float)_random.NextDouble() * 0.6f);

    private float GenerateInitialIntensity() => 
        0.3f + (float)_random.NextDouble() * 0.3f;

    private void UpdateSimulation(float[] spectrum, int barCount)
    {
        UpdateRaindrops(spectrum);
        UpdateParticlesIfNeeded();
        SpawnDropsIfNeeded(spectrum, barCount);
    }

    private void UpdateRaindrops(float[] spectrum) => 
        FilterAndMoveRaindrops(spectrum);

    private void FilterAndMoveRaindrops(float[] spectrum)
    {
        int writeIdx = 0;
        for (int i = 0; i < _raindropCount; i++)
        {
            Raindrop drop = _raindrops[i];
            if (MoveRaindrop(drop, out Raindrop updatedDrop))
                _raindrops[writeIdx++] = updatedDrop;
            else
                CreateSplashIfNeeded(drop, spectrum);
        }
        _raindropCount = writeIdx;
    }

    private bool MoveRaindrop(Raindrop drop, out Raindrop updatedDrop)
    {
        float newY = drop.Y + drop.FallSpeed * _actualDeltaTime;
        updatedDrop = drop.WithNewY(newY);
        return newY < _renderCache.LowerBound;
    }

    private void CreateSplashIfNeeded(Raindrop drop, float[] spectrum)
    {
        float intensity = GetDropIntensity(drop, spectrum);
        if (ShouldCreateSplash(intensity)) GenerateSplash(drop.X, intensity);
    }

    private static float GetDropIntensity(Raindrop drop, float[] spectrum)
        => drop.SpectrumIndex < spectrum.Length ? spectrum[drop.SpectrumIndex] : drop.Intensity;

    private static bool ShouldCreateSplash(float intensity) => 
        intensity > 0.2f;

    private void GenerateSplash(float x, float intensity)
        => _particleBuffer.CreateSplashParticles(x, _renderCache.LowerBound, intensity, _random);

    private void UpdateParticlesIfNeeded()
    {
        if (_frameCounter != 0) return;
        float adjustedDelta = CalculateAdjustedDeltaTime();
        _particleBuffer.UpdateParticles(adjustedDelta);
    }

    private float CalculateAdjustedDeltaTime() => 
        _actualDeltaTime * (_particleUpdateSkip + 1);

    private void SpawnDropsIfNeeded(float[] spectrum, int barCount)
    {
        if (!ShouldSpawnDrops()) return;
        SpawnNewDrops(spectrum, barCount);
        ResetSpawnTimer();
    }

    private bool ShouldSpawnDrops() => 
        _timeSinceLastSpawn >= SPAWN_INTERVAL;

    private void ResetSpawnTimer() => 
        _timeSinceLastSpawn = 0;

    private void SpawnNewDrops(float[] spectrum, int barCount)
    {
        if (!CanSpawnDrops(barCount, spectrum)) return;
        SpawnParameters spawnParams = CreateSpawnParameters(barCount);
        TrySpawnMultipleDrops(spectrum, barCount, ref spawnParams);
    }

    private static bool CanSpawnDrops(int barCount, float[] spectrum) => 
        barCount > 0 && spectrum.Length > 0;

    private void TrySpawnMultipleDrops(
        float[] spectrum,
        int barCount,
        ref SpawnParameters spawnParams)
    {
        for (int i = 0; i < barCount
            && i < spectrum.Length
            && spawnParams.SpawnsThisFrame < spawnParams.MaxSpawns; i++)
            TrySpawnSingleDrop(spectrum[i], i, ref spawnParams);
    }

    private readonly struct SpawnParameters
    {
        public readonly float StepWidth, Threshold, SpawnBoost;
        public readonly int MaxSpawns, SpawnsThisFrame;

        public SpawnParameters(
            float stepWidth,
            float threshold,
            float spawnBoost,
            int maxSpawns,
            int spawnsThisFrame = 0)
            => (StepWidth, Threshold, SpawnBoost, MaxSpawns, SpawnsThisFrame)
            = (stepWidth, threshold, spawnBoost, maxSpawns, spawnsThisFrame);

        public SpawnParameters WithSpawnsIncrease() => 
            new(
            StepWidth,
            Threshold,
            SpawnBoost,
            MaxSpawns,
            SpawnsThisFrame + 1);
    }

    private SpawnParameters CreateSpawnParameters(int barCount) => new(
        CalculateStepWidth(barCount),
        GetSpawnThreshold(),
        CalculateSpawnBoost(),
        3
    );

    private float CalculateStepWidth(int barCount) => 
        _renderCache.Width / barCount;

    private float GetSpawnThreshold()
        => _isOverlayActive ? Settings.Instance.SpawnThresholdOverlay : Settings.Instance.SpawnThresholdNormal;

    private float CalculateSpawnBoost() => 
        1.0f + _averageLoudness * 2.0f;

    private void TrySpawnSingleDrop(
        float intensity,
        int index,
        ref SpawnParameters spawnParams)
    {
        if (!ShouldSpawnDrop(intensity, spawnParams)) return;
        AddNewDrop(intensity, index, spawnParams);
        spawnParams = spawnParams.WithSpawnsIncrease();
    }

    private bool ShouldSpawnDrop(float intensity, SpawnParameters spawnParams)
        => intensity > spawnParams.Threshold &&
           _random.NextDouble() < Settings.Instance.SpawnProbability * intensity * spawnParams.SpawnBoost &&
           _raindropCount < _raindrops.Length;

    private void AddNewDrop(
        float intensity,
        int index,
        SpawnParameters spawnParams)
    {
        float x = CalculateRaindropX(index, spawnParams.StepWidth);
        float fallSpeed = CalculateRaindropFallSpeed(intensity);
        float size = CalculateRaindropSize(intensity);

        _raindrops[_raindropCount++] = new Raindrop(
            x,
            _renderCache.UpperBound,
            fallSpeed,
            size,
            intensity,
            index);
    }

    private static float CalculateRaindropX(int index, float stepWidth)
        => index * stepWidth + stepWidth * 0.5f +
           (float)(Random.Shared.NextDouble() * stepWidth * 0.5f - stepWidth * 0.25f);

    private float CalculateRaindropFallSpeed(float intensity)
    {
        float speedVariation = (float)(_random.NextDouble() * Settings.Instance.SpeedVariation);
        return Settings.Instance.BaseFallSpeed * (1f + intensity * Settings.Instance.IntensitySpeedMultiplier) +
               speedVariation;
    }

    private float CalculateRaindropSize(float intensity)
        => Settings.Instance.RaindropSize * (0.8f + intensity * 0.4f) *
           (0.9f + (float)_random.NextDouble() * 0.2f);

    private void RenderRaindropTrails(SKCanvas canvas, float[] spectrum, SKPaint basePaint)
        => ExecuteSafely(() => DrawRaindropTrails(canvas, spectrum, basePaint),
                         "RenderRaindropTrails",
                         "Error rendering raindrop trails");

    private void DrawRaindropTrails(SKCanvas canvas, float[] spectrum, SKPaint basePaint)
    {
        using var trailPaint = ConfigureTrailPaint(basePaint);
        if (_useAdvancedEffects) ApplyAdvancedTrailEffects(trailPaint, basePaint);
        RenderAllTrails(canvas, trailPaint, spectrum, basePaint.Color);
    }

    private SKPaint ConfigureTrailPaint(SKPaint basePaint)
    {
        var paint = _paintPool.Get();
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeCap = SKStrokeCap.Round;
        paint.IsAntialias = _useAntiAlias;
        paint.Color = basePaint.Color;
        return paint;
    }

    private static void ApplyAdvancedTrailEffects(SKPaint trailPaint, SKPaint basePaint)
    {
        trailPaint.Shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(0, 10),
            [basePaint.Color, basePaint.Color.WithAlpha(0)],
            [0, 1],
            SKShaderTileMode.Clamp);
    }

    private void RenderAllTrails(
        SKCanvas canvas,
        SKPaint trailPaint,
        float[] spectrum,
        SKColor baseColor)
    {
        for (int i = 0; i < _raindropCount; i++)
            RenderTrailForDrop(canvas, trailPaint, spectrum, i, baseColor);
    }

    private void RenderTrailForDrop(
        SKCanvas canvas,
        SKPaint trailPaint,
        float[] spectrum,
        int dropIndex,
        SKColor baseColor)
    {
        Raindrop drop = _raindrops[dropIndex];
        if (!ShouldRenderTrail(drop, spectrum, out float intensity, out float trailLength)) return;
        ConfigureTrailPaintForDrop(trailPaint, drop, intensity, baseColor);
        DrawTrailPath(canvas, trailPaint, drop, trailLength);
    }

    private static bool ShouldRenderTrail(
        Raindrop drop,
        float[] spectrum,
        out float intensity,
        out float trailLength)
    {
        intensity = 0;
        trailLength = 0;
        if (!IsTrailEligible(drop)) return false;
        intensity = GetTrailIntensity(drop, spectrum);
        if (intensity < TRAIL_INTENSITY_THRESHOLD) return false;
        trailLength = CalculateTrailLength(drop, intensity);
        return trailLength >= TRAIL_INTENSITY_THRESHOLD;
    }

    private static bool IsTrailEligible(Raindrop drop)
        => drop.FallSpeed >= Settings.Instance.BaseFallSpeed * FALLSPEED_THRESHOLD_MULTIPLIER &&
           drop.Size >= Settings.Instance.RaindropSize * RAINDROP_SIZE_THRESHOLD_MULTIPLIER;

    private static float GetTrailIntensity(Raindrop drop, float[] spectrum)
        => drop.SpectrumIndex < spectrum.Length ? spectrum[drop.SpectrumIndex] : drop.Intensity;

    private static float CalculateTrailLength(Raindrop drop, float intensity)
        => MathF.Min(drop.FallSpeed * TRAIL_LENGTH_MULTIPLIER * intensity,
               drop.Size * TRAIL_LENGTH_SIZE_FACTOR);

    private static void ConfigureTrailPaintForDrop(
        SKPaint paint,
        Raindrop drop,
        float intensity,
        SKColor baseColor)
    {
        paint.Color = baseColor.WithAlpha((byte)(TRAIL_OPACITY_MULTIPLIER * intensity));
        paint.StrokeWidth = drop.Size * TRAIL_STROKE_MULTIPLIER;
    }

    private void DrawTrailPath(
        SKCanvas canvas,
        SKPaint trailPaint,
        Raindrop drop,
        float trailLength)
    {
        _trailPath.Reset();
        _trailPath.MoveTo(drop.X, drop.Y);
        _trailPath.LineTo(drop.X, drop.Y - trailLength);
        if (!canvas.QuickReject(_trailPath.Bounds)) canvas.DrawPath(_trailPath, trailPaint);
    }

    private void RenderRaindrops(
        SKCanvas canvas,
        float[] spectrum,
        SKPaint basePaint) => 
        ExecuteSafely(() => DrawRaindrops(canvas, spectrum, basePaint),
                         "RenderRaindrops",
                         "Error rendering raindrops");

    private void DrawRaindrops(SKCanvas canvas, float[] spectrum, SKPaint basePaint)
    {
        using var dropPaint = ConfigureDropPaint(basePaint);
        using var highlightPaint = ConfigureHighlightPaintBase();
        RenderAllRaindrops(canvas, dropPaint, highlightPaint, spectrum, basePaint.Color);
    }

    private SKPaint ConfigureDropPaint(SKPaint basePaint)
    {
        var paint = _paintPool.Get();
        paint.Style = SKPaintStyle.Fill;
        paint.IsAntialias = _useAntiAlias;
        paint.Color = basePaint.Color;
        return paint;
    }

    private SKPaint ConfigureHighlightPaintBase()
    {
        var paint = _paintPool.Get();
        paint.Style = SKPaintStyle.Fill;
        paint.IsAntialias = _useAntiAlias;
        return paint;
    }

    private void RenderAllRaindrops(
        SKCanvas canvas,
        SKPaint dropPaint,
        SKPaint highlightPaint,
        float[] spectrum,
        SKColor baseColor)
    {
        for (int i = 0; i < _raindropCount; i++)
            RenderSingleRaindrop(canvas, dropPaint, highlightPaint, spectrum, i, baseColor);
    }

    private void RenderSingleRaindrop(
        SKCanvas canvas,
        SKPaint dropPaint,
        SKPaint highlightPaint,
        float[] spectrum,
        int index,
        SKColor baseColor)
    {
        Raindrop drop = _raindrops[index];
        float intensity = CalculateRaindropIntensity(drop, spectrum);
        PrepareDropPaint(dropPaint, intensity, drop, baseColor, out SKRect dropRect);
        if (!canvas.QuickReject(dropRect))
        {
            DrawRaindrop(canvas, dropPaint, drop);
            RenderHighlightIfNeeded(canvas, highlightPaint, drop, intensity);
        }
    }

    private static float CalculateRaindropIntensity(Raindrop drop, float[] spectrum)
        => drop.SpectrumIndex < spectrum.Length
            ? spectrum[drop.SpectrumIndex] * 0.7f + drop.Intensity * 0.3f
            : drop.Intensity;

    private static void PrepareDropPaint(
        SKPaint paint,
        float intensity,
        Raindrop drop,
        SKColor baseColor,
        out SKRect dropRect)
    {
        paint.Color = baseColor.WithAlpha(CalculateDropAlpha(intensity));
        dropRect = CalculateDropRect(drop);
    }

    private static byte CalculateDropAlpha(float intensity)
        => (byte)(255 * MathF.Min(0.7f + intensity * 0.3f, 1.0f));

    private static SKRect CalculateDropRect(Raindrop drop)
        => new(drop.X - drop.Size, drop.Y - drop.Size, drop.X + drop.Size, drop.Y + drop.Size);

    private static void DrawRaindrop(SKCanvas canvas, SKPaint dropPaint, Raindrop drop)
        => canvas.DrawCircle(drop.X, drop.Y, drop.Size, dropPaint);

    private void RenderHighlightIfNeeded(
        SKCanvas canvas,
        SKPaint highlightPaint,
        Raindrop drop,
        float intensity)
    {
        if (!ShouldRenderHighlight(drop, intensity)) return;
        ConfigureHighlightPaint(highlightPaint, intensity);
        DrawRaindropHighlight(canvas, highlightPaint, drop);
    }

    private bool ShouldRenderHighlight(Raindrop drop, float intensity)
        => _effectsThreshold < 2 &&
           drop.Size > Settings.Instance.RaindropSize * RAINDROP_SIZE_HIGHLIGHT_THRESHOLD &&
           intensity > INTENSITY_HIGHLIGHT_THRESHOLD;

    private static void ConfigureHighlightPaint(SKPaint paint, float intensity)
        => paint.Color = SKColors.White.WithAlpha((byte)(150 * intensity));

    private static void DrawRaindropHighlight(
        SKCanvas canvas,
        SKPaint paint,
        Raindrop drop)
    {
        float highlightSize = drop.Size * HIGHLIGHT_SIZE_MULTIPLIER;
        float highlightX = drop.X - drop.Size * HIGHLIGHT_OFFSET_MULTIPLIER;
        float highlightY = drop.Y - drop.Size * HIGHLIGHT_OFFSET_MULTIPLIER;
        canvas.DrawCircle(highlightX, highlightY, highlightSize, paint);
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
        => ExecuteSafely(() => HandleSettingsChange(e.PropertyName),
                         "OnSettingsChanged",
                         "Failed to process settings change");

    private void HandleSettingsChange(string? propertyName)
    {
        if (propertyName == nameof(Settings.MaxRaindrops)) UpdateRaindropsArraySize();
        else if (propertyName == nameof(Settings.MaxParticles))
            _particleBuffer.ResizeBuffer(Settings.Instance.MaxParticles);
        else if (propertyName == nameof(Settings.OverlayHeightMultiplier))
            _cacheNeedsUpdate = true;
    }

    private void UpdateRaindropsArraySize()
    {
        var newRaindrops = new Raindrop[Settings.Instance.MaxRaindrops];
        var newSmoothedCache = new float[Settings.Instance.MaxRaindrops];
        int copyCount = Min(_raindropCount, Settings.Instance.MaxRaindrops);
        if (copyCount > 0) Array.Copy(_raindrops, newRaindrops, copyCount);
        _raindrops = newRaindrops;
        _smoothedSpectrumCache = newSmoothedCache;
        _raindropCount = copyCount;
        _cacheNeedsUpdate = true;
    }

    public override void Dispose()
    {
        if (_disposed) return;
        ExecuteSafely(PerformDisposal, "Dispose", "Error during disposal");
        FinalizeDisposal();
    }

    private void PerformDisposal()
    {
        CleanupResources();
        base.Dispose();
    }

    private void CleanupResources()
    {
        UnsubscribeFromSettingsChanges();
        DisposeRenderingResources();
    }

    private void UnsubscribeFromSettingsChanges() => 
        Settings.Instance.PropertyChanged -= OnSettingsChanged;

    private void DisposeRenderingResources()
    {
        _spectrumSemaphore?.Dispose();
        _trailPath?.Dispose();
        _processedSpectrum = null;
    }

    private void FinalizeDisposal()
    {
        _disposed = true;
        Log(LogLevel.Debug, LOG_PREFIX, "Disposed");
    }

    private static void ExecuteSafely(Action action, string source, string errorMessage)
        => Safe(action, new ErrorHandlingOptions
        {
            Source = $"{LOG_PREFIX}.{source}",
            ErrorMessage = errorMessage
        });
}