#nullable enable

namespace SpectrumNet.Controllers;

public class ControllerFactory
    : AsyncDisposableBase, IMainController
{
    private const string LogPrefix = "ControllerFactory";

    private readonly SemaphoreSlim _transitionSemaphore = new(1, 1);
    private readonly CancellationTokenSource _cleanupCts = new();

    private readonly IUIController _uiController;
    private readonly IAudioController _audioController;
    private readonly IViewController _viewController;
    private readonly IInputController _inputController;

    public event PropertyChangedEventHandler? PropertyChanged;

    public SpectrumAnalyzer Analyzer { get; set; }
    public Renderer? Renderer { get; set; }

    public ControllerFactory(Window ownerWindow, SKElement renderElement)
    {
        ArgumentNullException.ThrowIfNull(ownerWindow);
        ArgumentNullException.ThrowIfNull(renderElement);

        var syncContext = SynchronizationContext.Current ??
            throw new InvalidOperationException("No synchronization context. Controller must be created in UI thread.");

        _uiController = new UIController(this);
        _audioController = new AudioController(this, syncContext);
        _viewController = new ViewController(this, renderElement);
        _inputController = new InputController(this);

        _audioController.WindowType = Settings.Instance.SelectedFftWindowType;

        Analyzer = new SpectrumAnalyzer(
           new FftProcessor { WindowType = this.WindowType },
           new SpectrumConverter(this.GainParameters),
           syncContext
       ) ?? throw new InvalidOperationException("Failed to create SpectrumAnalyzer");

        Renderer = new Renderer(
            this.SpectrumStyles,
            this,
            Analyzer,
            renderElement
        ) ?? throw new InvalidOperationException("Failed to create Renderer");

        _viewController.Analyzer = Analyzer;
        _viewController.Renderer = Renderer;

        ownerWindow.Closed += (_, _) => DisposeResources();
    }

    #region Свойства доступа к контроллерам

    public IUIController UIController => _uiController;
    public IAudioController AudioController => _audioController;
    public IViewController ViewController => _viewController;
    public IInputController InputController => _inputController;

    public Dispatcher Dispatcher => Application.Current?.Dispatcher ??
        throw new InvalidOperationException("Application.Current is null");

    #endregion

    #region Делегирование IUIController

    public bool IsOverlayActive
    {
        get => _uiController.IsOverlayActive;
        set => _uiController.IsOverlayActive = value;
    }

    public bool IsOverlayTopmost
    {
        get => _uiController.IsOverlayTopmost;
        set => _uiController.IsOverlayTopmost = value;
    }

    public bool IsPopupOpen
    {
        get => _uiController.IsPopupOpen;
        set => _uiController.IsPopupOpen = value;
    }

    public bool IsControlPanelOpen => _uiController.IsControlPanelOpen;

    public void ToggleTheme() => _uiController.ToggleTheme();
    public void OpenControlPanel() => _uiController.OpenControlPanel();
    public void CloseControlPanel() => _uiController.CloseControlPanel();
    public void ToggleControlPanel() => _uiController.ToggleControlPanel();
    public void OpenOverlay() => _uiController.OpenOverlay();
    public void CloseOverlay() => _uiController.CloseOverlay();

    #endregion

    #region Делегирование IAudioController

    public GainParameters GainParameters => _audioController.GainParameters;

    public bool IsRecording
    {
        get => _audioController.IsRecording;
        set => _audioController.IsRecording = value;
    }

    public bool CanStartCapture => _audioController.CanStartCapture;

    public bool IsTransitioning
    {
        get => _audioController.IsTransitioning;
        set => _audioController.IsTransitioning = value;
    }

    public FftWindowType WindowType
    {
        get => _audioController.WindowType;
        set
        {
            _audioController.WindowType = value;
            OnPropertyChanged(nameof(WindowType));
        }
    }

    public float MinDbLevel
    {
        get => _audioController.MinDbLevel;
        set => _audioController.MinDbLevel = value;
    }

    public float MaxDbLevel
    {
        get => _audioController.MaxDbLevel;
        set => _audioController.MaxDbLevel = value;
    }

    public float AmplificationFactor
    {
        get => _audioController.AmplificationFactor;
        set => _audioController.AmplificationFactor = value;
    }

    public async Task StartCaptureAsync() => await _audioController.StartCaptureAsync();
    public async Task StopCaptureAsync() => await _audioController.StopCaptureAsync();
    public async Task ToggleCaptureAsync() => await _audioController.ToggleCaptureAsync();
    public SpectrumAnalyzer? GetCurrentAnalyzer() => _audioController.GetCurrentAnalyzer();

    #endregion

    #region Делегирование IViewController

    public SKElement SpectrumCanvas => _viewController.SpectrumCanvas;
    public SpectrumBrushes SpectrumStyles => _viewController.SpectrumStyles;

    public int BarCount
    {
        get => _viewController.BarCount;
        set => _viewController.BarCount = value;
    }

    public double BarSpacing
    {
        get => _viewController.BarSpacing;
        set => _viewController.BarSpacing = value;
    }

    public RenderQuality RenderQuality
    {
        get => _viewController.RenderQuality;
        set => _viewController.RenderQuality = value;
    }

    public RenderStyle SelectedDrawingType
    {
        get => _viewController.SelectedDrawingType;
        set => _viewController.SelectedDrawingType = value;
    }

    public SpectrumScale ScaleType
    {
        get => _viewController.ScaleType;
        set => _viewController.ScaleType = value;
    }

    public string SelectedStyle
    {
        get => _viewController.SelectedStyle;
        set => _viewController.SelectedStyle = value;
    }

    public Palette? SelectedPalette
    {
        get => _viewController.SelectedPalette;
        set => _viewController.SelectedPalette = value;
    }

    public bool ShowPerformanceInfo
    {
        get => _viewController.ShowPerformanceInfo;
        set => _viewController.ShowPerformanceInfo = value;
    }

    public IReadOnlyDictionary<string, Palette> AvailablePalettes => 
        _viewController.AvailablePalettes;

    public IEnumerable<RenderStyle> AvailableDrawingTypes => 
        _viewController.AvailableDrawingTypes;

    public IEnumerable<FftWindowType> AvailableFftWindowTypes => 
        _viewController.AvailableFftWindowTypes;

    public IEnumerable<SpectrumScale> AvailableScaleTypes => 
        _viewController.AvailableScaleTypes;

    public IEnumerable<RenderQuality> AvailableRenderQualities => 
        _viewController.AvailableRenderQualities;

    public IEnumerable<RenderStyle> OrderedDrawingTypes => 
        _viewController.OrderedDrawingTypes;

    public void RequestRender() => 
        _viewController.RequestRender();

    public void UpdateRenderDimensions(int width, int height) => 
        _viewController.UpdateRenderDimensions(width, height);

    public void SynchronizeVisualization() => 
        _viewController.SynchronizeVisualization();

    public void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs? e) => 
        _viewController.OnPaintSurface(sender, e);

    #endregion

    #region Делегирование IInputController

    public bool HandleKeyDown(KeyEventArgs e, IInputElement? focusedElement) =>
        _inputController.HandleKeyDown(e, focusedElement);

    #endregion

    #region Общие методы и Очистка ресурсов

    public void OnPropertyChanged(params string[] propertyNames) =>
        Safe(() => 
        {
            if (PropertyChanged == null) return;
            foreach (var name in propertyNames)
            {
                Dispatcher.Invoke(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)));
            }
        }, new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error notifying property change" });

    public void DisposeResources() => Dispose();

    protected override void DisposeManaged() 
    {
        if (_isDisposed) return;
        Log(LogLevel.Information,
            LogPrefix,
            "Starting synchronous dispose");
        try
        {
            CleanupResourcesAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error,
                LogPrefix,
                $"Exception during synchronous dispose: {ex}");
        }
        finally
        {
            base.DisposeManaged(); 
            Log(LogLevel.Information,
                LogPrefix,
                "Synchronous dispose completed");
        }
    }

    protected override async ValueTask DisposeAsyncManagedResources()
    {
        if (_isDisposed) return;

        Log(LogLevel.Information,
            LogPrefix,
            "Starting asynchronous dispose");

        try
        {
            await CleanupResourcesAsync();
            await base.DisposeAsyncManagedResources();

            Log(LogLevel.Information,
                LogPrefix,
                "Asynchronous dispose completed");
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error,
                LogPrefix,
                $"Error during asynchronous dispose: {ex.Message}");
        }
    }

    private async Task CleanupResourcesAsync()
    {
        Log(LogLevel.Information, LogPrefix, "Starting cleanup of resources");

        if (!_cleanupCts.IsCancellationRequested)
            _cleanupCts.Cancel(); 

        using var overallTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var overallToken = overallTimeoutCts.Token;

        try
        {
            if (IsRecording)
            {
                Log(LogLevel.Debug, LogPrefix, "Stopping audio capture...");
                try
                {
                    await TimeoutAfter(
                        StopCaptureAsync(),
                        "StopCapture",
                        TimeSpan.FromSeconds(3),
                        overallToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Log(LogLevel.Error,
                        LogPrefix,
                        $"Error stopping capture: {ex.Message}");
                }
            }

            if (IsOverlayActive) Safe(
                CloseOverlay,
                new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error closing overlay" });

            if (IsControlPanelOpen) Safe(
                CloseControlPanel,
                new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error closing control panel" });

            var disposeTasks = new List<Task>();

            Log(LogLevel.Debug, LogPrefix, "Disposing controllers...");

            if (_viewController is IAsyncDisposable asyncView) 
                disposeTasks.Add(DisposeControllerAsyncWithTimeout(
                    asyncView,
                    "ViewController",
                    FromSeconds(3),
                    overallToken));

            if (_audioController is IAsyncDisposable asyncAudio) 
                disposeTasks.Add(DisposeControllerAsyncWithTimeout(
                    asyncAudio,
                    "AudioController",
                    FromSeconds(3),
                    overallToken));

            if (_uiController is IAsyncDisposable asyncUi) 
                disposeTasks.Add(DisposeControllerAsyncWithTimeout(
                    asyncUi,
                    "UIController",
                    FromSeconds(3),
                    overallToken));

            if (disposeTasks.Count > 0)
            {
                try
                {
                    await Task.WhenAll(disposeTasks).WaitAsync(overallToken);
                }
                catch (OperationCanceledException) { Log(
                    LogLevel.Warning,
                    LogPrefix,
                    "Overall timeout reached waiting for async disposal."); }

                catch (Exception ex) { Log(
                    LogLevel.Error,
                    LogPrefix,
                    $"Error waiting for async disposal tasks: {ex.Message}"); }
            }

            DisposeControllerSync(
                _viewController as IDisposable,
                _viewController is IAsyncDisposable);

            DisposeControllerSync(
                _audioController as IDisposable,
                _audioController is IAsyncDisposable);

            DisposeControllerSync(
                _uiController as IDisposable,
                _uiController is IAsyncDisposable);

            DisposeControllerSync(
                _inputController as IDisposable,
                _inputController is IAsyncDisposable);

            _transitionSemaphore?.Dispose();
            _cleanupCts?.Dispose();

            Log(LogLevel.Information,
                LogPrefix,
                "Resource cleanup process completed.");
        }
        catch (OperationCanceledException) when (overallToken.IsCancellationRequested)
        {
            Log(LogLevel.Warning,
                LogPrefix,
                "Resource cleanup timed out overall.");
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error,
                LogPrefix,
                $"Unexpected error during resource cleanup: {ex.Message}");
        }
    }

    private static async Task TimeoutAfter(
        Task task,
        string resourceName,
        TimeSpan timeout,
        CancellationToken overallToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(overallToken);
        linkedCts.CancelAfter(timeout);
        try
        {
            await task.WaitAsync(linkedCts.Token);

            Log(LogLevel.Debug,
                LogPrefix,
                $"{resourceName} completed within timeout.");
        }
        catch (OperationCanceledException) when (overallToken.IsCancellationRequested)
        {
            Log(LogLevel.Warning,
                LogPrefix,
                $"Overall cleanup timeout reached during {resourceName}.");
        }
        catch (OperationCanceledException) 
        {
            Log(LogLevel.Warning,
                LogPrefix,
                $"Timeout occurred while executing {resourceName} ({timeout.TotalSeconds}s).");
        }
    }

    private static async Task DisposeControllerAsyncWithTimeout(
        IAsyncDisposable disposable,
        string resourceName,
        TimeSpan timeout,
        CancellationToken overallToken)
    {
        Log(LogLevel.Debug,
            LogPrefix,
            $"Attempting async dispose for {resourceName}...");

        await TimeoutAfter(
            disposable.DisposeAsync().AsTask(),
            $"AsyncDispose_{resourceName}",
            timeout,
            overallToken);
    }

    private static void DisposeControllerSync(
        IDisposable? disposable,
        bool wasAsyncDisposable)
    {
        if (disposable == null || wasAsyncDisposable) return;

        string typeName = disposable.GetType().Name;

        Log(LogLevel.Debug,
            LogPrefix,
            $"Attempting sync dispose for {typeName}...");
        try
        {
            disposable.Dispose();

            Log(LogLevel.Information,
                LogPrefix,
                $"{typeName} disposed synchronously");
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error,
                LogPrefix,
                $"Error disposing {typeName} synchronously: {ex.Message}");
        }
    }

    #endregion
}