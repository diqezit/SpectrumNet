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

    private readonly Window _ownerWindow;
    private readonly SKElement _renderElement;
    private bool _isTransitioning;
    private bool _isRecording;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ControllerFactory(Window ownerWindow, SKElement renderElement)
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

        InitializeRenderingState();
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

    private UIController CreateUIController() => new(this);
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

    public bool HandleKeyDown(KeyEventArgs e, IInputElement? focusedElement) =>
        _inputController.HandleKeyDown(e, focusedElement);

    public void OnPropertyChanged(params string[] propertyNames) =>
        ExecuteSafely(() =>
        {
            if (PropertyChanged == null) return;

            foreach (var name in propertyNames)
                Dispatcher.Invoke(() =>
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)));
        },
        nameof(OnPropertyChanged),
        "Error notifying property change");

    public void DisposeResources() => Dispose();

    protected override void DisposeManaged()
    {
        if (_isDisposed) return;

        Log(LogLevel.Information, LOG_PREFIX, "Starting synchronous dispose");

        try
        {
            CleanupInitiation();
            StopCaptureIfNeeded();
            CloseUIElementsIfNeeded();
            DisposeAllControllers();
            DisposeResourceObjects();
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, LOG_PREFIX, $"Exception during synchronous dispose: {ex}");
        }
        finally
        {
            Log(LogLevel.Information, LOG_PREFIX, "Synchronous dispose completed");
        }
    }

    private void CleanupInitiation()
    {
        if (_ownerWindow != null)
            _ownerWindow.KeyDown -= OnWindowKeyDown;

        _cleanupCts.Cancel();
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
        DisposeControllerIfCreated(_viewController, typeof(ViewController).Name);
        DisposeControllerIfCreated(_audioController, typeof(AudioController).Name);
        DisposeControllerIfCreated(_uiController, typeof(UIController).Name);

        DisposeInputController();
    }

    private void DisposeInputController() =>
        ExecuteSafely(
            () => (_inputController as IDisposable)?.Dispose(),
            nameof(DisposeInputController),
            "Error disposing InputController");

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

        await StopCaptureAsync(token);
        await CloseUIElementsAsync(token);
        await DisposeControllersAsync(token);

        _transitionLock?.Dispose();
        _cleanupCts?.Dispose();
    }

    private async Task DetachEventHandlersAsync(CancellationToken token) =>
        await Dispatcher.InvokeAsync(() =>
        {
            if (_ownerWindow != null)
                _ownerWindow.KeyDown -= OnWindowKeyDown;
        }).Task.WaitAsync(token);

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
        await DisposeControllerAsyncIfCreated(
            _viewController,
            typeof(ViewController).Name,
            token);

        await DisposeControllerAsyncIfCreated(
            _audioController,
            typeof(AudioController).Name,
            token);

        await DisposeControllerAsyncIfCreated(
            _uiController,
            typeof(UIController).Name,
            token);

        await DisposeInputControllerAsync(token);
    }

    private async Task DisposeInputControllerAsync(CancellationToken token)
    {
        if (_inputController is IAsyncDisposable asyncDisposable)
            await ExecuteSafelyAsync(
                async () => await asyncDisposable.DisposeAsync().AsTask().WaitAsync(token),
                nameof(DisposeInputControllerAsync),
                "Error async disposing InputController",
                [typeof(OperationCanceledException)]);
        else if (_inputController is IDisposable disposable)
            ExecuteSafely(
                () => disposable.Dispose(),
                nameof(DisposeInputControllerAsync),
                "Error disposing InputController");
    }

    private static void DisposeControllerIfCreated<T>(
        Lazy<T> controller,
        string controllerName) where T : class
    {
        if (!controller.IsValueCreated) return;

        var instance = controller.Value;

        if (instance is IDisposable disposable)
            ExecuteSafely(
                () => disposable.Dispose(),
                nameof(DisposeControllerIfCreated),
                $"Error disposing {controllerName}");
    }

    private static async Task DisposeControllerAsyncIfCreated<T>(
        Lazy<T> controller,
        string controllerName,
        CancellationToken token) where T : class
    {
        if (!controller.IsValueCreated) return;

        var instance = controller.Value;

        if (instance is IAsyncDisposable asyncDisposable)
            await ExecuteSafelyAsync(
                async () => await asyncDisposable.DisposeAsync().AsTask().WaitAsync(token),
                nameof(DisposeControllerAsyncIfCreated),
                $"Error async disposing {controllerName}",
                [typeof(OperationCanceledException)]);
        else if (instance is IDisposable disposable)
            ExecuteSafely(
                () => disposable.Dispose(),
                nameof(DisposeControllerAsyncIfCreated),
                $"Error disposing {controllerName}");
    }

    private static void ExecuteSafely(
        Action action,
        string source,
        string errorMessage) =>
        Safe(
            action,
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.{source}",
                ErrorMessage = errorMessage
            });

    private static async Task ExecuteSafelyAsync(
        Func<Task> asyncAction,
        string source,
        string errorMessage,
        Type[]? ignoreExceptions = null) =>
        await SafeAsync(
            asyncAction,
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.{source}",
                ErrorMessage = errorMessage,
                IgnoreExceptions = ignoreExceptions
            });
}