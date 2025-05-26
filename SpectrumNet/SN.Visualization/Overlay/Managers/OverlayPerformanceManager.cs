// SN.Visualization/Overlay/OverlayPerformanceManager.cs
#nullable enable

namespace SpectrumNet.SN.Visualization.Overlay.Managers;

public sealed class OverlayPerformanceManager : IOverlayPerformanceManager
{
    private const string LogPrefix = nameof(OverlayPerformanceManager);
    private const double TARGET_FPS = 60.0;
    private const double TARGET_FRAME_TIME_MS = 1000.0 / TARGET_FPS;

    private readonly ISmartLogger _logger = Instance;
    private readonly IPerformanceMetricsManager _performanceMetricsManager = PerformanceMetricsManager.Instance;
    private readonly Stopwatch _frameTimer = new();

    private IMainController? _controller;
    private bool _disposed;
    private long _lastFrameTime;

    public OverlayPerformanceManager()
    {
        _frameTimer.Start();
        _lastFrameTime = _frameTimer.ElapsedMilliseconds;
    }

    public void Initialize(IMainController controller) =>
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));

    public bool ShouldRender()
    {
        if (_controller == null || _disposed)
            return false;

        if (!_controller.LimitFpsTo60)
            return true;

        var currentTime = _frameTimer.ElapsedMilliseconds;
        var elapsedTime = currentTime - _lastFrameTime;

        if (elapsedTime >= TARGET_FRAME_TIME_MS)
        {
            _lastFrameTime = currentTime;
            return true;
        }

        return false;
    }

    public void RecordFrame()
    {
        if (_disposed)
            return;

        _performanceMetricsManager.RecordFrameTime();
    }

    public void UpdateFpsLimit(bool isEnabled)
    {
        if (!isEnabled)
            _lastFrameTime = _frameTimer.ElapsedMilliseconds;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _frameTimer.Stop();
        _disposed = true;
    }
}