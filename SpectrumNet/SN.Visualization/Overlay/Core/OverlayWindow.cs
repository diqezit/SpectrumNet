#nullable enable

namespace SpectrumNet.SN.Visualization.Overlay.Core;

public sealed class OverlayWindow : Window, IDisposable
{
    private const string LogPrefix = nameof(OverlayWindow);
    private readonly ISmartLogger _logger = Instance;

    private readonly IMainController _controller;
    private readonly OverlayConfiguration _configuration;
    private readonly ITransparencyManager _transparencyManager;

    private readonly IOverlayWindowConfigurator _windowConfigurator;
    private readonly IOverlayPerformanceManager _performanceManager;
    private readonly IOverlayRenderManager _renderManager;
    private readonly IOverlayEventHandler _eventHandler;

    private readonly SKElement _renderElement;
    private bool _isDisposed;

    public new bool IsInitialized { get; private set; }

    public new bool Topmost
    {
        get => base.Topmost;
        set => _logger.Safe(() => HandleSetTopmost(value),
            LogPrefix, "Error setting topmost property");
    }

    public OverlayWindow(
        IMainController controller,
        OverlayConfiguration? configuration = null)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _configuration = configuration ?? new();
        _transparencyManager = RendererTransparencyManager.Instance;

        _renderElement = CreateRenderElement();

        _windowConfigurator = new OverlayWindowConfigurator();
        _performanceManager = new OverlayPerformanceManager();
        _renderManager = new OverlayRenderManager(_transparencyManager);
        _eventHandler = new OverlayEventHandler(
            _renderManager,
            _performanceManager,
            _transparencyManager,
            _configuration);

        InitializeWindow();
    }

    public void ForceRedraw() =>
        _logger.Safe(() => _renderManager.RequestRender(),
            LogPrefix, "Error forcing redraw");

    private void InitializeWindow()
    {
        _logger.Safe(() => HandleInitializeWindow(),
            LogPrefix, "Error initializing window");
    }

    private void HandleInitializeWindow()
    {
        _windowConfigurator.ConfigureWindow(this, _configuration);
        _windowConfigurator.ApplyTransparency(this);
        _windowConfigurator.ConfigureRendering(this);

        _performanceManager.Initialize(_controller);
        _renderManager.InitializeRenderer(_controller, _renderElement);

        _eventHandler.RegisterEvents(this, _renderElement);
        _transparencyManager.TransparencyChanged += OnTransparencyChanged;
        _controller.PropertyChanged += OnControllerPropertyChanged;

        Content = _renderElement;
        _controller.InputController.RegisterWindow(this);

        PreviewKeyDown += OnPreviewKeyDown;
        Focusable = true;
        IsInitialized = true;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_configuration.EnableEscapeToClose)
            {
                e.Handled = true;
                Close();
                return;
            }
        }
        bool handled = _controller.InputController.HandleKeyDown(e,
            Keyboard.FocusedElement);

        e.Handled = handled;
    }

    private SKElement CreateRenderElement() =>
        _logger.SafeResult(() => new SKElement(), new SKElement(),
            LogPrefix, "Error creating render element");

    private void HandleSetTopmost(bool value)
    {
        if (base.Topmost == value)
            return;

        base.Topmost = value;
        ForceRedraw();
    }

    private void OnTransparencyChanged(float level) =>
        _logger.Safe(() => Dispatcher.Invoke(() =>
        {
            Opacity = level;
            ForceRedraw();
        }), LogPrefix, "Error handling transparency change");

    private void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        _logger.Safe(() => HandleControllerPropertyChanged(e),
            LogPrefix, "Error handling controller property change");

    private void HandleControllerPropertyChanged(PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(IMainController.LimitFpsTo60)))
        {
            _performanceManager.UpdateFpsLimit(_controller.LimitFpsTo60);
            ForceRedraw();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Dispose();
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _logger.Safe(() => HandleDispose(),
            LogPrefix, "Error disposing overlay window");
    }

    private void HandleDispose()
    {
        PreviewKeyDown -= OnPreviewKeyDown;
        _transparencyManager.TransparencyChanged -= OnTransparencyChanged;
        _controller.PropertyChanged -= OnControllerPropertyChanged;
        _eventHandler.UnregisterEvents(this, _renderElement);
        _controller.InputController.UnregisterWindow(this);
        _eventHandler.Dispose();
        _renderManager.Dispose();
        _performanceManager.Dispose();

        _isDisposed = true;
    }
}