// SN.Controllers/Interfaces/IControllerProvider.cs
#nullable enable

namespace SpectrumNet.SN.Controllers.Interfaces;

public interface IControllerProvider
{
    IUIController UIController { get; }
    IAudioController AudioController { get; }
    IViewController ViewController { get; }
    IInputController InputController { get; }
    IOverlayManager OverlayManager { get; }
    IBatchOperationsManager BatchOperationsManager { get; }
    IResourceCleanupManager ResourceCleanupManager { get; }
    IFpsLimiter FpsLimiter { get; }
    IMainController MainController { get; }
}