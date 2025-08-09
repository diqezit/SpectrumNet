#nullable enable

namespace SpectrumNet.SN.Visualization.Overlay.Interfaces;

public interface IOverlayPerformanceManager : IDisposable
{
    void Initialize(IMainController controller);
    bool ShouldRender();
    void RecordFrame();
    void UpdateFpsLimit(bool isEnabled);
}