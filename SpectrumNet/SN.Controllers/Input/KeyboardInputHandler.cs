#nullable enable

namespace SpectrumNet.SN.Controllers.Input;

public class KeyboardInputHandler : IInputHandler
{
    private const string LogPrefix = nameof(KeyboardInputHandler);
    private readonly IMainController _controller;
    private readonly Dictionary<Key, Action> _globalKeyActions;
    private readonly ISmartLogger _logger = Instance;

    public KeyboardInputHandler(IMainController controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _globalKeyActions = CreateGlobalKeyActionsMap();
    }

    public bool HandleKeyDown(KeyEventArgs e, IInputElement? focusedElement)
    {
        if (e.Key == Key.Space
            && !e.IsRepeat
            && !IsTextInputElement(focusedElement))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _controller.ToggleCaptureAsync();
                }
                catch (Exception ex)
                {
                    _logger.Error(LogPrefix, "Error toggling capture", ex);
                }
            });

            e.Handled = true;
            return true;
        }

        if (!e.IsRepeat && !ShouldIgnoreKeyPress(focusedElement))
        {
            if (TryExecuteGlobalKeyAction(e.Key))
            {
                e.Handled = true;
                return true;
            }
        }

        return false;
    }

    private static bool IsTextInputElement(IInputElement? focusedElement) =>
        focusedElement is TextBox or PasswordBox;

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

    private void ExecuteAction(Action action, string context) =>
        _logger.Safe(() => action(), LogPrefix, $"Error executing {context}");

    private Dictionary<Key, Action> CreateGlobalKeyActionsMap() => new()
    {
        { Key.O, () => ToggleOverlay() },
        { Key.P, () => _controller.ToggleControlPanel() },
        { Key.Q, () => _controller.RenderQuality = RenderQuality.Low },
        { Key.W, () => _controller.RenderQuality = RenderQuality.Medium },
        { Key.E, () => _controller.RenderQuality = RenderQuality.High },
        { Key.Z, () => _controller.SelectPreviousRenderer() },
        { Key.X, () => _controller.SelectNextRenderer() },
        { Key.Escape, () => HandleEscapeKey() }
    };

    private void HandleEscapeKey()
    {
        if (_controller.IsPopupOpen)
            _controller.IsPopupOpen = false;
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