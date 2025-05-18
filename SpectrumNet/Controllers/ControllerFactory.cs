#nullable enable

using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace SpectrumNet.Controllers;

public sealed class ControllerFactory : AsyncDisposableBase,  IControllerFactory, IMainController
{
    private const string LOG_PREFIX = "ControllerFactory";

    private readonly IUIController _uiController;
    private readonly IAudioController _audioController;
    private readonly IViewController _viewController;
    private readonly IInputController _inputController;
    private readonly IOverlayManager _overlayManager;
    private readonly IRendererFactory _rendererFactory;
    private readonly ITransparencyManager _transparencyManager;

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
        _transparencyManager = RendererTransparencyManager.Instance;

        // Прямая инициализация контроллеров с передачей this как IMainController
        _viewController = new ViewController(this, _renderElement, _rendererFactory);
        _audioController = new AudioController(this, SynchronizationContext.Current!);
        _uiController = new UIController(this, _transparencyManager);
        _inputController = new InputController(this);
        _overlayManager = new OverlayManager(this, _transparencyManager);

        ownerWindow.Closed += (_, _) => DisposeResources();
        ownerWindow.KeyDown += OnWindowKeyDown;

        StartBatchUpdate();
        Initialize();
    }

    private void StartBatchUpdate()
    {
        _batchUpdateTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = FromMilliseconds(BATCH_UPDATE_INTERVAL_MS)
        };
        _batchUpdateTimer.Tick += ProcessBatchedUIOperations;
        _batchUpdateTimer.Start();
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

    public void Initialize()
    {
        InitializeRenderingState();
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

        // Устанавливаем analyzer в ViewController
        _viewController.Analyzer = analyzer;

        var renderer = new Renderer(
            SpectrumStyles,
            this,
            analyzer,
            _renderElement,
            _rendererFactory
        );

        // Устанавливаем renderer в ViewController
        _viewController.Renderer = renderer;

        RequestRender();
    }

    // Реализация IControllerFactory
    public IUIController UIController => _uiController;
    public IAudioController AudioController => _audioController;
    public IViewController ViewController => _viewController;
    public IInputController InputController => _inputController;
    public IOverlayManager OverlayManager => _overlayManager;

    // Реализация IMainController
    public Dispatcher Dispatcher => Application.Current?.Dispatcher ??
        throw new InvalidOperationException("Application.Current is null");

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

    public GainParameters GainParameters => _audioController.GainParameters;

    public bool IsRecording
    {
        get => _isRecording;
        set
        {
            if (_isDisposed || _isRecording == value) return;
            _isRecording = value;

            _audioController.IsRecording = value;

            OnPropertyChanged(nameof(IsRecording), nameof(CanStartCapture));

            if (_viewController.Renderer != null && !_isDisposed)
                RequestRender();
        }
    }

    public bool CanStartCapture => !_isDisposed && !IsRecording && _audioController.CanStartCapture;

    public bool IsTransitioning
    {
        get => _isTransitioning;
        set
        {
            if (_isDisposed || _isTransitioning == value) return;
            _isTransitioning = value;
            _audioController.IsTransitioning = value;
        }
    }

    #region Audio Properties

    public FftWindowType WindowType
    {
        get => _audioController.WindowType;
        set
        {
            if (_isDisposed) return;
            _audioController.WindowType = value;
            OnPropertyChanged(nameof(WindowType));
        }
    }

    public float MinDbLevel
    {
        get => _audioController.MinDbLevel;
        set
        {
            if (_isDisposed) return;
            _audioController.MinDbLevel = value;
        }
    }

    public float MaxDbLevel
    {
        get => _audioController.MaxDbLevel;
        set
        {
            if (_isDisposed) return;
            _audioController.MaxDbLevel = value;
        }
    }

    public float AmplificationFactor
    {
        get => _audioController.AmplificationFactor;
        set
        {
            if (_isDisposed) return;
            _audioController.AmplificationFactor = value;
        }
    }

    #endregion

    #region Audio Methods

    public async Task StartCaptureAsync()
    {
        if (_isDisposed) return;
        await _audioController.StartCaptureAsync();
    }

    public async Task StopCaptureAsync()
    {
        if (_isDisposed) return;
        await _audioController.StopCaptureAsync();
    }

    public async Task ToggleCaptureAsync()
    {
        if (_isDisposed) return;
        await _audioController.ToggleCaptureAsync();
    }

    public SpectrumAnalyzer? GetCurrentAnalyzer() =>
        _isDisposed ? null : _audioController.GetCurrentAnalyzer();

    #endregion

    #region View Properties

    public SKElement SpectrumCanvas => _renderElement;

    public SpectrumBrushes SpectrumStyles => SpectrumBrushes.Instance;

    public int BarCount
    {
        get => _viewController.BarCount;
        set
        {
            if (_isDisposed) return;
            _viewController.BarCount = value;
        }
    }

    public double BarSpacing
    {
        get => _viewController.BarSpacing;
        set
        {
            if (_isDisposed) return;
            _viewController.BarSpacing = value;
        }
    }

    public RenderQuality RenderQuality
    {
        get => _viewController.RenderQuality;
        set
        {
            if (_isDisposed) return;
            _viewController.RenderQuality = value;
        }
    }

    public RenderStyle SelectedDrawingType
    {
        get => _viewController.SelectedDrawingType;
        set
        {
            if (_isDisposed) return;
            _viewController.SelectedDrawingType = value;
        }
    }

    public SpectrumScale ScaleType
    {
        get => _viewController.ScaleType;
        set
        {
            if (_isDisposed) return;
            _viewController.ScaleType = value;
        }
    }

    public string SelectedStyle
    {
        get => _viewController.SelectedStyle;
        set
        {
            if (_isDisposed) return;
            _viewController.SelectedStyle = value;
        }
    }

    public Palette? SelectedPalette
    {
        get => _viewController.SelectedPalette;
        set
        {
            if (_isDisposed) return;
            _viewController.SelectedPalette = value;
        }
    }

    public bool ShowPerformanceInfo
    {
        get => _viewController.ShowPerformanceInfo;
        set
        {
            if (_isDisposed) return;
            _viewController.ShowPerformanceInfo = value;
        }
    }

    public IReadOnlyDictionary<string, Palette> AvailablePalettes => _viewController.AvailablePalettes;
    public IEnumerable<RenderStyle> AvailableDrawingTypes => _viewController.AvailableDrawingTypes;
    public IEnumerable<FftWindowType> AvailableFftWindowTypes => _viewController.AvailableFftWindowTypes;
    public IEnumerable<SpectrumScale> AvailableScaleTypes => _viewController.AvailableScaleTypes;
    public IEnumerable<RenderQuality> AvailableRenderQualities => _viewController.AvailableRenderQualities;
    public IEnumerable<RenderStyle> OrderedDrawingTypes => _viewController.OrderedDrawingTypes;

    public Renderer? Renderer
    {
        get => _viewController.Renderer;
        set => _viewController.Renderer = value;
    }

    public SpectrumAnalyzer Analyzer
    {
        get => _viewController.Analyzer;
        set => _viewController.Analyzer = value;
    }

    #endregion

    #region View Methods

    public void RequestRender()
    {
        if (_isDisposed) return;
        _viewController.RequestRender();
    }

    public void UpdateRenderDimensions(int width, int height)
    {
        if (_isDisposed) return;
        _viewController.UpdateRenderDimensions(width, height);
    }

    public void SynchronizeVisualization()
    {
        if (_isDisposed) return;
        _viewController.SynchronizeVisualization();
    }

    public void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs? e)
    {
        if (_isDisposed || e == null) return;
        _viewController.OnPaintSurface(sender, e);
    }

    #endregion

    #region UI Controller Methods

    public bool IsOverlayActive
    {
        get => _overlayManager.IsActive;
        set
        {
            if (_isDisposed) return;

            if (value != IsOverlayActive)
            {
                if (value)
                    _ = _overlayManager.OpenAsync();
                else
                    _ = _overlayManager.CloseAsync();
            }
        }
    }

    public bool IsOverlayTopmost
    {
        get => _overlayManager.IsTopmost;
        set
        {
            if (_isDisposed) return;
            _overlayManager.IsTopmost = value;
        }
    }

    public bool IsPopupOpen
    {
        get => _uiController.IsPopupOpen;
        set
        {
            if (_isDisposed) return;
            _uiController.IsPopupOpen = value;
        }
    }

    public bool IsControlPanelOpen => !_isDisposed && _uiController.IsControlPanelOpen;

    public void ToggleTheme()
    {
        if (_isDisposed) return;
        _uiController.ToggleTheme();
    }

    public void OpenControlPanel()
    {
        if (_isDisposed) return;
        _uiController.OpenControlPanel();
    }

    public void CloseControlPanel()
    {
        if (_isDisposed) return;
        _uiController.CloseControlPanel();
    }

    public void ToggleControlPanel()
    {
        if (_isDisposed) return;
        _uiController.ToggleControlPanel();
    }

    public void OpenOverlay()
    {
        if (_isDisposed) return;
        _ = _overlayManager.OpenAsync();
    }

    public void CloseOverlay()
    {
        if (_isDisposed) return;
        _ = _overlayManager.CloseAsync();
    }

    #endregion

    #region Input Controller Methods

    public void RegisterWindow(Window window)
    {
        if (_isDisposed) return;
        _inputController.RegisterWindow(window);
    }

    public void UnregisterWindow(Window window)
    {
        if (_isDisposed) return;
        _inputController.UnregisterWindow(window);
    }

    public bool HandleKeyDown(KeyEventArgs e, IInputElement? focusedElement)
    {
        if (_isDisposed) return false;
        return _inputController.HandleKeyDown(e, focusedElement);
    }

    public bool HandleMouseDown(object? sender, MouseButtonEventArgs e)
    {
        if (_isDisposed) return false;
        return _inputController.HandleMouseDown(sender, e);
    }

    public bool HandleMouseMove(object? sender, MouseEventArgs e)
    {
        if (_isDisposed) return false;
        return _inputController.HandleMouseMove(sender, e);
    }

    public bool HandleMouseUp(object? sender, MouseButtonEventArgs e)
    {
        if (_isDisposed) return false;
        return _inputController.HandleMouseUp(sender, e);
    }

    public bool HandleMouseDoubleClick(object? sender, MouseButtonEventArgs e)
    {
        if (_isDisposed) return false;
        return _inputController.HandleMouseDoubleClick(sender, e);
    }

    public bool HandleWindowDrag(object? sender, MouseButtonEventArgs e)
    {
        if (_isDisposed) return false;
        return _inputController.HandleWindowDrag(sender, e);
    }

    public void HandleMouseEnter(object? sender, MouseEventArgs e)
    {
        if (_isDisposed) return;
        _inputController.HandleMouseEnter(sender, e);
    }

    public void HandleMouseLeave(object? sender, MouseEventArgs e)
    {
        if (_isDisposed) return;
        _inputController.HandleMouseLeave(sender, e);
    }

    public bool HandleButtonClick(object? sender, RoutedEventArgs e)
    {
        if (_isDisposed) return false;
        return _inputController.HandleButtonClick(sender, e);
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

    public void DisposeResources()
    {
        Dispose();
    }

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
        if (!IsRecording)
            return;

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var stopTask = _audioController.StopCaptureAsync();

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
        if (_overlayManager.IsActive)
            _ = _overlayManager.CloseAsync();

        if (IsControlPanelOpen)
            _uiController.CloseControlPanel();
    }

    private void DisposeAllControllers()
    {
        if (_overlayManager is IDisposable overlayManager)
            DisposeController(overlayManager, "OverlayManager");

        if (_viewController is IDisposable viewController)
            DisposeController(viewController, "ViewController");

        if (_audioController is IDisposable audioController)
            DisposeController(audioController, "AudioController");

        if (_uiController is IDisposable uiController)
            DisposeController(uiController, "UIController");

        if (_inputController is IDisposable inputController)
            DisposeController(inputController, "InputController");
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
        if (IsRecording)
        {
            try
            {
                await _audioController.StopCaptureAsync().WaitAsync(TimeSpan.FromSeconds(3), token);
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
        await Dispatcher.InvokeAsync(() =>
        {
            try
            {
                if (_overlayManager.IsActive)
                    _ = _overlayManager.CloseAsync();

                if (IsControlPanelOpen)
                    _uiController.CloseControlPanel();
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, LOG_PREFIX, $"Error closing UI elements: {ex.Message}");
            }
        }).Task.WaitAsync(token);
    }

    private async Task DisposeControllersAsync(CancellationToken token)
    {
        if (_overlayManager is IAsyncDisposable asyncOverlayManager)
            await DisposeControllerAsync(asyncOverlayManager, "OverlayManager", token);
        else if (_overlayManager is IDisposable disposableOverlayManager)
            DisposeController(disposableOverlayManager, "OverlayManager");

        if (_viewController is IAsyncDisposable asyncViewController)
            await DisposeControllerAsync(asyncViewController, "ViewController", token);
        else if (_viewController is IDisposable disposableViewController)
            DisposeController(disposableViewController, "ViewController");

        if (_audioController is IAsyncDisposable asyncAudioController)
            await DisposeControllerAsync(asyncAudioController, "AudioController", token);
        else if (_audioController is IDisposable disposableAudioController)
            DisposeController(disposableAudioController, "AudioController");

        if (_uiController is IAsyncDisposable asyncUiController)
            await DisposeControllerAsync(asyncUiController, "UIController", token);
        else if (_uiController is IDisposable disposableUiController)
            DisposeController(disposableUiController, "UIController");

        if (_inputController is IAsyncDisposable asyncInputController)
            await DisposeControllerAsync(asyncInputController, "InputController", token);
        else if (_inputController is IDisposable disposableInputController)
            DisposeController(disposableInputController, "InputController");
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