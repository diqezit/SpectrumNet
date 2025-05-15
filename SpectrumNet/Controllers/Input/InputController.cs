#nullable enable

using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace SpectrumNet.Controllers.Input;

public class InputController : IInputController, IDisposable, IAsyncDisposable
{
    private const string LogPrefix = "InputController";

    private readonly IMainController _mainController;
    private readonly List<IInputHandler> _handlers;
    private readonly List<Window> _registeredWindows = [];
    private readonly Dictionary<Window, List<EventSubscription>> _eventSubscriptions = [];
    private bool _isDisposed;

    private record EventSubscription(string EventName, Delegate Handler);

    public InputController(IMainController mainController)
    {
        _mainController = mainController ?? throw new ArgumentNullException(nameof(mainController));
        _handlers = CreateHandlers();
    }

    public void RegisterWindow(Window window)
    {
        if (_registeredWindows.Contains(window) || _isDisposed)
            return;

        _registeredWindows.Add(window);

        var closedHandler = new EventHandler(OnWindowClosed);
        window.Closed += closedHandler;

        RegisterEventSubscription(window, "Closed", closedHandler);
    }

    public void UnregisterWindow(Window window)
    {
        if (!_registeredWindows.Contains(window))
            return;

        UnsubscribeAllEvents(window);
        _registeredWindows.Remove(window);
    }

    public bool HandleKeyDown(KeyEventArgs e, IInputElement? focusedElement) =>
        ExecuteHandlerAction(_handlers, h => h.HandleKeyDown(e, focusedElement));

    public bool HandleMouseDown(object? sender, MouseButtonEventArgs e) =>
        ExecuteHandlerAction(_handlers, h => h.HandleMouseDown(sender, e));

    public bool HandleMouseMove(object? sender, MouseEventArgs e) =>
        ExecuteHandlerAction(_handlers, h => h.HandleMouseMove(sender, e));

    public bool HandleMouseUp(object? sender, MouseButtonEventArgs e) =>
        ExecuteHandlerAction(_handlers, h => h.HandleMouseUp(sender, e));

    public bool HandleMouseDoubleClick(object? sender, MouseButtonEventArgs e) =>
        ExecuteHandlerAction(_handlers, h => h.HandleMouseDoubleClick(sender, e));

    public bool HandleWindowDrag(object? sender, MouseButtonEventArgs e) =>
        ExecuteHandlerAction(_handlers, h => h.HandleWindowDrag(sender, e));

    public void HandleMouseEnter(object? sender, MouseEventArgs e)
    {
        foreach (var handler in _handlers)
            handler.HandleMouseEnter(sender, e);
    }

    public void HandleMouseLeave(object? sender, MouseEventArgs e)
    {
        foreach (var handler in _handlers)
            handler.HandleMouseLeave(sender, e);
    }

    public bool HandleButtonClick(object? sender, RoutedEventArgs e) =>
        ExecuteHandlerAction(_handlers, h => h.HandleButtonClick(sender, e));

    public void Dispose()
    {
        if (_isDisposed)
            return;

        UnregisterAllWindows();
        _handlers.Clear();
        _isDisposed = true;
    }

    private void RegisterEventSubscription(
        Window window,
        string eventName,
        Delegate handler)
    {
        if (!_eventSubscriptions.TryGetValue(window, out var subscriptions))
        {
            subscriptions = [];
            _eventSubscriptions[window] = subscriptions;
        }

        subscriptions.Add(new EventSubscription(eventName, handler));
    }

    private void UnsubscribeAllEvents(Window window)
    {
        if (_eventSubscriptions.TryGetValue(window, out var subscriptions))
        {
            foreach (var subscription in subscriptions)
            {
                UnsubscribeWindowEvent(window, subscription);
            }

            _eventSubscriptions.Remove(window);
        }
    }

    private static void UnsubscribeWindowEvent(
        Window window,
        EventSubscription subscription)
    {
        try
        {
            switch (subscription.EventName)
            {
                case "Closed":
                    window.Closed -= (EventHandler)subscription.Handler;
                    break;
            }
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, LogPrefix,
                $"Error unsubscribing from {subscription.EventName}: {ex.Message}");
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is Window window)
            UnregisterWindow(window);
    }

    private static bool ExecuteHandlerAction(
        List<IInputHandler> handlers,
        Func<IInputHandler, bool> action)
    {
        foreach (var handler in handlers)
        {
            if (action(handler))
                return true;
        }
        return false;
    }

    private List<IInputHandler> CreateHandlers() =>
    [
        new KeyboardInputHandler(_mainController),
        new MouseInputHandler(_mainController),
        new WindowInputHandler(_mainController)
    ];

    private void UnregisterAllWindows()
    {
        foreach (var window in _registeredWindows.ToList())
        {
            UnregisterWindow(window);
        }

        _registeredWindows.Clear();
        _eventSubscriptions.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;

        UnregisterAllWindows();
        _handlers.Clear();
        _isDisposed = true;

        await ValueTask.CompletedTask;
    }
}