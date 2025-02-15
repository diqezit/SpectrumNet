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
        AsciiDonut,
        Bars,
        CircularBars,
        CircularWave,
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
                    RenderStyle.AsciiDonut => AsciiDonutRenderer.GetInstance(),
                    RenderStyle.Bars => BarsRenderer.GetInstance(),
                    RenderStyle.CircularBars => CircularBarsRenderer.GetInstance(),
                    RenderStyle.CircularWave => CircularWaveRenderer.GetInstance(),
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