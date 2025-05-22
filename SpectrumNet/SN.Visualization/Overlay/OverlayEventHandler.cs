#nullable enable

namespace SpectrumNet.SN.Visualization.Overlay;

public sealed class OverlayEventHandler(
    IOverlayRenderManager renderManager,
    IOverlayPerformanceManager performanceManager,
    ITransparencyManager transparencyManager,
    OverlayConfiguration configuration) : IOverlayEventHandler
{
    private const string LogPrefix = nameof(OverlayEventHandler);

    private readonly ISmartLogger _logger = Instance;

    private readonly IOverlayRenderManager _renderManager = renderManager ??
        throw new ArgumentNullException(nameof(renderManager));

    private readonly IOverlayPerformanceManager _performanceManager = performanceManager ??
        throw new ArgumentNullException(nameof(performanceManager));

    private readonly ITransparencyManager _transparencyManager = transparencyManager ??
        throw new ArgumentNullException(nameof(transparencyManager));

    private readonly OverlayConfiguration _configuration = configuration ??
        throw new ArgumentNullException(nameof(configuration));

    private bool _disposed;

    public void RegisterEvents(Window window, SKElement renderElement)
    {
        if (_disposed)
            return;

        _logger.Safe(() => HandleRegisterEvents(window),
            LogPrefix, "Error registering events");
    }

    public void UnregisterEvents(Window window, SKElement renderElement)
    {
        if (_disposed)
            return;

        _logger.Safe(() => HandleUnregisterEvents(window),
            LogPrefix, "Error unregistering events");
    }

    private void HandleRegisterEvents(Window window)
    {
        window.Closing += OnWindowClosing;
        window.SourceInitialized += OnSourceInitialized;
        window.DpiChanged += OnDpiChanged;
        window.IsVisibleChanged += OnIsVisibleChanged;
        window.MouseMove += OnMouseMove;
        window.MouseEnter += OnMouseEnter;
        window.MouseLeave += OnMouseLeave;

        if (_configuration.EnableEscapeToClose)
            window.KeyDown += OnKeyDown;

        CompositionTarget.Rendering += OnRendering;
    }

    private void HandleUnregisterEvents(Window window)
    {
        window.Closing -= OnWindowClosing;
        window.SourceInitialized -= OnSourceInitialized;
        window.DpiChanged -= OnDpiChanged;
        window.IsVisibleChanged -= OnIsVisibleChanged;
        window.MouseMove -= OnMouseMove;
        window.MouseEnter -= OnMouseEnter;
        window.MouseLeave -= OnMouseLeave;

        if (_configuration.EnableEscapeToClose)
            window.KeyDown -= OnKeyDown;

        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e) =>
        _logger.Safe(() => CompositionTarget.Rendering -= OnRendering,
            LogPrefix, "Error handling window closing");

    private void OnSourceInitialized(object? sender, EventArgs e) =>
        _logger.Safe(() => HandleSourceInitialized(sender),
            LogPrefix, "Error handling source initialized");

    private void OnDpiChanged(object? sender, DpiChangedEventArgs e) =>
        _logger.Safe(() => _renderManager.RequestRender(),
            LogPrefix, "Error handling DPI changed");

    private void OnIsVisibleChanged(object? sender, DependencyPropertyChangedEventArgs e) =>
        _logger.Safe(() => HandleVisibilityChanged(sender),
            LogPrefix, "Error handling visibility changed");

    private void OnKeyDown(object sender, KeyEventArgs e) =>
        _logger.Safe(() => HandleKeyDown(sender, e),
            LogPrefix, "Error handling key down");

    private void OnRendering(object? sender, EventArgs e) =>
        _logger.Safe(() => HandleRendering(),
            LogPrefix, "Error handling rendering");

    private void OnMouseMove(object sender, MouseEventArgs e) =>
        _logger.Safe(() => HandleMouseMove(e),
            LogPrefix, "Error handling mouse move");

    private void OnMouseEnter(object sender, MouseEventArgs e) =>
        _logger.Safe(() => HandleMouseEnter(e),
            LogPrefix, "Error handling mouse enter");

    private void OnMouseLeave(object sender, MouseEventArgs e) =>
        _logger.Safe(() => HandleMouseLeave(e),
            LogPrefix, "Error handling mouse leave");

    private void HandleSourceInitialized(object? sender)
    {
        if (sender is Window window)
        {
            ApplyWindowOptimizations(window);
            _transparencyManager.ActivateTransparency();
            _renderManager.RequestRender();
        }
    }

    private void HandleVisibilityChanged(object? sender)
    {
        if (sender is Window { IsVisible: true })
        {
            _transparencyManager.ActivateTransparency();
            _renderManager.RequestRender();
        }
    }

    private void HandleKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (sender is Window window)
                window.Close();
        }
    }

    private void HandleRendering()
    {
        if (_disposed)
            return;

        if (_performanceManager.ShouldRender())
        {
            _renderManager.RequestRender();
            _performanceManager.RecordFrame();
        }
    }

    private void HandleMouseMove(MouseEventArgs e)
    {
        _transparencyManager.OnMouseMove();
        e.Handled = true;
    }

    private void HandleMouseEnter(MouseEventArgs e)
    {
        _transparencyManager.OnMouseEnter();
        e.Handled = true;
    }

    private void HandleMouseLeave(MouseEventArgs e)
    {
        _transparencyManager.OnMouseLeave();
        e.Handled = true;
    }

    private void ApplyWindowOptimizations(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == nint.Zero || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        var extendedStyle = NativeMethods.GetWindowLong(handle, NativeMethods.GWL_EXSTYLE);
        _ = NativeMethods.SetWindowLong(
            handle,
            NativeMethods.GWL_EXSTYLE,
            extendedStyle | NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT
        );

        if (_configuration.DisableWindowAnimations)
            SystemBackdrop.DisableWindowAnimations(handle);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
    }
}