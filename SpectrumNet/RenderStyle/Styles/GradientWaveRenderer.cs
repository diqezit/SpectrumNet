#nullable enable

namespace SpectrumNet
{
    public class GradientWaveRenderer : ISpectrumRenderer, IDisposable
    {
        private static readonly Lazy<GradientWaveRenderer> _instance =
            new Lazy<GradientWaveRenderer>(() => new GradientWaveRenderer());
        private bool _isInitialized;
        private bool _disposed = false;
        private const float Offset = 10f;
        private bool _isOverlayActive;
        private float[]? _previousSpectrum;
        private const float SmoothingFactorNormal = 0.3f;
        private const float SmoothingFactorOverlay = 0.5f;
        private float _smoothingFactor = SmoothingFactorNormal;
        private const float MinMagnitudeThreshold = 0.01f;
        private const float MaxSpectrumValue = 1.5f;

        private GradientWaveRenderer() { }

        public static GradientWaveRenderer GetInstance() => _instance.Value;

        public void Initialize()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(GradientWaveRenderer));
            }

            if (_isInitialized) return;

            Log.Debug("GradientWaveRenderer initialized");
            _isInitialized = true;
        }

        public void Configure(bool isOverlayActive)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(GradientWaveRenderer));
            }

            _isOverlayActive = isOverlayActive;
            _smoothingFactor = isOverlayActive ? SmoothingFactorOverlay : SmoothingFactorNormal;
        }

        public void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info, float barWidth, float barSpacing, int barCount, SKPaint? paint, Action<SKCanvas, SKImageInfo>? drawPerformanceInfo)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(GradientWaveRenderer));
            }

            if (!_isInitialized)
            {
                Log.Warning("GradientWaveRenderer is not initialized.");
                return;
            }

            if (canvas == null || spectrum == null || spectrum.Length == 0 || paint == null)
            {
                Log.Warning("Invalid render parameters");
                return;
            }

            int actualBarCount = Math.Min(spectrum.Length / 2, barCount);
            float[] scaledSpectrum = ScaleSpectrum(spectrum, actualBarCount);
            float[] smoothedSpectrum = SmoothSpectrum(scaledSpectrum, actualBarCount);

            List<SKPoint> points = GetSpectrumPoints(smoothedSpectrum, info);

            using (var gradientPaint = paint.Clone())
            {
                gradientPaint.Style = SKPaintStyle.Stroke;
                gradientPaint.StrokeWidth = 3;
                gradientPaint.IsAntialias = true;

                for (int i = 0; i < points.Count - 1; i++)
                {
                    float normalizedValue = points[i].X / info.Width;
                    float saturation = _isOverlayActive ? 80f : 100f;
                    float lightness = _isOverlayActive ? 70f : 50f;
                    SKColor color = SKColor.FromHsl(normalizedValue * 360, saturation, lightness);
                    gradientPaint.Color = color;
                    canvas.DrawLine(points[i], points[i + 1], gradientPaint);
                }
            }

            drawPerformanceInfo?.Invoke(canvas, info);
        }

        private static float[] ScaleSpectrum(float[] spectrum, int targetCount)
        {
            int spectrumLength = spectrum.Length / 2;
            float[] scaledSpectrum = new float[targetCount];
            float blockSize = (float)spectrumLength / targetCount;

            for (int i = 0; i < targetCount; i++)
            {
                float sum = 0;
                int start = (int)(i * blockSize);
                int end = (int)((i + 1) * blockSize);

                for (int j = start; j < end && j < spectrumLength; j++)
                {
                    sum += spectrum[j];
                }

                scaledSpectrum[i] = sum / (end - start);
            }

            return scaledSpectrum;
        }

        private float[] SmoothSpectrum(float[] spectrum, int targetCount)
        {
            if (_previousSpectrum == null || _previousSpectrum.Length != targetCount)
                _previousSpectrum = new float[targetCount];

            var smoothedSpectrum = new float[targetCount];

            for (int i = 0; i < targetCount; i++)
            {
                float currentValue = spectrum[i];
                float smoothedValue = _previousSpectrum[i] + (currentValue - _previousSpectrum[i]) * _smoothingFactor;
                smoothedSpectrum[i] = Math.Clamp(smoothedValue, MinMagnitudeThreshold, MaxSpectrumValue);
                _previousSpectrum[i] = smoothedSpectrum[i];
            }

            return smoothedSpectrum;
        }

        private List<SKPoint> GetSpectrumPoints(float[] spectrum, SKImageInfo info)
        {
            List<SKPoint> points = new List<SKPoint>();
            float min_y = Offset;
            float max_y = info.Height - Offset;
            int spectrumLength = spectrum.Length;

            if (spectrumLength < 1)
            {
                return points;
            }

            float step = spectrumLength > 1 ? info.Width / (spectrumLength - 1) : 0;

            for (int i = 0; i < spectrumLength; i++)
            {
                float x = i * step;
                float y = max_y - (spectrum[i] * (max_y - min_y));
                points.Add(new SKPoint(x, y));
            }

            return points;
        }

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
                // Dispose managed resources here
            }

            // Release unmanaged resources here

            _previousSpectrum = null;
            _disposed = true;
        }

        ~GradientWaveRenderer()
        {
            Dispose(false);
        }
    }
}