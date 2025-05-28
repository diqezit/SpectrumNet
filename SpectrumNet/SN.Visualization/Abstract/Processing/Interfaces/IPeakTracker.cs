#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Processing.Interfaces;

public interface IPeakTracker
{
    void Update(int index, float value, float deltaTime);
    float GetPeak(int index);
    bool HasActivePeaks();
    void EnsureSize(int size);
    void Configure(float holdTime, float fallSpeed);
}
