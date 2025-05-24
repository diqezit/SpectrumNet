#nullable enable

using static SpectrumNet.SN.Visualization.Renderers.TextParticlesRenderer.Constants;
using static System.MathF;

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class TextParticlesRenderer : EffectSpectrumRenderer
{
    private const string LogPrefix = nameof(TextParticlesRenderer);

    private static readonly Lazy<TextParticlesRenderer> _instance =
        new(() => new TextParticlesRenderer());

    private TextParticlesRenderer() { }

    public static TextParticlesRenderer GetInstance() => _instance.Value;

    public static class Constants
    {
        public const float
            FOCAL_LENGTH = 1000f,
            GRAVITY = 9.81f,
            AIR_RESISTANCE = 0.98f,
            MAX_VELOCITY = 15f,
            DIRECTION_VARIANCE = 0.5f,
            RANDOM_DIRECTION_CHANCE = 0.05f,
            PHYSICS_TIMESTEP = 0.016f,
            BASE_TEXT_SIZE = 12f,
            ALPHA_MAX = 255f,
            MIN_ALPHA_THRESHOLD = 0.05f,
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

        public static readonly Dictionary<RenderQuality, QualitySettings> QualityPresets = new()
        {
            [RenderQuality.Low] = new(
                UseBatching: false,
                UseAntiAlias: false,
                CullingLevel: 2,
                FilterMode: SKFilterMode.Nearest,
                MipmapMode: SKMipmapMode.None
            ),
            [RenderQuality.Medium] = new(
                UseBatching: true,
                UseAntiAlias: true,
                CullingLevel: 1,
                FilterMode: SKFilterMode.Linear,
                MipmapMode: SKMipmapMode.Linear
            ),
            [RenderQuality.High] = new(
                UseBatching: true,
                UseAntiAlias: true,
                CullingLevel: 0,
                FilterMode: SKFilterMode.Linear,
                MipmapMode: SKMipmapMode.Linear
            )
        };

        public record QualitySettings(
            bool UseBatching,
            bool UseAntiAlias,
            int CullingLevel,
            SKFilterMode FilterMode,
            SKMipmapMode MipmapMode
        );
    }

    private readonly string _characters = DEFAULT_CHARACTERS;
    private readonly Random _random = new();
    private readonly SemaphoreSlim _renderSemaphore = new(1, 1);
    private readonly object _particleLock = new();
    private readonly RenderCache _renderCache = new();
    private readonly ISettings _settings = Settings.Settings.Instance;

    private QualitySettings _currentSettings = QualityPresets[RenderQuality.Medium];
    private CircularParticleBuffer? _particleBuffer;
    private float[]? _spectrumBuffer;
    private float[]? _velocityLookup;
    private float[]? _alphaCurve;
    private SKFont? _font;
    private SKPicture? _cachedBackground;

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

    protected override void OnInitialize()
    {
        InitializeComponents();
        _logger.Log(LogLevel.Debug, LogPrefix, "Initialized");
    }

    private void InitializeComponents()
    {
        InitializeSettings();
        InitializeBuffers();
        InitializeFont();
        InitializeLookupTables();
    }

    private void InitializeSettings()
    {
        var s = _settings;
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

    private void InitializeBuffers()
    {
        _particleBuffer = new CircularParticleBuffer(
            _settings.MaxParticles,
            _particleLife,
            _particleLifeDecay,
            _velocityMultiplier,
            this
        );
        _spectrumBuffer = ArrayPool<float>.Shared.Rent(SPECTRUM_BUFFER_SIZE);
    }

    private void InitializeFont()
    {
        _font = new SKFont
        {
            Size = BASE_TEXT_SIZE,
            Edging = _currentSettings.UseAntiAlias ? SKFontEdging.SubpixelAntialias : SKFontEdging.Alias
        };
    }

    private void InitializeLookupTables()
    {
        InitializeAlphaCurve();
        InitializeVelocityLookup();
    }

    private void InitializeAlphaCurve()
    {
        _alphaCurve = ArrayPool<float>.Shared.Rent(101);
        float step = 1f / (_alphaCurve.Length - 1);

        for (int i = 0; i < _alphaCurve.Length; i++)
            _alphaCurve[i] = Pow(i * step, _alphaDecayExponent);
    }

    private void InitializeVelocityLookup()
    {
        _velocityLookup = ArrayPool<float>.Shared.Rent(VELOCITY_LOOKUP_SIZE);
        float minVelocity = _settings.ParticleVelocityMin;

        for (int i = 0; i < _velocityLookup.Length; i++)
            _velocityLookup[i] = minVelocity + _velocityRange * i / VELOCITY_LOOKUP_SIZE;
    }

    protected override void OnQualitySettingsApplied()
    {
        _currentSettings = QualityPresets[Quality];
        UpdateFontQuality();
        InvalidateCachedBackground();
        RequestRedraw();
        _logger.Log(LogLevel.Debug, LogPrefix, $"Quality changed to {Quality}");
    }

    private void UpdateFontQuality()
    {
        if (_font != null)
            _font.Edging = _currentSettings.UseAntiAlias ? SKFontEdging.SubpixelAntialias : SKFontEdging.Alias;
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
        RenderParticleEffect(canvas, spectrum, info, barWidth, barCount, paint);
    }

    private void RenderParticleEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        int barCount,
        SKPaint paint)
    {
        if (_particleBuffer == null) return;

        int actualBarCount = (int)MathF.Min(spectrum.Length, barCount);
        UpdateRenderCache(info, actualBarCount);

        if (TryAcquireSemaphore())
        {
            try
            {
                UpdateAndSpawnParticles(spectrum, info.Width, barWidth);
            }
            finally
            {
                _renderSemaphore.Release();
            }
        }

        RenderParticles(canvas, info, paint);
    }

    private bool TryAcquireSemaphore() => _renderSemaphore.Wait(0);

    private void UpdateRenderCache(SKImageInfo info, int barCount)
    {
        _renderCache.Width = info.Width;
        _renderCache.Height = info.Height;
        _renderCache.StepSize = barCount > 0 ? info.Width / (float)barCount : 0f;

        float overlayHeight = info.Height * _settings.OverlayHeightMultiplier;
        _renderCache.OverlayHeight = IsOverlayActive ? overlayHeight : 0f;
        _renderCache.UpperBound = IsOverlayActive ? info.Height - overlayHeight : 0f;
        _renderCache.LowerBound = info.Height;
        _renderCache.BarCount = barCount;
    }

    private void UpdateAndSpawnParticles(
        float[] spectrum,
        float _,
        float barWidth)
    {
        _particleBuffer?.Update(
            _renderCache.UpperBound,
            _renderCache.LowerBound,
            _alphaDecayExponent
        );

        SpawnNewParticles(spectrum, _renderCache.LowerBound, barWidth);
    }

    private void SpawnNewParticles(
        float[] spectrum,
        float spawnY,
        float barWidth)
    {
        if (_particleBuffer == null || spectrum.Length == 0) return;

        float threshold = IsOverlayActive ? _spawnThresholdOverlay : _spawnThresholdNormal;
        float baseSize = IsOverlayActive ? _particleSizeOverlay : _particleSizeNormal;

        for (int i = 0; i < spectrum.Length; i++)
        {
            if (ShouldSpawnParticle(spectrum[i], threshold))
            {
                var particle = CreateParticle(i, spawnY, barWidth, baseSize, spectrum[i] / threshold);
                _particleBuffer.Add(particle);
            }
        }
    }

    private bool ShouldSpawnParticle(float value, float threshold)
    {
        if (value <= threshold) return false;
        float spawnChance = MathF.Min(value / threshold, MAX_SPAWN_INTENSITY) * _spawnProbability;
        return _random.NextDouble() < spawnChance;
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
        float z = _settings.MinZDepth + (float)_random.NextDouble() * _zRange;
        float lifeVariance = LIFE_VARIANCE_MIN +
            (float)_random.NextDouble() * (LIFE_VARIANCE_MAX - LIFE_VARIANCE_MIN);

        return new Particle
        {
            X = x,
            Y = y,
            Z = z,
            VelocityY = -GetRandomVelocity() * MathF.Min(intensity, MAX_SPAWN_INTENSITY),
            VelocityX = ((float)_random.NextDouble() - 0.5f) * 2f,
            Size = baseSize * MathF.Min(intensity, MAX_SPAWN_INTENSITY),
            Life = _particleLife * lifeVariance,
            Alpha = 1f,
            IsActive = true,
            Character = _characters[_random.Next(_characters.Length)]
        };
    }

    private float GetRandomVelocity()
    {
        if (_velocityLookup == null || _velocityLookup.Length == 0)
            return _settings.ParticleVelocityMin;
        return _velocityLookup[_random.Next(VELOCITY_LOOKUP_SIZE)];
    }

    private void RenderParticles(SKCanvas canvas, SKImageInfo info, SKPaint paint)
    {
        DrawCachedBackground(canvas, info);

        if (_currentSettings.UseBatching)
            RenderParticlesBatched(canvas, paint);
        else
            RenderParticlesIndividual(canvas, paint);
    }

    private void DrawCachedBackground(SKCanvas canvas, SKImageInfo info)
    {
        if (!UseAdvancedEffects || Quality == RenderQuality.Low) return;

        if (_cachedBackground == null)
            CreateCachedBackground(info);

        if (_cachedBackground != null)
            canvas.DrawPicture(_cachedBackground);
    }

    private void CreateCachedBackground(SKImageInfo info)
    {
        using var recorder = new SKPictureRecorder();
        using var recordCanvas = recorder.BeginRecording(
            new SKRect(0, 0, info.Width, info.Height)
        );
        _cachedBackground = recorder.EndRecording();
    }

    private void RenderParticlesIndividual(SKCanvas canvas, SKPaint paint)
    {
        if (_particleBuffer == null || _font == null) return;

        var particles = _particleBuffer.GetActiveParticles();
        if (particles.Count == 0) return;

        using var particlePaint = CreateStandardPaint(paint.Color);
        particlePaint.IsAntialias = _currentSettings.UseAntiAlias;

        var center = new Vector2(_renderCache.Width / 2f, _renderCache.Height / 2f);

        foreach (var particle in particles)
        {
            if (ShouldRenderParticle(particle))
                RenderSingleParticle(canvas, particle, center, particlePaint);
        }
    }

    private void RenderSingleParticle(
        SKCanvas canvas,
        Particle particle,
        Vector2 center,
        SKPaint paint)
    {
        var (screenPos, alpha) = CalculateParticleScreenData(particle, center);

        if (!IsRenderAreaVisible(canvas, screenPos.X, screenPos.Y, particle.Size, particle.Size))
            return;

        paint.Color = paint.Color.WithAlpha(alpha);
        canvas.DrawText(
            particle.Character.ToString(),
            screenPos.X,
            screenPos.Y,
            _font!,
            paint
        );
    }

    private void RenderParticlesBatched(SKCanvas canvas, SKPaint paint)
    {
        if (_particleBuffer == null || _font == null) return;

        var particles = _particleBuffer.GetActiveParticles();
        if (particles.Count == 0) return;

        using var particlePaint = CreateStandardPaint(paint.Color);
        particlePaint.IsAntialias = _currentSettings.UseAntiAlias;

        var center = new Vector2(_renderCache.Width / 2f, _renderCache.Height / 2f);
        var batches = new Dictionary<char, List<(Vector2 position, byte alpha)>>();

        PrepareBatches(particles, center, batches);
        RenderBatches(canvas, batches, particlePaint);
    }

    private void PrepareBatches(
        List<Particle> particles,
        Vector2 center,
        Dictionary<char, List<(Vector2, byte)>> batches)
    {
        foreach (var particle in particles)
        {
            if (!ShouldRenderParticle(particle)) continue;

            var (screenPos, alpha) = CalculateParticleScreenData(particle, center);

            if (!IsRenderAreaVisible(null, screenPos.X, screenPos.Y, particle.Size, particle.Size))
                continue;

            if (!batches.TryGetValue(particle.Character, out var list))
            {
                list = new List<(Vector2, byte)>(MAX_BATCH_SIZE);
                batches[particle.Character] = list;
            }

            list.Add((screenPos, alpha));
        }
    }

    private void RenderBatches(
        SKCanvas canvas,
        Dictionary<char, List<(Vector2, byte)>> batches,
        SKPaint paint)
    {
        foreach (var (character, positions) in batches)
        {
            string charStr = character.ToString();
            foreach (var (position, alpha) in positions)
            {
                paint.Color = paint.Color.WithAlpha(alpha);
                canvas.DrawText(charStr, position.X, position.Y, _font!, paint);
            }
        }
    }

    private static (Vector2 position, byte alpha) CalculateParticleScreenData(
        Particle particle,
        Vector2 center)
    {
        float depth = FOCAL_LENGTH + particle.Z;
        float scale = FOCAL_LENGTH / depth;

        Vector2 position = new(particle.X, particle.Y);
        Vector2 screenPos = center + (position - center) * scale;

        byte alpha = (byte)(particle.Alpha * (depth / (FOCAL_LENGTH + particle.Z)) * ALPHA_MAX);

        return (screenPos, alpha);
    }

    private bool ShouldRenderParticle(Particle p)
    {
        if (!p.IsActive) return false;

        if (_currentSettings.CullingLevel > 0 && p.Alpha < MIN_ALPHA_THRESHOLD)
            return false;

        if (_currentSettings.CullingLevel > 1 &&
            (p.Y < _renderCache.UpperBound || p.Y > _renderCache.LowerBound))
            return false;

        return true;
    }

    protected override void OnConfigurationChanged()
    {
        UpdateParticleSizes();
        InvalidateCachedBackground();
        RequestRedraw();
    }

    private void UpdateParticleSizes()
    {
        if (_particleBuffer == null) return;

        float newSize = IsOverlayActive ? _particleSizeOverlay : _particleSizeNormal;
        float oldSize = IsOverlayActive ? _particleSizeNormal : _particleSizeOverlay;
        float sizeRatio = newSize / oldSize;

        lock (_particleLock)
        {
            var particles = _particleBuffer.GetActiveParticles();
            for (int i = 0; i < particles.Count; i++)
            {
                var particle = particles[i];
                if (particle.IsActive)
                {
                    particle.Size *= sizeRatio;
                    particles[i] = particle;
                }
            }
        }
    }

    private void InvalidateCachedBackground()
    {
        _cachedBackground?.Dispose();
        _cachedBackground = null;
    }

    protected override void CleanupUnusedResources()
    {
        if (_particleBuffer != null)
        {
            var activeCount = _particleBuffer.GetActiveParticles().Count(p => p.IsActive);
            if (activeCount == 0)
            {
                _particleBuffer.Clear();
            }
        }

        if (!UseAdvancedEffects && _cachedBackground != null)
        {
            InvalidateCachedBackground();
        }
    }

    protected override void OnDispose()
    {
        ReleaseArrayPools();
        DisposeResources();
        _logger.Log(LogLevel.Debug, LogPrefix, "Disposed");
    }

    private void ReleaseArrayPools()
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
    }

    private void DisposeResources()
    {
        _renderSemaphore?.Dispose();
        _font?.Dispose();
        _cachedBackground?.Dispose();
        _particleBuffer = null;
        _font = null;
        _cachedBackground = null;
    }

    public struct Particle
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
        private List<Particle> _particles = new(capacity);
        private int _capacity = capacity;

        public void Add(Particle particle)
        {
            if (_particles.Count >= _capacity)
            {
                _particles.RemoveAll(p => !p.IsActive);

                if (_particles.Count >= _capacity)
                    _capacity += PARTICLE_BUFFER_GROWTH;
            }

            _particles.Add(particle);
        }

        public List<Particle> GetActiveParticles() => _particles;

        public void Clear() => _particles.Clear();

        public void Update(float upperBound, float lowerBound, float alphaDecayExponent)
        {
            if (_particles.Count == 0) return;

            var activeParticles = new List<Particle>(_particles.Count);
            float lifeRatioScale = 1f / particleLife;

            foreach (var particle in _particles)
            {
                if (!particle.IsActive) continue;

                var updated = UpdateParticle(
                    particle,
                    upperBound,
                    lowerBound,
                    lifeRatioScale,
                    alphaDecayExponent
                );

                if (updated.IsActive)
                    activeParticles.Add(updated);
            }

            _particles = activeParticles;
        }

        private Particle UpdateParticle(
            Particle p,
            float upperBound,
            float lowerBound,
            float lifeRatioScale,
            float alphaDecayExponent)
        {
            p.Life -= particleLifeDecay;

            if (p.Life <= 0 || IsOutOfBounds(p, upperBound, lowerBound))
            {
                p.IsActive = false;
                return p;
            }

            UpdatePhysics(ref p);
            p.Alpha = CalculateAlpha(p.Life * lifeRatioScale, alphaDecayExponent);

            return p;
        }

        private static bool IsOutOfBounds(Particle p, float upperBound, float lowerBound) =>
            p.Y < upperBound - BOUNDARY_MARGIN || p.Y > lowerBound + BOUNDARY_MARGIN;

        private void UpdatePhysics(ref Particle p)
        {
            p.VelocityY = Clamp(
                (p.VelocityY + GRAVITY * PHYSICS_TIMESTEP) * AIR_RESISTANCE,
                -MAX_VELOCITY * velocityMultiplier,
                MAX_VELOCITY * velocityMultiplier
            );

            if (renderer._random.NextDouble() < RANDOM_DIRECTION_CHANCE)
                p.VelocityX += ((float)renderer._random.NextDouble() - 0.5f) * DIRECTION_VARIANCE;

            p.VelocityX *= AIR_RESISTANCE;
            p.Y += p.VelocityY;
            p.X += p.VelocityX;
        }

        private float CalculateAlpha(float lifeRatio, float alphaDecayExponent)
        {
            if (lifeRatio <= 0f) return 0f;
            if (lifeRatio >= 1f) return 1f;

            var alphaCurve = renderer._alphaCurve;
            if (alphaCurve != null && alphaCurve.Length > 0)
            {
                int index = (int)(lifeRatio * 100);
                if (index < alphaCurve.Length)
                    return alphaCurve[index];
            }

            return Pow(lifeRatio, alphaDecayExponent);
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