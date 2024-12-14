#nullable enable

namespace SpectrumNet
{
    public sealed class TextParticlesRenderer : ISpectrumRenderer, IDisposable
    {
        #region Fields
        [StructLayout(LayoutKind.Sequential, Pack = 16)]
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
        private readonly string _characters = "01";

        private readonly float
            _velocityRange = Settings.Instance.ParticleVelocityMax - Settings.Instance.ParticleVelocityMin,
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

        private static readonly Lazy<TextParticlesRenderer> _lazyInstance =
            new(() => new TextParticlesRenderer(), LazyThreadSafetyMode.ExecutionAndPublication);
        public static TextParticlesRenderer GetInstance() => _lazyInstance.Value;
        #endregion

        #region Public Methods
        public void Initialize()
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(TextParticlesRenderer));
            if (_isInitialized) return;

            _particleBuffer = new CircularParticleBuffer(
                (int)Settings.Instance.MaxParticles,
                _particleLife,
                _particleLifeDecay,
                _velocityMultiplier);
            _isInitialized = true;
        }

        public void Configure(bool isOverlayActive)
        {
            if (_isOverlayActive == isOverlayActive) return;
            _isOverlayActive = isOverlayActive;
            UpdateParticleSizes();
        }

        public void Render(SKCanvas canvas, float[] spectrum, SKImageInfo info, float barWidth,
            float barSpacing, int barCount, SKPaint paint, Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
        {
            if (!ValidateRenderParameters(canvas, spectrum, info, paint, drawPerformanceInfo)) return;

            _renderCache.Width = info.Width;
            _renderCache.Height = info.Height;
            _renderCache.StepSize = barCount > 0 ? info.Width / barCount : 0f;

            UpdateRenderCacheBounds(info.Height);
            _particleBuffer?.Update(_renderCache.UpperBound, _renderCache.LowerBound, _alphaDecayExponent);
            _spectrumBuffer ??= ArrayPool<float>.Shared.Rent(2048);

            SpawnNewParticles(
                spectrum.AsSpan(0, Math.Min(spectrum.Length, 2048)),
                _renderCache.LowerBound,
                _renderCache.Width,
                barWidth);

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
            _alphaCurve ??= ArrayPool<float>.Shared.Rent(101);
            float step = 1f / (_alphaCurve.Length - 1);
            for (int i = 0; i < _alphaCurve.Length; i++)
                _alphaCurve[i] = (float)Math.Pow(i * step, _alphaDecayExponent);
        }

        private void InitializeVelocityLookup(float minVelocity)
        {
            _velocityLookup ??= ArrayPool<float>.Shared.Rent(VelocityLookupSize);
            for (int i = 0; i < VelocityLookupSize; i++)
                _velocityLookup[i] = minVelocity + _velocityRange * i / VelocityLookupSize;
        }

        private void UpdateRenderCacheBounds(float height)
        {
            float overlayHeight = height * Settings.Instance.OverlayHeightMultiplier;
            _renderCache.OverlayHeight = _isOverlayActive ? overlayHeight : 0f;
            _renderCache.UpperBound = _isOverlayActive ? height - overlayHeight : 0f;
            _renderCache.LowerBound = height;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private void SpawnNewParticles(ReadOnlySpan<float> spectrum, float spawnY, float canvasWidth, float barWidth)
        {
            if (_particleBuffer == null) return;

            Span<float> randomValues = stackalloc float[spectrum.Length];
            for (int i = 0; i < randomValues.Length; i++)
                randomValues[i] = (float)_random.NextDouble();

            float threshold = _isOverlayActive ? _spawnThresholdOverlay : _spawnThresholdNormal;
            float baseSize = (_isOverlayActive ? _particleSizeOverlay : _particleSizeNormal) * 2;
            int targetCount = Math.Min(spectrum.Length / 2, 2048);

            ScaleSpectrum(spectrum, SpectrumBuffer.AsSpan(0, targetCount));
            float xStep = _renderCache.StepSize;

            for (int i = 0; i < targetCount; i++)
            {
                float spectrumValue = SpectrumBuffer[i];
                if (spectrumValue <= threshold || _random.NextDouble() >= MathF.Min(spectrumValue / threshold, 3f) * _spawnProbability)
                    continue;

                _particleBuffer.Add(new Particle
                {
                    X = i * xStep + (float)_random.NextDouble() * barWidth,
                    Y = spawnY,
                    Z = Settings.Instance.MinZDepth + (float)_random.NextDouble() * _zRange,
                    Velocity = GetRandomVelocity() * MathF.Min(spectrumValue / threshold, 3f),
                    Size = baseSize * MathF.Min(spectrumValue / threshold, 3f),
                    Life = _particleLife,
                    Alpha = 1f,
                    IsActive = true,
                    Character = _characters[_random.Next(_characters.Length)]
                });
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float GetRandomVelocity() =>
            _velocityLookup?[_random.Next(VelocityLookupSize)] * _velocityMultiplier
            ?? throw new InvalidOperationException("Velocity lookup is not initialized.");

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private static void ScaleSpectrum(ReadOnlySpan<float> source, Span<float> dest)
        {
            if (source.IsEmpty || dest.IsEmpty) return;

            ref var sourceRef = ref MemoryMarshal.GetReference(source);
            ref var destRef = ref MemoryMarshal.GetReference(dest);
            float scale = (source.Length - 1) / (float)(dest.Length - 1);

            for (int i = 0; i < dest.Length; i++)
            {
                float index = i * scale;
                int baseIndex = (int)index;
                float fraction = index - baseIndex;

                Unsafe.Add(ref destRef, i) = baseIndex + 1 < source.Length
                    ? Unsafe.Add(ref sourceRef, baseIndex) * (1 - fraction) +
                      Unsafe.Add(ref sourceRef, baseIndex + 1) * fraction
                    : Unsafe.Add(ref sourceRef, baseIndex);
            }
        }

        private void UpdateParticleSizes()
        {
            if (_particleBuffer == null) return;
            float baseSize = _isOverlayActive ? _particleSizeOverlay : _particleSizeNormal;
            float oldBaseSize = _isOverlayActive ? _particleSizeNormal : _particleSizeOverlay;

            foreach (ref var particle in _particleBuffer.GetActiveParticles())
                particle.Size = baseSize * (particle.Size / oldBaseSize);
        }

        private void RenderParticles(SKCanvas canvas, SKPaint paint, float upperBound, float lowerBound)
        {
            if (_particleBuffer == null) return;
            var activeParticles = _particleBuffer.GetActiveParticles();
            if (activeParticles.IsEmpty) return;

            paint.Style = SKPaintStyle.Fill;
            const float focalLength = 1000f;
            float centerX = _renderCache.Width / 2f, centerY = _renderCache.Height / 2f;

            ref var activeRef = ref MemoryMarshal.GetReference(activeParticles);

            for (int i = 0; i < activeParticles.Length; i++)
            {
                ref readonly var p = ref Unsafe.Add(ref activeRef, i);
                if (!p.IsActive || p.Y < upperBound || p.Y > lowerBound) continue;

                float depth = focalLength + p.Z;
                float scale = focalLength / depth;
                float screenX = centerX + (p.X - centerX) * scale;
                float screenY = centerY + (p.Y - centerY) * scale;
                float textSize = p.Size * scale;

                paint.Color = paint.Color.WithAlpha((byte)(p.Alpha * (depth / (focalLength + p.Z)) * 255));
                canvas.DrawText(p.Character.ToString(), screenX - textSize / 2, screenY + textSize / 2,
                    new SKFont { Size = textSize }, paint);
            }
        }

        private bool ValidateRenderParameters(SKCanvas? canvas, float[]? spectrum, SKImageInfo info, SKPaint? paint,
            Action<SKCanvas, SKImageInfo>? drawPerformanceInfo) =>
            !_isDisposed && _isInitialized && canvas != null && spectrum?.Length >= 2 &&
            paint != null && drawPerformanceInfo != null && info.Width > 0 && info.Height > 0;

        private void Dispose(bool disposing)
        {
            if (_isDisposed) return;
            if (disposing)
            {
                if (_spectrumBuffer != null) ArrayPool<float>.Shared.Return(_spectrumBuffer);
                if (_velocityLookup != null) ArrayPool<float>.Shared.Return(_velocityLookup);
                if (_alphaCurve != null) ArrayPool<float>.Shared.Return(_alphaCurve);
                _spectrumBuffer = _velocityLookup = _alphaCurve = null;
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
                (_particleLife, _particleLifeDecay, _velocityMultiplier) = (particleLife, particleLifeDecay, velocityMultiplier);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Add(Particle particle)
            {
                if (_count >= _capacity) return;
                _buffer[_tail] = particle;
                _tail = (_tail + 1) % _capacity;
                _count++;
            }

            public Span<Particle> GetActiveParticles() => _buffer.AsSpan(_head, _count);

            public void Update(float upperBound, float lowerBound, float alphaDecayExponent)
            {
                int writeIndex = 0;
                for (int i = 0, readIndex = _head; i < _count; i++, readIndex = (readIndex + 1) % _capacity)
                {
                    ref var particle = ref _buffer[readIndex];
                    if (!particle.IsActive) continue;

                    particle.Y -= particle.Velocity * _velocityMultiplier;
                    particle.Life -= _particleLifeDecay;
                    particle.Alpha = MathF.Pow(particle.Life / _particleLife, alphaDecayExponent);

                    if (particle.Y >= upperBound && particle.Y <= lowerBound && particle.Alpha >= 0.01f)
                        _buffer[writeIndex++] = particle;
                    else
                        particle.IsActive = false;
                }

                _count = writeIndex;
                _head = 0;
                _tail = writeIndex;
            }
        }
        #endregion
    }
}