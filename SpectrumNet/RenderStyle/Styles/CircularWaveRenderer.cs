#nullable enable

namespace SpectrumNet
{
    public sealed class CircularWaveRenderer : ISpectrumRenderer, IDisposable
    {
        private static CircularWaveRenderer? _instance;
        private bool _isInitialized;
        private bool _isOverlayActive;
        private float _rotation, _rotationSpeed = DefaultRotationSpeed;
        private float _radiusProportion = DefaultRadiusProportion;
        private float _amplitudeScale = DefaultAmplitudeScale;
        private float _minMagnitudeThreshold = DefaultMinMagnitudeThreshold;
        private float _smoothingFactor = DefaultSmoothingFactor;

        private float[]? _previousSpectrum, _precomputedCosValues, _precomputedSinValues;
        private int _previousPointCount, _maxPointCount = DefaultMaxPointCount;
        private SKFont? _cachedFont;
        private readonly SKPath _path = new();

        #region Constants
        private const float DefaultRotationSpeed = 0.5f;
        private const float DefaultRadiusProportion = 0.4f;
        private const float DefaultAmplitudeScale = 0.5f;
        private const float DefaultMinMagnitudeThreshold = 0.01f;
        private const float DefaultSmoothingFactor = 0.3f;
        private const float OverlaySmoothingFactor = 0.5f;
        private const int DefaultMaxPointCount = 180;
        #endregion

        private CircularWaveRenderer() { }
        public static CircularWaveRenderer GetInstance() => _instance ??= new CircularWaveRenderer();

        public void Initialize()
        {
            if (!_isInitialized)
            {
                _isInitialized = true;
            }
        }

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

        public void Render(
            SKCanvas canvas,
            float[]? spectrum,
            SKImageInfo info,
            float barWidth,
            float barSpacing,
            int barCount,
            SKPaint paint,
            Action<SKCanvas, SKImageInfo>? drawPerformanceInfo)
        {
            if (!_isInitialized || !AreRenderParamsValid(canvas, spectrum, info, paint))
            {
                return;
            }

            int halfSpectrumLength = spectrum!.Length / 2;
            int pointCount = Math.Min(halfSpectrumLength, _maxPointCount);

            if (_previousPointCount != pointCount)
            {
                PrecomputeTrigonometryValues(pointCount);
                _previousPointCount = pointCount;
            }

            float[] scaledSpectrum = ScaleSpectrum(spectrum, pointCount, halfSpectrumLength);
            float[] smoothedSpectrum = SmoothSpectrum(scaledSpectrum.AsSpan(), pointCount);
            float radius = MathF.Min(info.Width, info.Height) * _radiusProportion;
            RenderCircularWave(canvas, smoothedSpectrum, pointCount, radius, info.Width / 2f, info.Height / 2f, paint);

            _rotation = (_rotation + _rotationSpeed) % 360f;
            if (drawPerformanceInfo != null)
            {
                drawPerformanceInfo(canvas, info);
            }
            else
            {
                DefaultDrawPerformanceInfo(canvas, info, paint);
            }
        }

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
                    _precomputedCosValues = _precomputedSinValues = _previousSpectrum = null;
                    _cachedFont?.Dispose();
                    _path.Dispose();
                }
                _disposed = true;
                _isInitialized = false;
            }
        }
        private bool _disposed;

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

        private static float[] ScaleSpectrum(float[] spectrum, int targetCount, int halfSpectrumLength)
        {
            var scaledSpectrum = new float[targetCount];
            float step = (float)halfSpectrumLength / targetCount;
            for (int i = 0; i < targetCount; i++)
            {
                scaledSpectrum[i] = spectrum[(int)(i * step)];
            }
            return scaledSpectrum;
        }

        private float[] SmoothSpectrum(ReadOnlySpan<float> scaledSpectrum, int pointCount)
        {
            _previousSpectrum ??= new float[pointCount];
            var smoothedSpectrum = new float[pointCount];
            for (int i = 0; i < pointCount; i++)
            {
                smoothedSpectrum[i] = _previousSpectrum[i] * (1 - _smoothingFactor) + scaledSpectrum[i] * _smoothingFactor;
                _previousSpectrum[i] = smoothedSpectrum[i];
            }
            return smoothedSpectrum;
        }

        private void RenderCircularWave(
            SKCanvas canvas,
            ReadOnlySpan<float> spectrum,
            int pointCount,
            float radius,
            float centerX,
            float centerY,
            SKPaint paint)
        {
            _path.Reset();
            float rad = _rotation * MathF.PI / 180f;
            float cosDelta = MathF.Cos(rad);
            float sinDelta = MathF.Sin(rad);
            bool firstPoint = true;
            for (int i = 0; i < pointCount; i++)
            {
                float amplitude = spectrum[i];
                if (amplitude < _minMagnitudeThreshold)
                    continue;
                float r = radius * (1f + amplitude * _amplitudeScale);
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
                using (var glowPaint = paint.Clone())
                {
                    glowPaint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 8);
                    SKColor baseColor = paint.Color;
                    glowPaint.Color = new SKColor(baseColor.Red, baseColor.Green, baseColor.Blue, (byte)(baseColor.Alpha / 2));
                    canvas.DrawPath(_path, glowPaint);
                }
                canvas.DrawPath(_path, paint);
            }
        }

        private static bool AreRenderParamsValid(SKCanvas? canvas, float[]? spectrum, SKImageInfo info, SKPaint? paint) =>
            canvas != null && spectrum != null && spectrum.Length > 0 && paint != null && info.Width > 0 && info.Height > 0;

        private void DefaultDrawPerformanceInfo(SKCanvas canvas, SKImageInfo info, SKPaint baseTextPaint)
        {
            string performanceText = "Performance Info";
            using SKPaint textPaint = new SKPaint
            {
                Color = SKColors.White,
                IsAntialias = true,
                TextSize = 24
            };
            // Use SKFont.Size property instead of TextSize.
            if (_cachedFont == null || _cachedFont.Size != textPaint.TextSize || _cachedFont.Typeface != textPaint.Typeface)
            {
                _cachedFont?.Dispose();
                _cachedFont = new SKFont(textPaint.Typeface, textPaint.TextSize);
            }
            float x = 10;
            float y = info.Height - 10;
            canvas.DrawText(performanceText, x, y, _cachedFont, textPaint);
        }
    }
}