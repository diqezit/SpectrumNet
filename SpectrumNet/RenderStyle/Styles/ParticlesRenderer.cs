#nullable enable

namespace SpectrumNet
{
    public sealed class ParticlesRenderer : ISpectrumRenderer, IDisposable
    {
        #region Fields
        private static readonly object _lock = new();
        private static ParticlesRenderer? _instance;
        private readonly Random _random = new();
        private const int VelocityLookupSize = 1024;

        private float[] _spectrumBuffer, _velocityLookup, _alphaCurve, _velocityLookupCached;
        private Particle[]? _particles;
        private ParticlePool _particlePool;

        private bool _isOverlayActive, _isInitialized, _isDisposed;
        private int _activeParticleCount;

        private readonly float _velocityRange, _particleLife, _particleLifeDecay, _alphaDecayExponent,
                               _spawnThresholdOverlay, _spawnThresholdNormal, _spawnProbability,
                               _particleSizeOverlay, _particleSizeNormal, _velocityMultiplier;

        private struct Particle
        {
            public float X, Y, Velocity, Life, Alpha, Size;
            public bool IsActive;
        }

        private class ParticlePool
        {
            private readonly Particle[] _particles;
            private readonly bool[] _isAvailable;
            private readonly object _lock = new();

            public ParticlePool(int maxParticles)
            {
                _particles = new Particle[maxParticles];
                _isAvailable = new bool[maxParticles];
                for (int i = 0; i < _isAvailable.Length; i++) _isAvailable[i] = true;
            }
        }
        #endregion

        #region Constructor
        private ParticlesRenderer()
        {
            var s = Settings.Instance ?? throw new InvalidOperationException("Settings not initialized");

            _spectrumBuffer = new float[2048];
            _velocityLookup = new float[VelocityLookupSize];
            _particlePool = new ParticlePool((int)s.MaxParticles);
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
        private static readonly Lazy<ParticlesRenderer> _lazyInstance =
            new(() => new ParticlesRenderer(), LazyThreadSafetyMode.ExecutionAndPublication);

        public static ParticlesRenderer GetInstance() => _lazyInstance.Value;

        public void Initialize()
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(ParticlesRenderer));
            if (_isInitialized) return;

            _particles = new Particle[(int)Settings.Instance.MaxParticles];
            _activeParticleCount = 0;
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

        public void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info, float barWidth,
                           float barSpacing, int barCount, SKPaint? paint,
                           Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
        {
            if (!ValidateRenderParameters(canvas, spectrum, info, paint, drawPerformanceInfo)) return;

            var (upperBound, lowerBound) = CalculateBounds(info.Height);
            UpdateParticles(upperBound, lowerBound);
            SpawnNewParticles(spectrum.AsSpan(), lowerBound, info.Width, barWidth);
            RenderParticles(canvas!, paint!, upperBound, lowerBound);
            drawPerformanceInfo(canvas!, info);
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _particles = null;
                _activeParticleCount = 0;
                _isInitialized = false;
                _isDisposed = true;
                GC.SuppressFinalize(this);
            }
        }
        #endregion

        #region Private Methods
        private void PrecomputeAlphaCurve() =>
            _alphaCurve = Enumerable.Range(0, 101)
                                    .Select(i => MathF.Pow(i / 100f, _alphaDecayExponent))
                                    .ToArray();

        private void InitializeVelocityLookup(float minVelocity)
        {
            for (int i = 0; i < VelocityLookupSize; i++)
                _velocityLookup[i] = minVelocity + _velocityRange * i / VelocityLookupSize;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float GetCachedAlpha(float normalizedLife) =>
            _alphaCurve[Math.Clamp((int)(normalizedLife * 100), 0, 100)];

        private void UpdateParticles(float upperBound, float lowerBound)
        {
            if (_particles == null) return;

            var span = _particles.AsSpan(0, _activeParticleCount);
            int count = 0;

            foreach (ref var particle in span)
            {
                particle.Y -= particle.Velocity * _velocityMultiplier;
                particle.Life -= _particleLifeDecay;
                particle.Alpha = GetCachedAlpha(particle.Life / _particleLife);
                if (particle.Alpha > 0.01f && particle.Y >= upperBound)
                    span[count++] = particle;
            }
            _activeParticleCount = count;
        }

        private void SpawnNewParticles(ReadOnlySpan<float> spectrum, float spawnY, float canvasWidth, float barWidth)
        {
            if (_particles == null || _activeParticleCount >= _particles.Length) return;

            float threshold = _isOverlayActive ? _spawnThresholdOverlay : _spawnThresholdNormal;
            float size = _isOverlayActive ? _particleSizeOverlay : _particleSizeNormal;
            int targetCount = Math.Min(spectrum.Length / 2, _spectrumBuffer.Length);
            ScaleSpectrum(spectrum, _spectrumBuffer.AsSpan(0, targetCount));
            float xStep = canvasWidth / targetCount;

            for (int i = 0; i < targetCount && _activeParticleCount < _particles.Length; i++)
            {
                if (_spectrumBuffer[i] <= threshold) continue;

                float probability = Math.Min(_spectrumBuffer[i] / threshold, 1f) * _spawnProbability;
                if (_random.NextDouble() >= probability) continue;

                float factor = Math.Min(_spectrumBuffer[i] / threshold, 2f);
                _particles[_activeParticleCount++] = new Particle
                {
                    X = i * xStep + (float)_random.NextDouble() * barWidth,
                    Y = spawnY,
                    Velocity = GetRandomVelocity() * factor,
                    Size = size * factor,
                    Life = _particleLife,
                    Alpha = 1f,
                    IsActive = true
                };
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float GetRandomVelocity() =>
            _velocityLookup[_random.Next(VelocityLookupSize)] * _velocityMultiplier;

        private void ScaleSpectrum(ReadOnlySpan<float> source, Span<float> dest)
        {
            int srcLen = source.Length / 2, destLen = dest.Length;
            if (srcLen == 0 || destLen == 0) return;

            int i = 0, vectorCount = Vector<float>.Count;
            Span<float> indices = stackalloc float[vectorCount];

            for (; i <= destLen - vectorCount; i += vectorCount)
            {
                for (int j = 0; j < vectorCount; j++)
                    indices[j] = (i + j) / (float)destLen * srcLen;

                for (int j = 0; j < vectorCount; j++)
                    dest[i + j] = source[Math.Clamp((int)indices[j], 0, srcLen - 1)];
            }

            for (; i < destLen; i++)
                dest[i] = source[Math.Clamp((int)(i / (float)destLen * srcLen), 0, srcLen - 1)];
        }

        private void UpdateParticleSizes()
        {
            if (_particles == null) return;

            float size = _isOverlayActive ? _particleSizeOverlay : _particleSizeNormal;
            foreach (ref var particle in _particles.AsSpan(0, _activeParticleCount))
                if (particle.IsActive) particle.Size = size;
        }

        private void RenderParticles(SKCanvas canvas, SKPaint paint, float upperBound, float lowerBound)
        {
            if (_particles == null) return;

            var originalColor = paint.Color;
            foreach (ref readonly var particle in _particles.AsSpan(0, _activeParticleCount))
            {
                if (particle.Y < upperBound || particle.Y > lowerBound) continue;
                paint.Color = originalColor.WithAlpha((byte)(particle.Alpha * 255));
                canvas.DrawCircle(particle.X, particle.Y, particle.Size, paint);
            }
            paint.Color = originalColor;
        }

        private (float upperBound, float lowerBound) CalculateBounds(float height)
        {
            var settings = Settings.Instance ?? throw new InvalidOperationException("Settings not initialized");
            float overlayHeight = height * settings.OverlayHeightMultiplier;
            float baseY = height * (_isOverlayActive ? settings.OverlayOffsetMultiplier : 1f);
            return _isOverlayActive
                ? (baseY - overlayHeight, baseY)
                : (0, height);
        }

        private bool ValidateRenderParameters(SKCanvas? canvas, float[]? spectrum, SKImageInfo info,
            SKPaint? paint, Action<SKCanvas, SKImageInfo>? drawPerformanceInfo) =>
            !_isDisposed && _isInitialized && _particles != null && canvas != null &&
            spectrum != null && paint != null && drawPerformanceInfo != null &&
            spectrum.Length >= 2 && info.Width > 0 && info.Height > 0;
        #endregion

    }
}