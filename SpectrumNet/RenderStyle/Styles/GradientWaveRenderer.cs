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
            // Возможность настройки поведения рендера, если потребуется
        }

        public void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info, float barWidth, float barSpacing, int barCount, SKPaint? paint)
        {
            if (!_isInitialized)
            {
                Log.Warning("GradientWaveRenderer is not initialized.");
                return;
            }

            if (canvas == null || spectrum == null || spectrum.Length == 0 || paint == null)
            {
                Log.Warning("Invalid render parameters");
                return;
            }

            // Масштабирование спектра
            int actualBarCount = Math.Min(spectrum.Length / 2, barCount);
            float[] scaledSpectrum = ScaleSpectrum(spectrum, actualBarCount);

            using var gradientPaint = paint.Clone();
            ConfigureGradientPaint(gradientPaint, info.Width);

            using var path = CreateSpectrumPath(scaledSpectrum, info);
            canvas.DrawPath(path, gradientPaint);
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

        private void ConfigureGradientPaint(SKPaint gradientPaint, int width)
        {
            gradientPaint.Style = SKPaintStyle.Stroke;
            gradientPaint.StrokeWidth = 3; // Можно сделать ширину динамической
            gradientPaint.IsAntialias = true;
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
            float centerY = info.Height / 2;
            float step = info.Width / (float)(spectrum.Length - 1); // Шаг между точками

            path.MoveTo(0, centerY);

            for (int i = 1; i < spectrum.Length; i++)
            {
                float x = i * step;
                float y = centerY - spectrum[i] * centerY;
                path.LineTo(x, y); // Можно заменить на CubicTo для более плавных линий
            }

            return path;
        }

        public void Dispose()
        {
            _isInitialized = false;
            Log.Debug("GradientWaveRenderer disposed");
        }
    }
}