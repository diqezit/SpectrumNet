#nullable enable

namespace SpectrumNet
{
    public static class RaindropsSettings
    {
        public const int MaxRaindrops = 1000;
        public const int MaxRipples = 150;
        public const float BaseFallSpeed = 2f;
        public const float RippleExpandSpeed = 2f;
        public const float SpectrumThreshold = 0.1f;
        public const float RippleStrokeWidth = 2f;
        public const float InitialRadius = 3f;
        public const float InitialAlpha = 1f;
        public const float RippleAlphaThreshold = 0.1f;
        public const float RippleAlphaDecay = 0.95f;
        public const double SpawnProbability = 0.15;
        public const float OverlayBottomMultiplier = 3.75f;
    }

    public sealed class RaindropsRenderer : ISpectrumRenderer, IDisposable
    {
        private static readonly Lazy<RaindropsRenderer> _instance = new(() => new RaindropsRenderer());

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct Raindrop
        {
            public readonly float X;
            public readonly float Y;
            public readonly float FallSpeed;

            public Raindrop(float x, float y, float fallSpeed)
            {
                X = x;
                Y = y;
                FallSpeed = fallSpeed;
            }

            public Raindrop WithNewY(float newY) => new(X, newY, FallSpeed);
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct Ripple
        {
            public readonly float X;
            public readonly float Y;
            public readonly float Radius;
            public readonly float Alpha;

            public Ripple(float x, float y, float radius, float alpha)
            {
                X = x;
                Y = y;
                Radius = radius;
                Alpha = alpha;
            }

            public Ripple WithUpdatedValues(float newRadius, float newAlpha) =>
                new(X, Y, newRadius, newAlpha);
        }

        private RenderCache _renderCache;
        private readonly Raindrop[] _raindrops;
        private readonly Ripple[] _ripples;
        private int _raindropCount;
        private int _rippleCount;

        private readonly SKPath _dropsPath;
        private readonly SKPath _ripplesPath;
        private readonly Random _random;
        private readonly float[] _scaledSpectrumCache;

        private bool _isInitialized;
        private bool _isOverlayActive;
        private bool _isDisposed;

        private RaindropsRenderer()
        {
            _raindrops = new Raindrop[RaindropsSettings.MaxRaindrops];
            _ripples = new Ripple[RaindropsSettings.MaxRipples];
            _dropsPath = new SKPath();
            _ripplesPath = new SKPath();
            _random = new Random();
            _scaledSpectrumCache = new float[RaindropsSettings.MaxRaindrops];
        }

        public static RaindropsRenderer GetInstance() => _instance.Value;

        public void Initialize()
        {
            EnsureNotDisposed();

            if (!_isInitialized)
            {
                _isInitialized = true;
                Log.Debug("RaindropsRenderer initialized");
            }
        }

        public void Configure(bool isOverlayActive)
        {
            EnsureNotDisposed();
            _isOverlayActive = isOverlayActive;
        }

        public void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info, float barWidth,
            float barSpacing, int barCount, SKPaint? paint, Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
        {
            EnsureNotDisposed();
            if (!IsValidRenderInput(canvas, spectrum, paint)) return;

            // Обновляем кэш только при изменении размеров
            if (_renderCache.Width != info.Width || _renderCache.Height != info.Height)
            {
                UpdateRenderCache(info);
            }

            int actualBarCount = Math.Min(spectrum!.Length / 2, barCount);
            Span<float> scaledSpectrum = _scaledSpectrumCache.AsSpan(0, actualBarCount);
            ScaleSpectrum(spectrum, scaledSpectrum, actualBarCount);

            UpdateRaindrops(scaledSpectrum, _renderCache.Width, _renderCache.LowerBound, _renderCache.UpperBound);
            UpdateRipples();

            drawPerformanceInfo(canvas!, info);
            RenderDropsAndRipples(canvas!, paint!);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateRenderCache(SKImageInfo info)
        {
            _renderCache = new RenderCache
            {
                Width = info.Width,
                Height = info.Height,
                LowerBound = _isOverlayActive ? info.Height * RaindropsSettings.OverlayBottomMultiplier : info.Height,
                UpperBound = _isOverlayActive ? info.Height * 0.1f : 0,
                StepSize = info.Width / RaindropsSettings.MaxRaindrops
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsValidRenderInput(SKCanvas? canvas, float[]? spectrum, SKPaint? paint) =>
            canvas != null && spectrum != null && paint != null && spectrum.Length > 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private (float overlayHeight, float baseY, float adjustedLowerBound) CalculateOverlayDimensions(float totalHeight)
        {
            if (_isOverlayActive)
            {
                float overlayHeight = totalHeight;
                float adjustedLowerBound = totalHeight * RaindropsSettings.OverlayBottomMultiplier;
                return (overlayHeight, totalHeight, adjustedLowerBound);
            }
            return (totalHeight, totalHeight, totalHeight);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ScaleSpectrum(ReadOnlySpan<float> source, Span<float> destination, int targetCount)
        {
            float blockSize = (source.Length / 2f) / targetCount;
            int halfSourceLength = source.Length / 2;

            for (int i = 0; i < targetCount; i++)
            {
                int start = (int)(i * blockSize);
                int length = Math.Min((int)((i + 1) * blockSize) - start, halfSourceLength - start);

                destination[i] = CalculateAverage(source.Slice(start, length));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float CalculateAverage(ReadOnlySpan<float> values)
        {
            if (values.Length == 0) return 0f;

            float sum = 0f;
            for (int i = 0; i < values.Length; i++)
            {
                sum += values[i];
            }
            return sum / values.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateRaindrops(ReadOnlySpan<float> spectrum, float width, float lowerBound, float upperBound)
        {
            // Обновление существующих капель
            for (int i = _raindropCount - 1; i >= 0; i--)
            {
                var drop = _raindrops[i];
                float newY = drop.Y + drop.FallSpeed;

                if (newY >= lowerBound)
                {
                    if (_rippleCount < RaindropsSettings.MaxRipples)
                    {
                        CreateRipple(drop.X, lowerBound);
                    }
                    RemoveRaindrop(i);
                }
                else
                {
                    _raindrops[i] = drop.WithNewY(newY);
                }
            }

            // Создание новых капель
            if (_raindropCount < RaindropsSettings.MaxRaindrops)
            {
                float step = width / spectrum.Length;

                for (int i = 0; i < spectrum.Length && _raindropCount < RaindropsSettings.MaxRaindrops; i++)
                {
                    float intensity = Math.Min(spectrum[i], 1f);

                    if (_random.NextDouble() < intensity * RaindropsSettings.SpawnProbability)
                    {
                        _raindrops[_raindropCount++] = new Raindrop(
                            i * step + (float)_random.NextDouble() * step,
                            upperBound,
                            RaindropsSettings.BaseFallSpeed * (1f + intensity)
                        );
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateRipples()
        {
            for (int i = _rippleCount - 1; i >= 0; i--)
            {
                var ripple = _ripples[i];
                var newRadius = ripple.Radius + RaindropsSettings.RippleExpandSpeed;
                var newAlpha = ripple.Alpha * RaindropsSettings.RippleAlphaDecay;

                if (newAlpha < RaindropsSettings.RippleAlphaThreshold)
                {
                    RemoveRipple(i);
                }
                else
                {
                    _ripples[i] = ripple.WithUpdatedValues(newRadius, newAlpha);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CreateRipple(float x, float y)
        {
            if (_rippleCount >= RaindropsSettings.MaxRipples) return;

            // Используем предварительно созданную структуру для уменьшения аллокаций
            _ripples[_rippleCount++] = new Ripple(
                x,
                y,
                RaindropsSettings.InitialRadius,
                RaindropsSettings.InitialAlpha
            );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RemoveRaindrop(int index)
        {
            // Оптимизированное удаление с перемещением последнего элемента
            int lastIndex = --_raindropCount;
            if (index < lastIndex)
            {
                _raindrops[index] = _raindrops[lastIndex];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RemoveRipple(int index)
        {
            // Оптимизированное удаление с перемещением последнего элемента
            int lastIndex = --_rippleCount;
            if (index < lastIndex)
            {
                _ripples[index] = _ripples[lastIndex];
            }
        }

        private void RenderDropsAndRipples(SKCanvas canvas, SKPaint paint)
        {
            if (canvas == null || paint == null) return;

            try
            {
                _dropsPath.Reset();
                _ripplesPath.Reset();

                // Отрисовка капель
                for (int i = 0; i < _raindropCount; i++)
                {
                    var drop = _raindrops[i];
                    _dropsPath.AddCircle(drop.X, drop.Y, RaindropsSettings.InitialRadius);
                }

                using (var dropPaint = paint.Clone())
                {
                    dropPaint.Style = SKPaintStyle.Fill;
                    canvas.DrawPath(_dropsPath, dropPaint);

                    // Отрисовка ряби
                    dropPaint.Style = SKPaintStyle.Stroke;
                    dropPaint.StrokeWidth = RaindropsSettings.RippleStrokeWidth;

                    for (int i = 0; i < _rippleCount; i++)
                    {
                        var ripple = _ripples[i];
                        dropPaint.Color = dropPaint.Color.WithAlpha((byte)(255 * ripple.Alpha));
                        _ripplesPath.AddCircle(ripple.X, ripple.Y, ripple.Radius);
                    }

                    canvas.DrawPath(_ripplesPath, dropPaint);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error in RenderDropsAndRipples: {ex.Message}");
            }
        }

        private void EnsureNotDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(RaindropsRenderer));
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            try
            {
                _dropsPath.Dispose();
                _ripplesPath.Dispose();
            }
            finally
            {
                _raindropCount = 0;
                _rippleCount = 0;
                _isInitialized = false;
                _isDisposed = true;

                Log.Debug("RaindropsRenderer disposed");
            }
        }
    }
}