namespace SpectrumNet
{
    public class BarsRenderer : ISpectrumRenderer, IDisposable
    {
        private static BarsRenderer? _instance;
        private bool _isInitialized;
        private readonly SKPath _path = new();
        private volatile bool _disposed;

        // Константы
        private const float MaxCornerRadius = 10f;
        private const float HighlightWidthProportion = 0.6f;
        private const float HighlightHeightProportion = 0.1f;
        private const float MaxHighlightHeight = 5f;
        private const float AlphaMultiplier = 1.5f;
        private const float HighlightAlphaDivisor = 3f;
        private const float DefaultCornerRadiusFactor = 5.0f;

        private BarsRenderer() { }

        public static BarsRenderer GetInstance() => _instance ??= new BarsRenderer();

        public void Initialize()
        {
            if (_isInitialized) return;
            Log.Debug("BarsRenderer initialized");
            _isInitialized = true;
        }

        public void Configure(bool isOverlayActive) { }

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

            float[] scaledSpectrum = ScaleSpectrum(spectrum, actualBarCount, halfSpectrumLength);
            float canvasHeight = info.Height;

            using var barPaint = basePaint.Clone();
            barPaint.IsAntialias = true;

            using var highlightPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = SKColors.White
            };

            float maxAlpha = 255f * AlphaMultiplier;

            for (int i = 0; i < actualBarCount; i++)
            {
                float barHeight = MathF.Max(scaledSpectrum[i] * canvasHeight, 1f);
                float x = i * totalBarWidth;
                float cornerRadius = MathF.Min(barWidth * DefaultCornerRadiusFactor, MaxCornerRadius);

                // Вычисление альфа-канала
                byte alpha = (byte)MathF.Min(barHeight / canvasHeight * maxAlpha, 255f);
                barPaint.Color = barPaint.Color.WithAlpha(alpha);

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
            int step = halfSpectrumLength / barCount;

            for (int i = 0; i < barCount; i++)
            {
                scaledSpectrum[i] = spectrum[i * step];
            }

            return scaledSpectrum;
        }

        private void RenderBar(SKCanvas canvas, float x, float barWidth, float barHeight,
                               float canvasHeight, float cornerRadius, SKPaint barPaint)
        {
            _path.Reset();
            _path.AddRoundRect(new SKRoundRect(
                new SKRect(x, canvasHeight - barHeight, x + barWidth, canvasHeight),
                cornerRadius, cornerRadius
            ));
            canvas.DrawPath(_path, barPaint);
        }

        private void RenderHighlight(SKCanvas canvas, float x, float barWidth, float barHeight,
                                     float canvasHeight, float highlightWidth, float highlightHeight, SKPaint highlightPaint)
        {
            float highlightX = x + (barWidth - highlightWidth) / 2;
            float highlightY = canvasHeight - barHeight;

            canvas.DrawRect(highlightX, highlightY, highlightWidth, highlightHeight, highlightPaint);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _path.Dispose();
            _disposed = true;
        }
    }
}