#nullable enable

using Vector = System.Numerics.Vector;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace SpectrumNet
{
    public class ParticlesRenderer : ISpectrumRenderer, IDisposable
    {
        private struct ParticleState
        {
            public Particle[] Particles;
            public int ParticleCount;

            public ParticleState(Particle[] particles, int count)
            {
                Particles = particles;
                ParticleCount = count;
            }
        }

        #region Constants
        private const int LOG_INTERVAL = 300;
        private const float MAX_VELOCITY = 2.0f;
        private const float LERP_AMOUNT = 0.1f;
        private const float INITIAL_ALPHA = 1.0f;
        #endregion

        #region Fields
        private static ParticlesRenderer? _instance;
        private Particle[] _particles = Array.Empty<Particle>();
        private int _particleCount, _frameCount;
        private readonly Random _random = new();
        private bool _isOverlayActive, _isInitialized;
        private static readonly bool IsSimdSupported = Sse.IsSupported && Sse2.IsSupported,
                                     IsAvxSupported = Avx.IsSupported,
                                     IsAvx2Supported = Avx2.IsSupported;
        private const int LogInterval = 300;
        private float _velocityRange, _particleLife, _particleLifeDecay, _alphaDecayExponent,
                      _spawnThresholdOverlay, _spawnThresholdNormal, _spawnProbability,
                      _particleSizeOverlay, _particleSizeNormal, _velocityMultiplier;
        private readonly object _stateLock = new();
        private Task? _updateTask;
        private ParticleState? _currentState;
        private readonly CancellationTokenSource _cancellationSource = new();
        #endregion

        #region Singleton
        public static ParticlesRenderer GetInstance() => _instance ??= new ParticlesRenderer();
        #endregion

        #region Constructor
        private ParticlesRenderer() { }
        #endregion

        #region Initialization
        public void Initialize()
        {
            if (_isInitialized) return;

            int maxParticles = (int)Settings.Instance.MaxParticles;
            _particles = new Particle[maxParticles];
            _particleCount = 0;
            CacheSettings();
            Log.Debug("ParticlesRenderer initialized with {MaxParticles} particles", maxParticles);
            _isInitialized = true;
        }

        private void CacheSettings()
        {
            var settings = Settings.Instance;
            _velocityRange = settings.ParticleVelocityMax - settings.ParticleVelocityMin;
            _particleLife = settings.ParticleLife;
            _particleLifeDecay = settings.ParticleLifeDecay;
            _alphaDecayExponent = settings.AlphaDecayExponent;
            _spawnThresholdOverlay = settings.SpawnThresholdOverlay;
            _spawnThresholdNormal = settings.SpawnThresholdNormal;
            _spawnProbability = settings.SpawnProbability;
            _particleSizeOverlay = settings.ParticleSizeOverlay;
            _particleSizeNormal = settings.ParticleSizeNormal;
            _velocityMultiplier = settings.VelocityMultiplier;
            UpdateParticleSizes();
        }
        #endregion

        #region Configuration
        public void Configure(bool isOverlayActive)
        {
            if (_isOverlayActive != isOverlayActive)
            {
                _isOverlayActive = isOverlayActive;
                UpdateParticleSizes();
            }
        }
        #endregion

        #region Rendering
        public void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info,
                          float barWidth, float barSpacing, int barCount, SKPaint? paint,
                          Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
        {
            if (!_isInitialized || _particles == null || !AreRenderParamsValid(canvas, spectrum.AsSpan(), info))
            {
                Log.Warning("ParticlesRenderer is not initialized or render parameters are invalid.");
                return;
            }

            var (overlayHeight, baseY) = GetOverlayDimensions(info.Height);
            float upperBound = _isOverlayActive ? baseY - overlayHeight : 0;
            float lowerBound = _isOverlayActive ? baseY : info.Height;
            float spawnY = lowerBound;

            StartParticleUpdate(spectrum, upperBound, spawnY, info.Width);

            ParticleState? currentState;
            lock (_stateLock)
            {
                currentState = _currentState;
            }

            if (currentState != null)
            {
                RenderParticles(canvas!, paint!, upperBound, lowerBound, currentState.Value);
            }

            drawPerformanceInfo(canvas!, info);
        }

        private bool AreRenderParamsValid(SKCanvas? canvas, ReadOnlySpan<float> spectrum, SKImageInfo info) =>
            canvas != null && !spectrum.IsEmpty && info.Width > 0 && info.Height > 0;

        private (float overlayHeight, float baseY) GetOverlayDimensions(float totalHeight) =>
            (_isOverlayActive
                ? (totalHeight * Settings.Instance.OverlayHeightMultiplier,
                   totalHeight * Settings.Instance.OverlayOffsetMultiplier)
                : (totalHeight, totalHeight));
        #endregion

        #region Particle Management
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Lerp(float start, float end, float amount) => start + (end - start) * amount;

        private unsafe void UpdateParticlesSimd(Span<Particle> particles, ref int particleCount, float upperBound)
        {
            int newCount = 0, vectorSize = Vector<float>.Count;
            var velocityMultiplierVector = new Vector<float>(_velocityMultiplier);
            var lifeDecayVector = new Vector<float>(_particleLifeDecay);
            var alphaDecayVector = new Vector<float>(_alphaDecayExponent);
            var maxVelocityVector = new Vector<float>(MAX_VELOCITY);
            var targetSizeVector = new Vector<float>(_isOverlayActive ? _particleSizeOverlay : _particleSizeNormal);
            var particleLifeVector = new Vector<float>(_particleLife);

            fixed (Particle* particlesPtr = particles)
            {
                for (int i = 0; i < particleCount; i += vectorSize)
                {
                    int remainingElements = Math.Min(vectorSize, particleCount - i);
                    var velocityVector = Vector.Min(LoadVector(particlesPtr, i, remainingElements, p => p.Velocity), maxVelocityVector);
                    var yVector = LoadVector(particlesPtr, i, remainingElements, p => p.Y) - velocityVector * velocityMultiplierVector;
                    var lifeVector = LoadVector(particlesPtr, i, remainingElements, p => p.Life) - lifeDecayVector;
                    var alphaVector = PowSimd(lifeVector / particleLifeVector, alphaDecayVector);
                    var sizeVector = LerpSimd(LoadVector(particlesPtr, i, remainingElements, p => p.Size), targetSizeVector, new Vector<float>(LERP_AMOUNT));

                    for (int j = 0; j < remainingElements; j++)
                        if (yVector[j] >= upperBound && lifeVector[j] > 0)
                            particles[newCount++] = new Particle { X = particlesPtr[i + j].X, Y = yVector[j], Velocity = velocityVector[j], Size = sizeVector[j], Life = lifeVector[j], Alpha = alphaVector[j] };
                }
            }
            particleCount = newCount;
        }

        private unsafe Vector<float> LoadVector(Particle* particles, int startIndex, int count, Func<Particle, float> selector)
        {
            Span<float> tempArray = stackalloc float[Vector<float>.Count];
            for (int j = 0; j < count; j++) tempArray[j] = selector(particles[startIndex + j]);
            return new Vector<float>(tempArray);
        }

        private static Vector<float> PowSimd(Vector<float> baseVector, Vector<float> exponentVector)
        {
            Span<float> tempArray = stackalloc float[Vector<float>.Count];
            for (int i = 0; i < Vector<float>.Count; i++) tempArray[i] = MathF.Pow(baseVector[i], exponentVector[i]);
            return new Vector<float>(tempArray);
        }

        private Vector<float> LerpSimd(Vector<float> start, Vector<float> end, Vector<float> amount) => start + (end - start) * amount;

        private void UpdateParticlesScalar(Span<Particle> particles, ref int particleCount, float upperBound)
        {
            int newCount = 0;
            for (int i = 0; i < particleCount; i++)
            {
                ref var particle = ref particles[i];
                UpdateParticleLifeAndPosition(ref particle);
                if (particle.Y >= upperBound && particle.Life > 0) particles[newCount++] = particle;
            }
            particleCount = newCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateParticleLifeAndPosition(ref Particle particle)
        {
            particle.Velocity = Math.Min(particle.Velocity, MAX_VELOCITY);
            particle.Y -= particle.Velocity * _velocityMultiplier;
            particle.Life -= _particleLifeDecay;
            particle.Alpha = MathF.Pow(particle.Life / _particleLife, _alphaDecayExponent);
            particle.Size = Lerp(particle.Size, _isOverlayActive ? _particleSizeOverlay : _particleSizeNormal, LERP_AMOUNT);
        }

        private void SpawnNewParticles(ReadOnlySpan<float> spectrum, float spawnY, float canvasWidth, Span<Particle> particles, ref int particleCount)
        {
            float spawnThreshold = _isOverlayActive ? _spawnThresholdOverlay : _spawnThresholdNormal;
            int maxNewParticles = Math.Min(particles.Length - particleCount, spectrum.Length / 2);
            float xStep = canvasWidth / maxNewParticles;

            for (int i = 0; i < maxNewParticles && particleCount < particles.Length; i++)
                if (spectrum[i] > spawnThreshold && _random.NextDouble() < _spawnProbability)
                    particles[particleCount++] = new Particle
                    {
                        X = i * xStep,
                        Y = spawnY,
                        Velocity = GetParticleVelocity(),
                        Size = _isOverlayActive ? _particleSizeOverlay : _particleSizeNormal,
                        Life = _particleLife,
                        Alpha = INITIAL_ALPHA
                    };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float GetParticleVelocity() => Settings.Instance.ParticleVelocityMin + (float)_random.NextDouble() * _velocityRange;

        private void UpdateParticleSizes()
        {
            float newSize = _isOverlayActive ? _particleSizeOverlay : _particleSizeNormal;
            Parallel.For(0, _particleCount, i => _particles[i].Size = newSize);
        }
        #endregion

        #region Rendering Helpers
        private void StartParticleUpdate(float[]? spectrum, float upperBound, float spawnY, float canvasWidth)
        {
            if (_updateTask != null && !_updateTask.IsCompleted) return;

            _updateTask = Task.Run(() => {
                try
                {
                    Span<Particle> localParticles = _particles.AsSpan();
                    int localCount = _particleCount;

                    if (IsSimdSupported)
                        UpdateParticlesSimd(localParticles, ref localCount, upperBound);
                    else
                        UpdateParticlesScalar(localParticles, ref localCount, upperBound);

                    SpawnNewParticles(spectrum.AsSpan(), spawnY, canvasWidth, localParticles, ref localCount);

                    lock (_stateLock)
                    {
                        _currentState = new ParticleState(localParticles.ToArray(), localCount);
                        _particleCount = localCount;
                    }
                }
                catch (Exception ex) { Log.Error($"Error in particle update: {ex}"); }
            }, _cancellationSource.Token);
        }

        private void RenderParticles(SKCanvas canvas, SKPaint basePaint, float upperBound, float lowerBound, ParticleState state)
        {
            using var particlePaint = basePaint.Clone();
            var particles = state.Particles.AsSpan(0, state.ParticleCount);

            // Сортировка частиц по размеру для оптимизации рендеринга
            Array.Sort(state.Particles, 0, state.ParticleCount, Comparer<Particle>.Create((a, b) => b.Size.CompareTo(a.Size)));

            float currentSize = -1;
            for (int i = 0; i < state.ParticleCount; i++)
            {
                var particle = particles[i];
                if (particle.Y >= upperBound && particle.Y <= lowerBound)
                {
                    if (particle.Size != currentSize)
                    {
                        currentSize = particle.Size;
                        canvas.Save();
                        canvas.Scale(currentSize, currentSize);
                    }

                    particlePaint.Color = particlePaint.Color.WithAlpha((byte)(particle.Alpha * 255));
                    canvas.DrawCircle(particle.X / currentSize, particle.Y / currentSize, 1, particlePaint);
                }
            }

            if (currentSize != -1)
            {
                canvas.Restore();
            }
        }
        #endregion

        #region IDisposable Implementation
        public void Dispose()
        {
            _particles = Array.Empty<Particle>();
            _particleCount = 0;
            _isInitialized = false;
        }
        #endregion
    }

    #region Particle Struct
    [StructLayout(LayoutKind.Sequential)]
    public struct Particle
    {
        public float X;
        public float Y;
        public float Velocity;
        public float Size;
        public float Life;
        public float Alpha;
    }
    #endregion
}