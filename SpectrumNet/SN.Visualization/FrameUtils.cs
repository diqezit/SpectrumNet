namespace SpectrumNet.SN.Visualization;

public interface IFrameCache : IDisposable
{
    bool IsDirty { get; }
    bool IsValid { get; }
    void MarkDirty();
    bool ShouldForceRefresh();
    void Update(SKSurface surface, SKImageInfo info);
    void Draw(SKCanvas canvas);
}

public sealed class FrameCache(ISmartLogger? logger = null, int maxAge = 3) : IFrameCache
{
    private readonly ISmartLogger _log = logger ?? Instance;
    private readonly object _sync = new();
    private readonly int _maxAge = maxAge;

    private SKBitmap? _bitmap;
    private SKImageInfo _info;
    private bool _dirty = true;
    private int _age;
    private bool _disposed;

    public bool IsDirty { get { lock (_sync) return _dirty; } }
    public bool IsValid { get { lock (_sync) return !_dirty && _bitmap != null; } }

    public void MarkDirty()
    {
        lock (_sync) { _dirty = true; _age = 0; }
    }

    public bool ShouldForceRefresh()
    {
        lock (_sync) return _age >= _maxAge;
    }

    public void Update(SKSurface surface, SKImageInfo info)
    {
        lock (_sync)
        {
            if (_disposed) return;

            try
            {
                if (_bitmap is null ||
                    _info.Width != info.Width ||
                    _info.Height != info.Height ||
                    _info.ColorType != info.ColorType ||
                    _info.AlphaType != info.AlphaType)
                {
                    _bitmap?.Dispose();
                    _bitmap = new SKBitmap(info.Width, info.Height, info.ColorType, info.AlphaType);
                    _info = info;
                }

                using SKImage snapshot = surface.Snapshot();
                if (snapshot.ReadPixels(info, _bitmap.GetPixels(), _bitmap.RowBytes))
                {
                    _dirty = false;
                    _age = 0;
                }
                else
                {
                    _log.Log(LogLevel.Warning, nameof(FrameCache), "ReadPixels failed");
                    _dirty = true;
                }
            }
            catch (Exception ex)
            {
                _log.Log(LogLevel.Error, nameof(FrameCache), $"Update error: {ex.Message}");
                _dirty = true;
            }
        }
    }

    public void Draw(SKCanvas canvas)
    {
        lock (_sync)
        {
            if (_disposed || _bitmap is null) return;

            try
            {
                canvas.DrawBitmap(_bitmap, 0, 0);
                _age++;
            }
            catch (Exception ex)
            {
                _log.Log(LogLevel.Error, nameof(FrameCache), $"Draw error: {ex.Message}");
                _dirty = true;
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _bitmap?.Dispose();
            _bitmap = null;
            _disposed = true;
        }
    }
}

public sealed class FpsLimiter
{
    private const double DefaultTargetFps = 60.0;

    private static readonly Lazy<FpsLimiter> _instance = new(() => new FpsLimiter());
    public static FpsLimiter Instance => _instance.Value;

    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly object _sync = new();
    private long _lastFrameTime;

    private bool _isEnabled;
    private double _targetFps = DefaultTargetFps;

    private FpsLimiter() => _lastFrameTime = _stopwatch.ElapsedMilliseconds;

    public bool IsEnabled
    {
        get { lock (_sync) return _isEnabled; }
        set { lock (_sync) _isEnabled = value; }
    }

    public double TargetFps
    {
        get { lock (_sync) return _targetFps; }
        set { lock (_sync) _targetFps = Max(value, 1.0); }
    }

    public bool ShouldRenderFrame()
    {
        lock (_sync)
        {
            if (!_isEnabled) return true;

            long now = _stopwatch.ElapsedMilliseconds;
            double targetMs = 1000.0 / _targetFps;

            if (now - _lastFrameTime < targetMs) return false;

            _lastFrameTime = now;
            return true;
        }
    }

    public void Reset()
    {
        lock (_sync) _lastFrameTime = _stopwatch.ElapsedMilliseconds;
    }
}
