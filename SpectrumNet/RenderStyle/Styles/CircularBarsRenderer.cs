namespace SpectrumNet
{
    public class CircularBarsRenderer : ISpectrumRenderer, IDisposable
    {
        private static CircularBarsRenderer? _instance;
        private bool _isInitialized;
        private readonly SKPath _path = new();
        private float[]? _cosValues;
        private float[]? _sinValues;
        private int _lastBarCount;

        // Константы
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
            Log.Debug("CircularBarsRenderer initialized");
        }

        public void Configure(bool isOverlayActive)
        {
            // В будущем можно добавить обработку конфигураций
            Log.Debug($"CircularBarsRenderer configured. Overlay active: {isOverlayActive}");
        }

        public void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info,
                           float barWidth, float barSpacing, int barCount, SKPaint? basePaint)
        {
            if (!_isInitialized || canvas == null || spectrum == null || spectrum.Length == 0 || basePaint == null)
            {
                Log.Warning("Invalid render parameters or CircularBarsRenderer is not initialized.");
                return;
            }

            float centerX = info.Width / 2;
            float centerY = info.Height / 2;
            float mainRadius = Math.Min(centerX, centerY) * RadiusProportion;

            using var barPaint = basePaint.Clone();
            barPaint.Style = SKPaintStyle.Stroke;
            barPaint.StrokeWidth = barWidth;

            int actualBarCount = Math.Min(spectrum.Length / 2, barCount);
            float[] scaledSpectrum = ScaleSpectrum(spectrum, actualBarCount);

            EnsureTrigArrays(actualBarCount);
            RenderBars(canvas, scaledSpectrum.AsSpan(), actualBarCount, centerX, centerY, mainRadius, barWidth, barPaint);
        }

        private float[] ScaleSpectrum(float[] spectrum, int targetCount)
        {
            float[] scaledSpectrum = new float[targetCount];
            int spectrumLength = spectrum.Length / 2;

            for (int i = 0; i < targetCount; i++)
            {
                int index = (int)((float)i / targetCount * spectrumLength);
                scaledSpectrum[i] = spectrum[index];
            }

            return scaledSpectrum;
        }

        private void EnsureTrigArrays(int barCount)
        {
            if (_cosValues == null || _sinValues == null || _lastBarCount != barCount)
            {
                _cosValues = new float[barCount];
                _sinValues = new float[barCount];

                for (int i = 0; i < barCount; i++)
                {
                    float angle = (float)(2 * Math.PI * i / barCount);
                    _cosValues[i] = (float)Math.Cos(angle);
                    _sinValues[i] = (float)Math.Sin(angle);
                }

                _lastBarCount = barCount;
            }
        }

        private void RenderBars(SKCanvas canvas, ReadOnlySpan<float> spectrum, int barCount,
                                float centerX, float centerY, float mainRadius, float barWidth, SKPaint barPaint)
        {
            _path.Reset();

            for (int i = 0; i < barCount; i++)
            {
                float magnitude = spectrum[i];
                float radius = mainRadius + magnitude * mainRadius * SpectrumMultiplier;

                AddBarToPath(i, centerX, centerY, mainRadius, radius, magnitude, barWidth);
            }

            canvas.DrawPath(_path, barPaint);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddBarToPath(int index, float centerX, float centerY, float mainRadius, float radius, float magnitude, float barWidth)
        {
            float startX = centerX + mainRadius * _cosValues![index];
            float startY = centerY + mainRadius * _sinValues![index];
            float endX = centerX + radius * _cosValues![index];
            float endY = centerY + radius * _sinValues![index];

            _path.MoveTo(startX, startY);
            _path.LineTo(endX, endY);

            if (radius - mainRadius > barWidth * 2)
                AddHighlight(index, centerX, centerY, mainRadius, magnitude, barWidth);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddHighlight(int index, float centerX, float centerY, float mainRadius, float magnitude, float barWidth)
        {
            float highlightRadius = mainRadius + magnitude * mainRadius * HighlightRadiusMultiplier;
            float highlightStartX = centerX + highlightRadius * _cosValues![index];
            float highlightStartY = centerY + highlightRadius * _sinValues![index];
            float highlightEndX = centerX + (highlightRadius + barWidth * HighlightWidthProportion) * _cosValues![index];
            float highlightEndY = centerY + (highlightRadius + barWidth * HighlightWidthProportion) * _sinValues![index];

            _path.MoveTo(highlightStartX, highlightStartY);
            _path.LineTo(highlightEndX, highlightEndY);
        }

        public void Dispose()
        {
            _path.Dispose();
            _cosValues = null;
            _sinValues = null;
            _isInitialized = false;
            Log.Debug("CircularBarsRenderer disposed");
        }
    }
}