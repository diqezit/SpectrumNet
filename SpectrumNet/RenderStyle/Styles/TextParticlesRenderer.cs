#nullable enable

namespace SpectrumNet
{
    public static class TextParticleConstants
    {
        public static class Rendering
        {
            public const float FocalLength = 1000f;
            public const float BaseTextSize = 12f;
        }

        public static class Particles
        {
            public const int VelocityLookupSize = 1024;
            public const float Gravity = 9.81f;
            public const float AirResistance = 0.98f;
            public const float MaxVelocity = 15f;
            public const float RandomDirectionChance = 0.05f;
            public const float DirectionVariance = 0.5f;
            public const float SpawnVariance = 5f;
            public const float SpawnHalfVariance = SpawnVariance / 2f;
            public const string DefaultCharacters = "01";
        }

        public static class Boundaries
        {
            public const float BoundaryMargin = 50f;
        }
    }

    public sealed class TextParticlesRenderer : ISpectrumRenderer, IDisposable
    {
        #region Fields
        private static readonly Lazy<TextParticlesRenderer> _lazyInstance =
            new(() => new TextParticlesRenderer(), LazyThreadSafetyMode.ExecutionAndPublication);

        private readonly Random _random = new();
        private CircularParticleBuffer? _particleBuffer;
        private RenderCache _renderCache = new();
        private float[]? _spectrumBuffer, _velocityLookup, _alphaCurve;
        private readonly string _characters = TextParticleConstants.Particles.DefaultCharacters;
        private readonly float _velocityRange, _particleLife, _particleLifeDecay, _alphaDecayExponent,
            _spawnThresholdOverlay, _spawnThresholdNormal, _spawnProbability, _particleSizeOverlay,
            _particleSizeNormal, _velocityMultiplier, _zRange;
        private bool _isOverlayActive, _isInitialized, _isDisposed;
        private readonly SKFont _font;
        private readonly SemaphoreSlim _renderSemaphore = new(1, 1);
        private readonly object _particleLock = new();
        private float[] SpectrumBuffer => _spectrumBuffer ??= ArrayPool<float>.Shared.Rent(2048);
        #endregion

        #region Constructor and Initialization
        private TextParticlesRenderer()
        {
            Settings s = Settings.Instance;

            _velocityRange = s.ParticleVelocityMax - s.ParticleVelocityMin;
            _particleLife = s.ParticleLife;
            _particleLifeDecay = s.ParticleLifeDecay;
            _alphaDecayExponent = s.AlphaDecayExponent;
            _spawnThresholdOverlay = s.SpawnThresholdOverlay;
            _spawnThresholdNormal = s.SpawnThresholdNormal;
            _spawnProbability = s.SpawnProbability;
            _particleSizeOverlay = s.ParticleSizeOverlay;
            _particleSizeNormal = s.ParticleSizeNormal;
            _velocityMultiplier = s.VelocityMultiplier;
            _zRange = s.MaxZDepth - s.MinZDepth;

            _font = new SKFont
            {
                Size = TextParticleConstants.Rendering.BaseTextSize,
                Edging = SKFontEdging.SubpixelAntialias
            };

            PrecomputeAlphaCurve();
            InitializeVelocityLookup(s.ParticleVelocityMin);
        }

        public static TextParticlesRenderer GetInstance() => _lazyInstance.Value;

        public void Initialize()
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(TextParticlesRenderer));
            if (_isInitialized) return;

            _particleBuffer = new CircularParticleBuffer(
                Settings.Instance.MaxParticles,
                _particleLife, _particleLifeDecay, _velocityMultiplier, this);

            _renderCache = new RenderCache();

            _isInitialized = true;
            Log.Information("TextParticlesRenderer initialized");
        }
        #endregion

        #region Configuration
        public void Configure(bool isOverlayActive)
        {
            if (_isOverlayActive == isOverlayActive) return;
            _isOverlayActive = isOverlayActive;
            UpdateParticleSizes();
        }
        #endregion

        #region Rendering
        public void Render(
            SKCanvas? canvas,
            float[]? spectrum,
            SKImageInfo info,
            float barWidth,
            float barSpacing,
            int barCount,
            SKPaint? paint,
            Action<SKCanvas, SKImageInfo>? drawPerformanceInfo)
        {
            if (!ValidateRenderParameters(canvas, spectrum, info, paint, drawPerformanceInfo))
            {
                Log.Error("Invalid render parameters for TextParticlesRenderer");
                return;
            }

            if (_particleBuffer == null)
            {
                Log.Warning("Particle buffer is null");
                return;
            }

            bool semaphoreAcquired = false;
            try
            {
                semaphoreAcquired = _renderSemaphore.Wait(0);

                if (semaphoreAcquired)
                {
                    _renderCache.Width = info.Width;
                    _renderCache.Height = info.Height;
                    _renderCache.StepSize = barCount > 0 ? info.Width / barCount : 0f;

                    UpdateRenderCacheBounds(info.Height);
                    _particleBuffer.Update(_renderCache.UpperBound, _renderCache.LowerBound, _alphaDecayExponent);
                }

                // spectrum проверен в ValidateRenderParameters и не может быть null здесь
                int spectrumLength = Math.Min(spectrum!.Length / 2, barCount);

                if (spectrumLength > 0)
                {
                    SpawnNewParticles(
                        spectrum.AsSpan(0, spectrumLength),
                        _renderCache.LowerBound,
                        _renderCache.Width,
                        barWidth
                    );
                }

                RenderParticles(canvas!, paint!);
                drawPerformanceInfo?.Invoke(canvas!, info);
            }
            catch (Exception ex)
            {
                Log.Error($"Error in TextParticlesRenderer.Render: {ex.Message}");
            }
            finally
            {
                if (semaphoreAcquired)
                {
                    _renderSemaphore.Release();
                }
            }
        }

        private void SpawnNewParticles(ReadOnlySpan<float> spectrum, float spawnY, float canvasWidth, float barWidth)
        {
            if (_particleBuffer == null || spectrum.IsEmpty) return;

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
                    Y = spawnY + _random.NextSingle() * TextParticleConstants.Particles.SpawnVariance - TextParticleConstants.Particles.SpawnHalfVariance,
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

            using var particlePaint = paint.Clone();
            particlePaint.Style = SKPaintStyle.Fill;
            particlePaint.IsAntialias = true;
            particlePaint.FilterQuality = SKFilterQuality.Medium;

            float centerX = _renderCache.Width / 2f;
            float centerY = _renderCache.Height / 2f;

            foreach (ref readonly var p in activeParticles)
            {
                if (!p.IsActive || p.Y < _renderCache.UpperBound || p.Y > _renderCache.LowerBound) continue;

                float depth = TextParticleConstants.Rendering.FocalLength + p.Z;
                float scale = TextParticleConstants.Rendering.FocalLength / depth;
                float screenX = centerX + (p.X - centerX) * scale;
                float screenY = centerY + (p.Y - centerY) * scale;

                if (screenX < 0 || screenX > _renderCache.Width || screenY < 0 || screenY > _renderCache.Height) continue;

                byte alpha = (byte)(p.Alpha * (depth / (TextParticleConstants.Rendering.FocalLength + p.Z)) * 255);
                particlePaint.Color = paint.Color.WithAlpha(alpha);
                canvas.DrawText(p.Character.ToString(), screenX, screenY, _font, particlePaint);
            }
        }
        #endregion

        #region Particle Structures and Classes
        private struct Particle
        {
            public float X, Y, Z;
            public float VelocityY, VelocityX;
            public float Size, Life, Alpha;
            public bool IsActive;
            public char Character;
        }

        private sealed class CircularParticleBuffer
        {
            private readonly TextParticlesRenderer _renderer;
            private readonly Particle[] _buffer;
            private readonly float _particleLife, _particleLifeDecay, _velocityMultiplier;
            private int _head, _tail, _count;

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
                if (p.Life <= 0 || p.Y < upperBound - TextParticleConstants.Boundaries.BoundaryMargin ||
                    p.Y > lowerBound + TextParticleConstants.Boundaries.BoundaryMargin)
                {
                    p.IsActive = false;
                    return false;
                }

                p.VelocityY = Math.Clamp(
                    (p.VelocityY + TextParticleConstants.Particles.Gravity * 0.016f) * TextParticleConstants.Particles.AirResistance,
                    -TextParticleConstants.Particles.MaxVelocity * _velocityMultiplier,
                    TextParticleConstants.Particles.MaxVelocity * _velocityMultiplier);

                if (_renderer._random.NextDouble() < TextParticleConstants.Particles.RandomDirectionChance)
                    p.VelocityX += (_renderer._random.NextSingle() - 0.5f) * TextParticleConstants.Particles.DirectionVariance;

                p.VelocityX *= TextParticleConstants.Particles.AirResistance;
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
        #endregion

        #region Helper Methods
        private void PrecomputeAlphaCurve()
        {
            _alphaCurve = ArrayPool<float>.Shared.Rent(101);
            float step = 1f / (_alphaCurve.Length - 1);
            for (int i = 0; i < _alphaCurve.Length; i++)
                _alphaCurve[i] = (float)Math.Pow(i * step, _alphaDecayExponent);
        }

        private void InitializeVelocityLookup(float minVelocity)
        {
            _velocityLookup = ArrayPool<float>.Shared.Rent(TextParticleConstants.Particles.VelocityLookupSize);
            for (int i = 0; i < TextParticleConstants.Particles.VelocityLookupSize; i++)
                _velocityLookup[i] = minVelocity + _velocityRange * i / TextParticleConstants.Particles.VelocityLookupSize;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float GetRandomVelocity() => _velocityLookup?[_random.Next(TextParticleConstants.Particles.VelocityLookupSize)] * _velocityMultiplier
            ?? throw new InvalidOperationException("Velocity lookup is not initialized.");

        private void UpdateParticleSizes()
        {
            if (_particleBuffer == null) return;

            float baseSize = _isOverlayActive ? _particleSizeOverlay : _particleSizeNormal;
            float oldBaseSize = _isOverlayActive ? _particleSizeNormal : _particleSizeOverlay;

            lock (_particleLock)
            {
                foreach (ref var particle in _particleBuffer.GetActiveParticles())
                    particle.Size = baseSize * (particle.Size / oldBaseSize);
            }
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
        #endregion

        #region Disposal
        public void Dispose()
        {
            if (_isDisposed) return;

            _renderSemaphore.Dispose();
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
        #endregion
    }
}