#nullable enable

namespace SpectrumNet.Controllers.RenderCore;

public class ViewController : IViewController, IDisposable
{
    private const string LogPrefix = "ViewController";

    private readonly IMainController _mainController;
    private readonly IRendererFactory _rendererFactory;
    private readonly IBrushProvider _brushProvider;

    private Renderer? _renderer;
    private SpectrumAnalyzer? _analyzer;
    private SKElement? _renderElement;

    private RenderStyle _selectedDrawingType = RenderStyle.Bars;
    private SpectrumScale _selectedScaleType = SpectrumScale.Linear;
    private RenderQuality _renderQuality = RenderQuality.Medium;
    private string _selectedStyle = DefaultSettings.SelectedPalette;

    private bool
        _showPerformanceInfo = true,
        _isDisposed,
        _updatingQuality;

    public ViewController(
        IMainController mainController,
        SKElement renderElement,
        IRendererFactory rendererFactory,
        IBrushProvider? brushProvider = null)
    {
        _mainController = mainController ??
            throw new ArgumentNullException(nameof(mainController));

        _renderElement = renderElement ??
            throw new ArgumentNullException(nameof(renderElement));

        _rendererFactory = rendererFactory ??
            throw new ArgumentNullException(nameof(rendererFactory));

        _brushProvider = brushProvider ?? SpectrumBrushes.Instance;

        // Инициализация доступных типов рендеринга
        AvailableDrawingTypes = [.. Enum.GetValues<RenderStyle>()
                                        .OrderBy(s => s.ToString())];

        AvailableFftWindowTypes = [.. Enum.GetValues<FftWindowType>()
                                          .OrderBy(wt => wt.ToString())];

        AvailableScaleTypes = [.. Enum.GetValues<SpectrumScale>()
                                      .OrderBy(s => s.ToString())];

        AvailableRenderQualities = [.. Enum.GetValues<RenderQuality>()
                                           .OrderBy(q => (int)q)];

        // Загрузка настроек
        LoadAndApplySettings();
    }

    #region IViewController Implementation

    public SpectrumAnalyzer Analyzer
    {
        get => _analyzer ?? throw new InvalidOperationException("Analyzer not initialized");
        set => _analyzer = value ?? throw new ArgumentNullException(nameof(value));
    }

    public Renderer? Renderer
    {
        get => _renderer;
        set => _renderer = value;
    }

    public SKElement SpectrumCanvas =>
        _renderElement ?? throw new InvalidOperationException("Render element not initialized");

    public SpectrumBrushes SpectrumStyles =>
        _brushProvider as SpectrumBrushes ?? SpectrumBrushes.Instance;

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
            if (_renderQuality == value
                || _updatingQuality)
                return;

            try
            {
                _updatingQuality = true;
                _renderQuality = value;
                Settings.Instance.SelectedRenderQuality = value;
                _mainController.OnPropertyChanged(nameof(RenderQuality));

                _rendererFactory.GlobalQuality = value;
                _renderer?.UpdateRenderQuality(value);

                Log(LogLevel.Information,
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
        set
        {
            if (_selectedScaleType == value) return;

            _selectedScaleType = value;
            Settings.Instance.SelectedScaleType = value;
            _mainController.OnPropertyChanged(nameof(ScaleType));
            UpdateAnalyzerSettings(value);
        }
    }

    public string SelectedStyle
    {
        get => _selectedStyle;
        set
        {
            if (string.IsNullOrEmpty(value))
            {
                Log(LogLevel.Warning, LogPrefix, "Attempt to set empty style name");
                value = DefaultSettings.SelectedPalette;
            }

            if (_selectedStyle == value) return;

            _selectedStyle = value;
            Settings.Instance.SelectedPalette = value;
            _mainController.OnPropertyChanged(nameof(SelectedStyle));

            if (_renderer != null)
            {
                var (color, brush) = _brushProvider.GetColorAndBrush(value);
                _renderer.UpdateSpectrumStyle(value, color, brush);
            }

            Log(LogLevel.Information, LogPrefix, $"Selected style changed to {value}");
        }
    }

    public Palette? SelectedPalette
    {
        get => AvailablePalettes.TryGetValue(SelectedStyle, out var palette) ? palette : null;
        set
        {
            if (value is null) return;
            SelectedStyle = value.Name;
            _mainController.OnPropertyChanged(nameof(SelectedPalette));
        }
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

    public IReadOnlyDictionary<string, Palette> AvailablePalettes =>
        _brushProvider.RegisteredPalettes;

    public IEnumerable<RenderStyle> AvailableDrawingTypes { get; }

    public IEnumerable<FftWindowType> AvailableFftWindowTypes { get; }

    public IEnumerable<SpectrumScale> AvailableScaleTypes { get; }

    public IEnumerable<RenderQuality> AvailableRenderQualities { get; }

    public IEnumerable<RenderStyle> OrderedDrawingTypes =>
        AvailableDrawingTypes.OrderBy(x =>
            Settings.Instance.FavoriteRenderers.Contains(x) ? 0 : 1).ThenBy(x => x.ToString());

    public void RequestRender() => _renderer?.RequestRender();

    public void UpdateRenderDimensions(int width, int height) =>
        _renderer?.UpdateRenderDimensions(width, height);

    public void SynchronizeVisualization() => _renderer?.SynchronizeWithController();

    public void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs? e)
    {
        if (e is null || _renderer is null)
        {
            Log(LogLevel.Error, LogPrefix, "PaintSurface called with null arguments");
            return;
        }

        _renderer.RenderFrame(sender, e);
    }

    #endregion

    #region Helper Methods

    private void LoadAndApplySettings() =>
        Safe(() =>
        {
            ShowPerformanceInfo = Settings.Instance.ShowPerformanceInfo;
            SelectedDrawingType = Settings.Instance.SelectedRenderStyle;
            ScaleType = Settings.Instance.SelectedScaleType;
            RenderQuality = Settings.Instance.SelectedRenderQuality;
            SelectedStyle = Settings.Instance.SelectedPalette;

            Log(LogLevel.Information, LogPrefix, "View settings loaded and applied successfully");
        }, new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error loading and applying settings" });

    private void UpdateSetting<T>(T value, Action<T> setter, string propertyName,
                                Func<T, bool> validator, T defaultValue, string errorMessage)
    {
        if (!validator(value))
        {
            Log(LogLevel.Warning, LogPrefix, $"Attempt to set {errorMessage}: {value}");
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

        if (_renderer != null && typeof(T) == typeof(RenderStyle))
            _renderer.UpdateRenderStyle((RenderStyle)(object)value);
    }

    private void UpdateAnalyzerSettings(SpectrumScale scale)
    {
        if (_analyzer == null) return;

        try
        {
            _analyzer.UpdateSettings(_mainController.WindowType, scale);
            _analyzer.ReprocessLastData();
            RequestRender();
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error,
                LogPrefix,
                $"Error updating analyzer settings: {ex.Message}");
        }
    }

    #endregion

    #region IDisposable Implementation

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        Safe(() =>
        {
            if (_renderer != null)
            {
                _renderer.Dispose();
                _renderer = null;
            }

            _renderElement = null;
        }, new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error during view controller disposal" });
    }

    #endregion
}