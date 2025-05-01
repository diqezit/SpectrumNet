#nullable enable

namespace SpectrumNet.Controllers.Input;

/// <summary>
/// Обрабатывает события клавиатуры,
/// управляя глобальными привязками клавиш
/// и привязками с модификаторами (Ctrl).
/// Диспетчизирует действия для
/// <see cref="IAudioVisualizationController"/>.
/// </summary>
public class KeyboardManager
{
    private readonly IAudioVisualizationController _controller;
    private readonly Dictionary<Key, Action> _globalKeyActions;
    private readonly Dictionary<(Key Key, ModifierKeys Modifiers), Action> _modifiedKeyActions;

    public KeyboardManager(IAudioVisualizationController controller)
    {
        _controller = controller ?? 
            throw new ArgumentNullException(nameof(controller));

        _globalKeyActions = new Dictionary<Key, Action>
        {
            { Space, () => _ = _controller.ToggleCaptureAsync() },
            { F10, () => _controller.RenderQuality = RenderQuality.Low },
            { F11, () => _controller.RenderQuality = RenderQuality.Medium },
            { F12, () => _controller.RenderQuality = RenderQuality.High },
            { Escape, () =>
                {
                    if (_controller.IsPopupOpen) _controller.IsPopupOpen = false;
                    else if (_controller.IsOverlayActive) _controller.CloseOverlay();
                }
            }
        };

        _modifiedKeyActions = new Dictionary<(Key, ModifierKeys), Action>
        {
            { (O, ModifierKeys.Control), () =>
                {
                    if (_controller.IsOverlayActive) _controller.CloseOverlay();
                    else _controller.OpenOverlay();
                }
            },
            { (P, ModifierKeys.Control), () => _controller.ToggleControlPanel() }
        };
    }

    public bool HandleKeyDown(KeyEventArgs e, IInputElement? focusedElement)
    {
        if (focusedElement is TextBox or PasswordBox or ComboBox)
            return false;

        if (Keyboard.Modifiers != ModifierKeys.None &&
            _modifiedKeyActions.TryGetValue(
            (e.Key, Keyboard.Modifiers),
            out var modAction))
        {
            modAction();
            return true;
        }

        if (_globalKeyActions.TryGetValue(
            e.Key,
            out var action))
        {
            action();
            return true;
        }

        return false;
    }
}
