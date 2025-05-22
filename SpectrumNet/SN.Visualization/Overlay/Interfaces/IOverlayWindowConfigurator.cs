#nullable enable

namespace SpectrumNet.SN.Visualization.Overlay.Interfaces;

public interface IOverlayWindowConfigurator
{
    void ConfigureWindow(Window window, OverlayConfiguration configuration);
    void ApplyTransparency(Window window);
    void ConfigureRendering(Window window);
}
