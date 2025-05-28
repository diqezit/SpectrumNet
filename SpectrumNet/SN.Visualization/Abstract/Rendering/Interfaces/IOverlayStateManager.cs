// SN.Visualization/Abstract/Rendering/IOverlayStateManager.cs
namespace SpectrumNet.SN.Visualization.Abstract.Rendering.Interfaces;

// Отвечает за состояние оверлея
public interface IOverlayStateManager
{
    bool IsOverlayActive { get; }
    float OverlayAlphaFactor { get; }
    bool StateChanged { get; }
    bool StateChangeRequested { get; }

    void SetOverlayActive(bool isActive);
    void SetOverlayTransparency(float level);
    void ResetStateFlags();
}
