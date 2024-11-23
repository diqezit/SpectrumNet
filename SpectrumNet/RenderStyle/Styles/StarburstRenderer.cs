namespace SpectrumNet
{
    public class StarburstRenderer : ISpectrumRenderer
    {
        private static StarburstRenderer? _instance;
        private bool _isInitialized;

        // Constants for magic numbers
        private const float Pi = (float)Math.PI;
        private const float IntensityMultiplier = 1.0f;

        private StarburstRenderer() { }

        public static StarburstRenderer GetInstance() => _instance ??= new StarburstRenderer();

        public void Initialize()
        {
            if (_isInitialized) return;

            _isInitialized = true;
        }

        public void Configure(bool isOverlayActive)
        {
            // Configuration logic can be added here if needed
        }

        public void Render(SKCanvas canvas, float[] spectrum, SKImageInfo info, float barWidth, float barSpacing, int barCount, SKPaint paint)
        {
            if (!_isInitialized)
            {
                return;
            }

            float centerX = info.Width / 2;
            float centerY = info.Height / 2;
            float maxRadius = Math.Min(centerX, centerY);

            for (int i = 0; i < spectrum.Length; i++)
            {
                float intensity = spectrum[i];
                float angle = (float)(i * Pi * 2 / (spectrum.Length / 2));
                float x = centerX + (float)Math.Cos(angle) * intensity * IntensityMultiplier * maxRadius;
                float y = centerY + (float)Math.Sin(angle) * intensity * IntensityMultiplier * maxRadius;

                using (var clonedPaint = paint.Clone())
                {
                    clonedPaint.Color = clonedPaint.Color.WithAlpha((byte)(255 * intensity));
                    canvas.DrawLine(centerX, centerY, x, y, clonedPaint);
                }
            }
        }

        public void Dispose()
        {
            // Dispose resources if any
        }
    }
}