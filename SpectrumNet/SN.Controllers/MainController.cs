#nullable enable

namespace SpectrumNet.SN.Controllers;

public sealed class MainController
    (IControllerProvider controllerProvider,
    Window ownerWindow)
    : AsyncDisposableBase,
    IMainController
{
    private const string LogPrefix = nameof(MainController);
    private readonly ISmartLogger _logger = Instance;

    private readonly IControllerProvider _controllerProvider = controllerProvider ??
            throw new ArgumentNullException(nameof(controllerProvider));

    private readonly Window _ownerWindow = ownerWindow ??
            throw new ArgumentNullException(nameof(ownerWindow));

    private readonly CancellationTokenSource _cleanupCts = new();
    private readonly SemaphoreSlim _transitionLock = new(1, 1);
    private readonly object _disposeLock = new();

    private bool _isTransitioning;
    private bool _isRecording;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Dispatcher Dispatcher => Application.Current?.Dispatcher ??
        throw new InvalidOperationException("Application.Current is null");

    public bool LimitFpsTo60
    {
        get => _controllerProvider.FpsLimiter.IsLimited;
        set
        {
            if (_isDisposed) return;

            _logger.Log(LogLevel.Information, LogPrefix,
                $"LimitFpsTo60 changing from {_controllerProvider.FpsLimiter.IsLimited} to {value}");

            _controllerProvider.FpsLimiter.SetLimit(value);
        }
    }

    public IGainParametersProvider GainParameters => AudioController.GainParameters;

    public bool IsRecording
    {
        get => _isRecording;
        set
        {
            if (_isDisposed || _isRecording == value) return;
            _isRecording = value;

            AudioController.IsRecording = value;

            OnPropertyChanged(nameof(IsRecording), nameof(CanStartCapture));

            if (ViewController.Renderer != null && !_isDisposed)
                RequestRender();
        }
    }

    public bool CanStartCapture => !_isDisposed && !IsRecording && AudioController.CanStartCapture;

    public bool IsTransitioning
    {
        get => _isTransitioning;
        set
        {
            if (_isDisposed || _isTransitioning == value) return;
            _isTransitioning = value;
            AudioController.IsTransitioning = value;
        }
    }

    public FftWindowType WindowType
    {
        get => AudioController.WindowType;
        set
        {
            if (_isDisposed) return;
            AudioController.WindowType = value;
            OnPropertyChanged(nameof(WindowType));
        }
    }

    public float MinDbLevel
    {
        get => AudioController.MinDbLevel;
        set
        {
            if (_isDisposed) return;
            AudioController.MinDbLevel = value;
        }
    }

    public float MaxDbLevel
    {
        get => AudioController.MaxDbLevel;
        set
        {
            if (_isDisposed) return;
            AudioController.MaxDbLevel = value;
        }
    }

    public float AmplificationFactor
    {
        get => AudioController.AmplificationFactor;
        set
        {
            if (_isDisposed) return;
            AudioController.AmplificationFactor = value;
        }
    }

    public IUIController UIController => _controllerProvider.UIController;
    public IAudioController AudioController => _controllerProvider.AudioController;
    public IViewController ViewController => _controllerProvider.ViewController;
    public IInputController InputController => _controllerProvider.InputController;
    public IOverlayManager OverlayManager => _controllerProvider.OverlayManager;

    public async Task StartCaptureAsync()
    {
        if (_isDisposed) return;
        await AudioController.StartCaptureAsync();
    }

    public async Task StopCaptureAsync()
    {
        if (_isDisposed) return;
        await AudioController.StopCaptureAsync();
    }

    public async Task ToggleCaptureAsync()
    {
        if (_isDisposed) return;
        await AudioController.ToggleCaptureAsync();
    }

    public SpectrumAnalyzer? GetCurrentAnalyzer() =>
        _isDisposed ? null : AudioController.GetCurrentAnalyzer();

    public SKElement SpectrumCanvas => ViewController.SpectrumCanvas;

    public SpectrumBrushes SpectrumStyles => SpectrumBrushes.Instance;

    public int BarCount
    {
        get => ViewController.BarCount;
        set
        {
            if (_isDisposed) return;
            ViewController.BarCount = value;
        }
    }

    public double BarSpacing
    {
        get => ViewController.BarSpacing;
        set
        {
            if (_isDisposed) return;
            ViewController.BarSpacing = value;
        }
    }

    public RenderQuality RenderQuality
    {
        get => ViewController.RenderQuality;
        set
        {
            if (_isDisposed) return;
            ViewController.RenderQuality = value;
        }
    }

    public RenderStyle SelectedDrawingType
    {
        get => ViewController.SelectedDrawingType;
        set
        {
            if (_isDisposed) return;
            ViewController.SelectedDrawingType = value;
        }
    }

    public SpectrumScale ScaleType
    {
        get => ViewController.ScaleType;
        set
        {
            if (_isDisposed) return;
            ViewController.ScaleType = value;
        }
    }

    public string SelectedStyle
    {
        get => ViewController.SelectedStyle;
        set
        {
            if (_isDisposed) return;
            ViewController.SelectedStyle = value;
        }
    }

    public Palette? SelectedPalette
    {
        get => ViewController.SelectedPalette;
        set
        {
            if (_isDisposed) return;
            ViewController.SelectedPalette = value;
        }
    }

    public bool ShowPerformanceInfo
    {
        get => ViewController.ShowPerformanceInfo;
        set
        {
            if (_isDisposed) return;
            ViewController.ShowPerformanceInfo = value;
        }
    }

    public IReadOnlyDictionary<string, Palette> AvailablePalettes => ViewController.AvailablePalettes;
    public IEnumerable<RenderStyle> AvailableDrawingTypes => ViewController.AvailableDrawingTypes;
    public IEnumerable<FftWindowType> AvailableFftWindowTypes => ViewController.AvailableFftWindowTypes;
    public IEnumerable<SpectrumScale> AvailableScaleTypes => ViewController.AvailableScaleTypes;
    public IEnumerable<RenderQuality> AvailableRenderQualities => ViewController.AvailableRenderQualities;
    public IEnumerable<RenderStyle> OrderedDrawingTypes => ViewController.OrderedDrawingTypes;

    public Renderer? Renderer
    {
        get => ViewController.Renderer;
        set => ViewController.Renderer = value;
    }

    public SpectrumAnalyzer Analyzer
    {
        get => ViewController.Analyzer;
        set => ViewController.Analyzer = value;
    }

    public void RequestRender()
    {
        if (_isDisposed) return;
        ViewController.RequestRender();
    }

    public void UpdateRenderDimensions(int width, int height)
    {
        if (_isDisposed) return;
        ViewController.UpdateRenderDimensions(width, height);
    }

    public void SynchronizeVisualization()
    {
        if (_isDisposed) return;
        ViewController.SynchronizeVisualization();
    }

    public void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs? e)
    {
        if (_isDisposed || e == null) return;
        ViewController.OnPaintSurface(sender, e);
    }

    public bool IsOverlayActive
    {
        get => OverlayManager.IsActive;
        set
        {
            if (_isDisposed) return;

            if (value != IsOverlayActive)
            {
                if (value)
                    _ = OverlayManager.OpenAsync();
                else
                    _ = OverlayManager.CloseAsync();
            }
        }
    }

    public bool IsOverlayTopmost
    {
        get => OverlayManager.IsTopmost;
        set
        {
            if (_isDisposed) return;
            OverlayManager.IsTopmost = value;
        }
    }

    public bool IsPopupOpen
    {
        get => UIController.IsPopupOpen;
        set
        {
            if (_isDisposed) return;
            UIController.IsPopupOpen = value;
        }
    }

    public bool IsControlPanelOpen => !_isDisposed && UIController.IsControlPanelOpen;

    public void ToggleTheme()
    {
        if (_isDisposed) return;
        UIController.ToggleTheme();
    }

    public void OpenControlPanel()
    {
        if (_isDisposed) return;
        UIController.OpenControlPanel();
    }

    public void CloseControlPanel()
    {
        if (_isDisposed) return;
        UIController.CloseControlPanel();
    }

    public void ToggleControlPanel()
    {
        if (_isDisposed) return;
        UIController.ToggleControlPanel();
    }

    public void OpenOverlay()
    {
        if (_isDisposed) return;
        _ = OverlayManager.OpenAsync();
    }

    public void CloseOverlay()
    {
        if (_isDisposed) return;
        _ = OverlayManager.CloseAsync();
    }

    public void RegisterWindow(Window window)
    {
        if (_isDisposed) return;
        InputController.RegisterWindow(window);
    }

    public void UnregisterWindow(Window window)
    {
        if (_isDisposed) return;
        InputController.UnregisterWindow(window);
    }

    public bool HandleKeyDown(KeyEventArgs e, IInputElement? focusedElement)
    {
        if (_isDisposed) return false;
        return InputController.HandleKeyDown(e, focusedElement);
    }

    public bool HandleMouseDown(object? sender, MouseButtonEventArgs e)
    {
        if (_isDisposed) return false;
        return InputController.HandleMouseDown(sender, e);
    }

    public bool HandleMouseMove(object? sender, MouseEventArgs e)
    {
        if (_isDisposed) return false;
        return InputController.HandleMouseMove(sender, e);
    }

    public bool HandleMouseUp(object? sender, MouseButtonEventArgs e)
    {
        if (_isDisposed) return false;
        return InputController.HandleMouseUp(sender, e);
    }

    public bool HandleMouseDoubleClick(object? sender, MouseButtonEventArgs e)
    {
        if (_isDisposed) return false;
        return InputController.HandleMouseDoubleClick(sender, e);
    }

    public bool HandleWindowDrag(object? sender, MouseButtonEventArgs e)
    {
        if (_isDisposed) return false;
        return InputController.HandleWindowDrag(sender, e);
    }

    public void HandleMouseEnter(object? sender, MouseEventArgs e)
    {
        if (_isDisposed) return;
        InputController.HandleMouseEnter(sender, e);
    }

    public void HandleMouseLeave(object? sender, MouseEventArgs e)
    {
        if (_isDisposed) return;
        InputController.HandleMouseLeave(sender, e);
    }

    public bool HandleButtonClick(object? sender, RoutedEventArgs e)
    {
        if (_isDisposed) return false;
        return InputController.HandleButtonClick(sender, e);
    }

    public void OnPropertyChanged(params string[] propertyNames)
    {
        if (_isDisposed) return;

        var batchManager = _controllerProvider.BatchOperationsManager;
        if (batchManager != null)
        {
            batchManager.ExecuteOrEnqueue(() =>
            {
                if (_isDisposed) return;

                foreach (var name in propertyNames)
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            });
        }
        else
        {
            if (_isDisposed) return;
            foreach (var name in propertyNames)
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public void DisposeResources() => Dispose();

    protected override void DisposeManaged()
    {
        if (_isDisposed) return;

        _logger.Log(LogLevel.Information, LogPrefix, "Starting synchronous dispose");

        var cleanupManager = _controllerProvider.ResourceCleanupManager;

        cleanupManager.SafeExecuteAction(() => _cleanupCts.Cancel(), "Cancelling cleanup token");
        cleanupManager.SafeExecuteAction(StopCaptureIfNeeded, "Stopping capture if needed");
        cleanupManager.SafeExecuteAction(CloseUIElementsIfNeeded, "Closing UI elements if needed");

        _transitionLock?.Dispose();
        _cleanupCts?.Dispose();

        _logger.Log(LogLevel.Information, LogPrefix, "Synchronous dispose completed");
    }

    private void StopCaptureIfNeeded()
    {
        if (!IsRecording) return;

        try
        {
            var stopTask = AudioController.StopCaptureAsync();
            if (!stopTask.Wait(5000))
                _logger.Log(LogLevel.Warning, LogPrefix, "StopCaptureAsync timed out, continuing with disposal");
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Error, LogPrefix, $"Error stopping capture during dispose: {ex.Message}");
        }
    }

    private void CloseUIElementsIfNeeded()
    {
        if (OverlayManager?.IsActive == true)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await OverlayManager.CloseAsync();
                }
                catch (Exception ex)
                {
                    _logger.Log(LogLevel.Warning, LogPrefix,
                        $"Error closing overlay during dispose: {ex.Message}");
                }
            });
        }

        if (IsControlPanelOpen)
        {
            try
            {
                UIController?.CloseControlPanel();
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Warning, LogPrefix,
                    $"Error closing control panel during dispose: {ex.Message}");
            }
        }
    }

    protected override async ValueTask DisposeAsyncManagedResources()
    {
        if (_isDisposed) return;

        _logger.Log(LogLevel.Information, LogPrefix, "Starting asynchronous dispose");

        using var timeoutCts = new CancellationTokenSource(FromSeconds(5));

        try
        {
            var cleanupManager = _controllerProvider.ResourceCleanupManager;

            _cleanupCts.Cancel();

            await cleanupManager.StopCaptureAsync(AudioController, IsRecording, timeoutCts.Token);
            await cleanupManager.CloseUIElementsAsync(UIController, OverlayManager, Dispatcher, timeoutCts.Token);

            _transitionLock?.Dispose();
            _cleanupCts?.Dispose();
        }
        catch (OperationCanceledException)
        {
            _logger.Log(LogLevel.Warning, LogPrefix, "Async disposal operation cancelled due to timeout");
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Error, LogPrefix, $"Error during asynchronous dispose: {ex.Message}");
        }
        finally
        {
            _logger.Log(LogLevel.Information, LogPrefix, "Asynchronous dispose completed");
        }
    }
}