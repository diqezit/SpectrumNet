#nullable enable

namespace SpectrumNet.Views;

public static class RendererFactory
{
    private const string LogPrefix = "SpectrumRendererFactory";

    private static readonly object _lock = new();
    private static readonly Dictionary<RenderStyle, ISpectrumRenderer> _rendererCache = [];
    private static readonly HashSet<RenderStyle> _initializedRenderers = [];
    private static RenderQuality _globalQuality;

    public static RenderQuality GlobalQuality
    {
        get => _globalQuality;
        set
        {
            if (_globalQuality == value) return;

            var oldQuality = _globalQuality;
            _globalQuality = value;

            ApplyGlobalQualityToRenderers();
            LogQualityChange(oldQuality, _globalQuality);
        }
    }

    public static ISpectrumRenderer CreateRenderer(
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

    public static IEnumerable<ISpectrumRenderer> GetAllRenderers()
    {
        lock (_lock)
            return [.. _rendererCache.Values];
    }

    public static void ConfigureAllRenderers(
        bool? isOverlayActive,
        RenderQuality? quality = null)
    {
        lock (_lock)
        {
            foreach (var renderer in _rendererCache.Values)
            {
                ConfigureRendererSafe(
                    renderer,
                    isOverlayActive,
                    quality);
            }

            LogRendererConfiguration();
        }
    }

    private static ISpectrumRenderer GetCachedOrCreateRenderer(
        RenderStyle style,
        CancellationToken cancellationToken)
    {
        if (_rendererCache.TryGetValue(style, out var cachedRenderer))
        {
            return cachedRenderer;
        }

        lock (_lock)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_rendererCache.TryGetValue(style, out cachedRenderer))
            {
                return cachedRenderer;
            }

            return CreateAndCacheRenderer(style);
        }
    }

    private static ISpectrumRenderer CreateAndCacheRenderer(RenderStyle style)
    {
        var createdRenderer = CreateAndInitializeRenderer(style);
        _rendererCache[style] = createdRenderer;

        LogRendererCreation(style);

        return createdRenderer;
    }

    private static ISpectrumRenderer CreateAndInitializeRenderer(RenderStyle style)
    {
        ISpectrumRenderer renderer;

        try
        {
            renderer = GetRendererInstance(style);
            LogRendererInstanceCreation(style);

            EnsureInitialized(style, renderer);

            return renderer;
        }
        catch (ArgumentException argEx)
        {
            LogRendererCreationError(style, "Argument error", argEx);
            throw;
        }
        catch (InvalidOperationException ioEx)
        {
            LogRendererCreationError(style, "Operation error", ioEx);
            throw;
        }
        catch (Exception unexpectedEx)
        {
            LogRendererCreationError(style, "Unexpected error", unexpectedEx);
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

    private static void EnsureInitialized(RenderStyle style, ISpectrumRenderer renderer)
    {
        if (!_initializedRenderers.Contains(style))
        {
            InitializeRenderer(style, renderer);
        }
        else
        {
            LogRendererAlreadyInitialized(style);
        }
    }

    private static void InitializeRenderer(RenderStyle style, ISpectrumRenderer renderer)
    {
        ExecuteSafely(
            () => renderer.Initialize(),
            "EnsureInitialized",
            $"Initialization error for style {style}"
        );

        _initializedRenderers.Add(style);
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
        ExecuteSafely(
            () => ConfigureRenderer(renderer, isOverlayActive, quality),
            "ConfigureRendererSafe",
            "Error configuring renderer",
            reThrowException: false
        );
    }

    private static void ConfigureRenderer(
        ISpectrumRenderer renderer,
        bool? isOverlayActive,
        RenderQuality? quality)
    {
        if (isOverlayActive.HasValue && quality.HasValue)
        {
            ConfigureOverlayAndQuality(renderer, isOverlayActive.Value, quality.Value);
        }
        else if (isOverlayActive.HasValue)
        {
            ConfigureOverlayOnly(renderer, isOverlayActive.Value);
        }
        else if (quality.HasValue)
        {
            ConfigureQualityOnly(renderer, quality.Value);
        }
    }

    private static void ConfigureOverlayAndQuality(
        ISpectrumRenderer renderer,
        bool isOverlayActive,
        RenderQuality quality)
    {
        renderer.Configure(isOverlayActive, quality);
    }

    private static void ConfigureOverlayOnly(
        ISpectrumRenderer renderer,
        bool isOverlayActive) =>
        renderer.Configure(isOverlayActive, renderer.Quality);

    private static void ConfigureQualityOnly(
        ISpectrumRenderer renderer,
        RenderQuality quality) =>
        renderer.Configure(renderer.IsOverlayActive, quality);

    private static void ApplyGlobalQualityToRenderers()
    {
        ExecuteSafely(
            () => ConfigureAllRenderers(null, _globalQuality),
            "GlobalQualitySetter",
            $"Failed to apply global quality {_globalQuality}"
        );
    }

    private static void LogQualityChange(RenderQuality oldQuality, RenderQuality newQuality)
    {
        Log(LogLevel.Information,
            LogPrefix,
            $"Global quality changed from {oldQuality} to {newQuality}",
            forceLog: true);
    }

    private static void LogRendererCreation(RenderStyle style)
    {
        Log(LogLevel.Information,
            LogPrefix,
            $"Created and initialized renderer for style {style}",
            forceLog: true);
    }

    private static void LogRendererInstanceCreation(RenderStyle style)
    {
        Log(LogLevel.Debug,
            LogPrefix,
            $"Instance created for style {style}",
            forceLog: true);
    }

    private static void LogRendererInitialized(RenderStyle style)
    {
        Log(LogLevel.Information,
            LogPrefix,
            $"Initialized renderer for style {style}",
            forceLog: true);
    }

    private static void LogRendererAlreadyInitialized(RenderStyle style)
    {
        Log(LogLevel.Debug,
            LogPrefix,
            $"Renderer for style {style} already initialized",
            forceLog: true);
    }

    private static void LogRendererConfigured(bool isOverlayActive, RenderQuality quality)
    {
        Log(LogLevel.Debug,
            LogPrefix,
            $"Configured renderer to overlay={isOverlayActive}, quality={quality}",
            forceLog: false);
    }

    private static void LogRendererConfiguration()
    {
        Log(LogLevel.Information,
            LogPrefix,
            "Configured all cached renderers",
            forceLog: true);
    }

    private static void LogRendererCreationError(RenderStyle style, string errorType, Exception ex)
    {
        Log(LogLevel.Error,
            LogPrefix,
            $"{errorType} during creation/initialization of renderer for style {style}: {ex.Message}",
            forceLog: true);
    }

    private class ErrorHandlingOptions
    {
        public string Source { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public bool ReThrowException { get; set; } = true;
    }

    private static void Safe(
        Action action,
        ErrorHandlingOptions options)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error,
                options.Source,
                $"{options.ErrorMessage}: {ex.Message}",
                forceLog: true);

            if (options.ReThrowException)
            {
                throw;
            }
        }
    }

    private static void ExecuteSafely(
        Action action,
        string source,
        string errorMessage,
        bool reThrowException = true)
    {
        Safe(
            action,
            new ErrorHandlingOptions
            {
                Source = $"{LogPrefix}.{source}",
                ErrorMessage = errorMessage,
                ReThrowException = reThrowException
            });
    }
}