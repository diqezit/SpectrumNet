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

        private CircularBarsRenderer() { }

        public static CircularBarsRenderer GetInstance() => _instance ??= new CircularBarsRenderer();

        public void Initialize()
        {
            if (_isInitialized) return;

            Log.Debug("CircularBarsRenderer initialized");
            _isInitialized = true;
        }

        public void Configure(bool isOverlayActive)
        {
            // Конфигурация не требуется для этого рендерера
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
                _cosValues![i] = (float)Math.Cos(angle);
                _sinValues![i] = (float)Math.Sin(angle);
            }
        }

        public void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info,
                         float barWidth, float barSpacing, int barCount, SKPaint? basePaint)
        {
            if (!_isInitialized)
            {
                Log.Warning("CircularBarsRenderer is not initialized.");
                return;
            }

            if (!AreRenderParamsValid(canvas, spectrum.AsSpan(), info, basePaint)) return;

            float centerX = info.Width / 2;
            float centerY = info.Height / 2;
            float mainRadius = Math.Min(centerX, centerY) * 0.8f;

            using var barPaint = basePaint!.Clone();
            barPaint.Style = SKPaintStyle.Stroke;
            barPaint.StrokeWidth = barWidth;

            int n = Math.Min(spectrum!.Length / 2, barCount);
            EnsureTrigArrays(n);

            RenderBars(canvas!, spectrum.AsSpan(), n, centerX, centerY, mainRadius, barWidth, barPaint, basePaint);
        }

        private void RenderBars(SKCanvas canvas, ReadOnlySpan<float> spectrum, int barCount,
                              float centerX, float centerY, float mainRadius, float barWidth,
                              SKPaint barPaint, SKPaint basePaint)
        {
            _path.Reset();

            for (int i = 0; i < barCount; i++)
            {
                float radius = mainRadius + spectrum[i] * mainRadius * 0.5f;
                AddBarToPath(i, centerX, centerY, mainRadius, radius, spectrum[i], barWidth, basePaint);
            }

            canvas.DrawPath(_path, barPaint);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddBarToPath(int index, float centerX, float centerY, float mainRadius, float radius,
                                float magnitude, float barWidth, SKPaint basePaint)
        {
            float startX = centerX + mainRadius * _cosValues![index];
            float startY = centerY + mainRadius * _sinValues![index];
            float endX = centerX + radius * _cosValues[index];
            float endY = centerY + radius * _sinValues[index];

            _path.MoveTo(startX, startY);
            _path.LineTo(endX, endY);

            if (radius - mainRadius > barWidth * 2)
            {
                AddHighlight(index, centerX, centerY, mainRadius, magnitude, barWidth);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddHighlight(int index, float centerX, float centerY, float mainRadius,
                                float magnitude, float barWidth)
        {
            float highlightRadius = mainRadius + (magnitude * mainRadius * 0.1f);
            float highlightStartX = centerX + highlightRadius * _cosValues![index];
            float highlightStartY = centerY + highlightRadius * _sinValues![index];
            float highlightEndX = centerX + (highlightRadius + barWidth * 0.6f) * _cosValues[index];
            float highlightEndY = centerY + (highlightRadius + barWidth * 0.6f) * _sinValues[index];

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