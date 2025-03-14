#nullable enable

namespace SpectrumNet
{
    /// <summary>
    /// Renderer that creates a dynamic constellation of stars that react to audio spectrum data.
    /// </summary>
    public sealed class ConstellationRenderer : BaseSpectrumRenderer
    {
        #region Constants
        private static class Constants
        {
            // Star properties
            public const string LOG_PREFIX = "ConstellationRenderer";
            public const int DEFAULT_STAR_COUNT = 120;    // Default number of stars
            public const int OVERLAY_STAR_COUNT = 80;     // Number of stars in overlay mode
            public const int MIN_STAR_X = 100;            // Minimum initial X position
            public const int MAX_STAR_X = 900;            // Maximum initial X position
            public const int MIN_STAR_Y = 100;            // Minimum initial Y position
            public const int MAX_STAR_Y = 700;            // Maximum initial Y position
            public const float MIN_STAR_SIZE = 3.0f;      // Minimum star size in pixels
            public const float MAX_STAR_SIZE = 9.0f;      // Maximum star size in pixels
            public const float DEFAULT_BRIGHTNESS = 0.65f;// Default star brightness (0-1)
            public const float BRIGHTNESS_VARIATION = 0.35f;// Random variation in brightness
            public const float MIN_BRIGHTNESS = 0.38f;    // Minimum allowed brightness
            public const float MAX_BRIGHTNESS = 1.0f;     // Maximum allowed brightness
            public const float TWINKLE_SPEED = 1.4f;      // Speed of twinkle animation
            public const float TWINKLE_INFLUENCE = 0.22f; // Influence of twinkle on brightness
            public const float SPECTRUM_INFLUENCE = 0.5f; // Influence of spectrum on brightness
            public const float SPECTRUM_AMPLIFICATION = 3.0f; // Amplification factor for spectrum
            public const float SPECTRUM_SIZE_INFLUENCE = 0.4f; // Influence of spectrum on star size
            public const float DIRECTION_CHANGE_CHANCE = 0.008f; // Chance to change direction
            public const float BASE_TWINKLE_SPEED = 0.6f; // Base speed for twinkle
            public const float MAX_TWINKLE_SPEED_VARIATION = 1.0f; // Maximum variation in twinkle speed
            public const byte MIN_STAR_COLOR_VALUE = 130; // Minimum value for star color components
            public const byte MAX_STAR_COLOR_VARIATION = 50; // Maximum variation in star color
            public const byte BASE_STAR_COLOR = 210;      // Base value for star color

            // Rendering constants
            public const int UPDATE_INTERVAL = 16;        // Update interval in milliseconds
            public const float GLOW_RADIUS = 3.0f;        // Blur radius for glow effect
            public const float FADE_IN_SPEED = 2.2f;      // Speed of fade-in effect
            public const float FADE_OUT_SPEED = 1.5f;     // Speed of fade-out effect
            public const float UNIT_RADIUS = 1.0f;        // Unit radius for shader drawing

            // Physics constants
            public const float TIME_STEP = 0.016f;        // Physics timestep in seconds
            public const float BASE_STAR_VELOCITY = 2.5f; // Base velocity for stars
            public const float STAR_ACCELERATION = 0.25f; // Acceleration factor for stars
            public const float STAR_DAMPING = 0.95f;      // Damping factor for star movement
            public const float MAX_SPEED = 12.0f;         // Maximum speed for stars
            public const float VELOCITY_LERP = 0.08f;     // Interpolation factor for velocity
            public const float EDGE_REPULSION_DISTANCE = 120.0f; // Distance for edge repulsion
            public const float EDGE_REPULSION_FORCE = 0.45f; // Force of edge repulsion
            public const float REPULSION_CURVE_POWER = 2.5f; // Exponent for repulsion falloff
            public const float MIN_STAR_LIFETIME = 5.0f;  // Minimum lifetime of a star in seconds
            public const float MAX_STAR_LIFETIME = 15.0f; // Maximum lifetime of a star in seconds
            public const float SPAWN_THRESHOLD = 0.1f;    // Threshold for spawning new stars
            public const float MAX_SPAWN_RATE = 5.0f;     // Maximum stars spawned per second
            public const float SPECTRUM_TIMEOUT = 0.3f;   // Timeout for spectrum data in seconds
            public const float FORCE_CLEAR_TIMEOUT = 2.0f; // Timeout for forcing star clearance
            public const float FREQUENCY_FORCE_FACTOR = 0.5f; // Factor for frequency-based forces

            // Spectrum processing constants
            public const float SMOOTHING_FACTOR = 0.15f;  // Factor for smoothing spectrum data
        }
        #endregion

        #region Fields
        private static readonly Lazy<ConstellationRenderer> _instance = new(() => new ConstellationRenderer());
        private bool _isOverlayActive;
        private float _lowSpectrum, _midSpectrum, _highSpectrum, _processedSpectrum;
        private float _spectrumEnergy, _spawnAccumulator, _lastSpectrumUpdateTime, _timeSinceLastSpectrum;
        private bool _hasActiveSpectrum; // Indicates if spectrum data is active

        private Star[]? _stars;
        private SKPaint? _starPaint, _glowPaint;
        private SKShader? _starShader, _glowShader;
        private readonly Random _random = new();
        private float _time;
        private int _starCount;
        private SKImageInfo _lastRenderInfo;
        private readonly object _starsLock = new();
        private CancellationTokenSource _updateTokenSource = new();
        private Task? _updateTask;
        private volatile bool _needsUpdate;
        private SKSurface? _renderSurface;
        private bool _useGlowEffects = true;
        #endregion

        #region Initialization
        private ConstellationRenderer()
        {
            // Private constructor for singleton pattern
        }

        public static ConstellationRenderer GetInstance() => _instance.Value;

        public override void Initialize() => SmartLogger.Safe(() =>
        {
            base.Initialize();
            _starCount = _isOverlayActive ? Constants.OVERLAY_STAR_COUNT : Constants.DEFAULT_STAR_COUNT;
            InitializeStars(_starCount);
            InitializeShadersAndPaints();
            StartUpdateLoop();
            SmartLogger.Log(LogLevel.Debug, Constants.LOG_PREFIX, "Initialized");
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.Initialize",
            ErrorMessage = "Failed to initialize renderer"
        });

        private void InitializeShadersAndPaints() => SmartLogger.Safe(() =>
        {
            _starPaint = CreateBasicPaint(SKColors.White);

            _glowShader = SKShader.CreateRadialGradient(
                new SKPoint(0, 0), Constants.UNIT_RADIUS,
                new SKColor[] { new SKColor(255, 255, 255, 128), SKColors.Transparent },
                new float[] { 0.0f, 1.0f },
                SKShaderTileMode.Clamp);

            _glowPaint = CreateGlowPaint(SKColors.White, Constants.GLOW_RADIUS, 128);
            _glowPaint.BlendMode = SKBlendMode.Plus;
            _glowPaint.Shader = _glowShader;
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.InitializeShadersAndPaints",
            ErrorMessage = "Failed to initialize shaders and paints"
        });
        #endregion

        #region Public Interface
        public override void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium) => SmartLogger.Safe(() =>
        {
            base.Configure(isOverlayActive, quality);
            if (_isOverlayActive == isOverlayActive && Quality == quality) return;
            _isOverlayActive = isOverlayActive;
            _starCount = _isOverlayActive ? Constants.OVERLAY_STAR_COUNT : Constants.DEFAULT_STAR_COUNT;
            InitializeStars(_starCount);
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.Configure",
            ErrorMessage = "Failed to configure renderer"
        });

        protected override void ApplyQualitySettings() => SmartLogger.Safe(() =>
        {
            base.ApplyQualitySettings();
            _useGlowEffects = _quality switch
            {
                RenderQuality.Low => false,
                RenderQuality.Medium => true,
                RenderQuality.High => true,
                _ => true
            };

            if (_starPaint != null)
            {
                _starPaint.IsAntialias = _useAntiAlias;
                _starPaint.FilterQuality = _filterQuality;
            }

            if (_glowPaint != null)
            {
                _glowPaint.IsAntialias = _useAntiAlias;
                _glowPaint.FilterQuality = _filterQuality;
            }
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.ApplyQualitySettings",
            ErrorMessage = "Failed to apply quality settings"
        });

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
            if (!QuickValidate(canvas, spectrum, info, paint))
            {
                drawPerformanceInfo?.Invoke(canvas!, info);
                return;
            }

            SKRect renderBounds = new(0, 0, info.Width, info.Height);
            if (canvas!.QuickReject(renderBounds))
            {
                drawPerformanceInfo?.Invoke(canvas, info);
                return;
            }

            SmartLogger.Safe(() =>
            {
                bool semaphoreAcquired = false;
                try
                {
                    semaphoreAcquired = _spectrumSemaphore.Wait(0);
                    if (semaphoreAcquired)
                    {
                        _timeSinceLastSpectrum += Constants.TIME_STEP;
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
                            _spectrumEnergy = 0;
                            if (_time - _lastSpectrumUpdateTime > Constants.SPECTRUM_TIMEOUT)
                            {
                                _hasActiveSpectrum = false;
                                _processedSpectrum = 0;
                            }
                        }
                        if (info.Width != _lastRenderInfo.Width || info.Height != _lastRenderInfo.Height || _renderSurface == null)
                            UpdateRenderSurfaces(info);
                        _needsUpdate = true;
                        if (_timeSinceLastSpectrum > Constants.FORCE_CLEAR_TIMEOUT) ClearAllStars();
                    }
                    RenderStarField(canvas!, info);
                }
                finally { if (semaphoreAcquired) _spectrumSemaphore.Release(); }
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.Render",
                ErrorMessage = "Error during rendering"
            });

            drawPerformanceInfo?.Invoke(canvas!, info);
        }

        private void ClearAllStars() => SmartLogger.Safe(() =>
        {
            if (_stars == null) return;
            lock (_starsLock)
            {
                for (int i = 0; i < _stars.Length; i++)
                    _stars[i] = _stars[i] with { IsActive = false, Opacity = 0 };
            }
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.ClearAllStars",
            ErrorMessage = "Failed to clear stars"
        });
        #endregion

        #region Rendering Pipeline
        private void UpdateRenderSurfaces(SKImageInfo info) => SmartLogger.Safe(() =>
        {
            _lastRenderInfo = info;
            _renderSurface?.Dispose();
            _renderSurface = SKSurface.Create(info);
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.UpdateRenderSurfaces",
            ErrorMessage = "Failed to update render surfaces"
        });

        private void RenderStarField(SKCanvas canvas, SKImageInfo info) => SmartLogger.Safe(() =>
        {
        if (_stars == null || _starPaint == null || _glowPaint == null || _renderSurface == null) return;
        _renderSurface.Canvas.Clear(SKColors.Transparent);
        SKCanvas renderCanvas = _renderSurface.Canvas;
        lock (_starsLock)
        {
            foreach (var star in _stars)
            {
                if (!star.IsActive || star.Lifetime <= 0 || star.Opacity <= 0.01f) continue;
                if (star.X < -10 || star.X > info.Width + 10 || star.Y < -10 || star.Y > info.Height + 10) continue;
                float lifetimeRatio = star.Lifetime / star.MaxLifetime;
                float fadeEffect = lifetimeRatio < 0.2f ? lifetimeRatio / 0.2f : 1.0f;
                float finalOpacity = star.Opacity * fadeEffect;
                if (finalOpacity < 0.01f) continue;
                byte alpha = (byte)Clamp((int)(255 * star.Brightness * finalOpacity), 0, 255);
                float dynamicSize = star.Size * (0.7f + lifetimeRatio * 0.3f);
                if (_useGlowEffects && (star.Brightness > Constants.DEFAULT_BRIGHTNESS || _spectrumEnergy > 0.45f))
                {
                    float glowSize = dynamicSize * (2.2f + _spectrumEnergy * 1.2f) * finalOpacity;
                    byte glowAlpha = (byte)(alpha * 0.6f);
                    _glowPaint.Color = star.Color.WithAlpha(glowAlpha);
                    renderCanvas.Save();
                    renderCanvas.Translate(star.X, star.Y);
                    renderCanvas.Scale(glowSize, glowSize);
                    renderCanvas.DrawCircle(0, 0, Constants.UNIT_RADIUS, _glowPaint);
                    renderCanvas.Restore();
                }
                _starPaint.Color = star.Color.WithAlpha(alpha);
                renderCanvas.Save();
                renderCanvas.Translate(star.X, star.Y);
                renderCanvas.Scale(dynamicSize, dynamicSize);
                renderCanvas.DrawCircle(0, 0, Constants.UNIT_RADIUS, _starPaint);
                renderCanvas.Restore();
            }
        }
            using SKImage finalSnapshot = _renderSurface.Snapshot();
            canvas.DrawImage(finalSnapshot, 0, 0);
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.RenderStarField",
            ErrorMessage = "Error rendering star field"
        });
        #endregion

        #region Physics and Simulation
        private void StartUpdateLoop() => SmartLogger.Safe(() =>
        {
            if (_updateTokenSource != null && !_updateTokenSource.IsCancellationRequested)
                _updateTokenSource.Cancel();

            if (_updateTask != null)
            {
                try { _updateTask.Wait(500); } catch { }
            }

            if (_updateTokenSource != null)
            {
                _updateTokenSource.Dispose();
                _updateTokenSource = new CancellationTokenSource();
            }

            _updateTask = Task.Run(async () =>
            {
                float accumulatedTime = 0;
                while (!_updateTokenSource.IsCancellationRequested)
                {
                    if (_needsUpdate && _isInitialized)
                    {
                        _time += Constants.TIME_STEP;
                        accumulatedTime += Constants.TIME_STEP;
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
                    await Task.Delay(Constants.UPDATE_INTERVAL, _updateTokenSource.Token);
                }
            }, _updateTokenSource.Token);
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.StartUpdateLoop",
            ErrorMessage = "Failed to start update loop"
        });

        private void FadeOutStars() => SmartLogger.Safe(() =>
        {
            if (_stars == null) return;
            lock (_starsLock)
            {
                for (int i = 0; i < _stars.Length; i++)
                {
                    var star = _stars[i];
                    if (!star.IsActive) continue;
                    float newOpacity = Math.Max(star.Opacity - Constants.TIME_STEP * Constants.FADE_OUT_SPEED, 0);
                    if (newOpacity <= 0.01f)
                        _stars[i] = star with { IsActive = false, Opacity = 0 };
                    else
                    {
                        float newLifetime = Math.Max(star.Lifetime - Constants.TIME_STEP * 2, 0);
                        if (newLifetime <= 0)
                            _stars[i] = star with { IsActive = false, Lifetime = 0, Opacity = 0 };
                        else
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
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.FadeOutStars",
            ErrorMessage = "Error fading out stars"
        });

        private void UpdateStars(SKImageInfo info) => SmartLogger.Safe(() =>
        {
            if (_stars == null) return;
            float screenWidth = info.Width, screenHeight = info.Height;
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
                    float newLifetime = star.Lifetime - Constants.TIME_STEP;
                    if (newLifetime <= 0)
                    {
                        _stars[i] = star with { IsActive = false, Lifetime = 0, Opacity = 0 };
                        continue;
                    }
                    float newOpacity = Math.Min(star.Opacity + Constants.TIME_STEP * Constants.FADE_IN_SPEED, 1.0f);
                    float twinkling = MathF.Sin(_time * Constants.TWINKLE_SPEED * star.TwinkleSpeed + star.TwinkleFactor);
                    float targetBrightness = Clamp(Constants.DEFAULT_BRIGHTNESS + (twinkling * Constants.TWINKLE_INFLUENCE) + (_spectrumEnergy * Constants.SPECTRUM_INFLUENCE), Constants.MIN_BRIGHTNESS, Constants.MAX_BRIGHTNESS);
                    float forceX = 0, forceY = 0;
                    CalculateEdgeRepulsion(star, screenWidth, screenHeight, ref forceX, ref forceY);
                    float inertiaFactor = 1.0f / (0.8f + star.Mass * 0.5f);
                    Vector2 acceleration = new Vector2(forceX, forceY) * Constants.STAR_ACCELERATION * inertiaFactor;
                    float spectrumAcceleration = 1.0f + _spectrumEnergy * 1.5f;
                    acceleration *= spectrumAcceleration;
                    Vector2 frequencyForce = new Vector2((_highSpectrum - _lowSpectrum), _midSpectrum) * Constants.FREQUENCY_FORCE_FACTOR;
                    acceleration += frequencyForce;
                    Vector2 currentVelocity = new Vector2(star.VelocityX, star.VelocityY);
                    Vector2 targetVelocity = currentVelocity + acceleration;
                    float lifetimeRatio = newLifetime / star.MaxLifetime;
                    float dampingFactor = Constants.STAR_DAMPING;
                    if (lifetimeRatio < 0.3f)
                        dampingFactor *= (0.8f + lifetimeRatio * 0.2f);
                    targetVelocity *= dampingFactor;
                    float maxSpeed = Constants.MAX_SPEED * (0.5f + _spectrumEnergy * 0.8f);
                    float speedSq = Vector2.Dot(targetVelocity, targetVelocity);
                    if (speedSq > maxSpeed * maxSpeed)
                        targetVelocity = Vector2.Normalize(targetVelocity) * maxSpeed;
                    float velocityLerpFactor = Constants.VELOCITY_LERP * (1.0f + _spectrumEnergy * 0.5f);
                    Vector2 newVelocity = currentVelocity + (targetVelocity - currentVelocity) * velocityLerpFactor;
                    float newX = star.X + newVelocity.X, newY = star.Y + newVelocity.Y;
                    if (newX < -Constants.EDGE_REPULSION_DISTANCE) newX = screenWidth + 10;
                    else if (newX > screenWidth + Constants.EDGE_REPULSION_DISTANCE) newX = -10;
                    if (newY < -Constants.EDGE_REPULSION_DISTANCE) newY = screenHeight + 10;
                    else if (newY > screenHeight + Constants.EDGE_REPULSION_DISTANCE) newY = -10;
                    float speed = newVelocity.Length();
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
                if (_random.NextDouble() < Constants.DIRECTION_CHANGE_CHANCE * (1 + _spectrumEnergy * 3) && _stars.Length > 0)
                {
                    int randomIndex = _random.Next(_stars.Length);
                    if (randomIndex < _stars.Length && _stars[randomIndex].IsActive)
                    {
                        float currentAngle = MathF.Atan2(_stars[randomIndex].VelocityY, _stars[randomIndex].VelocityX);
                        float angleChange = (float)(_random.NextDouble() * 0.5 - 0.25) * MathF.PI;
                        float newAngle = currentAngle + angleChange;
                        float magnitude = Constants.BASE_STAR_VELOCITY * (0.8f + _spectrumEnergy * 1.2f) * (0.8f + _stars[randomIndex].Mass * 0.4f);
                        float newVelX = MathF.Cos(newAngle) * magnitude;
                        float newVelY = MathF.Sin(newAngle) * magnitude;
                        _stars[randomIndex] = _stars[randomIndex] with { VelocityX = newVelX, VelocityY = newVelY };
                    }
                }
            }
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.UpdateStars",
            ErrorMessage = "Error updating stars"
        });

        private void CalculateEdgeRepulsion(in Star star, float screenWidth, float screenHeight, ref float forceX, ref float forceY)
        {
            float edgeDistX = Math.Min(star.X, screenWidth - star.X);
            float edgeDistY = Math.Min(star.Y, screenHeight - star.Y);
            float normalizedEdgeDistX = Clamp(edgeDistX / Constants.EDGE_REPULSION_DISTANCE, 0, 1);
            float normalizedEdgeDistY = Clamp(edgeDistY / Constants.EDGE_REPULSION_DISTANCE, 0, 1);
            float edgeForceX = Constants.EDGE_REPULSION_FORCE * (1 - MathF.Pow(normalizedEdgeDistX, Constants.REPULSION_CURVE_POWER)) * MathF.Sign(screenWidth / 2 - star.X);
            float edgeForceY = Constants.EDGE_REPULSION_FORCE * (1 - MathF.Pow(normalizedEdgeDistY, Constants.REPULSION_CURVE_POWER)) * MathF.Sign(screenHeight / 2 - star.Y);
            forceX += edgeForceX;
            forceY += edgeForceY;
        }

        private void SpawnNewStars(float screenWidth, float screenHeight) => SmartLogger.Safe(() =>
        {
            if (_stars == null || _midSpectrum < Constants.SPAWN_THRESHOLD) return;
            int starsToSpawn = (int)_spawnAccumulator;
            _spawnAccumulator -= starsToSpawn;
            lock (_starsLock)
            {
                for (int i = 0; i < starsToSpawn; i++)
                {
                    int availableSlot = -1;
                    for (int j = 0; j < _stars.Length; j++)
                    {
                        if (!_stars[j].IsActive || _stars[j].Lifetime <= 0)
                        {
                            availableSlot = j;
                            break;
                        }
                    }
                    if (availableSlot == -1) continue;
                    float x = _random.Next(0, (int)screenWidth);
                    float y = _random.Next(0, (int)screenHeight);
                    float starSize = Constants.MIN_STAR_SIZE + (Constants.MAX_STAR_SIZE - Constants.MIN_STAR_SIZE) * (float)_random.NextDouble();
                    float lifetime = Constants.MIN_STAR_LIFETIME + (Constants.MAX_STAR_LIFETIME - Constants.MIN_STAR_LIFETIME) * (float)_random.NextDouble();
                    float brightness = Constants.DEFAULT_BRIGHTNESS + (float)_random.NextDouble() * Constants.BRIGHTNESS_VARIATION;

                    byte r = (byte)(_random.Next(Constants.MIN_STAR_COLOR_VALUE, 255));
                    byte g = (byte)(_random.Next(Constants.MIN_STAR_COLOR_VALUE, 255));
                    byte b = (byte)(_random.Next(Constants.MIN_STAR_COLOR_VALUE, 255));
                    var color = new SKColor(r, g, b);

                    float initialSpeedMultiplier = 0.5f + _midSpectrum * 1.5f;
                    float baseAngle = (float)(_random.NextDouble() * MathF.PI * 2);
                    float angleOffset = (_highSpectrum - _lowSpectrum) * 0.5f;
                    float angle = baseAngle + angleOffset;
                    float vx = MathF.Cos(angle) * Constants.BASE_STAR_VELOCITY * initialSpeedMultiplier;
                    float vy = MathF.Sin(angle) * Constants.BASE_STAR_VELOCITY * initialSpeedMultiplier;
                    _stars[availableSlot] = new Star
                    {
                        X = x,
                        Y = y,
                        Size = starSize,
                        Brightness = brightness,
                        TwinkleFactor = (float)_random.NextDouble() * MathF.PI * 2,
                        TwinkleSpeed = Constants.BASE_TWINKLE_SPEED + (float)_random.NextDouble() * Constants.MAX_TWINKLE_SPEED_VARIATION,
                        Color = color,
                        IsActive = true,
                        VelocityX = vx,
                        VelocityY = vy,
                        Mass = starSize * 0.5f + 0.5f,
                        Speed = MathF.Sqrt(vx * vx + vy * vy),
                        Lifetime = lifetime,
                        MaxLifetime = lifetime,
                        Opacity = 0.01f
                    };
                }
            }
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.SpawnNewStars",
            ErrorMessage = "Error spawning new stars"
        });
        #endregion

        #region Spectrum Processing
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private void ProcessSpectrum(float[] spectrum) => SmartLogger.Safe(() =>
        {
        if (spectrum == null || spectrum.Length < 3) return;
        int totalLength = spectrum.Length, bandLength = totalLength / 3;
        float lowSum = 0, midSum = 0, highSum = 0;
        for (int i = 0; i < bandLength; i++) lowSum += spectrum[i];
        for (int i = bandLength; i < 2 * bandLength; i++) midSum += spectrum[i];
        for (int i = 2 * bandLength; i < totalLength; i++) highSum += spectrum[i];
        float avgLow = lowSum / bandLength, avgMid = midSum / bandLength, avgHigh = highSum / (totalLength - 2 * bandLength);
        _lowSpectrum = _lowSpectrum + (avgLow - _lowSpectrum) * Constants.SMOOTHING_FACTOR;
            _midSpectrum = _midSpectrum + (avgMid - _midSpectrum) * Constants.SMOOTHING_FACTOR;
            _highSpectrum = _highSpectrum + (avgHigh - _highSpectrum) * Constants.SMOOTHING_FACTOR;
            _processedSpectrum = Math.Min(_midSpectrum * Constants.SPECTRUM_AMPLIFICATION, Constants.MAX_BRIGHTNESS);
            _spectrumEnergy = _processedSpectrum;
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.ProcessSpectrum",
            ErrorMessage = "Error processing spectrum data"
        });
        #endregion

        #region Helper Methods
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
        #endregion

        #region Star Initialization
        private void InitializeStars(int count) => SmartLogger.Safe(() =>
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ConstellationRenderer));
            lock (_starsLock)
            {
                _stars = new Star[count];
                for (int i = 0; i < count; i++)
                {
                    byte r = (byte)(_random.Next(Constants.MIN_STAR_COLOR_VALUE, 255));
                    byte g = (byte)(_random.Next(Constants.MIN_STAR_COLOR_VALUE, 255));
                    byte b = (byte)(_random.Next(Constants.MIN_STAR_COLOR_VALUE, 255));

                    _stars[i] = new Star
                    {
                        X = _random.Next(Constants.MIN_STAR_X, Constants.MAX_STAR_X),
                        Y = _random.Next(Constants.MIN_STAR_Y, Constants.MAX_STAR_Y),
                        Size = Constants.MIN_STAR_SIZE + (float)_random.NextDouble() * (Constants.MAX_STAR_SIZE - Constants.MIN_STAR_SIZE),
                        Brightness = Constants.DEFAULT_BRIGHTNESS + (float)_random.NextDouble() * Constants.BRIGHTNESS_VARIATION,
                        TwinkleFactor = (float)_random.NextDouble() * MathF.PI * 2,
                        TwinkleSpeed = Constants.BASE_TWINKLE_SPEED + (float)_random.NextDouble() * Constants.MAX_TWINKLE_SPEED_VARIATION,
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
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.InitializeStars",
            ErrorMessage = "Failed to initialize stars"
        });
        #endregion

        #region Disposal
        public override void Dispose()
        {
            if (!_disposed)
            {
                SmartLogger.Safe(() =>
                {
                    _updateTokenSource.Cancel();
                    try { _updateTask?.Wait(500); } catch { }
                    _updateTokenSource.Dispose();
                    _updateTask?.Dispose();
                    _starPaint?.Dispose();
                    _glowPaint?.Dispose();
                    _renderSurface?.Dispose();
                    _glowShader?.Dispose();
                    _starShader?.Dispose();

                    _starPaint = null;
                    _glowPaint = null;
                    _renderSurface = null;
                    _stars = null;

                    base.Dispose();
                }, new SmartLogger.ErrorHandlingOptions
                {
                    Source = $"{Constants.LOG_PREFIX}.Dispose",
                    ErrorMessage = "Error during disposal"
                });
            }
        }
        #endregion

        #region Data Structures
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