#nullable enable

using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace SpectrumNet.SN.Visualization.Overlay;

public sealed class OverlayRenderManager
    (ITransparencyManager transparencyManager)
    : IOverlayRenderManager
{
    private const string LogPrefix = nameof(OverlayRenderManager);

    private readonly ISmartLogger _logger = Instance;

    private readonly ITransparencyManager _transparencyManager = transparencyManager ??
            throw new ArgumentNullException(nameof(transparencyManager));

    private IMainController? _controller;
    private SKElement? _renderElement;
    private bool _disposed;

    public void InitializeRenderer(IMainController controller, SKElement renderElement)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _renderElement = renderElement ?? throw new ArgumentNullException(nameof(renderElement));

        SetupRenderElement();
    }

    public void RequestRender()
    {
        if (_disposed || _renderElement == null)
            return;

        _logger.Safe(() => _renderElement.InvalidateVisual(),
            LogPrefix, "Error requesting render");
    }

    public void HandlePaintSurface(object? sender, SKPaintSurfaceEventArgs args)
    {
        if (_disposed || _controller == null || args == null)
            return;

        _logger.Safe(() => PerformRender(sender, args),
            LogPrefix, "Error handling paint surface");
    }

    private void SetupRenderElement()
    {
        if (_renderElement == null)
            return;

        _renderElement.VerticalAlignment = VerticalAlignment.Stretch;
        _renderElement.HorizontalAlignment = HorizontalAlignment.Stretch;
        _renderElement.SnapsToDevicePixels = true;
        _renderElement.UseLayoutRounding = true;

        RenderOptions.SetCachingHint(_renderElement, CachingHint.Cache);
        _renderElement.CacheMode = new BitmapCache
        {
            EnableClearType = false,
            SnapsToDevicePixels = true,
            RenderAtScale = 1.0
        };

        _renderElement.MouseMove += OnElementMouseMove;
        _renderElement.MouseEnter += OnElementMouseEnter;
        _renderElement.MouseLeave += OnElementMouseLeave;
        _renderElement.PaintSurface += HandlePaintSurface;
    }

    private void UnregisterElementEvents()
    {
        if (_renderElement == null)
            return;

        _renderElement.MouseMove -= OnElementMouseMove;
        _renderElement.MouseEnter -= OnElementMouseEnter;
        _renderElement.MouseLeave -= OnElementMouseLeave;
        _renderElement.PaintSurface -= HandlePaintSurface;
    }

    private void OnElementMouseMove(object sender, MouseEventArgs e) =>
        _transparencyManager.OnMouseMove();

    private void OnElementMouseEnter(object sender, MouseEventArgs e) =>
        _transparencyManager.OnMouseEnter();

    private void OnElementMouseLeave(object sender, MouseEventArgs e) =>
        _transparencyManager.OnMouseLeave();

    private void PerformRender(object? sender, SKPaintSurfaceEventArgs args)
    {
        args.Surface.Canvas.Clear(SKColors.Transparent);
        _controller?.OnPaintSurface(sender, args);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _logger.Safe(() => HandleDispose(),
            LogPrefix, "Error disposing render manager");
    }

    private void HandleDispose()
    {
        UnregisterElementEvents();
        _disposed = true;
    }
}