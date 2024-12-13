namespace SpectrumNet
{
    #region RenderCache Struct
    [StructLayout(LayoutKind.Sequential)]
    internal struct RenderCache
    {
        public float Width, Height, LowerBound, UpperBound, StepSize, OverlayHeight;
    }
    #endregion

    #region RenderStyle Enum
    public enum RenderStyle
    {
        Bars, Dots, Cubes, Waveform, Loudness, CircularBars, Particles, SphereRenderer,
        GradientWave, CircularWave, Fire, Raindrops, Gauge, Heartbeat, TextParticles, CosmicEcho, AsciiDonut,
    }
    #endregion

    #region ISpectrumRenderer Interface
    public interface ISpectrumRenderer : IDisposable
    {
        void Initialize();
        void Render(SKCanvas canvas, float[] spectrum, SKImageInfo info, float barWidth,
                    float barSpacing, int barCount, SKPaint paint, Action<SKCanvas, SKImageInfo> drawPerformanceInfo);
        void Configure(bool isOverlayActive);
    }
    #endregion

    #region SpectrumRendererFactory Class
    public static class SpectrumRendererFactory
    {
        private static readonly object _lock = new();
        private static readonly Dictionary<RenderStyle, ISpectrumRenderer> _rendererCache = new();
        private static readonly HashSet<RenderStyle> _initializedRenderers = new();

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

                var renderer = (ISpectrumRenderer)(style switch
                {
                    RenderStyle.Bars => BarsRenderer.GetInstance(),
                    RenderStyle.Dots => DotsRenderer.GetInstance(),
                    RenderStyle.Cubes => CubesRenderer.GetInstance(),
                    RenderStyle.Waveform => WaveformRenderer.GetInstance(),
                    RenderStyle.Loudness => LoudnessMeterRenderer.GetInstance(),
                    RenderStyle.CircularBars => CircularBarsRenderer.GetInstance(),
                    RenderStyle.Particles => ParticlesRenderer.GetInstance(),
                    RenderStyle.SphereRenderer => SphereRenderer.GetInstance(),
                    RenderStyle.GradientWave => GradientWaveRenderer.GetInstance(),
                    RenderStyle.CircularWave => CircularWaveRenderer.GetInstance(),
                    RenderStyle.Fire => FireRenderer.GetInstance(),
                    RenderStyle.Raindrops => RaindropsRenderer.GetInstance(),
                    RenderStyle.Gauge => GaugeRenderer.GetInstance(),

                    // New renderers

                    RenderStyle.Heartbeat => HeartbeatRenderer.GetInstance(),
                    RenderStyle.TextParticles => TextParticlesRenderer.GetInstance(),
                    RenderStyle.CosmicEcho => CosmicEchoRenderer.GetInstance(),
                    RenderStyle.AsciiDonut => AsciiDonutRenderer.GetInstance(),
                    _ => throw new ArgumentException($"Unknown render style: {style}")
                });

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
    }
    #endregion
}