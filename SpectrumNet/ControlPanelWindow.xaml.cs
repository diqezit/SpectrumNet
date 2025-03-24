#nullable enable

using static SpectrumNet.SmartLogger;

namespace SpectrumNet
{
    public partial class ControlPanelWindow : Window, IDisposable
    {
        private const string LogPrefix = "ControlPanelWindow";

        private readonly IAudioVisualizationController _controller;
        private readonly Dictionary<string, Action> _buttonActions;
        private readonly Dictionary<string, Action<double>> _sliderActions;
        private readonly Dictionary<(string Name, Type ItemType), Action<object>> _comboBoxActions;
        private readonly Dictionary<string, Action<bool>> _checkBoxActions;
        private bool _isDisposed;

        public ControlPanelWindow(IAudioVisualizationController controller)
        {
            ArgumentNullException.ThrowIfNull(controller);
            _controller = controller;
            InitializeComponent();

#if DEBUG
            Log(LogLevel.Debug, LogPrefix, "Creating control panel window");
#endif

            _buttonActions = CreateButtonActionsMap();
            _sliderActions = CreateSliderActionsMap();
            _comboBoxActions = CreateComboBoxActionsMap();
            _checkBoxActions = CreateCheckBoxActionsMap();

            DataContext = _controller;

            MouseDoubleClick += OnWindowMouseDoubleClick;
            KeyDown += OnKeyDown;
            SetupGainControlsPopup();
        }

        #region Event Setup

        private void SetupGainControlsPopup() =>
            Safe(() =>
            {
                if (GainControlsPopup is null) return;

                GainControlsPopup.Opened += OnGainControlsPopupOpened;
                GainControlsPopup.Closed += OnGainControlsPopupClosed;
                GainControlsPopup.MouseDown += OnGainControlsPopupMouseDown;
#if DEBUG
                Log(LogLevel.Debug, LogPrefix, "Gain controls popup setup complete");
#endif
            }, GetLoggerOptions("Error setting up gain controls popup"));

        private void OnGainControlsPopupOpened(object? sender, EventArgs e) =>
            Mouse.Capture(GainControlsPopup, CaptureMode.SubTree);

        private void OnGainControlsPopupClosed(object? sender, EventArgs e) =>
            Mouse.Capture(null);

        #endregion

        #region Event Handlers

        private void OnGainControlsPopupMouseDown(object sender, MouseButtonEventArgs e) =>
            Safe(() =>
            {
                if (e.OriginalSource is FrameworkElement { Name: "GainControlsPopup" })
                {
                    _controller.IsPopupOpen = false;
                    e.Handled = true;
                }
            }, GetLoggerOptions("Error handling gain controls popup mouse down"));

        private void OnWindowMouseDoubleClick(object sender, MouseButtonEventArgs e) =>
            Safe(() =>
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
#if DEBUG
                Log(LogLevel.Debug, LogPrefix, "Window state changed to: {0}", WindowState);
#endif
            }, GetLoggerOptions("Error handling double click"));

        private void OnButtonClick(object sender, RoutedEventArgs e) =>
            Safe(() =>
            {
                if (sender is Button { Name: var btnName } button && _buttonActions.TryGetValue(btnName, out var action))
                {
#if DEBUG
                    Log(LogLevel.Debug, LogPrefix, "Button clicked: {0}", btnName);
#endif
                    action();
                }
            }, GetLoggerOptions("Error handling button click"));

        private void OnSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
            Safe(() =>
            {
                if (!IsLoaded || sender is not Slider slider) return;

                if (_sliderActions.TryGetValue(slider.Name, out var action))
                {
#if DEBUG
                    Log(LogLevel.Debug, LogPrefix, "Slider changed: {0}, Value: {1}", slider.Name, slider.Value);
#endif
                    action(slider.Value);
                }
            }, GetLoggerOptions("Error handling slider change"));

        private void OnComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e) =>
            Safe(() =>
            {
                if (sender is not ComboBox { SelectedItem: var selectedItem } comboBox || selectedItem is null)
                    return;

                var key = (comboBox.Name, selectedItem.GetType());
                if (_comboBoxActions.TryGetValue(key, out var action))
                {
#if DEBUG
                    Log(LogLevel.Debug, LogPrefix, "ComboBox selection changed: {0}, Selected: {1}", 
                        comboBox.Name, selectedItem);
#endif
                    action(selectedItem);
                }
            }, GetLoggerOptions("Error handling selection change"));

        private void OnKeyDown(object sender, KeyEventArgs e) =>
            Safe(() =>
            {
                if (!IsActive) return;

                if (_controller.HandleKeyDown(e, Keyboard.FocusedElement))
                {
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.Escape)
                {
#if DEBUG
                    Log(LogLevel.Debug, LogPrefix, "Escape key pressed, closing window");
#endif
                    Close();
                    e.Handled = true;
                }
            }, GetLoggerOptions("Error handling key down"));

        private void OnCheckBoxChanged(object sender, RoutedEventArgs e) =>
            Safe(() =>
            {
                if (sender is CheckBox { Name: var cbName, IsChecked: var isChecked } checkbox &&
                    _checkBoxActions.TryGetValue(cbName, out var action))
                {
#if DEBUG
                    Log(LogLevel.Debug, LogPrefix, "CheckBox changed: {0}, Value: {1}", cbName, isChecked);
#endif
                    action(isChecked == true);
                }
            }, GetLoggerOptions("Error handling checkbox change"));

        private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
            Safe(() =>
            {
                if (e.ChangedButton != MouseButton.Left) return;

                var mousePos = e.GetPosition(TitleBar);
                var closeButtonBounds = CloseButton.TransformToAncestor(TitleBar)
                    .TransformBounds(new Rect(0, 0, CloseButton.ActualWidth, CloseButton.ActualHeight));

                if (!closeButtonBounds.Contains(mousePos))
                    DragMove();
            }, GetLoggerOptions("Error handling window drag"));


        private void OnFavoriteButtonClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is RenderStyle style)
            {
                var favorites = Settings.Instance.FavoriteRenderers;
                bool isFavorite = favorites.Contains(style);

#if DEBUG
                Log(LogLevel.Debug, LogPrefix, "{0} render style {1} from favorites", 
                    isFavorite ? "Removing" : "Adding", style);
#endif

                if (isFavorite)
                    favorites.Remove(style);
                else
                    favorites.Add(style);
            }
        }
        #endregion

        #region Helper Methods

        private bool IsControlOfType<T>(DependencyObject element) where T : DependencyObject
        {
            while (element is not null)
            {
                if (element is T) return true;
                element = VisualTreeHelper.GetParent(element);
            }
            return false;
        }

        private void ToggleOverlay() =>
            Safe(() =>
            {
#if DEBUG
                Log(LogLevel.Debug, LogPrefix, "Toggling overlay, current state: {0}", 
                    _controller.IsOverlayActive ? "Active" : "Inactive");
#endif
                if (_controller.IsOverlayActive)
                    _controller.CloseOverlay();
                else
                    _controller.OpenOverlay();
            }, GetLoggerOptions("Error toggling overlay"));

        private void OpenSettings() =>
            Safe(() =>
            {
#if DEBUG
                Log(LogLevel.Debug, LogPrefix, "Opening settings window");
#endif
                new SettingsWindow().ShowDialog();
            }, GetLoggerOptions("Error opening settings"));

        private ErrorHandlingOptions GetLoggerOptions(string errorMessage) =>
            new() { Source = LogPrefix, ErrorMessage = errorMessage };

        #endregion

        #region Action Maps

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

        #endregion

        #region IDisposable Implementation

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
#if DEBUG
                Log(LogLevel.Debug, LogPrefix, "Disposing control panel window");
#endif
                MouseDoubleClick -= OnWindowMouseDoubleClick;
                KeyDown -= OnKeyDown;

                if (GainControlsPopup is not null)
                {
                    GainControlsPopup.Opened -= OnGainControlsPopupOpened;
                    GainControlsPopup.Closed -= OnGainControlsPopupClosed;
                    GainControlsPopup.MouseDown -= OnGainControlsPopupMouseDown;
                }
            }

            _isDisposed = true;
        }

        ~ControlPanelWindow() => Dispose(false);

        #endregion
    }
}