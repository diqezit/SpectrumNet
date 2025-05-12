#nullable enable

using static SpectrumNet.Views.Renderers.KenwoodRenderer.Constants;
using static SpectrumNet.Views.Renderers.KenwoodRenderer.Constants.Quality;
using static System.MathF;

namespace SpectrumNet.Views.Renderers;

public sealed class KenwoodRenderer : EffectSpectrumRenderer
{
    private static readonly Lazy<KenwoodRenderer> _instance =
        new(() => new KenwoodRenderer());

    private KenwoodRenderer() { }

    public static KenwoodRenderer GetInstance() => _instance.Value;

    public record Constants
    {
        public const string LOG_PREFIX = "KenwoodRenderer";

        public const float
            ANIMATION_SPEED = 0.85f,
            PEAK_FALL_SPEED = 0.007f,
            PEAK_HOLD_TIME_MS = 500f,
            SPECTRUM_WEIGHT = 0.3f,
            BOOST_FACTOR = 0.3f,
            PEAK_HEIGHT = 3f,
            OVERLAY_ALPHA_FACTOR = 0.8f,
            OVERLAY_SMOOTHING_FACTOR = 0.5f,
            NORMAL_SMOOTHING_FACTOR = 0.3f,
            OVERLAY_TRANSITION_SMOOTHNESS = 0.7f,
            NORMAL_TRANSITION_SMOOTHNESS = 0.5f,
            OVERLAY_RENDERER_SMOOTHING_FACTOR = 0.6f,
            NORMAL_RENDERER_SMOOTHING_FACTOR_MULTIPLIER = 0.2f;

        public const int
            MAX_BUFFER_POOL_SIZE = 12,
            INITIAL_BUFFER_SIZE = 1024,
            BATCH_SIZE = 64,
            MAX_PARALLEL_THREADS = 2,
            HIGH_RENDER_STEP_THRESHOLD = 120,
            BATCH_SIZE_LARGE = 128,
            SIMD_THRESHOLD = 8,
            SMALL_BAR_COUNT_THRESHOLD = 32,
            SPECTRUM_HALF_DIVIDER = 2,
            DISPOSE_WAIT_TIMEOUT = 500,
            PARALLEL_THRESHOLD = 128,
            SMALL_BUFFER_COUNT = 4;

        public const float
            MIN_VISIBLE_VALUE = 1.0f,
            VELOCITY_DAMPING = 0.8f,
            SPRING_STIFFNESS = 0.2f,
            MAX_CHANGE_THRESHOLD = 0.3f,
            CHANGE_RATIO_DIVISOR = 0.7f,
            SMOOTH_FACTOR_MIN_MULTIPLIER = 0.3f,
            SMOOTH_FACTOR_DELTA_MULTIPLIER = 0.5f,
            SIMPLIFIED_BAR_PEAK_FALL_RATE = 2.0f,
            SHADOW_OFFSET_HIGH = 2.0f,
            SHADOW_OFFSET_MEDIUM = 1.5f,
            MIN_DELTA_THRESHOLD = 0.001f,
            MIN_CANVAS_HEIGHT = 5f,
            CANVAS_SIZE_CHANGE_THRESHOLD = 0.5f,
            MIN_BAR_COUNT = 2,
            PEAK_ANIMATION_THRESHOLD = 0.5f,
            SMALL_BAR_VALUE_MIN = 0f,
            SMALL_BAR_VALUE_MAX = 1f,
            CALCULATION_SLEEP_MS = 100f,
            CALCULATION_WAIT_TIME_MS = 50;

        public const int
            MAX_BAR_COUNT_MEDIUM_HIGH = 150,
            MAX_BAR_COUNT_HIGH_OVERLAY = 125,
            MAX_BAR_COUNT_MEDIUM_OVERLAY = 150;

        public static class Quality
        {
            public const bool
                LOW_USE_ANTIALIASING = false,
                MEDIUM_USE_ANTIALIASING = true,
                HIGH_USE_ANTIALIASING = true;

            public const bool
                LOW_USE_ADVANCED_EFFECTS = false,
                MEDIUM_USE_ADVANCED_EFFECTS = true,
                HIGH_USE_ADVANCED_EFFECTS = true;

            public const float
                LOW_SMOOTHING_FACTOR = 0.3f,
                MEDIUM_SMOOTHING_FACTOR = 0.8f,
                HIGH_SMOOTHING_FACTOR = 1.0f;

            public const float
                LOW_TRANSITION_SMOOTHNESS = 0.5f,
                MEDIUM_TRANSITION_SMOOTHNESS = 0.7f,
                HIGH_TRANSITION_SMOOTHNESS = 0.8f;

            public const float
                MEDIUM_GLOW_RADIUS = 2.0f,
                MEDIUM_GLOW_INTENSITY = 0.3f,
                MEDIUM_SHADOW_BLUR = 2.0f,
                MEDIUM_SHADOW_OPACITY = 0.4f;

            public const float
                HIGH_GLOW_RADIUS = 3.0f,
                HIGH_GLOW_INTENSITY = 0.4f,
                HIGH_SHADOW_BLUR = 3.0f,
                HIGH_SHADOW_OPACITY = 0.5f,
                HIGH_BAR_BLUR = 0.5f;

            public const bool
                LOW_USE_SHADOWS = false,
                MEDIUM_USE_SHADOWS = true,
                HIGH_USE_SHADOWS = true;

            public const bool
                LOW_USE_GLOW = false,
                MEDIUM_USE_GLOW = true,
                HIGH_USE_GLOW = true;

            public const bool
                LOW_USE_BAR_BLUR = false,
                MEDIUM_USE_BAR_BLUR = false,
                HIGH_USE_BAR_BLUR = true;
        }

        public const RenderQuality DEFAULT_QUALITY = RenderQuality.Medium;
    }

    private bool
        _useShadows,
        _useGlow,
        _useBarBlur;

    private float
        _glowRadius,
        _glowIntensity,
        _shadowBlur,
        _shadowOpacity,
        _barBlur,
        _rendererSmoothingFactor,
        _transitionSmoothness,
        _overlayAlphaFactor = 1.0f;

    private float
        _lastCanvasHeight,
        _pendingCanvasHeight;

    private readonly float
        _animationSpeed = ANIMATION_SPEED,
        _peakFallSpeed = PEAK_FALL_SPEED,
        _peakHoldTime = PEAK_HOLD_TIME_MS,
        _peakHeight = PEAK_HEIGHT;

    private int
        _currentBarCount,
        _lastRenderCount,
        _pendingBarCount;

    private float[]?
        _previousSpectrumBuffer,
        _peaks,
        _renderBarValues,
        _renderPeaks,
        _processingBarValues,
        _processingPeaks,
        _pendingSpectrum,
        _velocities;

    private DateTime[]? _peakHoldTimes;

    private SKPath? _cachedBarPath, _cachedPeakPath, _cachedShadowPath;

    private readonly ConcurrentQueue<float[]> _floatBufferPool = new();
    private readonly ConcurrentQueue<DateTime[]> _dateTimeBufferPool = new();

    private readonly SemaphoreSlim _dataSemaphore = new(1, 1);
    private readonly AutoResetEvent _dataAvailableEvent = new(false);

    private SKShader? _barGradient;

    private readonly SKPaint _peakPaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Fill,
        Color = SKColors.White
    };

    private readonly SKPaint _shadowPaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Fill,
        Color = SKColors.Black
    };

    private readonly SKPaint _glowPaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Fill
    };

    private static readonly SKColor[] _barColors = [
        new(0, 230, 120, 255), new(0, 255, 0, 255),
        new(255, 230, 0, 255), new(255, 180, 0, 255),
        new(255, 80, 0, 255), new(255, 30, 0, 255)
    ];

    private static readonly float[] _barColorPositions =
        [0f, 0.6f, 0.6f, 0.85f, 0.85f, 1f];

    private Task? _calculationTask;
    private CancellationTokenSource? _calculationCts;

    private volatile bool
        _buffersInitialized,
        _isConfiguring,
        _pathsNeedRebuild = true,
        _overlayStateChanged,
        _overlayStateChangeRequested;

    protected override void OnInitialize()
    {
        ExecuteSafely(
            InitializeComponents,
            nameof(OnInitialize),
            "Failed to initialize renderer resources"
        );
    }

    private void InitializeComponents()
    {
        base.OnInitialize();
        InitializeBufferPools();
        InitializePaths();
        InitializeBuffers(INITIAL_BUFFER_SIZE);
        InitializeQualityParams();
        StartCalculationThread();
        Log(LogLevel.Debug, LOG_PREFIX, "Initialized");
    }

    private void InitializeQualityParams()
    {
        ExecuteSafely(
            ApplyQualitySettingsInternal,
            nameof(InitializeQualityParams),
            "Failed to initialize quality parameters"
        );
    }

    public override void Configure(
        bool isOverlayActive,
        RenderQuality quality)
    {
        ExecuteSafely(
            () => SafeConfigure(isOverlayActive, quality),
            nameof(Configure),
            "Failed to configure renderer"
        );
    }

    private void SafeConfigure(
        bool isOverlayActive,
        RenderQuality quality)
    {
        if (_isConfiguring) return;

        try
        {
            _isConfiguring = true;
            bool configChanged = CheckConfigurationChange(isOverlayActive, quality);
            bool overlayChanged = _isOverlayActive != isOverlayActive;

            ApplyConfiguration(isOverlayActive, quality);

            if (overlayChanged)
            {
                _overlayStateChangeRequested = true;
                _overlayStateChanged = true;
                _overlayAlphaFactor = isOverlayActive ? OVERLAY_ALPHA_FACTOR : 1.0f;
                Log(LogLevel.Information, LOG_PREFIX,
                    $"Overlay state changed from {!isOverlayActive} to {isOverlayActive}");
            }

            if (configChanged)
            {
                ApplyQualitySettingsInternal();
                OnConfigurationChanged();
            }
        }
        finally
        {
            _isConfiguring = false;
        }
    }

    private bool CheckConfigurationChange(
        bool isOverlayActive,
        RenderQuality quality) =>
        _isOverlayActive != isOverlayActive || Quality != quality;

    private void ApplyConfiguration(
        bool isOverlayActive,
        RenderQuality quality)
    {
        _isOverlayActive = isOverlayActive;
        Quality = quality;
        base._smoothingFactor = isOverlayActive ? OVERLAY_SMOOTHING_FACTOR : NORMAL_SMOOTHING_FACTOR;
        _rendererSmoothingFactor = CalculateRendererSmoothingFactor(isOverlayActive);
        _transitionSmoothness = isOverlayActive ? OVERLAY_TRANSITION_SMOOTHNESS : NORMAL_TRANSITION_SMOOTHNESS;
    }

    private static float CalculateRendererSmoothingFactor(bool isOverlayActive) =>
        isOverlayActive ? OVERLAY_RENDERER_SMOOTHING_FACTOR :
                         (MEDIUM_SMOOTHING_FACTOR * NORMAL_RENDERER_SMOOTHING_FACTOR_MULTIPLIER);

    protected override void OnConfigurationChanged()
    {
        ExecuteSafely(
            LogConfigurationChange,
            nameof(OnConfigurationChanged),
            "Failed to apply configuration changes"
        );
    }

    private void LogConfigurationChange()
    {
        Log(LogLevel.Information,
            LOG_PREFIX,
            $"Configuration changed. Quality: {Quality}, " +
            $"Overlay: {_isOverlayActive}, " +
            $"Alpha: {_overlayAlphaFactor}");
    }

    protected override void ApplyQualitySettings()
    {
        ExecuteSafely(
            SafeApplyQualitySettings,
            nameof(ApplyQualitySettings),
            "Failed to apply quality settings"
        );
    }

    private void SafeApplyQualitySettings()
    {
        if (_isConfiguring) return;

        try
        {
            _isConfiguring = true;
            base.ApplyQualitySettings();
            ApplyQualitySettingsInternal();
            _overlayStateChanged = true;
        }
        finally
        {
            _isConfiguring = false;
        }
    }

    private void ApplyQualitySettingsInternal()
    {
        switch (Quality)
        {
            case RenderQuality.Low:
                ApplyLowQuality();
                break;

            case RenderQuality.Medium:
                ApplyMediumQuality();
                break;

            case RenderQuality.High:
                ApplyHighQuality();
                break;
        }

        UpdatePaintProperties();
        LogQualitySettings();
    }

    private void ApplyLowQuality()
    {
        _useAntiAlias = LOW_USE_ANTIALIASING;
        _useAdvancedEffects = LOW_USE_ADVANCED_EFFECTS;
        _useShadows = LOW_USE_SHADOWS;
        _useGlow = LOW_USE_GLOW;
        _useBarBlur = LOW_USE_BAR_BLUR;
        _rendererSmoothingFactor = LOW_SMOOTHING_FACTOR;
        _transitionSmoothness = LOW_TRANSITION_SMOOTHNESS;
    }

    private void ApplyMediumQuality()
    {
        _useAntiAlias = MEDIUM_USE_ANTIALIASING;
        _useAdvancedEffects = MEDIUM_USE_ADVANCED_EFFECTS;
        _useShadows = MEDIUM_USE_SHADOWS;
        _useGlow = MEDIUM_USE_GLOW;
        _useBarBlur = MEDIUM_USE_BAR_BLUR;
        SetMediumQualityEffects();
        _rendererSmoothingFactor = MEDIUM_SMOOTHING_FACTOR;
        _transitionSmoothness = MEDIUM_TRANSITION_SMOOTHNESS;
    }

    private void SetMediumQualityEffects()
    {
        _glowRadius = MEDIUM_GLOW_RADIUS;
        _glowIntensity = MEDIUM_GLOW_INTENSITY;
        _shadowBlur = MEDIUM_SHADOW_BLUR;
        _shadowOpacity = MEDIUM_SHADOW_OPACITY;
    }

    private void ApplyHighQuality()
    {
        _useAntiAlias = HIGH_USE_ANTIALIASING;
        _useAdvancedEffects = HIGH_USE_ADVANCED_EFFECTS;
        _useShadows = HIGH_USE_SHADOWS;
        _useGlow = HIGH_USE_GLOW;
        _useBarBlur = HIGH_USE_BAR_BLUR;
        SetHighQualityEffects();
        _rendererSmoothingFactor = HIGH_SMOOTHING_FACTOR;
        _transitionSmoothness = HIGH_TRANSITION_SMOOTHNESS;
    }

    private void SetHighQualityEffects()
    {
        _glowRadius = HIGH_GLOW_RADIUS;
        _glowIntensity = HIGH_GLOW_INTENSITY;
        _shadowBlur = HIGH_SHADOW_BLUR;
        _shadowOpacity = HIGH_SHADOW_OPACITY;
        _barBlur = HIGH_BAR_BLUR;
    }

    private void LogQualitySettings()
    {
        Log(LogLevel.Debug, LOG_PREFIX,
            $"Quality settings applied. Quality: {Quality}, " +
            $"AntiAlias: {UseAntiAlias}, AdvancedEffects: {UseAdvancedEffects}, " +
            $"Shadows: {_useShadows}, Glow: {_useGlow}, BarBlur: {_useBarBlur}");
    }

    private int LimitBarCount(int barCount)
    {
        if (Quality == RenderQuality.Low)
            return barCount;

        if (_isOverlayActive)
        {
            return Quality == RenderQuality.High
                ? Math.Min(barCount, MAX_BAR_COUNT_HIGH_OVERLAY)
                : Math.Min(barCount, MAX_BAR_COUNT_MEDIUM_OVERLAY);
        }

        return Math.Min(barCount, MAX_BAR_COUNT_MEDIUM_HIGH);
    }

    private static float CalculateBarWidthAdjustment(int requestedBarCount, int limitedBarCount)
    {
        if (requestedBarCount <= limitedBarCount)
            return 1.0f;

        return (float)requestedBarCount / limitedBarCount;
    }

    protected override void RenderEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint paint)
    {
        if (!ValidateRenderParameters(canvas, spectrum, info, barCount))
            return;

        ExecuteSafely(
            () => RenderEffectInternal(canvas, spectrum, info, barWidth, barSpacing, barCount),
            nameof(RenderEffect),
            "Error during rendering"
        );
    }

    private void RenderEffectInternal(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount)
    {
        int limitedBarCount = LimitBarCount(barCount);
        float widthAdjustment = CalculateBarWidthAdjustment(barCount, limitedBarCount);

        float adjustedBarWidth = barWidth * widthAdjustment;
        float adjustedBarSpacing = barSpacing * widthAdjustment;

        if (_overlayStateChangeRequested)
        {
            _overlayStateChangeRequested = false;
            _overlayStateChanged = true;
        }

        ExecuteRender(canvas, spectrum, info, adjustedBarWidth, adjustedBarSpacing, limitedBarCount);

        if (_overlayStateChanged)
        {
            _overlayStateChanged = false;
        }
    }

    private void ExecuteRender(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount)
    {
        float totalBarWidth = barWidth + barSpacing;
        UpdateState(canvas, spectrum, info, barCount);
        RenderFrame(canvas, info, barWidth, barSpacing, totalBarWidth);
    }

    protected override void OnInvalidateCachedResources()
    {
        ExecuteSafely(
            () => {
                _overlayStateChanged = true;
                LogResourceInvalidation();
            },
            nameof(OnInvalidateCachedResources),
            "Failed to invalidate cached resources"
        );
    }

    private static bool LogResourceInvalidation()
    {
        Log(LogLevel.Debug, LOG_PREFIX, "Cached resources invalidated");
        return true;
    }

    protected override void OnDispose()
    {
        ExecuteSafely(
            DisposeComponents,
            nameof(OnDispose),
            "Error during disposal"
        );
    }

    private void DisposeComponents()
    {
        DisposeThreadResources();
        DisposeSynchronizationObjects();
        ReturnAllBuffers();
        ClearBufferPools();
        DisposePaintsAndPaths();
        DisposeShaders();
        base.OnDispose();
        Log(LogLevel.Debug, LOG_PREFIX, "Disposed");
    }

    private void InitializeBufferPools()
    {
        for (int i = 0; i < SMALL_BUFFER_COUNT; i++)
        {
            _floatBufferPool.Enqueue(new float[INITIAL_BUFFER_SIZE]);
            _dateTimeBufferPool.Enqueue(new DateTime[INITIAL_BUFFER_SIZE]);
        }
    }

    private void InitializeBuffers(int size)
    {
        _previousSpectrumBuffer = new float[size];
        _peaks = new float[size];
        _peakHoldTimes = new DateTime[size];
        _velocities = new float[size];
        _renderBarValues = new float[size];
        _renderPeaks = new float[size];
        _processingBarValues = new float[size];
        _processingPeaks = new float[size];
        FillArraysWithDefaults();
        _buffersInitialized = true;
    }

    private void FillArraysWithDefaults()
    {
        if (_peakHoldTimes != null)
            Array.Fill(_peakHoldTimes, DateTime.MinValue);
        if (_velocities != null)
            Array.Fill(_velocities, 0f);
    }

    private void InitializePaths()
    {
        _cachedBarPath = new SKPath();
        _cachedPeakPath = new SKPath();
        _cachedShadowPath = new SKPath();
    }

    private void StartCalculationThread()
    {
        _calculationCts = new CancellationTokenSource();
        _calculationTask = Task.Factory.StartNew(
            () => CalculationThreadMain(_calculationCts.Token),
            _calculationCts.Token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    private void UpdatePaintProperties()
    {
        _peakPaint.IsAntialias = UseAntiAlias;
        _shadowPaint.IsAntialias = UseAntiAlias;
        _glowPaint.IsAntialias = UseAntiAlias;
    }

    private static bool ValidateRenderParameters(
        SKCanvas? canvas,
        float[]? spectrum,
        SKImageInfo info,
        int barCount)
    {
        if (canvas == null || canvas.QuickReject(new SKRect(0, 0, info.Width, info.Height)))
            return false;

        if (spectrum == null || spectrum.Length == 0)
            return false;

        if (info.Width <= 0 || info.Height <= 0)
            return false;

        if (barCount <= 0)
            return false;

        return true;
    }

    private void UpdateState(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        int barCount)
    {
        ProcessCanvasChanges(info, canvas);
        SubmitSpectrumData(spectrum, barCount, info.Height);
    }

    private void RenderFrame(
        SKCanvas canvas,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        float totalBarWidth)
    {
        if (!CheckRenderValidity())
            return;

        PreparePaths(info, barWidth, barSpacing, totalBarWidth);

        if (_isOverlayActive)
        {
            RenderInOverlayMode(canvas);
        }
        else
        {
            RenderInStandardMode(canvas);
        }
    }

    private void RenderInStandardMode(SKCanvas canvas)
    {
        if (ShouldRenderShadows())
            RenderShadows(canvas);

        RenderBars(canvas);

        if (ShouldRenderGlow())
            RenderGlow(canvas);

        RenderPeaks(canvas);
    }

    private bool ShouldRenderShadows() =>
        _useShadows &&
        _cachedBarPath != null &&
        !_cachedBarPath.IsEmpty &&
        _cachedShadowPath != null;

    private bool ShouldRenderGlow() =>
        _useGlow &&
        _cachedBarPath != null &&
        !_cachedBarPath.IsEmpty;

    private void RenderShadows(SKCanvas canvas)
    {
        using var shadowPaint = GetShadowPaint();
        canvas.DrawPath(_cachedShadowPath!, shadowPaint);
    }

    private SKPaint GetShadowPaint()
    {
        var shadowPaint = _paintPool.Get();
        shadowPaint.Color = SKColors.Black.WithAlpha((byte)(_shadowOpacity * 255));
        shadowPaint.IsAntialias = UseAntiAlias;

        if (_useAdvancedEffects)
            shadowPaint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, _shadowBlur);

        return shadowPaint;
    }

    private void RenderGlow(SKCanvas canvas)
    {
        using var glowPaint = GetGlowPaint();
        canvas.DrawPath(_cachedBarPath!, glowPaint);
    }

    private SKPaint GetGlowPaint()
    {
        var glowPaint = _paintPool.Get();
        glowPaint.Color = SKColors.White.WithAlpha((byte)(_glowIntensity * 255));
        glowPaint.IsAntialias = UseAntiAlias;
        glowPaint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, _glowRadius);
        glowPaint.Shader = _barGradient;
        return glowPaint;
    }

    private bool CheckRenderValidity() =>
        _renderBarValues != null &&
        _renderPeaks != null &&
        _currentBarCount > 0 &&
        _renderBarValues.Length > 0;

    private void PreparePaths(
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        float totalBarWidth)
    {
        int renderCount = Min(_currentBarCount, _renderBarValues?.Length ?? 0);
        if (renderCount <= 0)
            return;

        UpdatePathsIfNeeded(info, barWidth, barSpacing, totalBarWidth, renderCount);
    }

    private void RenderInOverlayMode(SKCanvas canvas)
    {
        using var overlayPaint = new SKPaint
        {
            Color = new SKColor(255, 255, 255, (byte)(255 * _overlayAlphaFactor))
        };

        canvas.SaveLayer(overlayPaint);
        try
        {
            RenderBars(canvas);
            RenderPeaks(canvas);
        }
        finally
        {
            canvas.Restore();
        }
    }

    private void UpdatePathsIfNeeded(
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        float totalBarWidth,
        int renderCount)
    {
        if (_pathsNeedRebuild || _lastRenderCount != renderCount)
        {
            UpdateRenderingPaths(info, barWidth, barSpacing, totalBarWidth, renderCount);
            _lastRenderCount = renderCount;
            _pathsNeedRebuild = false;
        }
    }

    private void ProcessCanvasChanges(
        SKImageInfo info,
        SKCanvas canvas)
    {
        bool canvasSizeChanged =
            MathF.Abs(_lastCanvasHeight - info.Height) > CANVAS_SIZE_CHANGE_THRESHOLD;

        if (canvasSizeChanged)
        {
            CreateGradients(info.Height);
            _lastCanvasHeight = info.Height;
        }
    }

    private void SubmitSpectrumData(
        float[] spectrum,
        int barCount,
        float canvasHeight)
    {
        _dataSemaphore.Wait();
        try
        {
            UpdatePendingData(spectrum, barCount, canvasHeight);
            SwapBuffersIfReady();
        }
        finally
        {
            _dataSemaphore.Release();
            _dataAvailableEvent.Set();
        }
    }

    private void UpdatePendingData(
        float[] spectrum,
        int barCount,
        float canvasHeight)
    {
        _pendingSpectrum = spectrum;
        _pendingBarCount = barCount;
        _pendingCanvasHeight = canvasHeight;
    }

    private void SwapBuffersIfReady()
    {
        if (_processingBarValues != null && _renderBarValues != null &&
            _processingPeaks != null && _renderPeaks != null &&
            _processingBarValues.Length == _renderBarValues.Length)
        {
            (_renderBarValues, _processingBarValues) =
                (_processingBarValues, _renderBarValues);
            (_renderPeaks, _processingPeaks) =
                (_processingPeaks, _renderPeaks);
        }
    }

    private void CalculationThreadMain(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_dataAvailableEvent.WaitOne((int)CALCULATION_WAIT_TIME_MS))
                    continue;

                if (ct.IsCancellationRequested)
                    break;

                var (data, barCount, canvasHeight) = ExtractPendingSpectrumData(ct);

                if (data != null && data.Length > 0)
                    ProcessSpectrumData(data, barCount, canvasHeight);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                HandleCalculationError(ex);
            }
        }
    }

    private static void HandleCalculationError(Exception ex)
    {
        Log(LogLevel.Error, LOG_PREFIX, $"Calculation error: {ex.Message}");
        Thread.Sleep((int)CALCULATION_SLEEP_MS);
    }

    private (float[]? data, int barCount, int canvasHeight) ExtractPendingSpectrumData(
        CancellationToken ct)
    {
        float[]? spectrumData = null;
        int barCount = 0;
        int canvasHeight = 0;

        _dataSemaphore.Wait(ct);
        try
        {
            if (_pendingSpectrum != null)
            {
                spectrumData = _pendingSpectrum;
                barCount = _pendingBarCount;
                canvasHeight = (int)_pendingCanvasHeight;
                _pendingSpectrum = null;
            }
        }
        finally
        {
            _dataSemaphore.Release();
        }

        return (spectrumData, barCount, canvasHeight);
    }

    private void ProcessSpectrumData(
        float[] spectrum,
        int barCount,
        float canvasHeight)
    {
        int spectrumLength = spectrum.Length;
        int actualBarCount = Min(spectrumLength, barCount);

        InitializeBuffersIfNeeded(actualBarCount);

        var (scaledSpectrum, computedBarValues, computedPeaks) = PrepareBuffers(actualBarCount);

        ScaleSpectrumData(spectrum, scaledSpectrum, actualBarCount, spectrumLength);
        ProcessAnimation(scaledSpectrum, computedBarValues, computedPeaks, canvasHeight, actualBarCount);
        UpdateRenderingBuffers(computedBarValues, computedPeaks, actualBarCount);
        ReturnToPool(scaledSpectrum, computedBarValues, computedPeaks);

        _pathsNeedRebuild = true;
    }

    private (float[] scaled, float[] bars, float[] peaks) PrepareBuffers(int actualBarCount) => (
        GetFloatBuffer(actualBarCount),
        GetFloatBuffer(actualBarCount),
        GetFloatBuffer(actualBarCount)
    );

    private static void ScaleSpectrumData(
        float[] spectrum,
        float[] scaledSpectrum,
        int actualBarCount,
        int spectrumLength) =>
        ScaleSpectrum(spectrum, scaledSpectrum, actualBarCount, spectrumLength);

    private void ProcessAnimation(
        float[] scaledSpectrum,
        float[] computedBarValues,
        float[] computedPeaks,
        float canvasHeight,
        int actualBarCount)
    {
        DateTime currentTime = DateTime.Now;
        ProcessSpectrumAnimation(
            scaledSpectrum,
            computedBarValues,
            computedPeaks,
            currentTime,
            canvasHeight,
            actualBarCount);
    }

    private void ReturnToPool(
        float[] scaledSpectrum,
        float[] computedBarValues,
        float[] computedPeaks)
    {
        ReturnBufferToPool(scaledSpectrum);
        ReturnBufferToPool(computedBarValues);
        ReturnBufferToPool(computedPeaks);
    }

    private void UpdateRenderingPaths(
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        float totalBarWidth,
        int renderCount)
    {
        ExecuteSafely(
            () => RebuildPaths(info, barWidth, barSpacing, totalBarWidth, renderCount),
            nameof(UpdateRenderingPaths),
            "Failed to update rendering paths"
        );
    }

    private void RebuildPaths(
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        float totalBarWidth,
        int renderCount)
    {
        ResetAllPaths();
        BuildPaths(info, barWidth, barSpacing, totalBarWidth, renderCount);
    }

    private void BuildPaths(
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        float totalBarWidth,
        int renderCount)
    {
        if (!ValidatePathBuildingRequirements(renderCount))
            return;

        int step = CalculateRenderStep(renderCount);
        BuildVisibleBars(info, barWidth, barSpacing, renderCount, step);
    }

    private int CalculateRenderStep(int renderCount) =>
        Quality == RenderQuality.High && renderCount > HIGH_RENDER_STEP_THRESHOLD ? 2 : 1;

    private void BuildVisibleBars(
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int renderCount,
        int step)
    {
        for (int i = 0; i < renderCount; i += step)
        {
            if (i >= _renderBarValues!.Length)
                break;

            float x = i * (barWidth + barSpacing);
            float barValue = _renderBarValues[i];

            BuildBarPath(info, x, barWidth, barValue);
            BuildPeakPath(info, x, barWidth, i);
        }
    }

    private void BuildBarPath(
        SKImageInfo info,
        float x,
        float barWidth,
        float barValue)
    {
        if (barValue <= MIN_VISIBLE_VALUE)
            return;

        float barTop = info.Height - barValue;
        _cachedBarPath!.AddRect(SKRect.Create(x, barTop, barWidth, barValue));

        if (_useShadows && _cachedShadowPath != null)
        {
            float shadowOffset = Quality == RenderQuality.High ?
                SHADOW_OFFSET_HIGH : SHADOW_OFFSET_MEDIUM;
            _cachedShadowPath.AddRect(SKRect.Create(
                x + shadowOffset,
                barTop + shadowOffset,
                barWidth,
                barValue));
        }
    }

    private void BuildPeakPath(
        SKImageInfo info,
        float x,
        float barWidth,
        int index)
    {
        if (index >= _renderPeaks!.Length || _renderPeaks[index] <= MIN_VISIBLE_VALUE)
            return;

        float peakY = info.Height - _renderPeaks[index];
        _cachedPeakPath!.AddRect(SKRect.Create(
            x,
            peakY - _peakHeight,
            barWidth,
            _peakHeight));
    }

    private bool ValidatePathBuildingRequirements(int renderCount) =>
        _renderBarValues != null &&
        _renderPeaks != null &&
        _cachedBarPath != null &&
        _cachedPeakPath != null &&
        renderCount > 0 &&
        _renderBarValues.Length > 0;

    private void ResetAllPaths()
    {
        _cachedBarPath?.Reset();
        _cachedPeakPath?.Reset();
        _cachedShadowPath?.Reset();
    }

    private void RenderBars(SKCanvas canvas)
    {
        if (_cachedBarPath == null || _cachedBarPath.IsEmpty)
            return;

        using var barPaint = CreateBarPaint();
        canvas.DrawPath(_cachedBarPath, barPaint);
    }

    private SKPaint CreateBarPaint()
    {
        var barPaint = CreateStandardPaint(SKColors.White);
        barPaint.Shader = _barGradient;

        if (_useBarBlur && _useAdvancedEffects)
            barPaint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, _barBlur);

        return barPaint;
    }

    private void RenderPeaks(SKCanvas canvas)
    {
        if (_cachedPeakPath == null || _cachedPeakPath.IsEmpty)
            return;

        canvas.DrawPath(_cachedPeakPath, _peakPaint);
    }

    private void CreateGradients(float height)
    {
        ExecuteSafely(
            () => RebuildGradient(height),
            nameof(CreateGradients),
            "Failed to create gradients"
        );
    }

    private void RebuildGradient(float height)
    {
        DisposeGradients();

        _barGradient = SKShader.CreateLinearGradient(
            new SKPoint(0, height),
            new SKPoint(0, 0),
            _barColors,
            _barColorPositions,
            SKShaderTileMode.Clamp);
    }

    private void DisposeGradients() => _barGradient?.Dispose();

    private float[] GetFloatBuffer(int size)
    {
        if (_floatBufferPool.TryDequeue(out var buffer) && buffer.Length >= size)
            return buffer;
        return new float[Max(size, INITIAL_BUFFER_SIZE)];
    }

    private DateTime[] GetDateTimeBuffer(int size)
    {
        if (_dateTimeBufferPool.TryDequeue(out var buffer) && buffer.Length >= size)
            return buffer;
        return new DateTime[Max(size, INITIAL_BUFFER_SIZE)];
    }

    private void ReturnBufferToPool(float[]? buffer)
    {
        if (buffer != null && _floatBufferPool.Count < MAX_BUFFER_POOL_SIZE)
            _floatBufferPool.Enqueue(buffer);
    }

    private void ReturnBufferToPool(DateTime[]? buffer)
    {
        if (buffer != null && _dateTimeBufferPool.Count < MAX_BUFFER_POOL_SIZE)
            _dateTimeBufferPool.Enqueue(buffer);
    }

    private void InitializeBuffersIfNeeded(int actualBarCount)
    {
        if (!NeedBufferReinitialization(actualBarCount))
            return;

        Log(LogLevel.Debug, LOG_PREFIX, $"Reinitializing buffers for {actualBarCount} bars");
        _dataSemaphore.Wait();
        try
        {
            ReinitializeBuffers(actualBarCount);
        }
        finally
        {
            _dataSemaphore.Release();
        }
    }

    private bool NeedBufferReinitialization(int actualBarCount) =>
        !_buffersInitialized ||
        _previousSpectrumBuffer == null ||
        _velocities == null ||
        _peaks == null ||
        _peakHoldTimes == null ||
        _previousSpectrumBuffer.Length < actualBarCount;

    private void ReinitializeBuffers(int actualBarCount)
    {
        ReturnCurrentBuffers();
        AllocateNewBuffers(actualBarCount);
        InitializeNewBuffers();
        _buffersInitialized = true;
    }

    private void ReturnCurrentBuffers()
    {
        ReturnBufferToPool(_previousSpectrumBuffer);
        ReturnBufferToPool(_peaks);
        ReturnBufferToPool(_peakHoldTimes);
        ReturnBufferToPool(_velocities);
    }

    private void AllocateNewBuffers(int actualBarCount)
    {
        _previousSpectrumBuffer = GetFloatBuffer(actualBarCount);
        _peaks = GetFloatBuffer(actualBarCount);
        _peakHoldTimes = GetDateTimeBuffer(actualBarCount);
        _velocities = GetFloatBuffer(actualBarCount);
    }

    private void InitializeNewBuffers()
    {
        if (_peakHoldTimes != null)
            Array.Fill(_peakHoldTimes, DateTime.MinValue);
        if (_velocities != null)
            Array.Fill(_velocities, 0f);
    }

    private void ProcessSpectrumAnimation(
        float[] scaledSpectrum,
        float[] computedBarValues,
        float[] computedPeaks,
        DateTime currentTime,
        float canvasHeight,
        int actualBarCount)
    {
        if (!EnsureBuffersForAnimation(actualBarCount))
            return;

        if (canvasHeight < MIN_CANVAS_HEIGHT || actualBarCount < MIN_BAR_COUNT)
        {
            ProcessMinimalSpectrum(scaledSpectrum, computedBarValues, computedPeaks, canvasHeight, actualBarCount);
            return;
        }

        var animationParams = CalculateAnimationParameters(canvasHeight);
        var physicsParams = CalculatePhysicsParameters();

        ProcessSpectrumAnimationBatched(
            scaledSpectrum,
            computedBarValues,
            computedPeaks,
            currentTime,
            canvasHeight,
            actualBarCount,
            animationParams,
            physicsParams);
    }

    private void ProcessSpectrumAnimationBatched(
        float[] scaledSpectrum,
        float[] computedBarValues,
        float[] computedPeaks,
        DateTime currentTime,
        float canvasHeight,
        int actualBarCount,
        (float smoothFactor, float peakFallRate, double peakHoldTimeMs) animParams,
        (float maxChangeThreshold, float velocityDamping, float springStiffness) physicsParams)
    {
        int batchSize = CalculateBatchSize(actualBarCount);

        if (ShouldUseParallelProcessing(actualBarCount))
        {
            ProcessAnimationInParallel(
                scaledSpectrum,
                computedBarValues,
                computedPeaks,
                currentTime,
                canvasHeight,
                actualBarCount,
                animParams,
                physicsParams,
                batchSize);
        }
        else
        {
            ProcessBatchSequential(
                0,
                actualBarCount,
                scaledSpectrum,
                computedBarValues,
                computedPeaks,
                currentTime,
                canvasHeight,
                animParams,
                physicsParams);
        }
    }

    private static bool ShouldUseParallelProcessing(int actualBarCount) =>
        actualBarCount >= PARALLEL_THRESHOLD;

    private static int CalculateBatchSize(int actualBarCount) =>
        actualBarCount > 256 ? BATCH_SIZE_LARGE : Constants.BATCH_SIZE;

    private void ProcessAnimationInParallel(
        float[] scaledSpectrum,
        float[] computedBarValues,
        float[] computedPeaks,
        DateTime currentTime,
        float canvasHeight,
        int actualBarCount,
        (float smoothFactor, float peakFallRate, double peakHoldTimeMs) animParams,
        (float maxChangeThreshold, float velocityDamping, float springStiffness) physicsParams,
        int batchSize)
    {
        Parallel.For(0, (actualBarCount + batchSize - 1) / batchSize,
            new ParallelOptions { MaxDegreeOfParallelism = MAX_PARALLEL_THREADS },
            batchIndex =>
            {
                int start = batchIndex * batchSize;
                int end = Math.Min(start + batchSize, actualBarCount);

                ProcessBatchSequential(
                    start,
                    end,
                    scaledSpectrum,
                    computedBarValues,
                    computedPeaks,
                    currentTime,
                    canvasHeight,
                    animParams,
                    physicsParams);
            });
    }

    private static void ProcessMinimalSpectrum(
        float[] scaledSpectrum,
        float[] computedBarValues,
        float[] computedPeaks,
        float canvasHeight,
        int actualBarCount)
    {
        for (int i = 0; i < actualBarCount; i++)
        {
            if (i >= scaledSpectrum.Length
                || i >= computedBarValues.Length
                || i >= computedPeaks.Length)
                continue;

            computedBarValues[i] = scaledSpectrum[i] * canvasHeight;
            computedPeaks[i] = scaledSpectrum[i] * canvasHeight;
        }
    }

    private bool EnsureBuffersForAnimation(int actualBarCount)
    {
        if (!_buffersInitialized
            || _velocities == null
            || _previousSpectrumBuffer == null
            || _peaks == null
            || _peakHoldTimes == null)
        {
            InitializeBuffersIfNeeded(actualBarCount);
            return _buffersInitialized;
        }
        return true;
    }

    private (float smoothFactor, float peakFallRate, double peakHoldTimeMs)
        CalculateAnimationParameters(float canvasHeight) => (
            _rendererSmoothingFactor * _animationSpeed,
            _peakFallSpeed * canvasHeight * _animationSpeed,
            _peakHoldTime
        );

    private (float maxChangeThreshold, float velocityDamping, float springStiffness)
        CalculatePhysicsParameters() => (
            MAX_CHANGE_THRESHOLD,
            VELOCITY_DAMPING * _transitionSmoothness,
            SPRING_STIFFNESS * (1 - _transitionSmoothness)
        );

    private void ProcessBatchSequential(
        int batchStart,
        int batchEnd,
        float[] scaledSpectrum,
        float[] computedBarValues,
        float[] computedPeaks,
        DateTime currentTime,
        float canvasHeight,
        (float smoothFactor, float peakFallRate, double peakHoldTimeMs) animParams,
        (float maxChangeThreshold, float velocityDamping, float springStiffness) physicsParams)
    {
        if (!ValidateAnimationBuffers())
            return;

        var prevSpectrumBuffer = _previousSpectrumBuffer!;
        var velocities = _velocities!;
        var peaks = _peaks!;
        var peakHoldTimes = _peakHoldTimes!;

        float velocityDamping = physicsParams.velocityDamping;
        float springStiffness = physicsParams.springStiffness;
        float smoothFactor = animParams.smoothFactor;
        float maxChangeThreshold = physicsParams.maxChangeThreshold;
        float peakFallRate = animParams.peakFallRate;
        double peakHoldTimeMs = animParams.peakHoldTimeMs;

        for (int i = batchStart; i < batchEnd; i++)
        {
            if (i >= scaledSpectrum.Length || i >= prevSpectrumBuffer.Length ||
                i >= computedBarValues.Length)
                continue;

            float targetValue = scaledSpectrum[i];

            if (targetValue * canvasHeight < PEAK_ANIMATION_THRESHOLD)
            {
                ProcessSimplifiedBar(i, targetValue, computedBarValues, computedPeaks,
                    canvasHeight, peaks);
                continue;
            }

            float currentValue = prevSpectrumBuffer[i];
            float delta = targetValue - currentValue;

            if (MathF.Abs(delta) < MIN_DELTA_THRESHOLD)
            {
                ProcessStaticBar(i, currentValue, computedBarValues, computedPeaks,
                    currentTime, canvasHeight, peakFallRate, peakHoldTimeMs, peaks, peakHoldTimes);
                continue;
            }

            ProcessAnimatedBar(
                i, delta, targetValue, currentValue,
                computedBarValues, computedPeaks,
                currentTime, canvasHeight,
                smoothFactor, maxChangeThreshold, velocityDamping, springStiffness,
                peakFallRate, peakHoldTimeMs,
                prevSpectrumBuffer, velocities, peaks, peakHoldTimes);
        }
    }

    private void ProcessSimplifiedBar(
        int i,
        float targetValue,
        float[] computedBarValues,
        float[] computedPeaks,
        float canvasHeight,
        float[] peaks)
    {
        _previousSpectrumBuffer![i] = targetValue;
        computedBarValues[i] = targetValue * canvasHeight;

        if (peaks != null && i < peaks.Length)
        {
            peaks[i] = MathF.Max(0, peaks[i] - SIMPLIFIED_BAR_PEAK_FALL_RATE);
            computedPeaks[i] = peaks[i];
        }
    }

    private static void ProcessStaticBar(
        int i,
        float currentValue,
        float[] computedBarValues,
        float[] computedPeaks,
        DateTime currentTime,
        float canvasHeight,
        float peakFallRate,
        double peakHoldTimeMs,
        float[] peaks,
        DateTime[] peakHoldTimes)
    {
        computedBarValues[i] = currentValue * canvasHeight;
        UpdatePeak(i, computedBarValues[i], computedPeaks, currentTime,
            peakFallRate, peakHoldTimeMs, peaks, peakHoldTimes);
    }

    private static void ProcessAnimatedBar(
        int i,
        float delta,
        float targetValue,
        float currentValue,
        float[] computedBarValues,
        float[] computedPeaks,
        DateTime currentTime,
        float canvasHeight,
        float smoothFactor,
        float maxChangeThreshold,
        float velocityDamping,
        float springStiffness,
        float peakFallRate,
        double peakHoldTimeMs,
        float[] prevSpectrumBuffer,
        float[] velocities,
        float[] peaks,
        DateTime[] peakHoldTimes)
    {
        float adaptiveSmoothFactor = CalculateAdaptiveSmoothFactor(
            delta, canvasHeight, smoothFactor, maxChangeThreshold);

        UpdateBarVelocity(i, delta, velocityDamping, springStiffness, velocities);

        float newValue = prevSpectrumBuffer[i] + velocities[i] + delta * adaptiveSmoothFactor;
        prevSpectrumBuffer[i] = MathF.Max(SMALL_BAR_VALUE_MIN, MathF.Min(SMALL_BAR_VALUE_MAX, newValue));

        computedBarValues[i] = prevSpectrumBuffer[i] * canvasHeight;
        UpdatePeak(i, computedBarValues[i], computedPeaks, currentTime,
            peakFallRate, peakHoldTimeMs, peaks, peakHoldTimes);
    }

    private static void UpdateBarVelocity(
        int i,
        float delta,
        float velocityDamping,
        float springStiffness,
        float[] velocities)
    {
        velocities[i] = velocities[i] * velocityDamping + delta * springStiffness;
    }

    private static float CalculateAdaptiveSmoothFactor(
        float delta,
        float canvasHeight,
        float smoothFactor,
        float maxChangeThreshold)
    {
        float absDelta = MathF.Abs(delta * canvasHeight);

        if (absDelta > maxChangeThreshold)
        {
            float changeRatio = MathF.Min(1.0f, absDelta / (canvasHeight * CHANGE_RATIO_DIVISOR));
            return MathF.Max(
                smoothFactor * SMOOTH_FACTOR_MIN_MULTIPLIER,
                smoothFactor * (1.0f + changeRatio * SMOOTH_FACTOR_DELTA_MULTIPLIER));
        }

        return smoothFactor;
    }

    private static void UpdatePeak(
        int i,
        float barValue,
        float[] computedPeaks,
        DateTime currentTime,
        float peakFallRate,
        double peakHoldTimeMs,
        float[] peaks,
        DateTime[] peakHoldTimes)
    {
        float currentPeak = peaks[i];

        if (barValue > currentPeak)
        {
            SetNewPeak(i, barValue, currentTime, peaks, peakHoldTimes);
        }
        else if (IsPeakHoldTimeElapsed(i, currentTime, peakHoldTimeMs, peakHoldTimes))
        {
            UpdateFallingPeak(i, barValue, peakFallRate, peaks);
        }

        computedPeaks[i] = peaks[i];
    }

    private static bool IsPeakHoldTimeElapsed(
        int i,
        DateTime currentTime,
        double peakHoldTimeMs,
        DateTime[] peakHoldTimes) =>
        (currentTime - peakHoldTimes[i]).TotalMilliseconds > peakHoldTimeMs;

    private static void SetNewPeak(
        int i,
        float barValue,
        DateTime currentTime,
        float[] peaks,
        DateTime[] peakHoldTimes)
    {
        peaks[i] = barValue;
        peakHoldTimes[i] = currentTime;
    }

    private static void UpdateFallingPeak(
        int i,
        float barValue,
        float peakFallRate,
        float[] peaks)
    {
        float currentPeak = peaks[i];
        float newPeak = currentPeak - peakFallRate;
        peaks[i] = MathF.Max(barValue, newPeak);
    }

    private bool ValidateAnimationBuffers() =>
        _velocities != null &&
        _previousSpectrumBuffer != null &&
        _peaks != null &&
        _peakHoldTimes != null;

    private void UpdateRenderingBuffers(
        float[] computedBarValues,
        float[] computedPeaks,
        int actualBarCount)
    {
        _dataSemaphore.Wait();
        try
        {
            EnsureProcessingBuffers(actualBarCount);
            CopyToProcessingBuffers(computedBarValues, computedPeaks, actualBarCount);
            SwapProcessingAndRenderingBuffers(actualBarCount);
            _currentBarCount = actualBarCount;
        }
        finally
        {
            _dataSemaphore.Release();
        }
    }

    private void CopyToProcessingBuffers(
        float[] computedBarValues,
        float[] computedPeaks,
        int actualBarCount)
    {
        int bytesToCopy = actualBarCount * sizeof(float);

        if (_processingBarValues != null)
            Buffer.BlockCopy(computedBarValues, 0, _processingBarValues, 0, bytesToCopy);

        if (_processingPeaks != null)
            Buffer.BlockCopy(computedPeaks, 0, _processingPeaks, 0, bytesToCopy);
    }

    private void SwapProcessingAndRenderingBuffers(int actualBarCount)
    {
        EnsureRenderingBuffers(actualBarCount);
        int bytesToCopy = actualBarCount * sizeof(float);

        if (_processingBarValues != null && _renderBarValues != null)
            Buffer.BlockCopy(_processingBarValues, 0, _renderBarValues, 0, bytesToCopy);

        if (_processingPeaks != null && _renderPeaks != null)
            Buffer.BlockCopy(_processingPeaks, 0, _renderPeaks, 0, bytesToCopy);
    }

    private void EnsureProcessingBuffers(int actualBarCount)
    {
        if (_processingBarValues == null || _processingPeaks == null ||
            _processingBarValues.Length < actualBarCount)
        {
            ReturnBufferToPool(_processingBarValues);
            ReturnBufferToPool(_processingPeaks);

            _processingBarValues = GetFloatBuffer(actualBarCount);
            _processingPeaks = GetFloatBuffer(actualBarCount);
        }
    }

    private void EnsureRenderingBuffers(int actualBarCount)
    {
        if (_renderBarValues == null || _renderPeaks == null ||
            _renderBarValues.Length < actualBarCount)
        {
            ReturnBufferToPool(_renderBarValues);
            ReturnBufferToPool(_renderPeaks);

            _renderBarValues = GetFloatBuffer(actualBarCount);
            _renderPeaks = GetFloatBuffer(actualBarCount);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void ScaleSpectrum(
        float[] spectrum,
        float[] scaledSpectrum,
        int barCount,
        int spectrumLength)
    {
        float blockSize = spectrumLength / (float)barCount;

        if (barCount < SMALL_BAR_COUNT_THRESHOLD)
        {
            ScaleSpectrumSequential(spectrum, scaledSpectrum, barCount, spectrumLength, blockSize);
            return;
        }

        Parallel.For(0, barCount,
            new ParallelOptions { MaxDegreeOfParallelism = MAX_PARALLEL_THREADS }, i =>
            {
                ScaleSpectrumForBar(i, spectrum, scaledSpectrum, spectrumLength, blockSize);
            });
    }

    private static void ScaleSpectrumSequential(
        float[] spectrum,
        float[] scaledSpectrum,
        int barCount,
        int spectrumLength,
        float blockSize)
    {
        for (int i = 0; i < barCount; i++)
        {
            ScaleSpectrumForBar(i, spectrum, scaledSpectrum, spectrumLength, blockSize);
        }
    }

    private static void ScaleSpectrumForBar(
        int barIndex,
        float[] spectrum,
        float[] scaledSpectrum,
        int spectrumLength,
        float blockSize)
    {
        int start = (int)(barIndex * blockSize);
        int end = (int)MathF.Min((int)((barIndex + 1) * blockSize), spectrumLength);
        int count = end - start;

        if (count <= 0)
        {
            scaledSpectrum[barIndex] = 0f;
            return;
        }

        var (sum, peak) = CalculateBlockStatistics(spectrum, start, end, count);
        scaledSpectrum[barIndex] = CalculateScaledValue(sum, peak, count, barIndex, spectrumLength);
    }

    private static (float sum, float peak) CalculateBlockStatistics(
        float[] spectrum,
        int start,
        int end,
        int count)
    {
        if (count < SIMD_THRESHOLD)
        {
            return CalculateBlockStatisticsFast(spectrum, start, end);
        }

        return CalculateBlockStatisticsSIMD(spectrum, start, end, count);
    }

    private static (float sum, float peak) CalculateBlockStatisticsFast(
        float[] spectrum,
        int start,
        int end)
    {
        float sum = 0f;
        float peak = float.MinValue;

        for (int j = start; j < end; j++)
        {
            float value = spectrum[j];
            sum += value;
            peak = MathF.Max(peak, value);
        }

        return (sum, peak);
    }

    private static (float sum, float peak) CalculateBlockStatisticsSIMD(
        float[] spectrum,
        int start,
        int end,
        int count)
    {
        float sum = 0f;
        float peak = float.MinValue;
        int vectorSize = Vector<float>.Count;
        int j = start;

        Vector<float> sumVector = Vector<float>.Zero;
        Vector<float> maxVector = new(float.MinValue);

        for (; j <= end - vectorSize; j += vectorSize)
        {
            var vec = new Vector<float>(spectrum, j);
            sumVector += vec;
            maxVector = System.Numerics.Vector.Max(maxVector, vec);
        }

        for (int k = 0; k < vectorSize; k++)
        {
            sum += sumVector[k];
            peak = MathF.Max(peak, maxVector[k]);
        }

        for (; j < end; j++)
        {
            float value = spectrum[j];
            sum += value;
            peak = MathF.Max(peak, value);
        }

        return (sum, peak);
    }

    private static float CalculateScaledValue(
        float sum,
        float peak,
        int count,
        int barIndex,
        int spectrumLength)
    {
        float average = count <= 1 ? sum : sum / count;
        float weight = SPECTRUM_WEIGHT;
        float baseValue = average * (1.0f - weight) + peak * weight;

        if (barIndex > spectrumLength / SPECTRUM_HALF_DIVIDER)
        {
            float boost = 1.0f + (float)barIndex / spectrumLength * BOOST_FACTOR;
            baseValue *= boost;
        }

        return MathF.Min(1.0f, baseValue);
    }

    private void DisposeThreadResources()
    {
        _calculationCts?.Cancel();
        _dataAvailableEvent.Set();

        try
        {
            _calculationTask?.Wait(DISPOSE_WAIT_TIMEOUT);
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, LOG_PREFIX, $"Dispose wait error: {ex.Message}");
        }

        _calculationCts?.Dispose();
    }

    private void DisposeSynchronizationObjects()
    {
        _dataAvailableEvent.Dispose();
        _dataSemaphore.Dispose();
    }

    private void ReturnAllBuffers()
    {
        ReturnBufferToPool(_previousSpectrumBuffer);
        ReturnBufferToPool(_peaks);
        ReturnBufferToPool(_peakHoldTimes);
        ReturnBufferToPool(_renderBarValues);
        ReturnBufferToPool(_renderPeaks);
        ReturnBufferToPool(_processingBarValues);
        ReturnBufferToPool(_processingPeaks);
        ReturnBufferToPool(_velocities);
    }

    private void ClearBufferPools()
    {
        while (_floatBufferPool.TryDequeue(out _)) { }
        while (_dateTimeBufferPool.TryDequeue(out _)) { }
    }

    private void DisposePaintsAndPaths()
    {
        _peakPaint.Dispose();
        _shadowPaint.Dispose();
        _glowPaint.Dispose();

        _cachedBarPath?.Dispose();
        _cachedPeakPath?.Dispose();
        _cachedShadowPath?.Dispose();
    }

    private void DisposeShaders() => _barGradient?.Dispose();
}