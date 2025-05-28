#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Processing.Interfaces;

// массовый аниматор для оптимизации
public interface IValueAnimatorArray
{
    float[] Values { get; }
    void Update(float[] targets, float speed);
    void EnsureSize(int size);
}
