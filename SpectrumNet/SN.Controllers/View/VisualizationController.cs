#nullable enable

using SpectrumNet.SN.Spectrum.Core;
using SpectrumNet.SN.Visualization.Core;

namespace SpectrumNet.SN.Controllers.View;

public class VisualizationController(
    IMainController mainController,
    SKElement renderElement,
    IRenderingManager renderingManager,
    IAnalyzerManager analyzerManager,
    IVisualizationSettingsManager settingsManager,
    IStylesProvider stylesProvider)
    : IViewController, IDisposable
{
    private const string LogPrefix = nameof(VisualizationController);
    private readonly ISmartLogger _logger = Instance;

    private readonly IRenderingManager _renderingManager = renderingManager ??
        throw new ArgumentNullException(nameof(renderingManager));

    private readonly IAnalyzerManager _analyzerManager = analyzerManager ??
        throw new ArgumentNullException(nameof(analyzerManager));

    private readonly IVisualizationSettingsManager _settingsManager = settingsManager ??
        throw new ArgumentNullException(nameof(settingsManager));

    private readonly IStylesProvider _stylesProvider = stylesProvider ??
        throw new ArgumentNullException(nameof(stylesProvider));

    private readonly IMainController _mainController = mainController ??
        throw new ArgumentNullException(nameof(mainController));

    private readonly SKElement _renderElement = renderElement ??
        throw new ArgumentNullException(nameof(renderElement));

    private bool _isDisposed;

    public IVisualizationSettingsManager SettingsManager => _settingsManager;

    public void RequestRender() => _renderingManager.RequestRender();

    public void UpdateRenderDimensions(int width, int height) =>
        _renderingManager.UpdateDimensions(width, height);

    public void SynchronizeVisualization() => _renderingManager.SynchronizeVisualization();

    public void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs? e) =>
        _renderingManager.OnPaintSurface(sender, e);

    public Renderer? Renderer
    {
        get => _renderingManager.Renderer;
        set => _renderingManager.Renderer = value;
    }

    public SpectrumAnalyzer Analyzer
    {
        get => _analyzerManager.Analyzer;
        set => _analyzerManager.Analyzer = value;
    }

    public SKElement SpectrumCanvas => _renderElement;
    public SpectrumBrushes SpectrumStyles => SpectrumBrushes.Instance;

    public int BarCount
    {
        get => _settingsManager.BarCount;
        set => _settingsManager.BarCount = value;
    }

    public double BarSpacing
    {
        get => _settingsManager.BarSpacing;
        set => _settingsManager.BarSpacing = value;
    }

    public RenderQuality RenderQuality
    {
        get => _settingsManager.RenderQuality;
        set => _settingsManager.RenderQuality = value;
    }

    public RenderStyle SelectedDrawingType
    {
        get => _settingsManager.SelectedDrawingType;
        set => _settingsManager.SelectedDrawingType = value;
    }

    public SpectrumScale ScaleType
    {
        get => _settingsManager.ScaleType;
        set => _settingsManager.ScaleType = value;
    }

    public string SelectedStyle
    {
        get => _settingsManager.SelectedStyle;
        set => _settingsManager.SelectedStyle = value;
    }

    public Palette? SelectedPalette
    {
        get => _stylesProvider.SelectedPalette;
        set => _stylesProvider.SelectedPalette = value;
    }

    public bool ShowPerformanceInfo
    {
        get => _settingsManager.ShowPerformanceInfo;
        set => _settingsManager.ShowPerformanceInfo = value;
    }

    public IReadOnlyDictionary<string, Palette> AvailablePalettes => _stylesProvider.AvailablePalettes;
    public IEnumerable<RenderStyle> AvailableDrawingTypes => _stylesProvider.AvailableDrawingTypes;
    public IEnumerable<FftWindowType> AvailableFftWindowTypes => _stylesProvider.AvailableFftWindowTypes;
    public IEnumerable<SpectrumScale> AvailableScaleTypes => _stylesProvider.AvailableScaleTypes;
    public IEnumerable<RenderQuality> AvailableRenderQualities => _stylesProvider.AvailableRenderQualities;
    public IEnumerable<RenderStyle> OrderedDrawingTypes => _stylesProvider.OrderedDrawingTypes;

    public void SelectNextRenderer() =>
        _logger.Safe(() => 
        _settingsManager.HandleSelectNextRenderer(), 
            LogPrefix, "Error selecting next renderer");

    public void SelectPreviousRenderer() =>
        _logger.Safe(() => 
        _settingsManager.HandleSelectPreviousRenderer(), 
            LogPrefix, "Error selecting previous renderer");

    public void Dispose()
    {
        if (_isDisposed) return;

        _logger.Safe(() =>
        {
            if (_renderingManager is IDisposable renderingManagerDisposable)
                renderingManagerDisposable.Dispose();

            if (_analyzerManager is IDisposable analyzerManagerDisposable)
                analyzerManagerDisposable.Dispose();

            if (_settingsManager is IDisposable settingsManagerDisposable)
                settingsManagerDisposable.Dispose();

            if (_stylesProvider is IDisposable stylesProviderDisposable)
                stylesProviderDisposable.Dispose();

            GC.SuppressFinalize(this);

            _isDisposed = true;
        }, LogPrefix, "Error during view controller disposal");
    }
}