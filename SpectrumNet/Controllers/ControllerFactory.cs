#nullable enable

namespace SpectrumNet.Controllers;

public sealed class ControllerFactory : AsyncDisposableBase, IMainController
{
    private const string LOG_PREFIX = "ControllerFactory";

    private readonly Lazy<UIController> _uiController;
    private readonly Lazy<AudioController> _audioController;
    private readonly Lazy<ViewController> _viewController;
    private readonly Lazy<InputController> _inputController;
    private readonly IRendererFactory _rendererFactory;

    private readonly CancellationTokenSource _cleanupCts = new();
    private readonly SemaphoreSlim _transitionLock = new(1, 1);
    private readonly object _disposeLock = new();

    private readonly Window _ownerWindow;
    private readonly SKElement _renderElement;
    private bool _isTransitioning;
    private bool _isRecording;

    private readonly ConcurrentQueue<Action> _pendingUIOperations = new();
    private DispatcherTimer? _batchUpdateTimer;
    private const int BATCH_UPDATE_INTERVAL_MS = 8;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ControllerFactory(Window ownerWindow, SKElement renderElement)
    {
        _ownerWindow = ownerWindow ?? throw new ArgumentNullException(nameof(ownerWindow));
        _renderElement = renderElement ?? throw new ArgumentNullException(nameof(renderElement));
        _isRecording = false;

        if (SynchronizationContext.Current == null)
        {
            throw new InvalidOperationException
                ("No synchronization context. Controller must be created in UI thread.");
        }

        _rendererFactory = RendererFactory.Instance;

        _uiController = new Lazy<UIController>(() => CreateUIController());
        _audioController = new Lazy<AudioController>(() => CreateAudioController());
        _viewController = new Lazy<ViewController>(() => CreateViewController());
        _inputController = new Lazy<InputController>(() => new InputController(this));

        ownerWindow.Closed += (_, _) => DisposeResources();
        ownerWindow.KeyDown += OnWindowKeyDown;

        BatchUpdate();
        InitializeRenderingState();
    }

    private void BatchUpdate()
    {
        _batchUpdateTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = FromMilliseconds(BATCH_UPDATE_INTERVAL_MS)
        };
        _batchUpdateTimer.Tick += ProcessBatchedUIOperations;
        _batchUpdateTimer.Start();
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (_isDisposed) return;

        if (e.Key == Key.Space && !e.IsRepeat)
        {
            _ = ToggleCaptureAsync();
            e.Handled = true;
        }
        else
        {
            HandleKeyDown(e, Keyboard.FocusedElement);
        }
    }

    private void InitializeRenderingState()
    {
        if (_isDisposed) return;

        var syncContext = SynchronizationContext.Current!;

        var analyzer = new SpectrumAnalyzer(
            new FftProcessor { WindowType = WindowType },
            new SpectrumConverter(GainParameters),
            syncContext
        );

        Analyzer = analyzer;

        var renderer = new Renderer(
            SpectrumStyles,
            this,
            analyzer,
            _renderElement,
            _rendererFactory
        );

        Renderer = renderer;

        RequestRender();
    }

    private UIController CreateUIController() => new(this, RendererTransparencyManager.Instance);
    private AudioController CreateAudioController() => new(this, SynchronizationContext.Current!);
    private ViewController CreateViewController() => new(this, _renderElement, _rendererFactory);

    public IUIController UIController => _uiController.Value;
    public IAudioController AudioController => _audioController.Value;
    public IViewController ViewController => _viewController.Value;
    public IInputController InputController => _inputController.Value;

    public Dispatcher Dispatcher => Application.Current?.Dispatcher ??
        throw new InvalidOperationException("Application.Current is null");

    public SpectrumAnalyzer Analyzer
    {
        get => ViewController.Analyzer;
        set => ViewController.Analyzer = value;
    }

    public Renderer? Renderer
    {
        get => ViewController.Renderer;
        set => ViewController.Renderer = value;
    }

    public bool IsOverlayActive
    {
        get => _uiController.IsValueCreated && UIController.IsOverlayActive;
        set
        {
            if (_isDisposed) return;

            if (_uiController.IsValueCreated)
                UIController.IsOverlayActive = value;
        }
    }

    public bool IsOverlayTopmost
    {
        get => !_uiController.IsValueCreated || UIController.IsOverlayTopmost;
        set
        {
            if (_isDisposed) return;

            if (_uiController.IsValueCreated)
                UIController.IsOverlayTopmost = value;
        }
    }

    public bool IsPopupOpen
    {
        get => _uiController.IsValueCreated && UIController.IsPopupOpen;
        set
        {
            if (_isDisposed) return;

            if (_uiController.IsValueCreated)
                UIController.IsPopupOpen = value;
        }
    }

    public bool IsControlPanelOpen => 
        !_isDisposed
        && _uiController.IsValueCreated
        && UIController.IsControlPanelOpen;

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
        UIController.OpenOverlay();
    }

    public void CloseOverlay()
    {
        if (_isDisposed) return;
        UIController.CloseOverlay();
    }

    public bool LimitFpsTo60
    {
        get => Settings.Instance.LimitFpsTo60;
        set
        {
            if (_isDisposed || Settings.Instance.LimitFpsTo60 == value) return;

            Log(LogLevel.Information, LOG_PREFIX,
                $"LimitFpsTo60 changing from {Settings.Instance.LimitFpsTo60} to {value}",
                forceLog: true);

            var originalStyle = Settings.Instance.SelectedRenderStyle;

            Safe(() => Settings.Instance.LimitFpsTo60 = value,
                new ErrorHandlingOptions
                {
                    Source = LOG_PREFIX,
                    ErrorMessage = "Error updating LimitFpsTo60 setting"
                });

            OnPropertyChanged(nameof(LimitFpsTo60));

            if (originalStyle != Settings.Instance.SelectedRenderStyle)
            {
                Log(LogLevel.Warning,
                    LOG_PREFIX,
                    $"Render style changed after updating LimitFpsTo60: {originalStyle} -> {Settings.Instance.SelectedRenderStyle}",
                    forceLog: true);

                Safe(() => Settings.Instance.SelectedRenderStyle = originalStyle,
                    new ErrorHandlingOptions
                    {
                        Source = LOG_PREFIX,
                        ErrorMessage = "Error restoring original render style"
                    });
            }
        }
    }

    public GainParameters GainParameters =>
        _audioController.IsValueCreated ?
            AudioController.GainParameters :
            new GainParameters(SynchronizationContext.Current);

    public bool IsRecording
    {
        get => _isRecording;
        set
        {
            if (_isDisposed || _isRecording == value) return;
            _isRecording = value;

            if (_audioController.IsValueCreated && !_isDisposed)
                AudioController.IsRecording = value;

            OnPropertyChanged(nameof(IsRecording), nameof(CanStartCapture));

            if (Renderer != null && !_isDisposed)
                RequestRender();
        }
    }

    public bool CanStartCapture => !_isDisposed && !IsRecording
        && (!_audioController.IsValueCreated || AudioController.CanStartCapture);

    public bool IsTransitioning
    {
        get => _isTransitioning;
        set
        {
            if (_isDisposed || _isTransitioning == value) return;
            _isTransitioning = value;

            if (_audioController.IsValueCreated && !_isDisposed)
                AudioController.IsTransitioning = value;
        }
    }

    #region Audio Properties

    public FftWindowType WindowType
    {
        get => _audioController.IsValueCreated ?
            AudioController.WindowType :
            Settings.Instance.SelectedFftWindowType;
        set
        {
            if (_isDisposed) return;

            if (_audioController.IsValueCreated)
                AudioController.WindowType = value;

            OnPropertyChanged(nameof(WindowType));
        }
    }

    public float MinDbLevel
    {
        get => _audioController.IsValueCreated ?
            AudioController.MinDbLevel :
            Settings.Instance.UIMinDbLevel;
        set
        {
            if (_isDisposed) return;

            if (_audioController.IsValueCreated)
                AudioController.MinDbLevel = value;
        }
    }

    public float MaxDbLevel
    {
        get => _audioController.IsValueCreated ?
            AudioController.MaxDbLevel :
            Settings.Instance.UIMaxDbLevel;
        set
        {
            if (_isDisposed) return;

            if (_audioController.IsValueCreated)
                AudioController.MaxDbLevel = value;
        }
    }

    public float AmplificationFactor
    {
        get => _audioController.IsValueCreated ?
            AudioController.AmplificationFactor :
            Settings.Instance.UIAmplificationFactor;
        set
        {
            if (_isDisposed) return;

            if (_audioController.IsValueCreated)
                AudioController.AmplificationFactor = value;
        }
    }

    #endregion

    #region Audio Methods

    public async Task StartCaptureAsync()
    {
        if (_isDisposed) return;
        await AudioController.StartCaptureAsync();
    }

    public async Task StopCaptureAsync()
    {
        if (_isDisposed) return;

        if (_audioController.IsValueCreated)
            await AudioController.StopCaptureAsync();
    }

    public async Task ToggleCaptureAsync()
    {
        if (_isDisposed) return;

        if (!_audioController.IsValueCreated)
        {
            IsRecording = true;
            await AudioController.StartCaptureAsync();
        }
        else
        {
            await AudioController.ToggleCaptureAsync();
        }
    }

    public SpectrumAnalyzer? GetCurrentAnalyzer() =>
        _isDisposed ? null :
        _audioController.IsValueCreated ?
            AudioController.GetCurrentAnalyzer() :
            null;

    #endregion

    #region View Properties

    public SKElement SpectrumCanvas => _renderElement;

    public SpectrumBrushes SpectrumStyles => SpectrumBrushes.Instance;

    public int BarCount
    {
        get => _viewController.IsValueCreated ?
            ViewController.BarCount :
            Settings.Instance.UIBarCount;
        set
        {
            if (_isDisposed) return;

            if (_viewController.IsValueCreated)
                ViewController.BarCount = value;
        }
    }

    public double BarSpacing
    {
        get => _viewController.IsValueCreated ?
            ViewController.BarSpacing :
            Settings.Instance.UIBarSpacing;
        set
        {
            if (_isDisposed) return;

            if (_viewController.IsValueCreated)
                ViewController.BarSpacing = value;
        }
    }

    public RenderQuality RenderQuality
    {
        get => _viewController.IsValueCreated ?
            ViewController.RenderQuality :
            Settings.Instance.SelectedRenderQuality;
        set
        {
            if (_isDisposed) return;

            if (_viewController.IsValueCreated)
                ViewController.RenderQuality = value;
        }
    }

    public RenderStyle SelectedDrawingType
    {
        get => _viewController.IsValueCreated ?
            ViewController.SelectedDrawingType :
            Settings.Instance.SelectedRenderStyle;
        set
        {
            if (_isDisposed) return;

            if (_viewController.IsValueCreated)
                ViewController.SelectedDrawingType = value;
        }
    }

    public SpectrumScale ScaleType
    {
        get => _viewController.IsValueCreated ?
            ViewController.ScaleType :
            Settings.Instance.SelectedScaleType;
        set
        {
            if (_isDisposed) return;

            if (_viewController.IsValueCreated)
                ViewController.ScaleType = value;
        }
    }

    public string SelectedStyle
    {
        get => _viewController.IsValueCreated ?
            ViewController.SelectedStyle :
            Settings.Instance.SelectedPalette;
        set
        {
            if (_isDisposed) return;

            if (_viewController.IsValueCreated)
                ViewController.SelectedStyle = value;
        }
    }

    public Palette? SelectedPalette
    {
        get => _viewController.IsValueCreated ? ViewController.SelectedPalette : null;
        set
        {
            if (_isDisposed) return;

            if (_viewController.IsValueCreated)
                ViewController.SelectedPalette = value;
        }
    }

    public bool ShowPerformanceInfo
    {
        get => _viewController.IsValueCreated ?
            ViewController.ShowPerformanceInfo :
            Settings.Instance.ShowPerformanceInfo;
        set
        {
            if (_isDisposed) return;

            if (_viewController.IsValueCreated)
                ViewController.ShowPerformanceInfo = value;
        }
    }

    public IReadOnlyDictionary<string, Palette> AvailablePalettes =>
        _viewController.IsValueCreated ?
            ViewController.AvailablePalettes :
            SpectrumBrushes.Instance.RegisteredPalettes;

    public IEnumerable<RenderStyle> AvailableDrawingTypes =>
        _viewController.IsValueCreated ?
            ViewController.AvailableDrawingTypes :
            Enum.GetValues<RenderStyle>();

    public IEnumerable<FftWindowType> AvailableFftWindowTypes =>
        _viewController.IsValueCreated ?
            ViewController.AvailableFftWindowTypes :
            Enum.GetValues<FftWindowType>();

    public IEnumerable<SpectrumScale> AvailableScaleTypes =>
        _viewController.IsValueCreated ?
            ViewController.AvailableScaleTypes :
            Enum.GetValues<SpectrumScale>();

    public IEnumerable<RenderQuality> AvailableRenderQualities =>
        _viewController.IsValueCreated ?
            ViewController.AvailableRenderQualities :
            Enum.GetValues<RenderQuality>();

    public IEnumerable<RenderStyle> OrderedDrawingTypes =>
        _viewController.IsValueCreated ?
            ViewController.OrderedDrawingTypes :
            Enum.GetValues<RenderStyle>();

    #endregion

    #region View Methods

    public void RequestRender()
    {
        if (_isDisposed) return;
        Renderer?.RequestRender();
    }

    public void UpdateRenderDimensions(int width, int height)
    {
        if (_isDisposed) return;
        Renderer?.UpdateRenderDimensions(width, height);
    }

    public void SynchronizeVisualization()
    {
        if (_isDisposed) return;
        Renderer?.SynchronizeWithController();
    }

    public void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs? e)
    {
        if (_isDisposed || e == null) return;

        if (_viewController.IsValueCreated)
            ViewController.OnPaintSurface(sender, e);
        else
            Renderer?.RenderFrame(sender, e);
    }

    #endregion

    #region IInputController Implementation

    public void RegisterWindow(Window window)
    {
        if (_isDisposed) return;
        InputController.RegisterWindow(window);
    }

    public void UnregisterWindow(Window window)
    {
        if (_isDisposed) return;

        if (_inputController.IsValueCreated)
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

    public bool HandleMouseMove(object? sender, System.Windows.Input.MouseEventArgs e)
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

    public void HandleMouseEnter(object? sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isDisposed) return;
        InputController.HandleMouseEnter(sender, e);
    }

    public void HandleMouseLeave(object? sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isDisposed) return;
        InputController.HandleMouseLeave(sender, e);
    }

    public bool HandleButtonClick(object? sender, RoutedEventArgs e)
    {
        if (_isDisposed) return false;
        return InputController.HandleButtonClick(sender, e);
    }

    #endregion

    public void OnPropertyChanged(params string[] propertyNames)
    {
        if (_isDisposed) return;

        _pendingUIOperations.Enqueue(() =>
        {
            if (_isDisposed) return;

            foreach (var name in propertyNames)
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        });
    }

    private void ProcessBatchedUIOperations(object? sender, EventArgs e)
    {
        if (_isDisposed) return;

        int processedCount = 0;
        const int MAX_OPERATIONS_PER_FRAME = 10;

        while (processedCount < MAX_OPERATIONS_PER_FRAME &&
               _pendingUIOperations.TryDequeue(out var operation))
        {
            try
            {
                operation();
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error,
                    LOG_PREFIX,
                    $"Error in UI operation: {ex.Message}");
            }
            processedCount++;
        }
    }

    public void DisposeResources() => Dispose();

    protected override void DisposeManaged()
    {
        if (_isDisposed) return;

        Log(LogLevel.Information,
            LOG_PREFIX,
            "Starting synchronous dispose");

        SafeExecuteDisposeAction(CleanupInitiation, "CleanupInitiation");
        SafeExecuteDisposeAction(StopCaptureIfNeeded, "StopCaptureIfNeeded");
        SafeExecuteDisposeAction(CloseUIElementsIfNeeded, "CloseUIElementsIfNeeded");
        SafeExecuteDisposeAction(DisposeAllControllers, "DisposeAllControllers");
        SafeExecuteDisposeAction(DisposeResourceObjects, "DisposeResourceObjects");

        Log(LogLevel.Information,
            LOG_PREFIX,
            "Synchronous dispose completed");
    }

    private static void SafeExecuteDisposeAction(Action action, string actionName)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error,
                LOG_PREFIX,
                $"Error during {actionName}: {ex.Message}");
        }
    }

    private void CleanupInitiation()
    {
        if (_ownerWindow != null)
            _ownerWindow.KeyDown -= OnWindowKeyDown;

        _cleanupCts.Cancel();

        if (_batchUpdateTimer != null)
        {
            _batchUpdateTimer.Stop();
            _batchUpdateTimer = null;
        }

        while (_pendingUIOperations.TryDequeue(out var operation))
        {
            try
            {
                operation();
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error,
                    LOG_PREFIX,
                    $"Error handling operation during dispose: {ex.Message}");
            }
        }
    }

    private void StopCaptureIfNeeded()
    {
        if (!IsRecording || !_audioController.IsValueCreated)
            return;

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var stopTask = _audioController.Value.StopCaptureAsync();

            if (!stopTask.Wait(5000))
            {
                Log(LogLevel.Warning,
                    LOG_PREFIX,
                    "StopCaptureAsync timed out, continuing with disposal");
            }
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error,
                LOG_PREFIX,
                $"Error stopping capture during dispose: {ex.Message}");
        }
    }

    private void CloseUIElementsIfNeeded()
    {
        if (_uiController.IsValueCreated)
        {
            if (IsOverlayActive)
                UIController.CloseOverlay();

            if (IsControlPanelOpen)
                UIController.CloseControlPanel();
        }
    }

    private void DisposeAllControllers()
    {
        var (viewController, audioController, uiController, inputController) = CaptureExistingControllers();

        if (viewController != null)
            DisposeController(viewController, "ViewController");

        if (audioController != null)
            DisposeController(audioController, "AudioController");

        if (uiController != null)
            DisposeController(uiController, "UIController");

        if (inputController != null)
            DisposeController(inputController, "InputController");
    }

    private (ViewController? viewController,
             AudioController? audioController,
             UIController? uiController,
             InputController? inputController) CaptureExistingControllers()
    {
        lock (_disposeLock)
        {
            ViewController? viewController =
                _viewController.IsValueCreated ? _viewController.Value : null;

            AudioController? audioController =
                _audioController.IsValueCreated ? _audioController.Value : null;

            UIController? uiController =
                _uiController.IsValueCreated ? _uiController.Value : null;

            InputController? inputController =
                _inputController.IsValueCreated ? _inputController.Value : null;

            return (viewController, audioController, uiController, inputController);
        }
    }

    private static void DisposeController<T>(
        T controller,
        string controllerName) where T : class
    {
        try
        {
            if (controller is IDisposable disposable)
                disposable.Dispose();
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error,
                LOG_PREFIX,
                $"Error disposing {controllerName}: {ex.Message}");
        }
    }

    private void DisposeResourceObjects()
    {
        Safe(() =>
        {
            _transitionLock?.Dispose();
            _cleanupCts?.Dispose();
        },
        new ErrorHandlingOptions
        {
            Source = LOG_PREFIX,
            ErrorMessage = "Error disposing resource objects"
        });
    }

    protected override async ValueTask DisposeAsyncManagedResources()
    {
        if (_isDisposed) return;

        Log(LogLevel.Information, LOG_PREFIX, "Starting asynchronous dispose");

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            await PerformAsyncCleanup(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            Log(LogLevel.Warning, LOG_PREFIX, "Async disposal operation cancelled due to timeout");
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, LOG_PREFIX, $"Error during asynchronous dispose: {ex.Message}");
        }
        finally
        {
            Log(LogLevel.Information, LOG_PREFIX, "Asynchronous dispose completed");
        }
    }

    private async Task PerformAsyncCleanup(CancellationToken token)
    {
        await DetachEventHandlersAsync(token);
        _cleanupCts.Cancel();

        if (_batchUpdateTimer != null)
        {
            await Dispatcher.InvokeAsync(() => _batchUpdateTimer.Stop()).Task;
            _batchUpdateTimer = null;
        }

        await StopCaptureAsync(token);
        await CloseUIElementsAsync(token);
        await DisposeControllersAsync(token);

        _transitionLock?.Dispose();
        _cleanupCts?.Dispose();
    }

    private async Task DetachEventHandlersAsync(CancellationToken token)
    {
        await Dispatcher.InvokeAsync(() =>
        {
            if (_ownerWindow != null)
                _ownerWindow.KeyDown -= OnWindowKeyDown;
        }).Task.WaitAsync(token);
    }

    private async Task StopCaptureAsync(CancellationToken token)
    {
        if (IsRecording && _audioController.IsValueCreated)
        {
            try
            {
                await _audioController.Value.StopCaptureAsync().WaitAsync(TimeSpan.FromSeconds(3), token);
            }
            catch (OperationCanceledException)
            {
                Log(LogLevel.Warning, LOG_PREFIX, "Stop capture operation cancelled or timed out");
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, LOG_PREFIX, $"Error stopping capture: {ex.Message}");
            }
        }
    }

    private async Task CloseUIElementsAsync(CancellationToken token)
    {
        if (!_uiController.IsValueCreated) return;

        await Dispatcher.InvokeAsync(() =>
        {
            try
            {
                if (IsOverlayActive)
                    UIController.CloseOverlay();

                if (IsControlPanelOpen)
                    UIController.CloseControlPanel();
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, LOG_PREFIX, $"Error closing UI elements: {ex.Message}");
            }
        }).Task.WaitAsync(token);
    }

    private async Task DisposeControllersAsync(CancellationToken token)
    {
        var (viewController, audioController, uiController, inputController) = CaptureExistingControllers();

        if (viewController != null)
            await DisposeControllerAsync(viewController, "ViewController", token);

        if (audioController != null)
            await DisposeControllerAsync(audioController, "AudioController", token);

        if (uiController != null)
            await DisposeControllerAsync(uiController, "UIController", token);

        if (inputController != null)
            await DisposeControllerAsync(inputController, "InputController", token);
    }

    private static async Task DisposeControllerAsync<T>(
        T controller,
        string controllerName,
        CancellationToken token) where T : class
    {
        try
        {
            if (controller is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3), token);
            }
            else if (controller is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
            Log(LogLevel.Warning, LOG_PREFIX, $"Dispose operation for {controllerName} timed out");
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, LOG_PREFIX, $"Error disposing {controllerName}: {ex.Message}");
        }
    }
}