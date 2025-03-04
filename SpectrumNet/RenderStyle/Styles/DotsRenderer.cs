namespace SpectrumNet
{
    public class DotsRenderer : ISpectrumRenderer, IDisposable
    {
        private static DotsRenderer? _instance;
        private bool _isInitialized;
        private float _dotRadiusMultiplier = 1f;
        private volatile bool _disposed;

        private const float MinIntensityThreshold = 0.01f;
        private const float MinDotRadius = 2f;
        private const float MaxDotMultiplier = 0.5f;
        private const float AlphaMultiplier = 255f;
        private const int AlphaBins = 16;

        // Smoothing constants and previous spectrum storage
        private const float SmoothingFactorNormal = 0.3f;
        private const float SmoothingFactorOverlay = 0.5f;
        private float _smoothingFactor = SmoothingFactorNormal;
        private float[]? _previousSpectrum;

        private struct CircleData
        {
            public float X;
            public float Y;
            public float Radius;
            public float Intensity;
        }

        private DotsRenderer() { }

        public static DotsRenderer GetInstance() => _instance ??= new DotsRenderer();

        public void Initialize()
        {
            if (!_isInitialized)
            {
                _isInitialized = true;
                Log.Debug("DotsRenderer initialized");
                _previousSpectrum = null; // Initialize previous spectrum
            }
        }

        public void Configure(bool isOverlayActive)
        {
            _dotRadiusMultiplier = isOverlayActive ? 1.5f : 1f;
            _smoothingFactor = isOverlayActive ? SmoothingFactorOverlay : SmoothingFactorNormal;
        }

        public void Render(
            SKCanvas? canvas,
            float[]? spectrum,
            SKImageInfo info, 
            float barWidth, 
            float barSpacing,

            int barCount, SKPaint? basePaint, Action<SKCanvas,
                SKImageInfo> drawPerformanceInfo)
        {
            if (!_isInitialized || canvas == null || spectrum == null || spectrum.Length < 2 ||
                basePaint == null || info.Width <= 0 || info.Height <= 0)
            {
                Log.Warning("Invalid render parameters or renderer not initialized.");
                return;
            }

            int spectrumLength = spectrum.Length;
            int actualBarCount = Math.Min(spectrumLength, barCount);
            float[] scaledSpectrum = ScaleSpectrum(spectrum, actualBarCount, spectrumLength);
            float[] smoothedSpectrum = SmoothSpectrum(scaledSpectrum, actualBarCount);
            float canvasHeight = info.Height;

            using var dotPaint = basePaint.Clone();
            dotPaint.IsAntialias = true;
            dotPaint.FilterQuality = SKFilterQuality.High;

            List<CircleData> circles = CalculateCircleData(smoothedSpectrum, info, barWidth * 
                MaxDotMultiplier * _dotRadiusMultiplier, barWidth + barSpacing, canvasHeight);
            List<List<CircleData>> circleBins = GroupCirclesByAlphaBin(circles);

            DrawCircles(canvas, circleBins, dotPaint);

            drawPerformanceInfo?.Invoke(canvas, info);
        }

        private List<CircleData> CalculateCircleData(float[] smoothedSpectrum, 
            SKImageInfo info, float multiplier, float totalWidth, float canvasHeight)
        {
            List<CircleData> circles = new List<CircleData>();
            for (int i = 0; i < smoothedSpectrum.Length; i++)
            {
                float intensity = smoothedSpectrum[i];
                if (intensity < MinIntensityThreshold) continue;

                float dotRadius = multiplier * intensity;
                if (dotRadius < MinDotRadius)
                    dotRadius = MinDotRadius;

                float x = i * totalWidth + dotRadius;
                float y = canvasHeight - (intensity * canvasHeight);

                circles.Add(new CircleData { X = x, Y = y, Radius = dotRadius, Intensity = intensity });
            }
            return circles;
        }

        private List<List<CircleData>> GroupCirclesByAlphaBin(List<CircleData> circles)
        {
            List<List<CircleData>> circleBins = new List<List<CircleData>>(AlphaBins);
            for (int i = 0; i < AlphaBins; i++)
            {
                circleBins.Add(new List<CircleData>());
            }

            foreach (var circle in circles)
            {
                byte alpha = (byte)(circle.Intensity * AlphaMultiplier);
                if (alpha > 255)
                    alpha = 255;

                int binIndex = (int)(alpha / (255f / (AlphaBins - 1)));
                if (binIndex >= AlphaBins)
                    binIndex = AlphaBins - 1;

                circleBins[binIndex].Add(circle);
            }

            return circleBins;
        }

        private void DrawCircles(SKCanvas canvas, List<List<CircleData>> circleBins, SKPaint dotPaint)
        {
            for (int binIndex = 0; binIndex < AlphaBins; binIndex++)
            {
                if (circleBins[binIndex].Count == 0)
                    continue;

                byte binAlpha = (byte)(binIndex * (255f / (AlphaBins - 1)));
                dotPaint.Color = dotPaint.Color.WithAlpha(binAlpha);

                using var path = new SKPath();
                foreach (var circle in circleBins[binIndex])
                {
                    path.AddCircle(circle.X, circle.Y, circle.Radius);
                }

                canvas.DrawPath(path, dotPaint);
            }
        }

        private static float[] ScaleSpectrum(float[] spectrum, int targetCount, int spectrumLength)
        {
            float[] scaledSpectrum = new float[targetCount];
            float blockSize = (float)spectrumLength / targetCount;

            for (int i = 0; i < targetCount; i++)
            {
                float startFloat = i * blockSize;
                float endFloat = (i + 1) * blockSize;

                int start = (int)Math.Floor(startFloat);
                int end = (int)Math.Ceiling(endFloat);

                end = Math.Min(end, spectrumLength);

                if (start >= end)
                    scaledSpectrum[i] = 0f;
                else
                {
                    float sum = 0f;
                    for (int j = start; j < end; j++)
                        sum += spectrum[j];
                    scaledSpectrum[i] = sum / (end - start);
                }
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
                smoothedSpectrum[i] = smoothedValue;
                _previousSpectrum[i] = smoothedValue;
            }

            return smoothedSpectrum;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _previousSpectrum = null;
                }
                _disposed = true;
                Log.Debug("DotsRenderer disposed");
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}