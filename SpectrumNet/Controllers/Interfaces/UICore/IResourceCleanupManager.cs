#nullable enable

namespace SpectrumNet.Controllers.Interfaces.UICore;

public interface IResourceCleanupManager
{
    void SafeExecuteAction(Action action, string actionName);

    Task DetachEventHandlersAsync(Window? ownerWindow, KeyEventHandler keyDownHandler,
        CancellationToken token);

    Task StopCaptureAsync(IAudioController audioController, bool isRecording, CancellationToken token);

    Task CloseUIElementsAsync(IUIController uiController, IOverlayManager overlayManager,
        Dispatcher dispatcher, CancellationToken token);

    Task DisposeControllerAsync<T>(T controller, string controllerName, CancellationToken token)
        where T : class;

    void DisposeController<T>(T controller, string controllerName) where T : class;
}