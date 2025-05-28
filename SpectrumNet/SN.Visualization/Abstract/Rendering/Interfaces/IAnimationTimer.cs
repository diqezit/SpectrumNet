#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Rendering.Interfaces;

public interface IAnimationTimer : IDisposable
{
    float Time { get; }
    float DeltaTime { get; }
    void Update();
    void Reset();
}