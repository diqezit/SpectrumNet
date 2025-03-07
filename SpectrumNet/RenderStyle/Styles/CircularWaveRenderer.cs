#nullable enable

namespace SpectrumNet
{
    public sealed class CircularWaveRenderer : ISpectrumRenderer, IDisposable
    {
        #region Constants
        private const string LogPrefix = "[CircularWaveRenderer] ";
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

        #region Fields
        private static CircularWaveRenderer? _instance;
        private bool _isInitialized;
        private bool _isOverlayActive;
        private float _rotation;
        private float _rotationSpeed = DefaultRotationSpeed;
        private float _radiusProportion = DefaultRadiusProportion;
        private float _amplitudeScale = DefaultAmplitudeScale;
        private float _minMagnitudeThreshold = DefaultMinMagnitudeThreshold;
        private float _smoothingFactor = DefaultSmoothingFactor;

        private float[]? _previousSpectrum;
        private float[]? _processedSpectrum;
        private float[]? _precomputedCosValues;
        private float[]? _precomputedSinValues;
        private int _previousPointCount;
        private int _maxPointCount = DefaultMaxPointCount;
        private SKFont? _cachedFont;
        private readonly SKPath _path = new();
        private readonly SemaphoreSlim _spectrumSemaphore = new(1, 1);
        private readonly object _spectrumLock = new();
        private bool _disposed;

        // RenderQuality fields
        private RenderQuality _quality = RenderQuality.Medium;
        private bool _useAntiAlias = true;
        private SKFilterQuality _filterQuality = SKFilterQuality.Medium;
        private bool _useAdvancedEffects = true;
        #endregion

        #region Constructor and Initialization
        private CircularWaveRenderer() { }

        public static CircularWaveRenderer GetInstance()
        {
            return _instance ??= new CircularWaveRenderer();
        }

        public void Initialize()
        {
            if (!_isInitialized)
            {
                _isInitialized = true;
                SmartLogger.Log(LogLevel.Debug, LogPrefix, "CircularWaveRenderer initialized");
            }
        }
        #endregion

        #region Configuration
        public void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium)
        {
            _isOverlayActive = isOverlayActive;
            Quality = quality;
            _smoothingFactor = isOverlayActive ? OverlaySmoothingFactor : DefaultSmoothingFactor;
        }

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
                    break;
                case RenderQuality.Medium:
                    _useAntiAlias = true;
                    _filterQuality = SKFilterQuality.Medium;
                    _useAdvancedEffects = true;
                    break;
                case RenderQuality.High:
                    _useAntiAlias = true;
                    _filterQuality = SKFilterQuality.High;
                    _useAdvancedEffects = true;
                    break;
            }
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

            int pointCount = Math.Max(MinPointCount, Math.Min(Math.Min(spectrum!.Length, _maxPointCount), barCount));
            float adjustedRotationSpeed = _rotationSpeed * (0.5f + 0.5f * pointCount / Math.Max(barCount, 1));

            // Process spectrum asynchronously
            Task.Run(async () =>
            {
                try
                {
                    await _spectrumSemaphore.WaitAsync();
                    _rotation = (_rotation + adjustedRotationSpeed) % 360f;
                    if (_previousPointCount != pointCount)
                    {
                        PrecomputeTrigonometryValues(pointCount);
                        _previousPointCount = pointCount;
                    }
                    float[] scaledSpectrum = ScaleSpectrum(spectrum, pointCount, spectrum.Length);
                    _processedSpectrum = SmoothSpectrum(scaledSpectrum, pointCount);
                }
                catch (Exception ex)
                {
                    SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error processing spectrum: {ex.Message}");
                }
                finally
                {
                    _spectrumSemaphore.Release();
                }
            });

            float[] renderSpectrum;
            lock (_spectrumLock)
            {
                renderSpectrum = _processedSpectrum ?? ProcessSynchronously(spectrum!, pointCount, spectrum.Length);
            }

            float radius = MathF.Min(info.Width, info.Height) * _radiusProportion *
                           (1f + 0.1f * (1f - (float)Math.Min(barCount, 100) / 100f));
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

        private bool ValidateRenderParameters(
            SKCanvas? canvas,
            float[]? spectrum,
            SKImageInfo info,
            SKPaint? paint)
        {
            if (!_isInitialized)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "CircularWaveRenderer not initialized before rendering");
                return false;
            }

            if (canvas == null)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Cannot render with null canvas");
                return false;
            }

            if (spectrum == null || spectrum.Length == 0)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Cannot render with null or empty spectrum");
                return false;
            }

            if (paint == null)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Cannot render with null paint");
                return false;
            }

            if (info.Width <= 0 || info.Height <= 0)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Cannot render with invalid canvas dimensions");
                return false;
            }

            return true;
        }

        private float[] ProcessSynchronously(float[] spectrum, int pointCount, int spectrumLength)
        {
            float[] scaledSpectrum = ScaleSpectrum(spectrum, pointCount, spectrumLength);
            return SmoothSpectrum(scaledSpectrum, pointCount);
        }
        #endregion

        #region Spectrum Processing
        private void PrecomputeTrigonometryValues(int pointCount)
        {
            if (_precomputedCosValues?.Length == pointCount)
                return;

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

        private float[] SmoothSpectrum(float[] scaledSpectrum, int pointCount)
        {
            if (_previousSpectrum == null || _previousSpectrum.Length != pointCount)
            {
                _previousSpectrum = new float[pointCount];
            }

            var smoothedSpectrum = new float[pointCount];

            if (Vector.IsHardwareAccelerated && pointCount >= Vector<float>.Count)
            {
                int vectorSize = Vector<float>.Count;
                int vectorizedLength = pointCount - (pointCount % vectorSize);

                for (int i = 0; i < vectorizedLength; i += vectorSize)
                {
                    Vector<float> currentValues = new Vector<float>(scaledSpectrum, i);
                    Vector<float> previousValues = new Vector<float>(_previousSpectrum, i);
                    Vector<float> smoothedValues = previousValues * (1 - _smoothingFactor) + currentValues * _smoothingFactor;
                    smoothedValues.CopyTo(smoothedSpectrum, i);
                    smoothedValues.CopyTo(_previousSpectrum, i);
                }

                for (int i = vectorizedLength; i < pointCount; i++)
                {
                    smoothedSpectrum[i] = _previousSpectrum[i] * (1 - _smoothingFactor) + scaledSpectrum[i] * _smoothingFactor;
                    _previousSpectrum[i] = smoothedSpectrum[i];
                }
            }
            else
            {
                for (int i = 0; i < pointCount; i++)
                {
                    smoothedSpectrum[i] = _previousSpectrum[i] * (1 - _smoothingFactor) + scaledSpectrum[i] * _smoothingFactor;
                    _previousSpectrum[i] = smoothedSpectrum[i];
                }
            }

            return smoothedSpectrum;
        }
        #endregion

        #region Wave Rendering
        private void RenderCircularWave(
            SKCanvas canvas,
            float[] spectrum,
            int pointCount,
            float radius,
            float centerX,
            float centerY,
            SKPaint paint,
            float amplitudeScale,
            float barWidth)
        {
            float rad = _rotation * MathF.PI / 180f;
            float cosDelta = MathF.Cos(rad);
            float sinDelta = MathF.Sin(rad);

            _path.Reset();

            float maxAmplitude = 0;
            for (int i = 0; i < pointCount; i++)
            {
                if (spectrum[i] > maxAmplitude)
                    maxAmplitude = spectrum[i];
            }

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

            if (!firstPoint)
            {
                _path.Close();

                byte alpha = (byte)(paint.Color.Alpha * Math.Min(maxAmplitude * WaveAlphaMultiplier, 1.0f));
                float blurRadius = Math.Max(4f, Math.Min(barWidth * 0.8f, 12f));

                if (_useAdvancedEffects)
                {
                    using var glowPaint = paint.Clone();
                    glowPaint.IsAntialias = _useAntiAlias;
                    glowPaint.FilterQuality = _filterQuality;
                    glowPaint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, blurRadius);
                    glowPaint.Color = paint.Color.WithAlpha((byte)(alpha * DefaultGlowIntensity));
                    canvas.DrawPath(_path, glowPaint);

                    if (maxAmplitude > HighIntensityThreshold)
                    {
                        using var innerGlowPaint = paint.Clone();
                        innerGlowPaint.IsAntialias = _useAntiAlias;
                        innerGlowPaint.FilterQuality = _filterQuality;
                        innerGlowPaint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, blurRadius * 0.5f);
                        innerGlowPaint.Color = new SKColor(255, 255, 255, (byte)(alpha * 0.3f));
                        canvas.DrawPath(_path, innerGlowPaint);
                    }
                }

                using var outlinePaint = paint.Clone();
                outlinePaint.IsAntialias = _useAntiAlias;
                outlinePaint.FilterQuality = _filterQuality;
                outlinePaint.Color = paint.Color.WithAlpha(alpha);
                outlinePaint.StrokeWidth = Math.Max(1f, barWidth * 0.2f);
                outlinePaint.Style = SKPaintStyle.Stroke;
                canvas.DrawPath(_path, outlinePaint);

                using var fillPaint = paint.Clone();
                fillPaint.IsAntialias = _useAntiAlias;
                fillPaint.FilterQuality = _filterQuality;
                fillPaint.Style = SKPaintStyle.Fill;
                fillPaint.Color = paint.Color.WithAlpha((byte)(alpha * 0.4f));
                canvas.DrawPath(_path, fillPaint);
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
                    IsAntialias = _useAntiAlias,
                    FilterQuality = _filterQuality,
                    TextSize = 24
                };

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
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error drawing performance info: {ex.Message}");
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
                SmartLogger.Log(LogLevel.Debug, LogPrefix, "CircularWaveRenderer disposed");
            }
        }
        #endregion
    }
}