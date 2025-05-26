#nullable enable

using Timer = System.Threading.Timer;

namespace SpectrumNet.SN.Visualization.Abstract;

public abstract class BaseSpectrumRenderer : ISpectrumRenderer
{
    private const int CLEANUP_INTERVAL_MS = 30000, HIGH_MEMORY_THRESHOLD_MB = 500;
    protected const float MIN_MAGNITUDE_THRESHOLD = 0.01f;

    private readonly ISmartLogger _logger = Instance;
    private readonly ISpectrumProcessingCoordinator _processingCoordinator;
    private readonly IQualityManager _qualityManager;
    private readonly IOverlayStateManager _overlayStateManager;
    private readonly IRenderingHelpers _renderingHelpers;
    private readonly Timer _cleanupTimer;

    private bool _isInitialized, _needsRedraw, _disposed;
    private Func<float, byte>? _customAlphaCalculator;
    private Func<float, float, float, float>? _customLerpFunction;
    private Func<float, SKColor, SKColor, SKColor>? _customGradientFunction;
    private Action<SKCanvas, SKImageInfo>? _customBackgroundRenderer;
    private Action<SKCanvas, float[], SKImageInfo>? _customPostProcessor;

    protected BaseSpectrumRenderer(
        ISpectrumProcessingCoordinator? processingCoordinator = null,
        IQualityManager? qualityManager = null,
        IOverlayStateManager? overlayStateManager = null,
        IRenderingHelpers? renderingHelpers = null)
    {
        _processingCoordinator = processingCoordinator ?? new SpectrumProcessingCoordinator();
        _qualityManager = qualityManager ?? new QualityManager();
        _overlayStateManager = overlayStateManager ?? new OverlayStateManager();
        _renderingHelpers = renderingHelpers ?? RenderingHelpers.Instance;
        _cleanupTimer = new Timer(
            PerformPeriodicCleanup,
            null,
            CLEANUP_INTERVAL_MS,
            CLEANUP_INTERVAL_MS);
    }

    public RenderQuality Quality => _qualityManager.Quality;
    public bool IsOverlayActive => _overlayStateManager.IsOverlayActive;
    protected bool UseAntiAlias => _qualityManager.UseAntiAlias;
    protected bool UseAdvancedEffects => _qualityManager.UseAdvancedEffects;
    protected SKSamplingOptions SamplingOptions => _qualityManager.SamplingOptions;

    protected void LogDebug(string message) =>
        _logger.Log(LogLevel.Debug, GetType().Name, message);

    protected void LogError(string message) =>
        _logger.Log(LogLevel.Error, GetType().Name, message);

    protected void SafeExecute(Action action, string errorMessage) =>
        _logger.Safe(action, GetType().Name, errorMessage);

    protected virtual float GetAnimationTime() => 0f;
    protected virtual float GetAnimationDeltaTime() => 0f;

    protected void SetProcessingSmoothingFactor(float factor) =>
        _processingCoordinator.SetSmoothingFactor(factor);

    protected (bool isValid, float[]? processedSpectrum) PrepareSpectrum(
        float[]? spectrum,
        int targetCount,
        int spectrumLength) =>
        _processingCoordinator.PrepareSpectrum(spectrum, targetCount, spectrumLength);

    protected void SetOverlayActive(bool isActive) =>
        _overlayStateManager.SetOverlayActive(isActive);

    protected float GetOverlayAlphaFactor() =>
        _overlayStateManager.OverlayAlphaFactor;

    protected bool IsOverlayStateChanged() =>
        _overlayStateManager.StateChanged;

    protected void ResetOverlayStateFlags() =>
        _overlayStateManager.ResetStateFlags();

    protected void ApplyQualitySettings(RenderQuality quality) =>
        _qualityManager.ApplyQuality(quality);

    public virtual void SetAlphaCalculator(Func<float, byte>? calculator) =>
        _customAlphaCalculator = calculator;

    public virtual void SetLerpFunction(Func<float, float, float, float>? lerpFunction) =>
        _customLerpFunction = lerpFunction;

    public virtual void SetGradientFunction(Func<float, SKColor, SKColor, SKColor>? gradientFunction) =>
        _customGradientFunction = gradientFunction;

    public virtual void SetBackgroundRenderer(Action<SKCanvas, SKImageInfo>? backgroundRenderer) =>
        _customBackgroundRenderer = backgroundRenderer;

    public virtual void SetPostProcessor(Action<SKCanvas, float[], SKImageInfo>? postProcessor) =>
        _customPostProcessor = postProcessor;

    protected byte CalculateAlpha(float magnitude, float multiplier = 255f) =>
        _customAlphaCalculator?.Invoke(magnitude) ??
        (byte)MathF.Min(Clamp(magnitude, 0f, 1f) * multiplier, 255);

    protected SKColor ApplyAlpha(SKColor color, float magnitude, float multiplier = 1f) =>
        color.WithAlpha(CalculateAlpha(magnitude, multiplier * 255f));

    protected SKColor InterpolateColor(
        SKColor baseColor,
        float magnitude,
        float minAlpha = 0.1f,
        float maxAlpha = 1f) =>
        baseColor.WithAlpha((byte)(Lerp(minAlpha, maxAlpha, magnitude) * 255));

    protected float Lerp(float current, float target, float amount) =>
        _customLerpFunction?.Invoke(current, target, amount) ??
        _renderingHelpers.Lerp(current, target, amount);

    protected SKColor GetGradientColor(float value, SKColor startColor, SKColor endColor) =>
        _customGradientFunction?.Invoke(value, startColor, endColor) ??
        _renderingHelpers.GetGradientColor(value, startColor, endColor);

    // Delegated rendering helpers
    protected float GetAverageInRange(float[] array, int start, int end) =>
        _renderingHelpers.GetAverageInRange(array, start, end);

    protected SKPoint[] CreateCirclePoints(int pointCount, float radius, SKPoint center) =>
        _renderingHelpers.CreateCirclePoints(pointCount, radius, center);

    protected Vector2[] CreateCircleVectors(int count) =>
        _renderingHelpers.CreateCircleVectors(count);

    protected float SmoothStep(float edge0, float edge1, float x) =>
        _renderingHelpers.SmoothStep(edge0, edge1, x);

    protected float Distance(SKPoint p1, SKPoint p2) =>
        _renderingHelpers.Distance(p1, p2);

    protected float Normalize(float value, float min, float max) =>
        _renderingHelpers.Normalize(value, min, max);

    protected SKRect GetBarRect(
        float x,
        float magnitude,
        float barWidth,
        float canvasHeight,
        float minHeight = 1f) =>
        _renderingHelpers.GetBarRect(x, magnitude, barWidth, canvasHeight, minHeight);

    protected SKRect GetCenteredRect(SKPoint center, float width, float height) =>
        _renderingHelpers.GetCenteredRect(center, width, height);

    protected SKPoint GetPolarPoint(SKPoint center, float angle, float radius) =>
        _renderingHelpers.GetPolarPoint(center, angle, radius);

    protected float GetFrequencyMagnitude(
        float[] spectrum,
        float frequency,
        float sampleRate,
        int fftSize) =>
        _renderingHelpers.GetFrequencyMagnitude(spectrum, frequency, sampleRate, fftSize);

    protected SKColor[] CreateGradientColors(int count, SKColor startColor, SKColor endColor) =>
        _renderingHelpers.CreateGradientColors(count, startColor, endColor);

    protected float EaseInOut(float t) => _renderingHelpers.EaseInOut(t);
    protected float EaseIn(float t) => _renderingHelpers.EaseIn(t);
    protected float EaseOut(float t) => _renderingHelpers.EaseOut(t);

    protected SKPath CreateWavePath(
        float[] values,
        float width,
        float height,
        float offsetY = 0) =>
        _renderingHelpers.CreateWavePath(values, width, height, offsetY);

    protected bool IsRenderAreaVisible(
        SKCanvas? canvas,
        float x,
        float y,
        float width,
        float height) =>
        _renderingHelpers.IsAreaVisible(canvas, x, y, width, height);

    protected bool IsRectVisible(SKCanvas? canvas, SKRect rect) =>
        _renderingHelpers.IsAreaVisible(canvas, rect);

    protected virtual void RenderBackground(SKCanvas canvas, SKImageInfo info) =>
        _customBackgroundRenderer?.Invoke(canvas, info);

    protected virtual void ApplyPostProcessing(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info) =>
        _customPostProcessor?.Invoke(canvas, spectrum, info);

    public virtual void SetOverlayTransparency(float level)
    {
        _overlayStateManager.SetOverlayTransparency(level);
        _needsRedraw = true;
    }

    public virtual void Initialize() => SafeExecute(() =>
    {
        if (!_isInitialized)
        {
            _isInitialized = true;
            OnInitialize();
        }
    }, "Failed to initialize renderer");

    protected virtual void OnInitialize() { }

    public virtual void Configure(
        bool isOverlayActive,
        RenderQuality quality = RenderQuality.Medium) =>
        SafeExecute(() =>
        {
            bool overlayChanged = _overlayStateManager.IsOverlayActive != isOverlayActive;
            bool qualityChanged = _qualityManager.Quality != quality;

            _overlayStateManager.SetOverlayActive(isOverlayActive);
            _qualityManager.ApplyQuality(quality);
            _processingCoordinator.SetSmoothingFactor(isOverlayActive ? 0.5f : 0.3f);

            if (overlayChanged || qualityChanged)
            {
                LogDebug($"Configuration changed. New Quality: {quality}");
                _needsRedraw = true;
                OnConfigurationChanged();
            }
        }, "Failed to configure renderer");

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
            LogError("Renderer is not initialized");
            return false;
        }
        if (canvas == null || spectrum == null || spectrum.Length == 0 || paint == null)
            return false;
        if (info.Width <= 0 || info.Height <= 0 || barCount <= 0)
            return false;
        if (_disposed)
        {
            LogError("Renderer is disposed");
            return false;
        }
        return true;
    }

    private void PerformPeriodicCleanup(object? state)
    {
        if (_disposed) return;

        SafeExecute(() =>
        {
            CleanupUnusedResources();
            var memoryMb = GC.GetTotalMemory(false) / 1024 / 1024;
            if (memoryMb > HIGH_MEMORY_THRESHOLD_MB)
            {
                GC.Collect(1, GCCollectionMode.Optimized);
                LogDebug($"Performed GC cleanup, memory was {memoryMb}MB");
            }
        }, "Periodic cleanup error");
    }

    protected virtual void CleanupUnusedResources() { }

    public virtual void Dispose() => SafeExecute(() =>
    {
        if (!_disposed)
        {
            _cleanupTimer?.Dispose();
            _processingCoordinator?.Dispose();
            OnDispose();
            _disposed = true;
            LogDebug("Disposed");
        }
    }, "Error during base disposal");

    protected virtual void OnDispose() { }
}