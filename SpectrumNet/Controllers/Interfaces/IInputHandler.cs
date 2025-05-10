#nullable enable

using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace SpectrumNet.Controllers.Interfaces;

public interface IInputHandler
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
}