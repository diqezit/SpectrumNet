#nullable enable

namespace SpectrumNet.Controllers.Interfaces.RenderCore;

public interface IFpsLimiter
{
    bool IsLimited { get; }
    void SetLimit(bool enabled);
    void Reset();
    bool ShouldRenderFrame();
    event EventHandler<bool>? LimitChanged;
}