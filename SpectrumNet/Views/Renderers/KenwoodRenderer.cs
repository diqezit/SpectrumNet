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
            PEAK_HOLD_TIME_MS = 500,
            SPECTRUM_WEIGHT = 0.3f,
            BOOST_FACTOR = 0.3f,
            PEAK_HEIGHT = 3f;

        public const int
            MAX_BUFFER_POOL_SIZE = 16,
            INITIAL_BUFFER_SIZE = 1024;

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
                MEDIUM_SMOOTHING_FACTOR = 1.5f,
                HIGH_SMOOTHING_FACTOR = 1.5f;

            public const float
                LOW_TRANSITION_SMOOTHNESS = 0.5f,
                MEDIUM_TRANSITION_SMOOTHNESS = 1f,
                HIGH_TRANSITION_SMOOTHNESS = 1f;

            public const float
                MEDIUM_GLOW_RADIUS = 3.0f,
                MEDIUM_GLOW_INTENSITY = 0.4f,
                MEDIUM_SHADOW_BLUR = 4.0f,
                MEDIUM_SHADOW_OPACITY = 0.5f;

            public const float
                HIGH_GLOW_RADIUS = 6.0f,
                HIGH_GLOW_INTENSITY = 0.6f,
                HIGH_SHADOW_BLUR = 8.0f,
                HIGH_SHADOW_OPACITY = 0.7f,
                HIGH_BAR_BLUR = 1.0f;

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
        _transitionSmoothness;

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

    private volatile bool _buffersInitialized;
    private volatile bool _isConfiguring;
    private volatile bool _pathsNeedRebuild = true;
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
            ApplyConfiguration(isOverlayActive, quality);

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
        RenderQuality quality)
    {
        return _isOverlayActive != isOverlayActive || Quality != quality;
    }

    private void ApplyConfiguration(
        bool isOverlayActive,
        RenderQuality quality)
    {
        _isOverlayActive = isOverlayActive;
        Quality = quality;
        base._smoothingFactor = isOverlayActive ? 0.5f : 0.3f;
        _rendererSmoothingFactor = CalculateRendererSmoothingFactor(isOverlayActive);
        _transitionSmoothness = isOverlayActive ? 0.7f : 0.5f;
    }

    private static float CalculateRendererSmoothingFactor(bool isOverlayActive) => 
        isOverlayActive ? 0.6f : (MEDIUM_SMOOTHING_FACTOR * 0.2f);

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
            $"Configuration changed. New Quality: {Quality}, Overlay: {_isOverlayActive}");
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
            () => ExecuteRender(canvas, spectrum, info, barWidth, barSpacing, barCount),
            nameof(RenderEffect),
            "Error during rendering"
        );
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
            LogResourceInvalidation,
            nameof(OnInvalidateCachedResources),
            "Failed to invalidate cached resources"
        );
    }

    private static void LogResourceInvalidation() => 
        Log(LogLevel.Debug, LOG_PREFIX, "Cached resources invalidated");

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
        for (int i = 0; i < 8; i++)
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
        if (_renderBarValues == null || _renderPeaks == null || _currentBarCount == 0)
            return;

        int renderCount = Min(_currentBarCount, _renderBarValues.Length);
        if (renderCount <= 0)
            return;

        UpdatePathsIfNeeded(info, barWidth, barSpacing, totalBarWidth, renderCount);
        RenderBarElements(canvas, info);
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
            MathF.Abs(_lastCanvasHeight - info.Height) > 0.5f;

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
                ProcessCalculationLoop(ct);
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

    private void ProcessCalculationLoop(CancellationToken ct)
    {
        if (!_dataAvailableEvent.WaitOne(50))
            return;

        if (ct.IsCancellationRequested)
            return;

        var (data, barCount, canvasHeight) = ExtractPendingSpectrumData(ct);

        if (data != null && data.Length > 0)
            ProcessSpectrumData(data, barCount, canvasHeight);
    }

    private static void HandleCalculationError(Exception ex)
    {
        Log(LogLevel.Error, LOG_PREFIX, $"Calculation error: {ex.Message}");
        Thread.Sleep(100);
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

        var (scaledSpectrum, computedBarValues, computedPeaks) =
            PrepareBuffers(actualBarCount);

        ScaleSpectrumData(spectrum, scaledSpectrum, actualBarCount, spectrumLength);
        ProcessAnimation(scaledSpectrum, computedBarValues, computedPeaks,
            canvasHeight, actualBarCount);
        UpdateRenderingBuffers(computedBarValues, computedPeaks, actualBarCount);
        ReturnToPool(scaledSpectrum, computedBarValues, computedPeaks);

        _pathsNeedRebuild = true;
    }

    private (float[] scaled, float[] bars, float[] peaks) PrepareBuffers(
        int actualBarCount)
    {
        return (
            GetFloatBuffer(actualBarCount),
            GetFloatBuffer(actualBarCount),
            GetFloatBuffer(actualBarCount)
        );
    }

    private static void ScaleSpectrumData(
        float[] spectrum,
        float[] scaledSpectrum,
        int actualBarCount,
        int spectrumLength)
    {
        ScaleSpectrum(spectrum, scaledSpectrum, actualBarCount, spectrumLength);
    }

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
        BuildSimpleBarPaths(info, barWidth, barSpacing, totalBarWidth, renderCount);
    }

    private void BuildSimpleBarPaths(
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        float totalBarWidth,
        int renderCount)
    {
        if (_renderBarValues == null || _renderPeaks == null ||
            _cachedBarPath == null || _cachedPeakPath == null)
            return;

        float minVisibleValue = 1f;

        for (int i = 0; i < renderCount && i < _renderBarValues.Length; i++)
        {
            float x = i * (barWidth + barSpacing);
            AddBarAndPeak(info, barWidth, minVisibleValue, i, x);
        }
    }

    private void AddBarAndPeak(
        SKImageInfo info,
        float barWidth,
        float minVisibleValue,
        int i,
        float x)
    {
        float barValue = _renderBarValues![i];

        if (barValue > minVisibleValue)
        {
            AddBar(info, barWidth, x, barValue);
            AddShadow(info, barWidth, x, barValue);
        }

        if (i < _renderPeaks!.Length && _renderPeaks[i] > minVisibleValue)
        {
            AddPeak(info, barWidth, x, i);
        }
    }

    private void AddBar(
        SKImageInfo info,
        float barWidth,
        float x,
        float barValue)
    {
        float barTop = info.Height - barValue;
        _cachedBarPath!.AddRect(SKRect.Create(x, barTop, barWidth, barValue));
    }

    private void AddShadow(
        SKImageInfo info,
        float barWidth,
        float x,
        float barValue)
    {
        if (_useShadows && _cachedShadowPath != null)
        {
            float shadowOffset = 2f;
            float barTop = info.Height - barValue;
            _cachedShadowPath.AddRect(SKRect.Create(
                x + shadowOffset,
                barTop + shadowOffset,
                barWidth,
                barValue));
        }
    }

    private void AddPeak(
        SKImageInfo info,
        float barWidth,
        float x,
        int i)
    {
        float peakY = info.Height - _renderPeaks![i];
        _cachedPeakPath!.AddRect(SKRect.Create(
            x,
            peakY - _peakHeight,
            barWidth,
            _peakHeight));
    }

    private void ResetAllPaths()
    {
        _cachedBarPath?.Reset();
        _cachedPeakPath?.Reset();
        _cachedShadowPath?.Reset();
    }

    private void RenderBarElements(SKCanvas canvas, SKImageInfo info)
    {
        RenderShadows(canvas);
        RenderBars(canvas);
        RenderBarGlow(canvas);
        RenderPeaks(canvas);
    }

    private void RenderShadows(SKCanvas canvas)
    {
        if (_useShadows && _cachedShadowPath != null)
        {
            var shadowPaint = ConfigureShadowPaint();
            canvas.DrawPath(_cachedShadowPath, shadowPaint);
        }
    }

    private SKPaint ConfigureShadowPaint()
    {
        var shadowPaint = _shadowPaint;
        shadowPaint.Color = SKColors.Black.WithAlpha((byte)(_shadowOpacity * 255));
        shadowPaint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, _shadowBlur);
        return shadowPaint;
    }

    private void RenderBars(SKCanvas canvas)
    {
        if (_cachedBarPath != null)
        {
            using var barPaint = CreateBarPaint();
            canvas.DrawPath(_cachedBarPath, barPaint);
        }
    }

    private SKPaint CreateBarPaint()
    {
        var barPaint = CreateStandardPaint(SKColors.White);
        barPaint.Shader = _barGradient;

        if (_useBarBlur)
            barPaint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, _barBlur);

        return barPaint;
    }

    private void RenderBarGlow(SKCanvas canvas)
    {
        if (_useGlow && _cachedBarPath != null)
        {
            var glowPaint = ConfigureGlowPaint();
            canvas.DrawPath(_cachedBarPath, glowPaint);
        }
    }

    private SKPaint ConfigureGlowPaint()
    {
        var glowPaint = _glowPaint;
        glowPaint.Color = SKColors.White.WithAlpha((byte)(_glowIntensity * 255));
        glowPaint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, _glowRadius);
        glowPaint.Shader = _barGradient;
        return glowPaint;
    }

    private void RenderPeaks(SKCanvas canvas)
    {
        if (_cachedPeakPath != null)
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

    private void DisposeGradients()
    {
        _barGradient?.Dispose();
    }

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

    private bool NeedBufferReinitialization(int actualBarCount)
    {
        return !_buffersInitialized
            || _previousSpectrumBuffer == null
            || _velocities == null
            || _peaks == null
            || _peakHoldTimes == null
            || _previousSpectrumBuffer.Length < actualBarCount;
    }

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

        var animationParams = CalculateAnimationParameters(canvasHeight);
        var physicsParams = CalculatePhysicsParameters();

        const int batchSize = 64;
        for (int batchStart = 0; batchStart < actualBarCount; batchStart += batchSize)
        {
            int batchEnd = Min(batchStart + batchSize, actualBarCount);
            ProcessBatch(
                batchStart,
                batchEnd,
                scaledSpectrum,
                computedBarValues,
                computedPeaks,
                currentTime,
                canvasHeight,
                animationParams,
                physicsParams);
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
        CalculateAnimationParameters(float canvasHeight)
    {
        float smoothFactor = _rendererSmoothingFactor * _animationSpeed;
        float peakFallRate = _peakFallSpeed * canvasHeight * _animationSpeed;
        double peakHoldTimeMs = _peakHoldTime;
        return (smoothFactor, peakFallRate, peakHoldTimeMs);
    }

    private (float maxChangeThreshold, float velocityDamping, float springStiffness)
        CalculatePhysicsParameters()
    {
        float maxChangeThreshold = 0.3f;
        float velocityDamping = 0.8f * _transitionSmoothness;
        float springStiffness = 0.2f * (1 - _transitionSmoothness);
        return (maxChangeThreshold, velocityDamping, springStiffness);
    }

    private void ProcessBatch(
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

        for (int i = batchStart; i < batchEnd; i++)
        {
            if (i >= _velocities!.Length || i >= _previousSpectrumBuffer!.Length)
                continue;

            ProcessSingleBar(
                i,
                scaledSpectrum,
                computedBarValues,
                computedPeaks,
                currentTime,
                canvasHeight,
                animParams,
                physicsParams);
        }
    }

    private bool ValidateAnimationBuffers()
    {
        return _velocities != null
            && _previousSpectrumBuffer != null
            && _peaks != null
            && _peakHoldTimes != null;
    }

    private void ProcessSingleBar(
        int i,
        float[] scaledSpectrum,
        float[] computedBarValues,
        float[] computedPeaks,
        DateTime currentTime,
        float canvasHeight,
        (float smoothFactor, float peakFallRate, double peakHoldTimeMs) animParams,
        (float maxChangeThreshold, float velocityDamping, float springStiffness) physicsParams)
    {
        float targetValue = scaledSpectrum[i];
        float currentValue = _previousSpectrumBuffer![i];
        float delta = targetValue - currentValue;

        float adaptiveSmoothFactor = CalculateAdaptiveSmoothFactor(
            delta,
            canvasHeight,
            animParams.smoothFactor,
            physicsParams.maxChangeThreshold);

        UpdateBarValue(
            i,
            delta,
            adaptiveSmoothFactor,
            physicsParams.velocityDamping,
            physicsParams.springStiffness);

        computedBarValues[i] = _previousSpectrumBuffer[i] * canvasHeight;
        UpdatePeak(i, computedBarValues[i], computedPeaks, currentTime, animParams, physicsParams);
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
            float changeRatio = MathF.Min(1.0f, absDelta / (canvasHeight * 0.7f));
            return MathF.Max(
                smoothFactor * 0.3f,
                smoothFactor * (1.0f + changeRatio * 0.5f));
        }

        return smoothFactor;
    }

    private void UpdateBarValue(
        int i,
        float delta,
        float adaptiveSmoothFactor,
        float velocityDamping,
        float springStiffness)
    {
        _velocities![i] = _velocities[i] * velocityDamping + delta * springStiffness;
        float newValue = _previousSpectrumBuffer![i] + _velocities[i] + delta * adaptiveSmoothFactor;
        _previousSpectrumBuffer[i] = MathF.Max(0f, MathF.Min(1f, newValue));
    }

    private void UpdatePeak(
        int i,
        float barValue,
        float[] computedPeaks,
        DateTime currentTime,
        (float smoothFactor, float peakFallRate, double peakHoldTimeMs) animParams,
        (float maxChangeThreshold, float velocityDamping, float springStiffness) physicsParams)
    {
        float currentPeak = _peaks![i];

        if (barValue > currentPeak)
        {
            SetNewPeak(i, barValue, currentTime);
        }
        else if (ShouldPeakFall(i, currentTime, animParams.peakHoldTimeMs))
        {
            UpdateFallingPeak(i, barValue, animParams.peakFallRate);
        }

        computedPeaks[i] = _peaks[i];
    }

    private void SetNewPeak(int i, float barValue, DateTime currentTime)
    {
        _peaks![i] = barValue;
        _peakHoldTimes![i] = currentTime;
    }

    private bool ShouldPeakFall(int i, DateTime currentTime, double peakHoldTimeMs)
    {
        return (currentTime - _peakHoldTimes![i]).TotalMilliseconds > peakHoldTimeMs;
    }

    private void UpdateFallingPeak(int i, float barValue, float peakFallRate)
    {
        float currentPeak = _peaks![i];
        float newPeak = currentPeak - peakFallRate;
        _peaks[i] = MathF.Max(barValue, newPeak);
    }

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
        int maxThreads = (int)MathF.Max(2, ProcessorCount / 2);

        Parallel.For(0, barCount, new ParallelOptions { MaxDegreeOfParallelism = maxThreads }, i =>
        {
            ScaleSpectrumForBar(i, spectrum, scaledSpectrum, spectrumLength, blockSize);
        });
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
        float average = sum / count;
        float weight = SPECTRUM_WEIGHT;
        float baseValue = average * (1.0f - weight) + peak * weight;

        if (barIndex > spectrumLength / 2)
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
            _calculationTask?.Wait(500);
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