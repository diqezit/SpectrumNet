// IInputController.cs
#nullable enable 

namespace SpectrumNet.Controllers.Interfaces;

public interface IInputController
{
    bool HandleKeyDown(KeyEventArgs e, IInputElement? focusedElement);
    void RegisterWindow(Window window);
    void UnregisterWindow(Window window);
}