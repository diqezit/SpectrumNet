#nullable enable

using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace SpectrumNet.Controllers.Input;

public class MouseInputHandler : IInputHandler
{
    private const string LogPrefix = nameof(MouseInputHandler);
    private readonly IMainController _controller;
    private readonly RendererTransparencyManager _transparencyManager = RendererTransparencyManager.Instance;
    private readonly ISmartLogger _logger = Instance;
    private bool _isTrackingMouse;

    public MouseInputHandler(IMainController controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
    }

    public bool HandleMouseDown(object? sender, MouseButtonEventArgs e) =>
        _logger.SafeResult(() => HandleMouseDownInternal(sender, e),
            false,
            LogPrefix,
            "Error handling mouse down event");

    private bool HandleMouseDownInternal(object? sender, MouseButtonEventArgs e)
    {
        if (!TryGetUiElementAndSkPoint(sender, e, out var element, out var skPoint)
            || e.LeftButton != MouseButtonState.Pressed)
            return false;

        if (TryHandlePlaceholderMouseDown(element, skPoint, e))
            return true;

        return false;
    }

    public bool HandleMouseMove(object? sender, MouseEventArgs e) =>
        _logger.SafeResult(() => HandleMouseMoveInternal(sender, e),
            false,
            LogPrefix,
            "Error handling mouse move event");

    private bool HandleMouseMoveInternal(object? sender, MouseEventArgs e)
    {
        _transparencyManager.OnMouseMove();

        if (!TryGetUiElementAndSkPoint(sender, e, out _, out var skPoint))
            return false;

        HandlePlaceholderMouseMoveIfRelevant(skPoint);

        if (_isTrackingMouse)
        {
            _controller.SpectrumCanvas?.InvalidateVisual();
            return true;
        }

        return false;
    }

    public bool HandleMouseUp(object? sender, MouseButtonEventArgs e) =>
        _logger.SafeResult(() => HandleMouseUpInternal(sender, e),
            false,
            LogPrefix,
            "Error handling mouse up event");

    private bool HandleMouseUpInternal(object? sender, MouseButtonEventArgs e)
    {
        if (!TryGetUiElementAndSkPoint(sender, e, out var element, out var skPoint)
            || e.LeftButton != MouseButtonState.Released
            || !_isTrackingMouse)
            return false;

        HandlePlaceholderMouseUpIfRelevant(skPoint);

        ReleaseMouseCaptureAndTracking(element);

        _controller.SpectrumCanvas?.InvalidateVisual();

        e.Handled = true;
        return true;
    }

    public bool HandleMouseDoubleClick(object? sender, MouseButtonEventArgs e) =>
        _logger.SafeResult(() => HandleMouseDoubleClickInternal(sender, e),
            false,
            LogPrefix,
            "Error handling mouse double click event");

    private bool HandleMouseDoubleClickInternal(object? sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left
            || sender is not FrameworkElement)
            return false;

        if (TryHandleCheckBoxDoubleClick(e.OriginalSource as DependencyObject, e))
        {
            return true;
        }

        if (TryHandlePlaceholderDoubleClick(e))
        {
            return true;
        }

        e.Handled = true;
        ToggleWindowState();
        return true;
    }

    public void HandleMouseEnter(object? sender, MouseEventArgs e) =>
        _logger.Safe(() => HandleMouseEnterInternal(sender, e),
            LogPrefix,
            "Error handling mouse enter event");

    private void HandleMouseEnterInternal(object? sender, MouseEventArgs e)
    {
        _transparencyManager.OnMouseEnter();

        HandlePlaceholderMouseEnterIfRelevant();
    }

    public void HandleMouseLeave(object? sender, MouseEventArgs e) =>
        _logger.Safe(() => HandleMouseLeaveInternal(sender, e),
            LogPrefix,
            "Error handling mouse leave event");

    private void HandleMouseLeaveInternal(object? sender, MouseEventArgs e)
    {
        _transparencyManager.OnMouseLeave();

        HandlePlaceholderMouseLeaveIfRelevant();

        if (_isTrackingMouse && sender is UIElement element)
        {
            var point = e.GetPosition(element);
            var skPoint = new SKPoint((float)point.X, (float)point.Y);

            _controller.Renderer?.HandlePlaceholderMouseUp(skPoint);

            ReleaseMouseCaptureAndTracking(element);
        }
    }

    private static bool TryGetUiElementAndSkPoint(
        object? sender,
        MouseEventArgs e,
        [NotNullWhen(true)] out UIElement? element,
        out SKPoint skPoint)
    {
        element = sender as UIElement;
        if (element is null)
        {
            skPoint = default;
            return false;
        }

        var point = e.GetPosition(element);
        skPoint = new SKPoint((float)point.X, (float)point.Y);
        return true;
    }

    private bool TryHandlePlaceholderMouseDown(UIElement element, SKPoint skPoint, MouseButtonEventArgs e)
    {
        if (_controller.Renderer?.ShouldShowPlaceholder != true)
            return false;

        var placeholder = _controller.Renderer.GetPlaceholder();
        if (placeholder?.HitTest(skPoint) == true)
        {
            _controller.Renderer.HandlePlaceholderMouseDown(skPoint);
            element.CaptureMouse();
            _isTrackingMouse = true;
            e.Handled = true;
            _controller.SpectrumCanvas?.InvalidateVisual();
            return true;
        }
        return false;
    }

    private void HandlePlaceholderMouseMoveIfRelevant(SKPoint skPoint)
    {
        if (_controller.Renderer?.ShouldShowPlaceholder == true)
        {
            _controller.Renderer.HandlePlaceholderMouseMove(skPoint);
        }
    }

    private void HandlePlaceholderMouseUpIfRelevant(SKPoint skPoint)
    {
        if (_controller.Renderer?.ShouldShowPlaceholder == true)
        {
            _controller.Renderer.HandlePlaceholderMouseUp(skPoint);
        }
    }

    private void HandlePlaceholderMouseEnterIfRelevant()
    {
        if (_controller.Renderer?.ShouldShowPlaceholder == true)
        {
            _controller.Renderer.HandlePlaceholderMouseEnter();
        }
    }

    private void HandlePlaceholderMouseLeaveIfRelevant()
    {
        if (_controller.Renderer?.ShouldShowPlaceholder == true)
        {
            _controller.Renderer.HandlePlaceholderMouseLeave();
        }
    }

    private static bool TryHandleCheckBoxDoubleClick(DependencyObject? originalSource, MouseButtonEventArgs e)
    {
        if (IsControlOfType<CheckBox>(originalSource))
        {
            e.Handled = true;
            return true;
        }
        return false;
    }

    private bool TryHandlePlaceholderDoubleClick(MouseButtonEventArgs e)
    {
        if (_controller.Renderer?.ShouldShowPlaceholder != true || _controller.SpectrumCanvas is null)
            return false;

        var position = e.GetPosition(_controller.SpectrumCanvas);
        var skPoint = new SKPoint((float)position.X, (float)position.Y);
        var placeholder = _controller.Renderer.GetPlaceholder();

        if (placeholder?.HitTest(skPoint) == true)
        {
            e.Handled = true;
            return true;
        }
        return false;
    }

    private void ReleaseMouseCaptureAndTracking(UIElement element)
    {
        if (Mouse.Captured == element)
        {
            element.ReleaseMouseCapture();
        }
        _isTrackingMouse = false;
    }

    private static bool IsControlOfType<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element is not null)
        {
            if (element is T) return true;
            element = VisualTreeHelper.GetParent(element);
        }
        return false;
    }

    private static void ToggleWindowState()
    {
        if (Application.Current?.MainWindow is Window window)
        {
            if (window.WindowState == WindowState.Maximized)
                window.WindowState = WindowState.Normal;
            else
                window.WindowState = WindowState.Maximized;
        }
    }

    public bool HandleKeyDown(KeyEventArgs e, IInputElement? focusedElement) => false;
    public bool HandleWindowDrag(object? sender, MouseButtonEventArgs e) => false;
    public bool HandleButtonClick(object? sender, RoutedEventArgs e) => false;
}