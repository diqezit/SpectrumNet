#nullable enable

namespace SpectrumNet
{
    public partial class MainWindow : Window
    {
        private const string LogPrefix = "MainWindow";
        private readonly Dictionary<string, Action> _windowButtonActions;
        private readonly AudioVisualizationController _controller;
        private PropertyChangedEventHandler? _themePropertyChangedHandler;

        public MainWindow()
        {
            InitializeComponent();

            _windowButtonActions = new Dictionary<string, Action>
            {
                ["MinimizeButton"] = () => _controller.MinimizeWindow(),
                ["MaximizeButton"] = () => _controller.MaximizeWindow(),
                ["CloseButton"] = () => _controller.CloseWindow(),
                ["OpenControlPanelButton"] = () => _controller.ToggleControlPanel()
            };

            _controller = new AudioVisualizationController(this, spectrumCanvas);

            DataContext = _controller;

            InitEventHandlers();
            ConfigureTheme();
            CompositionTarget.Rendering += OnRendering;
        }

        public void OnPaintSurface(object? sender, SKPaintGLSurfaceEventArgs? e) =>
            _controller.OnPaintSurface(sender, e);

        #region Event Handlers
        private void InitEventHandlers()
        {
            SizeChanged += OnWindowSizeChanged;
            StateChanged += OnStateChanged;
            MouseDoubleClick += OnWindowMouseDoubleClick;
            Closed += OnWindowClosed;
            KeyDown += OnKeyDown;
            LocationChanged += OnWindowLocationChanged;
        }

        private void OnRendering(object? sender, EventArgs? e) =>
            _controller.RequestRender();

        private void OnStateChanged(object? sender, EventArgs? e) =>
            SmartLogger.Safe(() =>
            {
                if (MaximizeButton != null && MaximizeIcon != null)
                {
                    MaximizeIcon.Data = Geometry.Parse(WindowState == WindowState.Maximized
                        ? "M0,0 L20,0 L20,20 L0,20 Z"
                        : "M2,2 H18 V18 H2 Z");

                    SpectrumNet.Settings.Instance.WindowState = WindowState;
                }
            }, new SmartLogger.ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error changing icon" });

        private void OnThemeToggleButtonChanged(object? sender, RoutedEventArgs? e) =>
            _controller.ToggleTheme();

        private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs? e) =>
            SmartLogger.Safe(() =>
            {
                if (e == null) return;

                _controller.UpdateRenderDimensions((int)e.NewSize.Width, (int)e.NewSize.Height);

                if (WindowState == WindowState.Normal)
                {
                    var settings = SpectrumNet.Settings.Instance;
                    settings.WindowWidth = Width;
                    settings.WindowHeight = Height;
                }
            }, new SmartLogger.ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error updating dimensions" });

        public void OnButtonClick(object sender, RoutedEventArgs e) =>
            SmartLogger.Safe(() =>
            {
                if (sender is Button btn && _windowButtonActions.TryGetValue(btn.Name, out var action))
                    action();
            }, new SmartLogger.ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error handling button click" });

        private void OnWindowMouseDoubleClick(object? sender, MouseButtonEventArgs? e) =>
            SmartLogger.Safe(() =>
            {
                if (e == null) return;

                if (IsCheckBoxOrChild(e.OriginalSource as DependencyObject))
                {
                    e.Handled = true;
                    return;
                }

                if (e.ChangedButton == MouseButton.Left)
                {
                    e.Handled = true;
                    _controller.MaximizeWindow();
                }
            }, new SmartLogger.ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error handling double click" });

        private bool IsCheckBoxOrChild(DependencyObject? element)
        {
            while (element != null)
            {
                if (element is CheckBox) return true;
                element = VisualTreeHelper.GetParent(element);
            }
            return false;
        }

        public void OnKeyDown(object sender, KeyEventArgs e) =>
            SmartLogger.Safe(() =>
            {
                if (!IsActive) return;

                if (_controller.HandleKeyDown(e, Keyboard.FocusedElement))
                {
                    e.Handled = true;
                    return;
                }

                switch (e.Key)
                {
                    case Key.Escape when WindowState == WindowState.Maximized:
                        WindowState = WindowState.Normal;
                        e.Handled = true;
                        break;
                }
            }, new SmartLogger.ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error handling key down" });

        private void OnWindowDrag(object? sender, MouseButtonEventArgs? e) =>
            SmartLogger.Safe(() =>
            {
                if (e?.ChangedButton == MouseButton.Left)
                {
                    DragMove();

                    if (WindowState == WindowState.Normal)
                        SaveWindowPosition();
                }
            }, new SmartLogger.ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error moving window" });

        private void OnWindowLocationChanged(object? sender, EventArgs e) =>
            SmartLogger.Safe(() =>
            {
                if (WindowState == WindowState.Normal)
                    SaveWindowPosition();
            }, new SmartLogger.ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error updating window location" });

        private void SaveWindowPosition() =>
            SmartLogger.Safe(() =>
            {
                var settings = SpectrumNet.Settings.Instance;
                settings.WindowLeft = Left;
                settings.WindowTop = Top;
            }, new SmartLogger.ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error saving window position" });

        private void OnWindowClosed(object? sender, EventArgs? e)
        {
            SmartLogger.Log(LogLevel.Information, LogPrefix, "Window closed event received");

            try
            {
                UnsubscribeFromEvents();
                SmartLogger.Safe(() => SettingsWindow.Instance.SaveSettings(),
                    new SmartLogger.ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error saving settings on window close" });

                StartForcedExitTimer();
                DisposeControllerAsync();
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error during window closing: {ex.Message}");
                Environment.Exit(1);
            }
        }

        private void StartForcedExitTimer()
        {
            var exitTimer = new System.Timers.Timer(2000);
            exitTimer.Elapsed += OnForceExitTimerElapsed;
            exitTimer.AutoReset = false;
            exitTimer.Start();
        }

        private void OnForceExitTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            SmartLogger.Log(LogLevel.Information, LogPrefix, "Forced exit timer triggered");
            Environment.Exit(0);
        }

        private void DisposeControllerAsync() =>
            Task.Run(() => SmartLogger.SafeDispose(_controller, "controller",
                new SmartLogger.ErrorHandlingOptions
                {
                    Source = LogPrefix,
                    ErrorMessage = "Error during controller disposal"
                }));

        private void UnsubscribeFromEvents()
        {
            CompositionTarget.Rendering -= OnRendering;
            SizeChanged -= OnWindowSizeChanged;
            StateChanged -= OnStateChanged;
            MouseDoubleClick -= OnWindowMouseDoubleClick;
            Closed -= OnWindowClosed;
            LocationChanged -= OnWindowLocationChanged;
            KeyDown -= OnKeyDown;

            if (_themePropertyChangedHandler != null && ThemeManager.Instance != null)
                ThemeManager.Instance.PropertyChanged -= _themePropertyChangedHandler;
        }
        #endregion

        #region Theme Management
        private void ConfigureTheme()
        {
            var tm = ThemeManager.Instance;
            if (tm != null)
            {
                tm.RegisterWindow(this);
                _themePropertyChangedHandler = OnThemePropertyChanged;
                tm.PropertyChanged += _themePropertyChangedHandler;
                UpdateThemeToggleButtonState();
            }
        }

        private void UpdateThemeToggleButtonState()
        {
            if (ThemeToggleButton == null) return;

            ThemeToggleButton.Checked -= OnThemeToggleButtonChanged;
            ThemeToggleButton.Unchecked -= OnThemeToggleButtonChanged;

            ThemeToggleButton.IsChecked = ThemeManager.Instance?.IsDarkTheme ?? false;

            ThemeToggleButton.Checked += OnThemeToggleButtonChanged;
            ThemeToggleButton.Unchecked += OnThemeToggleButtonChanged;
        }

        private void OnThemePropertyChanged(object? sender, PropertyChangedEventArgs? e)
        {
            if (e?.PropertyName == nameof(ThemeManager.IsDarkTheme) && sender is ThemeManager tm)
            {
                SpectrumNet.Settings.Instance.IsDarkTheme = tm.IsDarkTheme;
                UpdateThemeToggleButtonState();
            }
        }
        #endregion
    }
}