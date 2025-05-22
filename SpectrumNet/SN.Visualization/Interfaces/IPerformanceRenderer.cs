#nullable enable

namespace SpectrumNet.SN.Visualization.Interfaces;

public interface IPerformanceRenderer
{
    void RenderPerformanceInfo(SKCanvas canvas, SKImageInfo info);
    void UpdateMetrics(PerformanceMetrics metrics);
}