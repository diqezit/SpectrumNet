#nullable enable

namespace SpectrumNet
{
    public partial class ControlPanelWindow : Window, IDisposable
    {
        private readonly IAudioVisualizationController _controller;
        private bool _isDisposed;
        private readonly Dictionary<string, Action> _buttonActions;
        private readonly Dictionary<string, Action<double>> _sliderActions;
        private readonly Dictionary<(string Name, Type ItemType), Action<object>> _comboBoxActions;
        private readonly Dictionary<string, Action<bool>> _checkBoxActions;
        private const string LogPrefix = "ControlPanelWindow";

        public ControlPanelWindow(IAudioVisualizationController controller)
        {
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            InitializeComponent();

            _buttonActions = CreateButtonActionsMap();
            _sliderActions = CreateSliderActionsMap();
            _comboBoxActions = CreateComboBoxActionsMap();
            _checkBoxActions = CreateCheckBoxActionsMap();
            DataContext = _controller;

            MouseDoubleClick += OnWindowMouseDoubleClick;
            KeyDown += OnKeyDown;
            SetupGainControlsPopup();
        }

        private void SetupGainControlsPopup() =>
            SmartLogger.Safe(() =>
            {
                if (GainControlsPopup == null) return;
                GainControlsPopup.Opened += (_, _) => Mouse.Capture(GainControlsPopup, CaptureMode.SubTree);
                GainControlsPopup.Closed += (_, _) => Mouse.Capture(null);
                GainControlsPopup.MouseDown += OnGainControlsPopupMouseDown;
            }, GetLoggerOptions("Error setting up gain controls popup"));

        private void OnGainControlsPopupMouseDown(object sender, MouseButtonEventArgs e) =>
            SmartLogger.Safe(() =>
            {
                if (e.OriginalSource is FrameworkElement element && element.Name == "GainControlsPopup")
                {
                    _controller.IsPopupOpen = false;
                    e.Handled = true;
                }
            }, GetLoggerOptions("Error handling gain controls popup mouse down"));

        private void OnWindowMouseDoubleClick(object sender, MouseButtonEventArgs e) =>
            SmartLogger.Safe(() =>
            {
                if (e.ChangedButton != MouseButton.Left) return;
                if (e.OriginalSource is DependencyObject element &&
                    (IsControlOfType<Slider>(element) ||
                     IsControlOfType<ComboBox>(element) ||
                     IsControlOfType<CheckBox>(element) ||
                     IsControlOfType<Button>(element)))
                    return;

                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                e.Handled = true;
            }, GetLoggerOptions("Error handling double click"));

        private void OnButtonClick(object sender, RoutedEventArgs e) =>
            SmartLogger.Safe(() =>
            {
                if (sender is Button btn && _buttonActions.TryGetValue(btn.Name, out var action))
                    action();
            }, GetLoggerOptions("Error handling button click"));

        private void OnSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
            SmartLogger.Safe(() =>
            {
                if (!IsLoaded || sender is not Slider slider) return;
                if (_sliderActions.TryGetValue(slider.Name, out var action))
                    action(slider.Value);
            }, GetLoggerOptions("Error handling slider change"));

        private void OnComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e) =>
                    SmartLogger.Safe(() =>
                    {
                        if (sender is not ComboBox cb || cb.SelectedItem == null) return;
                        var key = (cb.Name, cb.SelectedItem.GetType());
                        if (_comboBoxActions.TryGetValue(key, out var action))
                            action(cb.SelectedItem);
                    }, GetLoggerOptions("Error handling selection change"));

        private void OnKeyDown(object sender, KeyEventArgs e) =>
            SmartLogger.Safe(() =>
            {
                if (!IsActive) return;

                if (_controller.HandleKeyDown(e, Keyboard.FocusedElement))
                {
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.Escape)
                {
                    Close();
                    e.Handled = true;
                }
            }, GetLoggerOptions("Error handling key down"));

        private void OnCheckBoxChanged(object sender, RoutedEventArgs e) =>
            SmartLogger.Safe(() =>
            {
                if (sender is CheckBox cb && _checkBoxActions.TryGetValue(cb.Name, out var action))
                    action(cb.IsChecked == true);
            }, GetLoggerOptions("Error handling checkbox change"));

        private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
            SmartLogger.Safe(() =>
            {
                if (e.ChangedButton != MouseButton.Left) return;
                var mousePos = e.GetPosition(TitleBar);
                var closeButtonBounds = CloseButton.TransformToAncestor(TitleBar)
                    .TransformBounds(new Rect(0, 0, CloseButton.ActualWidth, CloseButton.ActualHeight));
                if (!closeButtonBounds.Contains(mousePos))
                    DragMove();
            }, GetLoggerOptions("Error handling window drag"));

        private bool IsControlOfType<T>(DependencyObject element) where T : DependencyObject
        {
            while (element != null)
            {
                if (element is T) return true;
                element = VisualTreeHelper.GetParent(element);
            }
            return false;
        }

        private void ToggleOverlay() =>
            SmartLogger.Safe(() =>
            {
                if (_controller.IsOverlayActive) _controller.CloseOverlay();
                else _controller.OpenOverlay();
            }, GetLoggerOptions("Error toggling overlay"));

        private void OpenSettings() =>
            SmartLogger.Safe(() => new SettingsWindow().ShowDialog(),
                GetLoggerOptions("Error opening settings"));

        private SmartLogger.ErrorHandlingOptions GetLoggerOptions(string errorMessage) =>
            new() { Source = LogPrefix, ErrorMessage = errorMessage };

        private Dictionary<string, Action> CreateButtonActionsMap() => new()
        {
            ["CloseButton"] = Close,
            ["StartCaptureButton"] = () => _ = _controller.StartCaptureAsync(),
            ["StopCaptureButton"] = () => _ = _controller.StopCaptureAsync(),
            ["OverlayButton"] = ToggleOverlay,
            ["OpenPopupButton"] = () => _controller.IsPopupOpen = true,
            ["OpenSettingsButton"] = OpenSettings
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
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_isDisposed) return;
            if (disposing)
            {
                MouseDoubleClick -= OnWindowMouseDoubleClick;
                KeyDown -= OnKeyDown;
                GainControlsPopup?.Apply(p => p.MouseDown -= OnGainControlsPopupMouseDown);
            }
            _isDisposed = true;
        }

        ~ControlPanelWindow() => Dispose(false);
    }
}