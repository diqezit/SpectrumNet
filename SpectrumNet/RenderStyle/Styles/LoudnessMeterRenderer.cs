namespace SpectrumNet
{
    public class LoudnessMeterRenderer : ISpectrumRenderer, IDisposable
    {
        private static LoudnessMeterRenderer? _instance;
        private bool _isInitialized;
        private const float MinLoudnessThreshold = 0.001f;

        public LoudnessMeterRenderer() { }

        public static LoudnessMeterRenderer GetInstance() => _instance ??= new LoudnessMeterRenderer();

        public void Initialize()
        {
            if (_isInitialized) return;
            _isInitialized = true;
        }

        public void Configure(bool isOverlayActive) { }

        private bool AreRenderParamsValid(SKCanvas? canvas, ReadOnlySpan<float> spectrum, SKImageInfo info, SKPaint? paint)
        {
            if (canvas == null || spectrum.IsEmpty || paint == null || info.Width <= 0 || info.Height <= 0)
                return false;
            return true;
        }

        public void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info,
                         float barWidth, float barSpacing, int barCount, SKPaint? paint)
        {
            if (!_isInitialized)
                return;

            if (!AreRenderParamsValid(canvas, spectrum.AsSpan(), info, paint))
                return;

            float loudness = CalculateLoudness(spectrum.AsSpan());
            if (loudness < MinLoudnessThreshold)
                return;

            RenderMeter(canvas!, info, loudness, paint!);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float CalculateLoudness(ReadOnlySpan<float> spectrum)
        {
            if (spectrum.IsEmpty)
                return 0f;

            float sum = 0f;
            for (int i = 0; i < spectrum.Length; i++)
                sum += Math.Abs(spectrum[i]);

            return sum / spectrum.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RenderMeter(SKCanvas canvas, SKImageInfo info, float loudness, SKPaint paint)
        {
            float meterHeight = info.Height * loudness;

            using var clonedPaint = paint.Clone();
            clonedPaint.Color = paint.Color.WithAlpha((byte)(loudness * 255));

            canvas.DrawRect(0, info.Height - meterHeight, info.Width, meterHeight, clonedPaint);
        }

        public void Dispose() { }
    }
}