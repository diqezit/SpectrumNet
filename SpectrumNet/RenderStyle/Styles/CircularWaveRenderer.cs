#nullable enable

namespace SpectrumNet
{
    public sealed class CircularWaveRenderer : ISpectrumRenderer, IDisposable
    {
        #region Fields
        private static CircularWaveRenderer? _instance;
        private bool _isInitialized;
        private bool _isOverlayActive;
        private float _rotation, _rotationSpeed = DefaultRotationSpeed;
        private float _radiusProportion = DefaultRadiusProportion;
        private float _amplitudeScale = DefaultAmplitudeScale;
        private float _minMagnitudeThreshold = DefaultMinMagnitudeThreshold;
        private float _smoothingFactor = DefaultSmoothingFactor;

        private float[]? _previousSpectrum, _processedSpectrum;
        private float[]? _precomputedCosValues, _precomputedSinValues;
        private int _previousPointCount, _maxPointCount = DefaultMaxPointCount;
        private SKFont? _cachedFont;
        private readonly SKPath _path = new();
        private readonly SemaphoreSlim _spectrumSemaphore = new(1, 1);
        private readonly object _spectrumLock = new();
        private bool _disposed;

        #region Constants
        private const float DefaultRotationSpeed = 0.5f;
        private const float DefaultRadiusProportion = 0.4f;
        private const float DefaultAmplitudeScale = 0.5f;
        private const float DefaultMinMagnitudeThreshold = 0.01f;
        private const float DefaultSmoothingFactor = 0.3f;
        private const float OverlaySmoothingFactor = 0.5f;
        private const int DefaultMaxPointCount = 180;
        private const float DefaultGlowIntensity = 0.5f;
        private const float HighIntensityThreshold = 0.7f;
        private const float WaveAlphaMultiplier = 1.2f;
        private const int MinPointCount = 12;
        #endregion
        #endregion

        #region Constructor and Initialization
        private CircularWaveRenderer() { }

        public static CircularWaveRenderer GetInstance() => _instance ??= new CircularWaveRenderer();

        public void Initialize()
        {
            if (!_isInitialized)
            {
                _isInitialized = true;
                Log.Debug("CircularWaveRenderer initialized");
            }
        }
        #endregion

        #region Configuration
        public void Configure(bool isOverlayActive) =>
            _smoothingFactor = (_isOverlayActive = isOverlayActive) ? OverlaySmoothingFactor : DefaultSmoothingFactor;

        public void ConfigureAdvanced(
            bool? isOverlayActive = null,
            float? rotationSpeed = null,
            float? radiusProportion = null,
            float? amplitudeScale = null,
            float? minMagnitudeThreshold = null,
            int? maxPointCount = null)
        {
            _isOverlayActive = isOverlayActive ?? _isOverlayActive;
            _rotationSpeed = rotationSpeed ?? _rotationSpeed;
            _radiusProportion = radiusProportion ?? _radiusProportion;
            _amplitudeScale = amplitudeScale ?? _amplitudeScale;
            _minMagnitudeThreshold = minMagnitudeThreshold ?? _minMagnitudeThreshold;
            _maxPointCount = maxPointCount ?? _maxPointCount;
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
            if (!ValidateRenderParameters(canvas, spectrum, info, paint))
                return;

            float[] renderSpectrum;
            bool semaphoreAcquired = false;
            int spectrumLength = spectrum!.Length;

            // Use barCount to limit the number of points, ensuring a minimum
            int pointCount = Math.Max(
                MinPointCount,
                Math.Min(Math.Min(spectrumLength, _maxPointCount), barCount)
            );

            // Adjust rotation speed based on bar count - lower speed for fewer bars
            float adjustedRotationSpeed = _rotationSpeed * (0.5f + 0.5f * pointCount / Math.Max(barCount, 1));

            try
            {
                semaphoreAcquired = _spectrumSemaphore.Wait(0);

                if (semaphoreAcquired)
                {
                    // Update rotation for animation with adjusted speed
                    _rotation = (_rotation + adjustedRotationSpeed) % 360f;

                    // Ensure trig values are precomputed
                    if (_previousPointCount != pointCount)
                    {
                        PrecomputeTrigonometryValues(pointCount);
                        _previousPointCount = pointCount;
                    }

                    // Process spectrum data
                    float[] scaledSpectrum = ScaleSpectrum(spectrum, pointCount, spectrumLength);
                    _processedSpectrum = SmoothSpectrum(scaledSpectrum.AsSpan(), pointCount);
                }

                // Use processed spectrum or calculate synchronously if needed
                lock (_spectrumLock)
                {
                    renderSpectrum = _processedSpectrum ??
                                    ProcessSynchronously(spectrum, pointCount, spectrumLength);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error processing spectrum: {ex.Message}");
                return;
            }
            finally
            {
                if (semaphoreAcquired)
                {
                    _spectrumSemaphore.Release();
                }
            }

            try
            {
                // Adjust radius based on bar count - larger radius for fewer bars
                float radius = MathF.Min(info.Width, info.Height) * _radiusProportion *
                               (1f + 0.1f * (1f - (float)Math.Min(barCount, 100) / 100f));

                // Adjust amplitude scale based on bar count - stronger effect for fewer bars
                float amplitudeScale = _amplitudeScale * (1f + 0.2f * (1f - (float)Math.Min(barCount, 100) / 100f));

                RenderCircularWave(canvas!, renderSpectrum, pointCount, radius, info.Width / 2f, info.Height / 2f, paint!, amplitudeScale, barWidth);

                if (drawPerformanceInfo != null)
                {
                    drawPerformanceInfo(canvas!, info);
                }
                else
                {
                    DefaultDrawPerformanceInfo(canvas!, info, paint!);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error rendering wave: {ex.Message}");
            }
        }

        private bool ValidateRenderParameters(
            SKCanvas? canvas,
            float[]? spectrum,
            SKImageInfo info,
            SKPaint? paint)
        {
            if (!_isInitialized)
            {
                Log.Error("CircularWaveRenderer not initialized before rendering");
                return false;
            }

            if (canvas == null)
            {
                Log.Error("Cannot render with null canvas");
                return false;
            }

            if (spectrum == null || spectrum.Length == 0)
            {
                Log.Error("Cannot render with null or empty spectrum");
                return false;
            }

            if (paint == null)
            {
                Log.Error("Cannot render with null paint");
                return false;
            }

            if (info.Width <= 0 || info.Height <= 0)
            {
                Log.Error("Cannot render with invalid canvas dimensions");
                return false;
            }

            return true;
        }

        private float[] ProcessSynchronously(float[] spectrum, int pointCount, int spectrumLength)
        {
            // Изменено: используем полную длину спектра
            float[] scaledSpectrum = ScaleSpectrum(spectrum, pointCount, spectrumLength);
            return SmoothSpectrum(scaledSpectrum.AsSpan(), pointCount);
        }
        #endregion

        #region Spectrum Processing
        private void PrecomputeTrigonometryValues(int pointCount)
        {
            if (_precomputedCosValues?.Length == pointCount)
            {
                return;
            }

            _precomputedCosValues = new float[pointCount];
            _precomputedSinValues = new float[pointCount];
            float angleStep = 2 * MathF.PI / pointCount;

            for (int i = 0; i < pointCount; i++)
            {
                float angle = i * angleStep;
                _precomputedCosValues[i] = MathF.Cos(angle);
                _precomputedSinValues[i] = MathF.Sin(angle);
            }
        }

        private static float[] ScaleSpectrum(float[] spectrum, int targetCount, int spectrumLength)
        {
            var scaledSpectrum = new float[targetCount];
            float step = (float)spectrumLength / targetCount;

            for (int i = 0; i < targetCount; i++)
            {
                int index = (int)(i * step);
                if (index < spectrumLength)
                {
                    scaledSpectrum[i] = spectrum[index];
                }
            }

            return scaledSpectrum;
        }

        private float[] SmoothSpectrum(ReadOnlySpan<float> scaledSpectrum, int pointCount)
        {
            if (_previousSpectrum == null || _previousSpectrum.Length != pointCount)
            {
                _previousSpectrum = new float[pointCount];
            }

            var smoothedSpectrum = new float[pointCount];

            for (int i = 0; i < pointCount; i++)
            {
                smoothedSpectrum[i] = _previousSpectrum[i] * (1 - _smoothingFactor) +
                                      scaledSpectrum[i] * _smoothingFactor;
                _previousSpectrum[i] = smoothedSpectrum[i];
            }

            return smoothedSpectrum;
        }
        #endregion

        #region Wave Rendering
        private void RenderCircularWave(
            SKCanvas canvas,
            ReadOnlySpan<float> spectrum,
            int pointCount,
            float radius,
            float centerX,
            float centerY,
            SKPaint paint,
            float amplitudeScale,
            float barWidth)
        {
            // Calculate rotation matrix
            float rad = _rotation * MathF.PI / 180f;
            float cosDelta = MathF.Cos(rad);
            float sinDelta = MathF.Sin(rad);

            // Reset path for new drawing
            _path.Reset();

            // Find the maximum amplitude for alpha calculation
            float maxAmplitude = 0;
            for (int i = 0; i < pointCount; i++)
            {
                if (spectrum[i] > maxAmplitude)
                    maxAmplitude = spectrum[i];
            }

            // Draw the wave outline
            bool firstPoint = true;
            for (int i = 0; i < pointCount; i++)
            {
                float amplitude = spectrum[i];
                if (amplitude < _minMagnitudeThreshold)
                    continue;

                float r = radius * (1f + amplitude * amplitudeScale);
                float baseCos = _precomputedCosValues![i];
                float baseSin = _precomputedSinValues![i];
                float rotatedCos = baseCos * cosDelta - baseSin * sinDelta;
                float rotatedSin = baseSin * cosDelta + baseCos * sinDelta;
                float x = centerX + r * rotatedCos;
                float y = centerY + r * rotatedSin;

                if (firstPoint)
                {
                    _path.MoveTo(x, y);
                    firstPoint = false;
                }
                else
                {
                    _path.LineTo(x, y);
                }
            }

            // Only render if we have at least one point
            if (!firstPoint)
            {
                _path.Close();

                // Adjust alpha based on overall intensity
                byte alpha = (byte)(paint.Color.Alpha * Math.Min(maxAmplitude * WaveAlphaMultiplier, 1.0f));

                // Adjust blur radius based on bar width
                float blurRadius = Math.Max(4f, Math.Min(barWidth * 0.8f, 12f));

                // Draw outer glow
                using (var glowPaint = paint.Clone())
                {
                    glowPaint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, blurRadius);
                    SKColor baseColor = paint.Color;
                    glowPaint.Color = new SKColor(
                        baseColor.Red,
                        baseColor.Green,
                        baseColor.Blue,
                        (byte)(alpha * DefaultGlowIntensity));
                    canvas.DrawPath(_path, glowPaint);
                }

                // Draw inner glow for high intensity waves
                if (maxAmplitude > HighIntensityThreshold)
                {
                    using (var innerGlowPaint = paint.Clone())
                    {
                        innerGlowPaint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, blurRadius * 0.5f);
                        innerGlowPaint.Color = new SKColor(255, 255, 255, (byte)(alpha * 0.3f));
                        canvas.DrawPath(_path, innerGlowPaint);
                    }
                }

                // Draw main outline with thickness adjusted by bar width
                using (var outlinePaint = paint.Clone())
                {
                    outlinePaint.Color = outlinePaint.Color.WithAlpha(alpha);
                    outlinePaint.StrokeWidth = Math.Max(1f, barWidth * 0.2f);
                    outlinePaint.Style = SKPaintStyle.Stroke;
                    canvas.DrawPath(_path, outlinePaint);
                }

                // Fill with semi-transparent color
                using (var fillPaint = paint.Clone())
                {
                    fillPaint.Style = SKPaintStyle.Fill;
                    fillPaint.Color = fillPaint.Color.WithAlpha((byte)(alpha * 0.4f));
                    canvas.DrawPath(_path, fillPaint);
                }
            }
        }

        private void DefaultDrawPerformanceInfo(SKCanvas canvas, SKImageInfo info, SKPaint baseTextPaint)
        {
            try
            {
                string performanceText = "Performance Info";
                using SKPaint textPaint = new SKPaint
                {
                    Color = SKColors.White,
                    IsAntialias = true,
                    TextSize = 24
                };

                // Cache SKFont for better performance
                if (_cachedFont == null ||
                    _cachedFont.Size != textPaint.TextSize ||
                    _cachedFont.Typeface != textPaint.Typeface)
                {
                    _cachedFont?.Dispose();
                    _cachedFont = new SKFont(textPaint.Typeface, textPaint.TextSize);
                }

                float x = 10;
                float y = info.Height - 10;
                canvas.DrawText(performanceText, x, y, _cachedFont, textPaint);
            }
            catch (Exception ex)
            {
                Log.Error($"Error drawing performance info: {ex.Message}");
            }
        }
        #endregion

        #region Disposal
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _spectrumSemaphore.Dispose();
                    _precomputedCosValues = _precomputedSinValues = _previousSpectrum = _processedSpectrum = null;
                    _cachedFont?.Dispose();
                    _path.Dispose();
                }
                _disposed = true;
                _isInitialized = false;
                Log.Debug("CircularWaveRenderer disposed");
            }
        }
        #endregion
    }
}