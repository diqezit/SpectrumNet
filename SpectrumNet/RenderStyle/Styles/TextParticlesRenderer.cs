#nullable enable

namespace SpectrumNet
{
    public sealed class TextParticlesRenderer : ISpectrumRenderer, IDisposable
    {
        private const int VelocityLookupSize = 1024;
        private const float FocalLength = 1000f;
        private const float BaseTextSize = 12f;

        private readonly Random _random = new();
        private CircularParticleBuffer? _particleBuffer;
        private RenderCache _renderCache = new();
        private float[]? _spectrumBuffer, _velocityLookup, _alphaCurve;
        private readonly string _characters = "01";
        private readonly float _velocityRange, _particleLife, _particleLifeDecay, _alphaDecayExponent,
            _spawnThresholdOverlay, _spawnThresholdNormal, _spawnProbability, _particleSizeOverlay,
            _particleSizeNormal, _velocityMultiplier, _zRange;
        private bool _isOverlayActive, _isInitialized, _isDisposed;
        private readonly SKFont _font = new() { Size = BaseTextSize };
        private float[] SpectrumBuffer => _spectrumBuffer ??= ArrayPool<float>.Shared.Rent(2048);

        private TextParticlesRenderer()
        {
            Settings s = Settings.Instance;
            (_velocityRange, _particleLife, _particleLifeDecay, _alphaDecayExponent) =
                (s.ParticleVelocityMax - s.ParticleVelocityMin, s.ParticleLife, s.ParticleLifeDecay, s.AlphaDecayExponent);
            (_spawnThresholdOverlay, _spawnThresholdNormal, _spawnProbability) =
                (s.SpawnThresholdOverlay, s.SpawnThresholdNormal, s.SpawnProbability);
            (_particleSizeOverlay, _particleSizeNormal, _velocityMultiplier) =
                (s.ParticleSizeOverlay, s.ParticleSizeNormal, s.VelocityMultiplier);
            _zRange = s.MaxZDepth - s.MinZDepth;

            PrecomputeAlphaCurve();
            InitializeVelocityLookup(s.ParticleVelocityMin);
        }

        private static readonly Lazy<TextParticlesRenderer> _lazyInstance =
            new(() => new TextParticlesRenderer(), LazyThreadSafetyMode.ExecutionAndPublication);
        public static TextParticlesRenderer GetInstance() => _lazyInstance.Value;

        public void Configure(bool isOverlayActive)
        {
            if (_isOverlayActive == isOverlayActive) return;
            _isOverlayActive = isOverlayActive;
            UpdateParticleSizes();
        }

        public void Initialize()
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(TextParticlesRenderer));
            if (_isInitialized) return;

            _particleBuffer = new CircularParticleBuffer(
                (int)Settings.Instance.MaxParticles,
                _particleLife, _particleLifeDecay, _velocityMultiplier, this);

            _renderCache = new RenderCache();
            _isInitialized = true;
            Log.Information("TextParticlesRenderer initialized");
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            if (_spectrumBuffer != null) ArrayPool<float>.Shared.Return(_spectrumBuffer);
            if (_velocityLookup != null) ArrayPool<float>.Shared.Return(_velocityLookup);
            if (_alphaCurve != null) ArrayPool<float>.Shared.Return(_alphaCurve);
            _spectrumBuffer = _velocityLookup = _alphaCurve = null;
            _particleBuffer = null;
            _font.Dispose();
            _renderCache = new RenderCache();
            _isInitialized = false;
            _isDisposed = true;
            GC.SuppressFinalize(this);
        }

        public void Render(SKCanvas canvas, float[] spectrum, SKImageInfo info, float barWidth,
            float barSpacing, int barCount, SKPaint paint, Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
        {
            if (!ValidateRenderParameters(canvas, spectrum, info, paint, drawPerformanceInfo))
            {
                Log.Error("Invalid render parameters");
                return;
            }

            if (_particleBuffer == null)
            {
                Log.Warning("Particle buffer is null");
                return;
            }

            _renderCache.Width = info.Width;
            _renderCache.Height = info.Height;
            _renderCache.StepSize = barCount > 0 ? info.Width / barCount : 0f;

            UpdateRenderCacheBounds(info.Height);
            _particleBuffer.Update(_renderCache.UpperBound, _renderCache.LowerBound, _alphaDecayExponent);
            SpawnNewParticles(
                spectrum.AsSpan(0, Math.Min(spectrum.Length / 2, barCount)),
                _renderCache.LowerBound,
                _renderCache.Width,
                barWidth
            );

            RenderParticles(canvas, paint);
            drawPerformanceInfo(canvas, info);
        }

        private void SpawnNewParticles(ReadOnlySpan<float> spectrum, float spawnY, float canvasWidth, float barWidth)
        {
            if (_particleBuffer == null) return;

            float threshold = _isOverlayActive ? _spawnThresholdOverlay : _spawnThresholdNormal;
            float baseSize = _isOverlayActive ? _particleSizeOverlay : _particleSizeNormal;
            var scaledSpectrum = SpectrumBuffer.AsSpan(0, spectrum.Length);
            ScaleSpectrum(spectrum, scaledSpectrum);

            for (int i = 0; i < scaledSpectrum.Length; i++)
            {
                float spectrumValue = scaledSpectrum[i];
                if (spectrumValue <= threshold) continue;

                float spawnChance = Math.Min(spectrumValue / threshold, 3f) * _spawnProbability;
                if (_random.NextDouble() >= spawnChance) continue;

                float intensity = Math.Min(spectrumValue / threshold, 3f);
                _particleBuffer.Add(new Particle
                {
                    X = i * _renderCache.StepSize + _random.NextSingle() * barWidth,
                    Y = spawnY + _random.NextSingle() * 5f - 2.5f,
                    Z = Settings.Instance.MinZDepth + _random.NextSingle() * _zRange,
                    VelocityY = -GetRandomVelocity() * intensity,
                    VelocityX = (_random.NextSingle() - 0.5f) * 2f,
                    Size = baseSize * intensity,
                    Life = _particleLife * (0.8f + _random.NextSingle() * 0.4f),
                    Alpha = 1f,
                    IsActive = true,
                    Character = _characters[_random.Next(_characters.Length)]
                });
            }
        }

        private void RenderParticles(SKCanvas canvas, SKPaint paint)
        {
            if (_particleBuffer == null || canvas == null || paint == null) return;

            var activeParticles = _particleBuffer.GetActiveParticles();
            if (activeParticles.IsEmpty) return;

            paint.Style = SKPaintStyle.Fill;
            float centerX = _renderCache.Width / 2f;
            float centerY = _renderCache.Height / 2f;

            foreach (ref readonly var p in activeParticles)
            {
                if (!p.IsActive || p.Y < _renderCache.UpperBound || p.Y > _renderCache.LowerBound) continue;

                float depth = FocalLength + p.Z;
                float scale = FocalLength / depth;
                float screenX = centerX + (p.X - centerX) * scale;
                float screenY = centerY + (p.Y - centerY) * scale;

                if (float.IsNaN(screenX) || float.IsNaN(screenY)) continue;
                if (screenX < 0 || screenX > _renderCache.Width || screenY < 0 || screenY > _renderCache.Height) continue;

                paint.Color = paint.Color.WithAlpha((byte)(p.Alpha * (depth / (FocalLength + p.Z)) * 255));
                canvas.DrawText(p.Character.ToString(), screenX, screenY, _font, paint);
            }
        }

        private struct Particle
        {
            public float X, Y, Z;
            public float VelocityY, VelocityX;
            public float Size, Life, Alpha;
            public bool IsActive;
            public char Character;
        }

        private void PrecomputeAlphaCurve()
        {
            _alphaCurve = ArrayPool<float>.Shared.Rent(101);
            float step = 1f / (_alphaCurve.Length - 1);
            for (int i = 0; i < _alphaCurve.Length; i++)
                _alphaCurve[i] = (float)Math.Pow(i * step, _alphaDecayExponent);
        }

        private void InitializeVelocityLookup(float minVelocity)
        {
            _velocityLookup = ArrayPool<float>.Shared.Rent(VelocityLookupSize);
            for (int i = 0; i < VelocityLookupSize; i++)
                _velocityLookup[i] = minVelocity + _velocityRange * i / VelocityLookupSize;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float GetRandomVelocity() => _velocityLookup?[_random.Next(VelocityLookupSize)] * _velocityMultiplier
            ?? throw new InvalidOperationException("Velocity lookup is not initialized.");

        private void UpdateParticleSizes()
        {
            if (_particleBuffer == null) return;
            float baseSize = _isOverlayActive ? _particleSizeOverlay : _particleSizeNormal;
            float oldBaseSize = _isOverlayActive ? _particleSizeNormal : _particleSizeOverlay;

            foreach (ref var particle in _particleBuffer.GetActiveParticles())
                particle.Size = baseSize * (particle.Size / oldBaseSize);
        }

        private void UpdateRenderCacheBounds(float height)
        {
            float overlayHeight = height * Settings.Instance.OverlayHeightMultiplier;
            _renderCache.OverlayHeight = _isOverlayActive ? overlayHeight : 0f;
            _renderCache.UpperBound = _isOverlayActive ? height - overlayHeight : 0f;
            _renderCache.LowerBound = height;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ValidateRenderParameters(SKCanvas? canvas, float[]? spectrum, SKImageInfo info,
            SKPaint? paint, Action<SKCanvas, SKImageInfo>? drawPerformanceInfo) =>
            !_isDisposed && _isInitialized && canvas != null && spectrum != null &&
            spectrum.Length >= 2 && paint != null && drawPerformanceInfo != null &&
            info.Width > 0 && info.Height > 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private static void ScaleSpectrum(ReadOnlySpan<float> source, Span<float> dest)
        {
            if (source.IsEmpty || dest.IsEmpty) return;
            if (dest.Length == source.Length) { source.CopyTo(dest); return; }

            float scale = (float)(source.Length - 1) / (dest.Length - 1);
            for (int i = 0; i < dest.Length; i++)
            {
                float index = i * scale;
                int baseIndex = (int)index;
                float fraction = index - baseIndex;
                dest[i] = baseIndex >= source.Length - 1
                    ? source[^1]
                    : source[baseIndex] * (1 - fraction) + source[baseIndex + 1] * fraction;
            }
        }

        private sealed class CircularParticleBuffer
        {
            private readonly TextParticlesRenderer _renderer;
            private readonly Particle[] _buffer;
            private readonly float _particleLife, _particleLifeDecay, _velocityMultiplier;
            private int _head, _tail, _count;
            private const float Gravity = 9.81f, AirResistance = 0.98f, MaxVelocity = 15f;

            public CircularParticleBuffer(int capacity, float particleLife, float particleLifeDecay,
                float velocityMultiplier, TextParticlesRenderer renderer)
            {
                _buffer = new Particle[capacity];
                (_particleLife, _particleLifeDecay, _velocityMultiplier, _renderer) =
                    (particleLife, particleLifeDecay, velocityMultiplier, renderer);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Add(Particle particle)
            {
                if (_count < _buffer.Length)
                {
                    _buffer[_tail] = particle;
                    _tail = (_tail + 1) % _buffer.Length;
                    _count++;
                }
            }

            public Span<Particle> GetActiveParticles() => _buffer.AsSpan(_head, _count);

            public void Update(float upperBound, float lowerBound, float alphaDecayExponent)
            {
                if (_count == 0) return;

                int writeIndex = 0;
                float lifeRatioScale = 1f / _particleLife;

                for (int i = 0, readIndex = _head; i < _count; i++, readIndex = (readIndex + 1) % _buffer.Length)
                {
                    ref var particle = ref _buffer[readIndex];
                    if (!particle.IsActive || !UpdateParticle(ref particle, upperBound, lowerBound,
                        lifeRatioScale, alphaDecayExponent)) continue;

                    if (writeIndex != readIndex) _buffer[writeIndex] = particle;
                    writeIndex++;
                }

                _count = writeIndex;
                _head = 0;
                _tail = writeIndex % _buffer.Length;
            }

            private bool UpdateParticle(ref Particle p, float upperBound, float lowerBound,
                float lifeRatioScale, float alphaDecayExponent)
            {
                p.Life -= _particleLifeDecay;
                if (p.Life <= 0 || p.Y < upperBound - 50 || p.Y > lowerBound + 50)
                {
                    p.IsActive = false;
                    return false;
                }

                p.VelocityY = Math.Clamp((p.VelocityY + Gravity * 0.016f) * AirResistance,
                    -MaxVelocity * _velocityMultiplier, MaxVelocity * _velocityMultiplier);

                if (_renderer._random.NextDouble() < 0.1)
                    p.VelocityX += (_renderer._random.NextSingle() - 0.5f) * 0.5f;

                p.VelocityX *= AirResistance;
                p.Y += p.VelocityY;
                p.X += p.VelocityX;
                p.Alpha = CalculateAlpha(p.Life * lifeRatioScale, alphaDecayExponent);
                return true;
            }

            private float CalculateAlpha(float lifeRatio, float alphaDecayExponent) =>
                lifeRatio <= 0 ? 0f :
                lifeRatio >= 1 ? 1f :
                _renderer._alphaCurve?[(int)(lifeRatio * 100)] ??
                (float)Math.Pow(lifeRatio, alphaDecayExponent);
        }

        private sealed record RenderCache
        {
            public float Width, Height, StepSize, OverlayHeight, UpperBound, LowerBound;
        }
    }
}