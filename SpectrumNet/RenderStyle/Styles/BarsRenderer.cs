namespace SpectrumNet
{
    public class BarsRenderer : ISpectrumRenderer, IDisposable
    {
        private static BarsRenderer? _instance;
        private bool _isInitialized;
        private readonly SKPath _path = new();
        private volatile bool _disposed;

        private const float MaxCornerRadius = 10f, HighlightWidthProportion = 0.6f,
                           HighlightHeightProportion = 0.1f, MaxHighlightHeight = 5f,
                           AlphaMultiplier = 1.5f, HighlightAlphaDivisor = 3f,
                           DefaultCornerRadiusFactor = 5.0f;

        private float[]? _previousSpectrum;
        private float _smoothingFactor = 0.3f;

        private BarsRenderer() { }

        public static BarsRenderer GetInstance() => _instance ??= new BarsRenderer();

        public void Initialize()
        {
            if (!_isInitialized)
            {
                _isInitialized = true;
                Log.Debug("BarsRenderer initialized");
            }
        }

        public void Configure(bool isOverlayActive)
        {
            _smoothingFactor = isOverlayActive ? 0.5f : 0.3f;
        }

        public void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info,
                           float barWidth, float barSpacing, int barCount, SKPaint? basePaint,
                           Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
        {
            if (!_isInitialized || canvas == null || spectrum == null || spectrum.Length < 2 ||
                basePaint == null || info.Width <= 0 || info.Height <= 0)
            {
                Log.Warning("Invalid render parameters or renderer not initialized.");
                return;
            }

            int halfSpectrumLength = spectrum.Length / 2;
            int actualBarCount = Math.Min(halfSpectrumLength, barCount);
            float totalBarWidth = barWidth + barSpacing;
            float canvasHeight = info.Height;

            using var barPaint = basePaint.Clone();
            barPaint.IsAntialias = true;

            using var highlightPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = SKColors.White
            };

            float[] scaledSpectrum = ScaleSpectrum(spectrum, actualBarCount, halfSpectrumLength);
            float[] smoothedSpectrum = SmoothSpectrum(scaledSpectrum, actualBarCount);

            for (int i = 0; i < actualBarCount; i++)
            {
                float barHeight = MathF.Max(smoothedSpectrum[i] * canvasHeight, 1f);
                byte alpha = (byte)MathF.Min(smoothedSpectrum[i] * AlphaMultiplier * 255f, 255f);
                barPaint.Color = barPaint.Color.WithAlpha(alpha);

                float x = i * totalBarWidth;
                float cornerRadius = MathF.Min(barWidth * DefaultCornerRadiusFactor, MaxCornerRadius);
                RenderBar(canvas, x, barWidth, barHeight, canvasHeight, cornerRadius, barPaint);

                if (barHeight > cornerRadius * 2)
                {
                    float highlightWidth = barWidth * HighlightWidthProportion;
                    float highlightHeight = MathF.Min(barHeight * HighlightHeightProportion, MaxHighlightHeight);
                    byte highlightAlpha = (byte)(alpha / HighlightAlphaDivisor);
                    highlightPaint.Color = highlightPaint.Color.WithAlpha(highlightAlpha);

                    RenderHighlight(canvas, x, barWidth, barHeight, canvasHeight, highlightWidth, highlightHeight, highlightPaint);
                }
            }

            drawPerformanceInfo?.Invoke(canvas, info);
        }

        private static float[] ScaleSpectrum(float[] spectrum, int barCount, int halfSpectrumLength)
        {
            float[] scaledSpectrum = new float[barCount];
            float blockSize = (float)halfSpectrumLength / barCount;

            for (int i = 0; i < barCount; i++)
            {
                float sum = 0;
                int start = (int)(i * blockSize);
                int end = (int)((i + 1) * blockSize);
                end = Math.Min(end, halfSpectrumLength);
                for (int j = start; j < end; j++)
                {
                    sum += spectrum[j];
                }
                scaledSpectrum[i] = sum / (end - start);
            }

            return scaledSpectrum;
        }

        private float[] SmoothSpectrum(float[] scaledSpectrum, int actualBarCount)
        {
            if (_previousSpectrum == null || _previousSpectrum.Length != actualBarCount)
            {
                _previousSpectrum = new float[actualBarCount];
            }

            float[] smoothedSpectrum = new float[actualBarCount];

            for (int i = 0; i < actualBarCount; i++)
            {
                smoothedSpectrum[i] = _previousSpectrum[i] * (1 - _smoothingFactor) + scaledSpectrum[i] * _smoothingFactor;
                _previousSpectrum[i] = smoothedSpectrum[i];
            }

            return smoothedSpectrum;
        }

        private void RenderBar(SKCanvas canvas, float x, float barWidth, float barHeight,
                               float canvasHeight, float cornerRadius, SKPaint barPaint)
        {
            _path.Reset();
            _path.AddRoundRect(new SKRoundRect(new SKRect(x, canvasHeight - barHeight, x + barWidth, canvasHeight), cornerRadius, cornerRadius));
            canvas.DrawPath(_path, barPaint);
        }

        private static void RenderHighlight(SKCanvas canvas, float x, float barWidth, float barHeight,
                                            float canvasHeight, float highlightWidth, float highlightHeight, SKPaint highlightPaint)
        {
            float highlightX = x + (barWidth - highlightWidth) / 2;
            canvas.DrawRect(highlightX, canvasHeight - barHeight, highlightWidth, highlightHeight, highlightPaint);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing)
            {
                _path.Dispose();
                _previousSpectrum = null;
            }
            _disposed = true;
            Log.Debug("BarsRenderer disposed");
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}