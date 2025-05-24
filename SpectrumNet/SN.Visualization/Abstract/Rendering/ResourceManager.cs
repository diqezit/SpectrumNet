// SN.Visualization/Abstract/Rendering/ResourceManager.cs
#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Rendering;

// управление пулами ресурсов
public interface IResourceManager : IDisposable
{
    SKPath GetPath();
    void ReturnPath(SKPath path);
    SKPaint GetPaint();
    void ReturnPaint(SKPaint paint);
}

public class ResourceManager(int poolSize = ResourceManager.DEFAULT_POOL_SIZE) : IResourceManager
{
    public const int DEFAULT_POOL_SIZE = 5;
    private const int INITIAL_POOL_COUNT = 2;

    private readonly object _syncLock = new();
    private readonly ObjectPool<SKPath> _pathPool = CreatePathPool(poolSize);
    private readonly ObjectPool<SKPaint> _paintPool = CreatePaintPool(poolSize);

    private bool _disposed;

    public SKPath GetPath()
    {
        ValidateNotDisposed();
        return AcquirePathFromPool();
    }

    public void ReturnPath(SKPath path)
    {
        if (ShouldReturnResource(path))
            ReturnPathToPool(path);
    }

    public SKPaint GetPaint()
    {
        ValidateNotDisposed();
        return AcquirePaintFromPool();
    }

    public void ReturnPaint(SKPaint paint)
    {
        if (ShouldReturnResource(paint))
            ReturnPaintToPool(paint);
    }

    public void Dispose()
    {
        lock (_syncLock)
        {
            if (ShouldDispose())
                PerformDisposal();
        }
    }

    private static ObjectPool<SKPath> CreatePathPool(int maxSize) =>
        new(
            CreatePath,
            ResetPath,
            initialCount: INITIAL_POOL_COUNT,
            maxSize: maxSize);

    private static ObjectPool<SKPaint> CreatePaintPool(int maxSize) =>
        new(
            CreatePaint,
            ResetPaint,
            initialCount: INITIAL_POOL_COUNT,
            maxSize: maxSize);

    private static SKPath CreatePath() =>
        new();

    private static SKPaint CreatePaint() =>
        new();

    private static void ResetPath(SKPath path)
    {
        SafeReset(() => path.Reset());
    }

    private static void ResetPaint(SKPaint paint)
    {
        SafeReset(() => paint.Reset());
    }

    private static void SafeReset(Action resetAction)
    {
        try
        {
            resetAction();
        }
        catch
        {
            // Ignore reset errors
        }
    }

    private void ValidateNotDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private SKPath AcquirePathFromPool() =>
        _pathPool.Get();

    private void ReturnPathToPool(SKPath path) =>
        _pathPool.Return(path);

    private SKPaint AcquirePaintFromPool() =>
        _paintPool.Get();

    private void ReturnPaintToPool(SKPaint paint) =>
        _paintPool.Return(paint);

    private bool ShouldReturnResource<T>(T? resource) where T : class =>
        !_disposed && resource != null;

    private bool ShouldDispose() =>
        !_disposed;

    private void PerformDisposal()
    {
        DisposePools();
        MarkAsDisposed();
        SuppressFinalizer();
    }

    private void DisposePools()
    {
        DisposePathPool();
        DisposePaintPool();
    }

    private void DisposePathPool() =>
        _pathPool?.Dispose();

    private void DisposePaintPool() =>
        _paintPool?.Dispose();

    private void MarkAsDisposed() =>
        _disposed = true;

    private void SuppressFinalizer() =>
        SuppressFinalize(this);
}