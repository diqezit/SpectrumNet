#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Helpers;

public sealed class PathBatchRenderer(IResourceManager resourceManager) : IPathBatchRenderer
{
    private readonly IResourceManager _resourceManager = resourceManager ??
        throw new ArgumentNullException(nameof(resourceManager));
    private readonly ISmartLogger _logger = Instance;
    private SKPath? _currentPath;
    private bool _disposed;

    public void RenderBatch(
        SKCanvas canvas,
        Action<SKPath> buildPath,
        SKPaint paint)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(paint);

        var path = GetOrCreatePath();
        _logger.Safe(() =>
        {
            path.Reset();
            buildPath(path);
            if (!path.IsEmpty) canvas.DrawPath(path, paint);
        }, nameof(PathBatchRenderer), "Error rendering batch");
    }

    public void RenderFiltered<T>(
        SKCanvas canvas,
        IEnumerable<T> items,
        Func<T, bool> filter,
        Action<SKPath, T> addToPath,
        SKPaint paint) =>
        RenderBatch(canvas, path =>
        {
            foreach (var item in items)
                if (filter(item)) addToPath(path, item);
        }, paint);

    public void RenderRects(
        SKCanvas canvas,
        IEnumerable<SKRect> rects,
        SKPaint paint,
        float cornerRadius = 0) =>
        RenderBatch(canvas, path =>
        {
            foreach (var rect in rects)
                if (cornerRadius > 0)
                    path.AddRoundRect(rect, cornerRadius, cornerRadius);
                else
                    path.AddRect(rect);
        }, paint);

    public void RenderCircles(
        SKCanvas canvas,
        IEnumerable<(SKPoint center, float radius)> circles,
        SKPaint paint) =>
        RenderBatch(canvas, path =>
        {
            foreach (var (center, radius) in circles)
                path.AddCircle(center.X, center.Y, radius);
        }, paint);

    public void RenderLines(
        SKCanvas canvas,
        IEnumerable<(SKPoint start, SKPoint end)> lines,
        SKPaint paint) =>
        RenderBatch(canvas, path =>
        {
            foreach (var (start, end) in lines)
            {
                path.MoveTo(start);
                path.LineTo(end);
            }
        }, paint);

    public void RenderPolygon(
        SKCanvas canvas,
        SKPoint[] points,
        SKPaint paint,
        bool close = true)
    {
        if (points.Length < 2) return;

        RenderBatch(canvas, path =>
        {
            path.MoveTo(points[0]);
            for (int i = 1; i < points.Length; i++)
                path.LineTo(points[i]);
            if (close) path.Close();
        }, paint);
    }

    private SKPath GetOrCreatePath() => _currentPath ??= _resourceManager.GetPath();

    public void Dispose()
    {
        if (_disposed) return;
        if (_currentPath != null)
        {
            _resourceManager.ReturnPath(_currentPath);
            _currentPath = null;
        }
        _disposed = true;
    }
}