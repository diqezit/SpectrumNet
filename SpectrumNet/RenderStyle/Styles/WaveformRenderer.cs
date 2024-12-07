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
        private float[]? _previousSpectrum;
        private const float SmoothingFactorNormal = 0.3f;
        private const float SmoothingFactorOverlay = 0.5f;
        private float _smoothingFactor = SmoothingFactorNormal;
        private const float MinMagnitudeThreshold = 0.01f;
        private const float MaxSpectrumValue = 1.5f;
        private bool _disposed = false;

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
            _smoothingFactor = isOverlayActive ? SmoothingFactorOverlay : SmoothingFactorNormal;
        }

        public void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info,
                           float barWidth, float barSpacing, int barCount, SKPaint? paint,
                           Action<SKCanvas, SKImageInfo>? drawPerformanceInfo)
        {
            if (!_isInitialized || canvas is null || spectrum is null || spectrum.Length == 0 || paint is null)
            {
                Log.Warning("Invalid render parameters or WaveformRenderer is not initialized.");
                return;
            }

            int actualBarCount = Math.Min(spectrum.Length / 2, barCount);
            float[] scaledSpectrum = ScaleSpectrum(spectrum.AsSpan(), actualBarCount);
            float[] smoothedSpectrum = SmoothSpectrum(scaledSpectrum.AsSpan(), actualBarCount);

            float midY = info.Height / 2;
            float xStep = (float)info.Width / actualBarCount;

            using var waveformPaint = paint.Clone();
            waveformPaint.Style = SKPaintStyle.Stroke;
            waveformPaint.StrokeWidth = Math.Max(2f, 50f / actualBarCount);

            using var fillPaint = paint.Clone();
            fillPaint.Style = SKPaintStyle.Fill;
            fillPaint.Color = fillPaint.Color.WithAlpha(64);

            CreateWavePaths(smoothedSpectrum.AsSpan(), actualBarCount, midY, xStep);
            CreateFillPath(info.Width, midY);

            canvas.DrawPath(_fillPath, fillPaint);
            canvas.DrawPath(_topPath, waveformPaint);
            canvas.DrawPath(_bottomPath, waveformPaint);

            drawPerformanceInfo?.Invoke(canvas, info);
        }

        private float[] ScaleSpectrum(Span<float> spectrum, int targetCount)
        {
            int spectrumLength = spectrum.Length / 2;
            float[] scaledSpectrum = new float[targetCount];
            float blockSize = (float)spectrumLength / targetCount;

            for (int i = 0; i < targetCount; i++)
            {
                float sum = 0;
                int start = (int)(i * blockSize);
                int end = (int)((i + 1) * blockSize);
                int actualEnd = Math.Min(end, spectrumLength);
                int count = actualEnd - start;

                if (count <= 0)
                {
                    scaledSpectrum[i] = 0;
                    continue;
                }

                for (int j = start; j < actualEnd; j++)
                {
                    sum += spectrum[j];
                }

                scaledSpectrum[i] = sum / count;
            }

            return scaledSpectrum;
        }

        private float[] SmoothSpectrum(Span<float> spectrum, int targetCount)
        {
            if (_previousSpectrum == null || _previousSpectrum.Length != targetCount)
                _previousSpectrum = new float[targetCount];

            var smoothedSpectrum = new float[targetCount];

            for (int i = 0; i < targetCount; i++)
            {
                float currentValue = spectrum[i];
                float smoothedValue = _previousSpectrum[i] + (currentValue - _previousSpectrum[i]) * _smoothingFactor;
                smoothedSpectrum[i] = Math.Clamp(smoothedValue, MinMagnitudeThreshold, MaxSpectrumValue);
                _previousSpectrum[i] = smoothedSpectrum[i];
            }

            return smoothedSpectrum;
        }

        private void CreateWavePaths(Span<float> spectrum, int barCount, float midY, float xStep)
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

                float controlX = (prevX + x) / 2;
                _topPath.CubicTo(controlX, prevTopY, controlX, topY, x, topY);
                _bottomPath.CubicTo(controlX, prevBottomY, controlX, bottomY, x, bottomY);
            }
        }

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
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                // Dispose managed resources
                _topPath.Dispose();
                _bottomPath.Dispose();
                _fillPath.Dispose();
                _previousSpectrum = null;
            }

            // Release unmanaged resources here
            // Not applicable in this case

            _disposed = true;
        }

        ~WaveformRenderer()
        {
            Dispose(disposing: false);
        }
    }
}