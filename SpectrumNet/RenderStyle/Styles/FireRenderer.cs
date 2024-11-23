namespace SpectrumNet
{
    public class FireRenderer : ISpectrumRenderer
    {
        private static FireRenderer? _instance;
        private bool _isInitialized;
        private float[] _previousSpectrum = Array.Empty<float>();
        private const float DecayRate = 0.1f;
        private const float ControlPointProportion = 0.5f;
        private const float RandomOffsetProportion = 0.4f;
        private const float RandomOffsetCenter = 0.2f;
        private const float FlameBottomProportion = 0.2f;
        private const float FlameBottomMax = 5f;
        private readonly Random _random = new();

        private FireRenderer() { }

        public static FireRenderer GetInstance() => _instance ??= new FireRenderer();

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

            if (_previousSpectrum.Length != spectrum!.Length)
            {
                _previousSpectrum = new float[spectrum.Length];
                Array.Copy(spectrum, _previousSpectrum, spectrum.Length);
            }

            int actualBarCount = Math.Min(spectrum.Length / 2, barCount);
            float totalBarWidth = barWidth + barSpacing;

            using var paintClone = paint!.Clone();
            RenderFlames(canvas!, spectrum.AsSpan(), actualBarCount, totalBarWidth, barWidth, info.Height, paintClone);

            for (int i = 0; i < spectrum.Length; i++)
            {
                _previousSpectrum[i] = Math.Max(spectrum[i], _previousSpectrum[i] - DecayRate);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RenderFlames(SKCanvas canvas, ReadOnlySpan<float> spectrum, int barCount,
                                float totalBarWidth, float barWidth, float canvasHeight, SKPaint paint)
        {
            using var path = new SKPath();

            for (int i = 0; i < barCount; i++)
            {
                float x = i * totalBarWidth;
                float currentHeight = spectrum[i] * canvasHeight;
                float previousHeight = _previousSpectrum[i] * canvasHeight;

                RenderFlame(canvas, path, x, barWidth, currentHeight, previousHeight, canvasHeight, paint);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RenderFlame(SKCanvas canvas, SKPath path, float x, float barWidth,
                               float currentHeight, float previousHeight, float canvasHeight, SKPaint paint)
        {
            path.Reset();

            float flameTop = canvasHeight - Math.Max(currentHeight, previousHeight);
            float flameBottom = canvasHeight - Math.Min(currentHeight * FlameBottomProportion, FlameBottomMax);

            path.MoveTo(x, flameBottom);

            float controlY = flameTop + (flameBottom - flameTop) * ControlPointProportion;
            float randomOffset = (float)(_random.NextDouble() * barWidth * RandomOffsetProportion - barWidth * RandomOffsetCenter);

            path.QuadTo(
                x + barWidth * 0.5f + randomOffset, controlY,
                x + barWidth, flameBottom
            );

            byte alpha = (byte)(255 * Math.Min(currentHeight / canvasHeight * 1.5f, 1.0f));
            paint.Color = paint.Color.WithAlpha(alpha);

            canvas.DrawPath(path, paint);
        }

        public void Dispose()
        {
            _previousSpectrum = Array.Empty<float>();
        }
    }
}