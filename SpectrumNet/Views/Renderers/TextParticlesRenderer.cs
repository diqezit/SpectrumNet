#nullable enable
#pragma warning disable CS8618 

using static System.MathF;
using static SpectrumNet.Views.Renderers.TextParticlesRenderer.Constants;
using static SpectrumNet.Views.Renderers.TextParticlesRenderer.Constants.Quality;

namespace SpectrumNet.Views.Renderers;

public sealed class TextParticlesRenderer : EffectSpectrumRenderer
{
    private static readonly Lazy<TextParticlesRenderer> _instance = 
        new(() => new TextParticlesRenderer());

    public const string LOG_PREFIX = nameof(TextParticlesRenderer);

    public static TextParticlesRenderer GetInstance() => _instance.Value;

    public record Constants
    {

        public const float
            FOCAL_LENGTH = 1000f,
            GRAVITY = 9.81f,
            AIR_RESISTANCE = 0.98f,
            MAX_VELOCITY = 15f,
            DIRECTION_VARIANCE = 0.5f,
            RANDOM_DIRECTION_CHANCE = 0.05f,
            PHYSICS_TIMESTEP = 0.016f;

        public const float
            BASE_TEXT_SIZE = 12f,
            ALPHA_MAX = 255f,
            MIN_ALPHA_THRESHOLD = 0.05f;

        public const float
            SPAWN_VARIANCE = 5f,
            SPAWN_HALF_VARIANCE = SPAWN_VARIANCE / 2f,
            MIN_SPAWN_INTENSITY = 1f,
            MAX_SPAWN_INTENSITY = 3f,
            LIFE_VARIANCE_MIN = 0.8f,
            LIFE_VARIANCE_MAX = 1.2f,
            BOUNDARY_MARGIN = 50f;

        public const int
            VELOCITY_LOOKUP_SIZE = 1024,
            MAX_BATCH_SIZE = 1000,
            PARTICLE_BUFFER_GROWTH = 128,
            SPECTRUM_BUFFER_SIZE = 2048;

        public const string DEFAULT_CHARACTERS = "01";

        public static class Quality
        {
            public const bool
                LOW_USE_BATCHING = false,
                LOW_USE_ANTI_ALIAS = false;

            public const int
                LOW_CULLING_LEVEL = 2;

            public const SKFilterMode
                LOW_FILTER_MODE = SKFilterMode.Nearest;

            public const SKMipmapMode
                LOW_MIPMAP_MODE = SKMipmapMode.None;

            public const bool
                MEDIUM_USE_BATCHING = true,
                MEDIUM_USE_ANTI_ALIAS = true;

            public const int
                MEDIUM_CULLING_LEVEL = 1;

            public const SKFilterMode
                MEDIUM_FILTER_MODE = SKFilterMode.Linear;

            public const SKMipmapMode
                MEDIUM_MIPMAP_MODE = SKMipmapMode.Linear;

            public const bool
                HIGH_USE_BATCHING = true,
                HIGH_USE_ANTI_ALIAS = true;

            public const int
                HIGH_CULLING_LEVEL = 0;

            public const SKFilterMode
                HIGH_FILTER_MODE = SKFilterMode.Linear;

            public const SKMipmapMode
                HIGH_MIPMAP_MODE = SKMipmapMode.Linear;
        }
    }

    private string _characters;
    private float
        _velocityRange,
        _particleLife,
        _particleLifeDecay,
        _alphaDecayExponent,
        _spawnThresholdOverlay,
        _spawnThresholdNormal,
        _spawnProbability,
        _particleSizeOverlay,
        _particleSizeNormal,
        _velocityMultiplier,
        _zRange;

    private bool _useBatching;
    private int _cullingLevel;

    private readonly Random _random = new();
    private readonly SemaphoreSlim _renderSemaphore = new(1, 1);
    private readonly object _particleLock = new();
    private CircularParticleBuffer? _particleBuffer;
    private RenderCache _renderCache = new();
    private float[]? _spectrumBuffer;
    private float[]? _velocityLookup;
    private float[]? _alphaCurve;
    private SKFont? _font;
    private SKPicture? _cachedBackground;

    private TextParticlesRenderer()
    {
        InitializeBaseFields();
        InitializeFont();
        PrecomputeAlphaCurve();
        InitializeVelocityLookup();
    }

    protected override void OnInitialize() =>
        _logger.Safe(
            HandleInitialize,
            LOG_PREFIX,
            "Failed during renderer initialization"
        );

    private void InitializeBaseFields()
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
    }

    private void InitializeParticleBuffer() =>
        _particleBuffer = new CircularParticleBuffer(
            Settings.Instance.MaxParticles,
            _particleLife,
            _particleLifeDecay,
            _velocityMultiplier,
            this);

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

    private void InitializeVelocityLookup()
    {
        float minVelocity = Settings.Instance.ParticleVelocityMin;
        _velocityLookup = ArrayPool<float>.Shared.Rent(VELOCITY_LOOKUP_SIZE);

        for (int i = 0; i < _velocityLookup.Length; i++)
            _velocityLookup[i] = minVelocity + _velocityRange * i / VELOCITY_LOOKUP_SIZE;
    }

    protected override void OnConfigurationChanged() =>
        _logger.Safe(
            HandleConfigurationChanged,
            LOG_PREFIX,
            "Failed to handle configuration change"
        );

    private void HandleConfigurationChanged()
    {
        UpdateOverlayState(_isOverlayActive);
        _logger.Debug(LOG_PREFIX, $"Configuration changed. New Quality: {Quality}");
    }

    private void UpdateOverlayState(bool isOverlayActive)
    {
        UpdateParticleSizes();
        InvalidateCachedBackground();
    }

    protected override void OnQualitySettingsApplied() =>
        _logger.Safe(
            HandleQualitySettingsApplied,
            LOG_PREFIX,
            "Failed to apply specific quality settings"
        );

    private void HandleQualitySettingsApplied()
    {
        switch (Quality)
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

        _logger.Debug(LOG_PREFIX, $"Quality settings applied. New Quality: {Quality}");
    }

    private void ApplyLowQualitySettings()
    {
        _useBatching = LOW_USE_BATCHING;
        _cullingLevel = LOW_CULLING_LEVEL;
        _useAntiAlias = LOW_USE_ANTI_ALIAS;
        _samplingOptions = new SKSamplingOptions(
            LOW_FILTER_MODE,
            LOW_MIPMAP_MODE);
    }

    private void ApplyMediumQualitySettings()
    {
        _useBatching = MEDIUM_USE_BATCHING;
        _cullingLevel = MEDIUM_CULLING_LEVEL;
        _useAntiAlias = MEDIUM_USE_ANTI_ALIAS;
        _samplingOptions = new SKSamplingOptions(
            MEDIUM_FILTER_MODE,
            MEDIUM_MIPMAP_MODE);
    }

    private void ApplyHighQualitySettings()
    {
        _useBatching = HIGH_USE_BATCHING;
        _cullingLevel = HIGH_CULLING_LEVEL;
        _useAntiAlias = HIGH_USE_ANTI_ALIAS;
        _samplingOptions = new SKSamplingOptions(
            HIGH_FILTER_MODE,
            HIGH_MIPMAP_MODE);
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
        SKPaint paint) =>
        _logger.Safe(
            () => HandleRenderEffect(canvas, spectrum, info, barWidth, barCount, paint),
            LOG_PREFIX,
            "Error during rendering"
        );

    private void HandleRenderEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        int barCount,
        SKPaint paint)
    {
        if (_particleBuffer == null) return;

        int actualBarCount = Min(spectrum.Length, barCount);
        float[] processedSpectrum = PrepareSpectrum(spectrum, actualBarCount, spectrum.Length);

        UpdateStateAndSpawnParticles(processedSpectrum, info, actualBarCount, barWidth);
        RenderParticlesAndBackground(canvas, info, paint);
    }

    private void UpdateStateAndSpawnParticles(
        float[] processedSpectrum,
        SKImageInfo info,
        int actualBarCount,
        float barWidth) =>
        _logger.Safe(
            () => HandleUpdateStateAndSpawnParticles(processedSpectrum, info, actualBarCount, barWidth),
            LOG_PREFIX,
            "Error updating particle state"
        );

    private void HandleUpdateStateAndSpawnParticles(
        float[] processedSpectrum,
        SKImageInfo info,
        int actualBarCount,
        float barWidth)
    {
        bool semaphoreAcquired = false;
        try
        {
            semaphoreAcquired = _renderSemaphore.Wait(0);
            if (semaphoreAcquired)
            {
                UpdateRenderCache(info, actualBarCount);
                UpdateParticles();
                SpawnNewParticles(processedSpectrum,
                                  _renderCache.LowerBound,
                                  info.Width,
                                  barWidth);
            }
        }
        finally
        {
            if (semaphoreAcquired)
                _renderSemaphore.Release();
        }
    }

    private void RenderParticlesAndBackground(
        SKCanvas canvas,
        SKImageInfo info,
        SKPaint paint) =>
        _logger.Safe(
            () => HandleRenderParticlesAndBackground(canvas, info, paint),
            LOG_PREFIX,
            "Error rendering particles"
        );

    private void HandleRenderParticlesAndBackground(
        SKCanvas canvas,
        SKImageInfo info,
        SKPaint paint)
    {
        DrawCachedBackground(canvas, info);

        if (_useBatching)
            RenderParticlesBatched(canvas, paint);
        else
            RenderParticles(canvas, paint);
    }

    private void UpdateParticles() =>
        _particleBuffer!.Update(
            _renderCache.UpperBound,
            _renderCache.LowerBound,
            _alphaDecayExponent);

    private void DrawCachedBackground(SKCanvas canvas, SKImageInfo info)
    {
        if (!UseAdvancedEffects || Quality == RenderQuality.Low || canvas == null)
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

    private void RenderParticles(SKCanvas canvas, SKPaint paint) =>
        _logger.Safe(
            () => HandleRenderParticles(canvas, paint),
            LOG_PREFIX,
            "Error rendering particles"
        );

    private void HandleRenderParticles(SKCanvas canvas, SKPaint paint)
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
            _font!,
            particlePaint);
    }

    private static byte CalculateParticleAlpha(in Particle particle, float depth) =>
        (byte)(particle.Alpha * (depth / (FOCAL_LENGTH + particle.Z)) * ALPHA_MAX);

    private void RenderParticlesBatched(SKCanvas canvas, SKPaint paint) =>
        _logger.Safe(
            () => HandleRenderParticlesBatched(canvas, paint),
            LOG_PREFIX,
            "Error rendering particles in batches"
        );

    private void HandleRenderParticlesBatched(SKCanvas canvas, SKPaint paint)
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

            if (!IsRenderAreaVisible(null, screenPos.X, screenPos.Y, particle.Size, particle.Size))
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

    private SKPaint CreateParticlePaint(SKPaint basePaint) =>
        new()
        {
            Style = SKPaintStyle.Fill,
            IsAntialias = _useAntiAlias,
            Color = basePaint.Color
        };

    private Vector2 GetScreenCenter() =>
        new(_renderCache.Width / 2f, _renderCache.Height / 2f);

    private void SpawnNewParticles(
        float[] spectrum,
        float spawnY,
        float _,
        float barWidth) =>
        _logger.Safe(
            () => HandleSpawnNewParticles(spectrum, spawnY, barWidth),
            LOG_PREFIX,
            "Error spawning new particles"
        );

    private void HandleSpawnNewParticles(
        float[] spectrum,
        float spawnY,
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

    private float GetRandomVelocity() =>
        _velocityLookup?[_random.Next(VELOCITY_LOOKUP_SIZE)] ??
        throw new InvalidOperationException("Velocity lookup not initialized");

    private void UpdateParticleSizes() =>
        _logger.Safe(
            HandleUpdateParticleSizes,
            LOG_PREFIX,
            "Error updating particle sizes"
        );

    private void HandleUpdateParticleSizes()
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

    protected override void OnInvalidateCachedResources() =>
        _logger.Safe(
            () =>
            {
                base.OnInvalidateCachedResources();
                InvalidateCachedBackground();
                _logger.Debug(LOG_PREFIX, "Cached resources invalidated");
            },
            LOG_PREFIX,
            "Error invalidating cached resources"
        );

    protected override void OnDispose() =>
        _logger.Safe(
            () =>
            {
                ReleaseResources();
                ClearReferences();
                base.OnDispose();
                _logger.Debug(LOG_PREFIX, "Disposed");
            },
            LOG_PREFIX,
            "Error during specific disposal"
        );

    private void ReleaseResources()
    {
        _renderSemaphore?.Dispose();
        _font?.Dispose();
        _cachedBackground?.Dispose();

        if (_spectrumBuffer != null)
            ArrayPool<float>.Shared.Return(_spectrumBuffer);

        if (_velocityLookup != null)
            ArrayPool<float>.Shared.Return(_velocityLookup);

        if (_alphaCurve != null)
            ArrayPool<float>.Shared.Return(_alphaCurve);
    }

    private void ClearReferences()
    {
        _particleBuffer = null;
        _spectrumBuffer = null;
        _velocityLookup = null;
        _alphaCurve = null;
        _font = null;
        _cachedBackground = null;
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
        public float Width { get; set; }
        public float Height { get; set; }
        public float StepSize { get; set; }
        public float OverlayHeight { get; set; }
        public float UpperBound { get; set; }
        public float LowerBound { get; set; }
        public int BarCount { get; set; }
    }
}