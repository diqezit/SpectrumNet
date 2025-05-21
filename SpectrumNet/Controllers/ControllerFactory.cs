#nullable enable

namespace SpectrumNet.Controllers;

public sealed class ControllerFactory : IControllerProvider, IDisposable
{
    private const string LogPrefix = nameof(ControllerFactory);
    private readonly ISmartLogger _logger = Instance;

    private readonly Window _ownerWindow;
    private readonly SKElement _renderElement;

    private readonly IMainController _mainController;
    private readonly IUIController _uiController;
    private readonly IAudioController _audioController;
    private readonly IViewController _viewController;
    private readonly IInputController _inputController;
    private readonly IOverlayManager _overlayManager;
    private readonly IBatchOperationsManager _batchOperationsManager;
    private readonly IResourceCleanupManager _resourceCleanupManager;
    private readonly IFpsLimiter _fpsLimiter;
    private readonly ITransparencyManager _transparencyManager;

    private bool _isDisposed;

    public ControllerFactory(Window ownerWindow, SKElement renderElement)
    {
        _ownerWindow = ownerWindow ?? throw new ArgumentNullException(nameof(ownerWindow));
        _renderElement = renderElement ?? throw new ArgumentNullException(nameof(renderElement));

        if (SynchronizationContext.Current == null)
            throw new InvalidOperationException("No synchronization context. Controller must be created in UI thread.");

        _batchOperationsManager = new BatchOperationsManager();
        _resourceCleanupManager = new ResourceCleanupManager();
        _fpsLimiter = new FpsLimiterManager(Settings.Instance);
        _transparencyManager = RendererTransparencyManager.Instance;

        _mainController = new MainController(this, ownerWindow);

        _viewController = CreateViewController();
        _audioController = CreateAudioController();
        _uiController = CreateUIController();
        _inputController = CreateInputController();
        _overlayManager = CreateOverlayManager();

        InitializeRendering();
        RegisterWindowEvents();
    }

    public IUIController UIController => _uiController;
    public IAudioController AudioController => _audioController;
    public IViewController ViewController => _viewController;
    public IInputController InputController => _inputController;
    public IOverlayManager OverlayManager => _overlayManager;
    public IBatchOperationsManager BatchOperationsManager => _batchOperationsManager;
    public IResourceCleanupManager ResourceCleanupManager => _resourceCleanupManager;
    public IFpsLimiter FpsLimiter => _fpsLimiter;
    public IMainController MainController => _mainController;

    private IViewController CreateViewController() =>
        ViewControllerFactory.Create(_mainController, _renderElement);

    private IAudioController CreateAudioController() =>
        new AudioController(_mainController, SynchronizationContext.Current!);

    private IUIController CreateUIController() =>
        new UIController(_mainController, _transparencyManager);

    private IInputController CreateInputController() =>
        new InputController(_mainController);

    private IOverlayManager CreateOverlayManager() =>
        new OverlayManager(_mainController, _transparencyManager);

    private void InitializeRendering()
    {
        var syncContext = SynchronizationContext.Current!;

        var analyzer = new SpectrumAnalyzer(
            new FftProcessor { WindowType = _audioController.WindowType },
            new SpectrumConverter(_audioController.GainParameters),
            syncContext
        );

        _viewController.Analyzer = analyzer;

        var renderer = new Renderer(
            SpectrumBrushes.Instance,
            _mainController,
            analyzer,
            _renderElement,
            RendererFactory.Instance
        );

        _viewController.Renderer = renderer;

        if (_viewController is VisualizationController viewController)
            viewController.SettingsManager.InitializeAfterRendererCreated();
    }

    private void RegisterWindowEvents()
    {
        _ownerWindow.Closed += (_, _) => Dispose();
        _ownerWindow.KeyDown += (s, e) => _inputController.HandleKeyDown(e, Keyboard.FocusedElement);
        _fpsLimiter.LimitChanged += (_, _) => _mainController.OnPropertyChanged(nameof(IMainController.LimitFpsTo60));
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _logger.Log(LogLevel.Information, LogPrefix, "Disposing ControllerFactory");

        if (_mainController is IDisposable disposableMain)
            _resourceCleanupManager.DisposeController(disposableMain, nameof(MainController));

        if (_batchOperationsManager is IDisposable disposableBatch)
            _resourceCleanupManager.DisposeController(disposableBatch, nameof(BatchOperationsManager));
    }
}