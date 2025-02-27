#nullable enable
namespace SpectrumNet
{
    #region Structs
    /// <summary>
    /// Structure for storing cached values used in spectrum rendering.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct RenderCache
    {
        /// <summary>Width of the rendering area.</summary>
        public float Width;
        /// <summary>Height of the rendering area.</summary>
        public float Height;
        /// <summary>Lower bound of the display area.</summary>
        public float LowerBound;
        /// <summary>Upper bound of the spectral range.</summary>
        public float UpperBound;
        /// <summary>Step size for spectrum discretization.</summary>
        public float StepSize;
        /// <summary>Height of the overlay.</summary>
        public float OverlayHeight;
    }
    #endregion

    #region Enums
    /// <summary>
    /// Enumeration of spectrum rendering styles.
    /// </summary>
    public enum RenderStyle
    {
        AsciiDonut,
        Bars,
        CircularBars,
        CircularWave,
        Cube,
        Cubes,
        Fire,
        Gauge,
        GradientWave,
        Heartbeat,
        Loudness,
        Particles,
        Raindrops,
        Rainbow,
        SphereRenderer,
        TextParticles,
        Waveform,
        Kenwood
    }
    #endregion

    #region Interfaces
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
        void Render(SKCanvas canvas, float[] spectrum, SKImageInfo info, float barWidth,
                    float barSpacing, int barCount, SKPaint paint,
                    Action<SKCanvas, SKImageInfo> drawPerformanceInfo);

        /// <summary>Configures the renderer for overlay mode.</summary>
        /// <param name="isOverlayActive">Whether overlay mode is active.</param>
        void Configure(bool isOverlayActive);
    }
    #endregion

    #region Factory Classes
    /// <summary>
    /// Factory for creating spectrum renderer instances.
    /// </summary>
    public static class SpectrumRendererFactory
    {
        #region Private Fields
        private static readonly object _lock = new();
        private static readonly Dictionary<RenderStyle, ISpectrumRenderer> _rendererCache = new();
        private static readonly HashSet<RenderStyle> _initializedRenderers = new();
        #endregion

        #region Public Methods
        /// <summary>
        /// Creates or retrieves a spectrum renderer instance of the specified style.
        /// </summary>
        /// <param name="style">Rendering style.</param>
        /// <param name="isOverlayActive">Whether overlay mode is active.</param>
        /// <returns>Renderer instance.</returns>
        public static ISpectrumRenderer CreateRenderer(RenderStyle style, bool isOverlayActive)
        {
            if (_rendererCache.TryGetValue(style, out var cachedRenderer))
            {
                cachedRenderer.Configure(isOverlayActive);
                return cachedRenderer;
            }

            lock (_lock)
            {
                if (_rendererCache.TryGetValue(style, out cachedRenderer))
                {
                    cachedRenderer.Configure(isOverlayActive);
                    return cachedRenderer;
                }

                var renderer = GetRendererInstance(style);

                if (!_initializedRenderers.Contains(style))
                {
                    renderer.Initialize();
                    _initializedRenderers.Add(style);
                }

                renderer.Configure(isOverlayActive);
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
        /// Configures all cached renderers for overlay mode.
        /// </summary>
        /// <param name="isOverlayActive">Whether overlay mode is active.</param>
        public static void ConfigureAllRenderers(bool isOverlayActive)
        {
            lock (_lock)
            {
                foreach (var renderer in _rendererCache.Values)
                    renderer.Configure(isOverlayActive);
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Creates a renderer instance for the specified style.
        /// </summary>
        private static ISpectrumRenderer GetRendererInstance(RenderStyle style) => style switch
        {
            RenderStyle.AsciiDonut => AsciiDonutRenderer.GetInstance(),
            RenderStyle.Bars => BarsRenderer.GetInstance(),
            RenderStyle.CircularBars => CircularBarsRenderer.GetInstance(),
            RenderStyle.CircularWave => CircularWaveRenderer.GetInstance(),
            RenderStyle.Cube => CubeRenderer.GetInstance(),
            RenderStyle.Cubes => CubesRenderer.GetInstance(),
            RenderStyle.Fire => FireRenderer.GetInstance(),
            RenderStyle.Gauge => GaugeRenderer.GetInstance(),
            RenderStyle.GradientWave => GradientWaveRenderer.GetInstance(),
            RenderStyle.Heartbeat => HeartbeatRenderer.GetInstance(),
            RenderStyle.Loudness => LoudnessMeterRenderer.GetInstance(),
            RenderStyle.Particles => ParticlesRenderer.GetInstance(),
            RenderStyle.Raindrops => RaindropsRenderer.GetInstance(),
            RenderStyle.Rainbow => RainbowRenderer.GetInstance(),
            RenderStyle.SphereRenderer => SphereRenderer.GetInstance(),
            RenderStyle.TextParticles => TextParticlesRenderer.GetInstance(),
            RenderStyle.Waveform => WaveformRenderer.GetInstance(),
            RenderStyle.Kenwood => KenwoodRenderer.GetInstance(),
            _ => throw new ArgumentException($"Unknown render style: {style}")
        };
        #endregion
    }
    #endregion
}