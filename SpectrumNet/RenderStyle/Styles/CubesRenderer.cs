namespace SpectrumNet
{
    public class CubesRenderer : ISpectrumRenderer, IDisposable
    {
        private static CubesRenderer? _instance;
        private bool _isInitialized;
        private readonly SKPath _cubePath = new();
        private SKPaint? _cubePaint;

        // Константы
        private const float MinMagnitudeThreshold = 0.01f;
        private const float CubeTopWidthProportion = 0.75f;
        private const float CubeTopHeightProportion = 0.25f;

        private CubesRenderer() { }

        public static CubesRenderer GetInstance() => _instance ??= new CubesRenderer();

        public void Initialize()
        {
            if (_isInitialized) return;

            _cubePaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };

            _isInitialized = true;
            Log.Debug("CubesRenderer initialized");
        }

        public void Configure(bool isOverlayActive)
        {
            // Возможность настройки поведения рендера, если потребуется
        }

        public void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info,
                           float barWidth, float barSpacing, int barCount, SKPaint? basePaint)
        {
            if (!_isInitialized || canvas == null || spectrum == null || spectrum.Length == 0 || basePaint == null)
            {
                Log.Warning("Invalid render parameters or CubesRenderer is not initialized.");
                return;
            }

            int actualBarCount = Math.Min(spectrum.Length / 2, barCount); // Используем половину спектра
            float[] scaledSpectrum = ScaleSpectrum(spectrum, actualBarCount);
            float totalWidth = barWidth + barSpacing;

            for (int i = 0; i < actualBarCount; i++)
            {
                float magnitude = scaledSpectrum[i];
                if (magnitude < MinMagnitudeThreshold) continue;

                float height = magnitude * info.Height;
                float x = i * totalWidth;
                float y = info.Height - height;

                using var clonedPaint = basePaint.Clone();
                RenderCube(canvas, x, y, barWidth, height, magnitude, clonedPaint);
            }
        }

        private float[] ScaleSpectrum(float[] spectrum, int targetCount)
        {
            float[] scaledSpectrum = new float[targetCount];
            int spectrumLength = spectrum.Length / 2; // Берём только половину спектра

            for (int i = 0; i < targetCount; i++)
            {
                int index = (int)((float)i / targetCount * spectrumLength);
                scaledSpectrum[i] = spectrum[index];
            }

            return scaledSpectrum;
        }

        private void RenderCube(SKCanvas canvas, float x, float y, float barWidth,
                                float height, float magnitude, SKPaint paint)
        {
            paint.Color = paint.Color.WithAlpha((byte)(magnitude * 255));
            canvas.DrawRect(x, y, barWidth, height, paint);

            RenderCubeTop(canvas, x, y, barWidth, magnitude, paint);
        }

        private void RenderCubeTop(SKCanvas canvas, float x, float y, float barWidth,
                                   float magnitude, SKPaint paint)
        {
            _cubePath.Reset();
            _cubePath.MoveTo(x, y);
            _cubePath.LineTo(x + barWidth, y);
            _cubePath.LineTo(x + barWidth * CubeTopWidthProportion, y - barWidth * CubeTopHeightProportion);
            _cubePath.LineTo(x - barWidth * CubeTopHeightProportion, y - barWidth * CubeTopHeightProportion);
            _cubePath.Close();

            paint.Color = paint.Color.WithAlpha((byte)(magnitude * 200));
            canvas.DrawPath(_cubePath, paint);
        }

        public void Dispose()
        {
            if (_isInitialized)
            {
                _cubePath.Dispose();
                _cubePaint?.Dispose();
                _isInitialized = false;
                Log.Debug("CubesRenderer disposed");
            }
        }
    }
}