#nullable enable

namespace SpectrumNet
{
    public sealed class ParticlesRenderer : ISpectrumRenderer, IDisposable
    {
        #region Instance
        private static readonly Lazy<ParticlesRenderer> _lazyInstance = new(() =>
        new ParticlesRenderer(), LazyThreadSafetyMode.ExecutionAndPublication);
        public static ParticlesRenderer GetInstance() => _lazyInstance.Value;
        #endregion

        #region Fields
        private static readonly object _lock = new();
        private readonly Random _random = new();
        private const int VelocityLookupSize = 1024;
        private CircularParticleBuffer? _particleBuffer;
        private bool _isOverlayActive, _isInitialized, _isDisposed;
        private RenderCache _renderCache = new();
        private readonly float _velocityRange, _particleLife, _particleLifeDecay,
            _alphaDecayExponent, _spawnThresholdOverlay, _spawnThresholdNormal,
            _spawnProbability, _particleSizeOverlay, _particleSizeNormal,
            _velocityMultiplier;
        private float[]? _spectrumBuffer, _velocityLookup, _alphaCurve;
        #endregion

        #region Constructor
        private ParticlesRenderer()
        {
            var s = Settings.Instance;
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
            PrecomputeAlphaCurve();
            InitializeVelocityLookup(s.ParticleVelocityMin);
        }
        #endregion

        #region Public Methods
        public void Initialize()
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(ParticlesRenderer));
            if (_isInitialized) return;
            _particleBuffer = new CircularParticleBuffer((int)Settings.Instance.MaxParticles, _particleLife, _particleLifeDecay, _velocityMultiplier);
            _isInitialized = true;
        }

        public void Configure(bool isOverlayActive)
        {
            if (_isOverlayActive != isOverlayActive)
            {
                _isOverlayActive = isOverlayActive;
                UpdateParticleSizes();
            }
        }

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
            if (!ValidateRenderParameters(canvas, spectrum, info, paint, drawPerformanceInfo)) return;

            // Update RenderCache
            _renderCache.Width = info.Width;
            _renderCache.Height = info.Height;
            UpdateRenderCacheBounds(info.Height);
            _renderCache.StepSize = barCount > 0 ? info.Width / barCount : 0f;

            float upperBound = _renderCache.UpperBound;
            float lowerBound = _renderCache.LowerBound;

            UpdateParticles(upperBound, lowerBound);

            SpawnNewParticles(spectrum.AsSpan(0, Math.Min(spectrum!.Length, 2048)), lowerBound, _renderCache.Width, barWidth);

            RenderParticles(canvas!, paint!, upperBound, lowerBound);

            drawPerformanceInfo!(canvas!, info);
        }

        private void UpdateRenderCacheBounds(float height)
        {
            var settings = Settings.Instance;
            float overlayHeight = height * settings.OverlayHeightMultiplier;

            if (_isOverlayActive)
            {
                _renderCache.UpperBound = height - overlayHeight;
                _renderCache.LowerBound = height;
                _renderCache.OverlayHeight = overlayHeight;
            }
            else
            {
                _renderCache.UpperBound = 0f;
                _renderCache.LowerBound = height;
                _renderCache.OverlayHeight = 0f;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion

        #region Private Methods
        private void PrecomputeAlphaCurve()
        {
            if (_alphaCurve == null)
                _alphaCurve = ArrayPool<float>.Shared.Rent(101);

            float step = 1f / (_alphaCurve.Length - 1);
            for (int i = 0; i < _alphaCurve.Length; i++)
                _alphaCurve[i] = (float)Math.Pow(i * step, _alphaDecayExponent);
        }

        private void InitializeVelocityLookup(float minVelocity)
        {
            for (int i = 0; i < VelocityLookupSize; i++)
                VelocityLookup[i] = minVelocity + _velocityRange * i / VelocityLookupSize;
        }

        private void UpdateParticles(float upperBound, float lowerBound)
        {
            if (_particleBuffer == null) return;
            _particleBuffer.Update(upperBound, _alphaDecayExponent);
        }

        private void SpawnNewParticles(ReadOnlySpan<float> spectrum, float spawnY, float canvasWidth, float barWidth)
        {
            if (_particleBuffer == null) return;
            float threshold = _isOverlayActive ? _spawnThresholdOverlay : _spawnThresholdNormal;
            float baseSize = _isOverlayActive ? _particleSizeOverlay : _particleSizeNormal;
            int targetCount = Math.Min(spectrum.Length / 2, 2048);
            ScaleSpectrum(spectrum, SpectrumBuffer.AsSpan(0, targetCount));
            float xStep = _renderCache.StepSize;

            for (int i = 0; i < targetCount; i++)
            {
                float spectrumValue = SpectrumBuffer[i];
                if (spectrumValue <= threshold) continue;

                float densityFactor = MathF.Min(spectrumValue / threshold, 3f);
                if (_random.NextDouble() >= densityFactor * _spawnProbability) continue;

                _particleBuffer.Add(new Particle
                {
                    X = i * xStep + (float)_random.NextDouble() * barWidth,
                    Y = spawnY,
                    Velocity = GetRandomVelocity() * densityFactor,
                    Size = baseSize * densityFactor,
                    Life = _particleLife,
                    Alpha = 1f,
                    IsActive = true
                });
            }
        }

        [ThreadStatic]
        private static Random? _threadLocalRandom;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float GetRandomVelocity()
        {
            var random = _threadLocalRandom ??= new Random();
            return VelocityLookup[random.Next(VelocityLookupSize)] * _velocityMultiplier;
        }

        private static void ScaleSpectrum(ReadOnlySpan<float> source, Span<float> dest)
        {
            int srcLen = source.Length / 2, destLen = dest.Length;
            if (srcLen == 0 || destLen == 0) return;

            float scale = srcLen / (float)destLen;
            for (int i = 0; i < destLen; i++)
                dest[i] = source[(int)(i * scale)];
        }

        private void UpdateParticleSizes()
        {
            if (_particleBuffer == null) return;
            float baseSize = _isOverlayActive ? _particleSizeOverlay : _particleSizeNormal;
            float oldBaseSize = _isOverlayActive ? _particleSizeNormal : _particleSizeOverlay;
            foreach (ref var particle in _particleBuffer.GetActiveParticles())
            {
                float relativeSizeFactor = particle.Size / oldBaseSize;
                particle.Size = baseSize * relativeSizeFactor;
            }
        }

        private void RenderParticles(SKCanvas canvas, SKPaint paint, float upperBound, float lowerBound)
        {
            if (_particleBuffer == null) return;
            var activeParticles = _particleBuffer.GetActiveParticles();
            if (activeParticles.Length == 0) return;
            paint.Style = SKPaintStyle.Fill;
            paint.StrokeCap = SKStrokeCap.Round;
            paint.StrokeWidth = 0;

            foreach (ref readonly var particle in activeParticles)
            {
                if (particle.Y < upperBound || particle.Y > lowerBound) continue;

                paint.Color = paint.Color.WithAlpha((byte)(particle.Alpha * 255));
                canvas.DrawCircle(particle.X, particle.Y, particle.Size / 2, paint);
            }
        }

        private bool ValidateRenderParameters(
            SKCanvas? canvas,
            float[]? spectrum,
            SKImageInfo info,
            SKPaint? paint,
            Action<SKCanvas, SKImageInfo>? drawPerformanceInfo)
            => !_isDisposed && _isInitialized && canvas != null && spectrum != null && spectrum.Length >= 2
               && paint != null && drawPerformanceInfo != null && info.Width > 0 && info.Height > 0;

        private void Dispose(bool disposing)
        {
            if (_isDisposed)
                return;

            if (disposing)
            {
                if (_spectrumBuffer != null)
                {
                    ArrayPool<float>.Shared.Return(_spectrumBuffer);
                    _spectrumBuffer = null;
                }
                if (_velocityLookup != null)
                {
                    ArrayPool<float>.Shared.Return(_velocityLookup);
                    _velocityLookup = null;
                }
                if (_alphaCurve != null)
                {
                    ArrayPool<float>.Shared.Return(_alphaCurve);
                    _alphaCurve = null;
                }
                _particleBuffer = null;
                _renderCache = new RenderCache();
            }

            _isInitialized = false;
            _isDisposed = true;
        }
        #endregion

        #region Nested Classes
        private class CircularParticleBuffer
        {
            private readonly Particle[] _buffer;
            private int _head, _tail, _count;
            private readonly float _particleLife, _particleLifeDecay, _velocityMultiplier;

            public CircularParticleBuffer(int capacity, float particleLife, float particleLifeDecay, float velocityMultiplier)
            {
                _buffer = new Particle[capacity];
                _head = 0;
                _tail = 0;
                _count = 0;
                _particleLife = particleLife;
                _particleLifeDecay = particleLifeDecay;
                _velocityMultiplier = velocityMultiplier;
            }

            public void Add(Particle particle)
            {
                if (_count < _buffer.Length)
                {
                    _buffer[_tail] = particle;
                    _tail = (_tail + 1) % _buffer.Length;
                    _count++;
                }
            }

            public Span<Particle> GetActiveParticles()
            {
                return _buffer.AsSpan(_head, _count);
            }

            public void Update(float upperBound, float alphaDecayExponent)
            {
                int activeCount = 0;
                for (int i = 0; i < _count; i++)
                {
                    int index = (_head + i) % _buffer.Length;
                    ref var particle = ref _buffer[index];
                    if (!particle.IsActive || particle.Y < upperBound || particle.Alpha <= 0.01f)
                    {
                        particle.IsActive = false;
                    }
                    else
                    {
                        particle.Y -= particle.Velocity * _velocityMultiplier;
                        particle.Life -= _particleLifeDecay;
                        particle.Alpha = (float)Math.Pow(particle.Life / _particleLife, alphaDecayExponent);
                        particle.Size *= 0.99f;
                        _buffer[activeCount++] = particle;
                    }
                }
                _count = activeCount;
                _head = 0;
            }
        }
        #endregion

        #region Properties for Lazy Initialization
        private float[] SpectrumBuffer => _spectrumBuffer ??= ArrayPool<float>.Shared.Rent(2048);
        private float[] VelocityLookup => _velocityLookup ??= ArrayPool<float>.Shared.Rent(VelocityLookupSize);
        private float[] AlphaCurve => _alphaCurve ??= ArrayPool<float>.Shared.Rent(101);
        #endregion

        #region Particle Struct
        private struct Particle
        {
            public float X { get; set; }
            public float Y { get; set; }
            public float Velocity { get; set; }
            public float Size { get; set; }
            public float Life { get; set; }
            public float Alpha { get; set; }
            public bool IsActive { get; set; }
        }
        #endregion
    }
}