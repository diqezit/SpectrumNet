#nullable enable

namespace SpectrumNet.SN.Controllers.Input;

public class WindowInputHandler : IInputHandler
{
    private const string LogPrefix = nameof(WindowInputHandler);
    private readonly IMainController _controller;
    private readonly Dictionary<string, Action> _buttonActions;
    private readonly ISmartLogger _logger = Instance;

    public WindowInputHandler(IMainController controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _buttonActions = CreateButtonActionsMap();
    }

    public bool HandleWindowDrag(object? sender, MouseButtonEventArgs e) =>
        _logger.SafeResult(() => HandleWindowDragInternal(sender, e),
            false,
            LogPrefix,
            "Error handling window drag event");

    private static bool HandleWindowDragInternal(object? sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || sender is not DependencyObject obj)
            return false;

        if (FindParentBorder(obj, "TitleBar") is Border titleBar)
        {
            var mousePos = e.GetPosition(titleBar);

            if (titleBar.FindName("CloseButton") is Button closeButton)
            {
                var closeButtonBounds = closeButton.TransformToAncestor(titleBar)
                    .TransformBounds(new Rect(0, 0, closeButton.ActualWidth, closeButton.ActualHeight));

                if (!closeButtonBounds.Contains(mousePos))
                {
                    if (Application.Current?.MainWindow is Window window)
                    {
                        window.DragMove();
                        e.Handled = true;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    public bool HandleButtonClick(object? sender, RoutedEventArgs e) =>
        _logger.SafeResult(() => HandleButtonClickInternal(sender, e),
            false,
            LogPrefix,
            "Error handling button click event");

    private bool HandleButtonClickInternal(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Name: var btnName } button && _buttonActions.TryGetValue(btnName, out var action))
        {
            _logger.Safe(() => action(), LogPrefix, $"Error executing action for button {btnName}");
            e.Handled = true;
            return true;
        }

        return false;
    }

    private static Border? FindParentBorder(DependencyObject obj, string borderName)
    {
        while (obj != null)
        {
            if (obj is Border border && border.Name == borderName)
                return border;
            obj = GetParent(obj);
        }
        return null;
    }

    private Dictionary<string, Action> CreateButtonActionsMap() => new()
    {
        ["CloseButton"] = () =>
        {
            if (Application.Current?.MainWindow is Window window)
                window.Close();
        },
        ["MinimizeButton"] = () =>
        {
            if (Application.Current?.MainWindow is Window window)
                window.WindowState = WindowState.Minimized;
        },
        ["MaximizeButton"] = () =>
        {
            if (Application.Current?.MainWindow is Window window)
            {
                if (window.WindowState == WindowState.Maximized)
                    window.WindowState = WindowState.Normal;
                else
                    window.WindowState = WindowState.Maximized;
            }
        },
        ["OpenControlPanelButton"] = () => _controller.ToggleControlPanel(),
        ["ThemeToggleButton"] = () => _controller.ToggleTheme(),
        ["StartCaptureButton"] = () => _ = _controller.StartCaptureAsync(),
        ["StopCaptureButton"] = () => _ = _controller.StopCaptureAsync(),
        ["OverlayButton"] = () =>
        {
            if (_controller.IsOverlayActive)
                _controller.CloseOverlay();
            else
                _controller.OpenOverlay();
        },
        ["OpenSettingsButton"] = () => new SettingsWindow().ShowDialog(),
        ["OpenPopupButton"] = () => _controller.IsPopupOpen = true
    };

    public bool HandleKeyDown(KeyEventArgs e, IInputElement? focusedElement) => false;
    public bool HandleMouseDown(object? sender, MouseButtonEventArgs e) => false;
    public bool HandleMouseMove(object? sender, MouseEventArgs e) => false;
    public bool HandleMouseUp(object? sender, MouseButtonEventArgs e) => false;
    public bool HandleMouseDoubleClick(object? sender, MouseButtonEventArgs e) => false;
    public void HandleMouseEnter(object? sender, MouseEventArgs e) { }
    public void HandleMouseLeave(object? sender, MouseEventArgs e) { }
}