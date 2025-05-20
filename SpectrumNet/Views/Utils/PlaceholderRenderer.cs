#nullable enable

namespace SpectrumNet.Views.Utils;

public sealed class PlaceholderRenderer : IPlaceholderRenderer
{
    private const string LogPrefix = nameof(PlaceholderRenderer);
    private readonly ISmartLogger _logger = Instance;

    private readonly IPlaceholder _placeholder;
    private readonly IMainController _controller;

    public bool ShouldShowPlaceholder => !_controller.IsRecording;

    public PlaceholderRenderer(IMainController controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _placeholder = new RendererPlaceholder { CanvasSize = new SKSize(1, 1) };
    }

    public void RenderPlaceholder(SKCanvas canvas, SKImageInfo info)
    {
        _logger.Safe(() =>
        {
            UpdatePlaceholderSize(info);
            _placeholder.Render(canvas, info);
        }, LogPrefix, "Error rendering placeholder");
    }

    private void UpdatePlaceholderSize(SKImageInfo info) => 
        _placeholder.CanvasSize = new SKSize(info.Width, info.Height);

    public void HandleMouseDown(SKPoint point)
    {
        if (ShouldShowPlaceholder)
            _placeholder.OnMouseDown(point);
    }

    public void HandleMouseMove(SKPoint point)
    {
        if (ShouldShowPlaceholder)
            _placeholder.OnMouseMove(point);
    }

    public void HandleMouseUp(SKPoint point)
    {
        if (ShouldShowPlaceholder)
            _placeholder.OnMouseUp(point);
    }

    public void HandleMouseEnter()
    {
        if (ShouldShowPlaceholder)
            _placeholder.OnMouseEnter();
    }

    public void HandleMouseLeave()
    {
        if (ShouldShowPlaceholder)
            _placeholder.OnMouseLeave();
    }

    public bool HitTest(SKPoint point) =>
        ShouldShowPlaceholder && _placeholder.HitTest(point);

    public IPlaceholder? GetPlaceholder() =>
        ShouldShowPlaceholder ? _placeholder : null;
}