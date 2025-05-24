#nullable enable

namespace SpectrumNet.SN.Controllers.View;

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
        var stylesProvider = new StylesProvider(mainController, settings, brushProvider);
        var settingsManager = new VisualizationSettingsManager(
            mainController,
            rendererFactory,
            settings,
            stylesProvider);

        return new VisualizationController(
            mainController,
            renderElement,
            renderingManager,
            analyzerManager,
            settingsManager,
            stylesProvider);
    }
}