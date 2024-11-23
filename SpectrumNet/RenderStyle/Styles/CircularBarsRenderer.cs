namespace SpectrumNet
{
    public class CircularBarsRenderer : ISpectrumRenderer
    {
        private static CircularBarsRenderer? _instance;
        private bool _isInitialized;
        private readonly SKPath _path = new();
        private float[]? _cosValues;
        private float[]? _sinValues;
        private int _lastBarCount;

        // Constants for magic numbers
        private const float RadiusProportion = 0.8f;
        private const float SpectrumMultiplier = 0.5f;
        private const float HighlightRadiusMultiplier = 0.1f;
        private const float HighlightWidthProportion = 0.6f;

        private CircularBarsRenderer() { }

        public static CircularBarsRenderer GetInstance() => _instance ??= new CircularBarsRenderer();

        public void Initialize()
        {
            if (_isInitialized) return;
            _isInitialized = true;
        }

        public void Configure(bool isOverlayActive)
        {
            // Configuration not required for this renderer
        }

        private bool AreRenderParamsValid(SKCanvas? canvas, ReadOnlySpan<float> spectrum, SKImageInfo info, SKPaint? paint)
        {
            if (canvas == null || spectrum.IsEmpty || paint == null || info.Width <= 0 || info.Height <= 0)
                return false;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureTrigArrays(int barCount)
        {
            if (_cosValues == null || _sinValues == null || _lastBarCount != barCount)
            {
                _cosValues = new float[barCount];
                _sinValues = new float[barCount];
                PrecomputeTrigValues(barCount);
                _lastBarCount = barCount;
            }
        }

        private void PrecomputeTrigValues(int barCount)
        {
            for (int i = 0; i < barCount; i++)
            {
                float angle = (float)(2 * Math.PI * i / barCount);
                _cosValues[i] = (float)Math.Cos(angle);
                _sinValues[i] = (float)Math.Sin(angle);
            }
        }

        public void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info,
                         float barWidth, float barSpacing, int barCount, SKPaint? paint)
        {
            if (!_isInitialized)
                return;

            if (!AreRenderParamsValid(canvas, spectrum.AsSpan(), info, paint))
                return;

            float centerX = info.Width / 2;
            float centerY = info.Height / 2;
            float mainRadius = Math.Min(centerX, centerY) * RadiusProportion;

            using var barPaint = paint!.Clone();
            barPaint.Style = SKPaintStyle.Stroke;
            barPaint.StrokeWidth = barWidth;

            int n = Math.Min(spectrum!.Length / 2, barCount);
            EnsureTrigArrays(n);

            RenderBars(canvas!, spectrum.AsSpan(), n, centerX, centerY, mainRadius, barWidth, barPaint, paint);
        }

        private void RenderBars(SKCanvas canvas, ReadOnlySpan<float> spectrum, int barCount,
                              float centerX, float centerY, float mainRadius, float barWidth,
                              SKPaint barPaint, SKPaint paint)
        {
            _path.Reset();

            for (int i = 0; i < barCount; i++)
            {
                float radius = mainRadius + spectrum[i] * mainRadius * SpectrumMultiplier;
                AddBarToPath(i, centerX, centerY, mainRadius, radius, spectrum[i], barWidth, paint);
            }

            canvas.DrawPath(_path, barPaint);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddBarToPath(int index, float centerX, float centerY, float mainRadius, float radius,
                                float magnitude, float barWidth, SKPaint paint)
        {
            float startX = centerX + mainRadius * _cosValues[index];
            float startY = centerY + mainRadius * _sinValues[index];
            float endX = centerX + radius * _cosValues[index];
            float endY = centerY + radius * _sinValues[index];

            _path.MoveTo(startX, startY);
            _path.LineTo(endX, endY);

            if (radius - mainRadius > barWidth * 2)
                AddHighlight(index, centerX, centerY, mainRadius, magnitude, barWidth);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddHighlight(int index, float centerX, float centerY, float mainRadius,
                                float magnitude, float barWidth)
        {
            float highlightRadius = mainRadius + (magnitude * mainRadius * HighlightRadiusMultiplier);
            float highlightStartX = centerX + highlightRadius * _cosValues[index];
            float highlightStartY = centerY + highlightRadius * _sinValues[index];
            float highlightEndX = centerX + (highlightRadius + barWidth * HighlightWidthProportion) * _cosValues[index];
            float highlightEndY = centerY + (highlightRadius + barWidth * HighlightWidthProportion) * _sinValues[index];

            _path.MoveTo(highlightStartX, highlightStartY);
            _path.LineTo(highlightEndX, highlightEndY);
        }

        public void Dispose()
        {
            _path.Dispose();
            _cosValues = null;
            _sinValues = null;
        }
    }
}