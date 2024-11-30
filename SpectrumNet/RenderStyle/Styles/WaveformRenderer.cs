#nullable enable

namespace SpectrumNet
{
    public class WaveformRenderer : ISpectrumRenderer, IDisposable
    {
        private static WaveformRenderer? _instance;
        private bool _isInitialized;
        private readonly SKPath _topPath = new();
        private readonly SKPath _bottomPath = new();
        private readonly SKPath _fillPath = new();

        // Constants
        private const float StrokeWidth = 2f;
        private const byte FillAlpha = 64;

        private WaveformRenderer() { }

        public static WaveformRenderer GetInstance() => _instance ??= new WaveformRenderer();

        public void Initialize()
        {
            if (_isInitialized) return;
            _isInitialized = true;
            Log.Debug("WaveformRenderer initialized");
        }

        public void Configure(bool isOverlayActive)
        {
            // Optional configuration logic
            Log.Debug($"WaveformRenderer configured. Overlay active: {isOverlayActive}");
        }

        public void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info,
                           float barWidth, float barSpacing, int barCount, SKPaint? paint)
        {
            if (!_isInitialized || canvas == null || spectrum == null || spectrum.Length == 0 || paint == null)
            {
                Log.Warning("Invalid render parameters or WaveformRenderer is not initialized.");
                return;
            }

            // Масштабирование спектра
            int actualBarCount = Math.Min(spectrum.Length / 2, barCount);
            float[] scaledSpectrum = ScaleSpectrum(spectrum, actualBarCount);

            float midY = info.Height / 2;
            float xStep = (float)info.Width / actualBarCount;

            using var waveformPaint = paint.Clone();
            waveformPaint.Style = SKPaintStyle.Stroke;
            waveformPaint.StrokeWidth = Math.Max(StrokeWidth, 50f / actualBarCount); // Динамическая ширина линии

            using var fillPaint = paint.Clone();
            fillPaint.Style = SKPaintStyle.Fill;
            fillPaint.Color = fillPaint.Color.WithAlpha(FillAlpha);

            RenderWaveform(canvas, scaledSpectrum.AsSpan(), actualBarCount, midY, xStep, info.Width, waveformPaint, fillPaint);
        }

        private float[] ScaleSpectrum(float[] spectrum, int targetCount)
        {
            int spectrumLength = spectrum.Length / 2;
            float[] scaledSpectrum = new float[targetCount];
            float blockSize = (float)spectrumLength / targetCount;

            for (int i = 0; i < targetCount; i++)
            {
                float sum = 0;
                int start = (int)(i * blockSize);
                int end = (int)((i + 1) * blockSize);

                for (int j = start; j < end && j < spectrumLength; j++)
                {
                    sum += spectrum[j];
                }

                scaledSpectrum[i] = sum / (end - start); // Усреднение значений в блоке
            }

            return scaledSpectrum;
        }

        private void RenderWaveform(SKCanvas canvas, ReadOnlySpan<float> spectrum,
                                    int barCount, float midY, float xStep, float width,
                                    SKPaint waveformPaint, SKPaint fillPaint)
        {
            CreateWavePaths(spectrum, barCount, midY, xStep);
            CreateFillPath(width, midY);

            canvas.DrawPath(_fillPath, fillPaint);
            canvas.DrawPath(_topPath, waveformPaint);
            canvas.DrawPath(_bottomPath, waveformPaint);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CreateWavePaths(ReadOnlySpan<float> spectrum, int barCount,
                                     float midY, float xStep)
        {
            _topPath.Reset();
            _bottomPath.Reset();

            float x = 0;
            float topY = midY - (spectrum[0] * midY);
            float bottomY = midY + (spectrum[0] * midY);

            _topPath.MoveTo(x, topY);
            _bottomPath.MoveTo(x, bottomY);

            for (int i = 1; i < barCount; i++)
            {
                float prevX = (i - 1) * xStep;
                float prevTopY = midY - (spectrum[i - 1] * midY);
                float prevBottomY = midY + (spectrum[i - 1] * midY);

                x = i * xStep;
                topY = midY - (spectrum[i] * midY);
                bottomY = midY + (spectrum[i] * midY);

                // Плавные кривые через CubicTo
                float controlX = (prevX + x) / 2;
                _topPath.CubicTo(controlX, prevTopY, controlX, topY, x, topY);
                _bottomPath.CubicTo(controlX, prevBottomY, controlX, bottomY, x, bottomY);
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
            _isInitialized = false;
            Log.Debug("WaveformRenderer disposed");
        }
    }
}