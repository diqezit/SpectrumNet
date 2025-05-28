#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Processing;

public class PeakTracker : IPeakTracker
{
    private float[] _peaks = [];
    private float[] _timers = [];
    private float _holdTime = 0.5f;
    private float _fallSpeed = 0.1f;

    public void Configure(float holdTime, float fallSpeed)
    {
        _holdTime = holdTime;
        _fallSpeed = fallSpeed;
    }

    public void Update(int index, float value, float deltaTime)
    {
        if (index < 0 || index >= _peaks.Length) return;

        if (value > _peaks[index])
        {
            _peaks[index] = value;
            _timers[index] = _holdTime;
        }
        else if (_timers[index] > 0)
        {
            _timers[index] -= deltaTime;
        }
        else
        {
            _peaks[index] = Max(0, _peaks[index] - _fallSpeed * deltaTime);
        }
    }

    public float GetPeak(int index) =>
        index >= 0 && index < _peaks.Length ? _peaks[index] : 0f;

    public bool HasActivePeaks() =>
        Array.Exists(_timers, t => t > 0);

    public void EnsureSize(int size)
    {
        if (_peaks.Length < size)
        {
            Array.Resize(ref _peaks, size);
            Array.Resize(ref _timers, size);
        }
    }
}