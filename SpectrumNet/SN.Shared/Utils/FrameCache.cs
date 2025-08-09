#nullable enable

namespace SpectrumNet.SN.Shared.Utils;

public interface IFrameCache : IDisposable
{
    bool IsDirty { get; }
    void MarkDirty();
    bool ShouldForceRefresh();
    void Update(SKSurface surface, SKImageInfo info);
    void Draw(SKCanvas canvas);
}

public sealed class FrameCache(ISmartLogger? logger = null) : IFrameCache
{
    private const int MaxAge = 3;
    private readonly ISmartLogger _logger = logger ?? Instance;
    private readonly object _sync = new();
    private SKBitmap? _bitmap;
    private SKImageInfo _info;
    private bool _dirty = true;
    private int _age;
    private bool _disposed;

    public bool IsDirty
    {
        get { lock (_sync) return _dirty; }
    }

    public void MarkDirty()
    {
        lock (_sync)
        {
            _dirty = true;
            _age = 0;
        }
    }

    public bool ShouldForceRefresh()
    {
        lock (_sync) return _age >= MaxAge;
    }

    public void Update(SKSurface surface, SKImageInfo info)
    {
        ArgumentNullException.ThrowIfNull(surface);

        lock (_sync)
        {
            if (_disposed) return;

            try
            {
                if (_bitmap is null || _info.Width != info.Width || _info.Height != info.Height)
                {
                    _bitmap?.Dispose();
                    _bitmap = new SKBitmap(info.Width, info.Height, info.ColorType, info.AlphaType);
                    _info = info;
                }

                using var snapshot = surface.Snapshot();
                snapshot.ReadPixels(info, _bitmap!.GetPixels(), _bitmap.RowBytes);

                _dirty = false;
                _age = 0;
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, nameof(FrameCache), $"Error updating frame cache: {ex.Message}");
                _dirty = true;
                _age = 0;
            }
        }
    }

    public void Draw(SKCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        lock (_sync)
        {
            if (_disposed) return;

            if (_bitmap is null)
            {
                _logger.Log(LogLevel.Warning, nameof(FrameCache), "Attempted to draw null bitmap from cache.");
                return;
            }

            try
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawBitmap(_bitmap, 0, 0);
                _age++;
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, nameof(FrameCache), $"Error drawing cached frame: {ex.Message}");
                _dirty = true;
                _age = 0;
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;

            try
            {
                _bitmap?.Dispose();
                _bitmap = null;
                _disposed = true;
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, nameof(FrameCache), $"Error during FrameCache disposal: {ex.Message}");
            }
        }
    }
}