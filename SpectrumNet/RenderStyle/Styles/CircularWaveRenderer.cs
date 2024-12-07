namespace SpectrumNet
{
    public class CircularWaveRenderer : ISpectrumRenderer, IDisposable
    {
        private static CircularWaveRenderer? _instance;
        private bool _isInitialized, _disposed, _isOverlayActive;
        private readonly SKPath _path = new();
        private float _rotation, _rotationSpeed = 0.5f, _radiusProportion = 0.4f, _amplitudeScale = 0.5f, _minMagnitudeThreshold = 0.01f;
        private float[]? _previousSpectrum, _precomputedCosValues, _precomputedSinValues;
        private int _previousPointCount = -1;
        private float _smoothingFactor = 0.3f;

        private CircularWaveRenderer()
        {
            _precomputedCosValues = null;
            _precomputedSinValues = null;
        }

        public static CircularWaveRenderer GetInstance() => _instance ??= new CircularWaveRenderer();

        public void Initialize()
        {
            if (!_isInitialized)
            {
                _isInitialized = true;
                Log.Debug("CircularWaveRenderer initialized");
            }
        }

        public void Configure(bool isOverlayActive)
        {
            _isOverlayActive = isOverlayActive;
            _smoothingFactor = _isOverlayActive ? 0.5f : 0.3f;
        }

        public void ConfigureAdvanced(bool? isOverlayActive = null, float? rotationSpeed = null, float? radiusProportion = null,
                                      float? amplitudeScale = null, float? minMagnitudeThreshold = null)
        {
            if (isOverlayActive.HasValue) _isOverlayActive = isOverlayActive.Value;
            _rotationSpeed = rotationSpeed ?? _rotationSpeed;
            _radiusProportion = radiusProportion ?? _radiusProportion;
            _amplitudeScale = amplitudeScale ?? _amplitudeScale;
            _minMagnitudeThreshold = minMagnitudeThreshold ?? _minMagnitudeThreshold;
        }

        public void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info, float barWidth, float barSpacing,
                           int barCount, SKPaint? basePaint, Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
        {
            if (!_isInitialized || canvas == null || spectrum == null || spectrum.Length < 2 || basePaint == null ||
                info.Width <= 0 || info.Height <= 0)
            {
                Log.Warning("Invalid render parameters or renderer not initialized.");
                return;
            }

            int halfSpectrumLength = spectrum.Length / 2;
            int pointCount = Math.Min(halfSpectrumLength, 180);
            if (_previousPointCount != pointCount)
            {
                PrecomputeTrigonometryValues(pointCount);
                _previousPointCount = pointCount;
            }

            float[] scaledSpectrum = ScaleSpectrum(spectrum, pointCount, halfSpectrumLength);
            float[] smoothedSpectrum = SmoothSpectrum(scaledSpectrum, pointCount);
            float radius = Math.Min(info.Width, info.Height) * _radiusProportion;
            float centerX = info.Width / 2f, centerY = info.Height / 2f;

            using var renderPaint = basePaint.Clone();
            renderPaint.IsAntialias = true;

            RenderCircularWave(canvas, smoothedSpectrum.AsSpan(), pointCount, radius, centerX, centerY, renderPaint);

            _rotation = (_rotation + _rotationSpeed) % 360f;
            drawPerformanceInfo?.Invoke(canvas, info);
        }

        private void PrecomputeTrigonometryValues(int pointCount)
        {
            if (_precomputedCosValues == null || _precomputedCosValues.Length != pointCount)
            {
                _precomputedCosValues = new float[pointCount];
                _precomputedSinValues = new float[pointCount];
                float angleStep = 360f / pointCount;
                for (int i = 0; i < pointCount; i++)
                {
                    float angle = i * angleStep * (MathF.PI / 180f);
                    _precomputedCosValues[i] = MathF.Cos(angle);
                    _precomputedSinValues[i] = MathF.Sin(angle);
                }
            }
        }

        private static float[] ScaleSpectrum(float[] spectrum, int targetCount, int halfSpectrumLength)
        {
            float[] scaledSpectrum = new float[targetCount];
            int step = halfSpectrumLength / targetCount;
            for (int i = 0; i < targetCount; i++) scaledSpectrum[i] = spectrum[i * step];
            return scaledSpectrum;
        }

        private float[] SmoothSpectrum(float[] scaledSpectrum, int pointCount)
        {
            if (_previousSpectrum == null || _previousSpectrum.Length != pointCount)
            {
                _previousSpectrum = new float[pointCount];
            }

            float[] smoothedSpectrum = new float[pointCount];

            for (int i = 0; i < pointCount; i++)
            {
                smoothedSpectrum[i] = _previousSpectrum[i] * (1 - _smoothingFactor) + scaledSpectrum[i] * _smoothingFactor;
                _previousSpectrum[i] = smoothedSpectrum[i];
            }

            return smoothedSpectrum;
        }

        private void RenderCircularWave(SKCanvas canvas, ReadOnlySpan<float> spectrum, int pointCount, float radius,
                                        float centerX, float centerY, SKPaint paint)
        {
            _path.Reset();
            float angleStep = 360f / pointCount;

            for (int i = 0; i < pointCount; i++)
            {
                float amplitude = spectrum[i];
                if (amplitude < _minMagnitudeThreshold) continue;

                float angle = (i * angleStep + _rotation) * (MathF.PI / 180f);
                float r = radius * (1f + amplitude * _amplitudeScale);
                float x = centerX + r * _precomputedCosValues![i];
                float y = centerY + r * _precomputedSinValues![i];

                if (i == 0) _path.MoveTo(x, y);
                else _path.LineTo(x, y);
            }

            _path.Close();
            canvas.DrawPath(_path, paint);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing)
            {
                _precomputedCosValues = null;
                _precomputedSinValues = null;
                _previousSpectrum = null;
            }
            _disposed = true;
            _isInitialized = false;
            Log.Debug("CircularWaveRenderer disposed");
        }
    }
}