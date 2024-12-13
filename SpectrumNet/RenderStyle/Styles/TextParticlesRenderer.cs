#nullable enable

namespace SpectrumNet
{
    public sealed class TextParticlesRenderer : ISpectrumRenderer, IDisposable
    {
        #region Fields

        private struct Particle
        {
            public float X, Y, Z, Velocity, Size, Life, Alpha;
            public bool IsActive;
            public char Character;
        }

        private static readonly object _lock = new();
        private readonly Random _random = new();
        private const int VelocityLookupSize = 1024;
        private float[] SpectrumBuffer => _spectrumBuffer ??= ArrayPool<float>.Shared.Rent(2048);
        private CircularParticleBuffer? _particleBuffer;
        private RenderCache _renderCache = new();
        private float[]? _spectrumBuffer, _velocityLookup, _alphaCurve;
        private readonly string _characters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        private readonly float _velocityRange = Settings.Instance.ParticleVelocityMax - Settings.Instance.ParticleVelocityMin,
                              _particleLife = Settings.Instance.ParticleLife,
                              _particleLifeDecay = Settings.Instance.ParticleLifeDecay,
                              _alphaDecayExponent = Settings.Instance.AlphaDecayExponent,
                              _spawnThresholdOverlay = Settings.Instance.SpawnThresholdOverlay,
                              _spawnThresholdNormal = Settings.Instance.SpawnThresholdNormal,
                              _spawnProbability = Settings.Instance.SpawnProbability,
                              _particleSizeOverlay = Settings.Instance.ParticleSizeOverlay,
                              _particleSizeNormal = Settings.Instance.ParticleSizeNormal,
                              _velocityMultiplier = Settings.Instance.VelocityMultiplier,
                              _zRange = Settings.Instance.MaxZDepth - Settings.Instance.MinZDepth;
        private bool _isOverlayActive, _isInitialized, _isDisposed;

        #endregion

        #region Constructor and Singleton

        private TextParticlesRenderer()
        {
            PrecomputeAlphaCurve();
            InitializeVelocityLookup(Settings.Instance.ParticleVelocityMin);
        }

        private static readonly Lazy<TextParticlesRenderer> _lazyInstance = new(() =>
        new TextParticlesRenderer(), LazyThreadSafetyMode.ExecutionAndPublication);
        public static TextParticlesRenderer GetInstance() => _lazyInstance.Value;

        #endregion

        #region Public Methods

        public void Initialize()
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(TextParticlesRenderer));
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

        public void Render(SKCanvas canvas, float[] spectrum, SKImageInfo info, float barWidth, float barSpacing, int barCount, SKPaint paint, Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
        {
            if (!ValidateRenderParameters(canvas, spectrum, info, paint, drawPerformanceInfo)) return;
            _renderCache.Width = info.Width; _renderCache.Height = info.Height;
            UpdateRenderCacheBounds(info.Height);
            _renderCache.StepSize = barCount > 0 ? info.Width / barCount : 0f;

            _particleBuffer?.Update(_renderCache.UpperBound, _renderCache.LowerBound, _alphaDecayExponent);
            if (_spectrumBuffer == null) _spectrumBuffer = ArrayPool<float>.Shared.Rent(2048);

            SpawnNewParticles(spectrum.AsSpan(0, Math.Min(spectrum.Length, 2048)), _renderCache.LowerBound, _renderCache.Width, barWidth);
            RenderParticles(canvas, paint, _renderCache.UpperBound, _renderCache.LowerBound);

            drawPerformanceInfo(canvas, info);
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
            if (_alphaCurve == null) _alphaCurve = ArrayPool<float>.Shared.Rent(101);
            float step = 1f / (_alphaCurve.Length - 1);
            for (int i = 0; i < _alphaCurve.Length; i++) _alphaCurve[i] = (float)Math.Pow(i * step, _alphaDecayExponent);
        }

        private void InitializeVelocityLookup(float minVelocity)
        {
            if (_velocityLookup == null) _velocityLookup = ArrayPool<float>.Shared.Rent(VelocityLookupSize);
            for (int i = 0; i < VelocityLookupSize; i++) _velocityLookup[i] = minVelocity + _velocityRange * i / VelocityLookupSize;
        }

        private void UpdateRenderCacheBounds(float height)
        {
            float overlayHeight = height * Settings.Instance.OverlayHeightMultiplier;
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

        private void SpawnNewParticles(ReadOnlySpan<float> spectrum, float spawnY, float canvasWidth, float barWidth)
        {
            if (_particleBuffer == null) return;
            float threshold = _isOverlayActive ? _spawnThresholdOverlay : _spawnThresholdNormal;
            float baseSize = (_isOverlayActive ? _particleSizeOverlay : _particleSizeNormal) * 2; // Increase size
            int targetCount = Math.Min(spectrum.Length / 2, 2048);
            ScaleSpectrum(spectrum, SpectrumBuffer.AsSpan(0, targetCount));
            float xStep = _renderCache.StepSize;

            for (int i = 0; i < targetCount; i++)
            {
                float spectrumValue = SpectrumBuffer[i];
                if (spectrumValue <= threshold) continue;
                float densityFactor = MathF.Min(spectrumValue / threshold, 3f);
                if (_random.NextDouble() >= densityFactor * _spawnProbability) continue;

                char character = _characters[_random.Next(_characters.Length)];
                _particleBuffer.Add(new Particle
                {
                    X = i * xStep + (float)_random.NextDouble() * barWidth,
                    Y = spawnY,
                    Z = Settings.Instance.MinZDepth + (float)_random.NextDouble() * _zRange,
                    Velocity = GetRandomVelocity() * densityFactor,
                    Size = baseSize * densityFactor,
                    Life = _particleLife,
                    Alpha = 1f,
                    IsActive = true,
                    Character = character
                });
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float GetRandomVelocity()
        {
            if (_velocityLookup == null) throw new InvalidOperationException("Velocity lookup is not initialized.");
            return _velocityLookup[_random.Next(VelocityLookupSize)] * _velocityMultiplier;
        }

        private static void ScaleSpectrum(ReadOnlySpan<float> source, Span<float> dest)
        {
            int srcLen = source.Length / 2, destLen = dest.Length;
            if (srcLen == 0 || destLen == 0) return;
            float scale = srcLen / (float)destLen;
            for (int i = 0; i < destLen; i++) dest[i] = source[(int)(i * scale)];
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

            float focalLength = 1000f; // Adjust for projection depth
            float canvasWidth = _renderCache.Width, canvasHeight = _renderCache.Height, centerX = canvasWidth / 2f, centerY = canvasHeight / 2f;

            foreach (ref readonly var particle in activeParticles)
            {
                if (!particle.IsActive || particle.Y < upperBound || particle.Y > lowerBound) continue;
                float depth = focalLength + particle.Z, scale = focalLength / depth;
                float screenX = centerX + (particle.X - centerX) * scale, screenY = centerY + (particle.Y - centerY) * scale;
                float textSize = particle.Size * scale;
                float alpha = particle.Alpha * (depth / (focalLength + particle.Z));

                paint.Color = paint.Color.WithAlpha((byte)(alpha * 255));
                canvas.DrawText(particle.Character.ToString(), screenX - textSize / 2, screenY + textSize / 2, new SKFont { Size = textSize }, paint);
            }
        }

        private bool ValidateRenderParameters(SKCanvas? canvas, float[]? spectrum, SKImageInfo info, SKPaint? paint, Action<SKCanvas, SKImageInfo>? drawPerformanceInfo)
            => !_isDisposed && _isInitialized && canvas != null && spectrum != null && spectrum.Length >= 2
               && paint != null && drawPerformanceInfo != null && info.Width > 0 && info.Height > 0;

        private void Dispose(bool disposing)
        {
            if (_isDisposed) return;
            if (disposing)
            {
                if (_spectrumBuffer != null) ArrayPool<float>.Shared.Return(_spectrumBuffer);
                if (_velocityLookup != null) ArrayPool<float>.Shared.Return(_velocityLookup);
                if (_alphaCurve != null) ArrayPool<float>.Shared.Return(_alphaCurve);
                _spectrumBuffer = null;
                _velocityLookup = null;
                _alphaCurve = null;
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
            private Particle[] _buffer;
            private int _head, _tail, _count, _capacity;
            private readonly float _particleLife, _particleLifeDecay, _velocityMultiplier;

            public CircularParticleBuffer(int capacity, float particleLife, float particleLifeDecay, float velocityMultiplier)
            {
                _capacity = capacity;
                _buffer = new Particle[capacity];
                _particleLife = particleLife;
                _particleLifeDecay = particleLifeDecay;
                _velocityMultiplier = velocityMultiplier;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Add(Particle particle)
            {
                if (_count < _capacity)
                {
                    _buffer[_tail] = particle;
                    _tail = (_tail + 1) % _capacity;
                    _count++;
                }
            }

            public Span<Particle> GetActiveParticles() => _buffer.AsSpan(_head, _count);

            public void Update(float upperBound, float lowerBound, float alphaDecayExponent)
            {
                int writeIndex = 0, readIndex = _head;
                for (int i = 0; i < _count; i++)
                {
                    ref var particle = ref _buffer[readIndex];
                    if (particle.IsActive)
                    {
                        particle.Y -= particle.Velocity * _velocityMultiplier;
                        particle.Life -= _particleLifeDecay;
                        particle.Alpha = (float)Math.Pow(particle.Life / _particleLife, alphaDecayExponent);
                        if (particle.Y < upperBound || particle.Y > lowerBound || particle.Alpha < 0.01f)
                            particle.IsActive = false;
                        else
                            _buffer[writeIndex++] = particle;
                    }
                    readIndex = (readIndex + 1) % _capacity;
                }
                _count = writeIndex;
                _head = 0;
                _tail = writeIndex;
            }
        }

        #endregion
    }
}