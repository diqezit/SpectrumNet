#nullable enable

namespace SpectrumNet.Views;

public class RendererFactory : IRendererFactory
{
    private const string LOG_PREFIX = "RendererFactory";

    private static readonly Lazy<RendererFactory> _instance =
        new(() => new RendererFactory(RenderQuality.Medium),
            LazyThreadSafetyMode.ExecutionAndPublication);

    public static RendererFactory Instance => _instance.Value;

    private readonly object _lock = new();

    private readonly ConcurrentDictionary<RenderStyle, ISpectrumRenderer> _rendererCache = new();
    private readonly ConcurrentDictionary<RenderStyle, bool> _initializedRenderers = new();

    private RenderQuality _globalQuality;
    private bool _isApplyingGlobalQuality;

    private RendererFactory(RenderQuality initialQuality = RenderQuality.Medium)
    {
        _globalQuality = initialQuality;
    }

    public RenderQuality GlobalQuality
    {
        get => _globalQuality;
        set
        {
            if (_globalQuality == value || _isApplyingGlobalQuality)
                return;

            var oldQuality = _globalQuality;
            _globalQuality = value;

            ApplyGlobalQualityToRenderers();
            LogQualityChange(oldQuality, _globalQuality);
        }
    }

    public ISpectrumRenderer CreateRenderer(
        RenderStyle style,
        bool isOverlayActive,
        RenderQuality? quality = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var actualQuality = quality ?? _globalQuality;
        var renderer = GetCachedOrCreateRenderer(style, cancellationToken);

        return EnsureConfigured(renderer, isOverlayActive, actualQuality);
    }

    public IEnumerable<ISpectrumRenderer> GetAllRenderers()
    {
        lock (_lock)
            return [.. _rendererCache.Values];
    }

    public void ConfigureAllRenderers(
        bool? isOverlayActive,
        RenderQuality? quality = null)
    {
        if (_isApplyingGlobalQuality) return;

        lock (_lock)
        {
            foreach (var renderer in _rendererCache.Values)
                ConfigureRendererSafe(renderer, isOverlayActive, quality);

            LogRendererConfiguration();
        }
    }

    private ISpectrumRenderer GetCachedOrCreateRenderer(
        RenderStyle style,
        CancellationToken cancellationToken)
    {
        if (_rendererCache.TryGetValue(style, out var cachedRenderer))
            return cachedRenderer;

        lock (_lock)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return _rendererCache.GetOrAdd(style, _ =>
                CreateAndInitializeRenderer(style));
        }
    }

    private ISpectrumRenderer CreateAndInitializeRenderer(RenderStyle style)
    {
        try
        {
            var renderer = GetRendererInstance(style);
            LogRendererInstanceCreation(style);

            EnsureInitialized(style, renderer);

            return renderer;
        }
        catch (Exception ex)
        {
            LogRendererCreationError(style, "Error", ex);
            throw;
        }
    }

    private static ISpectrumRenderer EnsureConfigured(
        ISpectrumRenderer renderer,
        bool isOverlayActive,
        RenderQuality quality)
    {
        if (NeedsConfiguration(renderer, isOverlayActive, quality))
        {
            ConfigureRendererSafe(renderer, isOverlayActive, quality);
            LogRendererConfigured(isOverlayActive, quality);
        }

        return renderer;
    }

    private static bool NeedsConfiguration(
        ISpectrumRenderer renderer,
        bool isOverlayActive,
        RenderQuality quality) =>
        renderer.IsOverlayActive != isOverlayActive || renderer.Quality != quality;

    private void EnsureInitialized(RenderStyle style, ISpectrumRenderer renderer)
    {
        if (_initializedRenderers.TryAdd(style, true))
            InitializeRenderer(style, renderer);
        else
            LogRendererAlreadyInitialized(style);
    }

    private static void InitializeRenderer(
        RenderStyle style,
        ISpectrumRenderer renderer)
    {
        Safe(() => renderer.Initialize(),
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.EnsureInitialized",
                ErrorMessage = $"Initialization error for style {style}"
            });

        LogRendererInitialized(style);
    }

    private static ISpectrumRenderer GetRendererInstance(RenderStyle style) => style switch
    {
        RenderStyle.AsciiDonut => AsciiDonutRenderer.GetInstance(),
        RenderStyle.Bars => BarsRenderer.GetInstance(),
        RenderStyle.CircularBars => CircularBarsRenderer.GetInstance(),
        RenderStyle.CircularWave => CircularWaveRenderer.GetInstance(),
        RenderStyle.Constellation => ConstellationRenderer.GetInstance(),
        RenderStyle.Cube => CubeRenderer.GetInstance(),
        RenderStyle.Cubes => CubesRenderer.GetInstance(),
        RenderStyle.Dots => DotsRenderer.GetInstance(),
        RenderStyle.Fire => FireRenderer.GetInstance(),
        RenderStyle.Gauge => GaugeRenderer.GetInstance(),
        RenderStyle.Glitch => GlitchRenderer.GetInstance(),
        RenderStyle.GradientWave => GradientWaveRenderer.GetInstance(),
        RenderStyle.Heartbeat => HeartbeatRenderer.GetInstance(),
        RenderStyle.Kenwood => KenwoodRenderer.GetInstance(),
        RenderStyle.LedMeter => LedMeterRenderer.GetInstance(),
        RenderStyle.Loudness => LoudnessMeterRenderer.GetInstance(),
        RenderStyle.Particles => ParticlesRenderer.GetInstance(),
        RenderStyle.Polar => PolarRenderer.GetInstance(),
        RenderStyle.Raindrops => RaindropsRenderer.GetInstance(),
        RenderStyle.Rainbow => RainbowRenderer.GetInstance(),
        RenderStyle.SphereRenderer => SphereRenderer.GetInstance(),
        RenderStyle.TextParticles => TextParticlesRenderer.GetInstance(),
        RenderStyle.Voronoi => VoronoiRenderer.GetInstance(),
        RenderStyle.Waterfall => WaterfallRenderer.GetInstance(),
        RenderStyle.Waveform => WaveformRenderer.GetInstance(),
        RenderStyle.WaterRenderer => WaterRenderer.GetInstance(),
        _ => throw new ArgumentException($"Unknown render style: {style}")
    };

    private static void ConfigureRendererSafe(
        ISpectrumRenderer renderer,
        bool? isOverlayActive,
        RenderQuality? quality)
    {
        Safe(() => ConfigureRenderer(renderer, isOverlayActive, quality),
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.ConfigureRendererSafe",
                ErrorMessage = "Error configuring renderer"
            });
    }

    private static void ConfigureRenderer(
        ISpectrumRenderer renderer,
        bool? isOverlayActive,
        RenderQuality? quality)
    {
        if (isOverlayActive.HasValue && quality.HasValue)
            ConfigureOverlayAndQuality(renderer, isOverlayActive.Value, quality.Value);
        else if (isOverlayActive.HasValue)
            ConfigureOverlayOnly(renderer, isOverlayActive.Value);
        else if (quality.HasValue)
            ConfigureQualityOnly(renderer, quality.Value);
    }

    private static void ConfigureOverlayAndQuality(
        ISpectrumRenderer renderer,
        bool isOverlayActive,
        RenderQuality quality) =>
        renderer.Configure(isOverlayActive, quality);

    private static void ConfigureOverlayOnly(
        ISpectrumRenderer renderer,
        bool isOverlayActive) =>
        renderer.Configure(isOverlayActive, renderer.Quality);

    private static void ConfigureQualityOnly(
        ISpectrumRenderer renderer,
        RenderQuality quality) =>
        renderer.Configure(renderer.IsOverlayActive, quality);

    private void ApplyGlobalQualityToRenderers()
    {
        if (_isApplyingGlobalQuality) return;

        Safe(() =>
        {
            _isApplyingGlobalQuality = true;
            try
            {
                lock (_lock)
                {
                    foreach (var renderer in _rendererCache.Values)
                        ConfigureRendererSafe(renderer, null, _globalQuality);
                }
            }
            finally
            {
                _isApplyingGlobalQuality = false;
            }
        },
        new ErrorHandlingOptions
        {
            Source = $"{LOG_PREFIX}.GlobalQualitySetter",
            ErrorMessage = $"Failed to apply global quality {_globalQuality}"
        });
    }

    private static void LogQualityChange(RenderQuality oldQuality, RenderQuality newQuality)
    {
        Log(LogLevel.Information,
            LOG_PREFIX,
            $"Global quality changed from {oldQuality} to {newQuality}",
            forceLog: true);
    }

    private static void LogRendererInstanceCreation(RenderStyle style)
    {
        Log(LogLevel.Debug,
            LOG_PREFIX,
            $"Instance created for style {style}",
            forceLog: true);
    }

    private static void LogRendererInitialized(RenderStyle style)
    {
        Log(LogLevel.Information,
            LOG_PREFIX,
            $"Initialized renderer for style {style}",
            forceLog: true);
    }

    private static void LogRendererAlreadyInitialized(RenderStyle style)
    {
        Log(LogLevel.Debug,
            LOG_PREFIX,
            $"Renderer for style {style} already initialized",
            forceLog: true);
    }

    private static void LogRendererConfigured(bool isOverlayActive, RenderQuality quality)
    {
        Log(LogLevel.Debug,
            LOG_PREFIX,
            $"Configured renderer to overlay={isOverlayActive}, quality={quality}",
            forceLog: false);
    }

    private static void LogRendererConfiguration()
    {
        Log(LogLevel.Information,
            LOG_PREFIX,
            "Configured all cached renderers",
            forceLog: true);
    }

    private static void LogRendererCreationError(RenderStyle style, string errorType, Exception ex)
    {
        Log(LogLevel.Error,
            LOG_PREFIX,
            $"{errorType} during creation/initialization of renderer for style {style}: {ex.Message}",
            forceLog: true);
    }
}