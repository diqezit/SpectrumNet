#nullable enable

using SpectrumNet.DataSettings;
using SpectrumNet.Service.Enums;

namespace SpectrumNet.Views.Renderers;

/// <summary>
/// Renderer that visualizes spectrum data as falling raindrops with splash effects.
/// </summary>
public sealed class RaindropsRenderer : BaseSpectrumRenderer
{
    #region Singleton Pattern
    private static readonly Lazy<RaindropsRenderer> _instance = new(() => new RaindropsRenderer());
    private RaindropsRenderer()
    {
        _raindrops = new Raindrop[Settings.Instance.MaxRaindrops];
        _smoothedSpectrumCache = new float[Settings.Instance.MaxRaindrops];
        _particleBuffer = new ParticleBuffer(Settings.Instance.MaxParticles, 1);
        _renderCache = new RenderCache(1, 1, false);
        _frameTimer.Start();
        Settings.Instance.PropertyChanged += OnSettingsChanged;
    }
    public static RaindropsRenderer GetInstance() => _instance.Value;
    #endregion

    #region Constants
    private static class Constants
    {
        // Logging
        public const string LOG_PREFIX = "RaindropsRenderer";

        // Render Settings
        public const float TARGET_DELTA_TIME = 0.016f;   // Target frame time (seconds)
        public const float SMOOTH_FACTOR = 0.2f;     // Smoothing factor for spectrum data
        public const float TRAIL_LENGTH_MULTIPLIER = 0.15f;    // Multiplier for drop speed to compute trail length
        public const float TRAIL_LENGTH_SIZE_FACTOR = 5f;       // Factor for maximum trail length relative to drop size
        public const float TRAIL_STROKE_MULTIPLIER = 0.6f;     // Multiplier for trail stroke width
        public const float TRAIL_OPACITY_MULTIPLIER = 150f;     // Multiplier for trail opacity
        public const float TRAIL_INTENSITY_THRESHOLD = 0.3f;     // Minimum intensity required for trail rendering

        // Simulation Settings
        public const int INITIAL_DROP_COUNT = 30;       // Initial number of raindrops
        public const float GRAVITY = 9.8f;     // Gravity constant for particles
        public const float LIFETIME_DECAY = 0.4f;     // Particle lifetime decay factor
        public const float SPLASH_REBOUND = 0.5f;     // Rebound multiplier for splash particles
        public const float SPLASH_VELOCITY_THRESHOLD = 1.0f;     // Minimum velocity threshold for splash particles
        public const float SPAWN_INTERVAL = 0.05f;    // Time between spawns (seconds)
        public const float FALLSPEED_THRESHOLD_MULTIPLIER = 1.5f;  // Multiplier for base fall speed threshold for trails
        public const float RAINDROP_SIZE_THRESHOLD_MULTIPLIER = 0.9f; // Minimum size multiplier for trail rendering
        public const float RAINDROP_SIZE_HIGHLIGHT_THRESHOLD = 0.8f;  // Minimum size multiplier for highlights
        public const float INTENSITY_HIGHLIGHT_THRESHOLD = 0.4f;    // Intensity threshold for highlight rendering
        public const float HIGHLIGHT_SIZE_MULTIPLIER = 0.4f;    // Multiplier for highlight circle size
        public const float HIGHLIGHT_OFFSET_MULTIPLIER = 0.2f;    // Multiplier for highlight offset

        // Particle Creation Settings
        public const int SPLASH_PARTICLE_COUNT_MIN = 3;        // Minimum number of splash particles
        public const int SPLASH_PARTICLE_COUNT_MAX = 8;        // Maximum splash particles (exclusive upper bound)
        public const float PARTICLE_VELOCITY_BASE_MULTIPLIER = 0.7f;  // Base multiplier for particle velocity
        public const float PARTICLE_VELOCITY_INTENSITY_MULTIPLIER = 0.3f; // Intensity multiplier for particle velocity
        public const float SPLASH_UPWARD_BASE_MULTIPLIER = 0.8f;  // Base multiplier for upward force in splash
        public const float SPLASH_UPWARD_INTENSITY_MULTIPLIER = 0.2f;  // Intensity multiplier for upward force in splash
        public const float SPLASH_PARTICLE_SIZE_BASE_MULTIPLIER = 0.7f;  // Base multiplier for splash particle size
        public const float SPLASH_PARTICLE_SIZE_RANDOM_MULTIPLIER = 0.6f; // Random variation multiplier for splash particle size
        public const float SPLASH_PARTICLE_INTENSITY_MULTIPLIER = 0.5f;  // Intensity multiplier for splash particle size
        public const float SPLASH_PARTICLE_SIZE_INTENSITY_OFFSET = 0.8f;  // Offset for splash particle size based on intensity

        // Quality settings
        public static class Quality
        {
            // Low quality settings
            public const bool LOW_USE_ADVANCED_EFFECTS = false;
            public const int LOW_EFFECTS_THRESHOLD = 4;

            // Medium quality settings
            public const bool MEDIUM_USE_ADVANCED_EFFECTS = true;
            public const int MEDIUM_EFFECTS_THRESHOLD = 3;

            // High quality settings
            public const bool HIGH_USE_ADVANCED_EFFECTS = true;
            public const int HIGH_EFFECTS_THRESHOLD = 2;
        }
    }
    #endregion

    #region Nested Types
    /// <summary>
    /// Cache for render dimensions and parameters.
    /// </summary>
    private readonly struct RenderCache
    {
        public readonly float Width, Height, LowerBound, UpperBound, StepSize;
        public RenderCache(float width, float height, bool isOverlay)
        {
            Width = width;
            Height = height;
            LowerBound = isOverlay ? height * Settings.Instance.OverlayHeightMultiplier : height;
            UpperBound = 0f;
            StepSize = width / Settings.Instance.MaxRaindrops;
        }
    }

    /// <summary>
    /// Immutable structure representing a raindrop.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Raindrop
    {
        public readonly float X, Y, FallSpeed, Size, Intensity;
        public readonly int SpectrumIndex;
        public Raindrop(float x, float y, float fallSpeed, float size, float intensity, int spectrumIndex) =>
            (X, Y, FallSpeed, Size, Intensity, SpectrumIndex) = (x, y, fallSpeed, size, intensity, spectrumIndex);
        public Raindrop WithNewY(float newY) => new(X, newY, FallSpeed, Size, Intensity, SpectrumIndex);
    }

    /// <summary>
    /// Structure representing a particle for splash effects.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Particle
    {
        public float X, Y, VelocityX, VelocityY, Lifetime, Size;
        public bool IsSplash;
        public Particle(float x, float y, float velocityX, float velocityY, float size, bool isSplash) =>
            (X, Y, VelocityX, VelocityY, Lifetime, Size, IsSplash) = (x, y, velocityX, velocityY, 1.0f, size, isSplash);
        public bool Update(float deltaTime, float lowerBound)
        {
            X += VelocityX * deltaTime;
            Y += VelocityY * deltaTime;
            VelocityY += deltaTime * Constants.GRAVITY;
            Lifetime -= deltaTime * Constants.LIFETIME_DECAY;
            if (IsSplash && Y >= lowerBound)
            {
                Y = lowerBound;
                VelocityY = -VelocityY * Constants.SPLASH_REBOUND;
                if (Abs(VelocityY) < Constants.SPLASH_VELOCITY_THRESHOLD)
                    VelocityY = 0;
            }
            return Lifetime > 0;
        }
    }

    /// <summary>
    /// Buffer for managing collection of particles.
    /// </summary>
    private sealed class ParticleBuffer
    {
        private Particle[] _particles;
        private int _count;
        private float _lowerBound;
        private readonly Random _random;
        public ParticleBuffer(int capacity, float lowerBound)
        {
            _particles = new Particle[capacity];
            _count = 0;
            _lowerBound = lowerBound;
            _random = new Random();
        }
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
        [MethodImpl(AggressiveInlining)]
        public void AddParticle(in Particle particle)
        {
            if (_count < _particles.Length)
                _particles[_count++] = particle;
        }
        [MethodImpl(AggressiveInlining)]
        public void Clear() => _count = 0;
        [MethodImpl(AggressiveInlining)]
        public void UpdateParticles(float deltaTime)
        {
            int writeIndex = 0;
            for (int i = 0; i < _count; i++)
            {
                ref Particle p = ref _particles[i];
                if (p.Update(deltaTime, _lowerBound))
                {
                    if (writeIndex != i)
                        _particles[writeIndex] = p;
                    writeIndex++;
                }
            }
            _count = writeIndex;
        }
        [MethodImpl(AggressiveInlining)]
        public void RenderParticles(SKCanvas canvas, SKPaint basePaint)
        {
            if (_count == 0) return;
            using var splashPaint = basePaint.Clone();
            splashPaint.Style = Fill;
            splashPaint.IsAntialias = true;
            for (int i = 0; i < _count; i++)
            {
                ref Particle p = ref _particles[i];
                float clampedLifetime = Clamp(p.Lifetime, 0f, 1f);
                float alphaMultiplier = clampedLifetime * clampedLifetime;
                byte alpha = (byte)(255 * alphaMultiplier);
                splashPaint.Color = basePaint.Color.WithAlpha(alpha);
                float sizeMultiplier = 0.8f + 0.2f * clampedLifetime;
                canvas.DrawCircle(p.X, p.Y, p.Size * sizeMultiplier, splashPaint);
            }
        }
        [MethodImpl(AggressiveInlining)]
        public void CreateSplashParticles(float x, float y, float intensity, Random random)
        {
            int count = Min(random.Next(Constants.SPLASH_PARTICLE_COUNT_MIN, Constants.SPLASH_PARTICLE_COUNT_MAX),
                                 Settings.Instance.MaxParticles - _count);
            if (count <= 0) return;
            float particleVelocityMax = Settings.Instance.ParticleVelocityMax *
                (Constants.PARTICLE_VELOCITY_BASE_MULTIPLIER + intensity * Constants.PARTICLE_VELOCITY_INTENSITY_MULTIPLIER);
            float upwardForce = Settings.Instance.SplashUpwardForce *
                (Constants.SPLASH_UPWARD_BASE_MULTIPLIER + intensity * Constants.SPLASH_UPWARD_INTENSITY_MULTIPLIER);
            for (int i = 0; i < count; i++)
            {
                float angle = (float)(random.NextDouble() * PI * 2);
                float speed = (float)(random.NextDouble() * particleVelocityMax);
                float size = Settings.Instance.SplashParticleSize *
                    (Constants.SPLASH_PARTICLE_SIZE_BASE_MULTIPLIER + (float)random.NextDouble() * Constants.SPLASH_PARTICLE_SIZE_RANDOM_MULTIPLIER) *
                    (intensity * Constants.SPLASH_PARTICLE_INTENSITY_MULTIPLIER + Constants.SPLASH_PARTICLE_SIZE_INTENSITY_OFFSET);
                AddParticle(new Particle(
                    x, y,
                    MathF.Cos(angle) * speed,
                    MathF.Sin(angle) * speed - upwardForce,
                    size,
                    true));
            }
        }
    }
    #endregion

    #region Fields
    // Rendering resources
    private readonly ObjectPool<SKPaint> _paintPool = new(() => new SKPaint(), paint => paint.Reset(), 3);
    private readonly SKPath _trailPath = new();

    // Simulation state
    private RenderCache _renderCache;
    private Raindrop[] _raindrops;
    private int _raindropCount;
    private readonly Random _random = new();
    private float[] _smoothedSpectrumCache;
    private float _timeSinceLastSpawn;
    private bool _firstRender = true;
    private readonly Stopwatch _frameTimer = new();
    private float _actualDeltaTime = Constants.TARGET_DELTA_TIME;
    private float _averageLoudness = 0f;
    private readonly ParticleBuffer _particleBuffer;
    private int _frameCounter = 0;
    private int _particleUpdateSkip = 1;
    private int _effectsThreshold = Constants.Quality.MEDIUM_EFFECTS_THRESHOLD;

    // Quality-dependent settings
    private new bool _useAntiAlias = true;
    private new SKSamplingOptions _samplingOptions = new(SKFilterMode.Linear, SKMipmapMode.Linear);
    private new bool _useAdvancedEffects = true;
    private bool _isOverlayActive;
    private bool _cacheNeedsUpdate;

    // Synchronization and state
    private readonly SemaphoreSlim _renderSemaphore = new(1, 1);
    private readonly object _spectrumLock = new();
    private new bool _disposed;
    #endregion

    #region Initialization and Configuration
    /// <summary>
    /// Initializes the raindrops renderer and prepares resources for rendering.
    /// </summary>
    public override void Initialize()
    {
        Safe(() =>
        {
            base.Initialize();

            if (_disposed)
            {
                _raindropCount = 0;
                _particleBuffer.Clear();
                _disposed = false;
            }

            _firstRender = true;

            Log(LogLevel.Debug, Constants.LOG_PREFIX, "Initialized");
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.Initialize",
            ErrorMessage = "Failed to initialize renderer"
        });
    }

    /// <summary>
    /// Configures the renderer with overlay status and quality settings.
    /// </summary>
    /// <param name="isOverlayActive">Indicates if the renderer is used in overlay mode.</param>
    /// <param name="quality">The rendering quality level.</param>
    public override void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium)
    {
        Safe(() =>
        {
            base.Configure(isOverlayActive, quality);

            if (_isOverlayActive != isOverlayActive)
            {
                _isOverlayActive = isOverlayActive;
                _cacheNeedsUpdate = true;
                _firstRender = true;
                _raindropCount = 0;
                _particleBuffer.Clear();
            }

            // Update quality if needed
            if (_quality != quality)
            {
                _quality = quality;
                ApplyQualitySettings();
            }
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.Configure",
            ErrorMessage = "Failed to configure renderer"
        });
    }

    /// <summary>
    /// Applies quality settings based on the current quality level.
    /// </summary>
    protected override void ApplyQualitySettings()
    {
        Safe(() =>
        {
            base.ApplyQualitySettings();

            switch (_quality)
            {
                case RenderQuality.Low:
                    _useAntiAlias = false;
                    _samplingOptions = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);
                    _useAdvancedEffects = Constants.Quality.LOW_USE_ADVANCED_EFFECTS;
                    _effectsThreshold = Constants.Quality.LOW_EFFECTS_THRESHOLD;
                    break;

                case RenderQuality.Medium:
                    _useAntiAlias = true;
                    _samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                    _useAdvancedEffects = Constants.Quality.MEDIUM_USE_ADVANCED_EFFECTS;
                    _effectsThreshold = Constants.Quality.MEDIUM_EFFECTS_THRESHOLD;
                    break;

                case RenderQuality.High:
                    _useAntiAlias = true;
                    _samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                    _useAdvancedEffects = Constants.Quality.HIGH_USE_ADVANCED_EFFECTS;
                    _effectsThreshold = Constants.Quality.HIGH_EFFECTS_THRESHOLD;
                    break;
            }

            // No need to update cached objects as they are recreated for each render
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.ApplyQualitySettings",
            ErrorMessage = "Failed to apply quality settings"
        });
    }

    /// <summary>
    /// Handles settings changes from the application settings.
    /// </summary>
    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        Safe(() =>
        {
            if (e.PropertyName == nameof(Settings.MaxRaindrops))
            {
                var newRaindrops = new Raindrop[Settings.Instance.MaxRaindrops];
                var newSmoothedCache = new float[Settings.Instance.MaxRaindrops];
                int copyCount = Min(_raindropCount, Settings.Instance.MaxRaindrops);
                if (copyCount > 0)
                    Array.Copy(_raindrops, newRaindrops, copyCount);
                _raindrops = newRaindrops;
                _smoothedSpectrumCache = newSmoothedCache;
                _raindropCount = copyCount;
                _cacheNeedsUpdate = true;
            }
            else if (e.PropertyName == nameof(Settings.MaxParticles))
            {
                _particleBuffer.ResizeBuffer(Settings.Instance.MaxParticles);
            }
            else if (e.PropertyName == nameof(Settings.OverlayHeightMultiplier))
            {
                _cacheNeedsUpdate = true;
            }
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.OnSettingsChanged",
            ErrorMessage = "Failed to process settings change"
        });
    }
    #endregion

    #region Rendering
    /// <summary>
    /// Renders the raindrops visualization on the canvas using spectrum data.
    /// </summary>
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
        // Validate rendering parameters
        if (!ValidateRenderParameters(canvas, spectrum, info, paint))
        {
            drawPerformanceInfo?.Invoke(canvas!, info);
            return;
        }

        // Quick reject if canvas area is not visible
        if (canvas!.QuickReject(new SKRect(0, 0, info.Width, info.Height)))
        {
            drawPerformanceInfo?.Invoke(canvas, info);
            return;
        }

        Safe(() =>
        {
            float[] renderSpectrum;
            bool semaphoreAcquired = false;
            try
            {
                // Try to acquire semaphore for updating spectrum data
                semaphoreAcquired = _spectrumSemaphore.Wait(0);
                if (semaphoreAcquired)
                {
                    // Process spectrum data when semaphore is acquired
                    ProcessSpectrum(spectrum!, barCount);
                }

                // Get processed spectrum for rendering
                lock (_spectrumLock)
                {
                    renderSpectrum = _processedSpectrum ??
                                     ProcessSpectrumSynchronously(spectrum!, barCount);
                }

                // Calculate delta time
                float targetDeltaTime = Constants.TARGET_DELTA_TIME;
                float elapsed = (float)_frameTimer.Elapsed.TotalSeconds;
                _frameTimer.Restart();
                float speedMultiplier = elapsed / targetDeltaTime;
                _actualDeltaTime = Clamp(
                    targetDeltaTime * speedMultiplier,
                    Settings.Instance.MinTimeStep,
                    Settings.Instance.MaxTimeStep
                );

                _frameCounter = (_frameCounter + 1) % (_particleUpdateSkip + 1);

                // Clone and configure paint for rendering
                using var paintClone = paint!.Clone();
                paintClone.IsAntialias = _useAntiAlias;

                // Update and render scene
                UpdateAndRenderScene(canvas!, renderSpectrum, info, barCount, paintClone);
            }
            finally
            {
                // Release semaphore if acquired
                if (semaphoreAcquired)
                {
                    _spectrumSemaphore.Release();
                }
            }
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.Render",
            ErrorMessage = "Error during rendering"
        });

        // Draw performance info
        drawPerformanceInfo?.Invoke(canvas!, info);
    }

    /// <summary>
    /// Validates all render parameters before processing.
    /// </summary>
    private bool ValidateRenderParameters(SKCanvas? canvas, float[]? spectrum, SKImageInfo info, SKPaint? paint)
    {
        if (canvas == null)
        {
            Log(LogLevel.Error, Constants.LOG_PREFIX, "Canvas is null");
            return false;
        }

        if (spectrum == null || spectrum.Length == 0)
        {
            Log(LogLevel.Error, Constants.LOG_PREFIX, "Spectrum is null or empty");
            return false;
        }

        if (paint == null)
        {
            Log(LogLevel.Error, Constants.LOG_PREFIX, "Paint is null");
            return false;
        }

        if (info.Width <= 0 || info.Height <= 0)
        {
            Log(LogLevel.Error, Constants.LOG_PREFIX, $"Invalid image dimensions: {info.Width}x{info.Height}");
            return false;
        }

        if (_disposed)
        {
            Log(LogLevel.Error, Constants.LOG_PREFIX, "Renderer is disposed");
            return false;
        }

        return true;
    }
    #endregion

    #region Spectrum Processing
    /// <summary>
    /// Processes spectrum data for simulation.
    /// </summary>
    private void ProcessSpectrum(float[] spectrum, int barCount)
    {
        Safe(() =>
        {
            if (barCount <= 0) return;

            ProcessSpectrumData(
                spectrum.AsSpan(0, Min(spectrum.Length, barCount)),
                _smoothedSpectrumCache.AsSpan(0, barCount)
            );

            _processedSpectrum = _smoothedSpectrumCache;
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.ProcessSpectrum",
            ErrorMessage = "Error processing spectrum data"
        });
    }

    /// <summary>
    /// Processes spectrum data synchronously when async processing isn't available.
    /// </summary>
    private float[] ProcessSpectrumSynchronously(float[] spectrum, int barCount)
    {
        if (barCount <= 0) return Array.Empty<float>();

        Span<float> result = stackalloc float[barCount];
        ProcessSpectrumData(
            spectrum.AsSpan(0, Min(spectrum.Length, barCount)),
            result
        );

        float[] output = new float[barCount];
        result.CopyTo(output.AsSpan(0, barCount));
        return output;
    }

    /// <summary>
    /// Applies processing to spectrum data with smoothing.
    /// </summary>
    [MethodImpl(AggressiveOptimization)]
    private void ProcessSpectrumData(ReadOnlySpan<float> src, Span<float> dst)
    {
        if (src.IsEmpty || dst.IsEmpty) return;

        float sum = 0f;
        float blockSize = src.Length / (float)dst.Length;
        float smoothFactor = Constants.SMOOTH_FACTOR;

        for (int i = 0; i < dst.Length; i++)
        {
            int start = (int)(i * blockSize);
            int end = Min((int)((i + 1) * blockSize), src.Length);

            if (end <= start)
            {
                dst[i] = dst[i] * (1 - smoothFactor);
                continue;
            }

            int count = end - start;
            float value = 0f;

            // Use SIMD if possible for better performance
            if (count >= Vector<float>.Count)
            {
                int simdLength = count - count % Vector<float>.Count;
                Vector<float> vSum = Vector<float>.Zero;

                for (int j = 0; j < simdLength; j += Vector<float>.Count)
                {
                    vSum += MemoryMarshal.Read<Vector<float>>(
                        MemoryMarshal.AsBytes(src.Slice(start + j, Vector<float>.Count))
                    );
                }

                for (int k = 0; k < Vector<float>.Count; k++)
                {
                    value += vSum[k];
                }

                for (int j = simdLength; j < count; j++)
                {
                    value += src[start + j];
                }
            }
            else
            {
                for (int j = start; j < end; j++)
                {
                    value += src[j];
                }
            }

            value /= count;
            dst[i] = dst[i] * (1 - smoothFactor) + value * smoothFactor;
            sum += dst[i];
        }

        _averageLoudness = Clamp(sum / dst.Length * 4.0f, 0f, 1f);
    }
    #endregion

    #region Simulation Methods
    /// <summary>
    /// Updates and renders the entire simulation scene.
    /// </summary>
    private void UpdateAndRenderScene(SKCanvas canvas, float[] spectrum, SKImageInfo info, int barCount, SKPaint basePaint)
    {
        Safe(() =>
        {
            if (_cacheNeedsUpdate || _renderCache.Width != info.Width || _renderCache.Height != info.Height)
            {
                _renderCache = new RenderCache(info.Width, info.Height, _isOverlayActive);
                _particleBuffer.UpdateLowerBound(_renderCache.LowerBound);
                _cacheNeedsUpdate = false;
            }

            if (_firstRender)
            {
                InitializeInitialDrops(barCount);
                _firstRender = false;
            }

            _timeSinceLastSpawn += _actualDeltaTime;
            UpdateSimulation(spectrum, barCount);

            // Render particles (background)
            _particleBuffer.RenderParticles(canvas, basePaint);

            // Render raindrop trails and raindrops (foreground)
            RenderScene(canvas, basePaint, spectrum);
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.UpdateAndRenderScene",
            ErrorMessage = "Error during scene update and rendering"
        });
    }

    /// <summary>
    /// Initializes the first set of raindrops.
    /// </summary>
    private void InitializeInitialDrops(int barCount)
    {
        Safe(() =>
        {
            _raindropCount = 0;
            float width = _renderCache.Width;
            float height = _renderCache.Height;
            int initialCount = Min(Constants.INITIAL_DROP_COUNT, Settings.Instance.MaxRaindrops);

            for (int i = 0; i < initialCount; i++)
            {
                int spectrumIndex = _random.Next(barCount);
                float x = width * (float)_random.NextDouble();
                float y = height * (float)_random.NextDouble() * 0.5f;
                float speedVariation = (float)(_random.NextDouble() * Settings.Instance.SpeedVariation);
                float fallSpeed = Settings.Instance.BaseFallSpeed + speedVariation;
                float size = Settings.Instance.RaindropSize * (0.7f + (float)_random.NextDouble() * 0.6f);
                float intensity = 0.3f + (float)_random.NextDouble() * 0.3f;

                _raindrops[_raindropCount++] = new Raindrop(x, y, fallSpeed, size, intensity, spectrumIndex);
            }
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.InitializeInitialDrops",
            ErrorMessage = "Error during initializing initial drops"
        });
    }

    /// <summary>
    /// Updates simulation state for raindrops and particles.
    /// </summary>
    private void UpdateSimulation(float[] spectrum, int barCount)
    {
        UpdateRaindrops(spectrum);

        if (_frameCounter == 0)
        {
            float adjustedDelta = _actualDeltaTime * (_particleUpdateSkip + 1);
            _particleBuffer.UpdateParticles(adjustedDelta);
        }

        if (_timeSinceLastSpawn >= Constants.SPAWN_INTERVAL)
        {
            SpawnNewDrops(spectrum, barCount);
            _timeSinceLastSpawn = 0;
        }
    }

    /// <summary>
    /// Updates positions of raindrops and handles collisions.
    /// </summary>
    [MethodImpl(AggressiveInlining)]
    private void UpdateRaindrops(float[] spectrum)
    {
        int writeIdx = 0;
        for (int i = 0; i < _raindropCount; i++)
        {
            Raindrop drop = _raindrops[i];
            float newY = drop.Y + drop.FallSpeed * _actualDeltaTime;

            if (newY < _renderCache.LowerBound)
            {
                _raindrops[writeIdx++] = drop.WithNewY(newY);
            }
            else
            {
                float intensity = drop.SpectrumIndex < spectrum.Length ? spectrum[drop.SpectrumIndex] : drop.Intensity;
                if (intensity > 0.2f)
                {
                    _particleBuffer.CreateSplashParticles(drop.X, _renderCache.LowerBound, intensity, _random);
                }
            }
        }
        _raindropCount = writeIdx;
    }

    /// <summary>
    /// Spawns new raindrops based on spectrum intensity.
    /// </summary>
    [MethodImpl(AggressiveInlining)]
    private void SpawnNewDrops(float[] spectrum, int barCount)
    {
        if (barCount <= 0 || spectrum.Length == 0) return;

        float stepWidth = _renderCache.Width / barCount;
        float threshold = _isOverlayActive ? Settings.Instance.SpawnThresholdOverlay : Settings.Instance.SpawnThresholdNormal;
        float spawnBoost = 1.0f + _averageLoudness * 2.0f;
        int maxSpawnsPerFrame = 3;
        int spawnsThisFrame = 0;

        for (int i = 0; i < barCount && i < spectrum.Length && spawnsThisFrame < maxSpawnsPerFrame; i++)
        {
            float intensity = spectrum[i];
            if (intensity > threshold && _random.NextDouble() < Settings.Instance.SpawnProbability * intensity * spawnBoost)
            {
                float x = i * stepWidth + stepWidth * 0.5f +
                          (float)(_random.NextDouble() * stepWidth * 0.5f - stepWidth * 0.25f);
                float speedVariation = (float)(_random.NextDouble() * Settings.Instance.SpeedVariation);
                float fallSpeed = Settings.Instance.BaseFallSpeed *
                                  (1f + intensity * Settings.Instance.IntensitySpeedMultiplier) +
                                  speedVariation;
                float size = Settings.Instance.RaindropSize * (0.8f + intensity * 0.4f) * (0.9f + (float)_random.NextDouble() * 0.2f);

                if (_raindropCount < _raindrops.Length)
                {
                    _raindrops[_raindropCount++] = new Raindrop(x, _renderCache.UpperBound, fallSpeed, size, intensity, i);
                    spawnsThisFrame++;
                }
            }
        }
    }
    #endregion

    #region Rendering Implementation
    /// <summary>
    /// Renders the complete scene with trails and raindrops.
    /// </summary>
    private void RenderScene(SKCanvas canvas, SKPaint paint, float[] spectrum)
    {
        bool hasHighPerformance = _effectsThreshold < 3;
        if (hasHighPerformance && _averageLoudness > 0.3f)
        {
            RenderRaindropTrails(canvas, spectrum, paint);
        }
        RenderRaindrops(canvas, spectrum, paint);
    }

    /// <summary>
    /// Renders trails behind fast-moving raindrops.
    /// </summary>
    private void RenderRaindropTrails(SKCanvas canvas, float[] spectrum, SKPaint basePaint)
    {
        Safe(() =>
        {
            using var trailPaint = _paintPool.Get();
            trailPaint.Style = Stroke;
            trailPaint.StrokeCap = SKStrokeCap.Round;
            trailPaint.IsAntialias = _useAntiAlias;

            if (_useAdvancedEffects)
            {
                // Apply gradient shader for advanced trail effects
                trailPaint.Shader = SKShader.CreateLinearGradient(
                    new SKPoint(0, 0),
                    new SKPoint(0, 10),
                    new SKColor[] { basePaint.Color, basePaint.Color.WithAlpha(0) },
                    new float[] { 0, 1 },
                    SKShaderTileMode.Clamp);
            }

            for (int i = 0; i < _raindropCount; i++)
            {
                Raindrop drop = _raindrops[i];

                // Check if drop meets conditions for trail rendering
                if (drop.FallSpeed < Settings.Instance.BaseFallSpeed * Constants.FALLSPEED_THRESHOLD_MULTIPLIER ||
                    drop.Size < Settings.Instance.RaindropSize * Constants.RAINDROP_SIZE_THRESHOLD_MULTIPLIER)
                    continue;

                float intensity = drop.SpectrumIndex < spectrum.Length ? spectrum[drop.SpectrumIndex] : drop.Intensity;
                if (intensity < Constants.TRAIL_INTENSITY_THRESHOLD)
                    continue;

                float trailLength = Min(
                    drop.FallSpeed * Constants.TRAIL_LENGTH_MULTIPLIER * intensity,
                    drop.Size * Constants.TRAIL_LENGTH_SIZE_FACTOR
                );

                if (trailLength < Constants.TRAIL_INTENSITY_THRESHOLD)
                    continue;

                trailPaint.Color = basePaint.Color.WithAlpha((byte)(Constants.TRAIL_OPACITY_MULTIPLIER * intensity));
                trailPaint.StrokeWidth = drop.Size * Constants.TRAIL_STROKE_MULTIPLIER;

                _trailPath.Reset();
                _trailPath.MoveTo(drop.X, drop.Y);
                _trailPath.LineTo(drop.X, drop.Y - trailLength);

                // Quick reject to avoid drawing invisible paths
                if (canvas.QuickReject(_trailPath.Bounds))
                    continue;

                canvas.DrawPath(_trailPath, trailPaint);
            }
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.RenderRaindropTrails",
            ErrorMessage = "Error rendering raindrop trails"
        });
    }

    /// <summary>
    /// Renders the raindrops with optional highlights.
    /// </summary>
    private void RenderRaindrops(SKCanvas canvas, float[] spectrum, SKPaint basePaint)
    {
        Safe(() =>
        {
            using var dropPaint = _paintPool.Get();
            dropPaint.Style = Fill;
            dropPaint.IsAntialias = _useAntiAlias;

            using var highlightPaint = _paintPool.Get();
            highlightPaint.Style = Fill;
            highlightPaint.IsAntialias = _useAntiAlias;

            for (int i = 0; i < _raindropCount; i++)
            {
                Raindrop drop = _raindrops[i];
                float intensity = drop.SpectrumIndex < spectrum.Length
                    ? spectrum[drop.SpectrumIndex] * 0.7f + drop.Intensity * 0.3f
                    : drop.Intensity;

                byte alpha = (byte)(255 * Min(0.7f + intensity * 0.3f, 1.0f));
                dropPaint.Color = basePaint.Color.WithAlpha(alpha);

                // Quick rejection for raindrop circle
                SKRect dropRect = new SKRect(drop.X - drop.Size, drop.Y - drop.Size, drop.X + drop.Size, drop.Y + drop.Size);
                if (canvas.QuickReject(dropRect))
                    continue;

                canvas.DrawCircle(drop.X, drop.Y, drop.Size, dropPaint);

                // Add highlight for larger drops if quality permits
                if (_effectsThreshold < 2 &&
                    drop.Size > Settings.Instance.RaindropSize * Constants.RAINDROP_SIZE_HIGHLIGHT_THRESHOLD &&
                    intensity > Constants.INTENSITY_HIGHLIGHT_THRESHOLD)
                {
                    float highlightSize = drop.Size * Constants.HIGHLIGHT_SIZE_MULTIPLIER;
                    float highlightX = drop.X - drop.Size * Constants.HIGHLIGHT_OFFSET_MULTIPLIER;
                    float highlightY = drop.Y - drop.Size * Constants.HIGHLIGHT_OFFSET_MULTIPLIER;
                    highlightPaint.Color = SKColors.White.WithAlpha((byte)(150 * intensity));
                    canvas.DrawCircle(highlightX, highlightY, highlightSize, highlightPaint);
                }
            }
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.RenderRaindrops",
            ErrorMessage = "Error rendering raindrops"
        });
    }
    #endregion

    #region Disposal
    /// <summary>
    /// Disposes of resources used by the renderer.
    /// </summary>
    public override void Dispose()
    {
        if (!_disposed)
        {
            Safe(() =>
            {
                Settings.Instance.PropertyChanged -= OnSettingsChanged;

                // Dispose synchronization primitives
                _spectrumSemaphore?.Dispose();

                // Dispose rendering resources
                _trailPath?.Dispose();

                // Dispose object pools
                _paintPool?.Dispose();

                // Clean up cached data
                _processedSpectrum = null;

                // Call base implementation
                base.Dispose();
            }, new ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.Dispose",
                ErrorMessage = "Error during disposal"
            });

            _disposed = true;
            Log(LogLevel.Debug, Constants.LOG_PREFIX, "Disposed");
        }
    }
    #endregion
}