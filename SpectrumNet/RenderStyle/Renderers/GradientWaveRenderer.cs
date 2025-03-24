#nullable enable

using System.Numerics;
using System.Runtime.CompilerServices;

namespace SpectrumNet
{
    /// <summary>
    /// Renderer that visualizes spectrum data as gradient-colored waves with smooth transitions.
    /// </summary>
    public sealed class GradientWaveRenderer : BaseSpectrumRenderer
    {
        #region Singleton Pattern
        private static readonly Lazy<GradientWaveRenderer> _instance = new(() => new GradientWaveRenderer());
        private GradientWaveRenderer() { } // Приватный конструктор
        public static GradientWaveRenderer GetInstance() => _instance.Value;
        #endregion

        #region Constants
        private static class Constants
        {
            // Logging
            public const string LOG_PREFIX = "GradientWaveRenderer";

            // Layout constants
            public const float EDGE_OFFSET = 10f;               // Edge offset for drawing
            public const float BASELINE_OFFSET = 2f;            // Y-offset for the wave baseline
            public const int EXTRA_POINTS_COUNT = 4;            // Number of extra points to add to the wave path

            // Wave smoothing factors
            public const float SMOOTHING_FACTOR_NORMAL = 0.3f;  // Normal smoothing factor
            public const float SMOOTHING_FACTOR_OVERLAY = 0.5f; // More aggressive smoothing for overlay mode
            public const float MIN_MAGNITUDE_THRESHOLD = 0.01f; // Minimum magnitude to render
            public const float MAX_SPECTRUM_VALUE = 1.5f;       // Maximum spectrum value cap

            // Rendering properties
            public const float DEFAULT_LINE_WIDTH = 3f;         // Width of main spectrum line
            public const float GLOW_INTENSITY = 0.3f;           // Intensity of glow effect
            public const float HIGH_MAGNITUDE_THRESHOLD = 0.7f; // Threshold for extra effects
            public const float FILL_OPACITY = 0.2f;             // Opacity of gradient fill

            // Color properties
            public const float LINE_GRADIENT_SATURATION = 100f; // Saturation for normal mode
            public const float LINE_GRADIENT_LIGHTNESS = 50f;   // Lightness for normal mode
            public const float OVERLAY_GRADIENT_SATURATION = 100f; // Saturation for overlay mode
            public const float OVERLAY_GRADIENT_LIGHTNESS = 55f;   // Lightness for overlay mode
            public const float MAX_BLUR_RADIUS = 6f;            // Maximum blur for glow effect

            // Quality settings (Low)
            public const int POINT_COUNT_LOW = 20;              // Number of points in low quality
            public const int SMOOTHING_PASSES_LOW = 1;          // Smoothing passes in low quality

            // Quality settings (Medium)
            public const int POINT_COUNT_MEDIUM = 40;           // Number of points in medium quality
            public const int SMOOTHING_PASSES_MEDIUM = 2;       // Smoothing passes in medium quality

            // Quality settings (High)
            public const int POINT_COUNT_HIGH = 80;             // Number of points in high quality
            public const int SMOOTHING_PASSES_HIGH = 3;         // Smoothing passes in high quality

            // Performance optimization
            public const int BATCH_SIZE = 128;                  // Batch size for parallel processing
        }
        #endregion

        #region Fields
        // Object pools for efficient resource management
        private readonly ObjectPool<SKPath> _pathPool = new(() => new SKPath(), path => path.Reset(), 2);
        private readonly ObjectPool<SKPaint> _paintPool = new(() => new SKPaint(), paint => paint.Reset(), 3);

        // Rendering state
        private bool _isOverlayActive;
        private List<SKPoint>? _cachedPoints;
        private readonly SKPath _wavePath = new();
        private readonly SKPath _fillPath = new();
        private SKPicture? _cachedBackground;

        // Quality-dependent settings
        private new bool _useAntiAlias = true;
        private new SKSamplingOptions _samplingOptions = new(SKFilterMode.Linear, SKMipmapMode.Linear);
        private new bool _useAdvancedEffects = true;
        private int _smoothingPasses = 2;
        private int _pointCount = Constants.POINT_COUNT_MEDIUM;

        // Synchronization and state
        private readonly SemaphoreSlim _renderSemaphore = new(1, 1);
        private readonly object _spectrumLock = new();
        private new bool _disposed;
        #endregion

        #region Initialization and Configuration
        /// <summary>
        /// Initializes the gradient wave renderer and prepares resources for rendering.
        /// </summary>
        public override void Initialize()
        {
            SmartLogger.Safe(() =>
            {
                base.Initialize();

                _smoothingFactor = Constants.SMOOTHING_FACTOR_NORMAL;

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

                bool configChanged = _isOverlayActive != isOverlayActive || _quality != quality;

                // Update overlay mode
                _isOverlayActive = isOverlayActive;
                _smoothingFactor = isOverlayActive ?
                    Constants.SMOOTHING_FACTOR_OVERLAY :
                    Constants.SMOOTHING_FACTOR_NORMAL;

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
                        _smoothingPasses = Constants.SMOOTHING_PASSES_LOW;
                        _pointCount = Constants.POINT_COUNT_LOW;
                        break;

                    case RenderQuality.Medium:
                        _useAntiAlias = true;
                        _samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                        _useAdvancedEffects = true;
                        _smoothingPasses = Constants.SMOOTHING_PASSES_MEDIUM;
                        _pointCount = Constants.POINT_COUNT_MEDIUM;
                        break;

                    case RenderQuality.High:
                        _useAntiAlias = true;
                        _samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                        _useAdvancedEffects = true;
                        _smoothingPasses = Constants.SMOOTHING_PASSES_HIGH;
                        _pointCount = Constants.POINT_COUNT_HIGH;
                        break;
                }

                // Invalidate caches dependent on quality
                InvalidateCachedResources();
            }, new SmartLogger.ErrorHandlingOptions
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
            SmartLogger.Safe(() =>
            {
                _cachedBackground?.Dispose();
                _cachedBackground = null;
                _cachedPoints = null;
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.InvalidateCachedResources",
                ErrorMessage = "Failed to invalidate cached resources"
            });
        }
        #endregion

        #region Rendering
        /// <summary>
        /// Renders the gradient wave visualization on the canvas using spectrum data.
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

            SmartLogger.Safe(() =>
            {
                bool semaphoreAcquired = false;
                try
                {
                    // Try to acquire semaphore for updating animation state
                    semaphoreAcquired = _renderSemaphore.Wait(0);

                    float[] renderSpectrum;
                    List<SKPoint> renderPoints;

                    if (semaphoreAcquired)
                    {
                        // Process spectrum data when semaphore is acquired
                        ProcessSpectrumData(spectrum!, barCount);
                    }

                    // Get processed spectrum and points for rendering
                    lock (_spectrumLock)
                    {
                        renderSpectrum = _processedSpectrum ??
                                        ProcessSpectrumSynchronously(spectrum!, barCount);
                        renderPoints = _cachedPoints ??
                                      GenerateOptimizedPoints(renderSpectrum, info);
                    }

                    // Execute rendering process
                    RenderGradientWave(canvas!, renderPoints, renderSpectrum, info, paint!, barCount);
                }
                finally
                {
                    // Release semaphore if acquired
                    if (semaphoreAcquired)
                    {
                        _renderSemaphore.Release();
                    }
                }
            }, new SmartLogger.ErrorHandlingOptions
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
                SmartLogger.Log(LogLevel.Error, Constants.LOG_PREFIX, "Invalid render parameters: null values");
                return false;
            }

            if (info.Width <= 0 || info.Height <= 0)
            {
                SmartLogger.Log(LogLevel.Error, Constants.LOG_PREFIX, $"Invalid image dimensions: {info.Width}x{info.Height}");
                return false;
            }

            if (spectrum.Length == 0)
            {
                SmartLogger.Log(LogLevel.Warning, Constants.LOG_PREFIX, "Empty spectrum data");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Processes spectrum data for visualization.
        /// </summary>
        private void ProcessSpectrumData(float[] spectrum, int barCount)
        {
            SmartLogger.Safe(() =>
            {
                // Ensure spectrum buffer is initialized
                EnsureSpectrumBuffer(spectrum.Length);

                int spectrumLength = spectrum.Length;
                int actualBarCount = Math.Min(spectrumLength, barCount);

                // Scale spectrum data to target bar count
                float[] scaledSpectrum = ScaleSpectrum(spectrum, actualBarCount, spectrumLength);

                // Apply smoothing for transitions
                _processedSpectrum = SmoothSpectrumData(scaledSpectrum, actualBarCount);

                lock (_spectrumLock)
                {
                    // Clear cached points to force regeneration with new spectrum
                    _cachedPoints = null;
                }
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.ProcessSpectrumData",
                ErrorMessage = "Error processing spectrum data"
            });
        }

        /// <summary>
        /// Processes spectrum data synchronously when async processing isn't available.
        /// </summary>
        private float[] ProcessSpectrumSynchronously(float[] spectrum, int barCount)
        {
            int spectrumLength = spectrum.Length;
            int actualBarCount = Math.Min(spectrumLength, barCount);
            float[] scaledSpectrum = ScaleSpectrum(spectrum, actualBarCount, spectrumLength);
            return SmoothSpectrumData(scaledSpectrum, actualBarCount);
        }

        /// <summary>
        /// Ensures the spectrum buffer is of the correct size.
        /// </summary>
        private void EnsureSpectrumBuffer(int length)
        {
            SmartLogger.Safe(() =>
            {
                if (_previousSpectrum == null || _previousSpectrum.Length != length)
                {
                    _previousSpectrum = new float[length];
                }
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.EnsureSpectrumBuffer",
                ErrorMessage = "Error ensuring spectrum buffer"
            });
        }
        #endregion

        #region Spectrum Processing
        /// <summary>
        /// Applies smoothing to the spectrum data.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private float[] SmoothSpectrumData(float[] spectrum, int targetCount)
        {
            // First apply temporal smoothing with previous frame data
            float[] smoothedSpectrum = new float[targetCount];

            for (int i = 0; i < targetCount; i++)
            {
                if (_previousSpectrum == null) break;

                float currentValue = spectrum[i];
                float previousValue = _previousSpectrum[i];
                float smoothedValue = previousValue + (currentValue - previousValue) * _smoothingFactor;
                smoothedSpectrum[i] = Math.Clamp(smoothedValue, Constants.MIN_MAGNITUDE_THRESHOLD, Constants.MAX_SPECTRUM_VALUE);
                _previousSpectrum[i] = smoothedSpectrum[i];
            }

            // Apply additional spatial smoothing passes based on quality setting
            for (int pass = 0; pass < _smoothingPasses; pass++)
            {
                var extraSmoothedSpectrum = new float[targetCount];

                for (int i = 0; i < targetCount; i++)
                {
                    float sum = smoothedSpectrum[i];
                    int count = 1;

                    if (i > 0) { sum += smoothedSpectrum[i - 1]; count++; }
                    if (i < targetCount - 1) { sum += smoothedSpectrum[i + 1]; count++; }

                    extraSmoothedSpectrum[i] = sum / count;
                }

                // Swap buffers
                smoothedSpectrum = extraSmoothedSpectrum;
            }

            return smoothedSpectrum;
        }

        /// <summary>
        /// Generates optimized points for the wave visualization.
        /// </summary>
        private List<SKPoint> GenerateOptimizedPoints(float[] spectrum, SKImageInfo info)
        {
            float minY = Constants.EDGE_OFFSET;
            float maxY = info.Height - Constants.EDGE_OFFSET;
            int spectrumLength = spectrum.Length;

            if (spectrumLength < 1)
            {
                return new List<SKPoint>();
            }

            // Calculate optimal number of points based on quality
            int pointCount = Math.Min(_pointCount, spectrumLength);

            // Pre-allocate list with exact capacity to avoid resizing
            var points = new List<SKPoint>(pointCount + Constants.EXTRA_POINTS_COUNT);

            // Add edge points at start
            points.Add(new SKPoint(-Constants.EDGE_OFFSET, maxY));
            points.Add(new SKPoint(0, maxY));

            // Sample spectrum at optimal intervals
            float step = (float)spectrumLength / pointCount;

            for (int i = 0; i < pointCount; i++)
            {
                float position = i * step;
                int index = Math.Min((int)position, spectrumLength - 1);
                float remainder = position - index;

                // Use linear interpolation between points for smoother curves
                float value;
                if (index < spectrumLength - 1 && remainder > 0)
                {
                    value = spectrum[index] * (1 - remainder) + spectrum[index + 1] * remainder;
                }
                else
                {
                    value = spectrum[index];
                }

                float x = (i / (float)(pointCount - 1)) * info.Width;
                float y = maxY - (value * (maxY - minY));
                points.Add(new SKPoint(x, y));
            }

            // Add edge points at end
            points.Add(new SKPoint(info.Width, maxY));
            points.Add(new SKPoint(info.Width + Constants.EDGE_OFFSET, maxY));

            return points;
        }
        #endregion

        #region Rendering Implementation
        /// <summary>
        /// Renders the gradient wave visualization with optimized paths and effects.
        /// </summary>
        private void RenderGradientWave(
            SKCanvas canvas,
            List<SKPoint> points,
            float[] spectrum,
            SKImageInfo info,
            SKPaint basePaint,
            int barCount)
        {
            if (points.Count < 2) return;

            // Calculate maximum magnitude only once for effects
            float maxMagnitude = CalculateMaxMagnitude(spectrum);
            float yBaseline = info.Height - Constants.EDGE_OFFSET + Constants.BASELINE_OFFSET;
            bool shouldRenderGlow = maxMagnitude > Constants.HIGH_MAGNITUDE_THRESHOLD && _useAdvancedEffects;

            using var wavePath = _pathPool.Get();
            using var fillPath = _pathPool.Get();

            // Prepare paths for rendering
            CreateWavePaths(points, info, yBaseline, wavePath, fillPath);

            // Save canvas state for all operations
            canvas.Save();

            try
            {
                // Render fill with gradient
                RenderFillGradient(canvas, basePaint, maxMagnitude, info, yBaseline, fillPath);

                // Render glow effect if needed
                if (shouldRenderGlow)
                {
                    RenderGlowEffect(canvas, basePaint, maxMagnitude, barCount, wavePath);
                }

                // Render main line with optimized gradient
                RenderLineGradient(canvas, points, basePaint, info, wavePath);
            }
            finally
            {
                canvas.Restore();
            }
        }

        /// <summary>
        /// Calculates the maximum magnitude in the spectrum data.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private float CalculateMaxMagnitude(float[] spectrum)
        {
            // Use SIMD where available for bulk operations
            if (Vector.IsHardwareAccelerated && spectrum.Length >= Vector<float>.Count)
            {
                float maxMagnitude = 0f;
                int i = 0;

                // Process vectors
                int vectorCount = spectrum.Length / Vector<float>.Count;
                Vector<float> maxVec = Vector<float>.Zero;

                for (; i < vectorCount * Vector<float>.Count; i += Vector<float>.Count)
                {
                    Vector<float> vec = new Vector<float>(spectrum, i);
                    maxVec = Vector.Max(maxVec, vec);
                }

                // Find max from vector
                for (int j = 0; j < Vector<float>.Count; j++)
                {
                    maxMagnitude = Math.Max(maxMagnitude, maxVec[j]);
                }

                // Process remaining elements
                for (; i < spectrum.Length; i++)
                {
                    maxMagnitude = Math.Max(maxMagnitude, spectrum[i]);
                }

                return maxMagnitude;
            }
            else
            {
                // Fallback for non-SIMD
                float maxMagnitude = 0f;
                for (int i = 0; i < spectrum.Length; i++)
                {
                    if (spectrum[i] > maxMagnitude)
                        maxMagnitude = spectrum[i];
                }
                return maxMagnitude;
            }
        }

        /// <summary>
        /// Creates smooth paths for the wave and its fill.
        /// </summary>
        private void CreateWavePaths(List<SKPoint> points, SKImageInfo info, float yBaseline, SKPath wavePath, SKPath fillPath)
        {
            // Reset paths
            wavePath.Reset();
            fillPath.Reset();

            // Build wave path with optimized number of points
            wavePath.MoveTo(points[0]);

            // Use quadratic Bezier curves for smoother appearance
            for (int i = 1; i < points.Count - 2; i += 1)
            {
                float x1 = points[i].X;
                float y1 = points[i].Y;
                float x2 = points[i + 1].X;
                float y2 = points[i + 1].Y;
                float xMid = (x1 + x2) / 2;
                float yMid = (y1 + y2) / 2;

                wavePath.QuadTo(x1, y1, xMid, yMid);
            }

            if (points.Count >= 2)
            {
                wavePath.LineTo(points[points.Count - 1]);
            }

            // Build fill path by reusing wave path
            fillPath.AddPath(wavePath);
            fillPath.LineTo(info.Width, yBaseline);
            fillPath.LineTo(0, yBaseline);
            fillPath.Close();
        }

        /// <summary>
        /// Renders the gradient fill below the wave.
        /// </summary>
        private void RenderFillGradient(
            SKCanvas canvas,
            SKPaint basePaint,
            float maxMagnitude,
            SKImageInfo info,
            float yBaseline,
            SKPath fillPath)
        {
            using var fillPaint = _paintPool.Get();
            fillPaint.Style = SKPaintStyle.Fill;
            fillPaint.IsAntialias = _useAntiAlias;

            // Optimize gradient by using fewer color stops
            SKColor[] colors = new SKColor[3];
            colors[0] = basePaint.Color.WithAlpha((byte)(255 * Constants.FILL_OPACITY * maxMagnitude));
            colors[1] = basePaint.Color.WithAlpha((byte)(255 * Constants.FILL_OPACITY * maxMagnitude * 0.5f));
            colors[2] = SKColors.Transparent;

            float[] colorPositions = { 0.0f, 0.7f, 1.0f };

            fillPaint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, Constants.EDGE_OFFSET),
                new SKPoint(0, yBaseline),
                colors,
                colorPositions,
                SKShaderTileMode.Clamp);

            // Render path using simple Draw method - don't use specialized sampling
            canvas.DrawPath(fillPath, fillPaint);
        }

        /// <summary>
        /// Renders a glow effect for high intensity waves.
        /// </summary>
        private void RenderGlowEffect(
            SKCanvas canvas,
            SKPaint basePaint,
            float maxMagnitude,
            int barCount,
            SKPath wavePath)
        {
            if (!_useAdvancedEffects) return;

            // Optimize blur radius calculation
            float blurRadius = Math.Min(Constants.MAX_BLUR_RADIUS, 10f / MathF.Sqrt(barCount));

            using var glowPaint = _paintPool.Get();
            glowPaint.Style = SKPaintStyle.Stroke;
            glowPaint.StrokeWidth = Constants.DEFAULT_LINE_WIDTH * 2.0f;
            glowPaint.IsAntialias = _useAntiAlias;
            glowPaint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, blurRadius);
            glowPaint.Color = basePaint.Color.WithAlpha((byte)(255 * Constants.GLOW_INTENSITY * maxMagnitude));

            // Render path using simple Draw method
            canvas.DrawPath(wavePath, glowPaint);
        }

        /// <summary>
        /// Renders the main gradient line of the wave.
        /// </summary>
        private void RenderLineGradient(
            SKCanvas canvas,
            List<SKPoint> points,
            SKPaint basePaint,
            SKImageInfo info,
            SKPath wavePath)
        {
            float saturation = _isOverlayActive ?
                Constants.OVERLAY_GRADIENT_SATURATION :
                Constants.LINE_GRADIENT_SATURATION;

            float lightness = _isOverlayActive ?
                Constants.OVERLAY_GRADIENT_LIGHTNESS :
                Constants.LINE_GRADIENT_LIGHTNESS;

            // Optimize color array allocation based on quality
            int colorSteps = _quality == RenderQuality.Low ?
                Math.Max(2, points.Count / 4) :
                Math.Min(points.Count, _pointCount);

            var lineColors = new SKColor[colorSteps];
            var positions = new float[colorSteps];

            // Generate colors efficiently
            for (int i = 0; i < colorSteps; i++)
            {
                float normalizedValue = (float)i / (colorSteps - 1);
                int pointIndex = (int)(normalizedValue * (points.Count - 1));

                float segmentMagnitude = 1.0f - (points[pointIndex].Y - Constants.EDGE_OFFSET) /
                    (info.Height - 2 * Constants.EDGE_OFFSET);

                lineColors[i] = SKColor.FromHsl(
                    normalizedValue * 360,
                    saturation,
                    lightness,
                    (byte)(255 * Math.Min(0.6f + segmentMagnitude * 0.4f, 1.0f)));

                positions[i] = normalizedValue;
            }

            using var gradientPaint = _paintPool.Get();
            gradientPaint.Style = SKPaintStyle.Stroke;
            gradientPaint.StrokeWidth = Constants.DEFAULT_LINE_WIDTH;
            gradientPaint.IsAntialias = _useAntiAlias;
            gradientPaint.StrokeCap = SKStrokeCap.Round;
            gradientPaint.StrokeJoin = SKStrokeJoin.Round;
            gradientPaint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(info.Width, 0),
                lineColors,
                positions,
                SKShaderTileMode.Clamp);

            // Render path using simple Draw method
            canvas.DrawPath(wavePath, gradientPaint);
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
                SmartLogger.Safe(() =>
                {
                    // Dispose cached resources
                    _cachedBackground?.Dispose();
                    _cachedBackground = null;

                    // Dispose paths
                    _wavePath?.Dispose();
                    _fillPath?.Dispose();

                    // Dispose object pools
                    _pathPool?.Dispose();
                    _paintPool?.Dispose();

                    // Dispose synchronization primitives
                    _renderSemaphore?.Dispose();

                    // Clean up cached data
                    _previousSpectrum = null;
                    _processedSpectrum = null;
                    _cachedPoints = null;

                    // Call base implementation
                    base.Dispose();
                }, new SmartLogger.ErrorHandlingOptions
                {
                    Source = $"{Constants.LOG_PREFIX}.Dispose",
                    ErrorMessage = "Error during disposal"
                });

                _disposed = true;
                SmartLogger.Log(LogLevel.Debug, Constants.LOG_PREFIX, "Disposed");
            }
        }
        #endregion
    }
}