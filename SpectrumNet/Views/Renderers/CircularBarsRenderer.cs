#nullable enable

using SpectrumNet.Service.Enums;

namespace SpectrumNet.Views.Renderers
{
    /// <summary>
    /// Renderer that visualizes spectrum data as bars arranged in a circular pattern,
    /// where each bar extends from an inner circle.
    /// </summary>
    public sealed class CircularBarsRenderer : BaseSpectrumRenderer
    {
        #region Constants
        private static class Constants
        {
            // Logging
            public const string LOG_PREFIX = "CircularBarsRenderer";

            // Layout parameters
            public const float RADIUS_PROPORTION = 0.8f;          // Main radius proportion of min(width, height)
            public const float INNER_RADIUS_FACTOR = 0.9f;        // Inner circle radius factor
            public const float BAR_SPACING_FACTOR = 0.7f;         // Factor for spacing between bars

            // Bar properties
            public const float MIN_STROKE_WIDTH = 2f;             // Minimum width of bars
            public const float SPECTRUM_MULTIPLIER = 0.5f;        // Scale factor for bar length
            public const float MIN_MAGNITUDE_THRESHOLD = 0.01f;   // Minimum magnitude to render
            public const float MAX_BAR_HEIGHT = 1.5f;             // Maximum bar height multiplier
            public const float MIN_BAR_HEIGHT = 0.01f;            // Minimum bar height

            // Effect settings
            public const float GLOW_RADIUS = 3f;                  // Blur radius for glow effect
            public const float GLOW_INTENSITY = 0.4f;             // Intensity of glow effect
            public const float GLOW_THRESHOLD = 0.6f;             // Threshold for applying glow

            // Highlight settings
            public const float HIGHLIGHT_ALPHA = 0.7f;            // Alpha for highlight effect
            public const float HIGHLIGHT_POSITION = 0.7f;         // Position of highlight on bar (0-1)
            public const float HIGHLIGHT_INTENSITY = 0.5f;        // Intensity of highlight effect
            public const float HIGHLIGHT_THRESHOLD = 0.4f;        // Threshold for applying highlight

            // Inner circle properties
            public const byte INNER_CIRCLE_ALPHA = 80;            // Alpha for inner circle

            // Performance settings
            public const int PARALLEL_BATCH_SIZE = 32;            // Batch size for parallel processing
            public const int DEFAULT_PATH_POOL_SIZE = 8;          // Default size for path pool
        }
        #endregion

        #region Structures
        /// <summary>
        /// Configuration parameters for bar rendering.
        /// </summary>
        public readonly record struct RenderConfig(float BarWidth, float BarSpacing, int BarCount);
        #endregion

        #region Fields
        private static readonly Lazy<CircularBarsRenderer> _instance = new(() => new CircularBarsRenderer());

        // Bar vectors for angle calculations
        private Vector2[]? _barVectors;
        private int _previousBarCount;

        // Pooled resources
        private readonly SKPath _pathPool = new();
        private readonly SKPathPool _barPathPool;

        // Quality-dependent settings
        private new bool _useAntiAlias = true;
        private SKSamplingOptions _samplingOptions = new(SKFilterMode.Linear, SKMipmapMode.Linear);
        private new bool _useAdvancedEffects = true;

        // Thread safety
        private readonly object _renderLock = new();
        private new bool _disposed;
        #endregion

        #region Singleton Pattern
        /// <summary>
        /// Private constructor to enforce Singleton pattern.
        /// </summary>
        private CircularBarsRenderer()
        {
            _barPathPool = new SKPathPool(Constants.DEFAULT_PATH_POOL_SIZE);
        }

        /// <summary>
        /// Gets the singleton instance of the circular bars renderer.
        /// </summary>
        public static CircularBarsRenderer GetInstance() => _instance.Value;
        #endregion

        #region Initialization and Configuration
        /// <summary>
        /// Initializes the circular bars renderer and prepares rendering resources.
        /// </summary>
        public override void Initialize()
        {
            Safe(() =>
            {
                base.Initialize();

                // Apply initial quality settings
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

                // Apply quality settings if changed
                if (_quality != quality)
                {
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
                        _useAdvancedEffects = false;
                        break;

                    case RenderQuality.Medium:
                        _useAntiAlias = true;
                        _samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                        _useAdvancedEffects = true;
                        break;

                    case RenderQuality.High:
                        _useAntiAlias = true;
                        _samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                        _useAdvancedEffects = true;
                        break;
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
        /// Renders the circular bars visualization on the canvas using spectrum data.
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

            (float[] processedSpectrum, int count) processedData = (Array.Empty<float>(), 0);

            Safe(() =>
            {
                processedData = ProcessSpectrumInternal(spectrum!, barCount);
            }, new ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.ProcessSpectrum",
                ErrorMessage = "Error processing spectrum"
            });

            if (processedData.count == 0)
            {
                drawPerformanceInfo?.Invoke(canvas!, info);
                return;
            }

            // Calculate layout parameters
            float centerX = info.Width / 2f;
            float centerY = info.Height / 2f;
            float mainRadius = MathF.Min(centerX, centerY) * Constants.RADIUS_PROPORTION;
            float adjustedBarWidth = AdjustBarWidthForBarCount(
                barWidth, processedData.count, MathF.Min(info.Width, info.Height));

            // Quick reject if not visible
            SKRect renderBounds = new(
                centerX - mainRadius,
                centerY - mainRadius,
                centerX + mainRadius,
                centerY + mainRadius);

            if (canvas!.QuickReject(renderBounds))
            {
                drawPerformanceInfo?.Invoke(canvas, info);
                return;
            }

            Safe(() =>
            {
                lock (_renderLock)
                {
                    RenderCircularBars(
                        canvas,
                        processedData.processedSpectrum,
                        processedData.count,
                        centerX,
                        centerY,
                        mainRadius,
                        adjustedBarWidth,
                        paint!);
                }
            }, new ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.Render",
                ErrorMessage = "Error during rendering"
            });

            drawPerformanceInfo?.Invoke(canvas!, info);
        }

        /// <summary>
        /// Processes spectrum data for visualization.
        /// </summary>
        private (float[] processedSpectrum, int count) ProcessSpectrumInternal(
            float[] spectrum,
            int barCount,
            CancellationToken ct = default)
        {
            bool acquired = false;
            try
            {
                acquired = _spectrumSemaphore.Wait(0);

                if (acquired)
                {
                    int targetCount = Min(spectrum.Length, barCount);
                    _processedSpectrum = SmoothSpectrum(
                        ScaleSpectrum(spectrum, targetCount, spectrum.Length),
                        targetCount);
                    EnsureBarVectors(targetCount);
                }

                float[] result = _processedSpectrum ??
                                ScaleSpectrum(spectrum, barCount, spectrum.Length);

                return (result, result.Length);
            }
            finally
            {
                if (acquired)
                    _spectrumSemaphore.Release();
            }
        }

        /// <summary>
        /// Adjusts bar width based on the number of bars and available space.
        /// </summary>
        [MethodImpl(AggressiveInlining)]
        private float AdjustBarWidthForBarCount(float barWidth, int barCount, float minDimension)
        {
            // Calculate maximum width that would fit all bars around the circle
            float maxPossibleWidth = 2 * MathF.PI * Constants.RADIUS_PROPORTION * minDimension / 2 /
                                     barCount * Constants.BAR_SPACING_FACTOR;

            // Use the smaller of the provided width or maximum possible
            return MathF.Max(MathF.Min(barWidth, maxPossibleWidth), Constants.MIN_STROKE_WIDTH);
        }

        /// <summary>
        /// Renders the circular bars visualization with all effects.
        /// </summary>
        private void RenderCircularBars(
            SKCanvas canvas,
            float[] spectrum,
            int barCount,
            float centerX,
            float centerY,
            float mainRadius,
            float barWidth,
            SKPaint basePaint)
        {
            // Draw inner circle
            using var innerCirclePaint = new SKPaint
            {
                IsAntialias = _useAntiAlias,
                Style = Stroke,
                Color = basePaint.Color.WithAlpha(Constants.INNER_CIRCLE_ALPHA),
                StrokeWidth = barWidth * 0.5f
            };

            canvas.DrawCircle(
                centerX,
                centerY,
                mainRadius * Constants.INNER_RADIUS_FACTOR,
                innerCirclePaint);

            // Ensure bar vectors are calculated
            EnsureBarVectors(barCount);

            // Draw effects and bars
            if (_useAdvancedEffects)
            {
                RenderGlowEffects(canvas, spectrum, barCount, centerX, centerY, mainRadius, barWidth, basePaint);
            }

            RenderMainBars(canvas, spectrum, barCount, centerX, centerY, mainRadius, barWidth, basePaint);

            if (_useAdvancedEffects)
            {
                RenderHighlights(canvas, spectrum, barCount, centerX, centerY, mainRadius, barWidth, basePaint);
            }
        }
        #endregion

        #region Bar Vectors
        /// <summary>
        /// Ensures that bar vectors are calculated for the current bar count.
        /// </summary>
        private void EnsureBarVectors(int barCount)
        {
            Safe(() =>
            {
                if (_barVectors == null || _barVectors.Length != barCount || _previousBarCount != barCount)
                {
                    _barVectors = new Vector2[barCount];
                    float angleStep = 2 * MathF.PI / barCount;

                    for (int i = 0; i < barCount; i++)
                    {
                        _barVectors[i] = new Vector2(
                            MathF.Cos(angleStep * i),
                            MathF.Sin(angleStep * i));
                    }

                    _previousBarCount = barCount;
                }
            }, new ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.EnsureBarVectors",
                ErrorMessage = "Error calculating bar vectors"
            });
        }
        #endregion

        #region Bar Rendering Methods
        /// <summary>
        /// Renders glow effects for high intensity bars.
        /// </summary>
        private void RenderGlowEffects(
            SKCanvas canvas,
            float[] spectrum,
            int barCount,
            float centerX,
            float centerY,
            float mainRadius,
            float barWidth,
            SKPaint basePaint)
        {
            using var batchPath = new SKPath();

            for (int i = 0; i < barCount; i++)
            {
                // Skip bars below glow threshold
                if (spectrum[i] <= Constants.GLOW_THRESHOLD)
                    continue;

                float radius = mainRadius + spectrum[i] * mainRadius * Constants.SPECTRUM_MULTIPLIER;
                var path = _barPathPool.Get();

                AddBarToPath(path, i, centerX, centerY, mainRadius, radius);
                batchPath.AddPath(path);

                _barPathPool.Return(path);
            }

            if (!batchPath.IsEmpty)
            {
                using var glowPaint = new SKPaint
                {
                    IsAntialias = _useAntiAlias,
                    Style = Stroke,
                    Color = basePaint.Color.WithAlpha((byte)(255 * Constants.GLOW_INTENSITY)),
                    StrokeWidth = barWidth * 1.2f,
                    MaskFilter = SKMaskFilter.CreateBlur(Normal, Constants.GLOW_RADIUS)
                };

                canvas.DrawPath(batchPath, glowPaint);
            }
        }

        /// <summary>
        /// Renders the main bars for all spectrum values.
        /// </summary>
        private void RenderMainBars(
            SKCanvas canvas,
            float[] spectrum,
            int barCount,
            float centerX,
            float centerY,
            float mainRadius,
            float barWidth,
            SKPaint basePaint)
        {
            using var batchPath = new SKPath();

            for (int i = 0; i < barCount; i++)
            {
                // Skip insignificant magnitudes
                if (spectrum[i] < Constants.MIN_MAGNITUDE_THRESHOLD)
                    continue;

                float radius = mainRadius + spectrum[i] * mainRadius * Constants.SPECTRUM_MULTIPLIER;
                var path = _barPathPool.Get();

                AddBarToPath(path, i, centerX, centerY, mainRadius, radius);
                batchPath.AddPath(path);

                _barPathPool.Return(path);
            }

            if (!batchPath.IsEmpty)
            {
                using var barPaint = new SKPaint
                {
                    IsAntialias = _useAntiAlias,
                    Style = Stroke,
                    StrokeCap = SKStrokeCap.Round,
                    Color = basePaint.Color,
                    StrokeWidth = barWidth
                };

                canvas.DrawPath(batchPath, barPaint);
            }
        }

        /// <summary>
        /// Renders highlight effects on top of bars.
        /// </summary>
        private void RenderHighlights(
            SKCanvas canvas,
            float[] spectrum,
            int barCount,
            float centerX,
            float centerY,
            float mainRadius,
            float barWidth,
            SKPaint basePaint)
        {
            using var batchPath = new SKPath();

            for (int i = 0; i < barCount; i++)
            {
                // Skip bars below highlight threshold
                if (spectrum[i] <= Constants.HIGHLIGHT_THRESHOLD)
                    continue;

                float radius = mainRadius + spectrum[i] * mainRadius * Constants.SPECTRUM_MULTIPLIER;
                float innerPoint = mainRadius + (radius - mainRadius) * Constants.HIGHLIGHT_POSITION;

                var path = _barPathPool.Get();
                AddBarToPath(path, i, centerX, centerY, innerPoint, radius);
                batchPath.AddPath(path);

                _barPathPool.Return(path);
            }

            if (!batchPath.IsEmpty)
            {
                using var highlightPaint = new SKPaint
                {
                    IsAntialias = _useAntiAlias,
                    Style = Stroke,
                    Color = SKColors.White.WithAlpha((byte)(255 * Constants.HIGHLIGHT_INTENSITY)),
                    StrokeWidth = barWidth * 0.6f
                };

                canvas.DrawPath(batchPath, highlightPaint);
            }
        }

        /// <summary>
        /// Adds a single bar to the path.
        /// </summary>
        private void AddBarToPath(
            SKPath path,
            int index,
            float centerX,
            float centerY,
            float innerRadius,
            float outerRadius)
        {
            if (_barVectors == null)
                return;

            Vector2 vector = _barVectors[index];

            path.MoveTo(
                centerX + innerRadius * vector.X,
                centerY + innerRadius * vector.Y);

            path.LineTo(
                centerX + outerRadius * vector.X,
                centerY + outerRadius * vector.Y);
        }
        #endregion

        #region Path Pooling
        /// <summary>
        /// Custom pool implementation for SKPath objects.
        /// </summary>
        private class SKPathPool : IDisposable
        {
            private readonly List<SKPath> _paths = new();
            private readonly List<SKPath> _inUse = new();
            private readonly object _lockObject = new();
            private bool _disposed;

            public SKPathPool(int capacity)
            {
                for (int i = 0; i < capacity; i++)
                    _paths.Add(new SKPath());
            }

            public SKPath Get()
            {
                lock (_lockObject)
                {
                    if (_disposed)
                        throw new ObjectDisposedException(nameof(SKPathPool));

                    SKPath path;
                    if (_paths.Count > 0)
                    {
                        path = _paths[_paths.Count - 1];
                        _paths.RemoveAt(_paths.Count - 1);
                    }
                    else
                    {
                        path = new SKPath();
                    }

                    path.Reset();
                    _inUse.Add(path);
                    return path;
                }
            }

            public void Return(SKPath path)
            {
                lock (_lockObject)
                {
                    if (_disposed)
                        return;

                    if (_inUse.Remove(path))
                        _paths.Add(path);
                }
            }

            public void Dispose()
            {
                lock (_lockObject)
                {
                    if (!_disposed)
                    {
                        foreach (var path in _paths)
                            path.Dispose();

                        foreach (var path in _inUse)
                            path.Dispose();

                        _paths.Clear();
                        _inUse.Clear();
                        _disposed = true;
                    }
                }
            }
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
                    _pathPool?.Dispose();
                    _barPathPool?.Dispose();

                    base.Dispose();

                    _disposed = true;
                    Log(LogLevel.Debug, Constants.LOG_PREFIX, "Disposed");
                }, new ErrorHandlingOptions
                {
                    Source = $"{Constants.LOG_PREFIX}.Dispose",
                    ErrorMessage = "Error during disposal"
                });
            }
        }
        #endregion
    }
}