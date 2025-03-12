#nullable enable

namespace SpectrumNet
{
    public class GradientWaveRenderer : ISpectrumRenderer, IDisposable
    {
        #region Constants
        private const string LogPrefix = "[GradientWaveRenderer] ";

        private static class Constants
        {
            // Layout constants
            public const float Offset = 10f;                  // Edge offset for drawing
            public const float BaselineOffset = 2f;           // Y-offset for the wave baseline
            public const int ExtraPointsCount = 4;            // Number of extra points to add to the wave path

            // Wave smoothing factors
            public const float SmoothingFactorNormal = 0.3f;  // Normal smoothing factor
            public const float SmoothingFactorOverlay = 0.5f; // More aggressive smoothing for overlay mode
            public const float MinMagnitudeThreshold = 0.01f; // Minimum magnitude to render
            public const float MaxSpectrumValue = 1.5f;       // Maximum spectrum value cap

            // Rendering properties
            public const float DefaultLineWidth = 3f;         // Width of main spectrum line
            public const float GlowIntensity = 0.3f;          // Intensity of glow effect
            public const float HighMagnitudeThreshold = 0.7f; // Threshold for extra effects
            public const float FillOpacity = 0.2f;            // Opacity of gradient fill

            // Color properties
            public const float LineGradientSaturation = 100f; // Saturation for normal mode
            public const float LineGradientLightness = 50f;   // Lightness for normal mode
            public const float OverlayGradientSaturation = 100f; // Saturation for overlay mode
            public const float OverlayGradientLightness = 55f;   // Lightness for overlay mode
            public const float MaxBlurRadius = 6f;            // Maximum blur for glow effect
        }
        #endregion

        #region Fields
        private static readonly Lazy<GradientWaveRenderer> _instance =
            new Lazy<GradientWaveRenderer>(() => new GradientWaveRenderer());
        private bool _isInitialized;
        private bool _disposed = false;
        private bool _isOverlayActive;
        private float[]? _previousSpectrum;
        private float[]? _processedSpectrum;
        private List<SKPoint>? _cachedPoints;
        private readonly SemaphoreSlim _spectrumSemaphore = new(1, 1);
        private readonly object _spectrumLock = new();
        private readonly SKPath _wavePath = new();
        private readonly SKPath _fillPath = new();
        private SKPicture? _cachedBackground;
        private SKRect _previousRect;
        private RenderQuality _quality = RenderQuality.Medium;

        // Quality-related fields
        private bool _useAntiAlias = true;
        private SKFilterQuality _filterQuality = SKFilterQuality.Medium;
        private bool _useAdvancedEffects = true;
        private bool _useGpuAcceleration = true;
        private bool _useHighPrecisionColors = true;
        private int _smoothingPasses = 2;

        private float _smoothingFactor = Constants.SmoothingFactorNormal;
        #endregion

        #region Constructor and Initialization
        private GradientWaveRenderer() { }

        public static GradientWaveRenderer GetInstance() => _instance.Value;

        public void Initialize()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(GradientWaveRenderer));
            }

            if (_isInitialized) return;

            SmartLogger.Log(LogLevel.Debug, LogPrefix, "Initialized");
            _isInitialized = true;
        }
        #endregion

        #region Configuration
        public RenderQuality Quality
        {
            get => _quality;
            set
            {
                if (_quality != value)
                {
                    _quality = value;
                    ApplyQualitySettings();
                }
            }
        }

        private void ApplyQualitySettings()
        {
            switch (_quality)
            {
                case RenderQuality.Low:
                    _useAntiAlias = false;
                    _filterQuality = SKFilterQuality.Low;
                    _useAdvancedEffects = false;
                    _useGpuAcceleration = false;
                    _useHighPrecisionColors = false;
                    _smoothingPasses = 1;
                    break;

                case RenderQuality.Medium:
                    _useAntiAlias = true;
                    _filterQuality = SKFilterQuality.Medium;
                    _useAdvancedEffects = true;
                    _useGpuAcceleration = true;
                    _useHighPrecisionColors = true;
                    _smoothingPasses = 2;
                    break;

                case RenderQuality.High:
                    _useAntiAlias = true;
                    _filterQuality = SKFilterQuality.High;
                    _useAdvancedEffects = true;
                    _useGpuAcceleration = true;
                    _useHighPrecisionColors = true;
                    _smoothingPasses = 3;
                    break;
            }

            // Invalidate cached resources to rebuild with new quality settings
            _cachedBackground?.Dispose();
            _cachedBackground = null;
        }

        public void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(GradientWaveRenderer));
            }

            _isOverlayActive = isOverlayActive;
            _smoothingFactor = isOverlayActive ? Constants.SmoothingFactorOverlay : Constants.SmoothingFactorNormal;

            // Apply quality settings
            Quality = quality;
        }
        #endregion

        #region Rendering
        public void Render(
            SKCanvas? canvas,
            float[]? spectrum,
            SKImageInfo info,
            float barWidth,
            float barSpacing,
            int barCount,
            SKPaint? paint,
            Action<SKCanvas, SKImageInfo>? drawPerformanceInfo)
        {
            try
            {
                if (!ValidateRenderParameters(canvas, spectrum, paint))
                    return;

                // Fast path: if canvas can be quickly rejected from clipping, skip rendering
                if (canvas!.QuickReject(new SKRect(0, 0, info.Width, info.Height)))
                    return;

                float[] renderSpectrum;
                List<SKPoint> renderPoints;
                bool semaphoreAcquired = false;
                int actualBarCount = Math.Min(spectrum!.Length, barCount);

                try
                {
                    // Try to acquire semaphore, but don't block if unavailable
                    semaphoreAcquired = _spectrumSemaphore.Wait(0);

                    if (semaphoreAcquired)
                    {
                        // Process spectrum data in background thread when semaphore is acquired
                        float[] scaledSpectrum = ScaleSpectrum(spectrum, actualBarCount);
                        _processedSpectrum = SmoothSpectrum(scaledSpectrum, actualBarCount);
                        _cachedPoints = GenerateOptimizedPoints(_processedSpectrum, info);
                    }

                    lock (_spectrumLock)
                    {
                        if (_processedSpectrum != null && _cachedPoints != null)
                        {
                            renderSpectrum = _processedSpectrum;
                            renderPoints = _cachedPoints;
                        }
                        else
                        {
                            float[] scaledSpectrum = ScaleSpectrum(spectrum, actualBarCount);
                            renderSpectrum = SmoothSpectrum(scaledSpectrum, actualBarCount);
                            renderPoints = GenerateOptimizedPoints(renderSpectrum, info);
                        }
                    }
                }
                finally
                {
                    if (semaphoreAcquired)
                    {
                        _spectrumSemaphore.Release();
                    }
                }

                // Perform actual rendering with obtained data
                RenderOptimizedGradientWave(canvas!, renderPoints, renderSpectrum, info, paint!, barCount);

                // Draw performance info if requested
                drawPerformanceInfo?.Invoke(canvas!, info);
            }
            catch (ObjectDisposedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error rendering gradient wave: {ex.Message}");
            }
        }

        private bool ValidateRenderParameters(SKCanvas? canvas, float[]? spectrum, SKPaint? paint)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(GradientWaveRenderer));
            }

            if (!_isInitialized)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Not initialized");
                return false;
            }

            if (canvas == null)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Canvas is null");
                return false;
            }

            if (spectrum == null || spectrum.Length == 0)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Spectrum is null or empty");
                return false;
            }

            if (paint == null)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Paint is null");
                return false;
            }

            return true;
        }

        private void RenderOptimizedGradientWave(
            SKCanvas canvas,
            List<SKPoint> points,
            float[] spectrum,
            SKImageInfo info,
            SKPaint basePaint,
            int barCount)
        {
            if (points.Count < 2)
                return;

            // Calculate maximum magnitude only once
            float maxMagnitude = CalculateMaxMagnitude(spectrum);

            float yBaseline = info.Height - Constants.Offset + Constants.BaselineOffset;
            bool shouldRenderGlow = maxMagnitude > Constants.HighMagnitudeThreshold && _useAdvancedEffects;

            // Prepare paths only once for multiple uses
            PrepareWavePaths(points, info, yBaseline);

            // Consolidated canvas operations to minimize state changes
            canvas.Save();

            // Render fill with optimized gradient
            RenderFillGradient(canvas, basePaint, maxMagnitude, info, yBaseline);

            // Only render glow if needed and allowed by quality settings
            if (shouldRenderGlow)
            {
                RenderGlowEffect(canvas, basePaint, maxMagnitude, barCount);
            }

            // Render main line with optimized gradient
            RenderLineGradient(canvas, points, basePaint, info);

            canvas.Restore();
        }

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

        private void PrepareWavePaths(List<SKPoint> points, SKImageInfo info, float yBaseline)
        {
            _wavePath.Reset();
            _fillPath.Reset();

            // Build wave path with as few points as needed
            _wavePath.MoveTo(points[0]);

            // Use quadratic Bezier curves for smoother appearance with fewer points
            for (int i = 1; i < points.Count - 2; i += 1)
            {
                float x1 = points[i].X;
                float y1 = points[i].Y;
                float x2 = points[i + 1].X;
                float y2 = points[i + 1].Y;
                float xMid = (x1 + x2) / 2;
                float yMid = (y1 + y2) / 2;

                _wavePath.QuadTo(x1, y1, xMid, yMid);
            }

            if (points.Count >= 2)
            {
                _wavePath.LineTo(points[points.Count - 1]);
            }

            // Build fill path by reusing wave path
            _fillPath.AddPath(_wavePath);
            _fillPath.LineTo(info.Width, yBaseline);
            _fillPath.LineTo(0, yBaseline);
            _fillPath.Close();
        }

        private void RenderFillGradient(SKCanvas canvas, SKPaint basePaint, float maxMagnitude, SKImageInfo info, float yBaseline)
        {
            using (var fillPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                IsAntialias = _useAntiAlias,
                FilterQuality = _filterQuality
            })
            {
                // Optimize gradient by using fewer color stops
                SKColor[] colors = new SKColor[3];
                colors[0] = basePaint.Color.WithAlpha((byte)(255 * Constants.FillOpacity * maxMagnitude));
                colors[1] = basePaint.Color.WithAlpha((byte)(255 * Constants.FillOpacity * maxMagnitude * 0.5f));
                colors[2] = SKColors.Transparent;

                float[] colorPositions = { 0.0f, 0.7f, 1.0f };

                fillPaint.Shader = SKShader.CreateLinearGradient(
                    new SKPoint(0, Constants.Offset),
                    new SKPoint(0, yBaseline),
                    colors,
                    colorPositions,
                    SKShaderTileMode.Clamp);

                // Use single draw operation
                canvas.DrawPath(_fillPath, fillPaint);
            }
        }

        private void RenderGlowEffect(SKCanvas canvas, SKPaint basePaint, float maxMagnitude, int barCount)
        {
            // Optimize blur radius calculation
            float blurRadius = Math.Min(Constants.MaxBlurRadius, 10f / MathF.Sqrt(barCount));

            using (var glowPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Constants.DefaultLineWidth * 2.0f,
                IsAntialias = _useAntiAlias,
                FilterQuality = _filterQuality,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, blurRadius),
                Color = basePaint.Color.WithAlpha((byte)(255 * Constants.GlowIntensity * maxMagnitude))
            })
            {
                canvas.DrawPath(_wavePath, glowPaint);
            }
        }

        private void RenderLineGradient(SKCanvas canvas, List<SKPoint> points, SKPaint basePaint, SKImageInfo info)
        {
            float saturation = _isOverlayActive ? Constants.OverlayGradientSaturation : Constants.LineGradientSaturation;
            float lightness = _isOverlayActive ? Constants.OverlayGradientLightness : Constants.LineGradientLightness;

            // Optimize color array allocation - use fewer color stops for low quality
            int colorSteps = _quality == RenderQuality.Low ?
                Math.Max(2, points.Count / 4) :
                points.Count;

            SKColor[] lineColors = new SKColor[colorSteps];
            float[] positions = new float[colorSteps];

            // Generate colors more efficiently
            for (int i = 0; i < colorSteps; i++)
            {
                float normalizedValue = (float)i / (colorSteps - 1);
                int pointIndex = (int)(normalizedValue * (points.Count - 1));

                float segmentMagnitude = 1.0f - (points[pointIndex].Y - Constants.Offset) /
                    (info.Height - 2 * Constants.Offset);

                lineColors[i] = SKColor.FromHsl(
                    normalizedValue * 360,
                    saturation,
                    lightness,
                    (byte)(255 * Math.Min(0.6f + segmentMagnitude * 0.4f, 1.0f)));

                positions[i] = normalizedValue;
            }

            using (var gradientPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Constants.DefaultLineWidth,
                IsAntialias = _useAntiAlias,
                FilterQuality = _filterQuality,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round
            })
            {
                gradientPaint.Shader = SKShader.CreateLinearGradient(
                    new SKPoint(0, 0),
                    new SKPoint(info.Width, 0),
                    lineColors,
                    positions,
                    SKShaderTileMode.Clamp);

                canvas.DrawPath(_wavePath, gradientPaint);
            }
        }
        #endregion

        #region Spectrum Processing
        private static float[] ScaleSpectrum(float[] spectrum, int targetCount)
        {
            int spectrumLength = spectrum.Length;
            float[] scaledSpectrum = new float[targetCount];
            float blockSize = (float)spectrumLength / targetCount;

            // Use SIMD when appropriate and available
            if (Vector.IsHardwareAccelerated && blockSize >= 2 && spectrumLength >= Vector<float>.Count)
            {
                Parallel.For(0, targetCount, i =>
                {
                    int start = (int)(i * blockSize);
                    int end = Math.Min((int)((i + 1) * blockSize), spectrumLength);

                    float sum = 0;
                    int count = end - start;

                    if (count == 0) return;

                    for (int j = start; j < end; j++)
                    {
                        sum += spectrum[j];
                    }

                    scaledSpectrum[i] = sum / count;
                });
            }
            else
            {
                // Fallback for small arrays or when SIMD not available
                for (int i = 0; i < targetCount; i++)
                {
                    float sum = 0;
                    int start = (int)(i * blockSize);
                    int end = Math.Min((int)((i + 1) * blockSize), spectrumLength);
                    int count = end - start;

                    if (count == 0) continue;

                    for (int j = start; j < end; j++)
                    {
                        sum += spectrum[j];
                    }

                    scaledSpectrum[i] = sum / count;
                }
            }

            return scaledSpectrum;
        }

        private float[] SmoothSpectrum(float[] spectrum, int targetCount)
        {
            // Initialize previous spectrum if needed
            if (_previousSpectrum == null || _previousSpectrum.Length != targetCount)
                _previousSpectrum = new float[targetCount];

            // Reuse arrays to reduce allocations
            float[] smoothedSpectrum = new float[targetCount];

            // Apply temporal smoothing with pre-defined smoothing factor
            for (int i = 0; i < targetCount; i++)
            {
                float currentValue = spectrum[i];
                float previousValue = _previousSpectrum[i];
                float smoothedValue = previousValue + (currentValue - previousValue) * _smoothingFactor;
                smoothedSpectrum[i] = Math.Clamp(smoothedValue, Constants.MinMagnitudeThreshold, Constants.MaxSpectrumValue);
                _previousSpectrum[i] = smoothedSpectrum[i];
            }

            // Apply additional spatial smoothing based on quality setting
            for (int pass = 0; pass < _smoothingPasses; pass++)
            {
                float[] extraSmoothedSpectrum = new float[targetCount];

                for (int i = 0; i < targetCount; i++)
                {
                    float sum = smoothedSpectrum[i];
                    int count = 1;

                    if (i > 0) { sum += smoothedSpectrum[i - 1]; count++; }
                    if (i < targetCount - 1) { sum += smoothedSpectrum[i + 1]; count++; }

                    extraSmoothedSpectrum[i] = sum / count;
                }

                // Swap buffers
                float[] temp = smoothedSpectrum;
                smoothedSpectrum = extraSmoothedSpectrum;

                // Don't need this buffer anymore for the final pass
                if (pass < _smoothingPasses - 1)
                {
                    // Reuse the temp buffer for next iteration
                    extraSmoothedSpectrum = temp;
                }
            }

            return smoothedSpectrum;
        }

        private List<SKPoint> GenerateOptimizedPoints(float[] spectrum, SKImageInfo info)
        {
            float min_y = Constants.Offset;
            float max_y = info.Height - Constants.Offset;
            int spectrumLength = spectrum.Length;

            if (spectrumLength < 1)
            {
                return new List<SKPoint>();
            }

            // Calculate the optimal number of points based on quality settings
            int pointCount = _quality switch
            {
                RenderQuality.Low => Math.Max(20, spectrumLength / 4),
                RenderQuality.Medium => Math.Max(40, spectrumLength / 2),
                RenderQuality.High => spectrumLength,
                _ => spectrumLength / 2
            };

            // Pre-allocate list with exact capacity to avoid resizing
            List<SKPoint> points = new List<SKPoint>(pointCount + Constants.ExtraPointsCount);

            // Add edge points
            points.Add(new SKPoint(-Constants.Offset, max_y));
            points.Add(new SKPoint(0, max_y));

            // Sample spectrum at optimal intervals for the current quality level
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
                float y = max_y - (value * (max_y - min_y));
                points.Add(new SKPoint(x, y));
            }

            // Add final edge points
            points.Add(new SKPoint(info.Width, max_y));
            points.Add(new SKPoint(info.Width + Constants.Offset, max_y));

            return points;
        }
        #endregion

        #region Disposal
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                _spectrumSemaphore?.Dispose();
                _wavePath?.Dispose();
                _fillPath?.Dispose();
                _cachedBackground?.Dispose();
                _previousSpectrum = null;
                _processedSpectrum = null;
                _cachedPoints = null;
            }

            _disposed = true;
            SmartLogger.Log(LogLevel.Debug, LogPrefix, "Disposed");
        }

        ~GradientWaveRenderer()
        {
            Dispose(false);
        }
        #endregion
    }
}