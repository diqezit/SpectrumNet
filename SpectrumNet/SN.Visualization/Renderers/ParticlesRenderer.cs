#nullable enable

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class ParticlesRenderer : EffectSpectrumRenderer<ParticlesRenderer.QualitySettings>
{
    private static readonly Lazy<ParticlesRenderer> _instance =
        new(() => new ParticlesRenderer());

    public static ParticlesRenderer GetInstance() => _instance.Value;

    private const float DEFAULT_LINE_WIDTH = 3f,
        HIGH_MAGNITUDE_THRESHOLD = 0.7f,
        OFFSET = 10f,
        BASELINE_OFFSET = 2f,
        SMOOTHING_FACTOR_NORMAL = 0.3f,
        SMOOTHING_FACTOR_OVERLAY = 0.5f,
        SIZE_DECAY_FACTOR = 0.98f,
        MIN_PARTICLE_SIZE = 1f,
        MAX_DENSITY_FACTOR = 3f,
        PARTICLE_VELOCITY_MIN = 2f,
        PARTICLE_VELOCITY_MAX = 10f,
        PARTICLE_LIFE = 3f,
        PARTICLE_LIFE_DECAY = 0.016f,
        VELOCITY_MULTIPLIER = 1f,
        SPAWN_THRESHOLD_OVERLAY = 0.15f,
        SPAWN_THRESHOLD_NORMAL = 0.1f,
        PARTICLE_SIZE_OVERLAY = 4f,
        PARTICLE_SIZE_NORMAL = 6f,
        SPAWN_PROBABILITY = 0.8f,
        ALPHA_DECAY_EXPONENT = 2f,
        OVERLAY_HEIGHT_MULTIPLIER = 0.3f,
        OVERLAY_BOUNDARY_PADDING = 10f;

    private const int
        VELOCITY_LOOKUP_SIZE = 1024,
        ALPHA_CURVE_SIZE = 101,
        MAX_PARTICLES_LOW = 300,
        MAX_PARTICLES_MEDIUM = 600,
        MAX_PARTICLES_HIGH = 1000;

    private readonly List<Particle> _particles = [];
    private readonly RenderCache _renderCache = new();
    private float[]? _velocityLookup;
    private float[]? _alphaCurve;
    private readonly Random _random = new();

    public sealed class QualitySettings
    {
        public bool UseAntiAlias { get; init; }
        public bool UseAdvancedEffects { get; init; }
        public int MaxParticles { get; init; }
        public float ParticleDetail { get; init; }
        public float SpawnRate { get; init; }
    }

    protected override IReadOnlyDictionary<RenderQuality, QualitySettings>
        QualitySettingsPresets
    { get; } = new Dictionary<RenderQuality, QualitySettings>
    {
        [RenderQuality.Low] = new()
        {
            UseAntiAlias = false,
            UseAdvancedEffects = false,
            MaxParticles = MAX_PARTICLES_LOW,
            ParticleDetail = 0.6f,
            SpawnRate = 0.5f
        },
        [RenderQuality.Medium] = new()
        {
            UseAntiAlias = true,
            UseAdvancedEffects = true,
            MaxParticles = MAX_PARTICLES_MEDIUM,
            ParticleDetail = 0.8f,
            SpawnRate = 0.7f
        },
        [RenderQuality.High] = new()
        {
            UseAntiAlias = true,
            UseAdvancedEffects = true,
            MaxParticles = MAX_PARTICLES_HIGH,
            ParticleDetail = 1f,
            SpawnRate = 1f
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
        UpdateAndSpawnParticles(spectrum, renderParams);

        return new RenderData(
            ActiveParticleCount: _particles.Count,
            CacheData: _renderCache,
            AverageIntensity: CalculateAverageIntensity(spectrum));
    }

    private void EnsureInitialized()
    {
        if (_alphaCurve == null)
            InitializeComponents();
    }

    private void InitializeComponents()
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
        float velocityRange = PARTICLE_VELOCITY_MAX - PARTICLE_VELOCITY_MIN;

        for (int i = 0; i < VELOCITY_LOOKUP_SIZE; i++)
            _velocityLookup[i] = PARTICLE_VELOCITY_MIN + velocityRange * i / VELOCITY_LOOKUP_SIZE;
    }

    private void UpdateRenderCache(SKImageInfo info, RenderParameters renderParams)
    {
        _renderCache.Width = info.Width;
        _renderCache.Height = info.Height;
        _renderCache.StepSize = renderParams.BarWidth + renderParams.BarSpacing;

        if (IsOverlayActive)
        {
            _renderCache.OverlayHeight = info.Height * OVERLAY_HEIGHT_MULTIPLIER;
            _renderCache.UpperBound = OVERLAY_BOUNDARY_PADDING;
            _renderCache.LowerBound = info.Height;
        }
        else
        {
            _renderCache.OverlayHeight = 0f;
            _renderCache.UpperBound = 0f;
            _renderCache.LowerBound = info.Height;
        }
    }

    private void UpdateAndSpawnParticles(float[] spectrum, RenderParameters renderParams)
    {
        UpdateExistingParticles();
        SpawnNewParticles(spectrum, renderParams);
    }

    private void UpdateExistingParticles()
    {
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var particle = _particles[i];

            if (!UpdateParticle(ref particle))
            {
                _particles.RemoveAt(i);
                continue;
            }

            _particles[i] = particle;
        }
    }

    private bool UpdateParticle(ref Particle p)
    {
        p.Y -= p.Velocity * VELOCITY_MULTIPLIER;
        p.Life -= PARTICLE_LIFE_DECAY;

        if (p.Life <= 0 || p.Y <= _renderCache.UpperBound)
            return false;

        p.Size *= SIZE_DECAY_FACTOR;
        if (p.Size < MIN_PARTICLE_SIZE)
            return false;

        p.Alpha = CalculateParticleAlpha(p.Life / PARTICLE_LIFE);

        return true;
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

    private void SpawnNewParticles(float[] spectrum, RenderParameters renderParams)
    {
        var settings = CurrentQualitySettings!;

        if (_particles.Count >= settings.MaxParticles)
            return;

        float threshold = IsOverlayActive ? SPAWN_THRESHOLD_OVERLAY : SPAWN_THRESHOLD_NORMAL;
        float baseSize = IsOverlayActive ? PARTICLE_SIZE_OVERLAY : PARTICLE_SIZE_NORMAL;

        for (int i = 0; i < spectrum.Length; i++)
        {
            if (_particles.Count >= settings.MaxParticles)
                break;

            if (spectrum[i] <= threshold)
                continue;

            float intensity = spectrum[i] / threshold;
            float spawnChance = Clamp(intensity, 0f, 1f) * SPAWN_PROBABILITY * settings.SpawnRate;

            if (_random.NextDouble() < spawnChance)
            {
                CreateParticle(i, renderParams, baseSize, intensity);
            }
        }
    }

    private void CreateParticle(
        int index,
        RenderParameters renderParams,
        float baseSize,
        float intensity)
    {
        float x = renderParams.StartOffset + index * _renderCache.StepSize +
                 (float)_random.NextDouble() * renderParams.BarWidth;

        float velocity = GetRandomVelocity() * Clamp(intensity, 1f, MAX_DENSITY_FACTOR);
        float size = baseSize * Clamp(intensity, 1f, MAX_DENSITY_FACTOR) * CurrentQualitySettings!.ParticleDetail;

        _particles.Add(new Particle
        {
            X = x,
            Y = _renderCache.LowerBound,
            Velocity = velocity,
            Size = size,
            Life = PARTICLE_LIFE,
            Alpha = 1f
        });
    }

    private float GetRandomVelocity()
    {
        if (_velocityLookup == null || _velocityLookup.Length == 0)
            return PARTICLE_VELOCITY_MIN;

        return _velocityLookup[_random.Next(VELOCITY_LOOKUP_SIZE)];
    }

    private static bool ValidateRenderData(RenderData data) =>
        data.ActiveParticleCount > 0 || data.AverageIntensity > 0;

    private void RenderVisualization(
        SKCanvas canvas,
        RenderData data,
        RenderParameters renderParams,
        SKPaint basePaint)
    {
        RenderWithOverlay(canvas, () =>
        {
            RenderParticlesLayer(canvas, basePaint);
        });
    }

    private void RenderParticlesLayer(SKCanvas canvas, SKPaint basePaint)
    {
        var particlePaint = CreatePaint(basePaint.Color, SKPaintStyle.Fill);

        try
        {
            foreach (var particle in _particles)
            {
                if (!IsParticleVisible(particle))
                    continue;

                particlePaint.Color = basePaint.Color.WithAlpha((byte)(particle.Alpha * 255));
                canvas.DrawCircle(particle.X, particle.Y, particle.Size / 2, particlePaint);
            }
        }
        finally
        {
            ReturnPaint(particlePaint);
        }
    }

    private bool IsParticleVisible(Particle particle) =>
        particle.Y >= _renderCache.UpperBound &&
        particle.Y <= _renderCache.LowerBound &&
        particle.Alpha > 0 &&
        particle.Size > 0 &&
        IsAreaVisible(null, new SKRect(
            particle.X - particle.Size,
            particle.Y - particle.Size,
            particle.X + particle.Size,
            particle.Y + particle.Size));

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

        _particles.Clear();

        RequestRedraw();
    }

    protected override void CleanupUnusedResources()
    {
        base.CleanupUnusedResources();

        if (_particles.Count > CurrentQualitySettings!.MaxParticles * 2)
        {
            _particles.RemoveRange(
                CurrentQualitySettings.MaxParticles,
                _particles.Count - CurrentQualitySettings.MaxParticles);
        }
    }

    protected override void OnDispose()
    {
        _particles.Clear();
        _velocityLookup = null;
        _alphaCurve = null;

        base.OnDispose();
    }

    private struct Particle
    {
        public float X, Y;
        public float Velocity;
        public float Size;
        public float Life;
        public float Alpha;
    }

    private sealed class RenderCache
    {
        public float Width { get; set; }
        public float Height { get; set; }
        public float StepSize { get; set; }
        public float OverlayHeight { get; set; }
        public float UpperBound { get; set; }
        public float LowerBound { get; set; }
    }

    private record RenderData(
        int ActiveParticleCount,
        RenderCache CacheData,
        float AverageIntensity);
}