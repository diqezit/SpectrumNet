#nullable enable

namespace SpectrumNet.SN.Controllers.Input;

public class KeyboardInputHandler : IInputHandler
{
    private readonly IMainController _controller;
    private readonly IKeyBindingManager _keyBindingManager;
    private readonly Dictionary<string, Action> _actionHandlers;

    private const int 
        BAR_COUNT_STEP = 5,
        MIN_BAR_COUNT = 10,
        MAX_BAR_COUNT = 200;

    private const double
        BAR_SPACING_STEP = 0.5,
        MIN_BAR_SPACING = 0.0,
        MAX_BAR_SPACING = 20.0;

    public KeyboardInputHandler(IMainController controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _keyBindingManager = new KeyBindingManager(Settings.Settings.Instance);
        _actionHandlers = CreateActionHandlers();
    }

    public bool HandleKeyDown(KeyEventArgs e, IInputElement? focusedElement)
    {
        if (IsToggleRecordingKey(e, focusedElement))
        {
            ToggleRecordingAsync();
            return e.Handled = true;
        }

        if (ShouldProcessKey(e, focusedElement))
        {
            var action = _keyBindingManager.GetActionForKey(e.Key);
            if (action != null && _actionHandlers.TryGetValue(action, out var handler))
            {
                handler();
                return e.Handled = true;
            }
        }
        return false;
    }

    private Dictionary<string, Action> CreateActionHandlers() => new()
    {
        ["ToggleOverlay"] = ToggleOverlay,
        ["ToggleControlPanel"] = () => _controller.ToggleControlPanel(),
        ["QualityLow"] = () => _controller.RenderQuality = RenderQuality.Low,
        ["QualityMedium"] = () => _controller.RenderQuality = RenderQuality.Medium,
        ["QualityHigh"] = () => _controller.RenderQuality = RenderQuality.High,
        ["PreviousRenderer"] = () => _controller.SelectPreviousRenderer(),
        ["NextRenderer"] = () => _controller.SelectNextRenderer(),
        ["ClosePopup"] = ClosePopup,
        ["DecreaseBarCount"] = DecreaseBarCount,
        ["IncreaseBarCount"] = IncreaseBarCount,
        ["IncreaseBarSpacing"] = IncreaseBarSpacing,
        ["DecreaseBarSpacing"] = DecreaseBarSpacing
    };

    private void ToggleOverlay()
    {
        if (_controller.IsOverlayActive)
            _controller.CloseOverlay();
        else
            _controller.OpenOverlay();
    }

    private void ClosePopup()
    {
        if (_controller.IsPopupOpen)
            _controller.IsPopupOpen = false;
    }

    private void IncreaseBarCount() =>
        _controller.BarCount = Clamp(
            _controller.BarCount + BAR_COUNT_STEP,
            MIN_BAR_COUNT,
            MAX_BAR_COUNT);

    private void DecreaseBarCount() =>
        _controller.BarCount = Clamp(
            _controller.BarCount - BAR_COUNT_STEP,
            MIN_BAR_COUNT,
            MAX_BAR_COUNT);

    private void IncreaseBarSpacing() =>
        _controller.BarSpacing = Clamp(
            _controller.BarSpacing + BAR_SPACING_STEP,
            MIN_BAR_SPACING,
            MAX_BAR_SPACING);

    private void DecreaseBarSpacing() =>
        _controller.BarSpacing = Clamp(
            _controller.BarSpacing - BAR_SPACING_STEP,
            MIN_BAR_SPACING,
            MAX_BAR_SPACING);

    private bool IsToggleRecordingKey(KeyEventArgs e, IInputElement? focusedElement) =>
        e.Key == _keyBindingManager.GetKeyForAction("ToggleRecording")
        && !e.IsRepeat
        && !IsTextInput(focusedElement);

    private static bool ShouldProcessKey(KeyEventArgs e, IInputElement? focusedElement) =>
        !e.IsRepeat && !ShouldIgnore(focusedElement);

    private void ToggleRecordingAsync() =>
        Task.Run(async () => await _controller.ToggleCaptureAsync());

    private static bool IsTextInput(IInputElement? el) =>
        el is TextBox or PasswordBox;

    private static bool ShouldIgnore(IInputElement? el) =>
        el is TextBox or PasswordBox or ComboBox;

    public bool HandleMouseDown(object? sender, MouseButtonEventArgs e) => false;
    public bool HandleMouseMove(object? sender, MouseEventArgs e) => false;
    public bool HandleMouseUp(object? sender, MouseButtonEventArgs e) => false;
    public bool HandleMouseDoubleClick(object? sender, MouseButtonEventArgs e) => false;
    public bool HandleWindowDrag(object? sender, MouseButtonEventArgs e) => false;
    public void HandleMouseEnter(object? sender, MouseEventArgs e) { }
    public void HandleMouseLeave(object? sender, MouseEventArgs e) { }
    public bool HandleButtonClick(object? sender, RoutedEventArgs e) => false;
}