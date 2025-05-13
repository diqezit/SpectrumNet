#nullable enable

namespace SpectrumNet.Service.Utilities;

public class FrameCache : IDisposable
{
    private SKBitmap? _cachedFrame;
    private bool _isDirty = true;
    private SKImageInfo _lastInfo;
    private bool _isDisposed;
    private int _frameAge = 0;
    private const int MAX_FRAME_AGE = 3;

    private readonly object _cacheLock = new();

    public bool IsDirty => _isDirty;

    public void MarkDirty(bool dirty = true)
    {
        _isDirty = dirty;
        if (dirty)
        {
            _frameAge = 0;
        }
    }

    public bool ShouldForceRefresh() => _frameAge >= MAX_FRAME_AGE;

    public void UpdateCache(SKSurface surface, SKImageInfo info)
    {
        if (_isDisposed) return;

        lock (_cacheLock)
        {
            EnsureCacheSizeMatchesInfo(info);
            CopyFromSurfaceToCache(surface, info);
            MarkDirty(false);
            _frameAge = 0;
        }
    }

    private void EnsureCacheSizeMatchesInfo(SKImageInfo info)
    {
        if (_cachedFrame == null ||
            _lastInfo.Width != info.Width ||
            _lastInfo.Height != info.Height)
        {
            RecreateCache(info);
        }
    }

    private void RecreateCache(SKImageInfo info)
    {
        var oldFrame = _cachedFrame;
        SKBitmap? newFrame = null;

        try
        {
            newFrame = new SKBitmap(
                info.Width,
                info.Height,
                info.ColorType,
                info.AlphaType);

            _cachedFrame = newFrame;
            _lastInfo = info;
        }
        catch
        {
            newFrame?.Dispose();
            throw;
        }
        finally
        {
            oldFrame?.Dispose();
        }
    }

    private void CopyFromSurfaceToCache(SKSurface surface, SKImageInfo info)
    {
        if (_cachedFrame == null) return;

        SKImage? snapshot = null;
        try
        {
            snapshot = surface.Snapshot();
            snapshot.ReadPixels(info, _cachedFrame.GetPixels(), _cachedFrame.RowBytes);
        }
        finally
        {
            snapshot?.Dispose();
        }
    }

    public void DrawCachedFrame(SKCanvas canvas)
    {
        if (_isDisposed) return;

        lock (_cacheLock)
        {
            if (_cachedFrame == null) return;

            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(_cachedFrame, 0, 0);

            _frameAge++;
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;

        lock (_cacheLock)
        {
            _cachedFrame?.Dispose();
            _cachedFrame = null;
            _isDisposed = true;
        }
    }
}