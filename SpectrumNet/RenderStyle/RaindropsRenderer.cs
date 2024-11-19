#nullable enable

namespace SpectrumNet
{
    public class RaindropsRenderer : ISpectrumRenderer
    {
        private static RaindropsRenderer? _instance;
        private bool _isInitialized;
        private readonly List<Raindrop> _raindrops = new();
        private readonly Random _random = new();
        private const int MaxRaindrops = 100;
        private const float FallSpeed = 5f;
        private const float RippleExpandSpeed = 2f;

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
            Log.Debug("RaindropsRenderer initialized");
            _isInitialized = true;
        }

        public void Configure(bool isOverlayActive) { }

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
            if (!_isInitialized)
            {
                Log.Warning("RaindropsRenderer is not initialized.");
                return;
            }

            if (!AreRenderParamsValid(canvas, spectrum.AsSpan(), info, basePaint)) return;

            UpdateRaindrops(spectrum!, info.Width, info.Height);
            RenderDrops(canvas!, info.Height, basePaint!);
        }

        private void UpdateRaindrops(float[] spectrum, float width, float height)
        {
            // Update existing raindrops
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
                        drop.Radius = 1f;
                        drop.Y = height;
                    }
                }
            }

            // Create new raindrops based on spectrum
            if (_raindrops.Count < MaxRaindrops)
            {
                for (int i = 0; i < spectrum.Length / 2; i++)
                {
                    if (spectrum[i] > 0.5f && _random.NextDouble() < 0.1)
                    {
                        _raindrops.Add(new Raindrop
                        {
                            X = i * (width / (spectrum.Length / 2)) + (float)_random.NextDouble() * 10,
                            Y = 0,
                            Radius = 2f,
                            Alpha = 1f,
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
                    ripplePaint.StrokeWidth = 2;
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
        }
    }
}
