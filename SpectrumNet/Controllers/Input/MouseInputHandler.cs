#nullable enable

namespace SpectrumNet.Controllers.Input;

public class MouseInputHandler : IInputHandler
{
    private readonly IMainController _controller;
    private bool _isTrackingMouse;

    public MouseInputHandler(IMainController controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
    }

    public bool HandleMouseDown(object? sender, MouseButtonEventArgs e)
    {
        if (sender is not UIElement element || e.LeftButton != MouseButtonState.Pressed)
            return false;

        var point = e.GetPosition(element);
        var skPoint = new SKPoint((float)point.X, (float)point.Y);

        if (_controller.Renderer?.ShouldShowPlaceholder == true)
        {
            var placeholder = _controller.Renderer.GetPlaceholder();
            if (placeholder?.HitTest(skPoint) == true)
            {
                _controller.Renderer.HandlePlaceholderMouseDown(skPoint);
                element.CaptureMouse();
                _isTrackingMouse = true;
                e.Handled = true;
                _controller.SpectrumCanvas.InvalidateVisual();
                return true;
            }
        }

        return false;
    }

    public bool HandleMouseMove(object? sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not UIElement element)
            return false;

        var point = e.GetPosition(element);
        var skPoint = new SKPoint((float)point.X, (float)point.Y);

        if (_controller.Renderer?.ShouldShowPlaceholder == true)
        {
            _controller.Renderer.HandlePlaceholderMouseMove(skPoint);
            if (_isTrackingMouse)
            {
                _controller.SpectrumCanvas.InvalidateVisual();
                return true;
            }
        }

        return false;
    }

    public bool HandleMouseUp(object? sender, MouseButtonEventArgs e)
    {
        if (sender is not UIElement element || e.LeftButton != MouseButtonState.Released || !_isTrackingMouse)
            return false;

        var point = e.GetPosition(element);
        var skPoint = new SKPoint((float)point.X, (float)point.Y);

        if (_controller.Renderer?.ShouldShowPlaceholder == true)
        {
            _controller.Renderer.HandlePlaceholderMouseUp(skPoint);
        }

        element.ReleaseMouseCapture();
        _isTrackingMouse = false;
        _controller.SpectrumCanvas.InvalidateVisual();
        e.Handled = true;
        return true;
    }

    public bool HandleMouseDoubleClick(object? sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || sender is not FrameworkElement)
            return false;

        if (IsControlOfType<CheckBox>(e.OriginalSource as DependencyObject))
        {
            e.Handled = true;
            return true;
        }

        if (_controller.Renderer?.ShouldShowPlaceholder == true)
        {
            var position = e.GetPosition(_controller.SpectrumCanvas);
            var skPoint = new SKPoint((float)position.X, (float)position.Y);
            var placeholder = _controller.Renderer.GetPlaceholder();

            if (placeholder?.HitTest(skPoint) == true)
            {
                e.Handled = true;
                return true;
            }
        }

        e.Handled = true;
        ToggleWindowState();
        return true;
    }

    public void HandleMouseEnter(object? sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_controller.Renderer?.ShouldShowPlaceholder == true)
        {
            _controller.Renderer.HandlePlaceholderMouseEnter();
        }
    }

    public void HandleMouseLeave(object? sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_controller.Renderer?.ShouldShowPlaceholder == true)
        {
            _controller.Renderer.HandlePlaceholderMouseLeave();
        }

        if (_isTrackingMouse && sender is UIElement element)
        {
            var point = e.GetPosition(element);
            var skPoint = new SKPoint((float)point.X, (float)point.Y);
            _controller.Renderer?.HandlePlaceholderMouseUp(skPoint);

            element.ReleaseMouseCapture();
            _isTrackingMouse = false;
        }
    }

    private static bool IsControlOfType<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element is not null)
        {
            if (element is T) return true;
            element = GetParent(element);
        }
        return false;
    }

    private void ToggleWindowState()
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