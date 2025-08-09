#nullable enable

namespace SpectrumNet.SN.Visualization.Interfaces;

public interface IPlaceholder : IDisposable
{
    void Render(SKCanvas canvas, SKImageInfo info);
    SKSize CanvasSize { get; set; }
    void Reset();
    void OnMouseDown(SKPoint point);
    void OnMouseMove(SKPoint point);
    void OnMouseUp(SKPoint point);
    void OnMouseEnter();
    void OnMouseLeave();
    bool HitTest(SKPoint point);
    bool IsInteractive { get; set; }
    float Transparency { get; set; }
}
