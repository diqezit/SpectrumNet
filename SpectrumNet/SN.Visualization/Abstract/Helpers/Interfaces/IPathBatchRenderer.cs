#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Helpers.Interfaces;

public interface IPathBatchRenderer : IDisposable
{
    void RenderBatch(SKCanvas canvas, Action<SKPath> buildPath, SKPaint paint);
    void RenderFiltered<T>(SKCanvas canvas, IEnumerable<T> items, Func<T, bool> filter, Action<SKPath, T> addToPath, SKPaint paint);
    void RenderRects(SKCanvas canvas, IEnumerable<SKRect> rects, SKPaint paint, float cornerRadius = 0);
    void RenderCircles(SKCanvas canvas, IEnumerable<(SKPoint center, float radius)> circles, SKPaint paint);
    void RenderLines(SKCanvas canvas, IEnumerable<(SKPoint start, SKPoint end)> lines, SKPaint paint);
    void RenderPolygon(SKCanvas canvas, SKPoint[] points, SKPaint paint, bool close = true);
}