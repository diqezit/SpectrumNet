#nullable enable

namespace SpectrumNet;

public partial class ControlPanelWindow : Window, IDisposable
{
    private const string LogPrefix = nameof(ControlPanelWindow);

    private readonly IMainController _controller;
    private readonly IThemes _themeManager;
    private readonly Dictionary<string, Action> _buttonActions;
    private readonly Dictionary<string, Action<double>> _sliderActions;
    private readonly Dictionary<(string Name, Type ItemType), Action<object>> _comboBoxActions;
    private readonly Dictionary<string, Action<bool>> _checkBoxActions;
    private readonly ISmartLogger _logger = Instance;
    private bool _isDisposed;
    private readonly bool _isInitializingControls = true;

    public ControlPanelWindow(IMainController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        _controller = controller;
        _themeManager = ThemeManager.Instance;
        InitializeComponent();

        _buttonActions = CreateButtonActionsMap();
        _sliderActions = CreateSliderActionsMap();
        _comboBoxActions = CreateComboBoxActionsMap();
        _checkBoxActions = CreateCheckBoxActionsMap();

        DataContext = _controller;

        MouseDoubleClick += (s, e) => _controller.InputController.HandleMouseDoubleClick(s, e);
        KeyDown += (s, e) => _controller.InputController.HandleKeyDown(e, Keyboard.FocusedElement);

        SetupGainControlsPopup();
        SetInitialComboBoxSelections();
        _isInitializingControls = false;

        _controller.InputController.RegisterWindow(this);
        _themeManager.RegisterWindow(this);
    }

    private void SetInitialComboBoxSelections()
    {
        if (RenderQualityComboBox is not null)
        {
            RenderQualityComboBox.SelectedItem = _controller.RenderQuality;
        }
        if (RenderStyleComboBox is not null)
        {
            RenderStyleComboBox.SelectedItem = _controller.SelectedDrawingType;
        }
        if (FftWindowTypeComboBox is not null)
        {
            FftWindowTypeComboBox.SelectedItem = _controller.WindowType;
        }
        if (ScaleTypeComboBox is not null)
        {
            ScaleTypeComboBox.SelectedItem = _controller.ScaleType;
        }
    }

    private void SetupGainControlsPopup() =>
        _logger.Safe(() => HandleSetupGainControlsPopup(), LogPrefix, "Error setting up gain controls popup");

    private void HandleSetupGainControlsPopup()
    {
        if (GainControlsPopup is null) return;

        GainControlsPopup.Opened += OnGainControlsPopupOpened;
        GainControlsPopup.Closed += OnGainControlsPopupClosed;
        GainControlsPopup.MouseDown += OnGainControlsPopupMouseDown;
    }

    private void OnGainControlsPopupOpened(object? sender, EventArgs e) =>
        Mouse.Capture(GainControlsPopup, CaptureMode.SubTree);

    private void OnGainControlsPopupClosed(object? sender, EventArgs e) =>
        Mouse.Capture(null);

    private void OnGainControlsPopupMouseDown(object sender, MouseButtonEventArgs e) =>
        _logger.Safe(() => HandleGainControlsPopupMouseDown(sender, e), LogPrefix, "Error handling gain controls popup mouse down");

    private void HandleGainControlsPopupMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement { Name: "GainControlsPopup" })
        {
            _controller.IsPopupOpen = false;
            e.Handled = true;
        }
    }

    private void OnButtonClick(object sender, RoutedEventArgs e) =>
        _logger.Safe(() => HandleButtonClick(sender, e), LogPrefix, "Error handling button click");

    private void HandleButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Name: var btnName } button && _buttonActions.TryGetValue(btnName, out var action))
        {
            action();
        }
    }

    private void OnSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        _logger.Safe(() => HandleSliderValueChanged(sender, e), LogPrefix, "Error handling slider change");

    private void HandleSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || sender is not Slider slider) return;

        if (_sliderActions.TryGetValue(slider.Name, out var action))
        {
            action(slider.Value);
        }
    }

    private void OnComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        _logger.Safe(() => HandleComboBoxSelectionChanged(sender, e), LogPrefix, "Error handling selection change");

    private void HandleComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializingControls)
            return;

        if (sender is not ComboBox { SelectedItem: var selectedItem } comboBox || selectedItem is null)
            return;

        var key = (comboBox.Name, selectedItem.GetType());
        if (_comboBoxActions.TryGetValue(key, out var action))
        {
            action(selectedItem);
        }
    }

    private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        _logger.Safe(() => HandleTitleBarMouseLeftButtonDown(sender, e), LogPrefix, "Error handling window drag");

    private void HandleTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        var mousePos = e.GetPosition(TitleBar);
        var closeButtonBounds = CloseButton.TransformToAncestor(TitleBar)
            .TransformBounds(new Rect(0, 0, CloseButton.ActualWidth, CloseButton.ActualHeight));

        if (!closeButtonBounds.Contains(mousePos))
            DragMove();
    }

    private void OnFavoriteButtonClick(object sender, RoutedEventArgs e) =>
        _logger.Safe(() => HandleFavoriteButtonClick(sender, e), LogPrefix, "Error handling favorite button click");

    private void HandleFavoriteButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is RenderStyle style)
        {
            var favorites = Settings.Instance.FavoriteRenderers;
            bool isFavorite = favorites.Contains(style);

            if (isFavorite)
                favorites.Remove(style);
            else
                favorites.Add(style);

            SettingsWindow.Instance.SaveSettings();
            _controller.OnPropertyChanged(nameof(_controller.OrderedDrawingTypes));
            var currentSelection = RenderStyleComboBox.SelectedItem;

            RenderStyleComboBox.ItemsSource = null;
            RenderStyleComboBox.ItemsSource = _controller.OrderedDrawingTypes;
            RenderStyleComboBox.SelectedItem = currentSelection;
            RenderStyleComboBox.UpdateLayout();
        }
    }

    private Dictionary<string, Action> CreateButtonActionsMap() => new()
    {
        ["CloseButton"] = () =>
        {
            _controller.InputController.UnregisterWindow(this);
            Close();
        },
        ["StartCaptureButton"] = () => _ = _controller.StartCaptureAsync(),
        ["StopCaptureButton"] = () => _ = _controller.StopCaptureAsync(),
        ["OverlayButton"] = () =>
        {
            if (_controller.IsOverlayActive)
                _controller.CloseOverlay();
            else
                _controller.OpenOverlay();
        },
        ["OpenSettingsButton"] = () => new SettingsWindow().ShowDialog(),
        ["OpenPopupButton"] = () => _controller.IsPopupOpen = true
    };

    private Dictionary<string, Action<double>> CreateSliderActionsMap() => new()
    {
        ["barSpacingSlider"] = value => _controller.BarSpacing = value,
        ["barCountSlider"] = value => _controller.BarCount = (int)value,
        ["minDbLevelSlider"] = value => _controller.MinDbLevel = (float)value,
        ["maxDbLevelSlider"] = value => _controller.MaxDbLevel = (float)value,
        ["amplificationFactorSlider"] = value => _controller.AmplificationFactor = (float)value
    };

    private Dictionary<(string Name, Type ItemType), Action<object>> CreateComboBoxActionsMap() => new()
    {
        [("RenderStyleComboBox", typeof(RenderStyle))] = item => _controller.SelectedDrawingType = (RenderStyle)item,
        [("FftWindowTypeComboBox", typeof(FftWindowType))] = item => _controller.WindowType = (FftWindowType)item,
        [("ScaleTypeComboBox", typeof(SpectrumScale))] = item => _controller.ScaleType = (SpectrumScale)item,
        [("RenderQualityComboBox", typeof(RenderQuality))] = item => _controller.RenderQuality = (RenderQuality)item
    };

    private Dictionary<string, Action<bool>> CreateCheckBoxActionsMap() => new()
    {
        ["ShowPerformanceInfoCheckBox"] = value => _controller.ShowPerformanceInfo = value,
        ["OverlayTopmostCheckBox"] = value => _controller.IsOverlayTopmost = value
    };

    public void Dispose()
    {
        if (_isDisposed) return;

        _logger.Safe(() => HandleDispose(), LogPrefix, "Error during dispose");
        GC.SuppressFinalize(this);
    }

    private void HandleDispose()
    {
        _controller.InputController.UnregisterWindow(this);

        if (GainControlsPopup is not null)
        {
            GainControlsPopup.Opened -= OnGainControlsPopupOpened;
            GainControlsPopup.Closed -= OnGainControlsPopupClosed;
            GainControlsPopup.MouseDown -= OnGainControlsPopupMouseDown;
        }

        _isDisposed = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _themeManager.UnregisterWindow(this);
        Dispose();
    }

    ~ControlPanelWindow() => Dispose();
}