#nullable enable

namespace SpectrumNet.Controllers.RenderCore.Overlay.Interface;

public interface IOverlayEventHandler : IDisposable
{
    void RegisterEvents(Window window, SKElement renderElement);
    void UnregisterEvents(Window window, SKElement renderElement);
}
