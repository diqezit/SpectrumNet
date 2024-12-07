namespace SpectrumNet
{
    public class CircularBarsRenderer : ISpectrumRenderer, IDisposable
    {
        private static CircularBarsRenderer? _instance;
        private bool _isInitialized;
        private readonly SKPath _path = new();
        private float[]? _cosValues, _sinValues, _previousSpectrum;
        private volatile bool _disposed;

        private const float RadiusProportion = 0.8f, SpectrumMultiplier = 0.5f, SmoothingFactor = 0.3f;
        private const float MinMagnitudeThreshold = 0.01f, MaxBarHeight = 1.5f, MinBarHeight = 0.01f;

        private CircularBarsRenderer() { }

        public static CircularBarsRenderer GetInstance() => _instance ??= new CircularBarsRenderer();

        public void Initialize()
        {
            if (_isInitialized) return;
            _isInitialized = true;
            Log.Debug("CircularBarsRenderer initialized");
        }

        public void Configure(bool isOverlayActive) { }

        public void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info, float barWidth, float barSpacing, int barCount, SKPaint? basePaint, Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
        {
            if (!_isInitialized || canvas == null || spectrum == null || spectrum.Length < 2 || basePaint == null || info.Width <= 0 || info.Height <= 0)
            {
                Log.Warning("Invalid render parameters or renderer not initialized.");
                return;
            }

            float centerX = info.Width / 2, centerY = info.Height / 2, mainRadius = Math.Min(centerX, centerY) * RadiusProportion;
            int halfSpectrumLength = spectrum.Length / 2, actualBarCount = Math.Min(halfSpectrumLength, barCount);
            float[] downscaledSpectrum = ScaleSpectrum(spectrum, actualBarCount, halfSpectrumLength);
            float[] scaledSpectrum = SmoothSpectrum(downscaledSpectrum, actualBarCount);

            EnsureTrigArrays(actualBarCount);

            using var barPaint = basePaint.Clone();
            barPaint.IsAntialias = true;
            barPaint.Style = SKPaintStyle.Stroke;
            barPaint.StrokeWidth = barWidth;

            RenderBars(canvas, scaledSpectrum.AsSpan(), actualBarCount, centerX, centerY, mainRadius, barWidth, barPaint);
            drawPerformanceInfo?.Invoke(canvas, info);
        }

        private static float[] ScaleSpectrum(float[] spectrum, int targetCount, int halfSpectrumLength)
        {
            float[] scaledSpectrum = new float[targetCount];
            float blockSize = (float)halfSpectrumLength / targetCount;

            for (int i = 0; i < targetCount; i++)
            {
                int start = (int)(i * blockSize);
                int end = (int)((i + 1) * blockSize);
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

            var scaledSpectrum = new float[targetCount];

            for (int i = 0; i < targetCount; i++)
            {
                float currentValue = spectrum[i];
                float smoothedValue = _previousSpectrum[i] + (currentValue - _previousSpectrum[i]) * SmoothingFactor;
                scaledSpectrum[i] = Math.Clamp(smoothedValue, MinBarHeight, MaxBarHeight);
                _previousSpectrum[i] = scaledSpectrum[i];
            }

            return scaledSpectrum;
        }

        private void EnsureTrigArrays(int barCount)
        {
            if (_cosValues == null || _sinValues == null || _cosValues.Length != barCount)
            {
                _cosValues = new float[barCount];
                _sinValues = new float[barCount];

                for (int i = 0; i < barCount; i++)
                {
                    float angle = (float)(2 * Math.PI * i / barCount);
                    _cosValues[i] = (float)Math.Cos(angle);
                    _sinValues[i] = (float)Math.Sin(angle);
                }
            }
        }

        private void RenderBars(SKCanvas canvas, ReadOnlySpan<float> spectrum, int barCount, float centerX, float centerY, float mainRadius, float barWidth, SKPaint barPaint)
        {
            _path.Reset();

            for (int i = 0; i < barCount; i++)
            {
                float magnitude = spectrum[i];
                if (magnitude < MinMagnitudeThreshold) continue;

                float radius = mainRadius + magnitude * mainRadius * SpectrumMultiplier;
                AddBarToPath(i, centerX, centerY, mainRadius, radius);
            }

            canvas.DrawPath(_path, barPaint);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddBarToPath(int index, float centerX, float centerY, float mainRadius, float radius)
        {
            _path.MoveTo(centerX + mainRadius * _cosValues![index], centerY + mainRadius * _sinValues![index]);
            _path.LineTo(centerX + radius * _cosValues![index], centerY + radius * _sinValues![index]);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                _path.Dispose();
                _cosValues = null;
                _sinValues = null;
                _previousSpectrum = null;
            }

            _disposed = true;
            Log.Debug("CircularBarsRenderer disposed");
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}