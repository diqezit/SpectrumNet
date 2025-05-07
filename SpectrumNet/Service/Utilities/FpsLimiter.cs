#nullable enable

namespace SpectrumNet.Service.Utilities;

public sealed class FpsLimiter
{
    private const double DEFAULT_TARGET_FPS = 60.0;

    private static readonly Lazy<FpsLimiter> _instance = new(() => new FpsLimiter());
    public static FpsLimiter Instance => _instance.Value;

    private readonly Stopwatch _stopwatch = new();
    private readonly object _syncLock = new();

    private long _lastFrameTime;
    private bool _isEnabled;

    public FpsLimiter()
    {
        _stopwatch.Start();
        _lastFrameTime = _stopwatch.ElapsedMilliseconds;
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => _isEnabled = value;
    }

    public double TargetFps { get; set; } = DEFAULT_TARGET_FPS;

    public bool ShouldRenderFrame()
    {
        if (!_isEnabled)
            return true;

        lock (_syncLock)
        {
            long currentTime = _stopwatch.ElapsedMilliseconds;
            double elapsedTime = currentTime - _lastFrameTime;
            double targetFrameTime = 1000.0 / TargetFps;

            if (elapsedTime < targetFrameTime)
                return false;

            _lastFrameTime = currentTime;
            return true;
        }
    }

    public void Reset()
    {
        lock (_syncLock)
        {
            _lastFrameTime = _stopwatch.ElapsedMilliseconds;
        }
    }
}