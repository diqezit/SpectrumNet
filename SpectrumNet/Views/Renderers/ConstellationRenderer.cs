#nullable enable

using static SkiaSharp.SKBlendMode;
using static SpectrumNet.Views.Renderers.ConstellationRenderer.Constants;
using static System.MathF;

namespace SpectrumNet.Views.Renderers;

public sealed class ConstellationRenderer : EffectSpectrumRenderer
{
    private static readonly Lazy<ConstellationRenderer> _instance = new(() => new ConstellationRenderer());

    private static readonly SKColor[] _starGlowColors = [new(255, 255, 255, 128), SKColors.Transparent];
    private static readonly float[] _starGlowPositions = [0.0f, 1.0f];

    private readonly Random _random = new();
    private readonly object _starsLock = new();

    private Star[]? _stars;
    private SKPaint? _starPaint;
    private SKPaint? _glowPaint;
    private SKShader? _glowShader;
    private SKSurface? _renderSurface;
    private SKImageInfo _lastRenderInfo;
    private int _starCount;

    private float _lowSpectrum;
    private float _midSpectrum;
    private float _highSpectrum;
    private new float _processedSpectrum;
    private float _spectrumEnergy;
    private float _spawnAccumulator;
    private float _lastSpectrumUpdateTime;
    private float _timeSinceLastSpectrum;
    private bool _hasActiveSpectrum;

    private CancellationTokenSource _updateTokenSource = new();
    private Task? _updateTask;
    private volatile bool _needsUpdate;

    private bool _useGlowEffects = true;

    private ConstellationRenderer() { }

    public static ConstellationRenderer GetInstance() => _instance.Value;

    public record Constants
    {
        public const string LOG_PREFIX = "ConstellationRenderer";

        public const int
            DEFAULT_STAR_COUNT = 120,
            OVERLAY_STAR_COUNT = 80,
            MIN_STAR_X = 100,
            MAX_STAR_X = 900,
            MIN_STAR_Y = 100,
            MAX_STAR_Y = 700;

        public const float
            MIN_STAR_SIZE = 3.0f,
            MAX_STAR_SIZE = 9.0f,
            DEFAULT_BRIGHTNESS = 0.65f,
            BRIGHTNESS_VARIATION = 0.35f,
            MIN_BRIGHTNESS = 0.38f,
            MAX_BRIGHTNESS = 1.0f,
            TWINKLE_SPEED = 1.4f,
            TWINKLE_INFLUENCE = 0.22f,
            SPECTRUM_INFLUENCE = 0.5f,
            SPECTRUM_AMPLIFICATION = 3.0f,
            SPECTRUM_SIZE_INFLUENCE = 0.4f,
            DIRECTION_CHANGE_CHANCE = 0.008f,
            BASE_TWINKLE_SPEED = 0.6f,
            MAX_TWINKLE_SPEED_VARIATION = 1.0f;

        public const byte
            MIN_STAR_COLOR_VALUE = 130,
            MAX_STAR_COLOR_VARIATION = 50,
            BASE_STAR_COLOR = 210;

        public const int UPDATE_INTERVAL = 16;

        public const float
            GLOW_RADIUS = 3.0f,
            FADE_IN_SPEED = 2.2f,
            FADE_OUT_SPEED = 1.5f,
            UNIT_RADIUS = 1.0f;

        public const float
            TIME_STEP = 0.016f,
            BASE_STAR_VELOCITY = 2.5f,
            STAR_ACCELERATION = 0.25f,
            STAR_DAMPING = 0.95f,
            MAX_SPEED = 12.0f,
            VELOCITY_LERP = 0.08f,
            EDGE_REPULSION_DISTANCE = 120.0f,
            EDGE_REPULSION_FORCE = 0.45f,
            REPULSION_CURVE_POWER = 2.5f,
            MIN_STAR_LIFETIME = 5.0f,
            MAX_STAR_LIFETIME = 15.0f,
            SPAWN_THRESHOLD = 0.1f,
            MAX_SPAWN_RATE = 5.0f,
            SPECTRUM_TIMEOUT = 0.3f,
            FORCE_CLEAR_TIMEOUT = 2.0f,
            FREQUENCY_FORCE_FACTOR = 0.5f;

        public const float SMOOTHING_FACTOR = 0.15f;

        public static class Quality
        {
            public const bool
                LOW_USE_GLOW_EFFECTS = false,
                MEDIUM_USE_GLOW_EFFECTS = true,
                HIGH_USE_GLOW_EFFECTS = true;
        }
    }

    public override void Initialize()
    {
        Safe(
            () =>
            {
                base.Initialize();
                InitializeResources();
                ApplyQualitySettings();
                Log(
                    LogLevel.Debug,
                    LOG_PREFIX,
                    "Initialized"
                );
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.Initialize",
                ErrorMessage = "Failed to initialize renderer"
            }
        );
    }

    private void InitializeResources()
    {
        Safe(
            () =>
            {
                _starCount = _isOverlayActive
                    ? OVERLAY_STAR_COUNT
                    : DEFAULT_STAR_COUNT;

                InitializeStars(_starCount);
                InitializeShadersAndPaints();
                StartUpdateLoop();
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.InitializeResources",
                ErrorMessage = "Failed to initialize resources"
            }
        );
    }

    private void InitializeShadersAndPaints()
    {
        Safe(
            () =>
            {
                _starPaint = CreateBasicPaint(SKColors.White);

                _glowShader = SKShader.CreateRadialGradient(
                    new SKPoint(0, 0),
                    UNIT_RADIUS,
                    _starGlowColors,
                    _starGlowPositions,
                    SKShaderTileMode.Clamp
                );

                _glowPaint = CreateGlowPaint(
                    SKColors.White,
                    GLOW_RADIUS,
                    128
                );

                if (_glowPaint != null && _glowShader != null)
                {
                    _glowPaint.BlendMode = Plus;
                    _glowPaint.Shader = _glowShader;
                }
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.InitializeShadersAndPaints",
                ErrorMessage = "Failed to initialize shaders and paints"
            }
        );
    }

    private static SKPaint CreateBasicPaint(SKColor color)
    {
        return new SKPaint
        {
            Color = color,
            IsAntialias = true,
            Style = Fill
        };
    }

    public override void Configure(
        bool isOverlayActive,
        RenderQuality quality = RenderQuality.Medium)
    {
        Safe(
            () =>
            {
                bool configChanged = _isOverlayActive != isOverlayActive || Quality != quality;

                base.Configure(
                    isOverlayActive,
                    quality
                );

                if (configChanged)
                {
                    OnConfigurationChanged();
                }
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.Configure",
                ErrorMessage = "Failed to configure renderer"
            }
        );
    }

    protected override void OnConfigurationChanged()
    {
        Safe(
            () =>
            {
                _starCount = _isOverlayActive
                    ? OVERLAY_STAR_COUNT
                    : DEFAULT_STAR_COUNT;

                InitializeStars(_starCount);
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.OnConfigurationChanged",
                ErrorMessage = "Failed to handle configuration change"
            }
        );
    }

    protected override void ApplyQualitySettings()
    {
        Safe(
            () =>
            {
                base.ApplyQualitySettings();

                _useGlowEffects = Quality switch
                {
                    RenderQuality.Low => Constants.Quality.LOW_USE_GLOW_EFFECTS,
                    RenderQuality.Medium => Constants.Quality.MEDIUM_USE_GLOW_EFFECTS,
                    RenderQuality.High => Constants.Quality.HIGH_USE_GLOW_EFFECTS,
                    _ => true
                };

                UpdatePaintQualitySettings();
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.ApplyQualitySettings",
                ErrorMessage = "Failed to apply quality settings"
            }
        );
    }

    private void UpdatePaintQualitySettings()
    {
        if (_starPaint != null)
        {
            _starPaint.IsAntialias = UseAntiAlias;
        }

        if (_glowPaint != null)
        {
            _glowPaint.IsAntialias = UseAntiAlias;
        }
    }

    protected override void RenderEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint _)
    {
        if (!ValidateRenderParameters(
            canvas,
            spectrum,
            info))
        {
            return;
        }

        if (IsCanvasAreaOutsideView(canvas, info))
        {
            return;
        }

        Safe(
            () =>
            {
                ProcessSpectrumAndUpdateState(
                    spectrum,
                    info
                );

                RenderStarField(
                    canvas,
                    info
                );
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.RenderEffect",
                ErrorMessage = "Error during rendering"
            }
        );
    }

    private static bool IsCanvasAreaOutsideView(SKCanvas canvas, SKImageInfo info)
    {
        SKRect renderBounds = new(0, 0, info.Width, info.Height);
        return canvas.QuickReject(renderBounds);
    }

    private void ProcessSpectrumAndUpdateState(
        float[] spectrum,
        SKImageInfo info)
    {
        bool semaphoreAcquired = false;
        try
        {
            semaphoreAcquired = _spectrumSemaphore.Wait(0);
            if (semaphoreAcquired)
            {
                UpdateTimeSinceLastSpectrum();
                HandleSpectrumData(spectrum);
                UpdateRenderSurfaceIfNeeded(info);
                FlagForUpdate();
                CheckForClearingStars();
            }
        }
        finally
        {
            if (semaphoreAcquired)
                _spectrumSemaphore.Release();
        }
    }

    private void UpdateTimeSinceLastSpectrum()
    {
        _timeSinceLastSpectrum += TIME_STEP;
    }

    private void HandleSpectrumData(float[] spectrum)
    {
        bool hasNewSpectrumData = spectrum.Length >= 2;
        if (hasNewSpectrumData)
        {
            ProcessSpectrum(spectrum);
            _hasActiveSpectrum = true;
            _lastSpectrumUpdateTime = _time;
            _timeSinceLastSpectrum = 0;
        }
        else
        {
            HandleNoSpectrumData();
        }
    }

    private void FlagForUpdate()
    {
        _needsUpdate = true;
    }

    private void CheckForClearingStars()
    {
        if (_timeSinceLastSpectrum > FORCE_CLEAR_TIMEOUT)
        {
            ClearAllStars();
        }
    }

    private void HandleNoSpectrumData()
    {
        _spectrumEnergy = 0;
        if (_time - _lastSpectrumUpdateTime > SPECTRUM_TIMEOUT)
        {
            _hasActiveSpectrum = false;
            _processedSpectrum = 0;
        }
    }

    private void UpdateRenderSurfaceIfNeeded(SKImageInfo info)
    {
        if (info.Width != _lastRenderInfo.Width ||
            info.Height != _lastRenderInfo.Height ||
            _renderSurface == null)
        {
            UpdateRenderSurface(info);
        }
    }

    private bool ValidateRenderParameters(
        SKCanvas? canvas,
        float[]? spectrum,
        SKImageInfo info)
    {
        if (!_isInitialized)
        {
            Log(
                LogLevel.Error,
                LOG_PREFIX,
                "Renderer is not initialized"
            );
            return false;
        }

        if (canvas == null)
        {
            Log(
                LogLevel.Warning,
                LOG_PREFIX,
                "Canvas is null"
            );
            return false;
        }

        if (info.Width <= 0 || info.Height <= 0)
        {
            Log(
                LogLevel.Warning,
                LOG_PREFIX,
                "Invalid canvas dimensions"
            );
            return false;
        }

        if (_disposed)
        {
            Log(
                LogLevel.Warning,
                LOG_PREFIX,
                "Renderer is disposed"
            );
            return false;
        }

        return true;
    }

    private void ClearAllStars()
    {
        Safe(
            () =>
            {
                if (_stars == null) return;

                lock (_starsLock)
                {
                    for (int i = 0; i < _stars.Length; i++)
                    {
                        _stars[i] = _stars[i] with
                        {
                            IsActive = false,
                            Opacity = 0
                        };
                    }
                }
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.ClearAllStars",
                ErrorMessage = "Failed to clear stars"
            }
        );
    }

    private void UpdateRenderSurface(SKImageInfo info)
    {
        Safe(
            () =>
            {
                _lastRenderInfo = info;
                _renderSurface?.Dispose();
                _renderSurface = SKSurface.Create(info);
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.UpdateRenderSurface",
                ErrorMessage = "Failed to update render surface"
            }
        );
    }

    private void RenderStarField(SKCanvas canvas, SKImageInfo info)
    {
        Safe(
            () =>
            {
                if (!AreRenderResourcesValid())
                {
                    return;
                }

                PrepareRenderSurface();
                RenderStarsToSurface(info);
                DrawFinalComposite(canvas);
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.RenderStarField",
                ErrorMessage = "Error rendering star field"
            }
        );
    }

    private bool AreRenderResourcesValid()
    {
        return _stars != null &&
               _starPaint != null &&
               _glowPaint != null &&
               _renderSurface != null;
    }

    private void PrepareRenderSurface()
    {
        _renderSurface?.Canvas.Clear(SKColors.Transparent);
    }

    private void RenderStarsToSurface(SKImageInfo info)
    {
        if (_renderSurface == null) return;

        lock (_starsLock)
        {
            RenderActiveStars(
                _renderSurface.Canvas,
                info
            );
        }
    }

    private void DrawFinalComposite(SKCanvas canvas)
    {
        if (_renderSurface == null) return;

        using SKImage finalSnapshot = _renderSurface.Snapshot();
        canvas.DrawImage(finalSnapshot, 0, 0);
    }

    private void RenderActiveStars(
        SKCanvas renderCanvas,
        SKImageInfo info)
    {
        if (_stars == null || _starPaint == null || _glowPaint == null)
            return;

        foreach (var star in _stars)
        {
            if (!ShouldRenderStar(star, info))
            {
                continue;
            }

            (float finalOpacity, byte alpha) = CalculateStarOpacity(star);

            if (finalOpacity < 0.01f)
            {
                continue;
            }

            float dynamicSize = CalculateStarSize(star);

            if (ShouldRenderGlowEffect(star))
            {
                RenderStarGlow(
                    renderCanvas,
                    star,
                    dynamicSize,
                    alpha
                );
            }

            RenderStarCore(
                renderCanvas,
                star,
                dynamicSize,
                alpha
            );
        }
    }

    private static bool ShouldRenderStar(Star star, SKImageInfo info)
    {
        if (!star.IsActive || star.Lifetime <= 0 || star.Opacity <= 0.01f)
        {
            return false;
        }

        return !IsStarOutsideVisibleArea(star, info);
    }

    private static bool IsStarOutsideVisibleArea(Star star, SKImageInfo info)
    {
        return star.X < -10 ||
               star.X > info.Width + 10 ||
               star.Y < -10 ||
               star.Y > info.Height + 10;
    }

    private static (float finalOpacity, byte alpha) CalculateStarOpacity(Star star)
    {
        float lifetimeRatio = star.Lifetime / star.MaxLifetime;
        float fadeEffect = CalculateFadeEffect(lifetimeRatio);
        float finalOpacity = star.Opacity * fadeEffect;

        byte alpha = (byte)ClampInt(
            (int)(255 * star.Brightness * finalOpacity),
            0,
            255
        );

        return (finalOpacity, alpha);
    }

    private static float CalculateFadeEffect(float lifetimeRatio)
    {
        return lifetimeRatio < 0.2f ? lifetimeRatio / 0.2f : 1.0f;
    }

    private static float CalculateStarSize(Star star)
    {
        float lifetimeRatio = star.Lifetime / star.MaxLifetime;
        return star.Size * (0.7f + lifetimeRatio * 0.3f);
    }

    private bool ShouldRenderGlowEffect(Star star)
    {
        return _useGlowEffects &&
               (star.Brightness > DEFAULT_BRIGHTNESS || _spectrumEnergy > 0.45f);
    }

    private void RenderStarGlow(
        SKCanvas canvas,
        Star star,
        float dynamicSize,
        byte alpha)
    {
        if (_glowPaint == null)
            return;

        float finalOpacity = star.Opacity * (star.Lifetime / star.MaxLifetime);
        float glowSize = CalculateGlowSize(dynamicSize, finalOpacity);
        byte glowAlpha = (byte)(alpha * 0.6f);

        _glowPaint.Color = star.Color.WithAlpha(glowAlpha);

        canvas.Save();
        canvas.Translate(star.X, star.Y);
        canvas.Scale(glowSize, glowSize);
        canvas.DrawCircle(0, 0, UNIT_RADIUS, _glowPaint);
        canvas.Restore();
    }

    private float CalculateGlowSize(float dynamicSize, float finalOpacity)
    {
        return dynamicSize * (2.2f + _spectrumEnergy * 1.2f) * finalOpacity;
    }

    private void RenderStarCore(
        SKCanvas canvas,
        Star star,
        float dynamicSize,
        byte alpha)
    {
        if (_starPaint == null)
            return;

        _starPaint.Color = star.Color.WithAlpha(alpha);
        canvas.Save();
        canvas.Translate(star.X, star.Y);
        canvas.Scale(dynamicSize, dynamicSize);
        canvas.DrawCircle(0, 0, UNIT_RADIUS, _starPaint);
        canvas.Restore();
    }

    private void StartUpdateLoop()
    {
        Safe(
            () =>
            {
                StopExistingUpdateTask();
                RecreateTokenSource();
                StartNewUpdateTask();
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.StartUpdateLoop",
                ErrorMessage = "Failed to start update loop"
            }
        );
    }

    private void StopExistingUpdateTask()
    {
        if (_updateTokenSource != null && !_updateTokenSource.IsCancellationRequested)
        {
            _updateTokenSource.Cancel();
        }

        if (_updateTask != null)
        {
            try
            {
                _updateTask.Wait(500);
            }
            catch
            {
                // Ignore cancellation exceptions
            }
        }
    }

    private void RecreateTokenSource()
    {
        if (_updateTokenSource != null)
        {
            _updateTokenSource.Dispose();
            _updateTokenSource = new CancellationTokenSource();
        }
    }

    private void StartNewUpdateTask()
    {
        _updateTask = Task.Run(
            UpdateLoopAsync,
            _updateTokenSource.Token
        );
    }

    private async Task UpdateLoopAsync()
    {
        float accumulatedTime = 0;

        while (!_updateTokenSource.IsCancellationRequested)
        {
            if (_needsUpdate && _isInitialized)
            {
                UpdateSimulationTime(ref accumulatedTime);

                if (ShouldUpdateStars(accumulatedTime))
                {
                    UpdateStarsBasedOnState();
                    accumulatedTime -= TIME_STEP;
                }

                _needsUpdate = false;
            }

            await Task.Delay(
                UPDATE_INTERVAL,
                _updateTokenSource.Token
            );
        }
    }

    private void UpdateSimulationTime(ref float accumulatedTime)
    {
        _time += TIME_STEP;
        accumulatedTime += TIME_STEP;
    }

    private bool ShouldUpdateStars(float accumulatedTime)
    {
        return accumulatedTime >= TIME_STEP &&
               _stars != null &&
               _lastRenderInfo.Width > 0;
    }

    private void UpdateStarsBasedOnState()
    {
        if (_hasActiveSpectrum)
        {
            UpdateStars(_lastRenderInfo);
        }
        else
        {
            FadeOutStars();
        }
    }

    private void FadeOutStars()
    {
        Safe(
            () =>
            {
                if (_stars == null) return;

                lock (_starsLock)
                {
                    for (int i = 0; i < _stars.Length; i++)
                    {
                        if (!_stars[i].IsActive) continue;

                        float newOpacity = CalculateReducedOpacity(_stars[i]);
                        UpdateStarDuringFadeOut(i, newOpacity);
                    }
                }
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.FadeOutStars",
                ErrorMessage = "Error fading out stars"
            }
        );
    }

    private static float CalculateReducedOpacity(Star star)
    {
        return MathF.Max(
            star.Opacity - TIME_STEP * FADE_OUT_SPEED,
            0
        );
    }

    private void UpdateStarDuringFadeOut(int starIndex, float newOpacity)
    {
        if (_stars == null) return;

        if (newOpacity <= 0.01f)
        {
            DeactivateStar(starIndex);
        }
        else
        {
            float newLifetime = MathF.Max(
                _stars[starIndex].Lifetime - TIME_STEP * 2,
                0
            );

            if (newLifetime <= 0)
            {
                DeactivateStar(starIndex);
            }
            else
            {
                SlowDownStar(starIndex, newOpacity, newLifetime);
            }
        }
    }

    private void DeactivateStar(int starIndex)
    {
        if (_stars == null) return;

        _stars[starIndex] = _stars[starIndex] with
        {
            IsActive = false,
            Lifetime = 0,
            Opacity = 0
        };
    }

    private void SlowDownStar(int starIndex, float newOpacity, float newLifetime)
    {
        if (_stars == null) return;

        _stars[starIndex] = _stars[starIndex] with
        {
            Opacity = newOpacity,
            Lifetime = newLifetime,
            VelocityX = _stars[starIndex].VelocityX * 0.9f,
            VelocityY = _stars[starIndex].VelocityY * 0.9f
        };
    }

    private void UpdateStars(SKImageInfo info)
    {
        Safe(
            () =>
            {
                if (_stars == null) return;

                float screenWidth = info.Width;
                float screenHeight = info.Height;

                HandleStarSpawning(screenWidth, screenHeight);

                lock (_starsLock)
                {
                    UpdateAllStars(screenWidth, screenHeight);
                    ApplyRandomDirectionChanges();
                }
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.UpdateStars",
                ErrorMessage = "Error updating stars"
            }
        );
    }

    private void HandleStarSpawning(float screenWidth, float screenHeight)
    {
        if (_hasActiveSpectrum && _midSpectrum >= SPAWN_THRESHOLD)
        {
            _spawnAccumulator += _midSpectrum * TIME_STEP * MAX_SPAWN_RATE;
            SpawnNewStars(screenWidth, screenHeight);
        }
    }

    private void UpdateAllStars(float screenWidth, float screenHeight)
    {
        if (_stars == null) return;

        for (int i = 0; i < _stars.Length; i++)
        {
            if (!_stars[i].IsActive) continue;

            float newLifetime = _stars[i].Lifetime - TIME_STEP;
            if (newLifetime <= 0)
            {
                DeactivateStar(i);
                continue;
            }

            _stars[i] = CalculateUpdatedStar(
                _stars[i],
                newLifetime,
                screenWidth,
                screenHeight
            );
        }
    }

    private Star CalculateUpdatedStar(
        Star star,
        float newLifetime,
        float screenWidth,
        float screenHeight)
    {
        float newOpacity = CalculateFadeInOpacity(star);
        float targetBrightness = CalculateTargetBrightness(star);

        (Vector2 newVelocity, float speed) = CalculateStarVelocity(
            star,
            newLifetime,
            screenWidth,
            screenHeight
        );

        (float newX, float newY) = CalculateNewPosition(
            star,
            newVelocity,
            screenWidth,
            screenHeight
        );

        return CreateUpdatedStar(
            star,
            newX,
            newY,
            newOpacity,
            targetBrightness,
            newLifetime,
            newVelocity,
            speed
        );
    }

    private static float CalculateFadeInOpacity(Star star)
    {
        return MathF.Min(
            star.Opacity + TIME_STEP * FADE_IN_SPEED,
            1.0f
        );
    }

    private float CalculateTargetBrightness(Star star)
    {
        float twinkling = Sin(
            _time * TWINKLE_SPEED * star.TwinkleSpeed + star.TwinkleFactor
        );

        return ClampF(
            DEFAULT_BRIGHTNESS +
            twinkling * TWINKLE_INFLUENCE +
            _spectrumEnergy * SPECTRUM_INFLUENCE,
            MIN_BRIGHTNESS,
            MAX_BRIGHTNESS
        );
    }

    private Star CreateUpdatedStar(
        Star original,
        float newX,
        float newY,
        float newOpacity,
        float targetBrightness,
        float newLifetime,
        Vector2 newVelocity,
        float speed)
    {
        return new Star
        {
            X = newX,
            Y = newY,
            Size = original.Size,
            Brightness = original.Brightness + (targetBrightness - original.Brightness) *
                        (0.08f + _spectrumEnergy * 0.15f),
            TwinkleFactor = original.TwinkleFactor,
            TwinkleSpeed = original.TwinkleSpeed,
            Color = original.Color,
            IsActive = true,
            VelocityX = newVelocity.X,
            VelocityY = newVelocity.Y,
            Mass = original.Mass,
            Speed = speed,
            Lifetime = newLifetime,
            MaxLifetime = original.MaxLifetime,
            Opacity = newOpacity
        };
    }

    private void ApplyRandomDirectionChanges()
    {
        if (_stars == null || _stars.Length == 0) return;

        float directionChangeProbability = DIRECTION_CHANGE_CHANCE * (1f + _spectrumEnergy * 3f);
        if ((float)_random.NextDouble() < directionChangeProbability)
        {
            int randomIndex = _random.Next(_stars.Length);
            if (randomIndex < _stars.Length && _stars[randomIndex].IsActive)
            {
                ChangeStarDirection(randomIndex);
            }
        }
    }

    private void ChangeStarDirection(int starIndex)
    {
        if (_stars == null) return;

        float currentAngle = Atan2(
            _stars[starIndex].VelocityY,
            _stars[starIndex].VelocityX
        );

        float angleChange = (float)(_random.NextDouble() * 0.5f - 0.25f) * MathF.PI;
        float newAngle = currentAngle + angleChange;
        float magnitude = CalculateDirectionChangeMagnitude(starIndex);

        float newVelX = Cos(newAngle) * magnitude;
        float newVelY = Sin(newAngle) * magnitude;

        _stars[starIndex] = _stars[starIndex] with
        {
            VelocityX = newVelX,
            VelocityY = newVelY
        };
    }

    private float CalculateDirectionChangeMagnitude(int starIndex)
    {
        if (_stars == null) return BASE_STAR_VELOCITY;

        return BASE_STAR_VELOCITY *
               (0.8f + _spectrumEnergy * 1.2f) *
               (0.8f + _stars[starIndex].Mass * 0.4f);
    }

    private (Vector2 velocity, float speed) CalculateStarVelocity(
        Star star,
        float newLifetime,
        float screenWidth,
        float screenHeight)
    {
        float forceX = 0, forceY = 0;
        CalculateEdgeRepulsion(
            star,
            screenWidth,
            screenHeight,
            ref forceX,
            ref forceY
        );

        Vector2 acceleration = CalculateAcceleration(star, forceX, forceY);
        Vector2 targetVelocity = CalculateTargetVelocity(star, acceleration, newLifetime);

        float velocityLerpFactor = VELOCITY_LERP * (1.0f + _spectrumEnergy * 0.5f);
        Vector2 currentVelocity = new(star.VelocityX, star.VelocityY);
        Vector2 newVelocity = currentVelocity +
                             (targetVelocity - currentVelocity) *
                             velocityLerpFactor;

        float speed = newVelocity.Length();

        return (newVelocity, speed);
    }

    private Vector2 CalculateAcceleration(Star star, float forceX, float forceY)
    {
        float inertiaFactor = 1.0f / (0.8f + star.Mass * 0.5f);
        Vector2 baseForce = new(forceX, forceY);
        Vector2 baseAcceleration = baseForce * STAR_ACCELERATION * inertiaFactor;

        float spectrumAcceleration = 1.0f + _spectrumEnergy * 1.5f;
        baseAcceleration = Vector2.Multiply(baseAcceleration, spectrumAcceleration);

        Vector2 frequencyForce = new Vector2(
                   _highSpectrum - _lowSpectrum,
                   _midSpectrum
               ) * FREQUENCY_FORCE_FACTOR;

        return baseAcceleration + frequencyForce;
    }

    private Vector2 CalculateTargetVelocity(
        Star star,
        Vector2 acceleration,
        float newLifetime)
    {
        Vector2 currentVelocity = new(star.VelocityX, star.VelocityY);
        Vector2 targetVelocity = currentVelocity + acceleration;

        float dampingFactor = CalculateDampingFactor(star, newLifetime);
        targetVelocity = Vector2.Multiply(targetVelocity, dampingFactor);

        return LimitMaximumVelocity(targetVelocity);
    }

    private static float CalculateDampingFactor(Star star, float newLifetime)
    {
        float lifetimeRatio = newLifetime / star.MaxLifetime;
        float dampingFactor = STAR_DAMPING;

        if (lifetimeRatio < 0.3f)
        {
            dampingFactor *= 0.8f + lifetimeRatio * 0.2f;
        }

        return dampingFactor;
    }

    private Vector2 LimitMaximumVelocity(Vector2 velocity)
    {
        float maxSpeed = MAX_SPEED * (0.5f + _spectrumEnergy * 0.8f);
        float speedSq = Vector2.Dot(velocity, velocity);

        if (speedSq > maxSpeed * maxSpeed)
        {
            return Vector2.Normalize(velocity) * maxSpeed;
        }

        return velocity;
    }

    private static (float x, float y) CalculateNewPosition(
        Star star,
        Vector2 velocity,
        float screenWidth,
        float screenHeight)
    {
        float newX = star.X + velocity.X;
        float newY = star.Y + velocity.Y;

        (newX, newY) = WrapPositionAroundScreen(
            newX,
            newY,
            screenWidth,
            screenHeight
        );

        return (newX, newY);
    }

    private static (float x, float y) WrapPositionAroundScreen(
        float x,
        float y,
        float width,
        float height)
    {
        if (x < -EDGE_REPULSION_DISTANCE)
            x = width + 10;
        else if (x > width + EDGE_REPULSION_DISTANCE)
            x = -10;

        if (y < -EDGE_REPULSION_DISTANCE)
            y = height + 10;
        else if (y > height + EDGE_REPULSION_DISTANCE)
            y = -10;

        return (x, y);
    }

    private static void CalculateEdgeRepulsion(
        in Star star,
        float screenWidth,
        float screenHeight,
        ref float forceX,
        ref float forceY)
    {
        float edgeDistX = MathF.Min(star.X, screenWidth - star.X);
        float edgeDistY = MathF.Min(star.Y, screenHeight - star.Y);

        float normalizedEdgeDistX = ClampF(
            edgeDistX / EDGE_REPULSION_DISTANCE,
            0,
            1
        );

        float normalizedEdgeDistY = ClampF(
            edgeDistY / EDGE_REPULSION_DISTANCE,
            0,
            1
        );

        float edgeForceX = EDGE_REPULSION_FORCE *
                         (1f - Pow(normalizedEdgeDistX, REPULSION_CURVE_POWER)) *
                         MathF.Sign(screenWidth / 2f - star.X);

        float edgeForceY = EDGE_REPULSION_FORCE *
                         (1f - Pow(normalizedEdgeDistY, REPULSION_CURVE_POWER)) *
                         MathF.Sign(screenHeight / 2f - star.Y);

        forceX += edgeForceX;
        forceY += edgeForceY;
    }

    private void SpawnNewStars(float screenWidth, float screenHeight)
    {
        Safe(
            () =>
            {
                if (_stars == null || _midSpectrum < SPAWN_THRESHOLD)
                    return;

                int starsToSpawn = (int)_spawnAccumulator;
                _spawnAccumulator -= starsToSpawn;

                lock (_starsLock)
                {
                    for (int i = 0; i < starsToSpawn; i++)
                    {
                        int availableSlot = FindAvailableStarSlot();
                        if (availableSlot == -1) continue;

                        _stars[availableSlot] = CreateNewStar(
                            screenWidth,
                            screenHeight
                        );
                    }
                }
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.SpawnNewStars",
                ErrorMessage = "Error spawning new stars"
            }
        );
    }

    private int FindAvailableStarSlot()
    {
        if (_stars == null) return -1;

        for (int j = 0; j < _stars.Length; j++)
        {
            if (!_stars[j].IsActive || _stars[j].Lifetime <= 0)
            {
                return j;
            }
        }
        return -1;
    }

    private Star CreateNewStar(float screenWidth, float screenHeight)
    {
        float x = (float)_random.Next(0, (int)screenWidth);
        float y = (float)_random.Next(0, (int)screenHeight);

        float starSize = MIN_STAR_SIZE +
                        (MAX_STAR_SIZE - MIN_STAR_SIZE) *
                        (float)_random.NextDouble();

        float lifetime = MIN_STAR_LIFETIME +
                        (MAX_STAR_LIFETIME - MIN_STAR_LIFETIME) *
                        (float)_random.NextDouble();

        float brightness = DEFAULT_BRIGHTNESS +
                          (float)_random.NextDouble() * BRIGHTNESS_VARIATION;

        byte r = (byte)_random.Next(MIN_STAR_COLOR_VALUE, 255);
        byte g = (byte)_random.Next(MIN_STAR_COLOR_VALUE, 255);
        byte b = (byte)_random.Next(MIN_STAR_COLOR_VALUE, 255);
        var color = new SKColor(r, g, b);

        (float vx, float vy) = CalculateInitialVelocity();

        return new Star
        {
            X = x,
            Y = y,
            Size = starSize,
            Brightness = brightness,
            TwinkleFactor = (float)_random.NextDouble() * MathF.PI * 2f,
            TwinkleSpeed = BASE_TWINKLE_SPEED +
                          (float)_random.NextDouble() * MAX_TWINKLE_SPEED_VARIATION,
            Color = color,
            IsActive = true,
            VelocityX = vx,
            VelocityY = vy,
            Mass = starSize * 0.5f + 0.5f,
            Speed = Sqrt(vx * vx + vy * vy),
            Lifetime = lifetime,
            MaxLifetime = lifetime,
            Opacity = 0.01f
        };
    }

    private (float vx, float vy) CalculateInitialVelocity()
    {
        float initialSpeedMultiplier = 0.5f + _midSpectrum * 1.5f;
        float baseAngle = (float)_random.NextDouble() * MathF.PI * 2f;
        float angleOffset = (_highSpectrum - _lowSpectrum) * 0.5f;
        float angle = baseAngle + angleOffset;

        float vx = Cos(angle) * BASE_STAR_VELOCITY * initialSpeedMultiplier;
        float vy = Sin(angle) * BASE_STAR_VELOCITY * initialSpeedMultiplier;

        return (vx, vy);
    }

    [MethodImpl(AggressiveOptimization)]
    private void ProcessSpectrum(float[] spectrum)
    {
        Safe(
            () =>
            {
                if (spectrum.Length < 3) return;

                ProcessSpectrumBands(spectrum);
                CalculateSpectrumEnergy();
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.ProcessSpectrum",
                ErrorMessage = "Error processing spectrum data"
            }
        );
    }

    private void CalculateSpectrumEnergy()
    {
        _processedSpectrum = MathF.Min(
            _midSpectrum * SPECTRUM_AMPLIFICATION,
            MAX_BRIGHTNESS
        );

        _spectrumEnergy = _processedSpectrum;
    }

    private void ProcessSpectrumBands(float[] spectrum)
    {
        int totalLength = spectrum.Length;
        int bandLength = totalLength / 3;

        (float avgLow, float avgMid, float avgHigh) = CalculateBandAverages(
            spectrum,
            bandLength,
            totalLength
        );

        _lowSpectrum += (avgLow - _lowSpectrum) * SMOOTHING_FACTOR;
        _midSpectrum += (avgMid - _midSpectrum) * SMOOTHING_FACTOR;
        _highSpectrum += (avgHigh - _highSpectrum) * SMOOTHING_FACTOR;
    }

    private static (float low, float mid, float high) CalculateBandAverages(
        float[] spectrum,
        int bandLength,
        int totalLength)
    {
        (float lowSum, float midSum, float highSum) = CalculateBandSums(
            spectrum,
            bandLength,
            totalLength
        );

        float avgLow = lowSum / bandLength;
        float avgMid = midSum / bandLength;
        float avgHigh = highSum / (totalLength - 2 * bandLength);

        return (avgLow, avgMid, avgHigh);
    }

    private static (float lowSum, float midSum, float highSum) CalculateBandSums(
        float[] spectrum,
        int bandLength,
        int totalLength)
    {
        float lowSum = 0, midSum = 0, highSum = 0;

        for (int i = 0; i < bandLength; i++)
            lowSum += spectrum[i];

        for (int i = bandLength; i < 2 * bandLength; i++)
            midSum += spectrum[i];

        for (int i = 2 * bandLength; i < totalLength; i++)
            highSum += spectrum[i];

        return (lowSum, midSum, highSum);
    }

    private void InitializeStars(int count)
    {
        Safe(
            () =>
            {
                ObjectDisposedException.ThrowIf(_disposed, nameof(ConstellationRenderer));

                lock (_starsLock)
                {
                    _stars = new Star[count];

                    for (int i = 0; i < count; i++)
                    {
                        _stars[i] = CreateInitialStar();
                    }
                }
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.InitializeStars",
                ErrorMessage = "Failed to initialize stars"
            }
        );
    }

    private Star CreateInitialStar()
    {
        byte r = (byte)_random.Next(MIN_STAR_COLOR_VALUE, 255);
        byte g = (byte)_random.Next(MIN_STAR_COLOR_VALUE, 255);
        byte b = (byte)_random.Next(MIN_STAR_COLOR_VALUE, 255);

        return new Star
        {
            X = _random.Next(MIN_STAR_X, MAX_STAR_X),
            Y = _random.Next(MIN_STAR_Y, MAX_STAR_Y),
            Size = MIN_STAR_SIZE +
                  (float)_random.NextDouble() * (MAX_STAR_SIZE - MIN_STAR_SIZE),
            Brightness = DEFAULT_BRIGHTNESS +
                        (float)_random.NextDouble() * BRIGHTNESS_VARIATION,
            TwinkleFactor = (float)_random.NextDouble() * MathF.PI * 2f,
            TwinkleSpeed = BASE_TWINKLE_SPEED +
                          (float)_random.NextDouble() * MAX_TWINKLE_SPEED_VARIATION,
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

    [MethodImpl(AggressiveInlining)]
    private static float ClampF(float value, float min, float max) =>
        value < min ? min : value > max ? max : value;

    [MethodImpl(AggressiveInlining)]
    private static int ClampInt(int value, int min, int max) =>
        value < min ? min : value > max ? max : value;

    protected override void OnDispose()
    {
        Safe(
            () =>
            {
                DisposeUpdateResources();
                DisposeRenderResources();
                ClearStateResources();

                base.OnDispose();

                Log(
                    LogLevel.Debug,
                    LOG_PREFIX,
                    "Disposed"
                );
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.OnDispose",
                ErrorMessage = "Error during disposal"
            }
        );
    }

    private void DisposeUpdateResources()
    {
        if (_updateTokenSource != null)
        {
            _updateTokenSource.Cancel();
            try
            {
                _updateTask?.Wait(500);
            }
            catch
            {
                // Ignore cancellation exceptions
            }
            _updateTokenSource.Dispose();
        }
        _updateTask?.Dispose();
    }

    private void DisposeRenderResources()
    {
        _starPaint?.Dispose();
        _glowPaint?.Dispose();
        _renderSurface?.Dispose();
        _glowShader?.Dispose();
    }

    private void ClearStateResources()
    {
        _starPaint = null;
        _glowPaint = null;
        _renderSurface = null;
        _stars = null;
    }

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
}