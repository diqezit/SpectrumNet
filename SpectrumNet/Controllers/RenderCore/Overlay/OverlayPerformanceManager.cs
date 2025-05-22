#nullable enable

namespace SpectrumNet.Controllers.RenderCore.Overlay;

public sealed class OverlayPerformanceManager : IOverlayPerformanceManager
{
    private const string LogPrefix = nameof(OverlayPerformanceManager);

    private readonly ISmartLogger _logger = Instance;
    private readonly FpsLimiter _fpsLimiter = FpsLimiter.Instance;
    private readonly IPerformanceMetricsManager _performanceMetricsManager = PerformanceMetricsManager.Instance;

    private IMainController? _controller;
    private bool _disposed;

    public void Initialize(IMainController controller) => 
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));

    public bool ShouldRender()
    {
        if (_controller == null)
            return false;

        return !_controller.LimitFpsTo60 || _fpsLimiter.ShouldRenderFrame();
    }

    public void RecordFrame()
    {
        if (_disposed)
            return;

        _performanceMetricsManager.RecordFrameTime();
    }

    public void UpdateFpsLimit(bool isEnabled)
    {
        _logger.Safe(() => HandleUpdateFpsLimit(isEnabled),
            LogPrefix, "Error updating FPS limit");
    }

    private void HandleUpdateFpsLimit(bool isEnabled)
    {
        _fpsLimiter.IsEnabled = isEnabled;
        _fpsLimiter.Reset();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
    }
}