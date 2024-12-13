#nullable enable

namespace SpectrumNet
{
    public static class RaindropsSettings
    {
        public const int MaxRaindrops = 1000,
                          MaxRipples = 150,
                          MaxParticles = 5000;

        public const float BaseFallSpeed = 2f,
                          RippleExpandSpeed = 2f,
                          SpectrumThreshold = 0.1f,
                          RippleStrokeWidth = 2f,
                          InitialRadius = 3f,
                          InitialAlpha = 1f,
                          RippleAlphaThreshold = 0.1f,
                          RippleAlphaDecay = 0.95f,
                          OverlayBottomMultiplier = 3.75f;

        public const double SpawnProbability = 0.15;
    }

    public sealed class RaindropsRenderer : ISpectrumRenderer, IDisposable
    {
        private static readonly Lazy<RaindropsRenderer> _instance = new(() => new RaindropsRenderer());

        #region Structs

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct Raindrop
        {
            public readonly float X, Y, FallSpeed;

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
            public readonly float X, Y, Radius, Alpha;

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

        [StructLayout(LayoutKind.Sequential)]
        private struct Particle
        {
            public float X, Y, VelocityX, VelocityY;

            public Particle(float x, float y, float velocityX, float velocityY)
            {
                X = x;
                Y = y;
                VelocityX = velocityX;
                VelocityY = velocityY;
            }
        }

        #endregion

        #region Classes

        private class ParticleBuffer
        {
            private readonly Particle[] _particles;
            private int _count;

            public ParticleBuffer(int capacity)
            {
                _particles = new Particle[capacity];
                _count = 0;
            }

            public void AddParticle(Particle particle)
            {
                if (_count < _particles.Length)
                    _particles[_count++] = particle;
            }

            public void Clear() => _count = 0;

            public void UpdateParticles(float deltaTime)
            {
                for (int i = 0; i < _count; i++)
                {
                    var p = _particles[i];
                    p.X += p.VelocityX * deltaTime;
                    p.Y += p.VelocityY * deltaTime;
                }
            }

            public void RenderParticles(SKCanvas? canvas, SKPaint? paint)
            {
                if (canvas == null || paint == null) return;

                paint.Style = SKPaintStyle.Fill;
                for (int i = 0; i < _count; i++)
                    canvas.DrawCircle(_particles[i].X, _particles[i].Y, 2f, paint);
            }
        }

        #endregion

        #region Fields

        private RenderCache _renderCache;
        private readonly Raindrop[] _raindrops;
        private readonly Ripple[] _ripples;
        private int _raindropCount, _rippleCount;
        private readonly SKPath _dropsPath, _ripplesPath;
        private readonly Random _random;
        private readonly float[] _scaledSpectrumCache;

        private bool _isInitialized, _isOverlayActive, _overlayStatusChanged, _isDisposed;

        private readonly ParticleBuffer _particleBuffer;

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
            _particleBuffer = new ParticleBuffer(RaindropsSettings.MaxParticles);
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
            float barSpacing, int barCount, SKPaint? paint, Action<SKCanvas, SKImageInfo>? drawPerformanceInfo)
        {
            EnsureNotDisposed();
            if (canvas == null || spectrum == null || paint == null || spectrum.Length == 0) return;

            if (_overlayStatusChanged || _renderCache.Width != info.Width || _renderCache.Height != info.Height)
            {
                UpdateRenderCache(info);
                _overlayStatusChanged = false;
            }

            var actualBarCount = Math.Min(spectrum.Length / 2, barCount);
            ScaleSpectrum(spectrum, _scaledSpectrumCache.AsSpan(0, actualBarCount), actualBarCount);

            UpdateSimulation(_scaledSpectrumCache.AsSpan(0, actualBarCount));
            RenderScene(canvas, paint);

            drawPerformanceInfo?.Invoke(canvas, info);
        }

        #endregion

        #region Private Methods

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateRenderCache(SKImageInfo info) =>
            _renderCache = new RenderCache
            {
                Width = info.Width,
                Height = info.Height,
                LowerBound = _isOverlayActive ? info.Height * RaindropsSettings.OverlayBottomMultiplier : info.Height,
                UpperBound = _isOverlayActive ? info.Height * 0.1f : 0f,
                StepSize = info.Width / (float)RaindropsSettings.MaxRaindrops
            };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateSimulation(ReadOnlySpan<float> spectrum)
        {
            UpdateRaindrops(spectrum, _renderCache.Width, _renderCache.LowerBound, _renderCache.UpperBound);
            UpdateRipples();
            _particleBuffer.UpdateParticles(0.016f);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ScaleSpectrum(ReadOnlySpan<float> src, Span<float> dst, int count)
        {
            if (src.IsEmpty || dst.IsEmpty || count <= 0) return;

            var blockSize = src.Length / (2f * count);
            var halfLen = src.Length / 2;

            for (int i = 0; i < count; i++)
            {
                var start = (int)(i * blockSize);
                var end = Math.Min((int)((i + 1) * blockSize), halfLen);
                float sum = 0;
                for (int j = start; j < end; j++) sum += src[j];
                dst[i] = (end > start) ? sum / (end - start) : 0f;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateRaindrops(ReadOnlySpan<float> spectrum, float width, float lower, float upper)
        {
            var writeIdx = 0;
            for (int i = 0; i < _raindropCount; i++)
            {
                ref var drop = ref _raindrops[i];
                var newY = drop.Y + drop.FallSpeed;

                if (newY < lower)
                    _raindrops[writeIdx++] = drop.WithNewY(newY);
                else if (_rippleCount < RaindropsSettings.MaxRipples)
                    CreateRipple(drop.X, lower);
            }

            _raindropCount = writeIdx;
            SpawnNewDrops(spectrum, width / spectrum.Length, upper);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SpawnNewDrops(ReadOnlySpan<float> spectrum, float step, float upper)
        {
            for (int i = 0; i < spectrum.Length && _raindropCount < RaindropsSettings.MaxRaindrops; i++)
            {
                var intensity = Math.Clamp(spectrum[i], 0f, 1f);
                if (_random.NextDouble() < intensity * RaindropsSettings.SpawnProbability)
                {
                    _raindrops[_raindropCount++] = new Raindrop(
                        i * step + (float)_random.NextDouble() * _renderCache.StepSize,
                        upper,
                        RaindropsSettings.BaseFallSpeed * (1f + intensity)
                    );
                }
            }
            UpdateParticles();
        }

        private void RenderScene(SKCanvas canvas, SKPaint paint)
        {
            _dropsPath.Reset();
            for (int i = 0; i < _raindropCount; i++)
                _dropsPath.AddCircle(_raindrops[i].X, _raindrops[i].Y, 2f);

            paint.Style = SKPaintStyle.Fill;
            paint.Color = SKColors.White;
            canvas.DrawPath(_dropsPath, paint);

            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = RaindropsSettings.RippleStrokeWidth;

            _ripplesPath.Reset();
            for (int i = 0; i < _rippleCount; i++)
            {
                var ripple = _ripples[i];
                _ripplesPath.AddCircle(ripple.X, ripple.Y, ripple.Radius);
                paint.Color = paint.Color.WithAlpha((byte)(ripple.Alpha * 255));
                canvas.DrawPath(_ripplesPath, paint);
                _ripplesPath.Reset();
            }

            _particleBuffer.RenderParticles(canvas, paint);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateRipples()
        {
            for (int i = _rippleCount - 1; i >= 0; i--)
            {
                ref var ripple = ref _ripples[i];
                var newAlpha = ripple.Alpha * RaindropsSettings.RippleAlphaDecay;
                if (newAlpha < RaindropsSettings.RippleAlphaThreshold)
                    RemoveRipple(i);
                else
                    ripple = ripple.WithUpdatedValues(
                        ripple.Radius + RaindropsSettings.RippleExpandSpeed,
                        newAlpha
                    );
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CreateRipple(float x, float y)
        {
            if (_rippleCount < RaindropsSettings.MaxRipples)
                _ripples[_rippleCount++] = new Ripple(x, y, RaindropsSettings.InitialRadius, RaindropsSettings.InitialAlpha);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RemoveRipple(int index) => _ripples[index] = _ripples[--_rippleCount];

        private void UpdateParticles()
        {
            _particleBuffer.Clear();
            for (int i = 0; i < _raindropCount; i++)
            {
                var drop = _raindrops[i];
                var angle = (float)(_random.NextDouble() * Math.PI * 2);
                var speed = (float)(_random.NextDouble() * 2 + 1);

                _particleBuffer.AddParticle(new Particle(
                    drop.X,
                    drop.Y,
                    MathF.Cos(angle) * speed,
                    MathF.Sin(angle) * speed
                ));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureNotDisposed()
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(RaindropsRenderer));
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            _dropsPath.Dispose();
            _ripplesPath.Dispose();

            GC.SuppressFinalize(this);
        }

        #endregion
    }
}