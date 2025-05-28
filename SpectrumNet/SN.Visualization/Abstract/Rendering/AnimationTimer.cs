#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Rendering;

public class AnimationTimer : IAnimationTimer
{
    private float _time;
    private DateTime _lastUpdateTime = Now;
    private float _deltaTime;
    private bool _disposed;

    public float Time => _time;
    public float DeltaTime => _deltaTime;

    public void Update()
    {
        if (_disposed) return;

        var now = Now;
        _deltaTime = MathF.Max(0, (float)(now - _lastUpdateTime).TotalSeconds);
        _lastUpdateTime = now;
        _time += _deltaTime;
    }

    public void Reset()
    {
        if (_disposed) return;

        _time = 0;
        _deltaTime = 0;
        _lastUpdateTime = Now;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}