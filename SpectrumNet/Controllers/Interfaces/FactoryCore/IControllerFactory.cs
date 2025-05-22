#nullable enable

using SpectrumNet.Controllers.RenderCore.Overlay.Interface;

namespace SpectrumNet.Controllers.Interfaces.FactoryCore;

public interface IControllerFactory
{
    IUIController UIController { get; }
    IAudioController AudioController { get; }
    IViewController ViewController { get; }
    IInputController InputController { get; }
    IOverlayManager OverlayManager { get; }

    void Initialize();
    void DisposeResources();
}