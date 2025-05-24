#nullable enable

namespace SpectrumNet.SN.Visualization;

internal sealed class RendererConfigurator
{
    private const string LogPrefix = nameof(RendererConfigurator);
    private readonly ISmartLogger _logger = Instance;
    private readonly ConcurrentDictionary<ISpectrumRenderer, RenderQuality> _rendererQualityState = new();

    public void Configure(
        ISpectrumRenderer renderer,
        bool isOverlayActive,
        RenderQuality quality)
    {
        _logger.Safe(() => HandleConfigureRenderer(renderer, isOverlayActive, quality),
            LogPrefix,
            "Error configuring renderer");
    }

    public void ConfigureAll(
        IEnumerable<ISpectrumRenderer> renderers,
        bool? isOverlayActive,
        RenderQuality? quality)
    {
        foreach (var renderer in renderers)
        {
            var actualQuality = quality ?? renderer.Quality;
            var actualOverlay = isOverlayActive ?? renderer.IsOverlayActive;

            if (ShouldConfigureRenderer(renderer, actualOverlay, actualQuality))
            {
                Configure(renderer, actualOverlay, actualQuality);
                _rendererQualityState[renderer] = actualQuality;
            }
        }
    }

    public void ApplyQualityToAll(
        IEnumerable<ISpectrumRenderer> renderers,
        RenderQuality quality)
    {
        foreach (var renderer in renderers)
        {
            Configure(renderer, renderer.IsOverlayActive, quality);
            _rendererQualityState[renderer] = quality;
        }
    }

    public void Initialize(RenderStyle style, ISpectrumRenderer renderer)
    {
        _logger.Safe(() => renderer.Initialize(),
            LogPrefix,
            $"Initialization error for style {style}");

        _logger.Log(LogLevel.Information,
            LogPrefix,
            $"Initialized renderer for style {style}",
            forceLog: true);
    }

    private static bool ShouldConfigureRenderer(
        ISpectrumRenderer renderer,
        bool isOverlayActive,
        RenderQuality quality)
    {
        return renderer.IsOverlayActive != isOverlayActive ||
               renderer.Quality != quality;
    }

    private static void HandleConfigureRenderer(
        ISpectrumRenderer renderer,
        bool isOverlayActive,
        RenderQuality quality)
    {
        renderer.Configure(isOverlayActive, quality);
    }
}