#nullable enable

namespace SpectrumNet.SN.Visualization.Overlay.Interfaces;

public interface IOverlayEventHandler : IDisposable
{
    void RegisterEvents(Window window, SKElement renderElement);
    void UnregisterEvents(Window window, SKElement renderElement);
}
