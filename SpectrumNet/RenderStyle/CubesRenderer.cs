namespace SpectrumNet
{
    public class CubesRenderer : ISpectrumRenderer, IDisposable
    {
        private static CubesRenderer? _instance;
        private bool _isInitialized;
        private readonly SKPath _cubePath = new();
        private SKPaint? _cubePaint;

        public CubesRenderer() { }

        public static CubesRenderer GetInstance() => _instance ??= new CubesRenderer();

        public void Initialize()
        {
            if (_isInitialized) return;

            _cubePaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };

            Log.Debug("CubesRenderer initialized");
            _isInitialized = true;
        }

        public void Configure(bool isOverlayActive)
        {
            // Конфигурация не требуется для этого рендерера
        }

        private bool AreRenderParamsValid(SKCanvas? canvas, ReadOnlySpan<float> spectrum, SKImageInfo info, SKPaint? paint)
        {
            if (canvas == null || spectrum.IsEmpty || paint == null || info.Width <= 0 || info.Height <= 0)
            {
                Log.Warning("Invalid render parameters");
                return false;
            }
            return true;
        }

        public void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info,
                         float barWidth, float barSpacing, int barCount, SKPaint? basePaint)
        {
            if (!_isInitialized || _cubePaint == null)
            {
                Log.Warning("CubesRenderer is not initialized.");
                return;
            }

            if (!AreRenderParamsValid(canvas, spectrum.AsSpan(), info, basePaint)) return;

            int actualBarCount = Math.Min(spectrum!.Length / 2, barCount);
            float totalWidth = barWidth + barSpacing;

            RenderCubes(canvas!, spectrum.AsSpan(), info, actualBarCount, totalWidth, barWidth, basePaint!);
        }

        private void RenderCubes(SKCanvas canvas, ReadOnlySpan<float> spectrum, SKImageInfo info,
                               int barCount, float totalWidth, float barWidth, SKPaint basePaint)
        {
            for (int i = 0; i < barCount; i++)
            {
                float magnitude = spectrum[i];
                if (magnitude < 0.01f) continue; // Пропускаем кубы с очень низкой интенсивностью

                float height = magnitude * info.Height;
                float x = i * totalWidth;
                float y = info.Height - height;

                using (var clonedPaint = basePaint.Clone())
                {
                    RenderCube(canvas, x, y, barWidth, height, magnitude, clonedPaint);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RenderCube(SKCanvas canvas, float x, float y, float barWidth,
                              float height, float magnitude, SKPaint basePaint)
        {
            if (_cubePaint == null) return;

            // Настройка цвета для основного тела куба
            basePaint.Color = basePaint.Color.WithAlpha((byte)(magnitude * 255));

            // Отрисовка основного тела куба
            canvas.DrawRect(x, y, barWidth, height, basePaint);

            // Отрисовка верхней части куба
            RenderCubeTop(canvas, x, y, barWidth, magnitude, basePaint);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RenderCubeTop(SKCanvas canvas, float x, float y, float barWidth, float magnitude, SKPaint paint)
        {
            if (_cubePaint == null) return;

            _cubePath.Reset();

            // Построение пути для верхней части куба
            _cubePath.MoveTo(x, y);
            _cubePath.LineTo(x + barWidth, y);
            _cubePath.LineTo(x + barWidth * 0.75f, y - barWidth * 0.25f);
            _cubePath.LineTo(x - barWidth * 0.25f, y - barWidth * 0.25f);
            _cubePath.Close();

            // Настройка цвета для верхней части
            paint.Color = paint.Color.WithAlpha((byte)(magnitude * 200));

            // Отрисовка верхней части
            canvas.DrawPath(_cubePath, paint);
        }

        public void Dispose()
        {
            _cubePath.Dispose();
            _cubePaint?.Dispose();
        }
    }
}