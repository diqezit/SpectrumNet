#nullable enable

using static System.MathF;
using static SpectrumNet.Views.Renderers.TextParticlesRenderer.Constants;

namespace SpectrumNet.Views.Renderers;

public sealed class TextParticlesRenderer : EffectSpectrumRenderer
{
    public record Constants
    {
        public const string LOG_PREFIX = "TextParticlesRenderer";

        public const float
            FOCAL_LENGTH = 1000f,
            BASE_TEXT_SIZE = 12f,
            ALPHA_MAX = 255f,
            GRAVITY = 9.81f,
            AIR_RESISTANCE = 0.98f,
            MAX_VELOCITY = 15f,
            RANDOM_DIRECTION_CHANCE = 0.05f,
            DIRECTION_VARIANCE = 0.5f,
            SPAWN_VARIANCE = 5f,
            SPAWN_HALF_VARIANCE = SPAWN_VARIANCE / 2f,
            PHYSICS_TIMESTEP = 0.016f,
            MIN_SPAWN_INTENSITY = 1f,
            MAX_SPAWN_INTENSITY = 3f,
            LIFE_VARIANCE_MIN = 0.8f,
            LIFE_VARIANCE_MAX = 1.2f,
            BOUNDARY_MARGIN = 50f,
            MIN_ALPHA_THRESHOLD = 0.05f;

        public const int
            VELOCITY_LOOKUP_SIZE = 1024,
            MAX_BATCH_SIZE = 1000,
            PARTICLE_BUFFER_GROWTH = 128,
            SPECTRUM_BUFFER_SIZE = 2048;

        public const string DEFAULT_CHARACTERS = "01";
    }

    private static readonly Lazy<TextParticlesRenderer> _instance =
        new(() => new TextParticlesRenderer(), LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly Random _random = new();
    private readonly string _characters;
    private readonly float _velocityRange;
    private readonly float _particleLife;
    private readonly float _particleLifeDecay;
    private readonly float _alphaDecayExponent;
    private readonly float _spawnThresholdOverlay;
    private readonly float _spawnThresholdNormal;
    private readonly float _spawnProbability;
    private readonly float _particleSizeOverlay;
    private readonly float _particleSizeNormal;
    private readonly float _velocityMultiplier;
    private readonly float _zRange;
    private readonly SemaphoreSlim _renderSemaphore = new(1, 1);
    private readonly object _particleLock = new();

    private CircularParticleBuffer? _particleBuffer;
    private RenderCache _renderCache = new();
    private float[]? _spectrumBuffer;
    private float[]? _velocityLookup;
    private float[]? _alphaCurve;
    private SKFont? _font;
    private SKPicture? _cachedBackground;

    private bool _useBatching = true;
    private int _cullingLevel = 1;
    private bool _useHardwareAcceleration = true;

    private TextParticlesRenderer()
    {
        Settings s = Settings.Instance;
        _characters = DEFAULT_CHARACTERS;
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
    }

    public static TextParticlesRenderer GetInstance() => _instance.Value;

    public override void Initialize()
    {
        if (_isInitialized) return;

        base.Initialize();
        InitializeParticleBuffer();
        _renderCache = new RenderCache();
        ApplyQualitySettings();
        _isInitialized = true;
        Log(LogLevel.Debug, LOG_PREFIX, "Initialized");
    }

    private void InitializeParticleBuffer()
    {
        _particleBuffer = new CircularParticleBuffer(
            Settings.Instance.MaxParticles,
            _particleLife,
            _particleLifeDecay,
            _velocityMultiplier,
            this);
    }

    public override void Configure(
        bool isOverlayActive,
        RenderQuality quality = RenderQuality.Medium)
    {
        base.Configure(isOverlayActive, quality);

        if (_isOverlayActive != isOverlayActive)
        {
            _isOverlayActive = isOverlayActive;
            UpdateParticleSizes();
            InvalidateCachedBackground();
        }
    }

    protected override void ApplyQualitySettings()
    {
        base.ApplyQualitySettings();

        switch (_quality)
        {
            case RenderQuality.Low:
                ApplyLowQualitySettings();
                break;

            case RenderQuality.Medium:
                ApplyMediumQualitySettings();
                break;

            case RenderQuality.High:
                ApplyHighQualitySettings();
                break;
        }

        UpdateFontQualitySettings();
        InvalidateCachedBackground();
    }

    private void ApplyLowQualitySettings()
    {
        _useBatching = false;
        _cullingLevel = 2;
        _useHardwareAcceleration = false;
        _useAntiAlias = false;
        _samplingOptions = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);
    }

    private void ApplyMediumQualitySettings()
    {
        _useBatching = true;
        _cullingLevel = 1;
        _useHardwareAcceleration = true;
        _useAntiAlias = true;
        _samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
    }

    private void ApplyHighQualitySettings()
    {
        _useBatching = true;
        _cullingLevel = 0;
        _useHardwareAcceleration = true;
        _useAntiAlias = true;
        _samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
    }

    private void UpdateFontQualitySettings()
    {
        if (_font != null)
        {
            _font.Edging = _useAntiAlias ? SKFontEdging.SubpixelAntialias : SKFontEdging.Alias;
        }
    }

    protected override void RenderEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint paint)
    {
        if (_particleBuffer == null)
        {
            Log(LogLevel.Error, LOG_PREFIX, "Uninitialized buffer");
            return;
        }

        int actualBarCount = Min(spectrum.Length, barCount);
        float[] processedSpectrum = PrepareSpectrum(spectrum, actualBarCount, spectrum.Length);

        bool semaphoreAcquired = false;
        try
        {
            semaphoreAcquired = _renderSemaphore.Wait(0);
            if (semaphoreAcquired)
            {
                UpdateRenderCache(info, actualBarCount);
                UpdateParticles();
                SpawnNewParticles(processedSpectrum, _renderCache.LowerBound, info.Width, barWidth);
            }

            DrawCachedBackground(canvas, info);

            if (_useBatching)
                RenderParticlesBatched(canvas, paint);
            else
                RenderParticles(canvas, paint);
        }
        finally
        {
            if (semaphoreAcquired)
                _renderSemaphore.Release();
        }
    }

    private void UpdateParticles()
    {
        _particleBuffer!.Update(
            _renderCache.UpperBound,
            _renderCache.LowerBound,
            _alphaDecayExponent);
    }

    private void DrawCachedBackground(SKCanvas canvas, SKImageInfo info)
    {
        if (!UseAdvancedEffects || _quality == RenderQuality.Low || canvas == null)
            return;

        if (_cachedBackground == null)
        {
            CreateCachedBackground(info);
        }

        canvas.DrawPicture(_cachedBackground!);
    }

    private void CreateCachedBackground(SKImageInfo info)
    {
        using var recorder = new SKPictureRecorder();
        using var recordCanvas = recorder.BeginRecording(new SKRect(0, 0, info.Width, info.Height));
        _cachedBackground = recorder.EndRecording();
    }

    private void RenderParticles(SKCanvas canvas, SKPaint paint)
    {
        if (_particleBuffer == null || _font == null)
            return;

        var activeParticles = _particleBuffer.GetActiveParticles();
        if (activeParticles.IsEmpty)
            return;

        using var particlePaint = CreateParticlePaint(paint);
        Vector2 center = GetScreenCenter();

        foreach (ref readonly var particle in activeParticles)
        {
            RenderSingleParticle(canvas, particle, center, particlePaint);
        }
    }

    private void RenderSingleParticle(
        SKCanvas canvas,
        in Particle particle,
        Vector2 center,
        SKPaint particlePaint)
    {
        if (!ShouldRenderParticle(particle))
            return;

        float depth = FOCAL_LENGTH + particle.Z;
        float scale = FOCAL_LENGTH / depth;

        Vector2 position = new(particle.X, particle.Y);
        Vector2 screenPos = center + (position - center) * scale;

        if (!IsRenderAreaVisible(canvas, screenPos.X, screenPos.Y, particle.Size, particle.Size))
            return;

        byte alpha = CalculateParticleAlpha(particle, depth);
        particlePaint.Color = particlePaint.Color.WithAlpha(alpha);

        canvas.DrawText(
            particle.Character.ToString(),
            screenPos.X,
            screenPos.Y,
            _font,
            particlePaint);
    }

    private static byte CalculateParticleAlpha(in Particle particle, float depth) => 
        (byte)(particle.Alpha * (depth / (FOCAL_LENGTH + particle.Z)) * ALPHA_MAX);

    private void RenderParticlesBatched(SKCanvas canvas, SKPaint paint)
    {
        if (_particleBuffer == null || _font == null)
            return;

        var activeParticles = _particleBuffer.GetActiveParticles();
        if (activeParticles.IsEmpty)
            return;

        using var particlePaint = CreateParticlePaint(paint);
        Vector2 center = GetScreenCenter();

        var batches = new Dictionary<char, List<(Vector2 position, byte alpha)>>();

        PrepareParticleBatches(activeParticles, center, batches);
        RenderParticleBatches(canvas, batches, paint, particlePaint);
    }

    private void PrepareParticleBatches(
        Span<Particle> activeParticles,
        Vector2 center,
        Dictionary<char, List<(Vector2, byte)>> batches)
    {
        foreach (ref readonly var particle in activeParticles)
        {
            if (!ShouldRenderParticle(particle))
                continue;

            float depth = FOCAL_LENGTH + particle.Z;
            float scale = FOCAL_LENGTH / depth;

            Vector2 position = new(particle.X, particle.Y);
            Vector2 screenPos = center + (position - center) * scale;

            if (!IsRenderAreaVisible(null!, screenPos.X, screenPos.Y, particle.Size, particle.Size))
                continue;

            byte alpha = CalculateParticleAlpha(particle, depth);

            if (!batches.TryGetValue(particle.Character, out var list))
            {
                list = new List<(Vector2, byte)>(MAX_BATCH_SIZE);
                batches[particle.Character] = list;
            }

            list.Add((screenPos, alpha));
        }
    }

    private void RenderParticleBatches(
        SKCanvas canvas,
        Dictionary<char, List<(Vector2, byte)>> batches,
        SKPaint paint,
        SKPaint particlePaint)
    {
        foreach (var batch in batches)
        {
            string charStr = batch.Key.ToString();

            foreach (var (position, alpha) in batch.Value)
            {
                particlePaint.Color = paint.Color.WithAlpha(alpha);
                canvas.DrawText(charStr, position.X, position.Y, _font!, particlePaint);
            }
        }
    }

    private SKPaint CreateParticlePaint(SKPaint basePaint)
    {
        return new SKPaint
        {
            Style = SKPaintStyle.Fill,
            IsAntialias = _useAntiAlias,
            Color = basePaint.Color
        };
    }

    private Vector2 GetScreenCenter() => new(_renderCache.Width / 2f, _renderCache.Height / 2f);

    private void SpawnNewParticles(
        float[] spectrum,
        float spawnY,
        float canvasWidth,
        float barWidth)
    {
        if (_particleBuffer == null || spectrum.Length == 0)
            return;

        float threshold = _isOverlayActive ? _spawnThresholdOverlay : _spawnThresholdNormal;
        float baseSize = _isOverlayActive ? _particleSizeOverlay : _particleSizeNormal;

        for (int i = 0; i < spectrum.Length; i++)
        {
            TrySpawnParticle(spectrum[i], threshold, baseSize, i, spawnY, barWidth);
        }
    }

    private void TrySpawnParticle(
        float value,
        float threshold,
        float baseSize,
        int index,
        float spawnY,
        float barWidth)
    {
        if (value <= threshold)
            return;

        float spawnChance = MathF.Min(value / threshold, MAX_SPAWN_INTENSITY) * _spawnProbability;
        if (_random.NextDouble() >= spawnChance)
            return;

        float intensity = MathF.Min(value / threshold, MAX_SPAWN_INTENSITY);
        _particleBuffer!.Add(CreateParticle(index, spawnY, barWidth, baseSize, intensity));
    }

    private Particle CreateParticle(
        int index,
        float spawnY,
        float barWidth,
        float baseSize,
        float intensity)
    {
        float x = index * _renderCache.StepSize + (float)_random.NextDouble() * barWidth;
        float y = spawnY + (float)_random.NextDouble() * SPAWN_VARIANCE - SPAWN_HALF_VARIANCE;
        float z = Settings.Instance.MinZDepth + (float)_random.NextDouble() * _zRange;
        float lifeVariance = LIFE_VARIANCE_MIN +
                           (float)_random.NextDouble() *
                           (LIFE_VARIANCE_MAX - LIFE_VARIANCE_MIN);

        return new Particle
        {
            X = x,
            Y = y,
            Z = z,
            VelocityY = -GetRandomVelocity() * intensity,
            VelocityX = ((float)_random.NextDouble() - 0.5f) * 2f,
            Size = baseSize * intensity,
            Life = _particleLife * lifeVariance,
            Alpha = 1f,
            IsActive = true,
            Character = _characters[_random.Next(_characters.Length)]
        };
    }

    private struct Particle
    {
        public float X, Y, Z;
        public float VelocityX, VelocityY;
        public float Size, Life, Alpha;
        public bool IsActive;
        public char Character;
    }

    private sealed class CircularParticleBuffer(
        int capacity,
        float particleLife,
        float particleLifeDecay,
        float velocityMultiplier,
        TextParticlesRenderer renderer)
    {
        private Particle[] _buffer = new Particle[capacity];
        private int _head, _tail, _count;
        private readonly object _bufferLock = new();

        public void Add(Particle particle)
        {
            lock (_bufferLock)
            {
                if (_count >= capacity)
                    ResizeBuffer();

                _buffer[_tail] = particle;
                _tail = (_tail + 1) % capacity;
                _count++;
            }
        }

        public Span<Particle> GetActiveParticles()
        {
            lock (_bufferLock)
            {
                if (_count == 0)
                    return [];

                return _head < _tail
                    ? _buffer.AsSpan(_head, _count)
                    : BuildContiguousSpan();
            }
        }

        public void Update(float upperBound, float lowerBound, float alphaDecayExponent)
        {
            if (_count == 0)
                return;

            lock (_bufferLock)
            {
                UpdateActiveParticles(upperBound, lowerBound, alphaDecayExponent);
            }
        }

        private void UpdateActiveParticles(
            float upperBound,
            float lowerBound,
            float alphaDecayExponent)
        {
            int writeIndex = 0;
            float lifeRatioScale = 1f / particleLife;

            for (int i = 0, readIndex = _head; i < _count; i++, readIndex = (readIndex + 1) % capacity)
            {
                ref var particle = ref _buffer[readIndex];

                if (!particle.IsActive ||
                    !UpdateParticle(ref particle, upperBound, lowerBound, lifeRatioScale, alphaDecayExponent))
                    continue;

                if (writeIndex != readIndex)
                    _buffer[writeIndex] = particle;

                writeIndex++;
            }

            _count = writeIndex;
            _head = 0;
            _tail = writeIndex % capacity;
        }

        private bool UpdateParticle(
            ref Particle p,
            float upperBound,
            float lowerBound,
            float lifeRatioScale,
            float alphaDecayExponent)
        {
            p.Life -= particleLifeDecay;

            if (p.Life <= 0 ||
                p.Y < upperBound - BOUNDARY_MARGIN ||
                p.Y > lowerBound + BOUNDARY_MARGIN)
            {
                p.IsActive = false;
                return false;
            }

            UpdateParticleVelocity(ref p);
            UpdateParticlePosition(ref p);
            p.Alpha = CalculateAlpha(p.Life * lifeRatioScale, alphaDecayExponent);

            return true;
        }

        private void UpdateParticleVelocity(ref Particle p)
        {
            p.VelocityY = Clamp(
                (p.VelocityY + GRAVITY * PHYSICS_TIMESTEP) * AIR_RESISTANCE,
                -MAX_VELOCITY * velocityMultiplier,
                MAX_VELOCITY * velocityMultiplier);

            if (renderer._random.NextDouble() < RANDOM_DIRECTION_CHANCE)
                p.VelocityX += ((float)renderer._random.NextDouble() - 0.5f) * DIRECTION_VARIANCE;

            p.VelocityX *= AIR_RESISTANCE;
        }

        private static void UpdateParticlePosition(ref Particle p)
        {
            p.Y += p.VelocityY;
            p.X += p.VelocityX;
        }

        private float CalculateAlpha(float lifeRatio, float alphaDecayExponent)
        {
            if (lifeRatio <= 0f) return 0f;
            if (lifeRatio >= 1f) return 1f;
            return renderer._alphaCurve?[(int)(lifeRatio * 100)] ??
                   (float)Pow(lifeRatio, alphaDecayExponent);
        }

        private void ResizeBuffer()
        {
            int newCapacity = capacity + PARTICLE_BUFFER_GROWTH;
            var newBuffer = new Particle[newCapacity];

            if (_tail > _head)
            {
                Array.Copy(_buffer, _head, newBuffer, 0, _count);
            }
            else
            {
                int firstPart = capacity - _head;
                Array.Copy(_buffer, _head, newBuffer, 0, firstPart);
                Array.Copy(_buffer, 0, newBuffer, firstPart, _tail);
            }

            _head = 0;
            _tail = _count;
            _buffer = newBuffer;
            capacity = newCapacity;
        }

        private Span<Particle> BuildContiguousSpan()
        {
            var result = new Particle[_count];
            int firstPartSize = capacity - _head;

            Array.Copy(_buffer, _head, result, 0, firstPartSize);
            Array.Copy(_buffer, 0, result, firstPartSize, _tail);

            return result.AsSpan();
        }
    }

    private sealed record RenderCache
    {
        public float Width, Height, StepSize, OverlayHeight, UpperBound, LowerBound;
        public int BarCount;
    }

    private void InitializeFont()
    {
        _font = new SKFont { Size = BASE_TEXT_SIZE };
        if (_useAntiAlias)
            _font.Edging = SKFontEdging.SubpixelAntialias;
    }

    private void PrecomputeAlphaCurve()
    {
        _alphaCurve = ArrayPool<float>.Shared.Rent(101);
        float step = 1f / (_alphaCurve.Length - 1);

        for (int i = 0; i < _alphaCurve.Length; i++)
            _alphaCurve[i] = (float)Pow(i * step, _alphaDecayExponent);
    }

    private void InitializeVelocityLookup(float minVelocity)
    {
        _velocityLookup = ArrayPool<float>.Shared.Rent(VELOCITY_LOOKUP_SIZE);

        for (int i = 0; i < _velocityLookup.Length; i++)
            _velocityLookup[i] = minVelocity + _velocityRange * i / VELOCITY_LOOKUP_SIZE;
    }

    private float GetRandomVelocity() =>
        _velocityLookup?[_random.Next(VELOCITY_LOOKUP_SIZE)] ??
        throw new InvalidOperationException("Velocity lookup not initialized");

    private void UpdateParticleSizes()
    {
        if (_particleBuffer == null)
            return;

        float newSize = _isOverlayActive ? _particleSizeOverlay : _particleSizeNormal;
        float oldSize = _isOverlayActive ? _particleSizeNormal : _particleSizeOverlay;

        lock (_particleLock)
        {
            foreach (ref var particle in _particleBuffer.GetActiveParticles())
                if (particle.IsActive)
                    particle.Size = newSize * (particle.Size / oldSize);
        }
    }

    private void UpdateRenderCache(SKImageInfo info, int barCount)
    {
        _renderCache.Width = info.Width;
        _renderCache.Height = info.Height;
        _renderCache.StepSize = barCount > 0 ? info.Width / (float)barCount : 0f;

        float overlayHeight = info.Height * Settings.Instance.OverlayHeightMultiplier;
        _renderCache.OverlayHeight = _isOverlayActive ? overlayHeight : 0f;
        _renderCache.UpperBound = _isOverlayActive ? info.Height - overlayHeight : 0f;
        _renderCache.LowerBound = info.Height;
        _renderCache.BarCount = barCount;
    }

    private bool ShouldRenderParticle(in Particle p)
    {
        if (!p.IsActive)
            return false;

        if (_cullingLevel > 0 && p.Alpha < MIN_ALPHA_THRESHOLD)
            return false;

        if (_cullingLevel > 1 && (p.Y < _renderCache.UpperBound || p.Y > _renderCache.LowerBound))
            return false;

        return true;
    }

    private void InvalidateCachedBackground()
    {
        _cachedBackground?.Dispose();
        _cachedBackground = null;
    }

    protected override void OnDispose()
    {
        ReleaseResources();
        base.OnDispose();
    }

    private void ReleaseResources()
    {
        _renderSemaphore.Dispose();

        if (_spectrumBuffer != null)
            ArrayPool<float>.Shared.Return(_spectrumBuffer);

        if (_velocityLookup != null)
            ArrayPool<float>.Shared.Return(_velocityLookup);

        if (_alphaCurve != null)
            ArrayPool<float>.Shared.Return(_alphaCurve);

        _font?.Dispose();
        _cachedBackground?.Dispose();

        _particleBuffer = null;
        _spectrumBuffer = null;
        _velocityLookup = null;
        _alphaCurve = null;
        _font = null;
        _cachedBackground = null;
    }
}