#nullable enable

namespace SpectrumNet
{
    public static class TextParticleConstants
    {
        public static class Rendering
        {
            // Perspective projection parameters
            public const float FOCAL_LENGTH = 1000f;      // Distance to projection plane
            public const float BASE_TEXT_SIZE = 12f;      // Base size for text particles
        }

        public static class Particles
        {
            // Velocity and movement parameters
            public const int VELOCITY_LOOKUP_SIZE = 1024; // Size of pre-computed velocity array
            public const float GRAVITY = 9.81f;           // Downward acceleration
            public const float AIR_RESISTANCE = 0.98f;    // Velocity dampening factor
            public const float MAX_VELOCITY = 15f;        // Maximum particle velocity

            // Randomization parameters
            public const float RANDOM_DIRECTION_CHANCE = 0.05f;   // Probability of changing direction
            public const float DIRECTION_VARIANCE = 0.5f;         // Magnitude of direction change
            public const float SPAWN_VARIANCE = 5f;               // Spawn position variance
            public const float SPAWN_HALF_VARIANCE = SPAWN_VARIANCE / 2f;  // Half variance for centered distribution

            // Appearance parameters
            public const string DEFAULT_CHARACTERS = "01"; // Default character set for particles

            // Physics timestep
            public const float PHYSICS_TIMESTEP = 0.016f;  // Physics update time step (≈60fps)
        }

        public static class Boundaries
        {
            // Screen boundaries
            public const float BOUNDARY_MARGIN = 50f;     // Off-screen margin for culling
        }

        public static class Performance
        {
            // Batch rendering constants
            public const int MAX_BATCH_SIZE = 1000;       // Maximum particles per batch
            public const int PARTICLE_BUFFER_GROWTH = 128; // Buffer growth increment

            // Culling thresholds
            public const float MIN_ALPHA_THRESHOLD = 0.05f; // Minimum alpha for visibility
        }
    }

    public sealed class TextParticlesRenderer : ISpectrumRenderer, IDisposable
    {
        #region Fields
        private const string LOG_PREFIX = "[TextParticlesRenderer] ";

        private static readonly Lazy<TextParticlesRenderer> _lazyInstance =
            new(() => new TextParticlesRenderer(), LazyThreadSafetyMode.ExecutionAndPublication);

        private readonly Random _random = new();
        private CircularParticleBuffer? _particleBuffer;
        private RenderCache _renderCache = new();
        private float[]? _spectrumBuffer, _velocityLookup, _alphaCurve;
        private readonly string _characters = TextParticleConstants.Particles.DEFAULT_CHARACTERS;
        private readonly float _velocityRange, _particleLife, _particleLifeDecay, _alphaDecayExponent,
            _spawnThresholdOverlay, _spawnThresholdNormal, _spawnProbability, _particleSizeOverlay,
            _particleSizeNormal, _velocityMultiplier, _zRange;
        private bool _isOverlayActive, _isInitialized, _isDisposed;
        private SKFont? _font;
        private SKPicture? _cachedBackground;
        private readonly SemaphoreSlim _renderSemaphore = new(1, 1);
        private readonly object _particleLock = new();
        private float[] SpectrumBuffer => _spectrumBuffer ??= ArrayPool<float>.Shared.Rent(2048);

        // Quality settings
        private RenderQuality _quality = RenderQuality.Medium;
        private bool _useAntiAlias = true;
        private SKFilterQuality _filterQuality = SKFilterQuality.Medium;
        private bool _useAdvancedEffects = true;
        private bool _useHardwareAcceleration = true;
        private bool _useBatching = true;
        private int _cullingLevel = 1;
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

            InitializeFont();
            PrecomputeAlphaCurve();
            InitializeVelocityLookup(s.ParticleVelocityMin);

            ApplyQualitySettings();
        }

        private void InitializeFont()
        {
            _font = new SKFont
            {
                Size = TextParticleConstants.Rendering.BASE_TEXT_SIZE,
                Edging = SKFontEdging.SubpixelAntialias
            };
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
            SmartLogger.Log(LogLevel.Information, LOG_PREFIX, "Initialized");
        }
        #endregion

        #region Configuration
        public void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium)
        {
            if (_isOverlayActive != isOverlayActive)
            {
                _isOverlayActive = isOverlayActive;
                UpdateParticleSizes();

                // Invalidate cached background when overlay state changes
                if (_cachedBackground != null)
                {
                    _cachedBackground.Dispose();
                    _cachedBackground = null;
                }
            }

            Quality = quality;
        }

        public RenderQuality Quality
        {
            get => _quality;
            set
            {
                if (_quality != value)
                {
                    _quality = value;
                    ApplyQualitySettings();
                }
            }
        }

        private void ApplyQualitySettings()
        {
            switch (_quality)
            {
                case RenderQuality.Low:
                    _useAntiAlias = false;
                    _filterQuality = SKFilterQuality.Low;
                    _useAdvancedEffects = false;
                    _useHardwareAcceleration = true;
                    _useBatching = true;
                    _cullingLevel = 2;
                    break;

                case RenderQuality.Medium:
                    _useAntiAlias = true;
                    _filterQuality = SKFilterQuality.Medium;
                    _useAdvancedEffects = true;
                    _useHardwareAcceleration = true;
                    _useBatching = true;
                    _cullingLevel = 1;
                    break;

                case RenderQuality.High:
                    _useAntiAlias = true;
                    _filterQuality = SKFilterQuality.High;
                    _useAdvancedEffects = true;
                    _useHardwareAcceleration = true;
                    _useBatching = true;
                    _cullingLevel = 0;
                    break;
            }

            // Update font settings based on quality
            if (_font != null)
            {
                _font.Edging = _useAntiAlias ? SKFontEdging.SubpixelAntialias : SKFontEdging.Alias;
            }

            // Invalidate cached elements when quality changes
            if (_cachedBackground != null)
            {
                _cachedBackground.Dispose();
                _cachedBackground = null;
            }
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
                SmartLogger.Log(LogLevel.Error, LOG_PREFIX, "Invalid render parameters");
                return;
            }

            if (_particleBuffer == null)
            {
                SmartLogger.Log(LogLevel.Warning, LOG_PREFIX, "Particle buffer is null");
                return;
            }

            bool semaphoreAcquired = false;
            try
            {
                semaphoreAcquired = _renderSemaphore.Wait(0);

                if (semaphoreAcquired)
                {
                    UpdateRenderCache(info, barCount);
                    _particleBuffer.Update(_renderCache.UpperBound, _renderCache.LowerBound, _alphaDecayExponent);
                }

                int spectrumLength = Math.Min(spectrum!.Length, barCount);

                if (spectrumLength > 0)
                {
                    SpawnNewParticles(
                        spectrum.AsSpan(0, spectrumLength),
                        _renderCache.LowerBound,
                        _renderCache.Width,
                        barWidth
                    );
                }

                // Draw background if needed
                DrawCachedBackground(canvas!, info);

                // Render particles with batching if enabled
                if (_useBatching)
                    RenderParticlesBatched(canvas!, paint!);
                else
                    RenderParticles(canvas!, paint!);

                drawPerformanceInfo?.Invoke(canvas!, info);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LOG_PREFIX, $"Render error: {ex.Message}");
            }
            finally
            {
                if (semaphoreAcquired)
                {
                    _renderSemaphore.Release();
                }
            }
        }

        private void UpdateRenderCache(SKImageInfo info, int barCount)
        {
            _renderCache.Width = info.Width;
            _renderCache.Height = info.Height;
            _renderCache.StepSize = barCount > 0 ? info.Width / barCount : 0f;
            UpdateRenderCacheBounds(info.Height);

            // Update bar count if changed
            if (_renderCache.BarCount != barCount)
            {
                _renderCache.BarCount = barCount;
            }
        }

        private void DrawCachedBackground(SKCanvas canvas, SKImageInfo info)
        {
            // Only use cached background in Medium/High quality
            if (!_useAdvancedEffects || _quality == RenderQuality.Low)
                return;

            // Create cached background if needed
            if (_cachedBackground == null)
            {
                using var recorder = new SKPictureRecorder();
                using var recordCanvas = recorder.BeginRecording(new SKRect(0, 0, info.Width, info.Height));

                // Draw background elements here
                // This is a placeholder for any static background elements

                _cachedBackground = recorder.EndRecording();
            }

            // Draw the cached background
            canvas.DrawPicture(_cachedBackground);
        }

        private void SpawnNewParticles(ReadOnlySpan<float> spectrum, float spawnY, float canvasWidth, float barWidth)
        {
            if (_particleBuffer == null || spectrum.IsEmpty) return;

            float threshold = _isOverlayActive ? _spawnThresholdOverlay : _spawnThresholdNormal;
            float baseSize = _isOverlayActive ? _particleSizeOverlay : _particleSizeNormal;

            // Use optimized spectrum scaling
            var scaledSpectrum = SpectrumBuffer.AsSpan(0, spectrum.Length);
            ScaleSpectrumStandard(spectrum, scaledSpectrum);

            // Use a single random value for batch optimization
            float randomBase = (float)_random.NextDouble();

            for (int i = 0; i < scaledSpectrum.Length; i++)
            {
                float spectrumValue = scaledSpectrum[i];
                if (spectrumValue <= threshold) continue;

                float spawnChance = Math.Min(spectrumValue / threshold, 3f) * _spawnProbability;

                // Optimize random check
                float randomValue = (randomBase + i * 0.01f) % 1.0f;
                if (randomValue >= spawnChance) continue;

                float intensity = Math.Min(spectrumValue / threshold, 3f);

                // Create particle with optimized random values
                float randomX = (float)_random.NextDouble();
                float randomY = (float)_random.NextDouble();
                float randomZ = (float)_random.NextDouble();
                float randomLife = 0.8f + (float)_random.NextDouble() * 0.4f;

                _particleBuffer.Add(new Particle
                {
                    X = i * _renderCache.StepSize + randomX * barWidth,
                    Y = spawnY + randomY * TextParticleConstants.Particles.SPAWN_VARIANCE - TextParticleConstants.Particles.SPAWN_HALF_VARIANCE,
                    Z = Settings.Instance.MinZDepth + randomZ * _zRange,
                    VelocityY = -GetRandomVelocity() * intensity,
                    VelocityX = (randomX - 0.5f) * 2f,
                    Size = baseSize * intensity,
                    Life = _particleLife * randomLife,
                    Alpha = 1f,
                    IsActive = true,
                    Character = _characters[_random.Next(_characters.Length)]
                });
            }
        }

        private void RenderParticles(SKCanvas canvas, SKPaint paint)
        {
            if (_particleBuffer == null || canvas == null || paint == null || _font == null) return;

            var activeParticles = _particleBuffer.GetActiveParticles();
            if (activeParticles.IsEmpty) return;

            using var particlePaint = paint.Clone();
            particlePaint.Style = SKPaintStyle.Fill;
            particlePaint.IsAntialias = _useAntiAlias;
            particlePaint.FilterQuality = _filterQuality;

            // Pre-calculate center coordinates
            Vector2 center = new Vector2(_renderCache.Width / 2f, _renderCache.Height / 2f);
            float focalLength = TextParticleConstants.Rendering.FOCAL_LENGTH;

            // Set clip rect for better performance
            SKRect clipRect = new SKRect(0, 0, _renderCache.Width, _renderCache.Height);
            bool quickReject = canvas.QuickReject(clipRect);
            if (quickReject) return;

            foreach (ref readonly var p in activeParticles)
            {
                if (!p.IsActive) continue;

                // Apply culling based on quality level
                if (_cullingLevel > 0)
                {
                    // Skip particles with very low alpha in low quality mode
                    if (p.Alpha < TextParticleConstants.Performance.MIN_ALPHA_THRESHOLD)
                        continue;

                    // Skip particles outside screen bounds
                    if (p.Y < _renderCache.UpperBound || p.Y > _renderCache.LowerBound)
                        continue;
                }

                // Calculate perspective projection using Vector2 for SIMD optimization
                float depth = focalLength + p.Z;
                float scale = focalLength / depth;

                // Calculate screen positions with SIMD-friendly Vector2 operations
                Vector2 position = new Vector2(p.X, p.Y);
                Vector2 screenPos = center + (position - center) * scale;

                // Skip if outside screen bounds
                if (screenPos.X < 0 || screenPos.X > _renderCache.Width ||
                    screenPos.Y < 0 || screenPos.Y > _renderCache.Height)
                    continue;

                // Calculate alpha with depth factor
                byte alpha = (byte)(p.Alpha * (depth / (focalLength + p.Z)) * 255);
                particlePaint.Color = paint.Color.WithAlpha(alpha);

                // Draw text at calculated position
                canvas.DrawText(p.Character.ToString(), screenPos.X, screenPos.Y, _font, particlePaint);
            }
        }

        private void RenderParticlesBatched(SKCanvas canvas, SKPaint paint)
        {
            if (_particleBuffer == null || canvas == null || paint == null || _font == null) return;

            var activeParticles = _particleBuffer.GetActiveParticles();
            if (activeParticles.IsEmpty) return;

            using var particlePaint = paint.Clone();
            particlePaint.Style = SKPaintStyle.Fill;
            particlePaint.IsAntialias = _useAntiAlias;
            particlePaint.FilterQuality = _filterQuality;

            // Pre-calculate center coordinates
            Vector2 center = new Vector2(_renderCache.Width / 2f, _renderCache.Height / 2f);
            float focalLength = TextParticleConstants.Rendering.FOCAL_LENGTH;

            // Set clip rect for better performance
            SKRect clipRect = new SKRect(0, 0, _renderCache.Width, _renderCache.Height);
            bool quickReject = canvas.QuickReject(clipRect);
            if (quickReject) return;

            // Group particles by character for batched rendering
            Dictionary<char, List<(Vector2 position, byte alpha)>> batchedParticles = new();

            // Process particles and group them
            foreach (ref readonly var p in activeParticles)
            {
                if (!p.IsActive) continue;

                // Apply culling based on quality level
                if (_cullingLevel > 0)
                {
                    // Skip particles with very low alpha in low quality mode
                    if (p.Alpha < TextParticleConstants.Performance.MIN_ALPHA_THRESHOLD)
                        continue;

                    // Skip particles outside screen bounds
                    if (p.Y < _renderCache.UpperBound || p.Y > _renderCache.LowerBound)
                        continue;
                }

                // Calculate perspective projection
                float depth = focalLength + p.Z;
                float scale = focalLength / depth;

                // Calculate screen positions
                Vector2 position = new Vector2(p.X, p.Y);
                Vector2 screenPos = center + (position - center) * scale;

                // Skip if outside screen bounds
                if (screenPos.X < 0 || screenPos.X > _renderCache.Width ||
                    screenPos.Y < 0 || screenPos.Y > _renderCache.Height)
                    continue;

                // Calculate alpha with depth factor
                byte alpha = (byte)(p.Alpha * (depth / (focalLength + p.Z)) * 255);

                // Add to batch
                if (!batchedParticles.TryGetValue(p.Character, out var list))
                {
                    list = new List<(Vector2, byte)>(TextParticleConstants.Performance.MAX_BATCH_SIZE);
                    batchedParticles[p.Character] = list;
                }

                list.Add((screenPos, alpha));
            }

            // Render each batch
            foreach (var batch in batchedParticles)
            {
                char character = batch.Key;
                var particles = batch.Value;

                // Use optimized batch rendering for high quality mode
                if (_useAdvancedEffects && _quality == RenderQuality.High)
                {
                    // Batch rendering with multiple draw calls optimized for GPU
                    const int batchSize = 20;
                    for (int i = 0; i < particles.Count; i += batchSize)
                    {
                        int count = Math.Min(batchSize, particles.Count - i);
                        using (var path = new SKPath())
                        {
                            for (int j = 0; j < count; j++)
                            {
                                var (pos, alpha) = particles[i + j];
                                particlePaint.Color = paint.Color.WithAlpha(alpha);
                                canvas.DrawText(character.ToString(), pos.X, pos.Y, _font, particlePaint);
                            }
                        }
                    }
                }
                else
                {
                    // Standard rendering for each particle in batch
                    string charStr = character.ToString();
                    foreach (var (pos, alpha) in particles)
                    {
                        particlePaint.Color = paint.Color.WithAlpha(alpha);
                        canvas.DrawText(charStr, pos.X, pos.Y, _font, particlePaint);
                    }
                }
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
            private Particle[] _buffer;
            private readonly float _particleLife, _particleLifeDecay, _velocityMultiplier;
            private int _head, _tail, _count, _capacity;
            private readonly object _bufferLock = new object();

            public CircularParticleBuffer(int capacity, float particleLife, float particleLifeDecay,
                float velocityMultiplier, TextParticlesRenderer renderer)
            {
                _capacity = capacity;
                _buffer = new Particle[capacity];
                (_particleLife, _particleLifeDecay, _velocityMultiplier, _renderer) =
                    (particleLife, particleLifeDecay, velocityMultiplier, renderer);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Add(Particle particle)
            {
                lock (_bufferLock)
                {
                    // Resize buffer if needed
                    if (_count >= _capacity)
                    {
                        int newCapacity = _capacity + TextParticleConstants.Performance.PARTICLE_BUFFER_GROWTH;
                        var newBuffer = new Particle[newCapacity];

                        // Copy existing particles to new buffer
                        if (_tail > _head)
                        {
                            Array.Copy(_buffer, _head, newBuffer, 0, _count);
                        }
                        else
                        {
                            int firstPart = _capacity - _head;
                            Array.Copy(_buffer, _head, newBuffer, 0, firstPart);
                            Array.Copy(_buffer, 0, newBuffer, firstPart, _tail);
                        }

                        _head = 0;
                        _tail = _count;
                        _buffer = newBuffer;
                        _capacity = newCapacity;
                    }

                    _buffer[_tail] = particle;
                    _tail = (_tail + 1) % _capacity;
                    _count++;
                }
            }

            public Span<Particle> GetActiveParticles()
            {
                lock (_bufferLock)
                {
                    if (_count == 0) return Span<Particle>.Empty;

                    if (_head < _tail)
                    {
                        return _buffer.AsSpan(_head, _count);
                    }
                    else
                    {
                        // For wrapped buffer, create a new contiguous array
                        // Only do this when absolutely necessary
                        var result = new Particle[_count];
                        int firstPartSize = _capacity - _head;

                        Array.Copy(_buffer, _head, result, 0, firstPartSize);
                        Array.Copy(_buffer, 0, result, firstPartSize, _tail);

                        return result.AsSpan();
                    }
                }
            }

            public void Update(float upperBound, float lowerBound, float alphaDecayExponent)
            {
                if (_count == 0) return;

                lock (_bufferLock)
                {
                    int writeIndex = 0;
                    float lifeRatioScale = 1f / _particleLife;

                    // Use hardware acceleration if available
                    bool useHardwareAcceleration = Vector.IsHardwareAccelerated && _renderer._useHardwareAcceleration;

                    // Process particles in batches for better cache locality
                    for (int i = 0, readIndex = _head; i < _count; i++, readIndex = (readIndex + 1) % _capacity)
                    {
                        ref var particle = ref _buffer[readIndex];
                        if (!particle.IsActive || !UpdateParticle(ref particle, upperBound, lowerBound,
                            lifeRatioScale, alphaDecayExponent)) continue;

                        if (writeIndex != readIndex) _buffer[writeIndex] = particle;
                        writeIndex++;
                    }

                    _count = writeIndex;
                    _head = 0;
                    _tail = writeIndex % _capacity;
                }
            }

            private bool UpdateParticle(ref Particle p, float upperBound, float lowerBound,
                float lifeRatioScale, float alphaDecayExponent)
            {
                p.Life -= _particleLifeDecay;
                if (p.Life <= 0 || p.Y < upperBound - TextParticleConstants.Boundaries.BOUNDARY_MARGIN ||
                    p.Y > lowerBound + TextParticleConstants.Boundaries.BOUNDARY_MARGIN)
                {
                    p.IsActive = false;
                    return false;
                }

                // Update velocity with gravity and air resistance
                p.VelocityY = Math.Clamp(
                    (p.VelocityY + TextParticleConstants.Particles.GRAVITY * TextParticleConstants.Particles.PHYSICS_TIMESTEP) *
                    TextParticleConstants.Particles.AIR_RESISTANCE,
                    -TextParticleConstants.Particles.MAX_VELOCITY * _velocityMultiplier,
                    TextParticleConstants.Particles.MAX_VELOCITY * _velocityMultiplier);

                // Optimize random check with a single value
                float randomValue = (float)_renderer._random.NextDouble();
                if (randomValue < TextParticleConstants.Particles.RANDOM_DIRECTION_CHANCE)
                    p.VelocityX += (randomValue * 2 - 1) * TextParticleConstants.Particles.DIRECTION_VARIANCE;

                // Apply air resistance to horizontal velocity
                p.VelocityX *= TextParticleConstants.Particles.AIR_RESISTANCE;

                // Update position
                p.Y += p.VelocityY;
                p.X += p.VelocityX;

                // Update alpha based on life
                p.Alpha = CalculateAlpha(p.Life * lifeRatioScale, alphaDecayExponent);
                return true;
            }

            private float CalculateAlpha(float lifeRatio, float alphaDecayExponent)
            {
                if (lifeRatio <= 0f) return 0f;
                if (lifeRatio >= 1f) return 1f;

                // Use pre-computed alpha curve for better performance
                return _renderer._alphaCurve?[(int)(lifeRatio * 100)] ??
                    (float)Math.Pow(lifeRatio, alphaDecayExponent);
            }
        }

        private sealed record RenderCache
        {
            public float Width, Height, StepSize, OverlayHeight, UpperBound, LowerBound;
            public int BarCount;
        }
        #endregion

        #region Helper Methods
        private void PrecomputeAlphaCurve()
        {
            _alphaCurve = ArrayPool<float>.Shared.Rent(101);
            float step = 1f / (_alphaCurve.Length - 1);

            // Standard computation
            for (int i = 0; i < _alphaCurve.Length; i++)
                _alphaCurve[i] = (float)Math.Pow(i * step, _alphaDecayExponent);
        }

        private void InitializeVelocityLookup(float minVelocity)
        {
            _velocityLookup = ArrayPool<float>.Shared.Rent(TextParticleConstants.Particles.VELOCITY_LOOKUP_SIZE);

            // Standard computation
            for (int i = 0; i < _velocityLookup.Length; i++)
                _velocityLookup[i] = minVelocity + _velocityRange * i / TextParticleConstants.Particles.VELOCITY_LOOKUP_SIZE;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float GetRandomVelocity() => _velocityLookup?[_random.Next(TextParticleConstants.Particles.VELOCITY_LOOKUP_SIZE)] * _velocityMultiplier
            ?? throw new InvalidOperationException("Velocity lookup is not initialized.");

        private void UpdateParticleSizes()
        {
            if (_particleBuffer == null) return;

            float baseSize = _isOverlayActive ? _particleSizeOverlay : _particleSizeNormal;
            float oldBaseSize = _isOverlayActive ? _particleSizeNormal : _particleSizeOverlay;

            lock (_particleLock)
            {
                foreach (ref var particle in _particleBuffer.GetActiveParticles())
                    if (particle.IsActive)
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
                    info.Width > 0 && info.Height > 0 && _font != null;

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private void ScaleSpectrumOptimized(ReadOnlySpan<float> source, Span<float> dest)
        {
            if (source.IsEmpty || dest.IsEmpty) return;
            if (dest.Length == source.Length) { source.CopyTo(dest); return; }

            // Use hardware acceleration if available
            if (Vector.IsHardwareAccelerated && _useHardwareAcceleration &&
                source.Length >= Vector<float>.Count && dest.Length >= Vector<float>.Count)
            {
                // Process in parallel for large arrays
                if (dest.Length > 1000)
                {
                    ParallelScaleSpectrum(source, dest);
                }
                else
                {
                    ScaleSpectrumStandard(source, dest);
                }
            }
            else
            {
                ScaleSpectrumStandard(source, dest);
            }
        }

        private void ParallelScaleSpectrum(ReadOnlySpan<float> source, Span<float> dest)
        {
            float scale = (float)(source.Length - 1) / (dest.Length - 1);
            float[] sourceArray = source.ToArray();
            float[] destArray = new float[dest.Length];

            // Use Parallel.For for large arrays
            Parallel.For(0, dest.Length, i => {
                float index = i * scale;
                int baseIndex = (int)index;
                float fraction = index - baseIndex;

                destArray[i] = baseIndex >= sourceArray.Length - 1
                    ? sourceArray[sourceArray.Length - 1]
                    : sourceArray[baseIndex] * (1 - fraction) + sourceArray[baseIndex + 1] * fraction;
            });

            // Copy back to destination
            for (int i = 0; i < dest.Length; i++)
            {
                dest[i] = destArray[i];
            }
        }

        private static void ScaleSpectrumStandard(ReadOnlySpan<float> source, Span<float> dest)
        {
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

            // Return rented arrays to pool
            if (_spectrumBuffer != null) ArrayPool<float>.Shared.Return(_spectrumBuffer);
            if (_velocityLookup != null) ArrayPool<float>.Shared.Return(_velocityLookup);
            if (_alphaCurve != null) ArrayPool<float>.Shared.Return(_alphaCurve);

            // Dispose managed resources
            _font?.Dispose();
            _cachedBackground?.Dispose();

            // Clear references
            _spectrumBuffer = _velocityLookup = _alphaCurve = null;
            _particleBuffer = null;
            _font = null;
            _cachedBackground = null;
            _renderCache = new RenderCache();

            _isInitialized = false;
            _isDisposed = true;

            SmartLogger.Log(LogLevel.Information, LOG_PREFIX, "Disposed");
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}