#nullable enable

using System.Timers;
using static System.Environment;
using static System.Windows.Media.Geometry;
using static System.Windows.Media.VisualTreeHelper;
using static SpectrumNet.SmartLogger;

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

            _controller = new AudioVisualizationController(this, spectrumCanvas);
            DataContext = _controller;

            _windowButtonActions = new Dictionary<string, Action>
            {
                { "MinimizeButton", () => _controller.MinimizeWindow() },
                { "MaximizeButton", () => _controller.MaximizeWindow() },
                { "CloseButton", () => _controller.CloseWindow() },
                { "OpenControlPanelButton", () => _controller.ToggleControlPanel() }
            };

            InitEventHandlers();
            ConfigureTheme();
            CompositionTarget.Rendering += OnRendering;
        }

        public void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs? e) // Заменяем SKPaintGLSurfaceEventArgs на SKPaintSurfaceEventArgs
                => _controller.OnPaintSurface(sender, e);

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
            Safe(() =>
            {
                if (MaximizeButton is null || MaximizeIcon is null) return;

                MaximizeIcon.Data = Parse(WindowState == WindowState.Maximized
                    ? "M0,0 L20,0 L20,20 L0,20 Z"
                    : "M2,2 H18 V18 H2 Z");

                Settings.Instance.WindowState = WindowState;
            }, new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error changing icon" });

        private void OnThemeToggleButtonChanged(object? sender, RoutedEventArgs? e) =>
            _controller.ToggleTheme();

        private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs? e) =>
            Safe(() =>
            {
                if (e is null) return;

                _controller.UpdateRenderDimensions((int)e.NewSize.Width, (int)e.NewSize.Height);

                if (WindowState == WindowState.Normal)
                {
                    var settings = Settings.Instance;
                    settings.WindowWidth = Width;
                    settings.WindowHeight = Height;
                }
            }, new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error updating dimensions" });

        public void OnButtonClick(object sender, RoutedEventArgs e) =>
            Safe(() =>
            {
                if (sender is Button btn && _windowButtonActions.TryGetValue(btn.Name, out var action))
                    action();
            }, new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error handling button click" });

        private void OnWindowMouseDoubleClick(object? sender, MouseButtonEventArgs? e) =>
            Safe(() =>
            {
                if (e is null) return;

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
            }, new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error handling double click" });

        private bool IsCheckBoxOrChild(DependencyObject? element)
        {
            while (element is not null)
            {
                if (element is CheckBox) return true;
                element = GetParent(element);
            }
            return false;
        }

        public void OnKeyDown(object sender, KeyEventArgs e) =>
            Safe(() =>
            {
                if (!IsActive) return;

                if (_controller.HandleKeyDown(e, Keyboard.FocusedElement))
                {
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.Escape && WindowState == WindowState.Maximized)
                {
                    WindowState = WindowState.Normal;
                    e.Handled = true;
                }
            }, new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error handling key down" });

        private void OnWindowDrag(object? sender, MouseButtonEventArgs? e) =>
            Safe(() =>
            {
                if (e?.ChangedButton == MouseButton.Left)
                {
                    DragMove();

                    if (WindowState == WindowState.Normal)
                        SaveWindowPosition();
                }
            }, new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error moving window" });

        private void OnWindowLocationChanged(object? sender, EventArgs e) =>
            Safe(() =>
            {
                if (WindowState == WindowState.Normal)
                    SaveWindowPosition();
            }, new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error updating window location" });

        private void SaveWindowPosition() =>
            Safe(() =>
            {
                var settings = Settings.Instance;
                settings.WindowLeft = Left;
                settings.WindowTop = Top;
            }, new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error saving window position" });

        private void OnWindowClosed(object? sender, EventArgs? e)
        {
            Log(LogLevel.Information, LogPrefix, "Window closed event received");

            try
            {
                UnsubscribeFromEvents();
                Safe(() => SettingsWindow.Instance.SaveSettings(),
                    new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error saving settings on window close" });

                StartForcedExitTimer();
                DisposeControllerAsync();
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, LogPrefix, $"Error during window closing: {ex.Message}");
                Exit(1);
            }
        }

        private void StartForcedExitTimer()
        {
            var exitTimer = new System.Timers.Timer(2000);
            exitTimer.Elapsed += OnForceExitTimerElapsed;
            exitTimer.AutoReset = false;
            exitTimer.Start();
        }

        private void OnForceExitTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            Log(LogLevel.Information, LogPrefix, "Forced exit timer triggered");
            Exit(0);
        }

        private void DisposeControllerAsync() =>
            Task.Run(() => SafeDispose(_controller, "controller",
                new ErrorHandlingOptions
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

            if (_themePropertyChangedHandler is not null && ThemeManager.Instance is not null)
                ThemeManager.Instance.PropertyChanged -= _themePropertyChangedHandler;
        }
        #endregion

        #region Theme Management
        private void ConfigureTheme()
        {
            if (ThemeManager.Instance is { } tm)
            {
                tm.RegisterWindow(this);
                _themePropertyChangedHandler = OnThemePropertyChanged;
                tm.PropertyChanged += _themePropertyChangedHandler;
                UpdateThemeToggleButtonState();
            }
        }

        private void UpdateThemeToggleButtonState()
        {
            if (ThemeToggleButton is null) return;

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
                Settings.Instance.IsDarkTheme = tm.IsDarkTheme;
                UpdateThemeToggleButtonState();
            }
        }
        #endregion
    }
}