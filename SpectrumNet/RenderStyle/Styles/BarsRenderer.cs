namespace SpectrumNet
{
    public class BarsRenderer : ISpectrumRenderer
    {
        private static BarsRenderer? _instance;
        private bool _isInitialized;
        private readonly SKPath _path = new();
        private volatile bool _disposed;

        // Constants for magic numbers
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

        public void Configure(bool isOverlayActive)
        {
            // Configuration not required for this renderer
        }

        private bool AreRenderParamsValid(SKCanvas? canvas, ReadOnlySpan<float> spectrum, SKImageInfo info, SKPaint? paint)
        {
            if (canvas == null || spectrum.IsEmpty || paint == null || info.Width <= 0 || info.Height <= 0)
            {
                Log.Warning("Invalid render parameters");
                return false;
            }
            return true;
        }

        public void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info,
                           float barWidth, float barSpacing, int barCount, SKPaint? basePaint)
        {
            if (!_isInitialized)
            {
                Log.Warning("BarsRenderer is not initialized.");
                return;
            }

            if (!AreRenderParamsValid(canvas, spectrum.AsSpan(), info, basePaint)) return;

            int actualBarCount = Math.Min(spectrum!.Length, barCount);
            float totalBarWidth = barWidth + barSpacing;

            using var barPaint = basePaint!.Clone();
            SKPaint highlightPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = SKColors.White
            };

            // Масштабирование спектра
            float[] scaledSpectrum = ScaleSpectrum(spectrum, actualBarCount);

            RenderBars(canvas!, scaledSpectrum.AsSpan(), actualBarCount, totalBarWidth, barWidth,
                       barSpacing, info.Height, barPaint, highlightPaint);
        }

        private float[] ScaleSpectrum(float[] spectrum, int barCount)
        {
            int spectrumLength = spectrum.Length / 2;
            float[] scaledSpectrum = new float[barCount];

            for (int i = 0; i < barCount; i++)
            {
                int index = (int)((float)i / barCount * spectrumLength);
                scaledSpectrum[i] = spectrum[index];
            }

            return scaledSpectrum;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RenderBars(SKCanvas canvas, ReadOnlySpan<float> spectrum, int barCount,
                              float totalBarWidth, float barWidth, float barSpacing,
                              float canvasHeight, SKPaint barPaint, SKPaint highlightPaint)
        {
            float cornerRadiusFactor = DefaultCornerRadiusFactor;

            for (int i = 0; i < barCount; i++)
            {
                float barHeight = Math.Max(spectrum[i] * canvasHeight, 1);
                float x = i * totalBarWidth;

                float cornerRadius = Math.Min(barWidth * cornerRadiusFactor, MaxCornerRadius);
                RenderBar(canvas, x, barWidth, barHeight, canvasHeight, cornerRadius, barPaint);
                RenderHighlight(canvas, x, barWidth, barHeight, canvasHeight, cornerRadius, highlightPaint);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RenderBar(SKCanvas canvas, float x, float barWidth, float barHeight,
                             float canvasHeight, float cornerRadius, SKPaint barPaint)
        {
            _path.Reset();
            _path.AddRoundRect(new SKRoundRect(
                new SKRect(x, canvasHeight - barHeight, x + barWidth, canvasHeight),
                cornerRadius, 0
            ));

            byte alpha = (byte)(255 * Math.Min(barHeight / canvasHeight * AlphaMultiplier, 1.0f));
            barPaint.Color = barPaint.Color.WithAlpha(alpha);

            canvas.DrawPath(_path, barPaint);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RenderHighlight(SKCanvas canvas, float x, float barWidth, float barHeight,
                                   float canvasHeight, float cornerRadius, SKPaint highlightPaint)
        {
            if (barHeight <= cornerRadius * 2) return;

            float highlightWidth = barWidth * HighlightWidthProportion;
            float highlightHeight = Math.Min(barHeight * HighlightHeightProportion, MaxHighlightHeight);

            byte alpha = (byte)(255 * Math.Min(barHeight / canvasHeight * AlphaMultiplier, 1.0f) / HighlightAlphaDivisor);
            highlightPaint.Color = highlightPaint.Color.WithAlpha(alpha);

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