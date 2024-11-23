namespace SpectrumNet
{
    public class WaveformRenderer : ISpectrumRenderer
    {
        private static WaveformRenderer? _instance;
        private bool _isInitialized;
        private readonly SKPath _topPath = new();
        private readonly SKPath _bottomPath = new();
        private readonly SKPath _fillPath = new();

        // Constants for magic numbers
        private const float StrokeWidth = 2f;
        private const byte FillAlpha = 64;

        private WaveformRenderer() { }

        public static WaveformRenderer GetInstance() => _instance ??= new WaveformRenderer();

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
            return canvas != null && !spectrum.IsEmpty && paint != null && info.Width > 0 && info.Height > 0;
        }

        public void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info,
                         float barWidth, float barSpacing, int barCount, SKPaint? paint)
        {
            if (!_isInitialized || canvas == null || spectrum == null || paint == null)
                return;

            if (!AreRenderParamsValid(canvas, spectrum.AsSpan(), info, paint)) return;

            float midY = info.Height / 2;
            int spectrumMiddle = spectrum.Length / 2;
            float xStep = (float)info.Width / spectrumMiddle;

            using var waveformPaint = paint.Clone();
            waveformPaint.Style = SKPaintStyle.Stroke;
            waveformPaint.StrokeWidth = StrokeWidth;

            using var fillPaint = paint.Clone();
            fillPaint.Style = SKPaintStyle.Fill;
            fillPaint.Color = fillPaint.Color.WithAlpha(FillAlpha);

            RenderWaveform(canvas, spectrum.AsSpan(), spectrumMiddle, midY, xStep, info.Width, waveformPaint, fillPaint);
        }

        private void RenderWaveform(SKCanvas canvas, ReadOnlySpan<float> spectrum,
                                    int spectrumMiddle, float midY, float xStep, float width,
                                    SKPaint waveformPaint, SKPaint fillPaint)
        {
            CreateWavePaths(spectrum, spectrumMiddle, midY, xStep);
            CreateFillPath(width, midY);

            canvas.DrawPath(_fillPath, fillPaint);
            canvas.DrawPath(_topPath, waveformPaint);
            canvas.DrawPath(_bottomPath, waveformPaint);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CreateWavePaths(ReadOnlySpan<float> spectrum, int spectrumMiddle,
                                     float midY, float xStep)
        {
            _topPath.Reset();
            _bottomPath.Reset();

            float x = 0;
            float topY = midY - (spectrum[0] * midY);
            float bottomY = midY + (spectrum[0] * midY);

            _topPath.MoveTo(x, topY);
            _bottomPath.MoveTo(x, bottomY);

            for (int i = 1; i < spectrumMiddle; i++)
            {
                x = i * xStep;
                topY = midY - (spectrum[i] * midY);
                bottomY = midY + (spectrum[i] * midY);

                _topPath.LineTo(x, topY);
                _bottomPath.LineTo(x, bottomY);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CreateFillPath(float width, float midY)
        {
            _fillPath.Reset();
            _fillPath.AddPath(_topPath);
            _fillPath.LineTo(width, midY);
            _fillPath.LineTo(0, midY);
            _fillPath.Close();
        }

        public void Dispose()
        {
            _topPath.Dispose();
            _bottomPath.Dispose();
            _fillPath.Dispose();
        }
    }
}