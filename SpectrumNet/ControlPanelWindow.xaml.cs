#nullable enable

namespace SpectrumNet
{
    public partial class ControlPanelWindow : System.Windows.Window, IDisposable
    {
        private readonly MainWindow _mainWindow;
        private bool _isDisposed;
        private const string LogPrefix = "ControlPanelWindow";

        public ControlPanelWindow(MainWindow mainWindow)
        {
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
            InitializeComponent();
            Loaded += OnLoaded;
            MouseDoubleClick += OnWindowMouseDoubleClick;
            KeyDown += OnKeyDown;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            DataContext = _mainWindow;
        }

        private void OnWindowMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            SmartLogger.Safe(() => {
                if (e.ChangedButton == MouseButton.Left)
                {
                    if (e.OriginalSource is DependencyObject element)
                    {
                        if (IsControlOrChild(element, typeof(Slider)) ||
                            IsControlOrChild(element, typeof(ComboBox)) ||
                            IsControlOrChild(element, typeof(CheckBox)) ||
                            IsControlOrChild(element, typeof(Button)))
                        {
                            return;
                        }
                    }

                    WindowState = WindowState == WindowState.Maximized
                        ? WindowState.Normal
                        : WindowState.Maximized;

                    e.Handled = true;
                }
            }, GetLoggerOptions("Error handling double click"));
        }

        private bool IsControlOrChild(DependencyObject element, Type controlType)
        {
            while (element != null)
            {
                if (controlType.IsInstanceOfType(element))
                    return true;

                element = VisualTreeHelper.GetParent(element);
            }
            return false;
        }

        private void OnButtonClick(object sender, RoutedEventArgs e)
        {
            SmartLogger.Safe(() => {
                if (sender is not Button btn) return;

                switch (btn.Name)
                {
                    case "CloseButton":
                        Close();
                        break;
                    case "MinimizeButton":
                        WindowState = WindowState.Minimized;
                        break;
                    case "MaximizeButton":
                        WindowState = WindowState == WindowState.Maximized
                            ? WindowState.Normal
                            : WindowState.Maximized;
                        break;

                    case "StartCaptureButton":
                        _ = _mainWindow.StartCaptureAsync();
                        break;
                    case "StopCaptureButton":
                        _ = _mainWindow.StopCaptureAsync();
                        break;
                    case "ToggleCaptureButton":
                        _ = _mainWindow.ToggleCaptureAsync();
                        break;

                    case "OverlayButton":
                        ToggleOverlay();
                        break;
                    case "OverlayTopmostButton":
                        _mainWindow.IsOverlayTopmost = !_mainWindow.IsOverlayTopmost;
                        break;
                    case "OpenPopupButton":
                        _mainWindow.IsPopupOpen = true;
                        break;
                    case "OpenSettingsButton":
                        OpenSettings();
                        break;
                    case "ShowPerformanceInfoButton":
                        _mainWindow.ShowPerformanceInfo = !_mainWindow.ShowPerformanceInfo;
                        break;
                    case "ThemeToggleButton":
                        ThemeManager.Instance?.ToggleTheme();
                        break;

                    default:
                        _mainWindow.OnButtonClick(sender, e);
                        break;
                }
            }, GetLoggerOptions("Error handling button click"));
        }

        private void OnSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            SmartLogger.Safe(() => {
                if (!IsLoaded || _mainWindow == null) return;
                if (sender is not Slider slider) return;

                switch (slider.Name)
                {
                    case "barSpacingSlider":
                        _mainWindow.BarSpacing = slider.Value;
                        break;
                    case "barCountSlider":
                        _mainWindow.BarCount = (int)slider.Value;
                        break;
                    case "minDbLevelSlider":
                        _mainWindow.MinDbLevel = (float)slider.Value;
                        break;
                    case "maxDbLevelSlider":
                        _mainWindow.MaxDbLevel = (float)slider.Value;
                        break;
                    case "amplificationFactorSlider":
                        _mainWindow.AmplificationFactor = (float)slider.Value;
                        break;
                    default:
                        _mainWindow.OnSliderValueChanged(sender, e);
                        break;
                }
            }, GetLoggerOptions("Error handling slider change"));
        }

        private void OnComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SmartLogger.Safe(() => {
                if (sender is not ComboBox cb) return;

                switch (cb.Name)
                {
                    case "RenderStyleComboBox" when cb.SelectedItem is RenderStyle rs:
                        _mainWindow.SelectedDrawingType = rs;
                        break;
                    case "FftWindowTypeComboBox" when cb.SelectedItem is FftWindowType wt:
                        _mainWindow.WindowType = wt;
                        break;
                    case "ScaleTypeComboBox" when cb.SelectedItem is SpectrumScale scale:
                        _mainWindow.ScaleType = scale;
                        break;
                    case "RenderQualityComboBox" when cb.SelectedItem is RenderQuality quality:
                        _mainWindow.RenderQuality = quality;
                        break;
                    case "PaletteComboBox" when cb.SelectedItem is Palette palette:
                        _mainWindow.SelectedPalette = palette;
                        break;
                    default:
                        _mainWindow.OnComboBoxSelectionChanged(sender, e);
                        break;
                }
            }, GetLoggerOptions("Error handling selection change"));
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            SmartLogger.Safe(() => {
                if (!IsActive) return;

                switch (e.Key)
                {
                    case Key.Space:
                        if (!(Keyboard.FocusedElement is TextBox ||
                              Keyboard.FocusedElement is PasswordBox ||
                              Keyboard.FocusedElement is ComboBox))
                        {
                            _ = _mainWindow.ToggleCaptureAsync();
                            e.Handled = true;
                        }
                        break;
                    case Key.F10:
                        _mainWindow.RenderQuality = RenderQuality.Low;
                        e.Handled = true;
                        break;
                    case Key.F11:
                        _mainWindow.RenderQuality = RenderQuality.Medium;
                        e.Handled = true;
                        break;
                    case Key.F12:
                        _mainWindow.RenderQuality = RenderQuality.High;
                        e.Handled = true;
                        break;
                    case Key.Escape:
                        Close();
                        e.Handled = true;
                        break;
                }
            }, GetLoggerOptions("Error handling key down"));
        }

        private void OnCheckBoxChanged(object sender, RoutedEventArgs e)
        {
            SmartLogger.Safe(() => {
                if (sender is not CheckBox cb) return;

                switch (cb.Name)
                {
                    case "ShowPerformanceInfoCheckBox":
                        _mainWindow.ShowPerformanceInfo = cb.IsChecked == true;
                        break;
                    case "OverlayTopmostCheckBox":
                        _mainWindow.IsOverlayTopmost = cb.IsChecked == true;
                        break;
                }
            }, GetLoggerOptions("Error handling checkbox change"));
        }

        private void Slider_MouseWheelScroll(object sender, MouseWheelEventArgs e)
        {
            SmartLogger.Safe(() =>
            {
                if (sender is not Slider slider)
                    return;

                // Определяем шаг изменения в зависимости от диапазона слайдера
                double range = slider.Maximum - slider.Minimum;
                double step = range / 100.0; // Базовый шаг - 1% от диапазона

                // Если нажат Ctrl - более точное управление (шаг / 5)
                if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
                    step /= 5.0;
                // Если нажат Shift - более быстрое управление (шаг * 5)
                else if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
                    step *= 5.0;

                // Увеличиваем или уменьшаем значение в зависимости от направления прокрутки
                double newValue = slider.Value + (e.Delta > 0 ? step : -step);

                // Ограничиваем значение в пределах допустимого диапазона
                newValue = Math.Max(slider.Minimum, Math.Min(slider.Maximum, newValue));

                // Применяем новое значение
                slider.Value = newValue;

                // Вызываем событие изменения значения для обработки
                OnSliderValueChanged(slider, new RoutedPropertyChangedEventArgs<double>(slider.Value - step, slider.Value));

                // Отмечаем событие как обработанное
                e.Handled = true;
            }, GetLoggerOptions("Error handling slider mouse wheel"));
        }

        private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            SmartLogger.Safe(() => {
                if (e.ChangedButton == MouseButton.Left)
                {
                    var mousePos = e.GetPosition(TitleBar);
                    var closeButtonBounds = CloseButton.TransformToAncestor(TitleBar)
                        .TransformBounds(new Rect(0, 0, CloseButton.ActualWidth, CloseButton.ActualHeight));

                    if (!closeButtonBounds.Contains(mousePos))
                    {
                        DragMove();
                    }
                }
            }, GetLoggerOptions("Error handling window drag"));
        }

        private void ToggleOverlay()
        {
            if (_mainWindow.IsOverlayActive)
            {
                _mainWindow.CloseOverlay();
            }
            else
            {
                _mainWindow.OpenOverlay();
            }
        }

        private void OpenSettings()
        {
            SmartLogger.Safe(() => {
                new SettingsWindow().ShowDialog();
            }, GetLoggerOptions("Error opening settings"));
        }

        private SmartLogger.ErrorHandlingOptions GetLoggerOptions(string errorMessage) =>
            new SmartLogger.ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = errorMessage
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
                Loaded -= OnLoaded;
            }

            _isDisposed = true;
        }

        ~ControlPanelWindow()
        {
            Dispose(false);
        }
    }
}