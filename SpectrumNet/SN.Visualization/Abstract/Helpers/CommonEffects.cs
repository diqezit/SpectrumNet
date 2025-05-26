#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Helpers;

public interface ICommonEffects
{
    void RenderGlow(SKCanvas canvas, SKPath path, SKColor color, float radius, float alpha);
    void RenderGlow(SKCanvas canvas, SKRect rect, SKColor color, float radius, float alpha);
}

public class CommonEffects(IResourceManager resourceManager) : ICommonEffects
{
    private readonly IResourceManager _resourceManager = resourceManager;

    public void RenderGlow(SKCanvas canvas, SKPath path, SKColor color, float radius, float alpha)
    {
        if (path.IsEmpty) return;

        var paint = _resourceManager.GetPaint();
        try
        {
            ConfigureGlowPaint(paint, color, radius, alpha);
            canvas.DrawPath(path, paint);
        }
        finally
        {
            _resourceManager.ReturnPaint(paint);
        }
    }

    public void RenderGlow(SKCanvas canvas, SKRect rect, SKColor color, float radius, float alpha)
    {
        var paint = _resourceManager.GetPaint();
        try
        {
            ConfigureGlowPaint(paint, color, radius, alpha);
            canvas.DrawRect(rect, paint);
        }
        finally
        {
            _resourceManager.ReturnPaint(paint);
        }
    }

    private static void ConfigureGlowPaint(SKPaint paint, SKColor color, float radius, float alpha)
    {
        paint.Color = color.WithAlpha((byte)(alpha * 255));
        paint.Style = SKPaintStyle.Fill;
        paint.IsAntialias = true;
        paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, radius);
    }
}