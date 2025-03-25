#nullable enable

using System.Numerics;
using System.Runtime.CompilerServices;
using static SpectrumNet.SmartLogger;

namespace SpectrumNet
{
    /// <summary>
    /// Renderer that creates a dynamic constellation of stars that react to audio spectrum data.
    /// </summary>
    public sealed class ConstellationRenderer : BaseSpectrumRenderer
    {
        #region Singleton Pattern
        private static readonly Lazy<ConstellationRenderer> _instance = new(() => new ConstellationRenderer());
        private ConstellationRenderer() { } // Приватный конструктор
        public static ConstellationRenderer GetInstance() => _instance.Value;
        #endregion

        #region Constants
        private static class Constants
        {
            // Logging
            public const string LOG_PREFIX = "ConstellationRenderer";

            // Star properties
            public const int DEFAULT_STAR_COUNT = 120;         // Default number of stars
            public const int OVERLAY_STAR_COUNT = 80;          // Number of stars in overlay mode
            public const int MIN_STAR_X = 100;                 // Minimum initial X position
            public const int MAX_STAR_X = 900;                 // Maximum initial X position
            public const int MIN_STAR_Y = 100;                 // Minimum initial Y position
            public const int MAX_STAR_Y = 700;                 // Maximum initial Y position
            public const float MIN_STAR_SIZE = 3.0f;           // Minimum star size in pixels
            public const float MAX_STAR_SIZE = 9.0f;           // Maximum star size in pixels
            public const float DEFAULT_BRIGHTNESS = 0.65f;     // Default star brightness (0-1)
            public const float BRIGHTNESS_VARIATION = 0.35f;   // Random variation in brightness
            public const float MIN_BRIGHTNESS = 0.38f;         // Minimum allowed brightness
            public const float MAX_BRIGHTNESS = 1.0f;          // Maximum allowed brightness
            public const float TWINKLE_SPEED = 1.4f;           // Speed of twinkle animation
            public const float TWINKLE_INFLUENCE = 0.22f;      // Influence of twinkle on brightness
            public const float SPECTRUM_INFLUENCE = 0.5f;      // Influence of spectrum on brightness
            public const float SPECTRUM_AMPLIFICATION = 3.0f;  // Amplification factor for spectrum
            public const float SPECTRUM_SIZE_INFLUENCE = 0.4f; // Influence of spectrum on star size
            public const float DIRECTION_CHANGE_CHANCE = 0.008f; // Chance to change direction
            public const float BASE_TWINKLE_SPEED = 0.6f;      // Base speed for twinkle
            public const float MAX_TWINKLE_SPEED_VARIATION = 1.0f; // Maximum variation in twinkle speed
            public const byte MIN_STAR_COLOR_VALUE = 130;      // Minimum value for star color components
            public const byte MAX_STAR_COLOR_VARIATION = 50;   // Maximum variation in star color
            public const byte BASE_STAR_COLOR = 210;           // Base value for star color

            // Rendering constants
            public const int UPDATE_INTERVAL = 16;             // Update interval in milliseconds
            public const float GLOW_RADIUS = 3.0f;             // Blur radius for glow effect
            public const float FADE_IN_SPEED = 2.2f;           // Speed of fade-in effect
            public const float FADE_OUT_SPEED = 1.5f;          // Speed of fade-out effect
            public const float UNIT_RADIUS = 1.0f;             // Unit radius for shader drawing

            // Physics constants
            public const float TIME_STEP = 0.016f;             // Physics timestep in seconds
            public const float BASE_STAR_VELOCITY = 2.5f;      // Base velocity for stars
            public const float STAR_ACCELERATION = 0.25f;      // Acceleration factor for stars
            public const float STAR_DAMPING = 0.95f;           // Damping factor for star movement
            public const float MAX_SPEED = 12.0f;              // Maximum speed for stars
            public const float VELOCITY_LERP = 0.08f;          // Interpolation factor for velocity
            public const float EDGE_REPULSION_DISTANCE = 120.0f; // Distance for edge repulsion
            public const float EDGE_REPULSION_FORCE = 0.45f;   // Force of edge repulsion
            public const float REPULSION_CURVE_POWER = 2.5f;   // Exponent for repulsion falloff
            public const float MIN_STAR_LIFETIME = 5.0f;       // Minimum lifetime of a star in seconds
            public const float MAX_STAR_LIFETIME = 15.0f;      // Maximum lifetime of a star in seconds
            public const float SPAWN_THRESHOLD = 0.1f;         // Threshold for spawning new stars
            public const float MAX_SPAWN_RATE = 5.0f;          // Maximum stars spawned per second
            public const float SPECTRUM_TIMEOUT = 0.3f;        // Timeout for spectrum data in seconds
            public const float FORCE_CLEAR_TIMEOUT = 2.0f;     // Timeout for forcing star clearance
            public const float FREQUENCY_FORCE_FACTOR = 0.5f;  // Factor for frequency-based forces

            // Spectrum processing constants
            public const float SMOOTHING_FACTOR = 0.15f;       // Factor for smoothing spectrum data

            // Quality settings
            public static class Quality
            {
                // Low quality settings
                public const bool LOW_USE_GLOW_EFFECTS = false;

                // Medium quality settings
                public const bool MEDIUM_USE_GLOW_EFFECTS = true;

                // High quality settings
                public const bool HIGH_USE_GLOW_EFFECTS = true;
            }
        }
        #endregion

        #region Fields
        // Spectrum analysis fields
        private float _lowSpectrum;
        private float _midSpectrum;
        private float _highSpectrum;
        private float _processedSpectrum;
        private float _spectrumEnergy;
        private float _spawnAccumulator;
        private float _lastSpectrumUpdateTime;
        private float _timeSinceLastSpectrum;
        private bool _hasActiveSpectrum; // Indicates if spectrum data is active

        // Star rendering objects
        private Star[]? _stars;
        private SKPaint? _starPaint;
        private SKPaint? _glowPaint;
        private SKShader? _glowShader;
        private readonly Random _random = new();
        private float _time;
        private int _starCount;
        private new SKImageInfo _lastRenderInfo;

        // Synchronization and state
        private readonly object _starsLock = new();
        private CancellationTokenSource _updateTokenSource = new();
        private Task? _updateTask;
        private volatile bool _needsUpdate;
        private SKSurface? _renderSurface;

        // Quality settings
        private bool _useGlowEffects = true;
        private new bool _disposed;

        // Store flag for overlay mode
        private readonly bool _isOverlayMode;
        #endregion

        #region Initialization and Configuration
        /// <summary>
        /// Initializes the constellation renderer and prepares resources for rendering.
        /// </summary>
        public override void Initialize()
        {
            Safe(() =>
            {
                base.Initialize();

                // Initialize with appropriate star count based on overlay mode
                _starCount = _isOverlayMode ? Constants.OVERLAY_STAR_COUNT : Constants.DEFAULT_STAR_COUNT;

                // Create stars and rendering resources
                InitializeStars(_starCount);
                InitializeShadersAndPaints();

                // Start the background update loop for star physics
                StartUpdateLoop();

                Log(LogLevel.Debug, Constants.LOG_PREFIX, "Initialized");
            }, new ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.Initialize",
                ErrorMessage = "Failed to initialize renderer"
            });
        }

        /// <summary>
        /// Initializes shader and paint objects for star rendering.
        /// </summary>
        private void InitializeShadersAndPaints()
        {
            Safe(() =>
            {
                // Create basic paint for stars
                _starPaint = CreateBasicPaint(SKColors.White);

                // Create radial gradient shader for glow effect
                _glowShader = SKShader.CreateRadialGradient(
                    new SKPoint(0, 0),
                    Constants.UNIT_RADIUS,
                    new SKColor[] { new SKColor(255, 255, 255, 128), SKColors.Transparent },
                    new float[] { 0.0f, 1.0f },
                    SKShaderTileMode.Clamp);

                // Create paint for glow effect
                _glowPaint = CreateGlowPaint(SKColors.White, Constants.GLOW_RADIUS, 128);
                if (_glowPaint != null && _glowShader != null)
                {
                    _glowPaint.BlendMode = SKBlendMode.Plus;
                    _glowPaint.Shader = _glowShader;
                }
            }, new ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.InitializeShadersAndPaints",
                ErrorMessage = "Failed to initialize shaders and paints"
            });
        }

        /// <summary>
        /// Creates a basic paint object with specified color.
        /// </summary>
        private SKPaint CreateBasicPaint(SKColor color)
        {
            return new SKPaint
            {
                Color = color,
                IsAntialias = _useAntiAlias,
                Style = SKPaintStyle.Fill
            };
        }

        /// <summary>
        /// Creates a paint object for glow effects.
        /// </summary>
        private SKPaint CreateGlowPaint(SKColor color, float radius, byte alpha)
        {
            return new SKPaint
            {
                Color = color.WithAlpha(alpha),
                IsAntialias = _useAntiAlias,
                Style = SKPaintStyle.Fill,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, radius)
            };
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

                // Skip if configuration hasn't changed
                if (_isOverlayMode == isOverlayActive && _quality == quality) return;

                // Update star count based on overlay mode
                _starCount = _isOverlayMode ? Constants.OVERLAY_STAR_COUNT : Constants.DEFAULT_STAR_COUNT;

                // Reinitialize stars with new count
                InitializeStars(_starCount);
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

                // Set quality-dependent settings
                _useGlowEffects = _quality switch
                {
                    RenderQuality.Low => Constants.Quality.LOW_USE_GLOW_EFFECTS,
                    RenderQuality.Medium => Constants.Quality.MEDIUM_USE_GLOW_EFFECTS,
                    RenderQuality.High => Constants.Quality.HIGH_USE_GLOW_EFFECTS,
                    _ => true
                };

                // Update paint properties based on quality
                if (_starPaint != null)
                {
                    _starPaint.IsAntialias = _useAntiAlias;
                }

                if (_glowPaint != null)
                {
                    _glowPaint.IsAntialias = _useAntiAlias;
                }
            }, new ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.ApplyQualitySettings",
                ErrorMessage = "Failed to apply quality settings"
            });
        }
        #endregion

        #region Rendering
        /// <summary>
        /// Renders the constellation visualization on the canvas using spectrum data.
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
            if (!QuickValidate(canvas, spectrum, info, paint))
            {
                drawPerformanceInfo?.Invoke(canvas!, info);
                return;
            }

            // Quick reject if canvas area is not visible
            SKRect renderBounds = new(0, 0, info.Width, info.Height);
            if (canvas!.QuickReject(renderBounds))
            {
                drawPerformanceInfo?.Invoke(canvas, info);
                return;
            }

            Safe(() =>
            {
                bool semaphoreAcquired = false;
                try
                {
                    // Try to acquire semaphore for updating spectrum data
                    semaphoreAcquired = _spectrumSemaphore.Wait(0);
                    if (semaphoreAcquired)
                    {
                        // Update time since last spectrum data
                        _timeSinceLastSpectrum += Constants.TIME_STEP;

                        // Process new spectrum data if available
                        bool hasNewSpectrumData = spectrum != null && spectrum.Length >= 2;
                        if (hasNewSpectrumData)
                        {
                            ProcessSpectrum(spectrum!);
                            _hasActiveSpectrum = true;
                            _lastSpectrumUpdateTime = _time;
                            _timeSinceLastSpectrum = 0;
                        }
                        else
                        {
                            // Fade out spectrum energy when no data is received
                            _spectrumEnergy = 0;
                            if (_time - _lastSpectrumUpdateTime > Constants.SPECTRUM_TIMEOUT)
                            {
                                _hasActiveSpectrum = false;
                                _processedSpectrum = 0;
                            }
                        }

                        // Update render surface if needed
                        if (info.Width != _lastRenderInfo.Width ||
                            info.Height != _lastRenderInfo.Height ||
                            _renderSurface == null)
                        {
                            UpdateRenderSurfaces(info);
                        }

                        // Flag for update in background thread
                        _needsUpdate = true;

                        // Clear stars if no spectrum for too long
                        if (_timeSinceLastSpectrum > Constants.FORCE_CLEAR_TIMEOUT)
                        {
                            ClearAllStars();
                        }
                    }

                    // Render current star field
                    RenderStarField(canvas!, info);
                }
                finally
                {
                    // Release semaphore if acquired
                    if (semaphoreAcquired)
                        _spectrumSemaphore.Release();
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
        /// Validates essential rendering parameters.
        /// </summary>
        private bool QuickValidate(SKCanvas? canvas, float[]? spectrum, SKImageInfo info, SKPaint? paint)
        {
            if (canvas == null || paint == null)
            {
                Log(LogLevel.Warning, Constants.LOG_PREFIX, "Canvas or paint is null");
                return false;
            }

            if (info.Width <= 0 || info.Height <= 0)
            {
                Log(LogLevel.Warning, Constants.LOG_PREFIX, "Invalid canvas dimensions");
                return false;
            }

            if (_disposed)
            {
                Log(LogLevel.Warning, Constants.LOG_PREFIX, "Renderer is disposed");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Clears all stars from the field.
        /// </summary>
        private void ClearAllStars()
        {
            Safe(() =>
            {
                if (_stars == null) return;

                lock (_starsLock)
                {
                    for (int i = 0; i < _stars.Length; i++)
                    {
                        _stars[i] = _stars[i] with { IsActive = false, Opacity = 0 };
                    }
                }
            }, new ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.ClearAllStars",
                ErrorMessage = "Failed to clear stars"
            });
        }
        #endregion

        #region Rendering Implementation
        /// <summary>
        /// Updates render surfaces when canvas size changes.
        /// </summary>
        private void UpdateRenderSurfaces(SKImageInfo info)
        {
            Safe(() =>
            {
                _lastRenderInfo = info;
                _renderSurface?.Dispose();
                _renderSurface = SKSurface.Create(info);
            }, new ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.UpdateRenderSurfaces",
                ErrorMessage = "Failed to update render surfaces"
            });
        }

        /// <summary>
        /// Renders the star field to the canvas.
        /// </summary>
        private void RenderStarField(SKCanvas canvas, SKImageInfo info)
        {
            Safe(() =>
            {
                // Validate required resources
                if (_stars == null || _starPaint == null || _glowPaint == null || _renderSurface == null)
                    return;

                // Clear the render surface
                _renderSurface.Canvas.Clear(SKColors.Transparent);
                SKCanvas renderCanvas = _renderSurface.Canvas;

                // Render each active star
                lock (_starsLock)
                {
                    foreach (var star in _stars)
                    {
                        // Skip inactive or invisible stars
                        if (!star.IsActive || star.Lifetime <= 0 || star.Opacity <= 0.01f)
                            continue;

                        // Skip stars outside the visible area
                        if (star.X < -10 || star.X > info.Width + 10 || star.Y < -10 || star.Y > info.Height + 10)
                            continue;

                        // Calculate fade effect based on lifetime
                        float lifetimeRatio = star.Lifetime / star.MaxLifetime;
                        float fadeEffect = lifetimeRatio < 0.2f ? lifetimeRatio / 0.2f : 1.0f;
                        float finalOpacity = star.Opacity * fadeEffect;

                        if (finalOpacity < 0.01f)
                            continue;

                        // Calculate final alpha and size
                        byte alpha = (byte)Clamp((int)(255 * star.Brightness * finalOpacity), 0, 255);
                        float dynamicSize = star.Size * (0.7f + lifetimeRatio * 0.3f);

                        // Render glow effect for bright stars
                        if (_useGlowEffects && (star.Brightness > Constants.DEFAULT_BRIGHTNESS || _spectrumEnergy > 0.45f))
                        {
                            float glowSize = dynamicSize * (2.2f + _spectrumEnergy * 1.2f) * finalOpacity;
                            byte glowAlpha = (byte)(alpha * 0.6f);

                            if (_glowPaint != null)
                            {
                                _glowPaint.Color = star.Color.WithAlpha(glowAlpha);

                                renderCanvas.Save();
                                renderCanvas.Translate(star.X, star.Y);
                                renderCanvas.Scale(glowSize, glowSize);
                                renderCanvas.DrawCircle(0, 0, Constants.UNIT_RADIUS, _glowPaint);
                                renderCanvas.Restore();
                            }
                        }

                        // Render star core
                        if (_starPaint != null)
                        {
                            _starPaint.Color = star.Color.WithAlpha(alpha);
                            renderCanvas.Save();
                            renderCanvas.Translate(star.X, star.Y);
                            renderCanvas.Scale(dynamicSize, dynamicSize);
                            renderCanvas.DrawCircle(0, 0, Constants.UNIT_RADIUS, _starPaint);
                            renderCanvas.Restore();
                        }
                    }
                }

                // Draw final composite to main canvas
                using SKImage finalSnapshot = _renderSurface.Snapshot();
                canvas.DrawImage(finalSnapshot, 0, 0);
            }, new ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.RenderStarField",
                ErrorMessage = "Error rendering star field"
            });
        }
        #endregion

        #region Physics and Simulation
        /// <summary>
        /// Starts the background physics update loop.
        /// </summary>
        private void StartUpdateLoop()
        {
            Safe(() =>
            {
                // Cancel existing update task if running
                if (_updateTokenSource != null && !_updateTokenSource.IsCancellationRequested)
                    _updateTokenSource.Cancel();

                if (_updateTask != null)
                {
                    try { _updateTask.Wait(500); }
                    catch { /* Ignore cancellation exceptions */ }
                }

                // Create new cancellation token source
                if (_updateTokenSource != null)
                {
                    _updateTokenSource.Dispose();
                    _updateTokenSource = new CancellationTokenSource();
                }

                // Start new update task
                _updateTask = Task.Run(async () =>
                {
                    float accumulatedTime = 0;
                    while (!_updateTokenSource.IsCancellationRequested)
                    {
                        if (_needsUpdate && _isInitialized)
                        {
                            // Update simulation time
                            _time += Constants.TIME_STEP;
                            accumulatedTime += Constants.TIME_STEP;

                            // Update star physics at fixed time steps
                            if (accumulatedTime >= Constants.TIME_STEP && _stars != null && _lastRenderInfo.Width > 0)
                            {
                                if (_hasActiveSpectrum)
                                    UpdateStars(_lastRenderInfo);
                                else
                                    FadeOutStars();

                                accumulatedTime -= Constants.TIME_STEP;
                            }

                            _needsUpdate = false;
                        }

                        // Wait for next update interval
                        await Task.Delay(Constants.UPDATE_INTERVAL, _updateTokenSource.Token);
                    }
                }, _updateTokenSource.Token);
            }, new ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.StartUpdateLoop",
                ErrorMessage = "Failed to start update loop"
            });
        }

        /// <summary>
        /// Fades out all stars when no spectrum data is active.
        /// </summary>
        private void FadeOutStars()
        {
            Safe(() =>
            {
                if (_stars == null) return;

                lock (_starsLock)
                {
                    for (int i = 0; i < _stars.Length; i++)
                    {
                        var star = _stars[i];
                        if (!star.IsActive) continue;

                        // Reduce opacity over time
                        float newOpacity = Math.Max(star.Opacity - Constants.TIME_STEP * Constants.FADE_OUT_SPEED, 0);

                        // Deactivate stars that have faded out completely
                        if (newOpacity <= 0.01f)
                        {
                            _stars[i] = star with { IsActive = false, Opacity = 0 };
                        }
                        else
                        {
                            // Reduce lifetime faster during fade out
                            float newLifetime = Math.Max(star.Lifetime - Constants.TIME_STEP * 2, 0);

                            if (newLifetime <= 0)
                            {
                                _stars[i] = star with { IsActive = false, Lifetime = 0, Opacity = 0 };
                            }
                            else
                            {
                                // Update star with reduced opacity and slower movement
                                _stars[i] = star with
                                {
                                    Opacity = newOpacity,
                                    Lifetime = newLifetime,
                                    VelocityX = star.VelocityX * 0.9f,
                                    VelocityY = star.VelocityY * 0.9f
                                };
                            }
                        }
                    }
                }
            }, new ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.FadeOutStars",
                ErrorMessage = "Error fading out stars"
            });
        }

        /// <summary>
        /// Updates star positions and properties based on physics simulation.
        /// </summary>
        private void UpdateStars(SKImageInfo info)
        {
            Safe(() =>
            {
            if (_stars == null) return;

            float screenWidth = info.Width, screenHeight = info.Height;

            // Spawn new stars based on spectrum energy
            if (_hasActiveSpectrum && _midSpectrum >= Constants.SPAWN_THRESHOLD)
            {
                _spawnAccumulator += _midSpectrum * Constants.TIME_STEP * Constants.MAX_SPAWN_RATE;
                SpawnNewStars(screenWidth, screenHeight);
            }

            lock (_starsLock)
            {
                for (int i = 0; i < _stars.Length; i++)
                {
                    var star = _stars[i];
                    if (!star.IsActive) continue;

                    // Update star lifetime
                    float newLifetime = star.Lifetime - Constants.TIME_STEP;
                    if (newLifetime <= 0)
                    {
                        _stars[i] = star with { IsActive = false, Lifetime = 0, Opacity = 0 };
                        continue;
                    }

                    // Fade in new stars
                    float newOpacity = Math.Min(star.Opacity + Constants.TIME_STEP * Constants.FADE_IN_SPEED, 1.0f);

                    // Calculate twinkling effect
                    float twinkling = MathF.Sin(_time * Constants.TWINKLE_SPEED * star.TwinkleSpeed + star.TwinkleFactor);
                    float targetBrightness = Clamp(
                        Constants.DEFAULT_BRIGHTNESS +
                        (twinkling * Constants.TWINKLE_INFLUENCE) +
                        (_spectrumEnergy * Constants.SPECTRUM_INFLUENCE),
                        Constants.MIN_BRIGHTNESS,
                        Constants.MAX_BRIGHTNESS);

                    // Calculate forces acting on the star
                    float forceX = 0, forceY = 0;
                    CalculateEdgeRepulsion(star, screenWidth, screenHeight, ref forceX, ref forceY);

                    // Apply physics to calculate new position and velocity
                    float inertiaFactor = 1.0f / (0.8f + star.Mass * 0.5f);
                    Vector2 acceleration = new Vector2(forceX, forceY) * Constants.STAR_ACCELERATION * inertiaFactor;
                    float spectrumAcceleration = 1.0f + _spectrumEnergy * 1.5f;
                    acceleration *= spectrumAcceleration;

                    // Apply frequency-based forces
                    Vector2 frequencyForce = new Vector2(
                        (_highSpectrum - _lowSpectrum),
                        _midSpectrum) * Constants.FREQUENCY_FORCE_FACTOR;
                    acceleration += frequencyForce;

                    // Calculate new velocity
                    Vector2 currentVelocity = new Vector2(star.VelocityX, star.VelocityY);
                    Vector2 targetVelocity = currentVelocity + acceleration;

                    // Apply damping based on lifetime
                    float lifetimeRatio = newLifetime / star.MaxLifetime;
                    float dampingFactor = Constants.STAR_DAMPING;
                    if (lifetimeRatio < 0.3f)
                        dampingFactor *= (0.8f + lifetimeRatio * 0.2f);
                    targetVelocity *= dampingFactor;

                    // Limit maximum speed
                    float maxSpeed = Constants.MAX_SPEED * (0.5f + _spectrumEnergy * 0.8f);
                    float speedSq = Vector2.Dot(targetVelocity, targetVelocity);
                    if (speedSq > maxSpeed * maxSpeed)
                        targetVelocity = Vector2.Normalize(targetVelocity) * maxSpeed;

                    // Apply velocity interpolation for smoother movement
                    float velocityLerpFactor = Constants.VELOCITY_LERP * (1.0f + _spectrumEnergy * 0.5f);
                    Vector2 newVelocity = currentVelocity + (targetVelocity - currentVelocity) * velocityLerpFactor;

                    // Calculate new position
                    float newX = star.X + newVelocity.X;
                    float newY = star.Y + newVelocity.Y;

                    // Wrap around screen edges
                    if (newX < -Constants.EDGE_REPULSION_DISTANCE) newX = screenWidth + 10;
                    else if (newX > screenWidth + Constants.EDGE_REPULSION_DISTANCE) newX = -10;
                    if (newY < -Constants.EDGE_REPULSION_DISTANCE) newY = screenHeight + 10;
                    else if (newY > screenHeight + Constants.EDGE_REPULSION_DISTANCE) newY = -10;

                    // Calculate current speed
                    float speed = newVelocity.Length();

                    // Update star with new properties
                    _stars[i] = new Star
                    {
                        X = newX,
                        Y = newY,
                        Size = star.Size,
                        Brightness = star.Brightness + (targetBrightness - star.Brightness) * (0.08f + _spectrumEnergy * 0.15f),
                        TwinkleFactor = star.TwinkleFactor,
                        TwinkleSpeed = star.TwinkleSpeed,
                        Color = star.Color,
                        IsActive = true,
                        VelocityX = newVelocity.X,
                        VelocityY = newVelocity.Y,
                        Mass = star.Mass,
                        Speed = speed,
                        Lifetime = newLifetime,
                        MaxLifetime = star.MaxLifetime,
                        Opacity = newOpacity
                    };
                }

                    // Occasionally change direction of a random star based on spectrum energy
                    if (_random.NextDouble() < Constants.DIRECTION_CHANGE_CHANCE * (1 + _spectrumEnergy * 3) && _stars.Length > 0)
                    {
                        int randomIndex = _random.Next(_stars.Length);
                        if (randomIndex < _stars.Length && _stars[randomIndex].IsActive)
                        {
                            float currentAngle = MathF.Atan2(_stars[randomIndex].VelocityY, _stars[randomIndex].VelocityX);
                            float angleChange = (float)(_random.NextDouble() * 0.5 - 0.25) * MathF.PI;
                            float newAngle = currentAngle + angleChange;
                            float magnitude = Constants.BASE_STAR_VELOCITY * (0.8f + _spectrumEnergy * 1.2f) *
                                             (0.8f + _stars[randomIndex].Mass * 0.4f);
                            float newVelX = MathF.Cos(newAngle) * magnitude;
                            float newVelY = MathF.Sin(newAngle) * magnitude;
                            _stars[randomIndex] = _stars[randomIndex] with { VelocityX = newVelX, VelocityY = newVelY };
                        }
                    }
                }
            }, new ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.UpdateStars",
                ErrorMessage = "Error updating stars"
            });
        }

        /// <summary>
        /// Calculates repulsion forces from screen edges.
        /// </summary>
        private void CalculateEdgeRepulsion(in Star star, float screenWidth, float screenHeight, ref float forceX, ref float forceY)
        {
            // Calculate distance to nearest edge in X and Y directions
            float edgeDistX = Math.Min(star.X, screenWidth - star.X);
            float edgeDistY = Math.Min(star.Y, screenHeight - star.Y);

            // Normalize distances
            float normalizedEdgeDistX = Clamp(edgeDistX / Constants.EDGE_REPULSION_DISTANCE, 0, 1);
            float normalizedEdgeDistY = Clamp(edgeDistY / Constants.EDGE_REPULSION_DISTANCE, 0, 1);

            // Calculate repulsion forces with non-linear falloff
            float edgeForceX = Constants.EDGE_REPULSION_FORCE *
                               (1 - MathF.Pow(normalizedEdgeDistX, Constants.REPULSION_CURVE_POWER)) *
                               MathF.Sign(screenWidth / 2 - star.X);

            float edgeForceY = Constants.EDGE_REPULSION_FORCE *
                               (1 - MathF.Pow(normalizedEdgeDistY, Constants.REPULSION_CURVE_POWER)) *
                               MathF.Sign(screenHeight / 2 - star.Y);

            // Apply calculated forces
            forceX += edgeForceX;
            forceY += edgeForceY;
        }

        /// <summary>
        /// Spawns new stars based on spectrum energy.
        /// </summary>
        private void SpawnNewStars(float screenWidth, float screenHeight)
        {
            Safe(() =>
            {
                if (_stars == null || _midSpectrum < Constants.SPAWN_THRESHOLD) return;

                // Calculate number of stars to spawn
                int starsToSpawn = (int)_spawnAccumulator;
                _spawnAccumulator -= starsToSpawn;

                lock (_starsLock)
                {
                    for (int i = 0; i < starsToSpawn; i++)
                    {
                        // Find an available slot for a new star
                        int availableSlot = -1;
                        for (int j = 0; j < _stars.Length; j++)
                        {
                            if (!_stars[j].IsActive || _stars[j].Lifetime <= 0)
                            {
                                availableSlot = j;
                                break;
                            }
                        }

                        // Skip if no slot is available
                        if (availableSlot == -1) continue;

                        // Generate random position within screen
                        float x = _random.Next(0, (int)screenWidth);
                        float y = _random.Next(0, (int)screenHeight);

                        // Generate random properties
                        float starSize = Constants.MIN_STAR_SIZE +
                                        (Constants.MAX_STAR_SIZE - Constants.MIN_STAR_SIZE) *
                                        (float)_random.NextDouble();

                        float lifetime = Constants.MIN_STAR_LIFETIME +
                                        (Constants.MAX_STAR_LIFETIME - Constants.MIN_STAR_LIFETIME) *
                                        (float)_random.NextDouble();

                        float brightness = Constants.DEFAULT_BRIGHTNESS +
                                          (float)_random.NextDouble() * Constants.BRIGHTNESS_VARIATION;

                        // Generate random color
                        byte r = (byte)(_random.Next(Constants.MIN_STAR_COLOR_VALUE, 255));
                        byte g = (byte)(_random.Next(Constants.MIN_STAR_COLOR_VALUE, 255));
                        byte b = (byte)(_random.Next(Constants.MIN_STAR_COLOR_VALUE, 255));
                        var color = new SKColor(r, g, b);

                        // Calculate initial velocity based on spectrum
                        float initialSpeedMultiplier = 0.5f + _midSpectrum * 1.5f;
                        float baseAngle = (float)(_random.NextDouble() * MathF.PI * 2);
                        float angleOffset = (_highSpectrum - _lowSpectrum) * 0.5f;
                        float angle = baseAngle + angleOffset;
                        float vx = MathF.Cos(angle) * Constants.BASE_STAR_VELOCITY * initialSpeedMultiplier;
                        float vy = MathF.Sin(angle) * Constants.BASE_STAR_VELOCITY * initialSpeedMultiplier;

                        // Create new star
                        _stars[availableSlot] = new Star
                        {
                            X = x,
                            Y = y,
                            Size = starSize,
                            Brightness = brightness,
                            TwinkleFactor = (float)_random.NextDouble() * MathF.PI * 2,
                            TwinkleSpeed = Constants.BASE_TWINKLE_SPEED +
                                          (float)_random.NextDouble() * Constants.MAX_TWINKLE_SPEED_VARIATION,
                            Color = color,
                            IsActive = true,
                            VelocityX = vx,
                            VelocityY = vy,
                            Mass = starSize * 0.5f + 0.5f,
                            Speed = MathF.Sqrt(vx * vx + vy * vy),
                            Lifetime = lifetime,
                            MaxLifetime = lifetime,
                            Opacity = 0.01f  // Start with low opacity and fade in
                        };
                    }
                }
            }, new ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.SpawnNewStars",
                ErrorMessage = "Error spawning new stars"
            });
        }
        #endregion

        #region Spectrum Processing
        /// <summary>
        /// Processes spectrum data to extract frequency bands and energy.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private void ProcessSpectrum(float[] spectrum)
        {
            Safe(() =>
            {
                if (spectrum == null || spectrum.Length < 3) return;

                // Divide spectrum into frequency bands
                int totalLength = spectrum.Length;
                int bandLength = totalLength / 3;

                // Calculate sum for each frequency band
                float lowSum = 0, midSum = 0, highSum = 0;

                for (int i = 0; i < bandLength; i++)
                    lowSum += spectrum[i];

                for (int i = bandLength; i < 2 * bandLength; i++)
                    midSum += spectrum[i];

                for (int i = 2 * bandLength; i < totalLength; i++)
                    highSum += spectrum[i];

                // Calculate average for each band
                float avgLow = lowSum / bandLength;
                float avgMid = midSum / bandLength;
                float avgHigh = highSum / (totalLength - 2 * bandLength);

                // Apply smoothing to avoid abrupt changes
                _lowSpectrum = _lowSpectrum + (avgLow - _lowSpectrum) * Constants.SMOOTHING_FACTOR;
                _midSpectrum = _midSpectrum + (avgMid - _midSpectrum) * Constants.SMOOTHING_FACTOR;
                _highSpectrum = _highSpectrum + (avgHigh - _highSpectrum) * Constants.SMOOTHING_FACTOR;

                // Calculate overall spectrum energy
                _processedSpectrum = Math.Min(_midSpectrum * Constants.SPECTRUM_AMPLIFICATION, Constants.MAX_BRIGHTNESS);
                _spectrumEnergy = _processedSpectrum;
            }, new ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.ProcessSpectrum",
                ErrorMessage = "Error processing spectrum data"
            });
        }
        #endregion

        #region Helper Methods
        /// <summary>
        /// Clamps a float value between minimum and maximum.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;

        /// <summary>
        /// Clamps an integer value between minimum and maximum.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
        #endregion

        #region Star Initialization
        /// <summary>
        /// Initializes the star array with the specified count.
        /// </summary>
        private void InitializeStars(int count)
        {
            Safe(() =>
            {
                if (_disposed) throw new ObjectDisposedException(nameof(ConstellationRenderer));

                lock (_starsLock)
                {
                    _stars = new Star[count];

                    for (int i = 0; i < count; i++)
                    {
                        // Generate random color for each star
                        byte r = (byte)(_random.Next(Constants.MIN_STAR_COLOR_VALUE, 255));
                        byte g = (byte)(_random.Next(Constants.MIN_STAR_COLOR_VALUE, 255));
                        byte b = (byte)(_random.Next(Constants.MIN_STAR_COLOR_VALUE, 255));

                        // Initialize stars with random positions but inactive state
                        _stars[i] = new Star
                        {
                            X = _random.Next(Constants.MIN_STAR_X, Constants.MAX_STAR_X),
                            Y = _random.Next(Constants.MIN_STAR_Y, Constants.MAX_STAR_Y),
                            Size = Constants.MIN_STAR_SIZE +
                                  (float)_random.NextDouble() * (Constants.MAX_STAR_SIZE - Constants.MIN_STAR_SIZE),
                            Brightness = Constants.DEFAULT_BRIGHTNESS +
                                        (float)_random.NextDouble() * Constants.BRIGHTNESS_VARIATION,
                            TwinkleFactor = (float)_random.NextDouble() * MathF.PI * 2,
                            TwinkleSpeed = Constants.BASE_TWINKLE_SPEED +
                                          (float)_random.NextDouble() * Constants.MAX_TWINKLE_SPEED_VARIATION,
                            Color = new SKColor(r, g, b),
                            IsActive = false,
                            VelocityX = 0,
                            VelocityY = 0,
                            Mass = 1.0f,
                            Speed = 0,
                            Lifetime = 0,
                            MaxLifetime = 0,
                            Opacity = 0
                        };
                    }
                }
            }, new ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.InitializeStars",
                ErrorMessage = "Failed to initialize stars"
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
                    // Stop update loop
                    if (_updateTokenSource != null)
                    {
                        _updateTokenSource.Cancel();
                        try { _updateTask?.Wait(500); } catch { /* Ignore cancellation exceptions */ }
                        _updateTokenSource.Dispose();
                    }
                    _updateTask?.Dispose();

                    // Dispose rendering resources
                    _starPaint?.Dispose();
                    _glowPaint?.Dispose();
                    _renderSurface?.Dispose();
                    _glowShader?.Dispose();

                    // Clear references
                    _starPaint = null;
                    _glowPaint = null;
                    _renderSurface = null;
                    _stars = null;

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

        #region Data Structures
        /// <summary>
        /// Represents a star in the constellation.
        /// </summary>
        private readonly record struct Star
        {
            public float X { get; init; }
            public float Y { get; init; }
            public float Size { get; init; }
            public float Brightness { get; init; }
            public float TwinkleFactor { get; init; }
            public float TwinkleSpeed { get; init; }
            public SKColor Color { get; init; }
            public bool IsActive { get; init; }
            public float VelocityX { get; init; }
            public float VelocityY { get; init; }
            public float Mass { get; init; }
            public float Speed { get; init; }
            public float Lifetime { get; init; }
            public float MaxLifetime { get; init; }
            public float Opacity { get; init; }
        }
        #endregion
    }
}