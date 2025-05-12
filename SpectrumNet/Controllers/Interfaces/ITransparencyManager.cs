#nullable enable

namespace SpectrumNet.Controllers.Interfaces;

public interface ITransparencyManager
{
    float CurrentTransparency { get; }
    bool IsActive { get; }
    event Action<float> TransparencyChanged;
    void OnMouseEnter();
    void OnMouseLeave();
    void OnMouseMove();
    void ActivateTransparency();
    void SetRendererFactory(IRendererFactory factory);
}
