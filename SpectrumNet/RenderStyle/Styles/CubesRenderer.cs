namespace SpectrumNet
{
    public class CubesRenderer : ISpectrumRenderer, IDisposable
    {
        private static CubesRenderer? _instance;
        private bool _isInitialized;
        private SKPath _cubeTopPath = new SKPath();
        private volatile bool _disposed;

        private const float MinMagnitudeThreshold = 0.01f, CubeTopWidthProportion = 0.75f,
                           CubeTopHeightProportion = 0.25f, AlphaMultiplier = 255f;

        private float _smoothingFactor = 0.3f;
        private float[]? _previousSpectrum;

        private CubesRenderer() { }

        public static CubesRenderer GetInstance() => _instance ??= new CubesRenderer();

        public void Initialize()
        {
            if (!_isInitialized)
            {
                _isInitialized = true;
                Log.Debug("CubesRenderer initialized");
                _previousSpectrum = null; // Initialize previous spectrum
            }
        }

        public void Configure(bool isOverlayActive)
        {
            _smoothingFactor = isOverlayActive ? 0.5f : 0.3f;
        }

        public void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info, float barWidth, float barSpacing, int barCount, SKPaint? basePaint, Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
        {
            if (!_isInitialized || canvas == null || spectrum == null || spectrum.Length < 2 ||
                basePaint == null || info.Width <= 0 || info.Height <= 0)
            {
                Log.Warning("Invalid render parameters or renderer not initialized.");
                return;
            }

            int halfSpectrumLength = spectrum.Length / 2;
            int actualBarCount = Math.Min(halfSpectrumLength, barCount);
            float[] scaledSpectrum = ScaleSpectrum(spectrum, actualBarCount, halfSpectrumLength);
            float[] smoothedSpectrum = SmoothSpectrum(scaledSpectrum, actualBarCount);
            float canvasHeight = info.Height;

            using var cubePaint = basePaint.Clone();
            cubePaint.IsAntialias = true;
            cubePaint.FilterQuality = SKFilterQuality.High;

            for (int i = 0; i < actualBarCount; i++)
            {
                float magnitude = smoothedSpectrum[i];
                if (magnitude < MinMagnitudeThreshold) continue;

                float height = magnitude * canvasHeight, x = i * (barWidth + barSpacing), y = canvasHeight - height;
                cubePaint.Color = cubePaint.Color.WithAlpha((byte)(magnitude * AlphaMultiplier));

                RenderCube(canvas, x, y, barWidth, height, magnitude, cubePaint);
            }

            drawPerformanceInfo?.Invoke(canvas, info);
        }

        private static float[] ScaleSpectrum(float[] spectrum, int targetCount, int halfSpectrumLength)
        {
            float[] scaledSpectrum = new float[targetCount];
            float blockSize = (float)halfSpectrumLength / targetCount;

            for (int i = 0; i < targetCount; i++)
            {
                int start = (int)Math.Floor(i * blockSize);
                int end = (int)Math.Ceiling((i + 1) * blockSize);

                if (end <= start)
                    end = start + 1;

                end = Math.Min(end, halfSpectrumLength);

                float sum = 0;
                for (int j = start; j < end; j++)
                    sum += spectrum[j];

                scaledSpectrum[i] = sum / (end - start);
            }

            return scaledSpectrum;
        }

        private float[] SmoothSpectrum(float[] spectrum, int targetCount)
        {
            if (_previousSpectrum == null || _previousSpectrum.Length != targetCount)
                _previousSpectrum = new float[targetCount];

            float[] smoothedSpectrum = new float[targetCount];

            for (int i = 0; i < targetCount; i++)
            {
                float currentValue = spectrum[i];
                float smoothedValue = _previousSpectrum[i] + (currentValue - _previousSpectrum[i]) * _smoothingFactor;
                smoothedSpectrum[i] = smoothedValue;
                _previousSpectrum[i] = smoothedValue;
            }

            return smoothedSpectrum;
        }

        private void RenderCube(SKCanvas canvas, float x, float y, float barWidth, float height, float magnitude, SKPaint paint)
        {
            canvas.DrawRect(x, y, barWidth, height, paint);

            // Adjust cube top path if necessary
            if (_cubeTopPath.PointCount == 0)
            {
                _cubeTopPath.MoveTo(x, y);
                _cubeTopPath.LineTo(x + barWidth, y);
                _cubeTopPath.LineTo(x + barWidth * CubeTopWidthProportion, y - barWidth * CubeTopHeightProportion);
                _cubeTopPath.LineTo(x - barWidth * CubeTopHeightProportion, y - barWidth * CubeTopHeightProportion);
                _cubeTopPath.Close();
            }
            else
            {
                // Translate the path to the correct position if needed
                // canvas.Save();
                // canvas.Translate(x, y);
                // canvas.DrawPath(_cubeTopPath, paint);
                // canvas.Restore();
                // For simplicity, recreate the path here
                _cubeTopPath.Reset();
                _cubeTopPath.MoveTo(x, y);
                _cubeTopPath.LineTo(x + barWidth, y);
                _cubeTopPath.LineTo(x + barWidth * CubeTopWidthProportion, y - barWidth * CubeTopHeightProportion);
                _cubeTopPath.LineTo(x - barWidth * CubeTopHeightProportion, y - barWidth * CubeTopHeightProportion);
                _cubeTopPath.Close();
            }

            paint.Color = paint.Color.WithAlpha((byte)(magnitude * AlphaMultiplier * 0.8f));
            canvas.DrawPath(_cubeTopPath, paint);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _cubeTopPath.Dispose();
                    _previousSpectrum = null;
                }
                _disposed = true;
                Log.Debug("CubesRenderer disposed");
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}