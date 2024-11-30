namespace SpectrumNet
{
    public class DotsRenderer : ISpectrumRenderer, IDisposable
    {
        private static DotsRenderer? _instance;
        private bool _isInitialized;

        // Константы
        private const float MinIntensityThreshold = 0.01f;
        private const float MinDotRadius = 2f;

        private DotsRenderer() { }

        public static DotsRenderer GetInstance() => _instance ??= new DotsRenderer();

        public void Initialize()
        {
            if (_isInitialized) return;
            _isInitialized = true;
            Log.Debug("DotsRenderer initialized");
        }

        public void Configure(bool isOverlayActive)
        {
            // Возможность настроить рендерер
            Log.Debug($"DotsRenderer configured. Overlay active: {isOverlayActive}");
        }

        public void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info,
                           float barWidth, float barSpacing, int barCount, SKPaint? basePaint)
        {
            if (!_isInitialized || canvas == null || spectrum == null || spectrum.Length == 0 || basePaint == null)
            {
                Log.Warning("Invalid render parameters or DotsRenderer is not initialized.");
                return;
            }

            // Масштабирование спектра
            int actualBarCount = Math.Min(spectrum.Length / 2, barCount);
            float[] scaledSpectrum = ScaleSpectrum(spectrum, actualBarCount);

            // Динамическая настройка ширины точек
            float totalWidth = barWidth + barSpacing;
            if (actualBarCount < 50)
                totalWidth *= 50f / actualBarCount;

            RenderDots(canvas, scaledSpectrum.AsSpan(), info, totalWidth, barWidth, actualBarCount, basePaint);
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

        private void RenderDots(SKCanvas canvas, ReadOnlySpan<float> spectrum, SKImageInfo info,
                                float totalWidth, float barWidth, int barCount, SKPaint basePaint)
        {
            for (int i = 0; i < barCount; i++)
            {
                float intensity = spectrum[i];
                if (intensity < MinIntensityThreshold) continue;

                // Рассчитываем радиус точки
                float dotRadius = Math.Max(barWidth / 2 * intensity, MinDotRadius);

                // Позиция точки
                float x = i * totalWidth + dotRadius;
                float y = info.Height - (intensity * info.Height);

                RenderMainDot(canvas, x, y, dotRadius, intensity, basePaint);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RenderMainDot(SKCanvas canvas, float x, float y, float dotRadius,
                                   float intensity, SKPaint basePaint)
        {
            using var dotPaint = basePaint.Clone();
            dotPaint.Color = basePaint.Color.WithAlpha((byte)(255 * intensity));
            canvas.DrawCircle(x, y, dotRadius, dotPaint);
        }

        public void Dispose()
        {
            _isInitialized = false;
            Log.Debug("DotsRenderer disposed");
        }
    }
}