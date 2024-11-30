#nullable enable

namespace SpectrumNet
{
    public class ParticlesRenderer : ISpectrumRenderer, IDisposable
    {
        private int _frameCount = 0;
        private const int LogInterval = 300;

        private Particle[] _particles = Array.Empty<Particle>();
        private int _particleCount;
        private readonly Stack<Particle> _pool = new();
        private readonly Random _random = new();
        private bool _isOverlayActive;
        private bool _isInitialized = false;
        private float _velocityRange;
        private float _particleLife;
        private float _particleLifeDecay;
        private float _alphaDecayExponent;
        private float _spawnThresholdOverlay;
        private float _spawnThresholdNormal;
        private float _spawnProbability;
        private float _particleSizeOverlay;
        private float _particleSizeNormal;
        private float _velocityMultiplier;

        private static ParticlesRenderer? _instance;
        private ParticlesRenderer() { }

        public static ParticlesRenderer GetInstance() => _instance ??= new ParticlesRenderer();

        public void Initialize()
        {
            if (_isInitialized) return;

            int maxParticles = (int)Settings.Instance.MaxParticles;
            _particles = new Particle[maxParticles];
            _particleCount = 0;
            InitializeParticlePool();
            CacheSettings();
            Log.Debug("ParticlesRenderer initialized with {MaxParticles} particles", maxParticles);
            _isInitialized = true;
        }

        private void InitializeParticlePool()
        {
            for (int i = 0; i < Settings.Instance.MaxParticles; i++)
            {
                _pool.Push(new Particle());
            }
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

            // Обновляем размеры существующих частиц
            UpdateParticleSizes();
        }

        public void Configure(bool isOverlayActive)
        {
            if (_isOverlayActive != isOverlayActive)
            {
                _isOverlayActive = isOverlayActive;
                UpdateParticleSizes();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Particle GetOrCreateParticle()
        {
            return _pool.Count > 0 ? _pool.Pop() : new Particle();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ReturnParticleToPool(Particle particle)
        {
            if (_pool.Count < _particles.Length / 2) // Ограничиваем размер пула половиной максимального количества частиц
            {
                _pool.Push(particle);
            }
        }

        private bool AreRenderParamsValid(SKCanvas? canvas, ReadOnlySpan<float> spectrum, SKImageInfo info, SKPaint? paint)
        {
            if (canvas == null || spectrum.IsEmpty || paint == null || info.Width <= 0 || info.Height <= 0)
            {
                Log.Warning("Invalid render parameters");
                return false;
            }
            return true;
        }

        private (float overlayHeight, float baseY) GetOverlayDimensions(float totalHeight)
        {
            float overlayHeight = _isOverlayActive ? totalHeight * Settings.Instance.OverlayHeightMultiplier : totalHeight;
            float baseY = _isOverlayActive ? totalHeight * Settings.Instance.OverlayOffsetMultiplier : totalHeight;
            return (overlayHeight, baseY);
        }

        public void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info,
                           float barWidth, float barSpacing, int barCount, SKPaint? paint)
        {
            if (!_isInitialized || _particles == null)
            {
                Log.Warning("ParticlesRenderer is not initialized.");
                return;
            }

            if (!AreRenderParamsValid(canvas, spectrum.AsSpan(), info, paint)) return;

            _frameCount++;
            if (_frameCount % LogInterval == 0)
            {
                Log.Information("Particle count: {Count}/{Max}, Pool size: {PoolSize}",
                    _particleCount, _particles.Length, _pool.Count);
            }

            var (overlayHeight, baseY) = GetOverlayDimensions(info.Height);
            float upperBound = _isOverlayActive ? baseY - overlayHeight : 0;
            float lowerBound = _isOverlayActive ? baseY : info.Height;
            float spawnY = lowerBound;

            UpdateParticles(upperBound);
            SpawnNewParticles(spectrum.AsSpan(), spawnY, info.Width, barWidth);

            if (canvas != null && paint != null)
            {
                RenderParticles(canvas, paint, upperBound, lowerBound);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Lerp(float start, float end, float amount)
        {
            return start + (end - start) * amount;
        }

        private void UpdateParticles(float upperBound)
        {
            if (_particles == null) return;

            int newCount = 0;
            for (int i = 0; i < _particleCount; i++)
            {
                ref var particle = ref _particles[i];
                UpdateParticleLifeAndPosition(ref particle);

                if (particle.Y >= upperBound && particle.Life > 0)
                {
                    if (i != newCount)
                    {
                        _particles[newCount] = particle;
                    }
                    newCount++;
                }
                else
                {
                    ReturnParticleToPool(particle);
                }
            }
            _particleCount = newCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateParticleLifeAndPosition(ref Particle particle)
        {
            float maxVelocity = 2.0f;
            particle.Velocity = Math.Min(particle.Velocity, maxVelocity);

            particle.Y -= particle.Velocity * _velocityMultiplier;
            particle.Life -= _particleLifeDecay;
            float lifeRatio = particle.Life / _particleLife;
            particle.Alpha = MathF.Pow(lifeRatio, _alphaDecayExponent);

            float targetSize = _isOverlayActive ? _particleSizeOverlay : _particleSizeNormal;
            particle.Size = Lerp(particle.Size, targetSize, 0.1f);
        }

        private void SpawnNewParticles(ReadOnlySpan<float> spectrum, float spawnY, float canvasWidth, float barWidth)
        {
            if (_particles == null || _particleCount >= _particles.Length)
            {
                return; // Выход, если достигнут максимум частиц или массив не инициализирован
            }

            float spawnThreshold = _isOverlayActive ? _spawnThresholdOverlay : _spawnThresholdNormal;
            int maxParticles = _particles.Length;
            int availableSlots = maxParticles - _particleCount;
            int particleCount = Math.Min(availableSlots, spectrum.Length / 2);
            float xStep = canvasWidth / particleCount;

            for (int i = 0; i < particleCount; i++)
            {
                if (_particleCount >= maxParticles)
                {
                    break; // Выход из цикла, если достигнут максимум частиц
                }

                if (ShouldSpawnParticle(spectrum[i], spawnThreshold))
                {
                    SpawnParticle(i, xStep, spawnY);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ShouldSpawnParticle(float spectrumValue, float spawnThreshold)
        {
            return spectrumValue > spawnThreshold && _random.NextDouble() < _spawnProbability;
        }

        private void SpawnParticle(int index, float xStep, float spawnY)
        {
            if (_particleCount >= _particles.Length)
            {
                Log.Warning("Cannot spawn more particles. Maximum limit reached.");
                return;
            }

            var particle = GetOrCreateParticle();
            particle.X = index * xStep;
            particle.Y = spawnY;
            particle.Velocity = GetParticleVelocity();
            particle.Size = _isOverlayActive ? _particleSizeOverlay : _particleSizeNormal;
            particle.Life = _particleLife;
            particle.Alpha = 1.0f;
            _particles[_particleCount] = particle;
            _particleCount++;
        }

        private void UpdateParticleSizes()
        {
            float newSize = _isOverlayActive ? _particleSizeOverlay : _particleSizeNormal;
            for (int i = 0; i < _particleCount; i++)
            {
                _particles[i].Size = newSize;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float GetParticleVelocity()
        {
            return Settings.Instance.ParticleVelocityMin + (float)_random.NextDouble() * _velocityRange * _velocityMultiplier;
        }

        private void RenderParticles(SKCanvas canvas, SKPaint basePaint, float upperBound, float lowerBound)
        {
            if (_particles == null) return;

            using var particlePaint = basePaint.Clone();

            for (int i = 0; i < _particleCount; i++)
            {
                ref var particle = ref _particles[i];
                if (particle.Y >= upperBound && particle.Y <= lowerBound)
                {
                    particlePaint.Color = particlePaint.Color.WithAlpha((byte)(particle.Alpha * 255));
                    canvas.DrawCircle(particle.X, particle.Y, particle.Size, particlePaint);
                }
            }
        }
        public void Dispose()
        {
            _particles = Array.Empty<Particle>();
            _pool.Clear();
            _particleCount = 0;
            _isInitialized = false;
        }
    }

    public struct Particle
    {
        public float X;
        public float Y;
        public float Velocity;
        public float Size;
        public float Alpha;
        public float Life;
    }
}