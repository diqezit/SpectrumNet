#nullable enable

namespace SpectrumNet
{
    public static class RaindropsSettings
    {
        public const int MaxRaindrops = 1000, MaxRipples = 150;
        public const float
            BaseFallSpeed = 2f,
            RippleExpandSpeed = 2f,
            SpectrumThreshold = 0.1f,
            RippleStrokeWidth = 2f,
            InitialRadius = 3f,
            InitialAlpha = 1f,
            RippleAlphaThreshold = 0.1f,
            RippleAlphaDecay = 0.95f,
            OverlayLowerBoundOffset = 4.75f;
        public const double SpawnProbability = 0.15;
    }

    public sealed class RaindropsRenderer : ISpectrumRenderer, IDisposable
    {
        private static readonly Lazy<RaindropsRenderer> _instance = new(() => new RaindropsRenderer());
        private bool _isInitialized, _isOverlayActive, _isDisposed;
        private readonly ArrayPool<Raindrop> _raindropPool = ArrayPool<Raindrop>.Shared;
        private readonly ArrayPool<Ripple> _ripplePool = ArrayPool<Ripple>.Shared;
        private Raindrop[] _raindrops;
        private Ripple[] _activeRipples;
        private int _raindropCount;
        private int _activeRippleCount;
        private readonly Random _random = new();

        private RaindropsRenderer()
        {
            _raindrops = _raindropPool.Rent(RaindropsSettings.MaxRaindrops);
            _activeRipples = _ripplePool.Rent(RaindropsSettings.MaxRipples);
        }

        public static RaindropsRenderer GetInstance() => _instance.Value;

        private struct Raindrop { public float X, Y, FallSpeed; }
        private struct Ripple { public float X, Y, Radius, Alpha; }

        public void Initialize()
        {
            ThrowIfDisposed();
            if (!_isInitialized) { _isInitialized = true; Log.Debug("RaindropsRenderer initialized"); }
        }

        public void Configure(bool isOverlayActive) { ThrowIfDisposed(); _isOverlayActive = isOverlayActive; }

        public void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info, float barWidth,
            float barSpacing, int barCount, SKPaint? paint, Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
        {
            ThrowIfDisposed();
            if (!ValidateRenderParameters(canvas, spectrum, paint)) return;

            var (_, _, adjustedLowerBound) = GetOverlayDimensions(info.Height);
            int actualBarCount = Math.Min(spectrum!.Length / 2, barCount);
            Span<float> scaledSpectrum = stackalloc float[actualBarCount];
            ScaleSpectrum(spectrum, scaledSpectrum, actualBarCount);

            UpdateRaindrops(scaledSpectrum, info.Width, adjustedLowerBound, 0);
            RenderDrops(canvas!, adjustedLowerBound, paint!);

            drawPerformanceInfo(canvas!, info);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ValidateRenderParameters(SKCanvas? canvas, float[]? spectrum, SKPaint? paint) =>
            _isInitialized && canvas != null && spectrum != null && paint != null && spectrum.Length != 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private (float overlayHeight, float baseY, float adjustedLowerBound) GetOverlayDimensions(float totalHeight) =>
            _isOverlayActive ? (totalHeight * RaindropsSettings.OverlayLowerBoundOffset,
            totalHeight, totalHeight * RaindropsSettings.OverlayLowerBoundOffset) : (totalHeight, totalHeight, totalHeight);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ScaleSpectrum(float[] spectrum, Span<float> scaledSpectrum, int targetCount)
        {
            float blockSize = (float)(spectrum.Length / 2) / targetCount;
            for (int i = 0; i < targetCount; i++)
            {
                int start = (int)(i * blockSize);
                int end = Math.Min((int)((i + 1) * blockSize), spectrum.Length / 2);
                scaledSpectrum[i] = CalculateAverage(spectrum.AsSpan(start, end - start));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float CalculateAverage(Span<float> values)
        {
            if (values.IsEmpty)
                return 0;

            float sum = 0;
            for (int i = 0; i < values.Length; i++)
            {
                sum += values[i];
            }
            return sum / values.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateRaindrops(Span<float> spectrum, float width, float lowerBound, float upperBound)
        {
            for (int i = _raindropCount - 1; i >= 0; i--)
            {
                ref var drop = ref _raindrops[i];
                drop.Y += drop.FallSpeed;
                if (drop.Y >= lowerBound)
                {
                    CreateRipple(drop.X, lowerBound);
                    _raindropCount--;
                    if (i < _raindropCount)
                    {
                        _raindrops[i] = _raindrops[_raindropCount];
                    }
                }
            }

            UpdateRipples();
            SpawnNewRaindrops(spectrum, width, upperBound);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CreateRipple(float x, float y)
        {
            if (_activeRippleCount >= RaindropsSettings.MaxRipples) return;
            _activeRipples[_activeRippleCount++] = new Ripple
            {
                X = x,
                Y = y,
                Radius = RaindropsSettings.InitialRadius,
                Alpha = RaindropsSettings.InitialAlpha
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateRipples()
        {
            for (int i = _activeRippleCount - 1; i >= 0; i--)
            {
                ref var ripple = ref _activeRipples[i];
                ripple.Radius += RaindropsSettings.RippleExpandSpeed;
                ripple.Alpha *= RaindropsSettings.RippleAlphaDecay;
                if (ripple.Alpha < RaindropsSettings.RippleAlphaThreshold)
                {
                    _activeRippleCount--;
                    if (i < _activeRippleCount)
                    {
                        _activeRipples[i] = _activeRipples[_activeRippleCount];
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SpawnNewRaindrops(Span<float> spectrum, float width, float upperBound)
        {
            if (_raindropCount >= RaindropsSettings.MaxRaindrops) return;
            float step = width / spectrum.Length;
            for (int i = 0; i < spectrum.Length; i++)
            {
                float intensity = Math.Clamp(spectrum[i], 0f, 1f); // Нормализуем значение спектра
                if (_random.NextDouble() < intensity * RaindropsSettings.SpawnProbability)
                {
                    _raindrops[_raindropCount++] = new Raindrop
                    {
                        X = i * step + (float)_random.NextDouble() * step,
                        Y = upperBound,
                        FallSpeed = RaindropsSettings.BaseFallSpeed * (1f + intensity)
                    };
                    if (_raindropCount >= RaindropsSettings.MaxRaindrops) break;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RenderDrops(SKCanvas canvas, float lowerBound, SKPaint paint)
        {
            using var dropPaint = paint.Clone();
            dropPaint.Style = SKPaintStyle.Fill;
            for (int i = 0; i < _raindropCount; i++)
            {
                var drop = _raindrops[i];
                canvas.DrawCircle(drop.X, drop.Y, RaindropsSettings.InitialRadius, dropPaint);
            }

            dropPaint.Style = SKPaintStyle.Stroke;
            dropPaint.StrokeWidth = RaindropsSettings.RippleStrokeWidth;
            for (int i = 0; i < _activeRippleCount; i++)
            {
                var ripple = _activeRipples[i];
                dropPaint.Color = dropPaint.Color.WithAlpha((byte)(255 * ripple.Alpha));
                canvas.DrawCircle(ripple.X, ripple.Y, ripple.Radius, dropPaint);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ThrowIfDisposed()
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(RaindropsRenderer));
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _raindropPool.Return(_raindrops);
            _ripplePool.Return(_activeRipples);
            _raindrops = null!;
            _activeRipples = null!;
            _raindropCount = 0;
            _activeRippleCount = 0;
            _isInitialized = false;
            _isDisposed = true;
            Log.Debug("RaindropsRenderer disposed");
        }
    }
}