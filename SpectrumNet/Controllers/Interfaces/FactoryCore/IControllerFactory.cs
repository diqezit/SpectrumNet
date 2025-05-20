#nullable enable

using SpectrumNet.Controllers.Interfaces.ControllersCore;

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