#nullable enable

namespace SpectrumNet
{
    public class RaindropsRenderer : ISpectrumRenderer, IDisposable
    {
        private static RaindropsRenderer? _instance;
        private bool _isInitialized;
        private readonly List<Raindrop> _raindrops = new();
        private readonly Random _random = new();

        // Константы
        private const int MaxRaindrops = 100;
        private const float FallSpeed = 5f;
        private const float RippleExpandSpeed = 2f;
        private const float SpectrumThreshold = 0.5f;
        private const double SpawnProbability = 0.1;
        private const float RippleStrokeWidth = 2f;
        private const float InitialRadius = 2f;
        private const float InitialAlpha = 1f;

        private RaindropsRenderer() { }

        public static RaindropsRenderer GetInstance() => _instance ??= new RaindropsRenderer();

        private class Raindrop
        {
            public float X { get; set; }
            public float Y { get; set; }
            public float Radius { get; set; }
            public float Alpha { get; set; }
            public bool IsRipple { get; set; }
        }

        public void Initialize()
        {
            if (_isInitialized) return;

            _isInitialized = true;
            Log.Debug("RaindropsRenderer initialized");
        }

        public void Configure(bool isOverlayActive)
        {
            // Возможность настройки поведения рендера, если потребуется
        }

        public void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info,
                           float barWidth, float barSpacing, int barCount, SKPaint? paint,
                           Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
        {
            if (!_isInitialized || canvas == null || spectrum == null || spectrum.Length == 0 || paint == null)
            {
                Log.Warning("Invalid render parameters or RaindropsRenderer is not initialized.");
                return;
            }

            // Масштабирование спектра
            int actualBarCount = Math.Min(spectrum.Length / 2, barCount);
            float[] scaledSpectrum = ScaleSpectrum(spectrum, actualBarCount);

            UpdateRaindrops(scaledSpectrum, info.Width, info.Height);
            RenderDrops(canvas, info.Height, paint);

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

        private void UpdateRaindrops(float[] spectrum, float width, float height)
        {
            // Обновление существующих капель
            for (int i = _raindrops.Count - 1; i >= 0; i--)
            {
                var drop = _raindrops[i];

                if (drop.IsRipple)
                {
                    drop.Radius += RippleExpandSpeed;
                    drop.Alpha *= 0.95f;

                    if (drop.Alpha < 0.1f)
                        _raindrops.RemoveAt(i);
                }
                else
                {
                    drop.Y += FallSpeed;

                    if (drop.Y >= height)
                    {
                        drop.IsRipple = true;
                        drop.Radius = InitialRadius;
                        drop.Y = height;
                    }
                }
            }

            // Создание новых капель на основе спектра
            if (_raindrops.Count < MaxRaindrops)
            {
                float step = width / spectrum.Length;
                for (int i = 0; i < spectrum.Length; i++)
                {
                    if (spectrum[i] > SpectrumThreshold && _random.NextDouble() < SpawnProbability)
                    {
                        _raindrops.Add(new Raindrop
                        {
                            X = i * step + (float)_random.NextDouble() * 10,
                            Y = 0,
                            Radius = InitialRadius,
                            Alpha = InitialAlpha,
                            IsRipple = false
                        });
                    }
                }
            }
        }

        private void RenderDrops(SKCanvas canvas, float height, SKPaint paint)
        {
            using var dropPaint = paint.Clone();

            foreach (var drop in _raindrops)
            {
                dropPaint.Color = dropPaint.Color.WithAlpha((byte)(255 * drop.Alpha));

                if (drop.IsRipple)
                {
                    using var ripplePaint = paint.Clone();
                    ripplePaint.Style = SKPaintStyle.Stroke;
                    ripplePaint.StrokeWidth = RippleStrokeWidth;
                    ripplePaint.Color = ripplePaint.Color.WithAlpha((byte)(255 * drop.Alpha));

                    canvas.DrawCircle(drop.X, drop.Y, drop.Radius, ripplePaint);
                }
                else
                {
                    canvas.DrawCircle(drop.X, drop.Y, drop.Radius, dropPaint);
                }
            }
        }

        public void Dispose()
        {
            _raindrops.Clear();
            _isInitialized = false;
            Log.Debug("RaindropsRenderer disposed");
        }
    }
}