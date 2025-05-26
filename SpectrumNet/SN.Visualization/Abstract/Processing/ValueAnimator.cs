#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Processing;

public interface IValueAnimator
{
    float Current { get; }
    void Update(float target, float speed);
    void Reset(float value = 0f);
}

public class ValueAnimator : IValueAnimator
{
    private float _current;

    public float Current => _current;

    public void Update(float target, float speed) => 
        _current += (target - _current) * Math.Clamp(speed, 0f, 1f);
    
    public void Reset(float value = 0f) =>
        _current = value;
    
}

// массовый аниматор для оптимизации
public interface IValueAnimatorArray
{
    float[] Values { get; }
    void Update(float[] targets, float speed);
    void EnsureSize(int size);
}

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