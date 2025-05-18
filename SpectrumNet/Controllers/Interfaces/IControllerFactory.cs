#nullable enable

namespace SpectrumNet.Controllers.Interfaces;

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