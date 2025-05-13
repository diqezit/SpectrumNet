#nullable enable

namespace SpectrumNet.Controllers.Interfaces;

public interface ITransparencyManager : IDisposable
{
    float CurrentTransparency { get; }
    bool IsActive { get; }

    event Action<float> TransparencyChanged;

    void OnMouseEnter();
    void OnMouseLeave();
    void OnMouseMove();
    void ActivateTransparency();
    void DeactivateTransparency();
    void SetRendererFactory(IRendererFactory factory);
    void EnableGlobalMouseTracking();
    void DisableGlobalMouseTracking();
}