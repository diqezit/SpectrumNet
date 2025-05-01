// IInputController.cs
namespace SpectrumNet.Controllers.Interfaces;

public interface IInputController
{
    bool HandleKeyDown(KeyEventArgs e, IInputElement? focusedElement);
}