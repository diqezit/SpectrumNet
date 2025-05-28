#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Processing.Interfaces;

public interface IValueAnimator
{
    float Current { get; }
    void Update(float target, float speed);
    void Reset(float value = 0f);
}
