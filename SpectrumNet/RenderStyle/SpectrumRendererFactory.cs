#nullable enable

namespace SpectrumNet
{
    /// <summary>
    /// Enumeration of spectrum rendering styles.
    /// </summary>
    public enum RenderStyle
    {
        AsciiDonut,
        Bars,
        CircularBars,
        CircularWave,
        Constellation,
        Cube,
        Cubes,
        Fire,
        Gauge,
        Glitch,
        GradientWave,
        Heartbeat,
        Kenwood,
        LedMeter,
        Loudness,
        Particles,
        Polar,
        Raindrops,
        Rainbow,
        SphereRenderer,
        TextParticles,
        Voronoi,
        Waterfall,
        Waveform
    }

    /// <summary>
    /// Configuration record for renderer settings
    /// </summary>
    public record RendererConfig(bool IsOverlayActive, RenderQuality Quality);

    /// <summary>
    /// Factory for creating spectrum renderer instances.
    /// </summary>
    public static class SpectrumRendererFactory
    {
        private const string LogPrefix = "SpectrumRendererFactory";
        private static readonly object _lock = new();
        private static readonly Dictionary<RenderStyle, ISpectrumRenderer> _rendererCache = new();
        private static readonly HashSet<RenderStyle> _initializedRenderers = new();
        private static RenderQuality _globalQuality = RenderQuality.Medium;

        /// <summary>
        /// Gets or sets the global rendering quality for all renderers.
        /// </summary>
        public static RenderQuality GlobalQuality
        {
            get => _globalQuality;
            set
            {
                if (_globalQuality == value) return;

                _globalQuality = value;
                try
                {
                    ConfigureAllRenderers(isOverlayActive: null, _globalQuality);
                }
                catch (Exception ex)
                {
                    SmartLogger.Log(LogLevel.Error, LogPrefix,
                        $"Failed to apply global quality {value}: {ex.Message}", forceLog: true);
                }

                SmartLogger.Log(LogLevel.Information, LogPrefix, $"Global quality changed to {value}", forceLog: true);
            }
        }

        /// <summary>
        /// Creates or retrieves a spectrum renderer instance of the specified style.
        /// </summary>
        /// <param name="style">Rendering style.</param>
        /// <param name="isOverlayActive">Whether overlay mode is active.</param>
        /// <param name="quality">Rendering quality level.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>Renderer instance.</returns>
        public static ISpectrumRenderer CreateRenderer(
            RenderStyle style,
            bool isOverlayActive,
            RenderQuality? quality = null,
            CancellationToken cancellationToken = default)
        {
            var actualQuality = quality ?? _globalQuality;
            ISpectrumRenderer? renderer = null;

            if (_rendererCache.TryGetValue(style, out var cachedRenderer))
            {
                try
                {
                    cachedRenderer.Configure(isOverlayActive, actualQuality);
                    return cachedRenderer;
                }
                catch (Exception ex)
                {
                    SmartLogger.Log(LogLevel.Error, LogPrefix,
                        $"Error configuring cached renderer {style}: {ex.Message}", forceLog: true);
                    return GetFallbackRenderer(style, isOverlayActive, actualQuality);
                }
            }

            lock (_lock)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (_rendererCache.TryGetValue(style, out cachedRenderer))
                {
                    try
                    {
                        cachedRenderer.Configure(isOverlayActive, actualQuality);
                        return cachedRenderer;
                    }
                    catch (Exception ex)
                    {
                        SmartLogger.Log(LogLevel.Error, LogPrefix,
                            $"Error configuring cached renderer {style} after lock: {ex.Message}", forceLog: true);
                        return GetFallbackRenderer(style, isOverlayActive, actualQuality);
                    }
                }

                try
                {
                    renderer = GetRendererInstance(style);

                    if (!_initializedRenderers.Contains(style))
                    {
                        renderer.Initialize();
                        _initializedRenderers.Add(style);
                    }

                    renderer.Configure(isOverlayActive, actualQuality);
                    _rendererCache[style] = renderer;
                    return renderer;
                }
                catch (Exception ex)
                {
                    SmartLogger.Log(LogLevel.Error, LogPrefix,
                        $"Failed to create renderer {style}: {ex.Message}", forceLog: true);
                    return GetFallbackRenderer(style, isOverlayActive, actualQuality);
                }
            }
        }

        /// <summary>
        /// Returns a collection of all cached renderers.
        /// </summary>
        /// <returns>Collection of renderers.</returns>
        public static IEnumerable<ISpectrumRenderer> GetAllRenderers()
        {
            lock (_lock) return _rendererCache.Values.ToList();
        }

        /// <summary>
        /// Returns a cached renderer for the specified style.
        /// </summary>
        /// <param name="style">Rendering style.</param>
        /// <returns>Renderer or null.</returns>
        public static ISpectrumRenderer? GetCachedRenderer(RenderStyle style)
        {
            try
            {
                lock (_lock)
                    return _rendererCache.TryGetValue(style, out var renderer) ? renderer : null;
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix,
                    $"Error retrieving cached renderer {style}: {ex.Message}", forceLog: true);
                return null;
            }
        }

        /// <summary>
        /// Configures all cached renderers for overlay mode and quality.
        /// </summary>
        /// <param name="isOverlayActive">Whether overlay mode is active (null to keep current).</param>
        /// <param name="quality">Rendering quality level (null to keep current).</param>
        public static void ConfigureAllRenderers(bool? isOverlayActive, RenderQuality? quality = null)
        {
            try
            {
                lock (_lock)
                {
                    foreach (var renderer in _rendererCache.Values)
                    {
                        ConfigureRenderer(renderer, isOverlayActive, quality);
                    }
                }
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix,
                    $"Error configuring all renderers: {ex.Message}", forceLog: true);
            }
        }

        /// <summary>
        /// Sets the quality for a specific renderer style.
        /// </summary>
        /// <param name="style">Rendering style.</param>
        /// <param name="quality">Rendering quality level.</param>
        public static void SetRendererQuality(RenderStyle style, RenderQuality quality)
        {
            try
            {
                lock (_lock)
                {
                    if (_rendererCache.TryGetValue(style, out var renderer))
                    {
                        renderer.Quality = quality;
                    }
                }
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix,
                    $"Error setting quality for renderer {style}: {ex.Message}", forceLog: true);
            }
        }

        /// <summary>
        /// Creates a renderer instance for the specified style.
        /// </summary>
        private static ISpectrumRenderer GetRendererInstance(RenderStyle style) => style switch
        {
            RenderStyle.AsciiDonut => AsciiDonutRenderer.GetInstance(),
            RenderStyle.Bars => BarsRenderer.GetInstance(),
            RenderStyle.CircularBars => CircularBarsRenderer.GetInstance(),
            RenderStyle.CircularWave => CircularWaveRenderer.GetInstance(),
            RenderStyle.Constellation => ConstellationRenderer.GetInstance(),
            RenderStyle.Cube => CubeRenderer.GetInstance(),
            RenderStyle.Cubes => CubesRenderer.GetInstance(),
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
            _ => throw new ArgumentException($"Unknown render style: {style}")
        };

        /// <summary>
        /// Configures a single renderer with specified settings
        /// </summary>
        private static void ConfigureRenderer(
            ISpectrumRenderer renderer,
            bool? isOverlayActive,
            RenderQuality? quality)
        {
            try
            {
                switch (isOverlayActive, quality)
                {
                    case (bool overlay, RenderQuality q):
                        renderer.Configure(overlay, q);
                        break;
                    case (bool overlay, null):
                        renderer.Configure(overlay, renderer.Quality);
                        break;
                    case (null, RenderQuality q):
                        renderer.Configure(isOverlayActive: false, q);
                        renderer.Quality = q;
                        break;
                }
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix,
                    $"Error configuring renderer: {ex.Message}", forceLog: true);
            }
        }

        /// <summary>
        /// Returns a fallback renderer when the primary one fails
        /// </summary>
        private static ISpectrumRenderer GetFallbackRenderer(
            RenderStyle style,
            bool isOverlayActive,
            RenderQuality quality)
        {
            // Log fallback usage
            SmartLogger.Log(
                LogLevel.Warning,
                LogPrefix,
                $"Using fallback renderer for {style}",
                forceLog: true);

            // Attempt to create basic renderer as fallback
            try
            {
                var fallback = BarsRenderer.GetInstance();
                fallback.Initialize();
                fallback.Configure(isOverlayActive, quality);
                return fallback;
            }
            catch (Exception ex)
            {
                SmartLogger.Log(
                    LogLevel.Error,
                    LogPrefix,
                    $"Critical: Even fallback renderer failed: {ex.Message}",
                    forceLog: true);
                throw new InvalidOperationException("Could not create any renderer", ex);
            }
        }
    }
}