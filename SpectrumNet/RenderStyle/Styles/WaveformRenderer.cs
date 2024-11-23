namespace SpectrumNet
{
    public class WaveformRenderer : ISpectrumRenderer
    {
        private static WaveformRenderer? _instance;
        private bool _isInitialized;
        private readonly SKPath _topPath = new();
        private readonly SKPath _bottomPath = new();
        private readonly SKPath _fillPath = new();

        private WaveformRenderer() { }

        public static WaveformRenderer GetInstance() => _instance ??= new WaveformRenderer();

        public void Initialize()
        {
            if (_isInitialized) return;
            Log.Debug("WaveformRenderer initialized");
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

        public void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info,
                         float barWidth, float barSpacing, int barCount, SKPaint? basePaint)
        {
            if (!_isInitialized)
            {
                Log.Warning("WaveformRenderer is not initialized.");
                return;
            }

            if (!AreRenderParamsValid(canvas, spectrum.AsSpan(), info, basePaint)) return;

            float midY = info.Height / 2;
            int spectrumMiddle = spectrum!.Length / 2;
            float xStep = (float)info.Width / spectrumMiddle;

            using var waveformPaint = basePaint!.Clone();
            waveformPaint.Style = SKPaintStyle.Stroke;
            waveformPaint.StrokeWidth = 2;

            using var fillPaint = basePaint!.Clone();
            fillPaint.Style = SKPaintStyle.Fill;
            fillPaint.Color = fillPaint.Color.WithAlpha(64);

            RenderWaveform(canvas!, spectrum.AsSpan(), spectrumMiddle, midY, xStep,
                          info.Width, waveformPaint, fillPaint);
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

            _topPath.MoveTo(0, midY - (spectrum[0] * midY));
            _bottomPath.MoveTo(0, midY + (spectrum[0] * midY));

            for (int i = 1; i < spectrumMiddle; i++)
            {
                float x = i * xStep;
                float topY = midY - (spectrum[i] * midY);
                float bottomY = midY + (spectrum[i] * midY);

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