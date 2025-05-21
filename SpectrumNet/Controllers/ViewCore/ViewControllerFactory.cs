#nullable enable

namespace SpectrumNet.Controllers.ViewCore;

public static class ViewControllerFactory
{
    public static IViewController Create(
        IMainController mainController,
        SKElement renderElement,
        IRendererFactory? rendererFactory = null,
        IBrushProvider? brushProvider = null,
        ISettings? settings = null)
    {
        rendererFactory ??= RendererFactory.Instance;
        settings ??= SettingsProvider.Instance.Settings;

        var renderingManager = new RenderingManager(mainController, rendererFactory);
        var analyzerManager = new AnalyzerManager();
        var settingsManager = new VisualizationSettingsManager(
            mainController,
            rendererFactory,
            settings);
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