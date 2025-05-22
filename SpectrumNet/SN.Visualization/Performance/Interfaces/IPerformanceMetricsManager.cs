#nullable enable

namespace SpectrumNet.SN.Visualization.Performance.Interfaces;

public interface IPerformanceMetricsManager
{
    event EventHandler<PerformanceMetrics>? PerformanceMetricsUpdated;
    event EventHandler<PerformanceLevel>? PerformanceLevelChanged;

    float GetCurrentFps();
    double GetCurrentCpuUsagePercent();
    double GetCurrentRamUsageMb();
    PerformanceLevel GetCurrentPerformanceLevel();

    void RecordFrameTime();
    void Initialize();
    void Cleanup();
}