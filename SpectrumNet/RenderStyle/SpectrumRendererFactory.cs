namespace SpectrumNet
{
    internal struct RenderCache
    {
        public float Width;
        public float Height;
        public float LowerBound;
        public float UpperBound;
        public float StepSize;
    }

    public enum RenderStyle
    {
        Bars,
        Dots,
        Cubes,
        Waveform,
        Loudness,
        CircularBars,
        Particles,
        SphereRenderer,
        GradientWave,
        CircularWave,
        Fire,
        Raindrops,
        Gauge
    }

    public interface ISpectrumRenderer : IDisposable
    {
        void Initialize();
        void Render(SKCanvas canvas, float[] spectrum, SKImageInfo info, float barWidth, float barSpacing, int barCount, SKPaint paint, Action<SKCanvas, SKImageInfo> drawPerformanceInfo);
        void Configure(bool isOverlayActive);
    }

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

                var renderer = CreateNewRenderer(style);

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

        private static ISpectrumRenderer CreateNewRenderer(RenderStyle style)
        {
            return style switch
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
                _ => throw new ArgumentException($"Unknown render style: {style}")
            };
        }
    }
}