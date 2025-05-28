#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Core.Modules;

public class DefaultResourcePool : IResourcePool
{
    private readonly ObjectPool<SKPaint> _paintPool = new(
        () => new SKPaint(),
        paint => paint.Reset());

    private readonly ObjectPool<SKPath> _pathPool = new(
        () => new SKPath(),
        path => path.Reset());

    public SKPath GetPath() => _pathPool.Get();

    public void ReturnPath(SKPath path)
    {
        if (path != null)
            _pathPool.Return(path);
    }

    public SKPaint GetPaint() => _paintPool.Get();

    public void ReturnPaint(SKPaint paint)
    {
        if (paint != null)
            _paintPool.Return(paint);
    }

    public void CleanupUnused()
    {
        _paintPool.Clear();
        _pathPool.Clear();
    }

    public void Dispose()
    {
        _paintPool.Dispose();
        _pathPool.Dispose();
        SuppressFinalize(this);
    }
}