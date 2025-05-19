#nullable enable

namespace SpectrumNet.Service.Utilities;

public class FrameCache : IDisposable
{
    private const string LogPrefix = nameof(FrameCache);
    private readonly ISmartLogger _logger = Instance;

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
        lock (_cacheLock)
        {
            _isDirty = dirty;
            if (dirty)
            {
                _frameAge = 0;
            }
        }
    }

    public bool ShouldForceRefresh()
    {
        lock (_cacheLock)
        {
            return _frameAge >= MAX_FRAME_AGE;
        }
    }

    public void UpdateCache(SKSurface surface, SKImageInfo info)
    {
        if (_isDisposed || surface == null)
            return;

        SKBitmap? newCache = null;

        try
        {
            newCache = CreateNewBitmapIfNeeded(info);
            if (newCache != null || _cachedFrame != null)
            {
                UpdateCacheWithSafety(surface, info, newCache);
            }
        }
        catch
        {
            if (newCache != null && !_isDisposed &&
                (_cachedFrame == null || !ReferenceEquals(newCache, _cachedFrame)))
            {
                newCache.Dispose();
            }
            throw;
        }
    }

    private SKBitmap? CreateNewBitmapIfNeeded(SKImageInfo info)
    {
        bool needsNewCache = false;

        lock (_cacheLock)
        {
            if (_isDisposed)
                return null;

            needsNewCache = _cachedFrame == null ||
                          _lastInfo.Width != info.Width ||
                          _lastInfo.Height != info.Height;
        }

        if (needsNewCache)
        {
            return _logger.SafeResult(() =>
                new SKBitmap(
                    info.Width,
                    info.Height,
                    info.ColorType,
                    info.AlphaType),
                null,
                LogPrefix,
                "Error creating bitmap");
        }

        return null;
    }

    private void UpdateCacheWithSafety(
        SKSurface surface,
        SKImageInfo info,
        SKBitmap? newCache)
    {
        lock (_cacheLock)
        {
            if (_isDisposed)
                return;

            if (newCache != null)
            {
                var oldCache = _cachedFrame;
                _cachedFrame = newCache;
                _lastInfo = info;

                if (oldCache != null)
                {
                    _logger.Safe(() => oldCache.Dispose(),
                        LogPrefix,
                        "Error disposing old cache");
                }
            }

            if (_cachedFrame != null)
            {
                _logger.Safe(() => CopyContentFromSurface(surface, info),
                    LogPrefix,
                    "Error copying content");
            }

            MarkDirty(false);
            _frameAge = 0;
        }
    }

    private void CopyContentFromSurface(SKSurface surface, SKImageInfo info)
    {
        if (_cachedFrame == null || _isDisposed)
            return;

        using var snapshot = surface.Snapshot();
        snapshot.ReadPixels(info, _cachedFrame.GetPixels(), _cachedFrame.RowBytes);
    }

    public void DrawCachedFrame(SKCanvas canvas)
    {
        if (_isDisposed || canvas == null)
            return;

        lock (_cacheLock)
        {
            if (_cachedFrame == null)
                return;

            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(_cachedFrame, 0, 0);

            _frameAge++;
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        lock (_cacheLock)
        {
            if (_cachedFrame != null)
            {
                _logger.Safe(() => _cachedFrame.Dispose(),
                    LogPrefix,
                    "Error disposing frame cache");
                _cachedFrame = null;
            }

            _isDisposed = true;
        }
    }
}