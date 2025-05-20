#nullable enable

namespace SpectrumNet.Controllers.UICore;

public sealed class ResourceCleanupManager : IResourceCleanupManager
{
    private const string LogPrefix = nameof(ResourceCleanupManager);
    private readonly ISmartLogger _logger = Instance;

    public void SafeExecuteAction(Action action, string actionName) =>
        _logger.Safe(() => action(),
            LogPrefix,
            $"Error during {actionName}");

    public async Task DetachEventHandlersAsync(Window? ownerWindow, KeyEventHandler keyDownHandler,
        CancellationToken token)
    {
        if (ownerWindow == null) return;

        await _logger.SafeAsync(async () =>
        {
            await ownerWindow.Dispatcher.InvokeAsync(() =>
            {
                ownerWindow.KeyDown -= keyDownHandler;
            }).Task.WaitAsync(token);
        },
        LogPrefix,
        "Error detaching event handlers");
    }

    public async Task StopCaptureAsync(IAudioController audioController, bool isRecording, CancellationToken token) =>
        await _logger.SafeAsync(async () =>
        {
            if (isRecording)
            {
                try
                {
                    await audioController.StopCaptureAsync().WaitAsync(TimeSpan.FromSeconds(3), token);
                }
                catch (OperationCanceledException)
                {
                    _logger.Log(LogLevel.Warning, LogPrefix, "Stop capture operation cancelled or timed out");
                }
            }
        },
        LogPrefix,
        "Error stopping capture");

    public async Task CloseUIElementsAsync(IUIController uiController, IOverlayManager overlayManager,
        Dispatcher dispatcher, CancellationToken token) =>
        await _logger.SafeAsync(async () =>
        {
            await dispatcher.InvokeAsync(() =>
            {
                if (overlayManager.IsActive)
                    _ = overlayManager.CloseAsync();

                if (uiController.IsControlPanelOpen)
                    uiController.CloseControlPanel();
            }).Task.WaitAsync(token);
        },
        LogPrefix,
        "Error closing UI elements");

    public async Task DisposeControllerAsync<T>(T controller, string controllerName, CancellationToken token)
        where T : class =>
        await _logger.SafeAsync(async () =>
        {
            if (controller is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3), token);
            }
        },
        LogPrefix,
        $"Error disposing {controllerName}");

    public void DisposeController<T>(T controller, string controllerName) where T : class =>
        _logger.Safe(() =>
        {
            if (controller is IDisposable disposable)
                disposable.Dispose();
        },
        LogPrefix,
        $"Error disposing {controllerName}");
}