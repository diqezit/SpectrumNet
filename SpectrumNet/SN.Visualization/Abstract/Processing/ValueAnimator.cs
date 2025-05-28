#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Processing;

public class ValueAnimator : IValueAnimator
{
    private float _current;

    public float Current => _current;

    public void Update(float target, float speed) =>
        _current += (target - _current) * Math.Clamp(speed, 0f, 1f);

    public void Reset(float value = 0f) =>
        _current = value;

}