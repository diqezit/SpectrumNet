#nullable enable

using System.Windows.Navigation;

namespace SpectrumNet;

public partial class MainWindow : Window, IAsyncDisposable
{
    private const string LogPrefix = nameof(MainWindow);

    private readonly IControllerProvider _controllerProvider;
    private readonly IMainController _controller;
    private readonly Dictionary<string, Action> _windowButtonActions;
    private readonly SemaphoreSlim _disposeLock = new(1, 1);
    private readonly ManualResetEventSlim _disposeCompleted = new(false);
    private readonly ISettings _settings;
    private readonly IThemes _themeManager;
    private readonly ISmartLogger _logger = Instance;
    private readonly IPerformanceMetricsManager _performanceMetricsManager = PerformanceMetricsManager.Instance;

    private PropertyChangedEventHandler? _themePropertyChangedHandler;

    private bool _isDisposed, _isClosing;
    private CancellationTokenSource? _closingCts;

    public MainWindow()
    {
        InitializeComponent();

        _controllerProvider = new ControllerFactory(this, spectrumCanvas);
        _controller = _controllerProvider.MainController;
        _settings = SettingsProvider.Instance.Settings;
        _themeManager = ThemeManager.Instance;
        DataContext = _controller;

        _windowButtonActions = new Dictionary<string, Action>
        {
            ["MinimizeButton"] = () => WindowState = WindowState.Minimized,
            ["MaximizeButton"] = ToggleWindowState,
            ["CloseButton"] = Close,
            ["OpenControlPanelButton"] = _controller.ToggleControlPanel
        };

        InitEventHandlers();
        ConfigureTheme();
        CompositionTarget.Rendering += OnRendering;

        _controller.InputController.RegisterWindow(this);

        this.PreviewKeyDown += OnPreviewKeyDown;

        spectrumCanvas.MouseDown += (s, e) => _controller.InputController.HandleMouseDown(s, e);
        spectrumCanvas.MouseMove += (s, e) => _controller.InputController.HandleMouseMove(s, e);
        spectrumCanvas.MouseUp += (s, e) => _controller.InputController.HandleMouseUp(s, e);
        spectrumCanvas.MouseEnter += (s, e) => _controller.InputController.HandleMouseEnter(s, e);
        spectrumCanvas.MouseLeave += (s, e) => _controller.InputController.HandleMouseLeave(s, e);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && !e.IsRepeat)
        {
            bool handled = _controller.InputController.HandleKeyDown(e, Keyboard.FocusedElement);
            if (handled)
            {
                e.Handled = true;
            }
        }
    }

    public void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs? e)
    {
        if (_isClosing || _isDisposed || e == null)
            return;

        _controller.OnPaintSurface(sender, e);
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;

        await CleanupResourcesAsync();
        GC.SuppressFinalize(this);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _logger.Safe(() => HandleClosing(e), LogPrefix, "Error handling window closing");
        base.OnClosing(e);
    }

    private void HandleClosing(CancelEventArgs e)
    {
        if (_isDisposed) return;

        _isClosing = true;
        CompositionTarget.Rendering -= OnRendering;

        if (_closingCts != null && !_disposeCompleted.IsSet)
        {
            e.Cancel = true;
            return;
        }

        _closingCts = new CancellationTokenSource();

        _ = Task.Run(async () =>
        {
            try
            {
                await CleanupResourcesAsync();
                Dispatcher.Invoke(() => Close());
            }
            catch (Exception ex)
            {
                _logger.Error(LogPrefix, $"Error during resource cleanup: {ex.Message}");
            }
        });

        e.Cancel = true;
    }

    private void InitEventHandlers()
    {
        SizeChanged += OnWindowSizeChanged;
        StateChanged += OnStateChanged;
        MouseDoubleClick += OnWindowMouseDoubleClick;
        MouseLeftButtonDown += OnWindowDrag;
        Closed += OnWindowClosed;
        KeyDown += (s, e) => _controller.InputController.HandleKeyDown(e, Keyboard.FocusedElement);
        LocationChanged += OnWindowLocationChanged;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_isClosing || _isDisposed)
            return;

        bool shouldRender = !FpsLimiter.Instance.IsEnabled
            || FpsLimiter.Instance.ShouldRenderFrame();

        if (shouldRender)
        {
            _controller.RequestRender();
            _performanceMetricsManager.RecordFrameTime();
        }
    }

    private void OnButtonClick(object sender, RoutedEventArgs e)
    {
        _logger.Safe(() => HandleButtonClick(sender, e), LogPrefix, "Error handling button click");
    }

    private void HandleButtonClick(object sender, RoutedEventArgs e)
    {
        if (_isDisposed) return;
        if (sender is Button btn && _windowButtonActions.TryGetValue(btn.Name, out var action))
            action();
    }

    private void OnWindowDrag(object? sender, MouseButtonEventArgs e) =>
        _logger.Safe(() => HandleWindowDrag(sender, e), LogPrefix, "Error moving window");

    private void HandleWindowDrag(object? sender, MouseButtonEventArgs e)
    {
        if (_isDisposed
            || _isClosing
            || e.ChangedButton != MouseButton.Left
            || Mouse.LeftButton != MouseButtonState.Pressed)
            return;

        try
        {
            DragMove();
            SaveWindowPosition();
        }
        catch (InvalidOperationException ex)
        {
            _logger.Error(LogPrefix, $"Window drag error: {ex.Message}");
        }
    }

    private void OnWindowMouseDoubleClick(object? sender, MouseButtonEventArgs e) =>
        _logger.Safe(() => HandleWindowMouseDoubleClick(sender, e), LogPrefix, "Error handling double click");

    private void HandleWindowMouseDoubleClick(object? sender, MouseButtonEventArgs e)
    {
        if (_isDisposed || IsCheckBoxOrChild(e.OriginalSource as DependencyObject))
        {
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left)
        {
            if (_controller.Renderer?.ShouldShowPlaceholder == true)
            {
                var position = e.GetPosition(spectrumCanvas);
                var skPoint = new SKPoint((float)position.X, (float)position.Y);
                var placeholder = _controller.Renderer.GetPlaceholder();

                if (placeholder?.HitTest(skPoint) == true)
                {
                    e.Handled = true;
                    return;
                }
            }

            e.Handled = true;
            ToggleWindowState();
        }
    }

    private void ToggleWindowState()
    {
        if (WindowState == WindowState.Maximized)
            WindowState = WindowState.Normal;
        else
            WindowState = WindowState.Maximized;
    }

    private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs e) =>
        _logger.Safe(() => HandleWindowSizeChanged(sender, e), LogPrefix, "Error updating dimensions");

    private void HandleWindowSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_isClosing || _isDisposed) return;

        _controller.UpdateRenderDimensions((int)e.NewSize.Width, (int)e.NewSize.Height);

        if (WindowState == WindowState.Normal)
        {
            _settings.WindowWidth = Width;
            _settings.WindowHeight = Height;
        }
    }

    private void OnStateChanged(object? sender, EventArgs e) =>
        _logger.Safe(() => HandleStateChanged(sender, e), LogPrefix, "Error changing icon");

    private void HandleStateChanged(object? sender, EventArgs e)
    {
        if (MaximizeButton is null || MaximizeIcon is null) return;

        MaximizeIcon.Data = Geometry.Parse(
            WindowState == WindowState.Maximized
                ? "M0,0 L20,0 L20,20 L0,20 Z"
                : "M2,2 H18 V18 H2 Z");

        _settings.WindowState = WindowState;
    }

    private void OnWindowLocationChanged(object? sender, EventArgs e) =>
        _logger.Safe(() => HandleWindowLocationChanged(sender, e), LogPrefix, "Error updating window location");

    private void HandleWindowLocationChanged(object? sender, EventArgs e)
    {
        if (_isDisposed || _isClosing || WindowState != WindowState.Normal) return;
        SaveWindowPosition();
    }

    private void SaveWindowPosition()
    {
        _settings.WindowLeft = Left;
        _settings.WindowTop = Top;
    }

    private static bool IsCheckBoxOrChild(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is CheckBox) return true;
            element = GetParent(element);
        }
        return false;
    }

    private void OnWindowClosed(object? sender, EventArgs e) =>
        _logger.Safe(() => HandleWindowClosed(sender, e), LogPrefix, "Error during window close");

    private void HandleWindowClosed(object? sender, EventArgs e)
    {
        UnsubscribeFromEvents();

        if (_disposeCompleted.IsSet &&
            Application.Current != null &&
            !Application.Current.Dispatcher.HasShutdownStarted)
        {
            Application.Current.Shutdown();
        }
    }

    private void UnsubscribeFromEvents()
    {
        CompositionTarget.Rendering -= OnRendering;
        SizeChanged -= OnWindowSizeChanged;
        StateChanged -= OnStateChanged;
        MouseDoubleClick -= OnWindowMouseDoubleClick;
        MouseLeftButtonDown -= OnWindowDrag;
        Closed -= OnWindowClosed;
        LocationChanged -= OnWindowLocationChanged;
        PreviewKeyDown -= OnPreviewKeyDown;

        if (spectrumCanvas != null)
        {
            spectrumCanvas.MouseDown -= (s, e) => _controller.InputController.HandleMouseDown(s, e);
            spectrumCanvas.MouseMove -= (s, e) => _controller.InputController.HandleMouseMove(s, e);
            spectrumCanvas.MouseUp -= (s, e) => _controller.InputController.HandleMouseUp(s, e);
            spectrumCanvas.MouseEnter -= (s, e) => _controller.InputController.HandleMouseEnter(s, e);
            spectrumCanvas.MouseLeave -= (s, e) => _controller.InputController.HandleMouseLeave(s, e);
        }

        if (_themeManager != null && _themePropertyChangedHandler != null)
        {
            _themeManager.PropertyChanged -= _themePropertyChangedHandler;
            _themePropertyChangedHandler = null;
        }
    }

    private async Task CleanupResourcesAsync()
    {
        if (_isDisposed) return;

        if (!await _disposeLock.WaitAsync(FromSeconds(5)))
        {
            _logger.Warning(LogPrefix, "Failed to acquire dispose lock within timeout");
            return;
        }

        try
        {
            if (_isDisposed) return;
            _isDisposed = true;

            await SaveSettingsDuringCleanupAsync();

            if (_controllerProvider is IDisposable disposableProvider)
                disposableProvider.Dispose();
            if (_controller is IAsyncDisposable asyncDisposableMain)
                await asyncDisposableMain.DisposeAsync();
            else if (_controller is IDisposable disposableMain)
                disposableMain.Dispose();

            UnsubscribePostCleanupEvents();
            _disposeCompleted.Set();
        }
        catch (Exception ex)
        {
            _logger.Error(LogPrefix, $"Error during cleanup: {ex.Message}");
        }
        finally
        {
            _disposeLock.Release();
        }
    }

    private async Task SaveSettingsDuringCleanupAsync()
    {
        bool settingsSaved = await SaveSettingsWithTimeoutAsync(FromSeconds(3));
        if (!settingsSaved)
        {
            _logger.Warning(LogPrefix, "Failed to save settings within timeout, continuing with cleanup");
        }
    }

    private static async Task<bool> SaveSettingsWithTimeoutAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await Task.Run(() =>
            {
                Instance.Safe(() => SettingsWindow.Instance.SaveSettings(),
                    LogPrefix,
                    "Error saving settings");
            }, cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private void UnsubscribePostCleanupEvents()
    {
        Dispatcher.Invoke(() =>
        {
            CompositionTarget.Rendering -= OnRendering;

            if (_themeManager != null && _themePropertyChangedHandler != null)
            {
                _themeManager.PropertyChanged -= _themePropertyChangedHandler;
                _themePropertyChangedHandler = null;
            }
        });
    }

    private void ConfigureTheme()
    {
        if (_themeManager is null) return;

        _themeManager.RegisterWindow(this);
        _themePropertyChangedHandler = OnThemePropertyChanged;
        _themeManager.PropertyChanged += _themePropertyChangedHandler;
        UpdateThemeToggleButtonState();
    }

    private void OnThemePropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        _logger.Safe(() => HandleThemePropertyChanged(sender, e), LogPrefix, "Error handling theme change");

    private void HandleThemePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isDisposed || _isClosing || e.PropertyName != nameof(IThemes.IsDarkTheme))
            return;

        _settings.IsDarkTheme = _themeManager.IsDarkTheme;
        UpdateThemeToggleButtonState();
    }

    private void UpdateThemeToggleButtonState()
    {
        if (ThemeToggleButton is null) return;

        ThemeToggleButton.Checked -= OnThemeToggleButtonChanged;
        ThemeToggleButton.Unchecked -= OnThemeToggleButtonChanged;

        ThemeToggleButton.IsChecked = _themeManager.IsDarkTheme;

        ThemeToggleButton.Checked += OnThemeToggleButtonChanged;
        ThemeToggleButton.Unchecked += OnThemeToggleButtonChanged;
    }

    private void OnThemeToggleButtonChanged(object? sender, RoutedEventArgs e)
        => _controller.ToggleTheme();

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;
        if (_isClosing || _isDisposed) return;
        Process.Start(new ProcessStartInfo
        {
            FileName = e.Uri.AbsoluteUri,
            UseShellExecute = true
        });
    }
}