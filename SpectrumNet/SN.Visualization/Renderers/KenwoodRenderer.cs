#nullable enable

using static SpectrumNet.SN.Visualization.Renderers.KenwoodRenderer.Constants;
using static SpectrumNet.SN.Visualization.Renderers.KenwoodRenderer.Constants.Quality;
using static System.MathF;

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class KenwoodRenderer : EffectSpectrumRenderer
{
    private static readonly Lazy<KenwoodRenderer> _instance =
        new(() => new KenwoodRenderer());

    private const string LogPrefix = nameof(KenwoodRenderer);

    private KenwoodRenderer() { }

    public static KenwoodRenderer GetInstance() => _instance.Value;

    public record Constants
    {
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
            SMALL_BAR_VALUE_MAX = 1f;

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
        _rendererSmoothingFactor,
        _transitionSmoothness,
        _lastCanvasHeight,
        _pendingCanvasHeight;

    private readonly float _barBlur;

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
        _pathsNeedRebuild = true;

    protected override void OnInitialize()
    {
        _logger.Safe(() =>
        {
            base.OnInitialize();
            InitializeBufferPools();
            InitializePaths();
            InitializeBuffers(INITIAL_BUFFER_SIZE);
            ApplyQualitySettingsInternal();
            StartCalculationThread();
        }, LogPrefix, "Failed to initialize renderer");
    }

    protected override void OnConfigurationChanged()
    {
        _rendererSmoothingFactor = _isOverlayActive
            ? OVERLAY_RENDERER_SMOOTHING_FACTOR
            : (MEDIUM_SMOOTHING_FACTOR * NORMAL_RENDERER_SMOOTHING_FACTOR_MULTIPLIER);

        _transitionSmoothness = _isOverlayActive
            ? OVERLAY_TRANSITION_SMOOTHNESS
            : NORMAL_TRANSITION_SMOOTHNESS;
    }

    protected override void OnQualitySettingsApplied()
    {
        ApplyQualitySettingsInternal();
    }

    private void ApplyQualitySettingsInternal()
    {
        switch (Quality)
        {
            case RenderQuality.Low:
                LowQualitySettings();
                break;
            case RenderQuality.Medium:
                MediumQualitySettings();
                break;
            case RenderQuality.High:
                HighQualitySettings();
                break;
        }

        UpdatePaintProperties();
    }

    private void LowQualitySettings()
    {
        _useAntiAlias = LOW_USE_ANTIALIASING;
        _useAdvancedEffects = LOW_USE_ADVANCED_EFFECTS;
        _useShadows = LOW_USE_SHADOWS;
        _useGlow = LOW_USE_GLOW;
        _useBarBlur = LOW_USE_BAR_BLUR;
        _rendererSmoothingFactor = LOW_SMOOTHING_FACTOR;
        _transitionSmoothness = LOW_TRANSITION_SMOOTHNESS;
    }

    private void MediumQualitySettings()
    {
        _useAntiAlias = MEDIUM_USE_ANTIALIASING;
        _useAdvancedEffects = MEDIUM_USE_ADVANCED_EFFECTS;
        _useShadows = MEDIUM_USE_SHADOWS;
        _useGlow = MEDIUM_USE_GLOW;
        _useBarBlur = MEDIUM_USE_BAR_BLUR;
        _glowRadius = MEDIUM_GLOW_RADIUS;
        _glowIntensity = MEDIUM_GLOW_INTENSITY;
        _shadowBlur = MEDIUM_SHADOW_BLUR;
        _shadowOpacity = MEDIUM_SHADOW_OPACITY;
        _rendererSmoothingFactor = MEDIUM_SMOOTHING_FACTOR;
        _transitionSmoothness = MEDIUM_TRANSITION_SMOOTHNESS;
    }

    private void HighQualitySettings()
    {
        _useAntiAlias = HIGH_USE_ANTIALIASING;
        _useAdvancedEffects = HIGH_USE_ADVANCED_EFFECTS;
        _useShadows = HIGH_USE_SHADOWS;
        _useGlow = HIGH_USE_GLOW;
        _useBarBlur = MEDIUM_USE_BAR_BLUR; // Используем настройки как на Medium для полосок баров
        _glowRadius = HIGH_GLOW_RADIUS;
        _glowIntensity = HIGH_GLOW_INTENSITY;
        _shadowBlur = HIGH_SHADOW_BLUR;
        _shadowOpacity = HIGH_SHADOW_OPACITY;
        _rendererSmoothingFactor = HIGH_SMOOTHING_FACTOR;
        _transitionSmoothness = HIGH_TRANSITION_SMOOTHNESS;
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
        if (canvas == null || canvas.QuickReject(new SKRect(0, 0, info.Width, info.Height)) ||
            spectrum == null || spectrum.Length == 0 || info.Width <= 0 || info.Height <= 0 || barCount <= 0)
            return;

        _logger.Safe(() =>
        {
            int limitedBarCount = LimitBarCount(barCount);
            float widthAdjustment = CalculateBarWidthAdjustment(barCount, limitedBarCount);
            float adjustedBarWidth = barWidth * widthAdjustment;
            float adjustedBarSpacing = barSpacing * widthAdjustment;
            float totalBarWidth = adjustedBarWidth + adjustedBarSpacing;

            UpdateState(canvas, spectrum, info, limitedBarCount);
            RenderFrame(canvas, info, adjustedBarWidth, adjustedBarSpacing, totalBarWidth);
        }, LogPrefix, "Error during rendering");
    }

    public override void SetOverlayTransparency(float level)
    {
        base.SetOverlayTransparency(level);
        _overlayStateChanged = true;
    }

    protected override void OnDispose()
    {
        _logger.Safe(() =>
        {
            _calculationCts?.Cancel();
            _dataAvailableEvent.Set();

            try
            {
                _calculationTask?.Wait(DISPOSE_WAIT_TIMEOUT);
            }
            catch { }

            _calculationCts?.Dispose();
            _dataAvailableEvent.Dispose();
            _dataSemaphore.Dispose();

            ReturnAllBuffers();
            ClearBufferPools();

            _peakPaint.Dispose();
            _shadowPaint.Dispose();
            _glowPaint.Dispose();

            _cachedBarPath?.Dispose();
            _cachedPeakPath?.Dispose();
            _cachedShadowPath?.Dispose();

            _barGradient?.Dispose();

            base.OnDispose();
        }, LogPrefix, "Error during disposal");
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

        if (_peakHoldTimes != null)
            Array.Fill(_peakHoldTimes, DateTime.MinValue);
        if (_velocities != null)
            Array.Fill(_velocities, 0f);

        _buffersInitialized = true;
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

        return Quality == RenderQuality.High
            ? Math.Min(barCount, MAX_BAR_COUNT_MEDIUM_HIGH)
            : Math.Min(barCount, MAX_BAR_COUNT_MEDIUM_HIGH);
    }

    private static float CalculateBarWidthAdjustment(int requestedBarCount, int limitedBarCount)
    {
        if (requestedBarCount <= limitedBarCount)
            return 1.0f;

        return (float)requestedBarCount / limitedBarCount;
    }

    private void UpdateState(
        SKCanvas _,
        float[] spectrum,
        SKImageInfo info,
        int barCount)
    {
        bool canvasSizeChanged = MathF.Abs(_lastCanvasHeight - info.Height) > CANVAS_SIZE_CHANGE_THRESHOLD;
        if (canvasSizeChanged)
        {
            CreateGradients(info.Height);
            _lastCanvasHeight = info.Height;
        }

        SubmitSpectrumData(spectrum, barCount, info.Height);
    }

    private void RenderFrame(
        SKCanvas canvas,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        float totalBarWidth)
    {
        if (_renderBarValues == null || _renderPeaks == null || _currentBarCount <= 0)
            return;

        int renderCount = Min(_currentBarCount, _renderBarValues.Length);
        if (renderCount <= 0)
            return;

        if (_pathsNeedRebuild || _lastRenderCount != renderCount)
        {
            UpdateRenderingPaths(info, barWidth, barSpacing, totalBarWidth, renderCount);
            _lastRenderCount = renderCount;
            _pathsNeedRebuild = false;
        }

        if (_isOverlayActive)
        {
            RenderWithOverlay(canvas, () =>
            {
                RenderBars(canvas);
                RenderPeaks(canvas);
            });
        }
        else
        {
            if (_useShadows && _cachedBarPath != null && !_cachedBarPath.IsEmpty && _cachedShadowPath != null)
                RenderShadows(canvas);

            RenderBars(canvas);

            if (_useGlow && _cachedBarPath != null && !_cachedBarPath.IsEmpty)
                RenderGlow(canvas);

            RenderPeaks(canvas);
        }
    }

    private void RenderShadows(SKCanvas canvas)
    {
        using var shadowPaint = _paintPool.Get();
        shadowPaint.Color = SKColors.Black.WithAlpha((byte)(_shadowOpacity * 255));
        shadowPaint.IsAntialias = UseAntiAlias;

        if (_useAdvancedEffects)
            shadowPaint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, _shadowBlur);

        canvas.DrawPath(_cachedShadowPath!, shadowPaint);
    }

    private void RenderGlow(SKCanvas canvas)
    {
        using var glowPaint = _paintPool.Get();
        glowPaint.Color = SKColors.White.WithAlpha((byte)(_glowIntensity * 255));
        glowPaint.IsAntialias = UseAntiAlias;
        glowPaint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, _glowRadius);
        glowPaint.Shader = _barGradient;

        canvas.DrawPath(_cachedBarPath!, glowPaint);
    }

    private void SubmitSpectrumData(
        float[] spectrum,
        int barCount,
        float canvasHeight)
    {
        _dataSemaphore.Wait();
        try
        {
            _pendingSpectrum = spectrum;
            _pendingBarCount = barCount;
            _pendingCanvasHeight = canvasHeight;

            if (_processingBarValues != null && _renderBarValues != null &&
                _processingPeaks != null && _renderPeaks != null &&
                _processingBarValues.Length == _renderBarValues.Length)
            {
                (_renderBarValues, _processingBarValues) = (_processingBarValues, _renderBarValues);
                (_renderPeaks, _processingPeaks) = (_processingPeaks, _renderPeaks);
            }
        }
        finally
        {
            _dataSemaphore.Release();
            _dataAvailableEvent.Set();
        }
    }

    private void UpdateRenderingPaths(
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        float _,
        int renderCount)
    {
        _logger.Safe(() =>
        {
            _cachedBarPath?.Reset();
            _cachedPeakPath?.Reset();
            _cachedShadowPath?.Reset();

            if (_renderBarValues == null || _renderPeaks == null ||
                _cachedBarPath == null || _cachedPeakPath == null ||
                renderCount <= 0 || _renderBarValues.Length <= 0)
                return;

            int step = Quality == RenderQuality.High && renderCount > HIGH_RENDER_STEP_THRESHOLD ? 2 : 1;

            for (int i = 0; i < renderCount; i += step)
            {
                if (i >= _renderBarValues.Length)
                    break;

                float x = i * (barWidth + barSpacing);
                float barValue = _renderBarValues[i];

                if (barValue > MIN_VISIBLE_VALUE)
                {
                    float barTop = info.Height - barValue;
                    _cachedBarPath.AddRect(SKRect.Create(x, barTop, barWidth, barValue));

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

                if (i < _renderPeaks.Length && _renderPeaks[i] > MIN_VISIBLE_VALUE)
                {
                    float peakY = info.Height - _renderPeaks[i];
                    _cachedPeakPath.AddRect(SKRect.Create(
                        x,
                        peakY - _peakHeight,
                        barWidth,
                        _peakHeight));
                }
            }
        }, LogPrefix, "Failed to update rendering paths");
    }

    private void RenderBars(SKCanvas canvas)
    {
        if (_cachedBarPath == null || _cachedBarPath.IsEmpty)
            return;

        using var barPaint = CreateStandardPaint(SKColors.White);
        barPaint.Shader = _barGradient;

        if (_useBarBlur && _useAdvancedEffects)
            barPaint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, _barBlur);

        canvas.DrawPath(_cachedBarPath, barPaint);
    }

    private void RenderPeaks(SKCanvas canvas)
    {
        if (_cachedPeakPath == null || _cachedPeakPath.IsEmpty)
            return;

        canvas.DrawPath(_cachedPeakPath, _peakPaint);
    }

    private void CreateGradients(float height)
    {
        _logger.Safe(() =>
        {
            _barGradient?.Dispose();

            _barGradient = SKShader.CreateLinearGradient(
                new SKPoint(0, height),
                new SKPoint(0, 0),
                _barColors,
                _barColorPositions,
                SKShaderTileMode.Clamp);
        }, LogPrefix, "Failed to create gradients");
    }

    private void CalculationThreadMain(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_dataAvailableEvent.WaitOne(50))
                    continue;

                if (ct.IsCancellationRequested)
                    break;

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

                if (spectrumData != null && spectrumData.Length > 0)
                    ProcessSpectrumData(spectrumData, barCount, canvasHeight);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Error(LogPrefix, "Error in calculation thread", ex);
                Thread.Sleep(100);
            }
        }
    }

    private void ProcessSpectrumData(
        float[] spectrum,
        int barCount,
        float canvasHeight)
    {
        int spectrumLength = spectrum.Length;
        int actualBarCount = Min(spectrumLength, barCount);

        if (!_buffersInitialized || _previousSpectrumBuffer == null || _velocities == null ||
            _peaks == null || _peakHoldTimes == null || _previousSpectrumBuffer.Length < actualBarCount)
        {
            _dataSemaphore.Wait();
            try
            {
                ReturnAllBuffers();
                _previousSpectrumBuffer = GetFloatBuffer(actualBarCount);
                _peaks = GetFloatBuffer(actualBarCount);
                _peakHoldTimes = GetDateTimeBuffer(actualBarCount);
                _velocities = GetFloatBuffer(actualBarCount);

                if (_peakHoldTimes != null)
                    Array.Fill(_peakHoldTimes, DateTime.MinValue);
                if (_velocities != null)
                    Array.Fill(_velocities, 0f);

                _buffersInitialized = true;
            }
            finally
            {
                _dataSemaphore.Release();
            }
        }

        var scaled = GetFloatBuffer(actualBarCount);
        var computedBarValues = GetFloatBuffer(actualBarCount);
        var computedPeaks = GetFloatBuffer(actualBarCount);

        ScaleSpectrum(spectrum, scaled, actualBarCount, spectrumLength);
        ProcessAnimation(scaled, computedBarValues, computedPeaks, canvasHeight, actualBarCount);
        UpdateRenderingBuffers(computedBarValues, computedPeaks, actualBarCount);

        ReturnBufferToPool(scaled);
        ReturnBufferToPool(computedBarValues);
        ReturnBufferToPool(computedPeaks);

        _pathsNeedRebuild = true;
    }

    private void ProcessAnimation(
        float[] scaledSpectrum,
        float[] computedBarValues,
        float[] computedPeaks,
        float canvasHeight,
        int actualBarCount)
    {
        if (!_buffersInitialized || _velocities == null || _previousSpectrumBuffer == null ||
            _peaks == null || _peakHoldTimes == null)
            return;

        if (canvasHeight < MIN_CANVAS_HEIGHT || actualBarCount < MIN_BAR_COUNT)
        {
            for (int i = 0; i < actualBarCount; i++)
            {
                if (i >= scaledSpectrum.Length || i >= computedBarValues.Length || i >= computedPeaks.Length)
                    continue;

                computedBarValues[i] = scaledSpectrum[i] * canvasHeight;
                computedPeaks[i] = scaledSpectrum[i] * canvasHeight;
            }
            return;
        }

        float smoothFactor = _rendererSmoothingFactor * _animationSpeed;
        float peakFallRate = _peakFallSpeed * canvasHeight * _animationSpeed;
        double peakHoldTimeMs = _peakHoldTime;
        float velocityDamping = VELOCITY_DAMPING * _transitionSmoothness;
        float springStiffness = SPRING_STIFFNESS * (1 - _transitionSmoothness);

        DateTime currentTime = DateTime.Now;

        int batchSize = actualBarCount > 256 ? BATCH_SIZE_LARGE : Constants.BATCH_SIZE;

        if (actualBarCount >= PARALLEL_THRESHOLD)
        {
            Parallel.For(0, (actualBarCount + batchSize - 1) / batchSize,
                new ParallelOptions { MaxDegreeOfParallelism = MAX_PARALLEL_THREADS },
                batchIndex =>
                {
                    int start = batchIndex * batchSize;
                    int end = Math.Min(start + batchSize, actualBarCount);
                    ProcessAnimationBatch(start, end, scaledSpectrum, computedBarValues, computedPeaks,
                        currentTime, canvasHeight, smoothFactor, peakFallRate, peakHoldTimeMs,
                        velocityDamping, springStiffness);
                });
        }
        else
        {
            ProcessAnimationBatch(0, actualBarCount, scaledSpectrum, computedBarValues, computedPeaks,
                currentTime, canvasHeight, smoothFactor, peakFallRate, peakHoldTimeMs,
                velocityDamping, springStiffness);
        }
    }

    private void ProcessAnimationBatch(
        int start, int end, float[] scaledSpectrum, float[] computedBarValues, float[] computedPeaks,
        DateTime currentTime, float canvasHeight, float smoothFactor, float peakFallRate, double peakHoldTimeMs,
        float velocityDamping, float springStiffness)
    {
        for (int i = start; i < end; i++)
        {
            if (i >= scaledSpectrum.Length || i >= _previousSpectrumBuffer!.Length || i >= computedBarValues.Length)
                continue;

            float targetValue = scaledSpectrum[i];

            if (targetValue * canvasHeight < PEAK_ANIMATION_THRESHOLD)
            {
                _previousSpectrumBuffer[i] = targetValue;
                computedBarValues[i] = targetValue * canvasHeight;

                if (_peaks != null && i < _peaks.Length)
                {
                    _peaks[i] = MathF.Max(0, _peaks[i] - SIMPLIFIED_BAR_PEAK_FALL_RATE);
                    computedPeaks[i] = _peaks[i];
                }
                continue;
            }

            float currentValue = _previousSpectrumBuffer[i];
            float delta = targetValue - currentValue;

            if (MathF.Abs(delta) < MIN_DELTA_THRESHOLD)
            {
                computedBarValues[i] = currentValue * canvasHeight;
                UpdatePeak(i, computedBarValues[i], computedPeaks, currentTime, peakFallRate, peakHoldTimeMs);
                continue;
            }

            float adaptiveSmoothFactor = CalculateAdaptiveSmoothFactor(delta, canvasHeight, smoothFactor);
            _velocities![i] = _velocities[i] * velocityDamping + delta * springStiffness;

            float newValue = _previousSpectrumBuffer[i] + _velocities[i] + delta * adaptiveSmoothFactor;
            _previousSpectrumBuffer[i] = MathF.Max(SMALL_BAR_VALUE_MIN, MathF.Min(SMALL_BAR_VALUE_MAX, newValue));

            computedBarValues[i] = _previousSpectrumBuffer[i] * canvasHeight;
            UpdatePeak(i, computedBarValues[i], computedPeaks, currentTime, peakFallRate, peakHoldTimeMs);
        }
    }

    private static float CalculateAdaptiveSmoothFactor(float delta, float canvasHeight, float smoothFactor)
    {
        float absDelta = MathF.Abs(delta * canvasHeight);

        if (absDelta > MAX_CHANGE_THRESHOLD)
        {
            float changeRatio = MathF.Min(1.0f, absDelta / (canvasHeight * CHANGE_RATIO_DIVISOR));
            return MathF.Max(
                smoothFactor * SMOOTH_FACTOR_MIN_MULTIPLIER,
                smoothFactor * (1.0f + changeRatio * SMOOTH_FACTOR_DELTA_MULTIPLIER));
        }

        return smoothFactor;
    }

    private void UpdatePeak(
        int i, float barValue, float[] computedPeaks, DateTime currentTime,
        float peakFallRate, double peakHoldTimeMs)
    {
        float currentPeak = _peaks![i];

        if (barValue > currentPeak)
        {
            _peaks[i] = barValue;
            _peakHoldTimes![i] = currentTime;
        }
        else if ((currentTime - _peakHoldTimes![i]).TotalMilliseconds > peakHoldTimeMs)
        {
            float newPeak = currentPeak - peakFallRate;
            _peaks[i] = MathF.Max(barValue, newPeak);
        }

        computedPeaks[i] = _peaks[i];
    }

    private void UpdateRenderingBuffers(
        float[] computedBarValues,
        float[] computedPeaks,
        int actualBarCount)
    {
        _dataSemaphore.Wait();
        try
        {
            if (_processingBarValues == null || _processingPeaks == null ||
                _processingBarValues.Length < actualBarCount)
            {
                ReturnBufferToPool(_processingBarValues);
                ReturnBufferToPool(_processingPeaks);

                _processingBarValues = GetFloatBuffer(actualBarCount);
                _processingPeaks = GetFloatBuffer(actualBarCount);
            }

            int bytesToCopy = actualBarCount * sizeof(float);
            Buffer.BlockCopy(computedBarValues, 0, _processingBarValues, 0, bytesToCopy);
            Buffer.BlockCopy(computedPeaks, 0, _processingPeaks, 0, bytesToCopy);

            if (_renderBarValues == null || _renderPeaks == null || _renderBarValues.Length < actualBarCount)
            {
                ReturnBufferToPool(_renderBarValues);
                ReturnBufferToPool(_renderPeaks);

                _renderBarValues = GetFloatBuffer(actualBarCount);
                _renderPeaks = GetFloatBuffer(actualBarCount);
            }

            Buffer.BlockCopy(_processingBarValues, 0, _renderBarValues, 0, bytesToCopy);
            Buffer.BlockCopy(_processingPeaks, 0, _renderPeaks, 0, bytesToCopy);

            _currentBarCount = actualBarCount;
        }
        finally
        {
            _dataSemaphore.Release();
        }
    }

    private static void ScaleSpectrum(
        float[] spectrum,
        float[] scaledSpectrum,
        int barCount,
        int spectrumLength)
    {
        float blockSize = spectrumLength / (float)barCount;

        if (barCount < SMALL_BAR_COUNT_THRESHOLD)
        {
            for (int i = 0; i < barCount; i++)
            {
                ScaleSpectrumForBar(i, spectrum, scaledSpectrum, spectrumLength, blockSize);
            }
            return;
        }

        Parallel.For(0, barCount,
            new ParallelOptions { MaxDegreeOfParallelism = MAX_PARALLEL_THREADS }, i =>
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

        float sum = 0f;
        float peak = float.MinValue;

        if (count < SIMD_THRESHOLD)
        {
            for (int j = start; j < end; j++)
            {
                float value = spectrum[j];
                sum += value;
                peak = MathF.Max(peak, value);
            }
        }
        else
        {
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
        }

        float average = count <= 1 ? sum : sum / count;
        float weight = SPECTRUM_WEIGHT;
        float baseValue = average * (1.0f - weight) + peak * weight;

        if (barIndex > spectrumLength / SPECTRUM_HALF_DIVIDER)
        {
            float boost = 1.0f + (float)barIndex / spectrumLength * BOOST_FACTOR;
            baseValue *= boost;
        }

        scaledSpectrum[barIndex] = MathF.Min(1.0f, baseValue);
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
}