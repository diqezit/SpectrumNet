// SpectrumNet/Controllers/ViewCore/RenderingManager.cs
#nullable enable

namespace SpectrumNet.SN.Controllers.View;

public class RenderingManager(
    IMainController mainController,
    IRendererFactory rendererFactory) : IRenderingManager, IDisposable
{
    private const string LogPrefix = nameof(RenderingManager);
    private readonly ISmartLogger _logger = Instance;

    private readonly IMainController _mainController = mainController ??
        throw new ArgumentNullException(nameof(mainController));

    private readonly IRendererFactory _rendererFactory = rendererFactory ??
        throw new ArgumentNullException(nameof(rendererFactory));

    private Renderer? _renderer;
    private bool _isDisposed;

    public Renderer? Renderer
    {
        get => _renderer;
        set => _renderer = value;
    }

    public void RequestRender()
    {
        if (_isDisposed) return;
        _renderer?.RequestRender();
    }

    public void UpdateDimensions(int width, int height)
    {
        if (_isDisposed) return;
        _renderer?.UpdateRenderDimensions(width, height);
    }

    public void SynchronizeVisualization()
    {
        if (_isDisposed) return;
        _renderer?.SynchronizeWithController();
    }

    public void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs? e)
    {
        if (_isDisposed || e == null || _renderer == null)
            return;

        _renderer.RenderFrame(sender, e);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (_renderer != null)
        {
            _renderer.Dispose();
            _renderer = null;
        }
    }
}