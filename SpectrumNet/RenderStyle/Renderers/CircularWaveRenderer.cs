#nullable enable

namespace SpectrumNet
{
    /// <summary>
    /// Renderer that visualizes spectrum data as animated circular waves.
    /// </summary>
    public sealed class CircularWaveRenderer : BaseSpectrumRenderer
    {
        #region Constants
        private static class Constants
        {
            // Logging
            public const string LOG_PREFIX = "CircularWaveRenderer";

            // Animation parameters
            public const float TIME_STEP = 0.016f;                // Time increment per frame (~60 FPS)
            public const float ROTATION_SPEED = 0.5f;             // Base rotation speed in radians per second
            public const float SPECTRUM_ROTATION_INFLUENCE = 0.3f; // How much spectrum affects rotation
            public const float WAVE_SPEED = 2.0f;                 // Speed of wave animation
            public const float PHASE_OFFSET = 0.1f;               // Phase offset between rings

            // Rendering properties
            public const float CENTER_CIRCLE_RADIUS = 30.0f;      // Radius of the center circle
            public const float MIN_RADIUS_INCREMENT = 15.0f;      // Minimum spacing between rings
            public const float MAX_RADIUS_INCREMENT = 40.0f;      // Maximum spacing between rings
            public const float MIN_MAGNITUDE_THRESHOLD = 0.01f;   // Minimum magnitude to render
            public const int MAX_RING_COUNT = 32;                 // Maximum number of rings to render
            public const float MIN_STROKE_WIDTH = 1.5f;           // Minimum stroke width
            public const float MAX_STROKE_WIDTH = 8.0f;           // Maximum stroke width
            public const float STROKE_WIDTH_FACTOR = 6.0f;        // Factor for calculating stroke width
            public const float ALPHA_MULTIPLIER = 255.0f;         // Multiplier for alpha calculation
            public const float MIN_ALPHA_FACTOR = 0.3f;           // Minimum alpha factor for distant rings

            // Quality-specific settings
            public const int POINTS_PER_CIRCLE_LOW = 32;          // Point count for low quality
            public const int POINTS_PER_CIRCLE_MEDIUM = 64;       // Point count for medium quality
            public const int POINTS_PER_CIRCLE_HIGH = 128;        // Point count for high quality
            public const float GLOW_RADIUS_LOW = 1.5f;            // Glow radius for low quality
            public const float GLOW_RADIUS_MEDIUM = 3.0f;         // Glow radius for medium quality
            public const float GLOW_RADIUS_HIGH = 5.0f;           // Glow radius for high quality
        }
        #endregion

        #region Fields
        private static readonly Lazy<CircularWaveRenderer> _instance = new(() => new CircularWaveRenderer());

        // Object pools for efficient resource management
        private readonly ObjectPool<SKPath> _pathPool = new(() => new SKPath(), path => path.Reset(), 5);
        private readonly ObjectPool<SKPaint> _paintPool = new(() => new SKPaint(), paint => paint.Reset(), 3);

        // Rendering state
        private float _time;
        private float _angle;
        private float _rotationSpeed = Constants.ROTATION_SPEED;

        // Calculated and cached data
        private float[]? _ringMagnitudes;
        private SKPoint[]? _circlePoints;
        private SKPoint _center;

        // Quality-dependent settings
        private new bool _useAntiAlias = true;
        private SKSamplingOptions _samplingOptions = new(SKFilterMode.Linear, SKMipmapMode.Linear);
        private new bool _useAdvancedEffects = true;
        private int _pointsPerCircle = Constants.POINTS_PER_CIRCLE_MEDIUM;
        private float _glowRadius = Constants.GLOW_RADIUS_MEDIUM;

        // Thread safety
        private readonly object _renderLock = new();
        private DateTime _lastUpdateTime = DateTime.Now;
        private new bool _disposed;
        #endregion

        #region Singleton Pattern
        private CircularWaveRenderer() { }

        /// <summary>
        /// Gets the singleton instance of the circular wave renderer.
        /// </summary>
        public static CircularWaveRenderer GetInstance() => _instance.Value;
        #endregion

        #region Initialization and Configuration
        /// <summary>
        /// Initializes the circular wave renderer and prepares rendering resources.
        /// </summary>
        public override void Initialize()
        {
            SmartLogger.Safe(() =>
            {
                base.Initialize();

                // Initialize animation state
                _time = 0f;
                _angle = 0f;

                // Create initial circle points cache
                CreateCirclePointsCache();

                // Apply initial quality settings
                ApplyQualitySettings();

                SmartLogger.Log(LogLevel.Debug, Constants.LOG_PREFIX, "Initialized");
            }, new SmartLogger.ErrorHandlingOptions
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
            SmartLogger.Safe(() =>
            {
                base.Configure(isOverlayActive, quality);

                // Apply quality settings if changed
                if (_quality != quality)
                {
                    ApplyQualitySettings();
                }
            }, new SmartLogger.ErrorHandlingOptions
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
            SmartLogger.Safe(() =>
            {
                base.ApplyQualitySettings();

                switch (_quality)
                {
                    case RenderQuality.Low:
                        _useAntiAlias = false;
                        _samplingOptions = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);
                        _useAdvancedEffects = false;
                        _pointsPerCircle = Constants.POINTS_PER_CIRCLE_LOW;
                        _glowRadius = Constants.GLOW_RADIUS_LOW;
                        break;

                    case RenderQuality.Medium:
                        _useAntiAlias = true;
                        _samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                        _useAdvancedEffects = true;
                        _pointsPerCircle = Constants.POINTS_PER_CIRCLE_MEDIUM;
                        _glowRadius = Constants.GLOW_RADIUS_MEDIUM;
                        break;

                    case RenderQuality.High:
                        _useAntiAlias = true;
                        _samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                        _useAdvancedEffects = true;
                        _pointsPerCircle = Constants.POINTS_PER_CIRCLE_HIGH;
                        _glowRadius = Constants.GLOW_RADIUS_HIGH;
                        break;
                }

                // Recreate circle points cache with new point density
                CreateCirclePointsCache();
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.ApplyQualitySettings",
                ErrorMessage = "Failed to apply quality settings"
            });
        }

        /// <summary>
        /// Creates a cache of circle points to avoid recalculation on each frame.
        /// </summary>
        private void CreateCirclePointsCache()
        {
            SmartLogger.Safe(() =>
            {
                _circlePoints = new SKPoint[_pointsPerCircle];
                float angleStep = 2 * MathF.PI / _pointsPerCircle;

                for (int i = 0; i < _pointsPerCircle; i++)
                {
                    float angle = i * angleStep;
                    _circlePoints[i] = new SKPoint(MathF.Cos(angle), MathF.Sin(angle));
                }
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.CreateCirclePointsCache",
                ErrorMessage = "Failed to create circle points cache"
            });
        }
        #endregion

        #region Rendering
        /// <summary>
        /// Renders the circular wave visualization on the canvas using spectrum data.
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
            if (!QuickValidate(canvas, spectrum, info, paint))
            {
                drawPerformanceInfo?.Invoke(canvas!, info);
                return;
            }

            // Define render bounds and quick reject if not visible
            SKRect renderBounds = new(0, 0, info.Width, info.Height);
            if (canvas!.QuickReject(renderBounds))
            {
                drawPerformanceInfo?.Invoke(canvas, info);
                return;
            }

            SmartLogger.Safe(() =>
            {
                lock (_renderLock)
                {
                    // Update animation state
                    DateTime now = DateTime.Now;
                    float deltaTime = (float)(now - _lastUpdateTime).TotalSeconds;
                    _lastUpdateTime = now;

                    _time += Constants.TIME_STEP;
                    UpdateRotation(deltaTime, spectrum!);

                    // Calculate center point
                    _center = new SKPoint(info.Width * 0.5f, info.Height * 0.5f);

                    // Prepare and process spectrum data
                    int ringCount = Math.Min(barCount, Constants.MAX_RING_COUNT);
                    float[] processedSpectrum = PrepareSpectrum(spectrum!, ringCount, spectrum!.Length);
                    PrepareRingMagnitudes(processedSpectrum, ringCount);

                    // Render circular waves
                    RenderCircularWaves(canvas, info, paint!, ringCount);
                }
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.Render",
                ErrorMessage = "Error during rendering"
            });

            drawPerformanceInfo?.Invoke(canvas!, info);
        }

        /// <summary>
        /// Updates the rotation angle based on spectrum data.
        /// </summary>
        private void UpdateRotation(float deltaTime, float[] spectrum)
        {
            if (spectrum.Length == 0) return;

            // Calculate average spectrum intensity to influence rotation speed
            float avgIntensity = 0f;
            for (int i = 0; i < spectrum.Length; i++)
            {
                avgIntensity += spectrum[i];
            }
            avgIntensity /= spectrum.Length;

            // Update rotation speed and angle
            _rotationSpeed = Constants.ROTATION_SPEED * (1f + avgIntensity * Constants.SPECTRUM_ROTATION_INFLUENCE);
            _angle = (_angle + _rotationSpeed * deltaTime) % MathF.Tau;
        }

        /// <summary>
        /// Prepares ring magnitudes from spectrum data.
        /// </summary>
        private void PrepareRingMagnitudes(float[] spectrum, int ringCount)
        {
            if (_ringMagnitudes == null || _ringMagnitudes.Length != ringCount)
            {
                _ringMagnitudes = new float[ringCount];
            }

            // Group spectrum data into rings
            int spectrumLength = spectrum.Length;

            for (int i = 0; i < ringCount; i++)
            {
                int startIdx = i * spectrumLength / ringCount;
                int endIdx = (i + 1) * spectrumLength / ringCount;
                endIdx = Math.Min(endIdx, spectrumLength);

                float sum = 0f;
                for (int j = startIdx; j < endIdx; j++)
                {
                    sum += spectrum[j];
                }

                _ringMagnitudes[i] = sum / (endIdx - startIdx);
            }
        }

        /// <summary>
        /// Renders circular waves based on spectrum data.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private void RenderCircularWaves(SKCanvas canvas, SKImageInfo info, SKPaint basePaint, int ringCount)
        {
            if (_ringMagnitudes == null || _circlePoints == null) return;

            float smallerDimension = Math.Min(info.Width, info.Height);
            float maxRadius = smallerDimension * 0.45f;  // Leave some margin
            float radiusIncrement = (maxRadius - Constants.CENTER_CIRCLE_RADIUS) / ringCount;

            // Prepare base paint
            using var mainPaint = _paintPool.Get();
            mainPaint.Color = basePaint.Color;
            mainPaint.IsAntialias = _useAntiAlias;
            mainPaint.Style = SKPaintStyle.Stroke;

            // Draw from outer rings to inner for proper layering
            for (int i = ringCount - 1; i >= 0; i--)
            {
                float magnitude = _ringMagnitudes[i];
                if (magnitude < Constants.MIN_MAGNITUDE_THRESHOLD) continue;

                // Calculate ring properties
                float baseRadius = Constants.CENTER_CIRCLE_RADIUS + i * radiusIncrement;
                float waveOffset = MathF.Sin(_time * Constants.WAVE_SPEED + i * Constants.PHASE_OFFSET) * magnitude * radiusIncrement;
                float radius = baseRadius + waveOffset;

                // Skip if ring would be outside canvas
                if (radius > maxRadius ||
                    _center.X + radius < 0 || _center.X - radius > info.Width ||
                    _center.Y + radius < 0 || _center.Y - radius > info.Height)
                {
                    continue;
                }

                // Calculate alpha based on magnitude and distance from center
                float distanceFactor = 1.0f - (radius / maxRadius);
                float alphaFactor = Constants.MIN_ALPHA_FACTOR + (1.0f - Constants.MIN_ALPHA_FACTOR) * distanceFactor;
                byte alpha = (byte)Math.Min(magnitude * Constants.ALPHA_MULTIPLIER * alphaFactor, 255);

                // Calculate stroke width based on magnitude
                float strokeWidth = Constants.MIN_STROKE_WIDTH + magnitude * Constants.STROKE_WIDTH_FACTOR;
                strokeWidth = Math.Clamp(strokeWidth, Constants.MIN_STROKE_WIDTH, Constants.MAX_STROKE_WIDTH);

                mainPaint.StrokeWidth = strokeWidth;
                mainPaint.Color = basePaint.Color.WithAlpha(alpha);

                // Draw glow effect for high magnitude rings
                if (_useAdvancedEffects && magnitude > 0.5f)
                {
                    RenderRingWithGlow(canvas, radius, magnitude, mainPaint);
                }
                else
                {
                    RenderRing(canvas, radius, mainPaint);
                }
            }
        }

        /// <summary>
        /// Renders a single ring with the specified radius and paint.
        /// </summary>
        private void RenderRing(SKCanvas canvas, float radius, SKPaint paint)
        {
            if (_circlePoints == null) return;

            using var path = _pathPool.Get();
            path.Reset();

            bool firstPoint = true;
            foreach (var point in _circlePoints)
            {
                float x = _center.X + point.X * radius;
                float y = _center.Y + point.Y * radius;

                if (firstPoint)
                {
                    path.MoveTo(x, y);
                    firstPoint = false;
                }
                else
                {
                    path.LineTo(x, y);
                }
            }

            path.Close();
            canvas.DrawPath(path, paint);
        }

        /// <summary>
        /// Renders a ring with additional glow effect.
        /// </summary>
        private void RenderRingWithGlow(SKCanvas canvas, float radius, float magnitude, SKPaint paint)
        {
            if (_circlePoints == null) return;

            // Create glow effect paint
            using var glowPaint = _paintPool.Get();
            glowPaint.Color = paint.Color.WithAlpha((byte)(paint.Color.Alpha * 0.7f));
            glowPaint.IsAntialias = paint.IsAntialias;
            glowPaint.Style = paint.Style;
            glowPaint.StrokeWidth = paint.StrokeWidth * 1.5f;
            glowPaint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, _glowRadius * magnitude);

            // Draw the glow effect
            using var path = _pathPool.Get();
            path.Reset();

            bool firstPoint = true;
            foreach (var point in _circlePoints)
            {
                float x = _center.X + point.X * radius;
                float y = _center.Y + point.Y * radius;

                if (firstPoint)
                {
                    path.MoveTo(x, y);
                    firstPoint = false;
                }
                else
                {
                    path.LineTo(x, y);
                }
            }

            path.Close();

            // Draw glow first, then the main ring
            canvas.DrawPath(path, glowPaint);
            canvas.DrawPath(path, paint);
        }
        #endregion

        #region Helper Methods
        /// <summary>
        /// Determines if the magnitude is significant enough to render.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsSignificantMagnitude(float magnitude) =>
            magnitude >= Constants.MIN_MAGNITUDE_THRESHOLD;

        /// <summary>
        /// Calculates the alpha value based on magnitude and a factor.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private byte CalculateAlpha(float magnitude, float factor) =>
            (byte)Math.Min(magnitude * Constants.ALPHA_MULTIPLIER * factor, 255);
        #endregion

        #region Disposal
        /// <summary>
        /// Disposes of resources used by the renderer.
        /// </summary>
        public override void Dispose()
        {
            if (!_disposed)
            {
                SmartLogger.Safe(() =>
                {
                    _pathPool?.Dispose();
                    _paintPool?.Dispose();

                    base.Dispose();

                    _disposed = true;
                    SmartLogger.Log(LogLevel.Debug, Constants.LOG_PREFIX, "Disposed");
                }, new SmartLogger.ErrorHandlingOptions
                {
                    Source = $"{Constants.LOG_PREFIX}.Dispose",
                    ErrorMessage = "Error during disposal"
                });
            }
        }
        #endregion
    }
}