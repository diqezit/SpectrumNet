#nullable enable

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class TextParticlesRenderer : EffectSpectrumRenderer<TextParticlesRenderer.QualitySettings>
{
    private static readonly Lazy<TextParticlesRenderer> _instance =
        new(() => new TextParticlesRenderer());

    public static TextParticlesRenderer GetInstance() => _instance.Value;

    private const float FOCAL_LENGTH = 1000f,
        GRAVITY = 9.81f,
        AIR_RESISTANCE = 0.98f,
        MAX_VELOCITY = 15f,
        DIRECTION_VARIANCE = 0.5f,
        RANDOM_DIRECTION_CHANCE = 0.05f,
        PHYSICS_TIMESTEP = 0.016f,
        BASE_TEXT_SIZE = 14f,
        BASE_TEXT_SIZE_OVERLAY = 12f,
        ALPHA_MAX = 255f,
        MIN_ALPHA_THRESHOLD = 0.05f,
        SPAWN_VARIANCE = 5f,
        SPAWN_HALF_VARIANCE = SPAWN_VARIANCE / 2f,
        MIN_SPAWN_INTENSITY = 0.5f,
        MAX_SPAWN_INTENSITY = 2f,
        LIFE_VARIANCE_MIN = 0.8f,
        LIFE_VARIANCE_MAX = 1.2f,
        BOUNDARY_MARGIN = 50f,
        VELOCITY_RANGE_BASE = 10f,
        PARTICLE_LIFE_BASE = 3f,
        PARTICLE_LIFE_DECAY = 0.016f,
        ALPHA_DECAY_EXPONENT = 2f,
        SPAWN_THRESHOLD_OVERLAY = 0.15f,
        SPAWN_THRESHOLD_NORMAL = 0.1f,
        SPAWN_RATE_LOW = 0.3f,
        SPAWN_RATE_MEDIUM = 0.5f,
        SPAWN_RATE_HIGH = 0.7f,
        PARTICLE_SIZE_OVERLAY = 0.8f,
        PARTICLE_SIZE_NORMAL = 1f,
        VELOCITY_MULTIPLIER = 1f,
        Z_RANGE = 500f,
        MIN_Z_DEPTH = -250f,
        OVERLAY_HEIGHT_MULTIPLIER = 0.3f,
        DEPTH_BLUR_FACTOR = 2f,
        BATCH_RENDER_THRESHOLD = 10,
        SPAWN_COOLDOWN = 0.05f,
        INTENSITY_SMOOTHING = 0.85f;

    private const int VELOCITY_LOOKUP_SIZE = 1024,
        MAX_BATCH_SIZE = 1000,
        PARTICLE_POOL_SIZE = 2048,
        MAX_PARTICLES_LOW = 150,
        MAX_PARTICLES_MEDIUM = 400,
        MAX_PARTICLES_HIGH = 800,
        ALPHA_CURVE_SIZE = 101,
        MAX_SPAWN_PER_FRAME_LOW = 2,
        MAX_SPAWN_PER_FRAME_MEDIUM = 4,
        MAX_SPAWN_PER_FRAME_HIGH = 8;

    private const string DEFAULT_CHARACTERS = "01";

    private static readonly SKColor[] _particleColors = [
        new(0, 255, 0),
        new(0, 200, 255),
        new(255, 255, 0)
    ];
    private static readonly float[] _colorThresholds = [0.33f, 0.66f, 1.0f];

    private readonly string _characters = DEFAULT_CHARACTERS;
    private readonly Random _random = new();
    private readonly ParticlePool _particlePool = new(PARTICLE_POOL_SIZE);
    private readonly Dictionary<char, SKTextBlob> _textBlobCache = [];
    private readonly RenderCache _renderCache = new();
    private readonly float[] _spawnTimers;
    private readonly float[] _smoothedIntensities;

    private Particle[] _activeParticles = new Particle[MAX_PARTICLES_HIGH];
    private int _activeParticleCount;
    private float[]? _velocityLookup;
    private float[]? _alphaCurve;
    private SKFont? _font;
    private float _animationTime;

    public TextParticlesRenderer()
    {
        _spawnTimers = new float[256];
        _smoothedIntensities = new float[256];
    }

    public sealed class QualitySettings
    {
        public bool UseBatching { get; init; }
        public bool UseAntiAlias { get; init; }
        public bool UseColorVariation { get; init; }
        public bool UseDepthBlur { get; init; }
        public bool UseTextBlobs { get; init; }
        public bool UseSimpleRendering { get; init; }
        public int CullingLevel { get; init; }
        public int MaxParticles { get; init; }
        public float ParticleDetail { get; init; }
        public float SpawnRate { get; init; }
        public int MaxSpawnPerFrame { get; init; }
    }

    protected override IReadOnlyDictionary<RenderQuality, QualitySettings>
        QualitySettingsPresets
    { get; } = new Dictionary<RenderQuality, QualitySettings>
    {
        [RenderQuality.Low] = new()
        {
            UseBatching = false,
            UseAntiAlias = false,
            UseColorVariation = false,
            UseDepthBlur = false,
            UseTextBlobs = false,
            UseSimpleRendering = true,
            CullingLevel = 2,
            MaxParticles = MAX_PARTICLES_LOW,
            ParticleDetail = 0.6f,
            SpawnRate = SPAWN_RATE_LOW,
            MaxSpawnPerFrame = MAX_SPAWN_PER_FRAME_LOW
        },
        [RenderQuality.Medium] = new()
        {
            UseBatching = true,
            UseAntiAlias = true,
            UseColorVariation = true,
            UseDepthBlur = false,
            UseTextBlobs = true,
            UseSimpleRendering = false,
            CullingLevel = 1,
            MaxParticles = MAX_PARTICLES_MEDIUM,
            ParticleDetail = 0.8f,
            SpawnRate = SPAWN_RATE_MEDIUM,
            MaxSpawnPerFrame = MAX_SPAWN_PER_FRAME_MEDIUM
        },
        [RenderQuality.High] = new()
        {
            UseBatching = true,
            UseAntiAlias = true,
            UseColorVariation = true,
            UseDepthBlur = true,
            UseTextBlobs = true,
            UseSimpleRendering = false,
            CullingLevel = 0,
            MaxParticles = MAX_PARTICLES_HIGH,
            ParticleDetail = 1f,
            SpawnRate = SPAWN_RATE_HIGH,
            MaxSpawnPerFrame = MAX_SPAWN_PER_FRAME_HIGH
        }
    };

    protected override void RenderEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        RenderParameters renderParams,
        SKPaint passedInPaint)
    {
        if (CurrentQualitySettings == null || renderParams.EffectiveBarCount <= 0)
            return;

        var (isValid, processedSpectrum) = ProcessSpectrum(
            spectrum,
            renderParams.EffectiveBarCount,
            applyTemporalSmoothing: true);

        if (!isValid || processedSpectrum == null)
            return;

        var renderData = CalculateRenderData(
            processedSpectrum,
            info,
            renderParams);

        if (!ValidateRenderData(renderData))
            return;

        RenderVisualization(
            canvas,
            renderData,
            renderParams,
            passedInPaint);
    }

    private RenderData CalculateRenderData(
        float[] spectrum,
        SKImageInfo info,
        RenderParameters renderParams)
    {
        EnsureInitialized();
        UpdateRenderCache(info, renderParams);
        UpdateSmoothingData(spectrum);
        UpdateAndSpawnParticles(spectrum, renderParams);

        return new RenderData(
            ActiveParticleCount: _activeParticleCount,
            CacheData: _renderCache,
            AverageIntensity: CalculateAverageIntensity(spectrum));
    }

    private void UpdateSmoothingData(float[] spectrum)
    {
        for (int i = 0; i < spectrum.Length && i < _smoothedIntensities.Length; i++)
        {
            _smoothedIntensities[i] = Lerp(
                _smoothedIntensities[i],
                spectrum[i],
                1f - INTENSITY_SMOOTHING);
        }
    }

    private static bool ValidateRenderData(RenderData data) =>
        data.ActiveParticleCount > 0 || data.AverageIntensity > 0;

    private void RenderVisualization(
        SKCanvas canvas,
        RenderData data,
        RenderParameters renderParams,
        SKPaint basePaint)
    {
        var settings = CurrentQualitySettings!;

        RenderWithOverlay(canvas, () =>
        {
            if (settings.UseSimpleRendering)
                RenderParticlesSimple(canvas, data, settings);
            else if (settings.UseTextBlobs && _activeParticleCount > BATCH_RENDER_THRESHOLD)
                RenderParticlesWithTextBlobs(canvas, data, settings);
            else
                RenderParticlesBatched(canvas, data, settings);
        });
    }

    private void EnsureInitialized()
    {
        if (_font == null)
            InitializeComponents();
    }

    private void InitializeComponents()
    {
        InitializeFont();
        InitializeLookupTables();
        InitializeSpawnTimers();
    }

    private void InitializeFont()
    {
        var settings = CurrentQualitySettings!;
        float fontSize = IsOverlayActive ? BASE_TEXT_SIZE_OVERLAY : BASE_TEXT_SIZE;

        _font = new SKFont
        {
            Size = fontSize * settings.ParticleDetail,
            Edging = settings.UseAntiAlias ? SKFontEdging.SubpixelAntialias : SKFontEdging.Alias
        };
    }

    private void InitializeLookupTables()
    {
        InitializeAlphaCurve();
        InitializeVelocityLookup();
    }

    private void InitializeAlphaCurve()
    {
        _alphaCurve = new float[ALPHA_CURVE_SIZE];
        float step = 1f / (_alphaCurve.Length - 1);

        for (int i = 0; i < _alphaCurve.Length; i++)
            _alphaCurve[i] = MathF.Pow(i * step, ALPHA_DECAY_EXPONENT);
    }

    private void InitializeVelocityLookup()
    {
        _velocityLookup = new float[VELOCITY_LOOKUP_SIZE];
        float minVelocity = 5f;

        for (int i = 0; i < _velocityLookup.Length; i++)
            _velocityLookup[i] = minVelocity + VELOCITY_RANGE_BASE * i / VELOCITY_LOOKUP_SIZE;
    }

    private void InitializeSpawnTimers()
    {
        for (int i = 0; i < _spawnTimers.Length; i++)
            _spawnTimers[i] = (float)_random.NextDouble() * SPAWN_COOLDOWN;
    }

    private void UpdateRenderCache(SKImageInfo info, RenderParameters renderParams)
    {
        _renderCache.Width = info.Width;
        _renderCache.Height = info.Height;
        _renderCache.StepSize = renderParams.BarWidth + renderParams.BarSpacing;

        float overlayHeight = info.Height * OVERLAY_HEIGHT_MULTIPLIER;
        _renderCache.OverlayHeight = IsOverlayActive ? overlayHeight : 0f;
        _renderCache.UpperBound = IsOverlayActive ? info.Height - overlayHeight : 0f;
        _renderCache.LowerBound = info.Height;
        _renderCache.BarCount = renderParams.EffectiveBarCount;
    }

    private void UpdateAndSpawnParticles(float[] spectrum, RenderParameters renderParams)
    {
        _animationTime += PHYSICS_TIMESTEP;

        UpdateActiveParticles();
        SpawnNewParticlesContinuous(spectrum, renderParams);
    }

    private void UpdateActiveParticles()
    {
        int writeIndex = 0;

        for (int i = 0; i < _activeParticleCount; i++)
        {
            ref var particle = ref _activeParticles[i];

            if (!UpdateParticle(ref particle))
            {
                _particlePool.Return(particle);
                continue;
            }

            if (writeIndex != i)
                _activeParticles[writeIndex] = particle;

            writeIndex++;
        }

        _activeParticleCount = writeIndex;
    }

    private bool UpdateParticle(ref Particle p)
    {
        p.Life -= PARTICLE_LIFE_DECAY;

        if (p.Life <= 0 || IsOutOfBounds(p))
            return false;

        UpdatePhysics(ref p);
        p.Alpha = CalculateParticleAlpha(p.Life / PARTICLE_LIFE_BASE);

        return ShouldKeepParticle(p);
    }

    private bool ShouldKeepParticle(in Particle p)
    {
        var settings = CurrentQualitySettings!;

        if (settings.CullingLevel >= 2 && p.Alpha < MIN_ALPHA_THRESHOLD * 2)
            return false;

        if (settings.CullingLevel >= 1 && p.Alpha < MIN_ALPHA_THRESHOLD)
            return false;

        return true;
    }

    private bool IsOutOfBounds(in Particle p) =>
        p.Y < _renderCache.UpperBound - BOUNDARY_MARGIN ||
        p.Y > _renderCache.LowerBound + BOUNDARY_MARGIN;

    private void UpdatePhysics(ref Particle p)
    {
        p.VelocityY = UpdateVelocityY(p.VelocityY);
        p.VelocityX = UpdateVelocityX(p.VelocityX);

        p.Y += p.VelocityY;
        p.X += p.VelocityX;
    }

    private static float UpdateVelocityY(float velocityY)
    {
        float newVelocity = (velocityY + GRAVITY * PHYSICS_TIMESTEP) * AIR_RESISTANCE;
        return Clamp(newVelocity, -MAX_VELOCITY * VELOCITY_MULTIPLIER, MAX_VELOCITY * VELOCITY_MULTIPLIER);
    }

    private float UpdateVelocityX(float velocityX)
    {
        if (_random.NextDouble() < RANDOM_DIRECTION_CHANCE)
            velocityX += ((float)_random.NextDouble() - 0.5f) * DIRECTION_VARIANCE;

        return velocityX * AIR_RESISTANCE;
    }

    private float CalculateParticleAlpha(float lifeRatio)
    {
        if (lifeRatio <= 0f) return 0f;
        if (lifeRatio >= 1f) return 1f;

        if (_alphaCurve != null && _alphaCurve.Length > 0)
        {
            int index = (int)(lifeRatio * 100);
            index = Clamp(index, 0, _alphaCurve.Length - 1);
            return _alphaCurve[index];
        }

        return MathF.Pow(lifeRatio, ALPHA_DECAY_EXPONENT);
    }

    private void SpawnNewParticlesContinuous(float[] spectrum, RenderParameters renderParams)
    {
        if (spectrum.Length == 0) return;

        var settings = CurrentQualitySettings!;
        int spawnedThisFrame = 0;

        for (int i = 0; i < spectrum.Length && i < _spawnTimers.Length; i++)
        {
            if (_activeParticleCount >= settings.MaxParticles)
                break;

            if (spawnedThisFrame >= settings.MaxSpawnPerFrame)
                break;

            _spawnTimers[i] -= PHYSICS_TIMESTEP;

            if (_spawnTimers[i] <= 0 && ShouldSpawnParticleForBar(i))
            {
                SpawnParticleForBar(i, renderParams);
                spawnedThisFrame++;
                ResetSpawnTimer(i);
            }
        }
    }

    private bool ShouldSpawnParticleForBar(int index)
    {
        if (index >= _smoothedIntensities.Length) return false;

        float intensity = _smoothedIntensities[index];
        float threshold = IsOverlayActive ? SPAWN_THRESHOLD_OVERLAY : SPAWN_THRESHOLD_NORMAL;

        if (intensity <= threshold) return false;

        float spawnProbability = CalculateSpawnProbability(intensity, threshold);
        return _random.NextDouble() < spawnProbability;
    }

    private float CalculateSpawnProbability(float intensity, float threshold)
    {
        float normalizedIntensity = (intensity - threshold) / (1f - threshold);
        normalizedIntensity = Clamp(normalizedIntensity, 0f, 1f);

        return normalizedIntensity * CurrentQualitySettings!.SpawnRate;
    }

    private void SpawnParticleForBar(int index, RenderParameters renderParams)
    {
        float intensity = _smoothedIntensities[index];
        float threshold = IsOverlayActive ? SPAWN_THRESHOLD_OVERLAY : SPAWN_THRESHOLD_NORMAL;
        float normalizedIntensity = intensity / threshold;

        var particle = CreateParticle(
            index,
            _renderCache.LowerBound,
            renderParams,
            GetParticleSize(),
            normalizedIntensity);

        AddParticle(particle);
    }

    private void AddParticle(Particle particle)
    {
        if (_activeParticleCount < _activeParticles.Length)
        {
            _activeParticles[_activeParticleCount] = particle;
            _activeParticleCount++;
        }
    }

    private void ResetSpawnTimer(int index)
    {
        float baseDelay = SPAWN_COOLDOWN / CurrentQualitySettings!.SpawnRate;
        float variance = baseDelay * 0.5f;
        _spawnTimers[index] = baseDelay + (float)_random.NextDouble() * variance;
    }

    private float GetParticleSize() =>
        IsOverlayActive ? PARTICLE_SIZE_OVERLAY : PARTICLE_SIZE_NORMAL;

    private Particle CreateParticle(
        int index,
        float spawnY,
        RenderParameters renderParams,
        float baseSize,
        float intensity)
    {
        var particle = _particlePool.Rent();

        InitializeParticlePosition(ref particle, index, spawnY, renderParams);
        InitializeParticleVelocity(ref particle, intensity);
        InitializeParticleProperties(ref particle, baseSize, intensity);

        return particle;
    }

    private void InitializeParticlePosition(
        ref Particle particle,
        int index,
        float spawnY,
        RenderParameters renderParams)
    {
        particle.X = renderParams.StartOffset + index * _renderCache.StepSize +
                    (float)_random.NextDouble() * renderParams.BarWidth;
        particle.Y = spawnY + (float)_random.NextDouble() * SPAWN_VARIANCE - SPAWN_HALF_VARIANCE;
        particle.Z = MIN_Z_DEPTH + (float)_random.NextDouble() * Z_RANGE;
    }

    private void InitializeParticleVelocity(ref Particle particle, float intensity)
    {
        particle.VelocityY = -GetRandomVelocity() * Clamp(intensity, MIN_SPAWN_INTENSITY, MAX_SPAWN_INTENSITY);
        particle.VelocityX = ((float)_random.NextDouble() - 0.5f) * 2f;
    }

    private void InitializeParticleProperties(ref Particle particle, float baseSize, float intensity)
    {
        float lifeVariance = LIFE_VARIANCE_MIN +
                           (float)_random.NextDouble() * (LIFE_VARIANCE_MAX - LIFE_VARIANCE_MIN);

        particle.Size = baseSize * Clamp(intensity, MIN_SPAWN_INTENSITY, MAX_SPAWN_INTENSITY) *
                       CurrentQualitySettings!.ParticleDetail;
        particle.Life = PARTICLE_LIFE_BASE * lifeVariance;
        particle.Alpha = 1f;
        particle.Character = _characters[_random.Next(_characters.Length)];
        particle.Color = CurrentQualitySettings!.UseColorVariation
            ? GetParticleColor(intensity)
            : _particleColors[0];
    }

    private static SKColor GetParticleColor(float intensity)
    {
        for (int i = 0; i < _colorThresholds.Length; i++)
            if (intensity <= _colorThresholds[i])
                return _particleColors[i];

        return _particleColors[^1];
    }

    private float GetRandomVelocity()
    {
        if (_velocityLookup == null || _velocityLookup.Length == 0)
            return 5f;
        return _velocityLookup[_random.Next(VELOCITY_LOOKUP_SIZE)];
    }

    private void RenderParticlesSimple(
        SKCanvas canvas,
        RenderData data,
        QualitySettings settings)
    {
        if (_font == null || data.ActiveParticleCount == 0) return;

        var center = CalculateScreenCenter(data.CacheData);
        var paint = CreatePaint(SKColors.White, SKPaintStyle.Fill);

        try
        {
            for (int i = 0; i < data.ActiveParticleCount; i++)
            {
                ref var particle = ref _activeParticles[i];

                if (!ShouldRenderParticle(in particle, settings))
                    continue;

                RenderSimpleParticle(canvas, in particle, center, paint);
            }
        }
        finally
        {
            ReturnPaint(paint);
        }
    }

    private void RenderSimpleParticle(
        SKCanvas canvas,
        in Particle particle,
        SKPoint center,
        SKPaint paint)
    {
        var (screenPos, alpha, scale) = CalculateParticleScreenData(in particle, center);

        if (!IsAreaVisible(canvas, new SKRect(
            screenPos.X - particle.Size,
            screenPos.Y - particle.Size,
            screenPos.X + particle.Size,
            screenPos.Y + particle.Size)))
            return;

        paint.Color = particle.Color.WithAlpha(alpha);

        if (_font != null)
        {
            _font.Size = BASE_TEXT_SIZE * scale * CurrentQualitySettings!.ParticleDetail;

            canvas.DrawText(
                particle.Character.ToString(),
                screenPos.X,
                screenPos.Y,
                SKTextAlign.Center,
                _font,
                paint);
        }
    }

    private void RenderParticlesWithTextBlobs(
        SKCanvas canvas,
        RenderData data,
        QualitySettings settings)
    {
        if (_font == null || data.ActiveParticleCount == 0) return;

        var center = CalculateScreenCenter(data.CacheData);
        var characterGroups = new Dictionary<(char, int), List<ParticleRenderInfo>>();

        PrepareCharacterGroups(data, center, characterGroups, settings);
        RenderCharacterGroups(canvas, characterGroups, settings);
    }

    private static SKPoint CalculateScreenCenter(RenderCache cache) =>
        new(cache.Width / 2f, cache.Height / 2f);

    private void PrepareCharacterGroups(
        RenderData data,
        SKPoint center,
        Dictionary<(char, int), List<ParticleRenderInfo>> groups,
        QualitySettings settings)
    {
        for (int i = 0; i < data.ActiveParticleCount; i++)
        {
            ref var particle = ref _activeParticles[i];

            if (!ShouldRenderParticle(in particle, settings))
                continue;

            var renderInfo = CreateParticleRenderInfo(in particle, center);

            if (!IsParticleVisible(renderInfo, particle.Size))
                continue;

            AddToCharacterGroup(groups, particle.Character, renderInfo);
        }
    }

    private static ParticleRenderInfo CreateParticleRenderInfo(in Particle particle, SKPoint center)
    {
        var (screenPos, alpha, scale) = CalculateParticleScreenData(in particle, center);

        return new ParticleRenderInfo(
            Position: screenPos,
            Alpha: alpha,
            Scale: scale,
            Color: particle.Color,
            Depth: particle.Z);
    }

    private static bool IsParticleVisible(ParticleRenderInfo info, float size)
    {
        var bounds = new SKRect(
            info.Position.X - size,
            info.Position.Y - size,
            info.Position.X + size,
            info.Position.Y + size);

        return IsAreaVisible(null, bounds);
    }

    private static void AddToCharacterGroup(
        Dictionary<(char, int), List<ParticleRenderInfo>> groups,
        char character,
        ParticleRenderInfo info)
    {
        int sizeKey = (int)(info.Scale * 10);
        var key = (character, sizeKey);

        if (!groups.TryGetValue(key, out var list))
        {
            list = new List<ParticleRenderInfo>(MAX_BATCH_SIZE);
            groups[key] = list;
        }

        list.Add(info);
    }

    private void RenderCharacterGroups(
        SKCanvas canvas,
        Dictionary<(char, int), List<ParticleRenderInfo>> groups,
        QualitySettings settings)
    {
        if (_font == null) return;

        foreach (var ((character, sizeKey), renderInfos) in groups)
        {
            if (renderInfos.Count == 0) continue;

            RenderCharacterGroup(canvas, character, sizeKey, renderInfos, settings);
        }
    }

    private void RenderCharacterGroup(
        SKCanvas canvas,
        char character,
        int sizeKey,
        List<ParticleRenderInfo> renderInfos,
        QualitySettings settings)
    {
        float baseScale = sizeKey / 10f;
        _font!.Size = BASE_TEXT_SIZE * baseScale * settings.ParticleDetail;

        var textBlob = GetOrCreateTextBlob(character, _font);
        if (textBlob == null) return;

        foreach (var info in renderInfos)
            RenderTextBlob(canvas, textBlob, info, settings);
    }

    private void RenderTextBlob(
        SKCanvas canvas,
        SKTextBlob textBlob,
        ParticleRenderInfo info,
        QualitySettings settings)
    {
        var paint = CreatePaint(info.Color.WithAlpha(info.Alpha), SKPaintStyle.Fill);

        try
        {
            ApplyDepthBlur(paint, info.Depth, settings);
            canvas.DrawText(textBlob, info.Position.X, info.Position.Y, paint);
        }
        finally
        {
            ReturnPaint(paint);
        }
    }

    private static void ApplyDepthBlur(SKPaint paint, float depth, QualitySettings settings)
    {
        if (settings.UseDepthBlur && depth > 0)
        {
            using var blurFilter = SKMaskFilter.CreateBlur(
                SKBlurStyle.Normal,
                depth / Z_RANGE * DEPTH_BLUR_FACTOR);
            paint.MaskFilter = blurFilter;
        }
    }

    private SKTextBlob? GetOrCreateTextBlob(char character, SKFont font)
    {
        if (!_textBlobCache.TryGetValue(character, out var blob))
        {
            blob = SKTextBlob.Create(character.ToString(), font);
            if (blob != null)
                _textBlobCache[character] = blob;
        }
        return blob;
    }

    private void RenderParticlesBatched(
        SKCanvas canvas,
        RenderData data,
        QualitySettings settings)
    {
        if (_font == null || data.ActiveParticleCount == 0) return;

        var center = CalculateScreenCenter(data.CacheData);

        for (int i = 0; i < data.ActiveParticleCount; i++)
        {
            ref var particle = ref _activeParticles[i];

            if (ShouldRenderParticle(in particle, settings))
                RenderSingleParticle(canvas, in particle, center, settings);
        }
    }

    private void RenderSingleParticle(
        SKCanvas canvas,
        in Particle particle,
        SKPoint center,
        QualitySettings settings)
    {
        var renderInfo = CreateParticleRenderInfo(in particle, center);

        if (!IsParticleVisible(renderInfo, particle.Size))
            return;

        RenderParticleText(canvas, particle.Character.ToString(), renderInfo, settings);
    }

    private void RenderParticleText(
        SKCanvas canvas,
        string text,
        ParticleRenderInfo info,
        QualitySettings settings)
    {
        var paint = CreatePaint(info.Color.WithAlpha(info.Alpha), SKPaintStyle.Fill);

        try
        {
            if (_font != null)
            {
                _font.Size = BASE_TEXT_SIZE * info.Scale * settings.ParticleDetail;
                ApplyDepthBlur(paint, info.Depth, settings);

                canvas.DrawText(
                    text,
                    info.Position.X,
                    info.Position.Y,
                    SKTextAlign.Center,
                    _font,
                    paint);
            }
        }
        finally
        {
            ReturnPaint(paint);
        }
    }

    private static (SKPoint position, byte alpha, float scale) CalculateParticleScreenData(
        in Particle particle,
        SKPoint center)
    {
        float depth = FOCAL_LENGTH + particle.Z;
        float scale = FOCAL_LENGTH / depth;

        var screenPos = new SKPoint(
            center.X + (particle.X - center.X) * scale,
            center.Y + (particle.Y - center.Y) * scale);

        float depthAlpha = depth / (FOCAL_LENGTH + Z_RANGE);
        byte alpha = (byte)(particle.Alpha * depthAlpha * ALPHA_MAX);

        return (screenPos, alpha, scale);
    }

    private static bool ShouldRenderParticle(in Particle p, QualitySettings settings)
    {
        if (p.Alpha < MIN_ALPHA_THRESHOLD)
            return false;

        if (settings.CullingLevel > 0 && p.Life < PARTICLE_LIFE_BASE * 0.2f)
            return false;

        if (settings.CullingLevel > 1 && p.Life < PARTICLE_LIFE_BASE * 0.1f)
            return false;

        return true;
    }

    private static float CalculateAverageIntensity(float[] spectrum)
    {
        if (spectrum.Length == 0) return 0f;

        float sum = 0f;
        foreach (float value in spectrum)
            sum += value;

        return sum / spectrum.Length;
    }

    protected override int GetMaxBarsForQuality() => Quality switch
    {
        RenderQuality.Low => 64,
        RenderQuality.Medium => 128,
        RenderQuality.High => 256,
        _ => 128
    };

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();

        float smoothingFactor = Quality switch
        {
            RenderQuality.Low => 0.4f,
            RenderQuality.Medium => 0.3f,
            RenderQuality.High => 0.25f,
            _ => 0.3f
        };

        if (IsOverlayActive)
            smoothingFactor *= 1.2f;

        SetProcessingSmoothingFactor(smoothingFactor);

        ResetParticleSystem();
        ClearCaches();

        RequestRedraw();
    }

    private void ResetParticleSystem()
    {
        _activeParticleCount = 0;
        _animationTime = 0f;

        Array.Clear(_spawnTimers);
        Array.Clear(_smoothedIntensities);
        InitializeSpawnTimers();
    }

    private void ClearCaches()
    {
        foreach (var blob in _textBlobCache.Values)
            blob?.Dispose();
        _textBlobCache.Clear();

        _font?.Dispose();
        _font = null;
    }

    protected override void CleanupUnusedResources()
    {
        base.CleanupUnusedResources();

        CleanupInactiveParticles();
        CleanupTextBlobCache();
    }

    private void CleanupInactiveParticles()
    {
        if (_activeParticleCount == 0)
            return;

        int inactiveCount = 0;
        for (int i = 0; i < _activeParticleCount; i++)
            if (_activeParticles[i].Alpha < MIN_ALPHA_THRESHOLD)
                inactiveCount++;

        if (inactiveCount > _activeParticleCount / 2)
            CompactParticleArray();
    }

    private void CompactParticleArray()
    {
        int writeIndex = 0;

        for (int i = 0; i < _activeParticleCount; i++)
        {
            ref var particle = ref _activeParticles[i];
            if (particle.Alpha >= MIN_ALPHA_THRESHOLD)
            {
                if (writeIndex != i)
                    _activeParticles[writeIndex] = particle;
                writeIndex++;
            }
        }

        _activeParticleCount = writeIndex;
    }

    private void CleanupTextBlobCache()
    {
        if (_textBlobCache.Count > _characters.Length * 2)
        {
            foreach (var blob in _textBlobCache.Values)
                blob?.Dispose();
            _textBlobCache.Clear();
        }
    }

    protected override void OnDispose()
    {
        _velocityLookup = null;
        _alphaCurve = null;
        _activeParticles = null!;
        _activeParticleCount = 0;
        _animationTime = 0f;

        ClearCaches();

        base.OnDispose();
    }

    private struct Particle
    {
        public float X, Y, Z;
        public float VelocityX, VelocityY;
        public float Size, Life, Alpha;
        public char Character;
        public SKColor Color;
    }

    private sealed class ParticlePool(int maxSize)
    {
        private readonly Stack<Particle> _pool = new(maxSize);
        private readonly int _maxSize = maxSize;

        public Particle Rent()
        {
            if (_pool.Count > 0)
                return _pool.Pop();

            return new Particle();
        }

        public void Return(Particle particle)
        {
            if (_pool.Count < _maxSize)
            {
                particle.Life = 0;
                particle.Alpha = 0;
                _pool.Push(particle);
            }
        }
    }

    private sealed class RenderCache
    {
        public float Width { get; set; }
        public float Height { get; set; }
        public float StepSize { get; set; }
        public float OverlayHeight { get; set; }
        public float UpperBound { get; set; }
        public float LowerBound { get; set; }
        public int BarCount { get; set; }
    }

    private record RenderData(
        int ActiveParticleCount,
        RenderCache CacheData,
        float AverageIntensity);

    private record ParticleRenderInfo(
        SKPoint Position,
        byte Alpha,
        float Scale,
        SKColor Color,
        float Depth);
}