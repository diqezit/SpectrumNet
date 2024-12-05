#nullable enable

namespace SpectrumNet
{
    public sealed class ParticlesRenderer : ISpectrumRenderer, IDisposable
    {
        #region Fields
        private static readonly object _lock = new();
        private readonly Random _random = new();
        private const int VelocityLookupSize = 1024;

        private float[] _spectrumBuffer, _velocityLookup, _alphaCurve;
        private CircularParticleBuffer? _particleBuffer;

        private bool _isOverlayActive, _isInitialized, _isDisposed;

        private readonly float _velocityRange, _particleLife, _particleLifeDecay, _alphaDecayExponent,
                               _spawnThresholdOverlay, _spawnThresholdNormal, _spawnProbability,
                               _particleSizeOverlay, _particleSizeNormal, _velocityMultiplier;

        private struct Particle
        {
            public float X, Y, Velocity, Life, Alpha, Size;
            public bool IsActive;
        }
        #endregion

        #region Constructor
        private ParticlesRenderer()
        {
            var s = Settings.Instance ?? throw new InvalidOperationException("Settings not initialized");

            _spectrumBuffer = new float[2048];
            _velocityLookup = new float[VelocityLookupSize];
            _alphaCurve = new float[101]; // Initialize to match PrecomputeAlphaCurve
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

            _particleBuffer = new CircularParticleBuffer(
                (int)Settings.Instance.MaxParticles,
                _particleLife,
                _particleLifeDecay,
                _velocityMultiplier
            );
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
                _particleBuffer = null;
                _isInitialized = false;
                _isDisposed = true;
                GC.SuppressFinalize(this);
            }
        }
        #endregion

        #region Private Methods
        private void PrecomputeAlphaCurve()
        {
            for (int i = 0; i <= 100; i++)
                _alphaCurve[i] = MathF.Pow(i / 100f, _alphaDecayExponent);
        }

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
            if (_particleBuffer == null) return;

            _particleBuffer.Update(upperBound, _alphaDecayExponent);
        }

        private void SpawnNewParticles(ReadOnlySpan<float> spectrum, float spawnY, float canvasWidth, float barWidth)
        {
            if (_particleBuffer == null) return;

            float threshold = _isOverlayActive ? _spawnThresholdOverlay : _spawnThresholdNormal;
            float size = _isOverlayActive ? _particleSizeOverlay : _particleSizeNormal;
            int targetCount = Math.Min(spectrum.Length / 2, _spectrumBuffer.Length);
            ScaleSpectrum(spectrum, _spectrumBuffer.AsSpan(0, targetCount));
            float xStep = canvasWidth / targetCount;

            for (int i = 0; i < targetCount; i++)
            {
                if (_spectrumBuffer[i] <= threshold) continue;

                float probability = Math.Min(_spectrumBuffer[i] / threshold, 1f) * _spawnProbability;
                if (_random.NextDouble() >= probability) continue;

                float factor = Math.Min(_spectrumBuffer[i] / threshold, 2f);
                _particleBuffer.Add(new Particle
                {
                    X = i * xStep + (float)_random.NextDouble() * barWidth,
                    Y = spawnY,
                    Velocity = GetRandomVelocity() * factor,
                    Size = size * factor,
                    Life = _particleLife,
                    Alpha = 1f,
                    IsActive = true
                });
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
            if (_particleBuffer == null) return;

            float size = _isOverlayActive ? _particleSizeOverlay : _particleSizeNormal;
            var particles = _particleBuffer.GetActiveParticles();

            foreach (ref var particle in particles)
                particle.Size = size;
        }

        private void RenderParticles(SKCanvas canvas, SKPaint paint, float upperBound, float lowerBound)
        {
            if (_particleBuffer == null) return;

            var activeParticles = _particleBuffer.GetActiveParticles();
            if (activeParticles.Length == 0) return;

            using var pointPaint = paint.Clone();
            pointPaint.Style = SKPaintStyle.Fill;
            pointPaint.StrokeCap = SKStrokeCap.Round;
            pointPaint.StrokeWidth = 0; // Ensure no stroke

            foreach (ref readonly var particle in activeParticles)
            {
                if (particle.Y < upperBound || particle.Y > lowerBound) continue;
                pointPaint.Color = paint.Color.WithAlpha((byte)(particle.Alpha * 255));
                canvas.DrawCircle(particle.X, particle.Y, particle.Size / 2, pointPaint);
            }
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
            !_isDisposed && _isInitialized && canvas != null &&
            spectrum != null && paint != null && drawPerformanceInfo != null &&
            spectrum.Length >= 2 && info.Width > 0 && info.Height > 0;
        #endregion

        #region Nested Classes
        private class CircularParticleBuffer
        {
            private Particle[] _buffer;
            private int _head, _tail, _count;
            private readonly float _particleLife;
            private readonly float _particleLifeDecay;
            private readonly float _velocityMultiplier;

            public CircularParticleBuffer(int capacity, float particleLife,
                float particleLifeDecay, float velocityMultiplier)
            {
                _buffer = new Particle[capacity];
                _head = 0; // Initialize _head
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
                return _buffer.AsSpan(0, _count);
            }

            public void Update(float upperBound, float alphaDecayExponent)
            {
                int activeCount = 0;
                int consecutiveInactive = 0;
                const int maxConsecutiveInactive = 10; // Define a threshold

                for (int i = 0; i < _count; i++)
                {
                    int index = (_head + i) % _buffer.Length;
                    ref var particle = ref _buffer[index];

                    // Skip inactive particles
                    if (!particle.IsActive)
                    {
                        consecutiveInactive++;
                        if (consecutiveInactive >= maxConsecutiveInactive)
                            break;
                        continue;
                    }

                    // Update particle properties
                    particle.Y -= particle.Velocity * _velocityMultiplier;
                    particle.Life -= _particleLifeDecay;
                    particle.Alpha = MathF.Pow(particle.Life / _particleLife, alphaDecayExponent);

                    // Check if particle is still active
                    if (particle.Alpha <= 0.01f || particle.Y < upperBound)
                    {
                        particle.IsActive = false;
                        consecutiveInactive++;
                        if (consecutiveInactive >= maxConsecutiveInactive)
                            break;
                    }
                    else
                    {
                        _buffer[activeCount++] = particle;
                        consecutiveInactive = 0;
                    }
                }

                _count = activeCount;
            }
        }
        #endregion
    }
}