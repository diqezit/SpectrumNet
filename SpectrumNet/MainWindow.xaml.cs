#nullable enable

using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace SpectrumNet;

public partial class MainWindow : Window, IAsyncDisposable
{
    private const string LogPrefix = nameof(MainWindow);

    private readonly IMainController _controller;
    private readonly Dictionary<string, Action> _windowButtonActions;
    private readonly SemaphoreSlim _disposeLock = new(1, 1);
    private readonly ManualResetEventSlim _disposeCompleted = new(false);

    private PropertyChangedEventHandler? _themePropertyChangedHandler;

    private bool _isDisposed, _isClosing;
    private CancellationTokenSource? _closingCts;

    public MainWindow()
    {
        InitializeComponent();

        _controller = new ControllerFactory(this, spectrumCanvas);
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

        spectrumCanvas.MouseDown += (s, e) => _controller.InputController.HandleMouseDown(s, e);
        spectrumCanvas.MouseMove += (s, e) => _controller.InputController.HandleMouseMove(s, e);
        spectrumCanvas.MouseUp += (s, e) => _controller.InputController.HandleMouseUp(s, e);
        spectrumCanvas.MouseEnter += (s, e) => _controller.InputController.HandleMouseEnter(s, e);
        spectrumCanvas.MouseLeave += (s, e) => _controller.InputController.HandleMouseLeave(s, e);
    }

    public void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs? e)
    {
        if (_isClosing || _isDisposed)
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
        Safe(() => HandleClosing(e),
            new ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error handling window closing"
            }
        );

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
                Log(LogLevel.Error, LogPrefix, $"Error during resource cleanup: {ex.Message}");
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
            PerformanceMetricsManager.RecordFrameTime();
        }
    }

    private void OnButtonClick(object sender, RoutedEventArgs e)
    {
        Safe(() => HandleButtonClick(sender, e),
            new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error handling button click" }
        );
    }

    private void HandleButtonClick(object sender, RoutedEventArgs e)
    {
        if (_isDisposed) return;
        if (sender is Button btn && _windowButtonActions.TryGetValue(btn.Name, out var action))
            action();
    }

    private void OnWindowDrag(object? sender, MouseButtonEventArgs e)
    {
        Safe(() => HandleWindowDrag(sender, e),
            new ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error moving window"
            }
        );
    }

    private void HandleWindowDrag(object? sender, MouseButtonEventArgs e)
    {
        if (_isDisposed || _isClosing || e.ChangedButton != MouseButton.Left)
            return;

        DragMove();

        if (WindowState == WindowState.Normal)
            SaveWindowPosition();
    }

    private void OnWindowMouseDoubleClick(object? sender, MouseButtonEventArgs e)
    {
        Safe(() => HandleWindowMouseDoubleClick(sender, e),
            new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error handling double click" }
        );
    }

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

    private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        Safe(() => HandleWindowSizeChanged(sender, e),
            new ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error updating dimensions"
            }
        );
    }

    private void HandleWindowSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_isClosing || _isDisposed) return;

        _controller.UpdateRenderDimensions((int)e.NewSize.Width, (int)e.NewSize.Height);

        if (WindowState == WindowState.Normal)
        {
            var settings = Settings.Instance;
            settings.WindowWidth = Width;
            settings.WindowHeight = Height;
        }
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        Safe(() => HandleStateChanged(sender, e),
            new ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error changing icon"
            }
        );
    }

    private void HandleStateChanged(object? sender, EventArgs e)
    {
        if (MaximizeButton is null || MaximizeIcon is null) return;

        MaximizeIcon.Data = Geometry.Parse(
            WindowState == WindowState.Maximized
                ? "M0,0 L20,0 L20,20 L0,20 Z"
                : "M2,2 H18 V18 H2 Z");

        Settings.Instance.WindowState = WindowState;
    }

    private void OnWindowLocationChanged(object? sender, EventArgs e)
    {
        Safe(() => HandleWindowLocationChanged(sender, e),
            new ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error updating window location"
            }
        );
    }

    private void HandleWindowLocationChanged(object? sender, EventArgs e)
    {
        if (_isDisposed || _isClosing || WindowState != WindowState.Normal) return;
        SaveWindowPosition();
    }

    private void SaveWindowPosition()
    {
        var settings = Settings.Instance;
        settings.WindowLeft = Left;
        settings.WindowTop = Top;
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

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        Safe(() => HandleWindowClosed(sender, e),
            new ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error during window close"
            }
        );
    }

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

        spectrumCanvas.MouseDown -= (s, e) => _controller.InputController.HandleMouseDown(s, e);
        spectrumCanvas.MouseMove -= (s, e) => _controller.InputController.HandleMouseMove(s, e);
        spectrumCanvas.MouseUp -= (s, e) => _controller.InputController.HandleMouseUp(s, e);
        spectrumCanvas.MouseEnter -= (s, e) => _controller.InputController.HandleMouseEnter(s, e);
        spectrumCanvas.MouseLeave -= (s, e) => _controller.InputController.HandleMouseLeave(s, e);

        if (ThemeManager.Instance != null && _themePropertyChangedHandler != null)
        {
            ThemeManager.Instance.PropertyChanged -= _themePropertyChangedHandler;
            _themePropertyChangedHandler = null;
        }
    }

    private async Task CleanupResourcesAsync()
    {
        if (_isDisposed) return;

        if (!await _disposeLock.WaitAsync(FromSeconds(5)))
        {
            Log(LogLevel.Warning, LogPrefix, "Failed to acquire dispose lock within timeout");
            return;
        }

        try
        {
            if (_isDisposed) return;
            _isDisposed = true;

            await SaveSettingsDuringCleanupAsync();
            await StopCaptureDuringCleanupAsync();
            await CloseChildWindowsAsync();
            await DisposeControllerDuringCleanupAsync();

            UnsubscribePostCleanupEvents();
            _disposeCompleted.Set();
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, LogPrefix, $"Error during cleanup: {ex.Message}");
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
            Log(LogLevel.Warning, LogPrefix, "Failed to save settings within timeout, continuing with cleanup");
        }
    }

    private async Task StopCaptureDuringCleanupAsync()
    {
        if (_controller.IsRecording)
        {
            bool captureStoppedSuccessfully = await StopCaptureWithTimeoutAsync(FromSeconds(5));
            if (!captureStoppedSuccessfully)
            {
                Log(LogLevel.Warning, LogPrefix, "Failed to stop audio capture within timeout, continuing with cleanup");
            }
        }
    }

    private async Task CloseChildWindowsAsync()
    {
        await Dispatcher.InvokeAsync(() =>
        {
            if (_controller.IsOverlayActive)
                _controller.CloseOverlay();

            if (_controller.IsControlPanelOpen)
                _controller.CloseControlPanel();
        });
    }

    private async Task DisposeControllerDuringCleanupAsync()
    {
        bool controllerDisposed = await DisposeControllerWithTimeoutAsync(FromSeconds(10));
        if (!controllerDisposed)
        {
            Log(LogLevel.Warning, LogPrefix, "Failed to dispose controller within timeout");
        }
    }

    private void UnsubscribePostCleanupEvents()
    {
        Dispatcher.Invoke(() =>
        {
            CompositionTarget.Rendering -= OnRendering;

            if (ThemeManager.Instance != null && _themePropertyChangedHandler != null)
            {
                ThemeManager.Instance.PropertyChanged -= _themePropertyChangedHandler;
                _themePropertyChangedHandler = null;
            }
        });
    }

    private static async Task<bool> SaveSettingsWithTimeoutAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await Task.Run(() =>
            {
                Safe(() => SettingsWindow.Instance.SaveSettings(),
                    new ErrorHandlingOptions
                    {
                        Source = LogPrefix,
                        ErrorMessage = "Error saving settings"
                    });
            }, cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task<bool> StopCaptureWithTimeoutAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await _controller.StopCaptureAsync().WaitAsync(timeout, cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task<bool> DisposeControllerWithTimeoutAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            if (_controller is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().AsTask().WaitAsync(timeout, cts.Token);
            }
            else if (_controller is IDisposable disposable)
            {
                await Task.Run(() => disposable.Dispose(), cts.Token);
            }
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private void ConfigureTheme()
    {
        var tm = ThemeManager.Instance;
        if (tm is null) return;

        tm.RegisterWindow(this);
        _themePropertyChangedHandler = OnThemePropertyChanged;
        tm.PropertyChanged += _themePropertyChangedHandler;
        UpdateThemeToggleButtonState();
    }

    private void OnThemePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Safe(() => HandleThemePropertyChanged(sender, e),
            new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error handling theme change" }
        );
    }

    private void HandleThemePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isDisposed || _isClosing || e.PropertyName != nameof(ThemeManager.IsDarkTheme) ||
            sender is not ThemeManager tm)
            return;

        Settings.Instance.IsDarkTheme = tm.IsDarkTheme;
        UpdateThemeToggleButtonState();
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

    private void OnThemeToggleButtonChanged(object? sender, RoutedEventArgs e)
        => _controller.ToggleTheme();

    private static void Safe(Action action, ErrorHandlingOptions options)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, options.Source!, options.ErrorMessage + ": " + ex.Message);
        }
    }
}