#nullable enable

namespace SpectrumNet.SN.Visualization.Interfaces;

public interface IFpsLimiter
{
    bool IsLimited { get; }
    void SetLimit(bool enabled);
    void Reset();
    bool ShouldRenderFrame();
    event EventHandler<bool>? LimitChanged;
}