#nullable enable

namespace SpectrumNet.Controllers.RenderCore.Overlay.Interface;

public interface IOverlayPerformanceManager : IDisposable
{
    void Initialize(IMainController controller);
    bool ShouldRender();
    void RecordFrame();
    void UpdateFpsLimit(bool isEnabled);
}