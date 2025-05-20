#nullable enable

namespace SpectrumNet.Controllers.ViewCore;

public class VisualizationSettingsManager(
    IMainController mainController,
    IRendererFactory rendererFactory) : IVisualizationSettingsManager
{
    private const string LogPrefix = nameof(VisualizationSettingsManager);
    private readonly ISmartLogger _logger = Instance;

    private readonly IMainController _mainController = mainController ?? 
        throw new ArgumentNullException(nameof(mainController));

    private readonly IRendererFactory _rendererFactory = rendererFactory ?? 
        throw new ArgumentNullException(nameof(rendererFactory));

    private RenderStyle _selectedDrawingType = Settings.Instance.SelectedRenderStyle;
    private SpectrumScale _selectedScaleType = Settings.Instance.SelectedScaleType;
    private RenderQuality _renderQuality = Settings.Instance.SelectedRenderQuality;
    private string _selectedStyle = Settings.Instance.SelectedPalette;
    private bool _showPerformanceInfo = Settings.Instance.ShowPerformanceInfo;

    private bool _updatingQuality;

    public int BarCount
    {
        get => Settings.Instance.UIBarCount;
        set => UpdateSetting(
            value,
            v => Settings.Instance.UIBarCount = v,
            nameof(BarCount),
            v => v > 0,
            DefaultSettings.UIBarCount,
            "invalid bar count");
    }

    public double BarSpacing
    {
        get => Settings.Instance.UIBarSpacing;
        set => UpdateSetting(
            value,
            v => Settings.Instance.UIBarSpacing = v,
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
                Settings.Instance.SelectedRenderQuality = value;
                _mainController.OnPropertyChanged(nameof(RenderQuality));

                _rendererFactory.GlobalQuality = value;
                if (_mainController.Renderer != null)
                    _mainController.Renderer.UpdateRenderQuality(value);

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
        set => UpdateEnumProperty(ref _selectedDrawingType, value,
            v => Settings.Instance.SelectedRenderStyle = v, nameof(SelectedDrawingType));
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
        Settings.Instance.SelectedScaleType = value;
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
        Settings.Instance.SelectedPalette = value;
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
            Settings.Instance.ShowPerformanceInfo = value;
            _mainController.OnPropertyChanged(nameof(ShowPerformanceInfo));
        }
    }

    public void LoadAndApplySettings() =>
        _logger.Safe(() => HandleLoadAndApplySettings(), LogPrefix, "Error loading and applying settings");

    private void HandleLoadAndApplySettings()
    {
        ShowPerformanceInfo = Settings.Instance.ShowPerformanceInfo;
        SelectedDrawingType = Settings.Instance.SelectedRenderStyle;
        ScaleType = Settings.Instance.SelectedScaleType;
        RenderQuality = Settings.Instance.SelectedRenderQuality;
        SelectedStyle = Settings.Instance.SelectedPalette;

        _logger.Log(LogLevel.Information, LogPrefix, "View settings loaded and applied successfully");
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

    private void UpdateSetting<T>(T value, Action<T> setter, string propertyName,
                                Func<T, bool> validator, T defaultValue, string errorMessage)
    {
        if (!validator(value))
        {
            _logger.Log(LogLevel.Warning, LogPrefix, $"Attempt to set {errorMessage}: {value}");
            value = defaultValue;
        }

        setter(value);
        _mainController.OnPropertyChanged(propertyName);
    }

    private void UpdateEnumProperty<T>(ref T field, T value, Action<T> settingUpdater,
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