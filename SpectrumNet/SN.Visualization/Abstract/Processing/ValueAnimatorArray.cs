#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Processing;

public class ValueAnimatorArray : IValueAnimatorArray
{
    private float[] _values = [];

    public float[] Values => _values;

    public void Update(float[] targets, float speed)
    {
        int count = Math.Min(_values.Length, targets.Length);
        float clampedSpeed = Math.Clamp(speed, 0f, 1f);

        for (int i = 0; i < count; i++)
            _values[i] += (targets[i] - _values[i]) * clampedSpeed;
    }

    public void EnsureSize(int size)
    {
        if (_values.Length < size)
            Array.Resize(ref _values, size);
    }
}