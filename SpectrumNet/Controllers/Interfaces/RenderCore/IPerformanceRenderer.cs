#nullable enable

namespace SpectrumNet.Controllers.Interfaces.RenderCore;

public interface IPerformanceRenderer
{
    void RenderPerformanceInfo(SKCanvas canvas, SKImageInfo info);
    void UpdateMetrics(PerformanceMetrics metrics);
}