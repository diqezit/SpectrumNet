#nullable enable

using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace SpectrumNet.Controllers.Input;

public class KeyboardInputHandler : IInputHandler
{
    private readonly IMainController _controller;
    private readonly Dictionary<Key, Action> _globalKeyActions;

    public KeyboardInputHandler(IMainController controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _globalKeyActions = CreateGlobalKeyActionsMap();
    }

    public bool HandleKeyDown(KeyEventArgs e, IInputElement? focusedElement)
    {
        if (ShouldIgnoreKeyPress(focusedElement))
            return false;

        if (e.Key == Space && !e.IsRepeat)
        {
            _ = _controller.ToggleCaptureAsync();
            e.Handled = true;
            return true;
        }

        if (TryExecuteGlobalKeyAction(e.Key))
        {
            e.Handled = true;
            return true;
        }

        return false;
    }

    private static bool ShouldIgnoreKeyPress(IInputElement? focusedElement) =>
        focusedElement is TextBox or PasswordBox or ComboBox;

    private bool TryExecuteGlobalKeyAction(Key key)
    {
        if (_globalKeyActions.TryGetValue(key, out var action))
        {
            ExecuteAction(action, $"global key action for {key}");
            return true;
        }

        return false;
    }

    private static void ExecuteAction(Action action, string context)
    {
        Safe(() => action(),
            new ErrorHandlingOptions
            {
                Source = nameof(KeyboardInputHandler),
                ErrorMessage = $"Error executing {context}"
            });
    }

    private Dictionary<Key, Action> CreateGlobalKeyActionsMap() => new()
    {
        { Key.O, () => ToggleOverlay() },
        { Key.P, () => _controller.ToggleControlPanel() },
        { Key.Q, () => _controller.RenderQuality = RenderQuality.Low },
        { Key.W, () => _controller.RenderQuality = RenderQuality.Medium },
        { Key.E, () => _controller.RenderQuality = RenderQuality.High },
        { Escape, () => HandleEscapeKey() }
    };

    private void HandleEscapeKey()
    {
        if (_controller.IsPopupOpen)
            _controller.IsPopupOpen = false;
        else if (_controller.IsOverlayActive)
            _controller.CloseOverlay();
    }

    private void ToggleOverlay()
    {
        if (_controller.IsOverlayActive)
            _controller.CloseOverlay();
        else
            _controller.OpenOverlay();
    }

    public bool HandleMouseDown(object? sender, MouseButtonEventArgs e) => false;
    public bool HandleMouseMove(object? sender, MouseEventArgs e) => false;
    public bool HandleMouseUp(object? sender, MouseButtonEventArgs e) => false;
    public bool HandleMouseDoubleClick(object? sender, MouseButtonEventArgs e) => false;
    public bool HandleWindowDrag(object? sender, MouseButtonEventArgs e) => false;
    public void HandleMouseEnter(object? sender, MouseEventArgs e) { }
    public void HandleMouseLeave(object? sender, MouseEventArgs e) { }
    public bool HandleButtonClick(object? sender, RoutedEventArgs e) => false;
}