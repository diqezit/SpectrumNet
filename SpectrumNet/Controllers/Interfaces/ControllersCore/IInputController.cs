#nullable enable 

using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace SpectrumNet.Controllers.Interfaces.ControllersCore;

public interface IInputController
{
    bool HandleKeyDown(KeyEventArgs e, IInputElement? focusedElement);
    bool HandleMouseDown(object? sender, MouseButtonEventArgs e);
    bool HandleMouseMove(object? sender, MouseEventArgs e);
    bool HandleMouseUp(object? sender, MouseButtonEventArgs e);
    bool HandleMouseDoubleClick(object? sender, MouseButtonEventArgs e);
    bool HandleWindowDrag(object? sender, MouseButtonEventArgs e);
    void HandleMouseEnter(object? sender, MouseEventArgs e);
    void HandleMouseLeave(object? sender, MouseEventArgs e);
    bool HandleButtonClick(object? sender, RoutedEventArgs e);
    void RegisterWindow(Window window);
    void UnregisterWindow(Window window);
}