#nullable enable

using Timer = System.Threading.Timer;

namespace SpectrumNet.SN.Visualization.Abstract.Core;

public abstract class BaseSpectrumRenderer : ISpectrumRenderer
{
    private const int CLEANUP_INTERVAL_MS = 30000;

    private readonly ISmartLogger _logger = Instance;
    private readonly Timer _cleanupTimer;

    private bool _isInitialized;
    private bool _disposed;
    private bool _isOverlayActive;
    private RenderQuality _quality = RenderQuality.Medium;
    private float _overlayAlpha = 0.8f;

    protected BaseSpectrumRenderer()
    {
        _cleanupTimer = new Timer(
            _ => PerformCleanup(),
            null,
            CLEANUP_INTERVAL_MS,
            CLEANUP_INTERVAL_MS);
    }

    public RenderQuality Quality => _quality;
    public bool IsOverlayActive => _isOverlayActive;
    protected float OverlayAlpha => _overlayAlpha;
    protected bool IsInitialized => _isInitialized;

    public virtual void Initialize()
    {
        if (!_isInitialized)
        {
            _isInitialized = true;
            OnInitialize();
            LogDebug("Renderer initialized");
        }
    }

    public virtual void Configure(
        bool isOverlayActive,
        RenderQuality quality = RenderQuality.Medium)
    {
        _isOverlayActive = isOverlayActive;
        _quality = quality;
        OnConfigurationChanged();
        LogDebug($"Configured: Overlay={isOverlayActive}, Quality={quality}");
    }

    public virtual void SetOverlayTransparency(float level)
    {
        _overlayAlpha = Math.Clamp(level, 0f, 1f);
        OnOverlayTransparencyChanged(_overlayAlpha);
    }

    public abstract void Render(
        SKCanvas? canvas,
        float[]? spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint? paint,
        Action<SKCanvas, SKImageInfo>? drawPerformanceInfo);

    public abstract bool RequiresRedraw();

    protected virtual void OnInitialize() { }
    protected virtual void OnConfigurationChanged() { }
    protected virtual void OnOverlayTransparencyChanged(float alpha) { }
    protected virtual void OnCleanup() { }
    protected virtual void OnQualitySettingsApplied() { }
    protected virtual void CleanupUnusedResources() { }

    private void PerformCleanup()
    {
        if (!_disposed)
        {
            OnCleanup();
            CleanupUnusedResources();
        }
    }

    protected void LogDebug(string message) =>
        _logger.Log(LogLevel.Debug, GetType().Name, message);

    protected void LogError(string message) =>
        _logger.Log(LogLevel.Error, GetType().Name, message);

    protected void SafeExecute(Action action, string errorMessage) =>
        _logger.Safe(action, GetType().Name, errorMessage);

    public virtual void Dispose()
    {
        if (!_disposed)
        {
            _cleanupTimer.Dispose();
            OnDispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    protected virtual void OnDispose() { }
}