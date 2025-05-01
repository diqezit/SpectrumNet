#nullable enable

using SpectrumNet.Service.Enums;

namespace SpectrumNet.Views.Renderers;

/// <summary>
/// Renderer that visualizes spectrum data as animated heart shapes with pulsating effect.
/// </summary>
public sealed class HeartbeatRenderer : BaseSpectrumRenderer
{
    #region Singleton Pattern
    private static readonly Lazy<HeartbeatRenderer> _instance = new(() => new HeartbeatRenderer());
    private HeartbeatRenderer() { } // Приватный конструктор
    public static HeartbeatRenderer GetInstance() => _instance.Value;
    #endregion

    #region Constants
    private static class Constants
    {
        // Logging
        public const string LOG_PREFIX = "HeartbeatRenderer";

        // Rendering thresholds
        public const float MIN_MAGNITUDE_THRESHOLD = 0.05f;    // Minimum threshold for displaying spectrum magnitude
        public const float GLOW_INTENSITY = 0.2f;             // Controls the intensity of the glow effect
        public const float GLOW_ALPHA_DIVISOR = 3f;            // Divisor for alpha channel of glow effect
        public const float ALPHA_MULTIPLIER = 1.5f;           // Multiplier for alpha channel calculation

        // Animation properties
        public const float PULSE_FREQUENCY = 6f;              // Frequency of heart pulse animation
        public const float HEART_BASE_SCALE = 0.6f;            // Base scale factor for hearts
        public const float ANIMATION_TIME_INCREMENT = 0.016f;  // Time increment for animation per frame
        public const float RADIANS_PER_DEGREE = MathF.PI / 180f; // Radians per degree conversion factor

        // Layout configurations
        public static readonly (float Size, float Spacing, int Count) DEFAULT_CONFIG =
            (60f, 15f, 8);                                  // Default configuration for normal mode
        public static readonly (float Size, float Spacing, int Count) OVERLAY_CONFIG =
            (30f, 8f, 12);                                  // Configuration for overlay mode

        // Smoothing factors
        public const float SMOOTHING_FACTOR_NORMAL = 0.3f;     // Smoothing factor for normal mode
        public const float SMOOTHING_FACTOR_OVERLAY = 0.7f;    // Smoothing factor for overlay mode

        // Quality settings
        public static class Quality
        {
            // Low quality settings
            public const int LOW_HEART_SIDES = 8;              // Number of sides for heart shape in low quality
            public const bool LOW_USE_GLOW = false;            // Whether to use glow effect in low quality
            public const float LOW_SIMPLIFICATION_FACTOR = 0.5f; // Simplification factor for path in low quality

            // Medium quality settings
            public const int MEDIUM_HEART_SIDES = 12;          // Number of sides for heart shape in medium quality
            public const bool MEDIUM_USE_GLOW = true;          // Whether to use glow effect in medium quality
            public const float MEDIUM_SIMPLIFICATION_FACTOR = 0.2f; // Simplification factor for path in medium quality

            // High quality settings
            public const int HIGH_HEART_SIDES = 0;             // Number of sides for heart shape in high quality (0 means use cubic path)
            public const bool HIGH_USE_GLOW = true;            // Whether to use glow effect in high quality
            public const float HIGH_SIMPLIFICATION_FACTOR = 0f; // Simplification factor for path in high quality
        }
    }
    #endregion

    #region Fields
    // Object pools for efficient resource management
    private readonly ObjectPool<SKPath> _pathPool = new(() => new SKPath(), path => path.Reset(), 10);
    private readonly ObjectPool<SKPaint> _paintPool = new(() => new SKPaint(), paint => paint.Reset(), 5);

    // Rendering state
    private bool _isOverlayActive;
    private float _globalAnimationTime;
    private float _heartSize, _heartSpacing;
    private int _heartCount;
    private float[]? _cosValues, _sinValues;
    private SKPicture? _cachedHeartPicture;

    // Quality-dependent settings
    private new bool _useAntiAlias = true;
    private new SKSamplingOptions _samplingOptions = new(SKFilterMode.Linear, SKMipmapMode.Linear);
    private new bool _useAdvancedEffects = true;
    private int _heartSides = Constants.Quality.MEDIUM_HEART_SIDES;
    private float _simplificationFactor = Constants.Quality.MEDIUM_SIMPLIFICATION_FACTOR;

    // Spectrum processing
    private int _lastSpectrumLength, _lastTargetCount;
    private float[]? _cachedScaledSpectrum, _cachedSmoothedSpectrum;

    // Synchronization and state
    private readonly object _spectrumLock = new();
    private Task? _spectrumProcessingTask;
    private new bool _disposed;
    #endregion

    #region Initialization and Configuration
    /// <summary>
    /// Initializes the heartbeat renderer and prepares resources for rendering.
    /// </summary>
    public override void Initialize()
    {
        Safe(() =>
        {
            base.Initialize();

            UpdateConfiguration(Constants.DEFAULT_CONFIG);
            ApplyQualitySettings();

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

            bool configChanged = _isOverlayActive != isOverlayActive || _quality != quality;

            // Update overlay mode
            _isOverlayActive = isOverlayActive;
            _smoothingFactor = isOverlayActive ?
                Constants.SMOOTHING_FACTOR_OVERLAY :
                Constants.SMOOTHING_FACTOR_NORMAL;

            // Update configuration based on mode
            UpdateConfiguration(isOverlayActive ? Constants.OVERLAY_CONFIG : Constants.DEFAULT_CONFIG);

            // Update quality if needed
            if (_quality != quality)
            {
                _quality = quality;
                ApplyQualitySettings();
            }

            // If config changed, invalidate cached resources
            if (configChanged)
            {
                InvalidateCachedResources();
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
                    _useAdvancedEffects = Constants.Quality.LOW_USE_GLOW;
                    _heartSides = Constants.Quality.LOW_HEART_SIDES;
                    _simplificationFactor = Constants.Quality.LOW_SIMPLIFICATION_FACTOR;
                    break;

                case RenderQuality.Medium:
                    _useAntiAlias = true;
                    _samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                    _useAdvancedEffects = Constants.Quality.MEDIUM_USE_GLOW;
                    _heartSides = Constants.Quality.MEDIUM_HEART_SIDES;
                    _simplificationFactor = Constants.Quality.MEDIUM_SIMPLIFICATION_FACTOR;
                    break;

                case RenderQuality.High:
                    _useAntiAlias = true;
                    _samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                    _useAdvancedEffects = Constants.Quality.HIGH_USE_GLOW;
                    _heartSides = Constants.Quality.HIGH_HEART_SIDES;
                    _simplificationFactor = Constants.Quality.HIGH_SIMPLIFICATION_FACTOR;
                    break;
            }

            // Invalidate caches dependent on quality
            InvalidateCachedResources();
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.ApplyQualitySettings",
            ErrorMessage = "Failed to apply quality settings"
        });
    }

    /// <summary>
    /// Invalidates cached resources when configuration changes.
    /// </summary>
    private void InvalidateCachedResources()
    {
        Safe(() =>
        {
            _cachedHeartPicture?.Dispose();
            _cachedHeartPicture = null;
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.InvalidateCachedResources",
            ErrorMessage = "Failed to invalidate cached resources"
        });
    }
    #endregion

    #region Rendering
    /// <summary>
    /// Renders the heartbeat visualization on the canvas using spectrum data.
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
            _globalAnimationTime = (_globalAnimationTime + Constants.ANIMATION_TIME_INCREMENT) % 1000f;

            // Adjust configuration based on current canvas and parameters
            AdjustConfiguration(barCount, barSpacing, info.Width, info.Height);
            int actualHeartCount = Min(spectrum!.Length, _heartCount);

            // Process spectrum data
            if (_quality == RenderQuality.High && _spectrumProcessingTask == null)
            {
                ProcessSpectrumAsync(spectrum, actualHeartCount);
            }
            else
            {
                ProcessSpectrum(spectrum, actualHeartCount);
            }

            if (_cachedSmoothedSpectrum == null)
            {
                ProcessSpectrum(spectrum, actualHeartCount);
            }

            // Render hearts based on spectrum data
            RenderHeartbeats(canvas, _cachedSmoothedSpectrum, info, paint!);
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
        if (canvas == null || spectrum == null || paint == null)
        {
            Log(LogLevel.Error, Constants.LOG_PREFIX, "Invalid render parameters: null values");
            return false;
        }

        if (info.Width <= 0 || info.Height <= 0)
        {
            Log(LogLevel.Error, Constants.LOG_PREFIX, $"Invalid image dimensions: {info.Width}x{info.Height}");
            return false;
        }

        if (spectrum.Length == 0)
        {
            Log(LogLevel.Warning, Constants.LOG_PREFIX, "Empty spectrum data");
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

    #region Configuration Methods
    /// <summary>
    /// Updates the configuration with the specified parameters.
    /// </summary>
    private void UpdateConfiguration((float Size, float Spacing, int Count) config)
    {
        Safe(() =>
        {
            (_heartSize, _heartSpacing, _heartCount) = config;
            _previousSpectrum = _cachedScaledSpectrum = _cachedSmoothedSpectrum = null;
            _lastSpectrumLength = _lastTargetCount = 0;
            PrecomputeTrigValues();
            InvalidateCachedResources();
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.UpdateConfiguration",
            ErrorMessage = "Failed to update configuration"
        });
    }

    /// <summary>
    /// Adjusts the configuration based on current rendering parameters.
    /// </summary>
    private void AdjustConfiguration(int barCount, float barSpacing, int canvasWidth, int canvasHeight)
    {
        Safe(() =>
        {
            _heartSize = Max(10f, Constants.DEFAULT_CONFIG.Size - barCount * 0.3f + barSpacing * 0.5f);
            _heartSpacing = Max(5f, Constants.DEFAULT_CONFIG.Spacing - barCount * 0.1f + barSpacing * 0.2f);
            _heartCount = Clamp(barCount / 2, 4, 32);

            float maxSize = Min(canvasWidth, canvasHeight) / 4f;
            if (_heartSize > maxSize) _heartSize = maxSize;

            if (_cosValues == null || _sinValues == null || _cosValues.Length != _heartCount)
                PrecomputeTrigValues();
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.AdjustConfiguration",
            ErrorMessage = "Failed to adjust configuration"
        });
    }

    /// <summary>
    /// Precomputes trigonometric values for heart positioning.
    /// </summary>
    private void PrecomputeTrigValues()
    {
        Safe(() =>
        {
            _cosValues = new float[_heartCount];
            _sinValues = new float[_heartCount];
            float angleStep = 360f / _heartCount * Constants.RADIANS_PER_DEGREE;

            for (int i = 0; i < _heartCount; i++)
            {
                float angle = i * angleStep;
                _cosValues[i] = MathF.Cos(angle);
                _sinValues[i] = MathF.Sin(angle);
            }
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.PrecomputeTrigValues",
            ErrorMessage = "Failed to precompute trigonometric values"
        });
    }
    #endregion

    #region Spectrum Processing
    /// <summary>
    /// Asynchronously processes spectrum data.
    /// </summary>
    private void ProcessSpectrumAsync(float[] spectrum, int targetCount)
    {
        Safe(() =>
        {
            if (_spectrumProcessingTask != null && !_spectrumProcessingTask.IsCompleted)
                return;

            _spectrumProcessingTask = Task.Run(() =>
            {
                lock (_spectrumLock)
                {
                    ProcessSpectrum(spectrum, targetCount);
                }
            });
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.ProcessSpectrumAsync",
            ErrorMessage = "Failed to process spectrum asynchronously"
        });
    }

    /// <summary>
    /// Processes spectrum data for visualization.
    /// </summary>
    private void ProcessSpectrum(float[] spectrum, int targetCount)
    {
        Safe(() =>
        {
            bool needRescale = _lastSpectrumLength != spectrum.Length || _lastTargetCount != targetCount;

            if (needRescale || _cachedScaledSpectrum == null)
            {
                _cachedScaledSpectrum = new float[targetCount];
                _lastSpectrumLength = spectrum.Length;
                _lastTargetCount = targetCount;
            }

            // Scale spectrum to target count
            if (IsHardwareAccelerated && spectrum.Length >= Vector<float>.Count)
            {
                ScaleSpectrumSIMD(spectrum, _cachedScaledSpectrum, targetCount);
            }
            else
            {
                ScaleSpectrumStandard(spectrum, _cachedScaledSpectrum, targetCount);
            }

            // Initialize smoothed spectrum if needed
            if (_cachedSmoothedSpectrum == null || _cachedSmoothedSpectrum.Length != targetCount)
                _cachedSmoothedSpectrum = new float[targetCount];

            // Apply smoothing
            SmoothSpectrum(_cachedScaledSpectrum, _cachedSmoothedSpectrum, targetCount);
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.ProcessSpectrum",
            ErrorMessage = "Failed to process spectrum"
        });
    }

    /// <summary>
    /// Scales spectrum data using standard approach.
    /// </summary>
    [MethodImpl(AggressiveInlining)]
    private static void ScaleSpectrumStandard(float[] source, float[] target, int targetCount)
    {
        float blockSize = source.Length / (float)targetCount;

        for (int i = 0; i < targetCount; i++)
        {
            float sum = 0;
            int startIdx = (int)(i * blockSize);
            int endIdx = Min(source.Length, (int)((i + 1) * blockSize));

            for (int j = startIdx; j < endIdx; j++) sum += source[j];

            target[i] = sum / Max(1, endIdx - startIdx);
        }
    }

    /// <summary>
    /// Scales spectrum data using SIMD vectorization for performance.
    /// </summary>
    [MethodImpl(AggressiveOptimization)]
    private static void ScaleSpectrumSIMD(float[] source, float[] target, int targetCount)
    {
        float blockSize = source.Length / (float)targetCount;

        for (int i = 0; i < targetCount; i++)
        {
            int startIdx = (int)(i * blockSize);
            int endIdx = Min(source.Length, (int)((i + 1) * blockSize));
            int count = endIdx - startIdx;

            if (count < Vector<float>.Count)
            {
                float blockSum = 0;
                for (int blockIdx = startIdx; blockIdx < endIdx; blockIdx++)
                    blockSum += source[blockIdx];
                target[i] = blockSum / Max(1, count);
                continue;
            }

            Vector<float> sumVector = Vector<float>.Zero;
            int vectorized = count - count % Vector<float>.Count;
            int vecIdx = 0;

            for (; vecIdx < vectorized; vecIdx += Vector<float>.Count)
            {
                Vector<float> vec = new Vector<float>(source, startIdx + vecIdx);
                sumVector += vec;
            }

            float remainingSum = 0;
            for (int k = 0; k < Vector<float>.Count; k++)
            {
                remainingSum += sumVector[k];
            }

            for (; vecIdx < count; vecIdx++)
            {
                remainingSum += source[startIdx + vecIdx];
            }

            target[i] = remainingSum / Max(1, count);
        }
    }

    /// <summary>
    /// Applies temporal smoothing to the spectrum data.
    /// </summary>
    [MethodImpl(AggressiveOptimization)]
    private void SmoothSpectrum(float[] source, float[] target, int count)
    {
        Safe(() =>
        {
            if (_previousSpectrum == null || _previousSpectrum.Length != count)
            {
                _previousSpectrum = new float[count];
                Array.Copy(source, _previousSpectrum, count);
            }

            if (IsHardwareAccelerated && count >= Vector<float>.Count)
            {
                int vectorized = count - count % Vector<float>.Count;

                for (int i = 0; i < vectorized; i += Vector<float>.Count)
                {
                    Vector<float> sourceVector = new Vector<float>(source, i);
                    Vector<float> previousVector = new Vector<float>(_previousSpectrum, i);
                    Vector<float> diff = sourceVector - previousVector;
                    Vector<float> smoothingVector = new Vector<float>(_smoothingFactor);
                    Vector<float> resultVector = previousVector + diff * smoothingVector;

                    resultVector.CopyTo(target, i);
                    resultVector.CopyTo(_previousSpectrum, i);
                }

                for (int i = vectorized; i < count; i++)
                {
                    target[i] = _previousSpectrum[i] + (source[i] - _previousSpectrum[i]) * _smoothingFactor;
                    _previousSpectrum[i] = target[i];
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    target[i] = _previousSpectrum[i] + (source[i] - _previousSpectrum[i]) * _smoothingFactor;
                    _previousSpectrum[i] = target[i];
                }
            }
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.SmoothSpectrum",
            ErrorMessage = "Failed to smooth spectrum"
        });
    }
    #endregion

    #region Rendering Implementation
    /// <summary>
    /// Renders heartbeat visualization using processed spectrum data.
    /// </summary>
    private void RenderHeartbeats(SKCanvas canvas, float[]? spectrum, SKImageInfo info, SKPaint basePaint)
    {
        Safe(() =>
        {
            if (spectrum == null)
                return;

            float centerX = info.Width / 2f;
            float centerY = info.Height / 2f;
            float radius = Min(info.Width, info.Height) / 3f;

            using var heartPath = _pathPool.Get();

            // Create cached heart picture if needed
            if (_cachedHeartPicture == null)
            {
                var recorder = new SKPictureRecorder();
                var recordCanvas = recorder.BeginRecording(new SKRect(-1, -1, 1, 1));
                CreateHeartPath(heartPath, 0, 0, 1f);
                recordCanvas.DrawPath(heartPath, basePaint);
                _cachedHeartPicture = recorder.EndRecording();
                heartPath.Reset();
            }

            using var heartPaint = _paintPool.Get();
            heartPaint.IsAntialias = _useAntiAlias;
            heartPaint.Style = Fill;

            // Only create glow paint if needed
            using var glowPaint = _useAdvancedEffects ? _paintPool.Get() : null;
            if (glowPaint != null)
            {
                glowPaint.IsAntialias = _useAntiAlias;
                glowPaint.Style = Fill;
            }

            lock (_spectrumLock)
            {
                for (int i = 0; i < spectrum.Length; i++)
                {
                    float magnitude = spectrum[i];
                    if (magnitude < Constants.MIN_MAGNITUDE_THRESHOLD)
                        continue;

                    float x = centerX + _cosValues![i] * radius * (1 - magnitude * 0.5f);
                    float y = centerY + _sinValues![i] * radius * (1 - magnitude * 0.5f);

                    float heartSize = _heartSize * magnitude * Constants.HEART_BASE_SCALE *
                                     (MathF.Sin(_globalAnimationTime * Constants.PULSE_FREQUENCY) * 0.1f + 1f);

                    SKRect heartBounds = new(
                        x - heartSize,
                        y - heartSize,
                        x + heartSize,
                        y + heartSize
                    );

                    if (canvas.QuickReject(heartBounds))
                        continue;

                    byte alpha = (byte)MathF.Min(magnitude * Constants.ALPHA_MULTIPLIER * 255f, 255f);
                    heartPaint.Color = basePaint.Color.WithAlpha(alpha);

                    if (_heartSides > 0)
                    {
                        DrawSimplifiedHeart(canvas, x, y, heartSize, heartPath, heartPaint, glowPaint, alpha);
                    }
                    else
                    {
                        DrawCachedHeart(canvas, x, y, heartSize, heartPaint, glowPaint, alpha);
                    }
                }
            }
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.RenderHeartbeats",
            ErrorMessage = "Failed to render heartbeats"
        });
    }

    /// <summary>
    /// Draws simplified heart shape using polygon approximation.
    /// </summary>
    private void DrawSimplifiedHeart(
        SKCanvas canvas,
        float x,
        float y,
        float size,
        SKPath path,
        SKPaint heartPaint,
        SKPaint? glowPaint,
        byte alpha)
    {
        Safe(() =>
        {
            path.Reset();
            float angleStep = 360f / _heartSides * Constants.RADIANS_PER_DEGREE;
            path.MoveTo(x, y + size / 2);

            for (int i = 0; i < _heartSides; i++)
            {
                float angle = i * angleStep;
                float radius = size * (1 + 0.3f * MathF.Sin(angle * 2)) * (1 - _simplificationFactor * 0.5f);
                float px = x + MathF.Cos(angle) * radius;
                float py = y + MathF.Sin(angle) * radius - size * 0.2f;
                path.LineTo(px, py);
            }

            path.Close();

            if (_useAdvancedEffects && glowPaint != null)
            {
                glowPaint.Color = heartPaint.Color.WithAlpha((byte)(alpha / Constants.GLOW_ALPHA_DIVISOR));
                glowPaint.MaskFilter = SKMaskFilter.CreateBlur(
                    Normal,
                    size * 0.2f * (1 - _simplificationFactor)
                );
                canvas.DrawPath(path, glowPaint);
            }

            canvas.DrawPath(path, heartPaint);
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.DrawSimplifiedHeart",
            ErrorMessage = "Failed to draw simplified heart"
        });
    }

    /// <summary>
    /// Draws heart using cached picture for better performance.
    /// </summary>
    private void DrawCachedHeart(
        SKCanvas canvas,
        float x,
        float y,
        float size,
        SKPaint heartPaint,
        SKPaint? glowPaint,
        byte alpha)
    {
        Safe(() =>
        {
            canvas.Save();
            canvas.Translate(x, y);
            canvas.Scale(size, size);

            if (_useAdvancedEffects && glowPaint != null)
            {
                glowPaint.Color = heartPaint.Color.WithAlpha((byte)(alpha / Constants.GLOW_ALPHA_DIVISOR));
                glowPaint.MaskFilter = SKMaskFilter.CreateBlur(
                    Normal,
                    size * Constants.GLOW_INTENSITY
                );

                canvas.DrawPicture(_cachedHeartPicture!, glowPaint);
            }

            canvas.DrawPicture(_cachedHeartPicture!, heartPaint);
            canvas.Restore();
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.DrawCachedHeart",
            ErrorMessage = "Failed to draw cached heart"
        });
    }

    /// <summary>
    /// Creates a heart-shaped path.
    /// </summary>
    private static void CreateHeartPath(SKPath path, float x, float y, float size)
    {
        path.Reset();
        path.MoveTo(x, y + size / 2);
        path.CubicTo(x - size, y, x - size, y - size / 2, x, y - size);
        path.CubicTo(x + size, y - size / 2, x + size, y, x, y + size / 2);
        path.Close();
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
                _spectrumProcessingTask?.Wait(100);

                // Dispose cached resources
                _cachedHeartPicture?.Dispose();
                _cachedHeartPicture = null;

                // Dispose object pools
                _pathPool.Dispose();
                _paintPool.Dispose();

                // Clean up cached data
                _previousSpectrum = _cachedScaledSpectrum = _cachedSmoothedSpectrum = null;
                _cosValues = _sinValues = null;

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