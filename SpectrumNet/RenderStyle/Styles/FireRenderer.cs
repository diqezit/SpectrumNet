namespace SpectrumNet
{
    public class FireRenderer : ISpectrumRenderer, IDisposable
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
            Log.Debug("FireRenderer initialized");
        }

        public void Configure(bool isOverlayActive)
        {
            // Возможность настройки поведения рендера, если потребуется
        }

        public void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info,
                           float barWidth, float barSpacing, int barCount, SKPaint? basePaint,
                           Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
        {
            if (!_isInitialized || canvas == null || spectrum == null || spectrum.Length == 0 || basePaint == null)
            {
                Log.Warning("Invalid render parameters or FireRenderer is not initialized.");
                return;
            }

            if (_previousSpectrum.Length != spectrum.Length)
            {
                _previousSpectrum = new float[spectrum.Length];
                Array.Copy(spectrum, _previousSpectrum, spectrum.Length);
            }

            // Масштабирование спектра
            int actualBarCount = Math.Min(spectrum.Length / 2, barCount);
            float[] scaledSpectrum = ScaleSpectrum(spectrum, actualBarCount);

            // Динамическая настройка ширины столбцов
            float totalBarWidth = barWidth + barSpacing;
            if (actualBarCount < 50)
                totalBarWidth *= 50f / actualBarCount;

            using var paintClone = basePaint.Clone();
            RenderFlames(canvas, scaledSpectrum.AsSpan(), actualBarCount, totalBarWidth, barWidth, info.Height, paintClone);

            // Обновление предыдущего спектра с учётом затухания
            for (int i = 0; i < spectrum.Length; i++)
            {
                _previousSpectrum[i] = Math.Max(spectrum[i], _previousSpectrum[i] - DecayRate);
            }

            // Отрисовка информации о производительности
            drawPerformanceInfo(canvas, info);
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

            // Верх и низ пламени
            float flameTop = canvasHeight - Math.Max(currentHeight, previousHeight);
            float flameBottom = canvasHeight - Math.Min(currentHeight * FlameBottomProportion, FlameBottomMax);

            // Построение пути пламени
            path.MoveTo(x, flameBottom);

            float controlY = flameTop + (flameBottom - flameTop) * ControlPointProportion;
            float randomOffset = (float)(_random.NextDouble() * barWidth * RandomOffsetProportion - barWidth * RandomOffsetCenter);

            path.QuadTo(
                x + barWidth * 0.5f + randomOffset, controlY,
                x + barWidth, flameBottom
            );

            // Прозрачность пламени
            byte alpha = (byte)(255 * Math.Min(currentHeight / canvasHeight * 1.5f, 1.0f));
            paint.Color = paint.Color.WithAlpha(alpha);

            canvas.DrawPath(path, paint);
        }

        public void Dispose()
        {
            _previousSpectrum = Array.Empty<float>();
            _isInitialized = false;
            Log.Debug("FireRenderer disposed");
        }
    }
}