#nullable enable

namespace SpectrumNet.Controllers.RenderCore.Overlay.Interface;

public interface IOverlayRenderManager : IDisposable
{
    void InitializeRenderer(IMainController controller, SKElement renderElement);
    void RequestRender();
    void HandlePaintSurface(object? sender, SKPaintSurfaceEventArgs args);
}
