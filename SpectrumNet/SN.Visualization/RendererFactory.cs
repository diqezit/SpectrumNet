#nullable enable

namespace SpectrumNet.SN.Visualization;

public sealed class RendererFactory : IRendererFactory
{
    private const string LogPrefix = nameof(RendererFactory);

    private static readonly Lazy<RendererFactory> _instance =
        new(() => new RendererFactory(RenderQuality.Medium),
            LazyThreadSafetyMode.ExecutionAndPublication);

    public static RendererFactory Instance => _instance.Value;

    private readonly object _lock = new();
    private readonly ISmartLogger _logger = SmartLogger.Instance;

    private readonly ConcurrentDictionary<RenderStyle, ISpectrumRenderer> _rendererCache = new();
    private readonly ConcurrentDictionary<RenderStyle, bool> _initializedRenderers = new();
    private readonly ConcurrentDictionary<ISpectrumRenderer, RenderQuality> _rendererQualityState = new();

    private RenderQuality _globalQuality;

    private bool
        _isApplyingGlobalQuality,
        _suppressConfigEvents;

    private readonly RendererTransparencyManager _transparencyManager;

    private RendererFactory(RenderQuality initialQuality = RenderQuality.Medium)
    {
        _globalQuality = initialQuality;
        _transparencyManager = RendererTransparencyManager.Instance;
        _transparencyManager.SetRendererFactory(this);
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

        if (!_suppressConfigEvents
            && ShouldConfigureRenderer(renderer, isOverlayActive, actualQuality))
        {
            var oldSuppress = _suppressConfigEvents;
            try
            {
                _suppressConfigEvents = true;
                ConfigureRenderer(renderer, isOverlayActive, actualQuality);
                _rendererQualityState[renderer] = actualQuality;
            }
            finally
            {
                _suppressConfigEvents = oldSuppress;
            }
        }

        return renderer;
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
        if (_isApplyingGlobalQuality || _suppressConfigEvents)
            return;

        var oldSuppress = _suppressConfigEvents;
        try
        {
            _suppressConfigEvents = true;

            lock (_lock)
            {
                foreach (var renderer in _rendererCache.Values)
                {
                    var actualQuality = quality ?? _globalQuality;
                    var actualOverlay = isOverlayActive ?? renderer.IsOverlayActive;

                    if (ShouldConfigureRenderer(renderer, actualOverlay, actualQuality))
                    {
                        ConfigureRenderer(renderer, actualOverlay, actualQuality);
                        _rendererQualityState[renderer] = actualQuality;
                    }
                }
            }

            LogRendererConfiguration();
        }
        finally
        {
            _suppressConfigEvents = oldSuppress;
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

    private bool ShouldConfigureRenderer(
        ISpectrumRenderer renderer,
        bool isOverlayActive,
        RenderQuality quality)
    {
        if (_isApplyingGlobalQuality)
            return false;

        if (renderer.IsOverlayActive == isOverlayActive &&
            renderer.Quality == quality)
            return false;

        return true;
    }

    private void EnsureInitialized(RenderStyle style, ISpectrumRenderer renderer)
    {
        if (_initializedRenderers.TryAdd(style, true))
            InitializeRenderer(style, renderer);
        else
            LogRendererAlreadyInitialized(style);
    }

    private void InitializeRenderer(
        RenderStyle style,
        ISpectrumRenderer renderer)
    {
        _logger.Safe(() => renderer.Initialize(),
            LogPrefix,
            $"Initialization error for style {style}");

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
        RenderStyle.Waterfall => WaterfallRenderer.GetInstance(),
        RenderStyle.Waveform => WaveformRenderer.GetInstance(),
        RenderStyle.WaterRenderer => WaterRenderer.GetInstance(),
        RenderStyle.HackerTextRenderer => HackerTextRenderer.GetInstance(),
        _ => throw new ArgumentException($"Unknown render style: {style}")
    };

    private void ConfigureRenderer(
        ISpectrumRenderer renderer,
        bool? isOverlayActive,
        RenderQuality? quality)
    {
        _logger.Safe(() => HandleConfigureRenderer(renderer, isOverlayActive, quality),
            LogPrefix,
            "Error configuring renderer");
    }

    private void HandleConfigureRenderer(
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
        if (_isApplyingGlobalQuality)
            return;

        _logger.Safe(() => HandleApplyGlobalQualityToRenderers(),
            LogPrefix,
            $"Failed to apply global quality {_globalQuality}");
    }

    private void HandleApplyGlobalQualityToRenderers()
    {
        var oldSuppress = _suppressConfigEvents;
        _isApplyingGlobalQuality = true;
        try
        {
            _suppressConfigEvents = true;

            lock (_lock)
            {
                foreach (var renderer in _rendererCache.Values)
                {
                    ConfigureRenderer(renderer, renderer.IsOverlayActive, _globalQuality);
                    _rendererQualityState[renderer] = _globalQuality;
                }
            }
        }
        finally
        {
            _suppressConfigEvents = oldSuppress;
            _isApplyingGlobalQuality = false;
        }
    }

    private void LogQualityChange(RenderQuality oldQuality, RenderQuality newQuality)
    {
        _logger.Log(LogLevel.Information,
            LogPrefix,
            $"Global quality changed from {oldQuality} to {newQuality}",
            forceLog: true);
    }

    private void LogRendererInstanceCreation(RenderStyle style)
    {
        _logger.Log(LogLevel.Debug,
            LogPrefix,
            $"Instance created for style {style}",
            forceLog: true);
    }

    private void LogRendererInitialized(RenderStyle style)
    {
        _logger.Log(LogLevel.Information,
            LogPrefix,
            $"Initialized renderer for style {style}",
            forceLog: true);
    }

    private void LogRendererAlreadyInitialized(RenderStyle style)
    {
        _logger.Log(LogLevel.Debug,
            LogPrefix,
            $"Renderer for style {style} already initialized",
            forceLog: true);
    }

    private void LogRendererConfiguration()
    {
        _logger.Log(LogLevel.Information,
            LogPrefix,
            "Configured all cached renderers",
            forceLog: true);
    }

    private void LogRendererCreationError(RenderStyle style, string errorType, Exception ex)
    {
        _logger.Log(LogLevel.Error,
            LogPrefix,
            $"{errorType} during creation/initialization of renderer for style {style}: {ex.Message}",
            forceLog: true);
    }

    public void Dispose()
    {
        _logger.Safe(() => HandleDispose(),
            LogPrefix,
            "Error during RendererFactory disposal");
    }

    private void HandleDispose()
    {
        lock (_lock)
        {
            foreach (var renderer in _rendererCache.Values)
            {
                _logger.Safe(() => renderer.Dispose(),
                    renderer.GetType().Name,
                    "Error disposing renderer");
            }
            _rendererCache.Clear();
            _initializedRenderers.Clear();
            _rendererQualityState.Clear();

            if (_transparencyManager is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}