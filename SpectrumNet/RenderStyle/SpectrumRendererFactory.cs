#nullable enable

namespace SpectrumNet
{
    public enum RenderStyle
    {
        Bars,

        // Implementation for others renders temporary disabled to fix all issues and compabilites im main logic

        //Raindrops,
        //AsciiDonut,
        //CircularBars,
        //CircularWave,
        //Constellation,
        //Cube,
        //Cubes,
        //Fire,
        //Gauge,
        //Glitch,
        //GradientWave,
        //Heartbeat,
        //Kenwood,
        //LedMeter,
        //Loudness,
        //Particles,
        //Polar,
        //Rainbow,
        //SphereRenderer,
        //TextParticles,
        //Voronoi,
        //Waterfall,
        //Waveform
    }

    public enum RenderQuality
    {
        Low,
        Medium,
        High
    }

    public static class SpectrumRendererFactory
    {
        private const string LogPrefix = "[SpectrumRendererFactory] ";
        private static readonly object _lock = new();
        private static readonly Dictionary<RenderStyle, ISpectrumRenderer> _rendererCache = new();
        private static readonly HashSet<RenderStyle> _initializedRenderers = new();
        private static RenderQuality _globalQuality = RenderQuality.Medium;

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
                SmartLogger.Log(LogLevel.Information, LogPrefix, $"Created new renderer instance for {style}");

                if (!_initializedRenderers.Contains(style))
                {
                    try
                    {
                        renderer.Initialize();
                        _initializedRenderers.Add(style);
                        SmartLogger.Log(LogLevel.Information, LogPrefix, $"Initialized renderer for {style}");
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

        public static IEnumerable<ISpectrumRenderer> GetAllRenderers()
        {
            lock (_lock)
                return _rendererCache.Values.ToList();
        }

        public static ISpectrumRenderer? GetCachedRenderer(RenderStyle style)
        {
            lock (_lock)
            {
                return _rendererCache.TryGetValue(style, out var renderer)
                    ? renderer
                    : null;
            }
        }

        public static void ConfigureAllRenderers(bool? isOverlayActive, RenderQuality? quality = null)
        {
            lock (_lock)
            {
                if (_rendererCache.Count == 0)
                {
                    foreach (RenderStyle style in Enum.GetValues(typeof(RenderStyle)))
                    {
                        try
                        {
                            CreateRenderer(style, isOverlayActive ?? false, quality ?? _globalQuality);
                        }
                        catch (Exception ex)
                        {
                            SmartLogger.Log(LogLevel.Error, LogPrefix, $"Failed to pre-initialize renderer {style}: {ex.Message}", forceLog: true);
                        }
                    }
                }

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

        private static ISpectrumRenderer GetRendererInstance(RenderStyle style) => style switch
        {
            RenderStyle.Bars => Bars3DRenderer.GetInstance(),

            // Implementation for others renders temporary disabled due to fix issues and compabilities 

            //RenderStyle.Raindrops => RaindropsRenderer.GetInstance(),
            //RenderStyle.AsciiDonut => AsciiDonutRenderer.GetInstance(),
            //RenderStyle.CircularBars => CircularBarsRenderer.GetInstance(),
            //RenderStyle.CircularWave => CircularWaveRenderer.GetInstance(),
            //RenderStyle.Constellation => ConstellationRenderer.GetInstance(),
            //RenderStyle.Cube => CubeRenderer.GetInstance(),
            //RenderStyle.Cubes => CubesRenderer.GetInstance(),
            //RenderStyle.Fire => FireRenderer.GetInstance(),
            //RenderStyle.Gauge => GaugeRenderer.GetInstance(),
            //RenderStyle.Glitch => GlitchRenderer.GetInstance(),
            //RenderStyle.GradientWave => GradientWaveRenderer.GetInstance(),
            //RenderStyle.Heartbeat => HeartbeatRenderer.GetInstance(),
            //RenderStyle.Kenwood => KenwoodRenderer.GetInstance(),
            //RenderStyle.LedMeter => LedMeterRenderer.GetInstance(),
            //RenderStyle.Loudness => LoudnessMeterRenderer.GetInstance(),
            //RenderStyle.Particles => ParticlesRenderer.GetInstance(),
            //RenderStyle.Polar => PolarRenderer.GetInstance(),
            //RenderStyle.Rainbow => RainbowRenderer.GetInstance(),
            //RenderStyle.SphereRenderer => SphereRenderer.GetInstance(),
            //RenderStyle.TextParticles => TextParticlesRenderer.GetInstance(),
            //RenderStyle.Voronoi => VoronoiRenderer.GetInstance(),
            //RenderStyle.Waterfall => WaterfallRenderer.GetInstance(),
            //RenderStyle.Waveform => WaveformRenderer.GetInstance(),

            _ => throw new ArgumentException($"Unknown render style: {style}")
        };
    }
}