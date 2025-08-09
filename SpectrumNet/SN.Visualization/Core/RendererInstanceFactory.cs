#nullable enable

namespace SpectrumNet.SN.Visualization.Core;

internal static class RendererInstanceFactory
{
    public static ISpectrumRenderer CreateInstance(RenderStyle style) => style switch
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
        RenderStyle.Glitch => MatrixRainRenderer.GetInstance(),
        RenderStyle.GradientWave => GradientWaveRenderer.GetInstance(),
        RenderStyle.Heartbeat => HeartbeatRenderer.GetInstance(),
        RenderStyle.Kenwood => KenwoodBarsRenderer.GetInstance(),
        RenderStyle.LedMeter => LedMeterRenderer.GetInstance(),
        RenderStyle.Loudness => LoudnessMeterRenderer.GetInstance(),
        RenderStyle.Particles => ParticlesRenderer.GetInstance(),
        RenderStyle.PixelGrid => PixelGridRenderer.GetInstance(),
        RenderStyle.Raindrops => RaindropsRenderer.GetInstance(),
        RenderStyle.Rainbow => RainbowRenderer.GetInstance(),
        RenderStyle.SphereRenderer => SphereRenderer.GetInstance(),
        RenderStyle.TextParticles => TextParticlesRenderer.GetInstance(),
        RenderStyle.Waterfall => WaterfallRenderer.GetInstance(),
        RenderStyle.Waveform => WaveformRenderer.GetInstance(),
        RenderStyle.RippleRenderer => RippleRenderer.GetInstance(),
        RenderStyle.HackerTextRenderer => HackerTextRenderer.GetInstance(),
        RenderStyle.LedPanelRenderer => LedPanelRenderer.GetInstance(),

        _ => throw new ArgumentException($"Unknown render style: {style}")
    };
}