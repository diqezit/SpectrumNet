#nullable enable

using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace SpectrumNet.Controllers.Input;

public class InputController : IInputController, IDisposable
{
    private const string LogPrefix = "InputController";

    private readonly IMainController _mainController;
    private readonly List<IInputHandler> _handlers;
    private readonly List<Window> _registeredWindows = [];
    private bool _isDisposed;

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
        window.Closed += OnWindowClosed;
    }

    public void UnregisterWindow(Window window)
    {
        if (!_registeredWindows.Contains(window))
            return;

        window.Closed -= OnWindowClosed;
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

    private static bool ExecuteHandlerAction(List<IInputHandler> handlers, Func<IInputHandler, bool> action)
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

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is Window window)
            UnregisterWindow(window);
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        foreach (var window in _registeredWindows.ToList())
        {
            UnregisterWindow(window);
        }

        _registeredWindows.Clear();
        _isDisposed = true;
    }
}