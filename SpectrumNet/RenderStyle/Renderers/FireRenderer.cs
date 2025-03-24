#nullable enable

namespace SpectrumNet
{
    /// <summary>
    /// Renderer that visualizes spectrum data as animated fire effect.
    /// </summary>
    public sealed class FireRenderer : BaseSpectrumRenderer
    {
        #region Constants
        private static class Constants
        {
            // Logging
            public const string LOG_PREFIX = "FireRenderer";

            // Time and animation
            public const float TIME_STEP = 0.016f;              // Time increment per frame (~60 FPS)
            public const float DECAY_RATE = 0.08f;              // How quickly frequencies decrease when no longer present

            // Flame shape parameters
            public const float CONTROL_POINT_PROPORTION = 0.4f;  // Controls flame curvature 
            public const float RANDOM_OFFSET_PROPORTION = 0.5f;  // How much randomness in flame shape
            public const float RANDOM_OFFSET_CENTER = 0.25f;     // Center point for random distribution
            public const float FLAME_BOTTOM_PROPORTION = 0.25f;  // Height of flame base relative to total height
            public const float FLAME_BOTTOM_MAX = 6f;            // Maximum pixel height for flame base
            public const float MIN_BOTTOM_ALPHA = 0.3f;          // Minimum opacity for flame base

            // Wave animation parameters  
            public const float WAVE_SPEED = 2.0f;               // Speed of wave animation
            public const float WAVE_AMPLITUDE = 0.2f;           // Height of wave animation
            public const float HORIZONTAL_WAVE_FACTOR = 0.15f;   // Horizontal movement factor

            // Bezier curve control points
            public const float CUBIC_CONTROL_POINT1 = 0.33f;     // First control point position (x-axis)
            public const float CUBIC_CONTROL_POINT2 = 0.66f;     // Second control point position (x-axis)

            // Opacity animation parameters
            public const float OPACITY_WAVE_SPEED = 3.0f;        // Speed of opacity pulsing
            public const float OPACITY_PHASE_SHIFT = 0.2f;       // Phase offset between flames
            public const float OPACITY_WAVE_AMPLITUDE = 0.1f;    // Amount of opacity variation
            public const float OPACITY_BASE = 0.9f;              // Base opacity level

            // Positioning
            public const float POSITION_PHASE_SHIFT = 0.5f;      // Offset between flame positions
            public const int MIN_BAR_COUNT = 10;                 // Minimum number of flame columns

            // Effects
            public const float GLOW_INTENSITY = 0.3f;            // Intensity of glow effect
            public const float HIGH_INTENSITY_THRESHOLD = 0.7f;  // Threshold to trigger glow effects

            // Quality settings (Low)
            public const float GLOW_RADIUS_LOW = 1.5f;           // Blur radius for glow in low quality
            public const int MAX_DETAIL_LEVEL_LOW = 2;           // Detail level for low quality

            // Quality settings (Medium)
            public const float GLOW_RADIUS_MEDIUM = 3f;          // Blur radius for glow in medium quality
            public const int MAX_DETAIL_LEVEL_MEDIUM = 4;        // Detail level for medium quality

            // Quality settings (High) 
            public const float GLOW_RADIUS_HIGH = 5f;            // Blur radius for glow in high quality
            public const int MAX_DETAIL_LEVEL_HIGH = 8;          // Detail level for high quality

            // Performance optimization
            public const int SPECTRUM_PROCESSING_CHUNK_SIZE = 128; // Batch size for parallel processing
        }
        #endregion

        #region Fields
        private static readonly Lazy<FireRenderer> _instance = new(() => new FireRenderer());

        // Object pools for efficient resource management
        private readonly ObjectPool<SKPath> _pathPool = new(() => new SKPath(), path => path.Reset(), 10);
        private readonly ObjectPool<SKPaint> _paintPool = new(() => new SKPaint(), paint => paint.Reset(), 5);

        // Rendering state
        private bool _isOverlayActive;
        private float _time;
        private float[] _previousSpectrum = Array.Empty<float>();
        private float[]? _processedSpectrum;
        private readonly Random _random = new();

        // Quality-dependent settings
        private new bool _useAntiAlias = true;
        private SKSamplingOptions _samplingOptions = new(SKFilterMode.Linear, SKMipmapMode.Linear);
        private new bool _useAdvancedEffects = true;
        private int _sampleCount = 2;
        private float _pathSimplification = 0.2f;
        private int _maxDetailLevel = 4;

        // Cached resources
        private SKPicture? _cachedBasePicture;
        private readonly SemaphoreSlim _renderSemaphore = new(1, 1);
        private readonly object _spectrumLock = new();
        private new bool _disposed;
        #endregion

        #region Singleton Pattern
        private FireRenderer() { }
        public static FireRenderer GetInstance() => _instance.Value;
        #endregion

        #region Initialization and Configuration
        /// <summary>
        /// Initializes the fire renderer and prepares resources for rendering.
        /// </summary>
        public override void Initialize()
        {
            SmartLogger.Safe(() =>
            {
                base.Initialize();
                _time = 0f;

                // Initial quality settings
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
                        _sampleCount = 1;
                        _pathSimplification = 0.5f;
                        _maxDetailLevel = Constants.MAX_DETAIL_LEVEL_LOW;
                        break;

                    case RenderQuality.Medium:
                        _useAntiAlias = true;
                        _samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                        _useAdvancedEffects = true;
                        _sampleCount = 2;
                        _pathSimplification = 0.2f;
                        _maxDetailLevel = Constants.MAX_DETAIL_LEVEL_MEDIUM;
                        break;

                    case RenderQuality.High:
                        _useAntiAlias = true;
                        _samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                        _useAdvancedEffects = true;
                        _sampleCount = 4;
                        _pathSimplification = 0.0f;
                        _maxDetailLevel = Constants.MAX_DETAIL_LEVEL_HIGH;
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
                _cachedBasePicture?.Dispose();
                _cachedBasePicture = null;
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.InvalidateCachedResources",
                ErrorMessage = "Failed to invalidate cached resources"
            });
        }
        #endregion

        #region Rendering
        /// <summary>
        /// Renders the fire visualization on the canvas using spectrum data.
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
                    semaphoreAcquired = _renderSemaphore.Wait(0);

                    if (semaphoreAcquired)
                    {
                        _time += Constants.TIME_STEP;
                        ProcessSpectrumData(spectrum!, barCount);
                    }

                    float[] renderSpectrum;
                    lock (_spectrumLock)
                    {
                        renderSpectrum = _processedSpectrum ??
                                        ProcessSpectrumSynchronously(spectrum!, barCount);
                    }

                    using var renderScope = new RenderScope(
                        this, canvas!, renderSpectrum, info, barWidth, barSpacing, barCount, paint!);
                    renderScope.Execute(drawPerformanceInfo);
                }
                finally
                {
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
        #endregion

        #region Spectrum Processing
        /// <summary>
        /// Processes spectrum data for visualization in a thread-safe manner.
        /// </summary>
        private void ProcessSpectrumData(float[] spectrum, int barCount)
        {
            SmartLogger.Safe(() =>
            {
                EnsureSpectrumBuffer(spectrum.Length);

                int spectrumLength = spectrum.Length;
                int actualBarCount = Math.Min(spectrumLength, barCount);

                // Process only at the quality-dependent sample rate
                float[] scaledSpectrum = ScaleSpectrum(spectrum, actualBarCount, spectrumLength);

                UpdatePreviousSpectrum(spectrum);

                lock (_spectrumLock)
                {
                    _processedSpectrum = scaledSpectrum;
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
            return ScaleSpectrum(spectrum, actualBarCount, spectrumLength);
        }

        /// <summary>
        /// Ensures the spectrum buffer is of the correct size.
        /// </summary>
        private void EnsureSpectrumBuffer(int length)
        {
            SmartLogger.Safe(() =>
            {
                if (_previousSpectrum.Length != length)
                {
                    _previousSpectrum = new float[length];
                }
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.EnsureSpectrumBuffer",
                ErrorMessage = "Error ensuring spectrum buffer"
            });
        }

        /// <summary>
        /// Scales the spectrum data to the target count of bars.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private float[] ScaleSpectrum(float[] spectrum, int targetCount, int spectrumLength)
        {
            float[] scaledSpectrum = new float[targetCount];
            float blockSize = (float)spectrumLength / targetCount;
            float[] localSpectrum = spectrum;

            if (Vector.IsHardwareAccelerated && spectrumLength >= Vector<float>.Count)
            {
                int chunkSize = Math.Min(Constants.SPECTRUM_PROCESSING_CHUNK_SIZE, targetCount);

                Parallel.For(0, (targetCount + chunkSize - 1) / chunkSize, chunkIndex =>
                {
                    int startIdx = chunkIndex * chunkSize;
                    int endIdx = Math.Min(startIdx + chunkSize, targetCount);

                    for (int i = startIdx; i < endIdx; i++)
                    {
                        float sum = 0;
                        int start = (int)(i * blockSize);
                        int end = (int)((i + 1) * blockSize);
                        end = Math.Min(end, spectrumLength);

                        if (end - start >= Vector<float>.Count)
                        {
                            int vectorizableEnd = start + ((end - start) / Vector<float>.Count) * Vector<float>.Count;

                            Vector<float> sumVector = Vector<float>.Zero;
                            for (int j = start; j < vectorizableEnd; j += Vector<float>.Count)
                            {
                                sumVector += new Vector<float>(localSpectrum, j);
                            }

                            for (int j = 0; j < Vector<float>.Count; j++)
                            {
                                sum += sumVector[j];
                            }

                            for (int j = vectorizableEnd; j < end; j++)
                            {
                                sum += localSpectrum[j];
                            }
                        }
                        else
                        {
                            for (int j = start; j < end; j++)
                            {
                                sum += localSpectrum[j];
                            }
                        }

                        scaledSpectrum[i] = sum / (end - start);
                    }
                });
            }
            else
            {
                Parallel.For(0, targetCount, i =>
                {
                    float sum = 0;
                    int start = (int)(i * blockSize);
                    int end = (int)((i + 1) * blockSize);
                    end = Math.Min(end, spectrumLength);

                    for (int j = start; j < end; j++)
                        sum += localSpectrum[j];

                    scaledSpectrum[i] = sum / (end - start);
                });
            }

            return scaledSpectrum;
        }

        /// <summary>
        /// Updates the previous spectrum with decay for smooth transitions.
        /// </summary>
        private void UpdatePreviousSpectrum(float[] spectrum)
        {
            SmartLogger.Safe(() =>
            {
                for (int i = 0; i < spectrum.Length; i++)
                {
                    _previousSpectrum[i] = Math.Max(
                        spectrum[i],
                        _previousSpectrum[i] - Constants.DECAY_RATE
                    );
                }
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.UpdatePreviousSpectrum",
                ErrorMessage = "Error updating previous spectrum"
            });
        }
        #endregion

        #region Rendering Implementation
        /// <summary>
        /// Encapsulates a rendering operation for a single frame.
        /// </summary>
        private class RenderScope : IDisposable
        {
            private readonly FireRenderer _renderer;
            private readonly SKCanvas _canvas;
            private readonly float[] _spectrum;
            private readonly SKImageInfo _info;
            private readonly float _barWidth;
            private readonly float _barSpacing;
            private readonly int _barCount;
            private readonly SKPaint _basePaint;
            private readonly SKPaint _workingPaint;
            private readonly SKPaint _glowPaint;
            private readonly float _glowRadius;
            private readonly int _maxDetailLevel;
            private readonly bool _useAdvancedEffects;

            public RenderScope(
                FireRenderer renderer,
                SKCanvas canvas,
                float[] spectrum,
                SKImageInfo info,
                float barWidth,
                float barSpacing,
                int barCount,
                SKPaint basePaint)
            {
                _renderer = renderer;
                _canvas = canvas;
                _spectrum = spectrum;
                _info = info;
                _barWidth = barWidth;
                _barSpacing = barSpacing;
                _barCount = barCount;
                _basePaint = basePaint;

                // Initialize working paint
                _workingPaint = renderer._paintPool.Get();
                _workingPaint.Color = basePaint.Color;
                _workingPaint.Style = basePaint.Style;
                _workingPaint.StrokeWidth = basePaint.StrokeWidth;
                _workingPaint.IsStroke = basePaint.IsStroke;
                _workingPaint.IsAntialias = renderer._useAntiAlias;
                _workingPaint.ImageFilter = basePaint.ImageFilter;
                _workingPaint.Shader = basePaint.Shader;

                // Initialize glow paint
                _glowPaint = renderer._paintPool.Get();
                _glowPaint.Color = basePaint.Color;
                _glowPaint.Style = basePaint.Style;
                _glowPaint.StrokeWidth = basePaint.StrokeWidth;
                _glowPaint.IsStroke = basePaint.IsStroke;
                _glowPaint.IsAntialias = renderer._useAntiAlias;

                // Configure glow radius based on quality
                switch (renderer._quality)
                {
                    case RenderQuality.Low:
                        _glowRadius = Constants.GLOW_RADIUS_LOW;
                        break;
                    case RenderQuality.Medium:
                        _glowRadius = Constants.GLOW_RADIUS_MEDIUM;
                        break;
                    case RenderQuality.High:
                        _glowRadius = Constants.GLOW_RADIUS_HIGH;
                        break;
                    default:
                        _glowRadius = Constants.GLOW_RADIUS_MEDIUM;
                        break;
                }

                _glowPaint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, _glowRadius);
                _maxDetailLevel = renderer._maxDetailLevel;
                _useAdvancedEffects = renderer._useAdvancedEffects;
            }

            public void Execute(Action<SKCanvas, SKImageInfo>? drawPerformanceInfo)
            {
                var actualBarCount = _spectrum.Length;
                var totalBarWidth = CalculateTotalBarWidth(actualBarCount);

                RenderFlames(actualBarCount, totalBarWidth);

                drawPerformanceInfo?.Invoke(_canvas, _info);
            }

            private float CalculateTotalBarWidth(int actualBarCount)
            {
                float totalBarWidth = _barWidth + _barSpacing;
                return actualBarCount < Constants.MIN_BAR_COUNT
                    ? totalBarWidth * (float)Constants.MIN_BAR_COUNT / actualBarCount
                    : totalBarWidth;
            }

            private void RenderFlames(int actualBarCount, float totalBarWidth)
            {
                _canvas.Save();
                _canvas.ClipRect(new SKRect(0, 0, _info.Width, _info.Height));

                var flameGroups = new List<(List<FlameParameters> Flames, float Intensity)>();
                var currentGroup = new List<FlameParameters>();
                float currentIntensity = 0;

                for (int i = 0; i < actualBarCount; i++)
                {
                    if (i >= _barCount) break;

                    var spectrumValue = _spectrum[i];
                    if (spectrumValue < 0.01f) continue;

                    var flameParams = CalculateFlameParameters(i, totalBarWidth, spectrumValue);
                    float intensity = flameParams.CurrentHeight / flameParams.CanvasHeight;

                    if (currentGroup.Count > 0 && Math.Abs(intensity - currentIntensity) > 0.2f)
                    {
                        flameGroups.Add((currentGroup, currentIntensity));
                        currentGroup = new List<FlameParameters>();
                    }

                    currentGroup.Add(flameParams);
                    currentIntensity = intensity;
                }

                if (currentGroup.Count > 0)
                    flameGroups.Add((currentGroup, currentIntensity));

                foreach (var group in flameGroups.OrderBy(g => g.Intensity))
                {
                    foreach (var flameParams in group.Flames)
                    {
                        RenderSingleFlame(flameParams);
                    }
                }

                _canvas.Restore();
            }

            private FlameParameters CalculateFlameParameters(int index, float totalBarWidth, float spectrumValue)
            {
                float x = index * totalBarWidth;
                float waveOffset = (float)Math.Sin(_renderer._time * Constants.WAVE_SPEED + index * Constants.POSITION_PHASE_SHIFT)
                    * Constants.WAVE_AMPLITUDE;
                float currentHeight = spectrumValue * _info.Height * (1 + waveOffset);
                float previousHeight = _renderer._previousSpectrum.Length > index ?
                                      _renderer._previousSpectrum[index] * _info.Height : 0;
                float baselinePosition = _info.Height;

                return new FlameParameters(
                    x, currentHeight, previousHeight,
                    _barWidth, _info.Height, index,
                    baselinePosition
                );
            }

            private void RenderSingleFlame(FlameParameters parameters)
            {
                var path = _renderer._pathPool.Get();

                try
                {
                    var (flameTop, flameBottom) = CalculateFlameVerticalPositions(parameters);
                    var x = CalculateHorizontalPosition(parameters);

                    // Skip rendering if the flame would be too small to be visible
                    if (flameBottom - flameTop < 1)
                    {
                        return;
                    }

                    // Only render glow for high intensity flames and when advanced effects are enabled
                    if (_useAdvancedEffects &&
                        parameters.CurrentHeight / parameters.CanvasHeight > Constants.HIGH_INTENSITY_THRESHOLD)
                    {
                        RenderFlameGlow(path, x, flameTop, flameBottom, parameters);
                    }

                    RenderFlameBase(path, x, flameBottom);
                    RenderFlameBody(path, x, flameTop, flameBottom, parameters);
                }
                finally
                {
                    _renderer._pathPool.Return(path);
                }
            }

            private (float flameTop, float flameBottom) CalculateFlameVerticalPositions(FlameParameters parameters)
            {
                float flameTop = parameters.CanvasHeight - Math.Max(parameters.CurrentHeight, parameters.PreviousHeight);
                float flameBottom = parameters.CanvasHeight - Constants.FLAME_BOTTOM_MAX;

                return (flameTop, flameBottom);
            }

            private float CalculateHorizontalPosition(FlameParameters parameters)
            {
                float waveOffset = (float)Math.Sin(_renderer._time * Constants.WAVE_SPEED +
                    parameters.Index * Constants.POSITION_PHASE_SHIFT) *
                    (parameters.BarWidth * Constants.HORIZONTAL_WAVE_FACTOR);
                return parameters.X + waveOffset;
            }

            private void RenderFlameBase(SKPath path, float x, float flameBottom)
            {
                path.Reset();
                path.MoveTo(x, flameBottom);
                path.LineTo(x + _barWidth, flameBottom);

                using var bottomPaint = _renderer._paintPool.Get();
                bottomPaint.Color = _workingPaint.Color.WithAlpha((byte)(255 * Constants.MIN_BOTTOM_ALPHA));
                bottomPaint.Style = _workingPaint.Style;
                bottomPaint.StrokeWidth = _workingPaint.StrokeWidth;
                bottomPaint.IsStroke = _workingPaint.IsStroke;
                bottomPaint.IsAntialias = _workingPaint.IsAntialias;
                bottomPaint.ImageFilter = _workingPaint.ImageFilter;
                bottomPaint.Shader = _workingPaint.Shader;

                _canvas.DrawPath(path, bottomPaint);
            }

            private void RenderFlameGlow(SKPath path, float x, float flameTop, float flameBottom, FlameParameters parameters)
            {
                path.Reset();
                path.MoveTo(x, flameBottom);

                float height = flameBottom - flameTop;
                var controlPoints = CalculateControlPoints(x, flameBottom, height, parameters.BarWidth);

                path.CubicTo(
                    controlPoints.cp1X, controlPoints.cp1Y,
                    controlPoints.cp2X, controlPoints.cp2Y,
                    x + parameters.BarWidth, flameBottom
                );

                float intensity = parameters.CurrentHeight / parameters.CanvasHeight;
                byte glowAlpha = (byte)(255 * intensity * Constants.GLOW_INTENSITY);
                _glowPaint.Color = _glowPaint.Color.WithAlpha(glowAlpha);

                _canvas.DrawPath(path, _glowPaint);
            }

            private void RenderFlameBody(SKPath path, float x, float flameTop, float flameBottom, FlameParameters parameters)
            {
                path.Reset();
                path.MoveTo(x, flameBottom);

                float height = flameBottom - flameTop;
                var controlPoints = CalculateControlPoints(x, flameBottom, height, parameters.BarWidth);

                path.CubicTo(
                    controlPoints.cp1X, controlPoints.cp1Y,
                    controlPoints.cp2X, controlPoints.cp2Y,
                    x + parameters.BarWidth, flameBottom
                );

                UpdatePaintForFlame(parameters);
                _canvas.DrawPath(path, _workingPaint);
            }

            private (float cp1X, float cp1Y, float cp2X, float cp2Y) CalculateControlPoints(
                float x, float flameBottom, float height, float barWidth)
            {
                float cp1Y = flameBottom - height * Constants.CUBIC_CONTROL_POINT1;
                float cp2Y = flameBottom - height * Constants.CUBIC_CONTROL_POINT2;

                // Add randomness based on quality level and detail
                float detailFactor = (float)_maxDetailLevel / Constants.MAX_DETAIL_LEVEL_HIGH;
                float randomnessFactor = detailFactor * Constants.RANDOM_OFFSET_PROPORTION;

                float randomOffset1 = (float)(_renderer._random.NextDouble() *
                    barWidth * randomnessFactor -
                    barWidth * Constants.RANDOM_OFFSET_CENTER);
                float randomOffset2 = (float)(_renderer._random.NextDouble() *
                    barWidth * randomnessFactor -
                    barWidth * Constants.RANDOM_OFFSET_CENTER);

                return (
                    x + barWidth * Constants.CUBIC_CONTROL_POINT1 + randomOffset1,
                    cp1Y,
                    x + barWidth * Constants.CUBIC_CONTROL_POINT2 + randomOffset2,
                    cp2Y
                );
            }

            private void UpdatePaintForFlame(FlameParameters parameters)
            {
                float opacityWave = (float)Math.Sin(_renderer._time * Constants.OPACITY_WAVE_SPEED +
                    parameters.Index * Constants.OPACITY_PHASE_SHIFT) *
                    Constants.OPACITY_WAVE_AMPLITUDE + Constants.OPACITY_BASE;

                byte alpha = (byte)(255 * Math.Min(
                    parameters.CurrentHeight / parameters.CanvasHeight * opacityWave, 1.0f));
                _workingPaint.Color = _workingPaint.Color.WithAlpha(alpha);
            }

            public void Dispose()
            {
                // Return paints to pool instead of disposing
                _renderer._paintPool.Return(_workingPaint);
                _renderer._paintPool.Return(_glowPaint);
            }
        }

        /// <summary>
        /// Parameters for a single flame in the visualization.
        /// </summary>
        private readonly record struct FlameParameters(
            float X,
            float CurrentHeight,
            float PreviousHeight,
            float BarWidth,
            float CanvasHeight,
            int Index,
            float BaselinePosition
        );
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
                    _cachedBasePicture?.Dispose();
                    _cachedBasePicture = null;

                    _pathPool?.Dispose();
                    _paintPool?.Dispose();

                    _renderSemaphore?.Dispose();

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