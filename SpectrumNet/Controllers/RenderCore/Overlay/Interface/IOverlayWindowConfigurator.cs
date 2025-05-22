#nullable enable

namespace SpectrumNet.Controllers.RenderCore.Overlay.Interface;

public interface IOverlayWindowConfigurator
{
    void ConfigureWindow(Window window, OverlayConfiguration configuration);
    void ApplyTransparency(Window window);
    void ConfigureRendering(Window window);
}
