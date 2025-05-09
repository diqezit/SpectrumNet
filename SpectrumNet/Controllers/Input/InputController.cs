#nullable enable

namespace SpectrumNet.Controllers.Input;

public class InputController : IInputController, IDisposable
{
    private const string LogPrefix = "InputController";

    private readonly IMainController _mainController;
    private readonly Dictionary<Key, Action> _globalKeyActions;
    private readonly Dictionary<(Key Key, ModifierKeys Modifiers), Action> _modifiedKeyActions;
    private readonly List<Window> _registeredWindows = [];
    private bool _isDisposed;

    public InputController(IMainController mainController)
    {
        _mainController = mainController ??
            throw new ArgumentNullException(nameof(mainController));

        _globalKeyActions = CreateGlobalKeyActionsMap();
        _modifiedKeyActions = CreateModifiedKeyActionsMap();
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

    public bool HandleKeyDown(KeyEventArgs e, IInputElement? focusedElement)
    {
        if (ShouldIgnoreKeyPressBasedOnFocus(focusedElement))
            return false;

        return ProcessKeyAction(e.Key, e);
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is Window window)
            UnregisterWindow(window);
    }

    private bool ProcessKeyAction(Key key, KeyEventArgs? e)
    {
        if (ShouldIgnoreKeyPressBasedOnFocus(Keyboard.FocusedElement))
            return false;

        if (TryExecuteModifiedKeyAction(key, e))
            return true;

        if (TryExecuteGlobalKeyAction(key, e))
            return true;

        return false;
    }

    private bool TryExecuteModifiedKeyAction(Key key, KeyEventArgs? e)
    {
        if (Keyboard.Modifiers == ModifierKeys.None)
            return false;

        if (_modifiedKeyActions.TryGetValue((key, Keyboard.Modifiers), out var action))
        {
            ExecuteAction(action, $"modified key action for {key}");
            if (e != null) e.Handled = true;
            return true;
        }

        return false;
    }

    private bool TryExecuteGlobalKeyAction(Key key, KeyEventArgs? e)
    {
        if (_globalKeyActions.TryGetValue(key, out var action))
        {
            ExecuteAction(action, $"global key action for {key}");
            if (e != null) e.Handled = true;
            return true;
        }

        return false;
    }

    private static bool ShouldIgnoreKeyPressBasedOnFocus(IInputElement? focusedElement)
    {
        return focusedElement is TextBox
            || focusedElement is PasswordBox
            || focusedElement is ComboBox;
    }

    private static void ExecuteAction(Action action, string context)
    {
        Safe(() => action(),
            new ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = $"Error executing {context}"
            });
    }

    private Dictionary<Key, Action> CreateGlobalKeyActionsMap() => new()
    {
        { Space, () => _ = _mainController.ToggleCaptureAsync() },
        { Key.Q, () => _mainController.RenderQuality = RenderQuality.Low },
        { Key.W, () => _mainController.RenderQuality = RenderQuality.Medium },
        { Key.E, () => _mainController.RenderQuality = RenderQuality.High },
        { Escape, () => HandleEscapeKey() }
    };

    private void HandleEscapeKey()
    {
        if (_mainController.IsPopupOpen)
            _mainController.IsPopupOpen = false;
        else if (_mainController.IsOverlayActive)
            _mainController.CloseOverlay();
    }

    private Dictionary<(Key, ModifierKeys), Action> CreateModifiedKeyActionsMap() => new()
    {
        { (O, ModifierKeys.Control), () => ToggleOverlay() },
        { (P, ModifierKeys.Control), () => _mainController.ToggleControlPanel() }
    };

    private void ToggleOverlay()
    {
        if (_mainController.IsOverlayActive)
            _mainController.CloseOverlay();
        else
            _mainController.OpenOverlay();
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