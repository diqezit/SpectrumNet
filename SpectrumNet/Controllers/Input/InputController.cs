#nullable enable

namespace SpectrumNet.Controllers.Input;

public class InputController : IInputController
{
    private const string LogPrefix = "InputController";

    private readonly IMainController _mainController;
    private readonly Dictionary<Key, Action> _globalKeyActions;
    private readonly Dictionary<(Key Key, ModifierKeys Modifiers), Action> _modifiedKeyActions;

    public InputController(IMainController mainController)
    {
        _mainController = mainController ?? 
            throw new ArgumentNullException(nameof(mainController));

        _globalKeyActions = new Dictionary<Key, Action>
        {
            { Space, () => _ = _mainController.ToggleCaptureAsync() },
            { F10, () => _mainController.RenderQuality = RenderQuality.Low },
            { F11, () => _mainController.RenderQuality = RenderQuality.Medium },
            { F12, () => _mainController.RenderQuality = RenderQuality.High },
            { Escape, () =>
                {
                    if (_mainController.IsPopupOpen)
                        _mainController.IsPopupOpen = false;
                    else if (_mainController.IsOverlayActive)
                        _mainController.CloseOverlay();
                }
            }
        };

        _modifiedKeyActions = new Dictionary<(Key, ModifierKeys), Action>
        {
            { (O, ModifierKeys.Control), () =>
                {
                    if (_mainController.IsOverlayActive)
                        _mainController.CloseOverlay();
                    else
                        _mainController.OpenOverlay();
                }
            },
            { (P, ModifierKeys.Control), () => _mainController.ToggleControlPanel() }
        };
    }

    public bool HandleKeyDown(KeyEventArgs e, IInputElement? focusedElement)
    {
        if (focusedElement is TextBox or PasswordBox or ComboBox)
            return false;

        if (Keyboard.Modifiers != ModifierKeys.None &&
            _modifiedKeyActions.TryGetValue((e.Key, Keyboard.Modifiers), out var modAction))
        {
            Safe(() => modAction(),
                new ErrorHandlingOptions
                {
                    Source = LogPrefix,
                    ErrorMessage = $"Error executing modified key action for {e.Key}"
                });
            return true;
        }

        if (_globalKeyActions.TryGetValue(e.Key, out var action))
        {
            Safe(() => action(),
                new ErrorHandlingOptions
                {
                    Source = LogPrefix,
                    ErrorMessage = $"Error executing global key action for {e.Key}"
                });
            return true;
        }

        return false;
    }
}