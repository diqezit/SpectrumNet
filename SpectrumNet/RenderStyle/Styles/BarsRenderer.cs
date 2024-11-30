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
                           float barWidth, float barSpacing, int barCount, SKPaint? basePaint)
        {
            if (!_isInitialized || canvas == null || spectrum?.Length == 0 || basePaint == null || info.Width <= 0 || info.Height <= 0)
            {
                Log.Warning("Invalid render parameters or renderer not initialized.");
                return;
            }

            int actualBarCount = Math.Min(spectrum.Length, barCount);
            float totalBarWidth = barWidth + barSpacing;

            using var barPaint = basePaint.Clone();
            using var highlightPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = SKColors.White
            };

            float[] scaledSpectrum = ScaleSpectrum(spectrum, actualBarCount);
            float canvasHeight = info.Height;

            for (int i = 0; i < actualBarCount; i++)
            {
                float barHeight = Math.Max(scaledSpectrum[i] * canvasHeight, 1);
                float x = i * totalBarWidth;
                float cornerRadius = Math.Min(barWidth * DefaultCornerRadiusFactor, MaxCornerRadius);

                RenderBar(canvas, x, barWidth, barHeight, canvasHeight, cornerRadius, barPaint);
                RenderHighlight(canvas, x, barWidth, barHeight, canvasHeight, cornerRadius, highlightPaint);
            }
        }

        private float[] ScaleSpectrum(float[] spectrum, int barCount)
        {
            float[] scaledSpectrum = new float[barCount];
            int spectrumLength = spectrum.Length / 2;

            for (int i = 0; i < barCount; i++)
                scaledSpectrum[i] = spectrum[(int)((float)i / barCount * spectrumLength)];

            return scaledSpectrum;
        }

        private void RenderBar(SKCanvas canvas, float x, float barWidth, float barHeight,
                               float canvasHeight, float cornerRadius, SKPaint barPaint)
        {
            _path.Reset();
            _path.AddRoundRect(new SKRoundRect(
                new SKRect(x, canvasHeight - barHeight, x + barWidth, canvasHeight),
                cornerRadius, 0
            ));

            barPaint.Color = barPaint.Color.WithAlpha((byte)(255 * Math.Min(barHeight / canvasHeight * AlphaMultiplier, 1.0f)));
            canvas.DrawPath(_path, barPaint);
        }

        private void RenderHighlight(SKCanvas canvas, float x, float barWidth, float barHeight,
                                     float canvasHeight, float cornerRadius, SKPaint highlightPaint)
        {
            if (barHeight <= cornerRadius * 2) return;

            float highlightWidth = barWidth * HighlightWidthProportion;
            float highlightHeight = Math.Min(barHeight * HighlightHeightProportion, MaxHighlightHeight);

            highlightPaint.Color = highlightPaint.Color.WithAlpha(
                (byte)(255 * Math.Min(barHeight / canvasHeight * AlphaMultiplier, 1.0f) / HighlightAlphaDivisor)
            );

            canvas.DrawRect(
                x + (barWidth - highlightWidth) / 2,
                canvasHeight - barHeight + cornerRadius,
                highlightWidth,
                highlightHeight,
                highlightPaint
            );
        }

        public void Dispose()
        {
            if (_disposed) return;
            _path.Dispose();
            _disposed = true;
        }
    }
}