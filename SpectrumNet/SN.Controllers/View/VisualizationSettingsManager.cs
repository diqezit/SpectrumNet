#nullable enable

namespace SpectrumNet.SN.Controllers.View;

public class VisualizationSettingsManager : IVisualizationSettingsManager
{
    private const string LogPrefix = nameof(VisualizationSettingsManager);
    private readonly ISmartLogger _logger = Instance;

    private readonly IMainController _mainController;
    private readonly IRendererFactory _rendererFactory;
    private readonly ISettings _settings;
    private readonly IStylesProvider _stylesProvider;

    private RenderStyle _selectedDrawingType;
    private SpectrumScale _selectedScaleType;
    private RenderQuality _renderQuality;
    private string _selectedStyle;
    private bool _showPerformanceInfo;

    private bool _updatingQuality;

    public VisualizationSettingsManager(
        IMainController mainController,
        IRendererFactory rendererFactory,
        ISettings settings,
        IStylesProvider stylesProvider)
    {
        _mainController = mainController ?? throw new ArgumentNullException(nameof(mainController));
        _rendererFactory = rendererFactory ?? throw new ArgumentNullException(nameof(rendererFactory));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _stylesProvider = stylesProvider ?? throw new ArgumentNullException(nameof(stylesProvider));

        _selectedDrawingType = _settings.SelectedRenderStyle;
        _selectedScaleType = _settings.SelectedScaleType;
        _renderQuality = _settings.SelectedRenderQuality;
        _selectedStyle = _settings.SelectedPalette;
        _showPerformanceInfo = _settings.ShowPerformanceInfo;
    }

    public int BarCount
    {
        get => _settings.UIBarCount;
        set => UpdateSetting(
            value,
            v => _settings.UIBarCount = v,
            nameof(BarCount),
            v => v > 0,
            DefaultSettings.UIBarCount,
            "invalid bar count");
    }

    public double BarSpacing
    {
        get => _settings.UIBarSpacing;
        set => UpdateSetting(
            value,
            v => _settings.UIBarSpacing = v,
            nameof(BarSpacing),
            v => v >= 0,
            DefaultSettings.UIBarSpacing,
            "negative spacing");
    }

    public RenderQuality RenderQuality
    {
        get => _renderQuality;
        set
        {
            if (_renderQuality == value || _updatingQuality)
                return;

            try
            {
                _updatingQuality = true;
                _renderQuality = value;
                _settings.SelectedRenderQuality = value;
                _mainController.OnPropertyChanged(nameof(RenderQuality));

                _rendererFactory.GlobalQuality = value;
                _mainController.Renderer?.UpdateRenderQuality(value);

                _logger.Log(LogLevel.Information,
                    LogPrefix,
                    $"Render quality set to {value}");
            }
            finally
            {
                _updatingQuality = false;
            }
        }
    }

    public RenderStyle SelectedDrawingType
    {
        get => _selectedDrawingType;
        set => UpdateEnumProperty(
            ref _selectedDrawingType,
            value,
            v => _settings.SelectedRenderStyle = v,
            nameof(SelectedDrawingType));
    }

    public SpectrumScale ScaleType
    {
        get => _selectedScaleType;
        set => _logger.Safe(() => HandleScaleTypeChange(value), LogPrefix, "Error changing scale type");
    }

    private void HandleScaleTypeChange(SpectrumScale value)
    {
        if (_selectedScaleType == value) return;

        _selectedScaleType = value;
        _settings.SelectedScaleType = value;
        _mainController.OnPropertyChanged(nameof(ScaleType));
        UpdateAnalyzerSettings(value);
    }

    public string SelectedStyle
    {
        get => _selectedStyle;
        set => _logger.Safe(() => HandleSelectedStyleChange(value), LogPrefix, "Error changing style");
    }

    private void HandleSelectedStyleChange(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            _logger.Log(LogLevel.Warning, LogPrefix, "Attempt to set empty style name");
            value = DefaultSettings.SelectedPalette;
        }

        if (_selectedStyle == value) return;

        _selectedStyle = value;
        _settings.SelectedPalette = value;
        _mainController.OnPropertyChanged(nameof(SelectedStyle));

        if (_mainController.Renderer != null)
        {
            var (color, brush) = _mainController.SpectrumStyles.GetColorAndBrush(value);
            _mainController.Renderer.UpdateSpectrumStyle(value, color, brush);
        }

        _logger.Log(LogLevel.Information, LogPrefix, $"Selected style changed to {value}");
    }

    public bool ShowPerformanceInfo
    {
        get => _showPerformanceInfo;
        set
        {
            if (_showPerformanceInfo == value) return;

            _showPerformanceInfo = value;
            _settings.ShowPerformanceInfo = value;
            _mainController.OnPropertyChanged(nameof(ShowPerformanceInfo));
        }
    }

    public void HandleSelectPreviousRenderer()
    {
        var renderers = _stylesProvider.OrderedDrawingTypes.ToList();
        if (renderers.Count <= 1) return;

        var currentIndex = renderers.IndexOf(SelectedDrawingType);
        var previousIndex = currentIndex - 1;
        if (previousIndex < 0) previousIndex = renderers.Count - 1;
        SelectedDrawingType = renderers[previousIndex];
    }

    public void HandleSelectNextRenderer()
    {
        var renderers = _stylesProvider.OrderedDrawingTypes.ToList();
        if (renderers.Count <= 1) return;

        var currentIndex = renderers.IndexOf(SelectedDrawingType);
        var nextIndex = (currentIndex + 1) % renderers.Count;
        SelectedDrawingType = renderers[nextIndex];
    }

    public void InitializeAfterRendererCreated()
    {
        if (_mainController.Renderer != null)
        {
            _mainController.Renderer.UpdateRenderStyle(_selectedDrawingType);

            var (color, brush) = _mainController.SpectrumStyles.GetColorAndBrush(_selectedStyle);
            _mainController.Renderer.UpdateSpectrumStyle(_selectedStyle, color, brush);

            _mainController.Renderer.UpdateRenderQuality(_renderQuality);

            _logger.Log(LogLevel.Information, LogPrefix, "Settings applied to initialized renderer");
        }
        else
        {
            _logger.Log(LogLevel.Warning, LogPrefix, "Cannot apply settings: Renderer is still null");
        }
    }

    private void UpdateSetting<T>(
        T value,
        Action<T> setter,
        string propertyName,
        Func<T, bool> validator,
        T defaultValue,
        string errorMessage)
    {
        if (!validator(value))
        {
            _logger.Log(LogLevel.Warning, LogPrefix, $"Attempt to set {errorMessage}: {value}");
            value = defaultValue;
        }

        setter(value);
        _mainController.OnPropertyChanged(propertyName);
    }

    private void UpdateEnumProperty<T>(
        ref T field,
        T value,
        Action<T> settingUpdater,
        [CallerMemberName] string propertyName = "")
        where T : struct, Enum
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;

        field = value;
        _mainController.OnPropertyChanged(propertyName);
        settingUpdater(value);

        try
        {
            if (_mainController.Renderer != null && typeof(T) == typeof(RenderStyle))
                _mainController.Renderer.UpdateRenderStyle((RenderStyle)(object)value);
        }
        catch (NullReferenceException)
        {
            _logger.Log(LogLevel.Warning, LogPrefix,
                $"Renderer not yet available when setting {propertyName}");
        }
    }

    private void UpdateAnalyzerSettings(SpectrumScale scale)
    {
        if (_mainController.Analyzer == null) return;

        try
        {
            _mainController.Analyzer.UpdateSettings(_mainController.WindowType, scale);
            _mainController.Analyzer.ReprocessLastData();
            _mainController.RequestRender();
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Error,
                LogPrefix,
                $"Error updating analyzer settings: {ex.Message}");
        }
    }
}