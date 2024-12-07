namespace SpectrumNet
{
    public class CubesRenderer : ISpectrumRenderer, IDisposable
    {
        private static CubesRenderer? _instance;
        private bool _isInitialized;
        private readonly SKPath _cubePath = new();
        private volatile bool _disposed;

        // Константы
        private const float MinMagnitudeThreshold = 0.01f;
        private const float CubeTopWidthProportion = 0.75f;
        private const float CubeTopHeightProportion = 0.25f;
        private const float AlphaMultiplier = 255f;

        private CubesRenderer() { }

        public static CubesRenderer GetInstance() => _instance ??= new CubesRenderer();

        public void Initialize()
        {
            if (_isInitialized) return;

            _isInitialized = true;
            Log.Debug("CubesRenderer initialized");
        }

        public void Configure(bool isOverlayActive)
        {
            // Возможность настройки поведения рендера
        }

        public void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info,
                           float barWidth, float barSpacing, int barCount, SKPaint? basePaint,
                           Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
        {
            if (!_isInitialized || canvas == null || spectrum == null || spectrum.Length < 2 ||
                basePaint == null || info.Width <= 0 || info.Height <= 0)
            {
                Log.Warning("Invalid render parameters or renderer not initialized.");
                return;
            }

            int halfSpectrumLength = spectrum.Length / 2;
            int actualBarCount = Math.Min(halfSpectrumLength, barCount);
            float totalWidth = barWidth + barSpacing;

            float[] scaledSpectrum = ScaleSpectrum(spectrum, actualBarCount, halfSpectrumLength);
            float canvasHeight = info.Height;

            using var cubePaint = basePaint.Clone();
            cubePaint.IsAntialias = true;

            for (int i = 0; i < actualBarCount; i++)
            {
                float magnitude = scaledSpectrum[i];
                if (magnitude < MinMagnitudeThreshold) continue;

                float height = magnitude * canvasHeight;
                float x = i * totalWidth;
                float y = canvasHeight - height;

                cubePaint.Color = cubePaint.Color.WithAlpha((byte)(magnitude * AlphaMultiplier));
                RenderCube(canvas, x, y, barWidth, height, magnitude, cubePaint);
            }

            drawPerformanceInfo?.Invoke(canvas, info);
        }

        private static float[] ScaleSpectrum(float[] spectrum, int targetCount, int halfSpectrumLength)
        {
            float[] scaledSpectrum = new float[targetCount];
            int step = halfSpectrumLength / targetCount;

            for (int i = 0; i < targetCount; i++)
            {
                scaledSpectrum[i] = spectrum[i * step];
            }

            return scaledSpectrum;
        }

        private void RenderCube(SKCanvas canvas, float x, float y, float barWidth,
                                float height, float magnitude, SKPaint paint)
        {
            // Отрисовка базового прямоугольника (куба)
            canvas.DrawRect(x, y, barWidth, height, paint);

            // Отрисовка верхней части куба
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

            // Осветляем цвет верхней части куба
            paint.Color = paint.Color.WithAlpha((byte)(magnitude * AlphaMultiplier * 0.8f));
            canvas.DrawPath(_cubePath, paint);
        }

        public void Dispose()
        {
            if (_disposed) return;

            _cubePath.Dispose();
            _disposed = true;
            Log.Debug("CubesRenderer disposed");
        }
    }
}