#nullable enable

namespace SpectrumNet.Controllers;

public sealed class ControllerFactory : AsyncDisposableBase, IMainController
{
    private const string LOG_PREFIX = "ControllerFactory";

    private readonly Lazy<UIController> _uiController;
    private readonly InputController _inputController;
    private readonly Lazy<AudioController> _audioController;
    private readonly Lazy<ViewController> _viewController;
    private readonly IRendererFactory _rendererFactory;

    private readonly CancellationTokenSource _cleanupCts = new();
    private readonly SemaphoreSlim _transitionLock = new(1, 1);
    private readonly object _disposeLock = new();

    private readonly Window _ownerWindow;
    private readonly SKElement _renderElement;
    private bool _isTransitioning;
    private bool _isRecording;

    private readonly ConcurrentQueue<Action> _pendingUIOperations = new();
    private DispatcherTimer _batchUpdateTimer;
    private const int BATCH_UPDATE_INTERVAL_MS = 8;

    public event PropertyChangedEventHandler? PropertyChanged;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    public ControllerFactory(Window ownerWindow, SKElement renderElement)
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    {
        _ownerWindow = ownerWindow ?? throw new ArgumentNullException(nameof(ownerWindow));
        _renderElement = renderElement ?? throw new ArgumentNullException(nameof(renderElement));
        _isRecording = false;

        if (SynchronizationContext.Current == null)
        {
            throw new InvalidOperationException("No synchronization context. Controller must be created in UI thread.");
        }

        _rendererFactory = RendererFactory.Instance;

        _uiController = new Lazy<UIController>(CreateUIController);
        _audioController = new Lazy<AudioController>(CreateAudioController);
        _viewController = new Lazy<ViewController>(CreateViewController);
        _inputController = new InputController(this);

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
    public IInputController InputController => _inputController;

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
            if (_uiController.IsValueCreated)
                UIController.IsOverlayActive = value;
        }
    }

    public bool IsOverlayTopmost
    {
        get => !_uiController.IsValueCreated || UIController.IsOverlayTopmost;
        set
        {
            if (_uiController.IsValueCreated)
                UIController.IsOverlayTopmost = value;
        }
    }

    public bool IsPopupOpen
    {
        get => _uiController.IsValueCreated && UIController.IsPopupOpen;
        set
        {
            if (_uiController.IsValueCreated)
                UIController.IsPopupOpen = value;
        }
    }

    public bool IsControlPanelOpen => _uiController.IsValueCreated && UIController.IsControlPanelOpen;

    public void ToggleTheme() => UIController.ToggleTheme();
    public void OpenControlPanel() => UIController.OpenControlPanel();
    public void CloseControlPanel() => UIController.CloseControlPanel();
    public void ToggleControlPanel() => UIController.ToggleControlPanel();
    public void OpenOverlay() => UIController.OpenOverlay();
    public void CloseOverlay() => UIController.CloseOverlay();

    public bool LimitFpsTo60
    {
        get => Settings.Instance.LimitFpsTo60;
        set
        {
            if (Settings.Instance.LimitFpsTo60 == value) return;

            Log(LogLevel.Information, LOG_PREFIX,
                $"LimitFpsTo60 changing from {Settings.Instance.LimitFpsTo60} to {value}",
                forceLog: true);

            var originalStyle = Settings.Instance.SelectedRenderStyle;
            Settings.Instance.LimitFpsTo60 = value;
            OnPropertyChanged(nameof(LimitFpsTo60));

            if (originalStyle != Settings.Instance.SelectedRenderStyle)
            {
                Log(LogLevel.Warning, LOG_PREFIX,
                    $"Render style changed after updating LimitFpsTo60: {originalStyle} -> {Settings.Instance.SelectedRenderStyle}",
                    forceLog: true);

                Settings.Instance.SelectedRenderStyle = originalStyle;
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
            if (_isRecording == value) return;
            _isRecording = value;

            if (_audioController.IsValueCreated)
                AudioController.IsRecording = value;

            OnPropertyChanged(nameof(IsRecording), nameof(CanStartCapture));

            if (Renderer != null)
                RequestRender();
        }
    }

    public bool CanStartCapture => !IsRecording
        && (!_audioController.IsValueCreated || AudioController.CanStartCapture);

    public bool IsTransitioning
    {
        get => _isTransitioning;
        set
        {
            if (_isTransitioning == value) return;
            _isTransitioning = value;

            if (_audioController.IsValueCreated)
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
            if (_audioController.IsValueCreated)
                AudioController.AmplificationFactor = value;
        }
    }

    #endregion

    #region Audio Methods

    public async Task StartCaptureAsync() =>
        await AudioController.StartCaptureAsync();

    public async Task StopCaptureAsync() =>
        await (_audioController.IsValueCreated ?
            AudioController.StopCaptureAsync() :
            Task.CompletedTask);

    public async Task ToggleCaptureAsync()
    {
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
            if (_viewController.IsValueCreated)
                ViewController.SelectedStyle = value;
        }
    }

    public Palette? SelectedPalette
    {
        get => _viewController.IsValueCreated ? ViewController.SelectedPalette : null;
        set
        {
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

    public void RequestRender() =>
        Renderer?.RequestRender();

    public void UpdateRenderDimensions(int width, int height) =>
        Renderer?.UpdateRenderDimensions(width, height);

    public void SynchronizeVisualization() =>
        Renderer?.SynchronizeWithController();

    public void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs? e)
    {
        if (_viewController.IsValueCreated)
            ViewController.OnPaintSurface(sender, e);
        else if (Renderer != null && e != null)
            Renderer.RenderFrame(sender, e);
    }

    #endregion

    #region IInputController Implementation

    public void RegisterWindow(Window window) =>
        _inputController.RegisterWindow(window);

    public void UnregisterWindow(Window window) =>
        _inputController.UnregisterWindow(window);

    public bool HandleKeyDown(KeyEventArgs e, IInputElement? focusedElement) =>
        _inputController.HandleKeyDown(e, focusedElement);

    public bool HandleMouseDown(object? sender, MouseButtonEventArgs e) =>
        _inputController.HandleMouseDown(sender, e);

    public bool HandleMouseMove(object? sender, System.Windows.Input.MouseEventArgs e) =>
        _inputController.HandleMouseMove(sender, e);

    public bool HandleMouseUp(object? sender, MouseButtonEventArgs e) =>
        _inputController.HandleMouseUp(sender, e);

    public bool HandleMouseDoubleClick(object? sender, MouseButtonEventArgs e) =>
        _inputController.HandleMouseDoubleClick(sender, e);

    public bool HandleWindowDrag(object? sender, MouseButtonEventArgs e) =>
        _inputController.HandleWindowDrag(sender, e);

    public void HandleMouseEnter(object? sender, System.Windows.Input.MouseEventArgs e) =>
        _inputController.HandleMouseEnter(sender, e);

    public void HandleMouseLeave(object? sender, System.Windows.Input.MouseEventArgs e) =>
        _inputController.HandleMouseLeave(sender, e);

    public bool HandleButtonClick(object? sender, RoutedEventArgs e) =>
        _inputController.HandleButtonClick(sender, e);

    #endregion

    public void OnPropertyChanged(params string[] propertyNames)
    {
        _pendingUIOperations.Enqueue(() =>
        {
            foreach (var name in propertyNames)
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        });
    }

    private void ProcessBatchedUIOperations(object? sender, EventArgs e)
    {
        int processedCount = 0;
        const int MAX_OPERATIONS_PER_FRAME = 10;

        while (processedCount < MAX_OPERATIONS_PER_FRAME &&
               _pendingUIOperations.TryDequeue(out var operation))
        {
            operation();
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
        _batchUpdateTimer?.Stop();

        while (_pendingUIOperations.TryDequeue(out var operation))
        {
            operation();
        }
    }

    private void StopCaptureIfNeeded()
    {
        if (IsRecording && _audioController.IsValueCreated)
            AudioController.StopCaptureAsync().GetAwaiter().GetResult();
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
        var (viewController, audioController, uiController) = CaptureExistingControllers();

        if (viewController != null)
            DisposeController(viewController, "ViewController");

        if (audioController != null)
            DisposeController(audioController, "AudioController");

        if (uiController != null)
            DisposeController(uiController, "UIController");

        DisposeInputController();
    }

    private (ViewController? viewController,
             AudioController? audioController,
             UIController? uiController) CaptureExistingControllers()
    {
        lock (_disposeLock)
        {
            ViewController? viewController =
                _viewController.IsValueCreated ? _viewController.Value : null;

            AudioController? audioController =
                _audioController.IsValueCreated ? _audioController.Value : null;

            UIController? uiController =
                _uiController.IsValueCreated ? _uiController.Value : null;

            return (viewController, audioController, uiController);
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

    private void DisposeInputController()
    {
        Safe(() => (_inputController as IDisposable)?.Dispose(),
            $"{LOG_PREFIX}.{nameof(DisposeInputController)}",
            "Error disposing InputController");
    }

    private void DisposeResourceObjects()
    {
        _transitionLock?.Dispose();
        _cleanupCts?.Dispose();
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
        _batchUpdateTimer?.Stop();

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
            await AudioController.StopCaptureAsync().WaitAsync(token);
    }

    private async Task CloseUIElementsAsync(CancellationToken token)
    {
        if (!_uiController.IsValueCreated) return;

        await Dispatcher.InvokeAsync(() =>
        {
            if (IsOverlayActive)
                UIController.CloseOverlay();

            if (IsControlPanelOpen)
                UIController.CloseControlPanel();
        }).Task.WaitAsync(token);
    }

    private async Task DisposeControllersAsync(CancellationToken token)
    {
        var (viewController, audioController, uiController) = CaptureExistingControllers();

        if (viewController != null)
            await DisposeControllerAsync(viewController, "ViewController", token);

        if (audioController != null)
            await DisposeControllerAsync(audioController, "AudioController", token);

        if (uiController != null)
            await DisposeControllerAsync(uiController, "UIController", token);

        await DisposeInputControllerAsync(token);
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
                await asyncDisposable.DisposeAsync().AsTask().WaitAsync(token);
            }
            else if (controller is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error,
                LOG_PREFIX,
                $"Error disposing {controllerName}: {ex.Message}");
        }
    }

    private async Task DisposeInputControllerAsync(CancellationToken token)
    {
        if (_inputController is IAsyncDisposable asyncDisposable)
        {
            await SafeAsync(
                async () => await asyncDisposable.DisposeAsync().AsTask().WaitAsync(token),
                $"{LOG_PREFIX}.{nameof(DisposeInputControllerAsync)}",
                "Error async disposing InputController",
                ignoreExceptions: [typeof(OperationCanceledException)]);
        }
        else if (_inputController is IDisposable disposable)
        {
            Safe(() => disposable.Dispose(),
                $"{LOG_PREFIX}.{nameof(DisposeInputControllerAsync)}",
                "Error disposing InputController");
        }
    }
}