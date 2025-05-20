// SpectrumNet/Controllers/ViewCore/ViewControllerFactory.cs
#nullable enable

namespace SpectrumNet.Controllers.ViewCore;

public static class ViewControllerFactory
{
    public static IViewController Create(
        IMainController mainController,
        SKElement renderElement,
        IRendererFactory? rendererFactory = null,
        IBrushProvider? brushProvider = null)
    {
        rendererFactory ??= RendererFactory.Instance;

        var renderingManager = new RenderingManager(mainController, rendererFactory);
        var analyzerManager = new AnalyzerManager();
        var settingsManager = new VisualizationSettingsManager(mainController, rendererFactory);
        var stylesProvider = new StylesProvider(mainController, brushProvider);

        return new VisualizationController(
            mainController,
            renderElement,
            renderingManager,
            analyzerManager,
            settingsManager,
            stylesProvider);
    }
}