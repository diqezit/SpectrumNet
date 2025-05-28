namespace SpectrumNet.SN.Visualization.Abstract.Rendering.Interfaces;

// Отвечает за отслеживание времени для анимаций
public interface IAnimationTimer
{
    float Time { get; }
    float DeltaTime { get; }
    void Update();
    void Reset();
}
