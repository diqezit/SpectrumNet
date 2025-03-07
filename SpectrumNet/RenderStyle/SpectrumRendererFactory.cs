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
    /// Enumeration of rendering quality levels.
    /// </summary>
    public enum RenderQuality
    {
        Low,
        Medium,
        High
    }

    /// <summary>
    /// Interface for classes that perform spectrum rendering.
    /// </summary>
    public interface ISpectrumRenderer : IDisposable
    {
        /// <summary>Initializes the renderer.</summary>
        void Initialize();

        /// <summary>Renders the spectrum on the given canvas.</summary>
        /// <param name="canvas">Canvas for drawing.</param>
        /// <param name="spectrum">Array of spectrum values.</param>
        /// <param name="info">Image information.</param>
        /// <param name="barWidth">Width of bars.</param>
        /// <param name="barSpacing">Spacing between bars.</param>
        /// <param name="barCount">Number of bars.</param>
        /// <param name="paint">Object for styling the rendering.</param>
        /// <param name="drawPerformanceInfo">Method for drawing performance information.</param>
        void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info, float barWidth,
                    float barSpacing, int barCount, SKPaint? paint,
                    Action<SKCanvas, SKImageInfo> drawPerformanceInfo);

        /// <summary>Configures the renderer for overlay mode and quality settings.</summary>
        /// <param name="isOverlayActive">Whether overlay mode is active.</param>
        /// <param name="quality">Rendering quality level.</param>
        void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium);

        /// <summary>Gets or sets the current rendering quality.</summary>
        RenderQuality Quality { get; set; }
    }

    /// <summary>
    /// Factory for creating spectrum renderer instances.
    /// </summary>
    public static class SpectrumRendererFactory
    {
        private const string LogPrefix = "[SpectrumRendererFactory] ";
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
                if (_globalQuality != value)
                {
                    _globalQuality = value;
                    ConfigureAllRenderers(isOverlayActive: null, _globalQuality);
                    SmartLogger.Log(LogLevel.Information, LogPrefix, $"Global quality changed to {value}", forceLog: true);
                }
            }
        }

        /// <summary>
        /// Creates or retrieves a spectrum renderer instance of the specified style.
        /// </summary>
        /// <param name="style">Rendering style.</param>
        /// <param name="isOverlayActive">Whether overlay mode is active.</param>
        /// <param name="quality">Rendering quality level.</param>
        /// <returns>Renderer instance.</returns>
        public static ISpectrumRenderer CreateRenderer(RenderStyle style, bool isOverlayActive, RenderQuality? quality = null)
        {
            RenderQuality actualQuality = quality ?? _globalQuality;

            if (_rendererCache.TryGetValue(style, out var cachedRenderer))
            {
                cachedRenderer.Configure(isOverlayActive, actualQuality);
                return cachedRenderer;
            }

            lock (_lock)
            {
                if (_rendererCache.TryGetValue(style, out cachedRenderer))
                {
                    cachedRenderer.Configure(isOverlayActive, actualQuality);
                    return cachedRenderer;
                }

                var renderer = GetRendererInstance(style);

                if (!_initializedRenderers.Contains(style))
                {
                    try
                    {
                        renderer.Initialize();
                        _initializedRenderers.Add(style);
                    }
                    catch (Exception ex)
                    {
                        SmartLogger.Log(LogLevel.Error, LogPrefix, $"Failed to initialize renderer {style}: {ex.Message}", forceLog: true);
                        throw;
                    }
                }

                renderer.Configure(isOverlayActive, actualQuality);
                _rendererCache[style] = renderer;
                return renderer;
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
            lock (_lock) return _rendererCache.TryGetValue(style, out var renderer) ? renderer : null;
        }

        /// <summary>
        /// Configures all cached renderers for overlay mode and quality.
        /// </summary>
        /// <param name="isOverlayActive">Whether overlay mode is active (null to keep current).</param>
        /// <param name="quality">Rendering quality level (null to keep current).</param>
        public static void ConfigureAllRenderers(bool? isOverlayActive, RenderQuality? quality = null)
        {
            lock (_lock)
            {
                foreach (var renderer in _rendererCache.Values)
                {
                    try
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
                            renderer.Configure(isOverlayActive: false, quality.Value);
                            renderer.Quality = quality.Value;
                        }
                    }
                    catch (Exception ex)
                    {
                        SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error configuring renderer: {ex.Message}", forceLog: true);
                    }
                }
            }
        }

        /// <summary>
        /// Sets the quality for a specific renderer style.
        /// </summary>
        /// <param name="style">Rendering style.</param>
        /// <param name="quality">Rendering quality level.</param>
        public static void SetRendererQuality(RenderStyle style, RenderQuality quality)
        {
            lock (_lock)
            {
                if (_rendererCache.TryGetValue(style, out var renderer))
                {
                    renderer.Quality = quality;
                }
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
    }
}