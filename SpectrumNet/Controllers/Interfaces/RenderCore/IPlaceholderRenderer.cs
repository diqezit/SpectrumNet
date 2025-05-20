#nullable enable

namespace SpectrumNet.Controllers.Interfaces.RenderCore;

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