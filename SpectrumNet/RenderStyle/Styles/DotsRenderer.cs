namespace SpectrumNet
{
    public class DotsRenderer : ISpectrumRenderer, IDisposable
    {
        private static DotsRenderer? _instance;
        private bool _isInitialized;
        private const float MinIntensityThreshold = 0.01f;
        private const float MinDotRadius = 2f;

        public DotsRenderer() { }

        public static DotsRenderer GetInstance() => _instance ??= new DotsRenderer();

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

            float totalWidth = barWidth + barSpacing;
            int halfLength = spectrum!.Length / 2;

            RenderDots(canvas!, spectrum.AsSpan(), info, totalWidth, barWidth, halfLength, paint!);
        }

        private void RenderDots(SKCanvas canvas, ReadOnlySpan<float> spectrum, SKImageInfo info,
                              float totalWidth, float barWidth, int halfLength, SKPaint paint)
        {
            for (int i = 0; i < halfLength; i++)
            {
                float intensity = spectrum[i];
                if (intensity < MinIntensityThreshold) continue;

                float dotRadius = Math.Max(barWidth / 2 * intensity, MinDotRadius);
                float x = i * totalWidth + dotRadius;
                float y = info.Height - (intensity * info.Height);

                RenderMainDot(canvas, x, y, dotRadius, intensity, paint);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RenderMainDot(SKCanvas canvas, float x, float y, float dotRadius,
                                 float intensity, SKPaint paint)
        {
            using var dotPaint = paint.Clone();
            dotPaint.Color = paint.Color.WithAlpha((byte)(255 * intensity));
            canvas.DrawCircle(x, y, dotRadius, dotPaint);
        }

        public void Dispose()
        {
            // No paints to dispose as clones are used
        }
    }
}