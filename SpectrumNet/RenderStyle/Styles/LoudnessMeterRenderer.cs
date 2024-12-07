#nullable enable

namespace SpectrumNet
{
    public class LoudnessMeterRenderer : ISpectrumRenderer, IDisposable
    {
        private static LoudnessMeterRenderer? _instance;
        private bool _isInitialized;
        private bool _disposed = false;
        private const float MinLoudnessThreshold = 0.001f;
        private const float SmoothingFactorNormal = 0.3f;
        private const float SmoothingFactorOverlay = 0.5f;
        private float _smoothingFactor = SmoothingFactorNormal;
        private float _previousLoudness = 0f;

        public LoudnessMeterRenderer() { }

        public static LoudnessMeterRenderer GetInstance() => _instance ??= new LoudnessMeterRenderer();

        public void Initialize()
        {
            if (_isInitialized) return;
            _isInitialized = true;
            _previousLoudness = 0f;
        }

        public void Configure(bool isOverlayActive)
        {
            _smoothingFactor = isOverlayActive ? SmoothingFactorOverlay : SmoothingFactorNormal;
        }

        private bool AreRenderParamsValid(SKCanvas? canvas, ReadOnlySpan<float> spectrum, SKImageInfo info, SKPaint? paint)
        {
            if (canvas == null || spectrum.IsEmpty || paint == null || info.Width <= 0 || info.Height <= 0)
                return false;
            return true;
        }

        public void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info,
                           float barWidth, float barSpacing, int barCount, SKPaint? paint,
                           Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(LoudnessMeterRenderer));
            }

            if (!_isInitialized)
                return;

            if (!AreRenderParamsValid(canvas, spectrum.AsSpan(), info, paint))
                return;

            float loudness = CalculateLoudness(spectrum.AsSpan());
            if (loudness < MinLoudnessThreshold)
                return;

            float smoothedLoudness = _previousLoudness + (loudness - _previousLoudness) * _smoothingFactor;
            smoothedLoudness = Math.Clamp(smoothedLoudness, MinLoudnessThreshold, 1f);
            _previousLoudness = smoothedLoudness;

            RenderMeter(canvas!, info, smoothedLoudness, paint!);

            // Отрисовка информации о производительности
            drawPerformanceInfo(canvas!, info);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float CalculateLoudness(ReadOnlySpan<float> spectrum)
        {
            if (spectrum.IsEmpty)
                return 0f;

            float sum = 0f;
            for (int i = 0; i < spectrum.Length; i++)
                sum += Math.Abs(spectrum[i]);

            return Math.Clamp(sum / spectrum.Length, 0f, 1f);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RenderMeter(SKCanvas canvas, SKImageInfo info, float loudness, SKPaint paint)
        {
            float meterHeight = info.Height * loudness;

            using var clonedPaint = paint.Clone();
            clonedPaint.Color = paint.Color.WithAlpha((byte)(loudness * 255));

            canvas.DrawRect(0, info.Height - meterHeight, info.Width, meterHeight, clonedPaint);
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
                // Dispose managed resources here if any
            }

            _disposed = true;
        }

        ~LoudnessMeterRenderer()
        {
            Dispose(disposing: false);
        }
    }
}