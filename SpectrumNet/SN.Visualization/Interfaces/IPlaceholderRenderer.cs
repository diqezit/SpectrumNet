#nullable enable

namespace SpectrumNet.SN.Visualization.Interfaces;

public interface IPlaceholderRenderer
{
    void RenderPlaceholder(SKCanvas canvas, SKImageInfo info);
    void HandleMouseDown(SKPoint point);
    void HandleMouseMove(SKPoint point);
    void HandleMouseUp(SKPoint point);
    void HandleMouseEnter();
    void HandleMouseLeave();
    bool HitTest(SKPoint point);
    IPlaceholder? GetPlaceholder();
    bool ShouldShowPlaceholder { get; }
}