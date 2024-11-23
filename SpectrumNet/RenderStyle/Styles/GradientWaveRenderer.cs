#nullable enable

namespace SpectrumNet
{
    public class GradientWaveRenderer : ISpectrumRenderer, IDisposable
    {
        private static GradientWaveRenderer? _instance;
        private bool _isInitialized;

        private GradientWaveRenderer() { }

        public static GradientWaveRenderer GetInstance() => _instance ??= new GradientWaveRenderer();

        public void Initialize()
        {
            if (_isInitialized) return;

            Log.Debug("GradientWaveRenderer initialized");
            _isInitialized = true;
        }

        public void Configure(bool isOverlayActive)
        {
            // Configuration logic can be added here if needed
        }

        public void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info, float barWidth, float barSpacing, int barCount, SKPaint? paint)
        {
            if (!_isInitialized)
            {
                Log.Warning("GradientWaveRenderer is not initialized.");
                return;
            }

            if (canvas == null || spectrum == null || paint == null)
            {
                Log.Warning("Invalid render parameters");
                return;
            }

            using var gradientPaint = paint.Clone();
            ConfigureGradientPaint(gradientPaint, info.Width);

            using var path = CreateSpectrumPath(spectrum, info);
            canvas.DrawPath(path, gradientPaint);
        }

        private void ConfigureGradientPaint(SKPaint gradientPaint, int width)
        {
            gradientPaint.Style = SKPaintStyle.Stroke;
            gradientPaint.StrokeWidth = 2;
            gradientPaint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(width, 0),
                new SKColor[] { SKColors.Blue, SKColors.Purple, SKColors.Red },
                null,
                SKShaderTileMode.Clamp);
        }

        private SKPath CreateSpectrumPath(float[] spectrum, SKImageInfo info)
        {
            var path = new SKPath();
            path.MoveTo(0, info.Height / 2);

            float step = info.Width / ((float)spectrum.Length / 2);

            for (int i = 0; i < spectrum.Length; i++)
            {
                float x = i * step;
                float y = info.Height / 2 - spectrum[i] * info.Height / 2;
                path.LineTo(x, y);
            }

            return path;
        }

        public void Dispose()
        {
            // Dispose resources if any
        }
    }
}