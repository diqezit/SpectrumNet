namespace SpectrumNet
{
    public class StarburstRenderer : ISpectrumRenderer
    {
        private static StarburstRenderer? _instance;
        private bool _isInitialized;

        private StarburstRenderer() { }

        public static StarburstRenderer GetInstance() => _instance ??= new StarburstRenderer();

        public void Initialize()
        {
            if (_isInitialized) return;

            Log.Debug("StarburstRenderer initialized");
            _isInitialized = true;
        }

        public void Configure(bool isOverlayActive)
        {
            // Configuration logic can be added here if needed
        }

        public void Render(SKCanvas canvas, float[] spectrum, SKImageInfo info, float barWidth, float barSpacing, int barCount, SKPaint basePaint)
        {
            if (!_isInitialized)
            {
                Log.Warning("StarburstRenderer is not initialized.");
                return;
            }

            float centerX = info.Width / 2;
            float centerY = info.Height / 2;
            float maxRadius = Math.Min(centerX, centerY);

            for (int i = 0; i < spectrum.Length; i++)
            {
                float intensity = spectrum[i];
                float angle = (float)(i * Math.PI * 2 / (spectrum.Length / 2));
                float x = centerX + (float)Math.Cos(angle) * intensity * maxRadius;
                float y = centerY + (float)Math.Sin(angle) * intensity * maxRadius;

                using var paint = basePaint.Clone();
                paint.Color = paint.Color.WithAlpha((byte)(255 * intensity));
                canvas.DrawLine(centerX, centerY, x, y, paint);
            }
        }

        public void Dispose()
        {
            // Dispose resources if any
        }
    }
}