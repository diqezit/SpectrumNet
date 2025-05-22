#nullable enable

namespace SpectrumNet.SN.Visualization.Overlay.Interfaces;

public interface IOverlayRenderManager : IDisposable
{
    void InitializeRenderer(IMainController controller, SKElement renderElement);
    void RequestRender();
    void HandlePaintSurface(object? sender, SKPaintSurfaceEventArgs args);
}
