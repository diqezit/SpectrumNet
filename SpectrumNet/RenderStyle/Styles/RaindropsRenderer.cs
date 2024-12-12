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

        #region Structs

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

        #endregion

        #region Fields

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
        private bool _overlayStatusChanged;
        private bool _isDisposed;

        #endregion

        #region Constructors

        private RaindropsRenderer()
        {
            _raindrops = new Raindrop[RaindropsSettings.MaxRaindrops];
            _ripples = new Ripple[RaindropsSettings.MaxRipples];
            _dropsPath = new SKPath();
            _ripplesPath = new SKPath();
            _random = new Random();
            _scaledSpectrumCache = new float[RaindropsSettings.MaxRaindrops];
            _overlayStatusChanged = false;
        }

        #endregion

        #region Public Methods

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
            if (_isOverlayActive != isOverlayActive)
            {
                _isOverlayActive = isOverlayActive;
                _overlayStatusChanged = true;
            }
        }

        public void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info, float barWidth,
            float barSpacing, int barCount, SKPaint? paint, Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
        {
            EnsureNotDisposed();
            if (canvas == null || spectrum == null || paint == null || spectrum.Length == 0) return;

            // Update render cache if overlay status has changed or dimensions have changed
            if (_overlayStatusChanged ||
                _renderCache.Width != info.Width ||
                _renderCache.Height != info.Height)
            {
                UpdateRenderCache(info);
                _overlayStatusChanged = false;
            }

            int actualBarCount = Math.Min(spectrum.Length / 2, barCount);
            Span<float> scaledSpectrum = _scaledSpectrumCache.AsSpan(0, actualBarCount);
            ScaleSpectrum(spectrum, scaledSpectrum, actualBarCount);

            UpdateRaindrops(scaledSpectrum, _renderCache.Width, _renderCache.LowerBound, _renderCache.UpperBound);
            UpdateRipples();

            RenderDropsAndRipples(canvas, paint);

            drawPerformanceInfo!(canvas!, info);
        }

        #endregion

        #region Private Methods

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateRenderCache(SKImageInfo info)
        {
            _renderCache = new RenderCache
            {
                Width = info.Width,
                Height = info.Height,
                LowerBound = _isOverlayActive ? info.Height * RaindropsSettings.OverlayBottomMultiplier : info.Height,
                UpperBound = _isOverlayActive ? info.Height * 0.1f : 0f,
                StepSize = info.Width / (float)RaindropsSettings.MaxRaindrops
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ScaleSpectrum(ReadOnlySpan<float> source, Span<float> destination, int targetCount)
        {
            if (source.IsEmpty || destination.IsEmpty || targetCount <= 0)
                return;

            float blockSize = source.Length / (2f * targetCount);
            int halfSourceLength = source.Length / 2;

            for (int i = 0; i < targetCount; i++)
            {
                int startIndex = (int)(i * blockSize);
                int endIndex = Math.Min((int)((i + 1) * blockSize), halfSourceLength);

                destination[i] = endIndex > startIndex
                    ? CalculateAverageSpan(source.Slice(startIndex, endIndex - startIndex))
                    : 0f;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float CalculateAverageSpan(ReadOnlySpan<float> values)
        {
            float sum = 0f;
            for (int i = 0; i < values.Length; i++)
            {
                sum += values[i];
            }
            return values.Length > 0 ? sum / values.Length : 0f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateRaindrops(ReadOnlySpan<float> spectrum, float width, float lowerBound, float upperBound)
        {
            int writeIndex = 0;
            for (int i = 0; i < _raindropCount; i++)
            {
                ref var drop = ref _raindrops[i];
                float newY = drop.Y + drop.FallSpeed;

                if (newY < lowerBound)
                {
                    _raindrops[writeIndex] = drop.WithNewY(newY);
                    writeIndex++;
                }
                else if (_rippleCount < RaindropsSettings.MaxRipples)
                {
                    CreateRipple(drop.X, lowerBound);
                }
            }
            _raindropCount = writeIndex;

            SpawnRaindropsFromSpectrum(spectrum, width / spectrum.Length, upperBound);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SpawnRaindropsFromSpectrum(ReadOnlySpan<float> spectrum, float step, float upperBound)
        {
            for (int i = 0; i < spectrum.Length && _raindropCount < RaindropsSettings.MaxRaindrops; i++)
            {
                float intensity = Math.Clamp(spectrum[i], 0f, 1f);

                if (_random.NextDouble() < intensity * RaindropsSettings.SpawnProbability)
                {
                    _raindrops[_raindropCount++] = CreateRaindrop(i * step, upperBound, intensity);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Raindrop CreateRaindrop(float baseX, float upperBound, float intensity)
        {
            return new Raindrop(
                baseX + (float)_random.NextDouble() * _renderCache.StepSize,
                upperBound,
                RaindropsSettings.BaseFallSpeed * (1f + intensity)
            );
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

            _ripples[_rippleCount++] = new Ripple(
                x,
                y,
                RaindropsSettings.InitialRadius,
                RaindropsSettings.InitialAlpha
            );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RemoveRipple(int index)
        {
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
                using var dropPaint = paint.Clone();

                RenderRaindrops(canvas, dropPaint);
                RenderRipples(canvas, dropPaint);
            }
            catch (Exception ex)
            {
                Log.Error($"Error in RenderDropsAndRipples: {ex.Message}");
            }
        }

        private void RenderRaindrops(SKCanvas canvas, SKPaint dropPaint)
        {
            dropPaint.Style = SKPaintStyle.Fill;
            _dropsPath.Reset();

            for (int i = 0; i < _raindropCount; i++)
                _dropsPath.AddCircle(_raindrops[i].X, _raindrops[i].Y, RaindropsSettings.InitialRadius);

            canvas.DrawPath(_dropsPath, dropPaint);
        }

        private void RenderRipples(SKCanvas canvas, SKPaint dropPaint)
        {
            dropPaint.Style = SKPaintStyle.Stroke;
            dropPaint.StrokeWidth = RaindropsSettings.RippleStrokeWidth;
            _ripplesPath.Reset();

            for (int i = 0; i < _rippleCount; i++)
            {
                var ripple = _ripples[i];
                dropPaint.Color = dropPaint.Color.WithAlpha((byte)(255 * ripple.Alpha));
                _ripplesPath.AddCircle(ripple.X, ripple.Y, ripple.Radius);
            }

            canvas.DrawPath(_ripplesPath, dropPaint);
        }

        private void EnsureNotDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(RaindropsRenderer));
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_isDisposed) return;

            try
            {
                _dropsPath?.Dispose();
                _ripplesPath?.Dispose();
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

        #endregion
    }
}