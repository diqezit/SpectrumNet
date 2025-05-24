namespace SpectrumNet.SN.Visualization.Abstract.Rendering;

// Отвечает за отслеживание времени для анимаций
public interface IAnimationTimer
{
    float Time { get; }
    float DeltaTime { get; }
    void Update();
    void Reset();
}

public class AnimationTimer : IAnimationTimer
{
    private float _time;
    private DateTime _lastUpdateTime = Now;
    private float _deltaTime;

    public float Time => _time;
    public float DeltaTime => _deltaTime;

    public void Update()
    {
        var now = Now;
        _deltaTime = MathF.Max(0, (float)(now - _lastUpdateTime).TotalSeconds);
        _lastUpdateTime = now;
        _time += _deltaTime;
    }

    public void Reset()
    {
        _time = 0;
        _deltaTime = 0;
        _lastUpdateTime = Now;
    }
}