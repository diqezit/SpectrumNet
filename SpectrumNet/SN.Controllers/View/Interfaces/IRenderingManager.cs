// SpectrumNet/Controllers/Interfaces/ViewCore/IRenderingManager.cs
#nullable enable

namespace SpectrumNet.SN.Controllers.View.Interfaces;

public interface IRenderingManager
{
    void RequestRender();
    void UpdateDimensions(int width, int height);
    void SynchronizeVisualization();
    void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs? e);
    Renderer? Renderer { get; set; }
}