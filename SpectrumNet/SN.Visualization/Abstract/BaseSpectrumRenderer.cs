#nullable enable

using Timer = System.Threading.Timer;

namespace SpectrumNet.SN.Visualization.Abstract;

public abstract class BaseSpectrumRenderer : ISpectrumRenderer
{
    private const string LogPrefix = nameof(BaseSpectrumRenderer);

    private const int 
        CLEANUP_INTERVAL_MS = 30000,
        HIGH_MEMORY_THRESHOLD_MB = 500;

    protected const float MIN_MAGNITUDE_THRESHOLD = 0.01f;

    protected readonly ISmartLogger _logger = Instance;
    protected readonly ISpectrumProcessingCoordinator _processingCoordinator;
    protected readonly IQualityManager _qualityManager;
    protected readonly IOverlayStateManager _overlayStateManager;

    private readonly Timer _cleanupTimer;

    protected bool 
        _isInitialized,
        _needsRedraw,
        _disposed;

    protected BaseSpectrumRenderer(
        ISpectrumProcessingCoordinator? processingCoordinator = null,
        IQualityManager? qualityManager = null,
        IOverlayStateManager? overlayStateManager = null)
    {
        _processingCoordinator = processingCoordinator ?? new SpectrumProcessingCoordinator();
        _qualityManager = qualityManager ?? new QualityManager();
        _overlayStateManager = overlayStateManager ?? new OverlayStateManager();

        _cleanupTimer = new Timer(
            PerformPeriodicCleanup,
            null,
            CLEANUP_INTERVAL_MS,
            CLEANUP_INTERVAL_MS
        );
    }

    public RenderQuality Quality => _qualityManager.Quality;
    public bool IsOverlayActive => _overlayStateManager.IsOverlayActive;
    protected bool UseAntiAlias => _qualityManager.UseAntiAlias;
    protected bool UseAdvancedEffects => _qualityManager.UseAdvancedEffects;
    protected SKSamplingOptions SamplingOptions => _qualityManager.SamplingOptions;

    public virtual void SetOverlayTransparency(float level)
    {
        _overlayStateManager.SetOverlayTransparency(level);
        _needsRedraw = true;
    }

    public virtual void Initialize()
    {
        _logger.Safe(() => HandleInitialize(),
                  GetType().Name,
                  "Failed to initialize renderer");
    }

    protected virtual void HandleInitialize()
    {
        if (!_isInitialized)
        {
            _isInitialized = true;
            OnInitialize();
        }
    }

    protected virtual void OnInitialize() { }

    public virtual void Configure(
        bool isOverlayActive,
        RenderQuality quality = RenderQuality.Medium)
    {
        _logger.Safe(() => HandleConfigure(isOverlayActive, quality),
                  GetType().Name,
                  "Failed to configure renderer");
    }

    protected virtual void HandleConfigure(
        bool isOverlayActive,
        RenderQuality quality = RenderQuality.Medium)
    {
        bool overlayChanged = _overlayStateManager.IsOverlayActive != isOverlayActive;
        bool qualityChanged = _qualityManager.Quality != quality;

        _overlayStateManager.SetOverlayActive(isOverlayActive);
        _qualityManager.ApplyQuality(quality);

        _processingCoordinator.SetSmoothingFactor(isOverlayActive ? 0.5f : 0.3f);

        if (overlayChanged || qualityChanged)
        {
            _logger.Log(LogLevel.Debug, GetType().Name,
                $"Configuration changed. New Quality: {quality}");

            _needsRedraw = true;
            OnConfigurationChanged();
        }
    }

    protected virtual void OnConfigurationChanged() { }

    public abstract void Render(
        SKCanvas? canvas,
        float[]? spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint? paint,
        Action<SKCanvas, SKImageInfo>? drawPerformanceInfo);

    public virtual bool RequiresRedraw() =>
        _needsRedraw ||
        _overlayStateManager.StateChanged ||
        _overlayStateManager.IsOverlayActive;

    protected bool ValidateRenderParameters(
        SKCanvas? canvas,
        float[]? spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint? paint)
    {
        if (!_isInitialized)
        {
            _logger.Log(LogLevel.Error, GetType().Name, "Renderer is not initialized");
            return false;
        }

        if (canvas == null || spectrum == null || spectrum.Length == 0 || paint == null)
        {
            return false;
        }

        if (info.Width <= 0 || info.Height <= 0 || barCount <= 0)
        {
            return false;
        }

        if (_disposed)
        {
            _logger.Log(LogLevel.Error, GetType().Name, "Renderer is disposed");
            return false;
        }

        return true;
    }

    protected static bool IsRenderAreaVisible(
        SKCanvas? canvas,
        float x,
        float y,
        float width,
        float height) =>
        canvas == null ||
        !canvas.QuickReject(new SKRect(x, y, x + width, y + height));

    private void PerformPeriodicCleanup(object? state)
    {
        if (_disposed) return;

        _logger.Safe(() =>
        {
            CleanupUnusedResources();

            var memoryMb = GC.GetTotalMemory(false) / 1024 / 1024;
            if (memoryMb > HIGH_MEMORY_THRESHOLD_MB)
            {
                GC.Collect(1, GCCollectionMode.Optimized);
                _logger.Log(LogLevel.Debug, GetType().Name,
                    $"Performed GC cleanup, memory was {memoryMb}MB");
            }
        },
        GetType().Name,
        "Periodic cleanup error");
    }

    protected virtual void CleanupUnusedResources() { }

    public virtual void Dispose()
    {
        _logger.Safe(() => HandleDispose(),
                  GetType().Name,
                  "Error during base disposal");
    }

    protected virtual void HandleDispose()
    {
        if (!_disposed)
        {
            _cleanupTimer?.Dispose();
            _processingCoordinator?.Dispose();
            OnDispose();
            _disposed = true;
            _logger.Log(LogLevel.Debug, GetType().Name, "Disposed");
        }
    }

    protected virtual void OnDispose() { }
}