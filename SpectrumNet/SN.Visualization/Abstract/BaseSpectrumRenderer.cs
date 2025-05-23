#nullable enable

using Timer = System.Threading.Timer;

namespace SpectrumNet.SN.Visualization.Abstract;

public abstract class BaseSpectrumRenderer : SpectrumProcessor, ISpectrumRenderer
{
    private const string LogPrefix = nameof(BaseSpectrumRenderer);

    private const int
        CLEANUP_INTERVAL_MS = 30000,
        HIGH_MEMORY_THRESHOLD_MB = 500;

    protected bool
        _isInitialized,
        _isOverlayActive,
        _overlayStateChanged,
        _needsRedraw;

    protected bool _useAntiAlias = true;
    protected bool _useAdvancedEffects = true;
    protected SKSamplingOptions _samplingOptions = new(
        SKFilterMode.Linear,
        SKMipmapMode.Linear);

    protected RenderQuality _quality;
    protected volatile bool _isApplyingQuality;

    private readonly Timer _cleanupTimer;

    protected BaseSpectrumRenderer() : base()
    {
        _cleanupTimer = new Timer(
            PerformPeriodicCleanup,
            null,
            CLEANUP_INTERVAL_MS,
            CLEANUP_INTERVAL_MS
        );
    }

    public virtual void SetOverlayTransparency(float level)
    {
        // переопределяем в наследниках
    }

    public RenderQuality Quality
    {
        get => _quality;
        set
        {
            if (_quality == value)
                return;

            _quality = value;
        }
    }

    public bool IsOverlayActive => _isOverlayActive;
    protected bool UseAntiAlias => _useAntiAlias;
    protected bool UseAdvancedEffects => _useAdvancedEffects;
    protected SKSamplingOptions SamplingOptions => _samplingOptions;

    public virtual void Initialize() =>
        _logger.Safe(() => HandleInitialize(),
                  GetType().Name,
                  "Failed to initialize renderer");

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
        RenderQuality quality = RenderQuality.Medium) =>
        _logger.Safe(() => HandleConfigure(isOverlayActive, quality),
                  GetType().Name,
                  "Failed to configure renderer");

    protected virtual void HandleConfigure(
        bool isOverlayActive,
        RenderQuality quality = RenderQuality.Medium)
    {
        bool overlayChanged = _isOverlayActive != isOverlayActive;
        bool qualityChanged = _quality != quality;

        _isOverlayActive = isOverlayActive;
        Quality = quality;

        SetSmoothingFactor(isOverlayActive
            ? OVERLAY_SMOOTHING_FACTOR
            : DEFAULT_SMOOTHING_FACTOR);

        if (overlayChanged || qualityChanged)
        {
            _logger.Log(LogLevel.Debug, GetType().Name, $"Configuration changed. New Quality: {Quality}");

            ApplyQualitySettings();
            _overlayStateChanged = overlayChanged;
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

    public virtual bool RequiresRedraw() => _needsRedraw || _overlayStateChanged || _isOverlayActive;

    protected virtual void ApplyQualitySettings() =>
        _logger.Safe(() => HandleApplyQualitySettings(),
                  GetType().Name,
                  "Failed to apply base quality settings");

    protected virtual void HandleApplyQualitySettings()
    {
        if (_isApplyingQuality)
            return;

        try
        {
            _isApplyingQuality = true;

            (_useAntiAlias, _useAdvancedEffects) = QualityBasedSettings();
            _samplingOptions = QualityBasedSamplingOptions();

            OnQualitySettingsApplied();
        }
        finally
        {
            _isApplyingQuality = false;
        }
    }

    protected virtual void OnQualitySettingsApplied() { }

    protected virtual (bool useAntiAlias, bool useAdvancedEffects) QualityBasedSettings() =>
        _quality switch
        {
            RenderQuality.Low => (false, false),
            RenderQuality.Medium => (true, true),
            RenderQuality.High => (true, true),
            _ => (true, true)
        };

    protected virtual SKSamplingOptions QualityBasedSamplingOptions() =>
        _quality switch
        {
            RenderQuality.Low => new(SKFilterMode.Nearest, SKMipmapMode.None),
            RenderQuality.Medium => new(SKFilterMode.Linear, SKMipmapMode.Linear),
            RenderQuality.High => new(SKFilterMode.Linear, SKMipmapMode.Linear),
            _ => new(SKFilterMode.Linear, SKMipmapMode.Linear)
        };

    protected bool QuickValidate(
        SKCanvas? canvas,
        float[]? spectrum,
        SKImageInfo info,
        SKPaint? paint) =>
        _isInitialized
        && canvas != null
        && spectrum != null
        && spectrum.Length > 0
        && paint != null
        && info.Width > 0
        && info.Height > 0;

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

        if (canvas == null)
        {
            _logger.Log(LogLevel.Error, GetType().Name, "Canvas is null");
            return false;
        }

        if (spectrum == null || spectrum.Length == 0)
        {
            _logger.Log(LogLevel.Error, GetType().Name, "Spectrum is null or empty");
            return false;
        }

        if (paint == null)
        {
            _logger.Log(LogLevel.Error, GetType().Name, "Paint is null");
            return false;
        }

        if (info.Width <= 0 || info.Height <= 0)
        {
            _logger.Log(LogLevel.Error, GetType().Name, $"Invalid image dimensions: {info.Width}x{info.Height}");
            return false;
        }

        if (barCount <= 0)
        {
            _logger.Log(LogLevel.Error, GetType().Name, "Bar count must be greater than zero");
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

    protected virtual void CleanupUnusedResources()
    {
        // переопределяем в наследниках
    }

    public override void Dispose() =>
        _logger.Safe(() => HandleDispose(),
                  GetType().Name,
                  "Error during base disposal");

    protected override void HandleDispose()
    {
        if (!_disposed)
        {
            _cleanupTimer?.Dispose();
            OnDispose();
            base.HandleDispose();
            _logger.Log(LogLevel.Debug, GetType().Name, "Disposed");
        }
    }
}