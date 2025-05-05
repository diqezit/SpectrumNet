#nullable enable

namespace SpectrumNet.Views;

public static class RendererFactory
{
    private const string LogPrefix = "SpectrumRendererFactory";
    private static readonly object _lock = new();
    private static readonly Dictionary<RenderStyle, ISpectrumRenderer> _rendererCache = [];
    private static readonly HashSet<RenderStyle> _initializedRenderers = [];
    private static RenderQuality _globalQuality = RenderQuality.Medium;

    public static RenderQuality GlobalQuality
    {
        get => _globalQuality;
        set
        {
            if (_globalQuality == value) return;

            var oldQuality = _globalQuality;
            _globalQuality = value;

            ExecuteSafely(
                () => ConfigureAllRenderers(null, _globalQuality),
                "GlobalQualitySetter",
                $"Failed to apply global quality {_globalQuality}"
            );

            Log(
                LogLevel.Information,
                LogPrefix,
                $"Global quality changed from {oldQuality} to {_globalQuality}",
                forceLog: true);
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

        if (_rendererCache.TryGetValue(style, out var cachedRenderer))
        {
            return EnsureConfigured(cachedRenderer, isOverlayActive, actualQuality);
        }

        lock (_lock)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_rendererCache.TryGetValue(style, out cachedRenderer))
            {
                return EnsureConfigured(cachedRenderer, isOverlayActive, actualQuality);
            }

            var createdRenderer = CreateAndInitializeRenderer(style);
            _rendererCache[style] = createdRenderer;
            Log(
                LogLevel.Information,
                LogPrefix,
                $"Created and initialized renderer for style {style}",
                forceLog: true);

            return EnsureConfigured(createdRenderer, isOverlayActive, actualQuality);
        }
    }

    private static ISpectrumRenderer EnsureConfigured(
        ISpectrumRenderer renderer,
        bool isOverlayActive,
        RenderQuality quality)
    {
        if (renderer.IsOverlayActive != isOverlayActive || renderer.Quality != quality)
        {
            ConfigureRendererSafe(renderer, isOverlayActive, quality);
            Log(LogLevel.Debug,
                LogPrefix,
                $"Configured renderer to overlay={isOverlayActive}, quality={quality}",
                forceLog: false);
        }
        return renderer;
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
            Log(LogLevel.Information,
                LogPrefix,
                "Configured all cached renderers",
                forceLog: true);
        }
    }

    private static ISpectrumRenderer CreateAndInitializeRenderer(
        RenderStyle style)
    {
        ISpectrumRenderer renderer;
        try
        {
            renderer = GetRendererInstance(style);
            Log(LogLevel.Debug,
                LogPrefix,
                $"Instance created for style {style}",
                forceLog: true);

            EnsureInitialized(style, renderer);

            return renderer;
        }
        catch (ArgumentException argEx)
        {
            Log(LogLevel.Error,
                LogPrefix,
                $"Argument error during creation/initialization of renderer for style {style}: {argEx.Message}",
                forceLog: true);
            throw;
        }
        catch (InvalidOperationException ioEx)
        {
            Log(LogLevel.Error,
                LogPrefix,
                $"Operation error during creation/initialization of renderer for style {style}: {ioEx.Message}",
                forceLog: true);
            throw;
        }
        catch (Exception unexpectedEx)
        {
            Log(LogLevel.Error,
                LogPrefix,
                $"Unexpected error during creation/initialization of renderer for style {style}: {unexpectedEx.Message}",
                forceLog: true);
            throw;
        }
    }

    private static void EnsureInitialized(
        RenderStyle style,
        ISpectrumRenderer renderer)
    {
        if (!_initializedRenderers.Contains(style))
        {
            ExecuteSafely(
                () => renderer.Initialize(),
                "EnsureInitialized",
                $"Initialization error for style {style}"
            );
            _initializedRenderers.Add(style);
            Log(LogLevel.Information,
                LogPrefix,
                $"Initialized renderer for style {style}",
                forceLog: true);
        }
        else
        {
            Log(LogLevel.Debug,
                LogPrefix,
                $"Renderer for style {style} already initialized",
                forceLog: true);
        }
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
            renderer.Configure(isOverlayActive.Value, quality.Value);
        }
        else if (isOverlayActive.HasValue)
        {
            renderer.Configure(isOverlayActive.Value, renderer.Quality);
        }
        else if (quality.HasValue)
        {
            renderer.Configure(renderer.IsOverlayActive, quality.Value);
        }
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