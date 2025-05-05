#nullable enable

using static SpectrumNet.Views.Renderers.KenwoodRenderer.Constants;

namespace SpectrumNet.Views.Renderers;

public sealed class KenwoodRenderer : EffectSpectrumRenderer
{
    private static readonly Lazy<KenwoodRenderer> _instance = new(() => new KenwoodRenderer());

    private KenwoodRenderer() { }

    public static KenwoodRenderer GetInstance() => _instance.Value;

    public record Constants
    {
        public const string LOG_PREFIX = "KenwoodRenderer";

        public const float
            ANIMATION_SPEED = 0.85f,
            REFLECTION_OPACITY = 0.4f,
            REFLECTION_HEIGHT = 0.6f,
            PEAK_FALL_SPEED = 0.007f,
            SCALE_FACTOR = 0.9f,
            DESIRED_SEGMENT_HEIGHT = 10f,
            SEGMENT_GAP = 2f;

        public const int
            MAX_BUFFER_POOL_SIZE = 16,
            INITIAL_BUFFER_SIZE = 1024,
            PEAK_HOLD_TIME_MS = 500;

        public const float
            OUTLINE_STROKE_WIDTH = 1.2f,
            SHADOW_BLUR_RADIUS = 3f,
            SPECTRUM_WEIGHT = 0.3f,
            BOOST_FACTOR = 0.3f;

        public static class Quality
        {
            public const bool
                LOW_ENABLE_SHADOWS = false,
                LOW_ENABLE_REFLECTIONS = false,
                LOW_USE_SEGMENTED_BARS = false;

            public const float
                LOW_SEGMENT_ROUNDNESS = 0f,
                LOW_SMOOTHING_FACTOR = 0.3f,
                LOW_TRANSITION_SMOOTHNESS = 0.5f;

            public const bool
                MEDIUM_ENABLE_SHADOWS = true,
                MEDIUM_ENABLE_REFLECTIONS = false,
                MEDIUM_USE_SEGMENTED_BARS = true;

            public const float
                MEDIUM_SEGMENT_ROUNDNESS = 2f,
                MEDIUM_SMOOTHING_FACTOR = 1.5f,
                MEDIUM_TRANSITION_SMOOTHNESS = 1f;

            public const bool
                HIGH_ENABLE_SHADOWS = true,
                HIGH_ENABLE_REFLECTIONS = true,
                HIGH_USE_SEGMENTED_BARS = true;

            public const float
                HIGH_SEGMENT_ROUNDNESS = 4f,
                HIGH_SMOOTHING_FACTOR = 1.5f,
                HIGH_TRANSITION_SMOOTHNESS = 1f;
        }

        public const RenderQuality DEFAULT_QUALITY = RenderQuality.Medium;
    }

    private bool _enableShadows;
    private bool _enableReflections;
    private bool _useSegmentedBars;
    private float _segmentRoundness;
    private new float _smoothingFactor;
    private float _transitionSmoothness;

    private readonly float
        _animationSpeed = ANIMATION_SPEED,
        _reflectionOpacity = REFLECTION_OPACITY,
        _reflectionHeight = REFLECTION_HEIGHT,
        _peakFallSpeed = PEAK_FALL_SPEED,
        _scaleFactor = SCALE_FACTOR,
        _desiredSegmentHeight = DESIRED_SEGMENT_HEIGHT;

    private int _currentSegmentCount, _currentBarCount, _lastRenderCount;
    private float _lastTotalBarWidth, _lastBarWidth, _lastBarSpacing, _lastSegmentHeight;
    private int _pendingBarCount;
    private float _pendingCanvasHeight;

    private new float[]? _previousSpectrum;
    private float[]? _peaks, _renderBarValues, _renderPeaks;
    private float[]? _processingBarValues, _processingPeaks, _pendingSpectrum, _velocities;
    private DateTime[]? _peakHoldTimes;

    private SKPath? _cachedBarPath, _cachedGreenSegmentsPath, _cachedYellowSegmentsPath, _cachedRedSegmentsPath;
    private SKPath? _cachedOutlinePath, _cachedPeakPath, _cachedShadowPath, _cachedReflectionPath;
    private bool _pathsNeedRebuild = true;
    private SKMatrix _lastTransform = SKMatrix.Identity;

    private readonly ConcurrentQueue<float[]> _floatBufferPool = new();
    private readonly ConcurrentQueue<DateTime[]> _dateTimeBufferPool = new();

    private readonly SemaphoreSlim _dataSemaphore = new(1, 1);
    private readonly AutoResetEvent _dataAvailableEvent = new(false);

    private SKShader? _barGradient, _greenGradient, _yellowGradient, _redGradient, _reflectionGradient;

    private readonly SKPaint _peakPaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Fill,
        Color = SKColors.White
    };

    private readonly SKPaint _segmentShadowPaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Fill,
        Color = new SKColor(0, 0, 0, 80),
        MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, SHADOW_BLUR_RADIUS)
    };

    private readonly SKPaint _outlinePaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        Color = new SKColor(255, 255, 255, 60),
        StrokeWidth = OUTLINE_STROKE_WIDTH
    };

    private readonly SKPaint _reflectionPaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Fill,
        BlendMode = SKBlendMode.SrcOver
    };

    private static readonly SKColor[] _greenColors = [new(0, 230, 120, 255), new(0, 255, 0, 255)];
    private static readonly SKColor[] _yellowColors = [new(255, 230, 0, 255), new(255, 180, 0, 255)];
    private static readonly SKColor[] _redColors = [new(255, 80, 0, 255), new(255, 30, 0, 255)];
    private static readonly SKColor[] _barColors = [
        new(0, 230, 120, 255), new(0, 255, 0, 255),
        new(255, 230, 0, 255), new(255, 180, 0, 255),
        new(255, 80, 0, 255), new(255, 30, 0, 255)
    ];
    private static readonly float[] _barColorPositions = [0f, 0.6f, 0.6f, 0.85f, 0.85f, 1f];

    private Task? _calculationTask;
    private CancellationTokenSource? _calculationCts;
    private SKRect _lastCanvasRect = SKRect.Empty;

    private readonly Dictionary<int, SKPaint> _segmentPaints = [];
    private volatile bool _buffersInitialized;

    protected override void OnInitialize()
    {
        ExecuteSafely(
            () =>
            {
                base.OnInitialize();
                InitializeBufferPools();
                InitializePaths();
                InitializeBuffers(INITIAL_BUFFER_SIZE);
                StartCalculationThread();

                Log(LogLevel.Debug, LOG_PREFIX, "Initialized");
            },
            "OnInitialize",
            "Failed to initialize renderer resources"
        );
    }

    protected override void OnConfigurationChanged()
    {
        ExecuteSafely(
            () =>
            {
                _smoothingFactor = _isOverlayActive ? 0.6f : Constants.Quality.MEDIUM_SMOOTHING_FACTOR * 0.2f;
                _transitionSmoothness = _isOverlayActive ? 0.7f : 0.5f;
                _pathsNeedRebuild = true;
            },
            "OnConfigurationChanged",
            "Failed to apply configuration changes"
        );
    }

    protected override void OnQualitySettingsApplied()
    {
        ExecuteSafely(
            () =>
            {
                base.OnQualitySettingsApplied();
                ApplyQualityBasedSettings();
                UpdatePaintProperties();
                _pathsNeedRebuild = true;

                Log(LogLevel.Debug, LOG_PREFIX, $"Quality changed to {Quality}");
            },
            "OnQualitySettingsApplied",
            "Failed to apply quality settings"
        );
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
            () =>
            {
                float totalWidth = info.Width;
                float totalBarWidth = totalWidth / barCount;
                barWidth = totalBarWidth * 0.7f;
                barSpacing = totalBarWidth * 0.3f;

                UpdateState(canvas, spectrum, info, barCount);
                RenderFrame(canvas, info, barWidth, barSpacing, totalBarWidth, barCount);
            },
            "RenderEffect",
            "Error during rendering"
        );
    }

    protected override void OnInvalidateCachedResources()
    {
        ExecuteSafely(
            () =>
            {
                _pathsNeedRebuild = true;
            },
            "OnInvalidateCachedResources",
            "Failed to invalidate cached resources"
        );
    }

    protected override void OnDispose()
    {
        ExecuteSafely(
            () =>
            {
                DisposeThreadResources();
                DisposeSynchronizationObjects();
                ReturnAllBuffers();
                ClearBufferPools();
                DisposePaintsAndPaths();
                DisposeShaders();

                base.OnDispose();
                Log(LogLevel.Debug, LOG_PREFIX, "Disposed");
            },
            "OnDispose",
            "Error during disposal"
        );
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
        _previousSpectrum = new float[size];
        _peaks = new float[size];
        _peakHoldTimes = new DateTime[size];
        _velocities = new float[size];
        _renderBarValues = new float[size];
        _renderPeaks = new float[size];
        _processingBarValues = new float[size];
        _processingPeaks = new float[size];

        Array.Fill(_peakHoldTimes, DateTime.MinValue);
        Array.Fill(_velocities, 0f);
        _buffersInitialized = true;
    }

    private void InitializePaths()
    {
        _cachedBarPath = new SKPath();
        _cachedGreenSegmentsPath = new SKPath();
        _cachedYellowSegmentsPath = new SKPath();
        _cachedRedSegmentsPath = new SKPath();
        _cachedOutlinePath = new SKPath();
        _cachedPeakPath = new SKPath();
        _cachedShadowPath = new SKPath();
        _cachedReflectionPath = new SKPath();
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

    private void ApplyQualityBasedSettings()
    {
        switch (Quality)
        {
            case RenderQuality.Low:
                ApplyLowQualitySettings();
                break;

            case RenderQuality.Medium:
                ApplyMediumQualitySettings();
                break;

            case RenderQuality.High:
                ApplyHighQualitySettings();
                break;
        }
    }

    private void ApplyLowQualitySettings()
    {
        _enableShadows = Constants.Quality.LOW_ENABLE_SHADOWS;
        _enableReflections = Constants.Quality.LOW_ENABLE_REFLECTIONS;
        _segmentRoundness = Constants.Quality.LOW_SEGMENT_ROUNDNESS;
        _useSegmentedBars = Constants.Quality.LOW_USE_SEGMENTED_BARS;
        _smoothingFactor = Constants.Quality.LOW_SMOOTHING_FACTOR;
        _transitionSmoothness = Constants.Quality.LOW_TRANSITION_SMOOTHNESS;
    }

    private void ApplyMediumQualitySettings()
    {
        _enableShadows = Constants.Quality.MEDIUM_ENABLE_SHADOWS;
        _enableReflections = Constants.Quality.MEDIUM_ENABLE_REFLECTIONS;
        _segmentRoundness = Constants.Quality.MEDIUM_SEGMENT_ROUNDNESS;
        _useSegmentedBars = Constants.Quality.MEDIUM_USE_SEGMENTED_BARS;
        _smoothingFactor = Constants.Quality.MEDIUM_SMOOTHING_FACTOR;
        _transitionSmoothness = Constants.Quality.MEDIUM_TRANSITION_SMOOTHNESS;
    }

    private void ApplyHighQualitySettings()
    {
        _enableShadows = Constants.Quality.HIGH_ENABLE_SHADOWS;
        _enableReflections = Constants.Quality.HIGH_ENABLE_REFLECTIONS;
        _segmentRoundness = Constants.Quality.HIGH_SEGMENT_ROUNDNESS;
        _useSegmentedBars = Constants.Quality.HIGH_USE_SEGMENTED_BARS;
        _smoothingFactor = Constants.Quality.HIGH_SMOOTHING_FACTOR;
        _transitionSmoothness = Constants.Quality.HIGH_TRANSITION_SMOOTHNESS;
    }

    private void UpdatePaintProperties()
    {
        _peakPaint.IsAntialias = UseAntiAlias;
        _segmentShadowPaint.IsAntialias = UseAntiAlias;
        _outlinePaint.IsAntialias = UseAntiAlias;
        _reflectionPaint.IsAntialias = UseAntiAlias;
    }

    private bool ValidateRenderParameters(
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

        if (!_buffersInitialized)
        {
            InitializeBuffers(Max(barCount, INITIAL_BUFFER_SIZE));
        }

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
        float totalBarWidth,
        int _)
    {
        if (_renderBarValues == null || _renderPeaks == null || _currentBarCount == 0)
            return;

        int renderCount = Min(_currentBarCount, _renderBarValues.Length);
        if (renderCount <= 0)
            return;

        float totalGap = (_currentSegmentCount - 1) * SEGMENT_GAP;
        float segmentHeight = (info.Height - totalGap) / _currentSegmentCount * _scaleFactor;

        UpdateRenderingPaths(
            info,
            barWidth,
            barSpacing,
            totalBarWidth,
            segmentHeight,
            renderCount);

        RenderBarElements(canvas, info);
    }

    private void ProcessCanvasChanges(
        SKImageInfo info,
        SKCanvas canvas)
    {
        bool canvasSizeChanged = MathF.Abs(_lastCanvasRect.Height - info.Height) > 0.5f ||
                                MathF.Abs(_lastCanvasRect.Width - info.Width) > 0.5f;

        bool transformChanged = canvas.TotalMatrix != _lastTransform;
        if (transformChanged)
            _lastTransform = canvas.TotalMatrix;

        if (canvasSizeChanged)
        {
            _currentSegmentCount = (int)MathF.Max(1, (int)(info.Height / (_desiredSegmentHeight + SEGMENT_GAP)));
            CreateGradients(info.Height);
            _lastCanvasRect = info.Rect;
            _pathsNeedRebuild = true;
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

                var (data, barCount, canvasHeight) = ExtractPendingSpectrumData(ct);

                if (data != null && data.Length > 0)
                    ProcessSpectrumData(
                        data,
                        barCount,
                        canvasHeight);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, LOG_PREFIX, $"Calculation error: {ex.Message}");
                Thread.Sleep(100);
            }
        }
    }

    private (float[]? data, int barCount, int canvasHeight) ExtractPendingSpectrumData(CancellationToken ct)
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

        float[] scaledSpectrum = GetFloatBuffer(actualBarCount);
        ScaleSpectrum(
            spectrum,
            scaledSpectrum,
            actualBarCount,
            spectrumLength);

        float[] computedBarValues = GetFloatBuffer(actualBarCount);
        float[] computedPeaks = GetFloatBuffer(actualBarCount);
        DateTime currentTime = DateTime.Now;

        ProcessSpectrumAnimation(
            scaledSpectrum,
            computedBarValues,
            computedPeaks,
            currentTime,
            canvasHeight,
            actualBarCount);

        UpdateRenderingBuffers(
            computedBarValues,
            computedPeaks,
            actualBarCount);

        ReturnBufferToPool(scaledSpectrum);
        ReturnBufferToPool(computedBarValues);
        ReturnBufferToPool(computedPeaks);
        _pathsNeedRebuild = true;
    }

    private void UpdateRenderingPaths(
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        float totalBarWidth,
        float segmentHeight,
        int renderCount)
    {
        bool dimensionsChanged = MathF.Abs(_lastTotalBarWidth - totalBarWidth) > 0.01f ||
                                MathF.Abs(_lastBarWidth - barWidth) > 0.01f ||
                                MathF.Abs(_lastBarSpacing - barSpacing) > 0.01f ||
                                MathF.Abs(_lastSegmentHeight - segmentHeight) > 0.01f ||
                                _lastRenderCount != renderCount;

        if (_pathsNeedRebuild || dimensionsChanged)
        {
            RebuildPaths(
                info,
                barWidth,
                barSpacing,
                totalBarWidth,
                segmentHeight,
                renderCount);

            _lastTotalBarWidth = totalBarWidth;
            _lastBarWidth = barWidth;
            _lastBarSpacing = barSpacing;
            _lastSegmentHeight = segmentHeight;
            _lastRenderCount = renderCount;
            _pathsNeedRebuild = false;
        }
    }

    private void RebuildPaths(
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        float totalBarWidth,
        float segmentHeight,
        int renderCount)
    {
        ExecuteSafely(
            () =>
            {
                ResetAllPaths();

                float minVisibleValue = segmentHeight * 0.5f;

                if (!_useSegmentedBars)
                {
                    BuildSimpleBarPaths(
                        info,
                        barWidth,
                        barSpacing,
                        totalBarWidth,
                        renderCount,
                        minVisibleValue);
                }
                else
                {
                    BuildSegmentedBarPaths(
                        info,
                        barWidth,
                        barSpacing,
                        totalBarWidth,
                        segmentHeight,
                        renderCount,
                        minVisibleValue);
                }
            },
            "RebuildPaths",
            "Failed to rebuild rendering paths"
        );
    }

    private void BuildSimpleBarPaths(
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        float totalBarWidth,
        int renderCount,
        float minVisibleValue)
    {
        if (_renderBarValues == null || _renderPeaks == null ||
            _cachedBarPath == null || _cachedPeakPath == null)
            return;

        for (int i = 0; i < renderCount && i < _renderBarValues.Length; i++)
        {
            float x = i * totalBarWidth + barSpacing / 2;
            float barValue = _renderBarValues[i];

            if (barValue > minVisibleValue)
            {
                AddBarPath(
                    info,
                    x,
                    barWidth,
                    barValue);
            }

            if (i < _renderPeaks.Length)
            {
                AddPeakPath(
                    info,
                    i,
                    x,
                    barWidth,
                    minVisibleValue);
            }
        }
    }

    private void AddBarPath(
        SKImageInfo info,
        float x,
        float barWidth,
        float barValue)
    {
        if (_cachedBarPath == null)
            return;

        float barTop = info.Height - barValue;
        _cachedBarPath.AddRect(
            SKRect.Create(
                x,
                barTop,
                barWidth,
                barValue));
    }

    private void AddPeakPath(
        SKImageInfo info,
        int index,
        float x,
        float barWidth,
        float minVisibleValue)
    {
        if (_renderPeaks == null || _cachedPeakPath == null ||
            index >= _renderPeaks.Length)
            return;

        if (_renderPeaks[index] > minVisibleValue)
        {
            float peakY = info.Height - _renderPeaks[index];
            float peakHeight = 3f;
            _cachedPeakPath.AddRect(
                SKRect.Create(
                    x,
                    peakY - peakHeight,
                    barWidth,
                    peakHeight));
        }
    }

    private void BuildSegmentedBarPaths(
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        float totalBarWidth,
        float segmentHeight,
        int renderCount,
        float minVisibleValue)
    {
        if (_renderBarValues == null || _renderPeaks == null)
            return;

        int greenCount = (int)(_currentSegmentCount * 0.6);
        int yellowCount = (int)(_currentSegmentCount * 0.25);

        if (_enableReflections && info.Height > 200)
        {
            BuildReflectionPath(
                info,
                barWidth,
                barSpacing,
                totalBarWidth,
                segmentHeight,
                renderCount,
                minVisibleValue);
        }

        for (int i = 0; i < renderCount && i < _renderBarValues.Length; i++)
        {
            BuildBarSegments(
                info,
                i,
                barWidth,
                barSpacing,
                totalBarWidth,
                segmentHeight,
                minVisibleValue,
                greenCount,
                yellowCount);

            AddPeakPath(
                info,
                i,
                i * totalBarWidth + barSpacing / 2,
                barWidth,
                minVisibleValue);
        }
    }

    private void BuildBarSegments(
        SKImageInfo info,
        int barIndex,
        float barWidth,
        float barSpacing,
        float totalBarWidth,
        float segmentHeight,
        float minVisibleValue,
        int greenCount,
        int yellowCount)
    {
        if (_renderBarValues == null || barIndex >= _renderBarValues.Length)
            return;

        float x = barIndex * totalBarWidth + barSpacing / 2;
        float barValue = _renderBarValues[barIndex];

        if (barValue <= minVisibleValue)
            return;

        int segmentsToRender = (int)MathF.Min(
            (int)MathF.Ceiling(barValue / (segmentHeight + SEGMENT_GAP)),
            _currentSegmentCount);

        bool needShadow = _enableShadows && barValue > info.Height * 0.5f;

        for (int segIndex = 0; segIndex < segmentsToRender; segIndex++)
        {
            float segmentBottom = info.Height - segIndex * (segmentHeight + SEGMENT_GAP);
            float segmentTop = segmentBottom - segmentHeight;

            if (needShadow && _cachedShadowPath != null)
            {
                AddRoundRectToPath(
                    _cachedShadowPath,
                    x + 2,
                    segmentTop + 2,
                    barWidth,
                    segmentHeight);
            }

            SKPath? targetPath = DetermineSegmentColorPath(
                segIndex,
                greenCount,
                yellowCount);

            if (targetPath != null)
            {
                AddRoundRectToPath(
                    targetPath,
                    x,
                    segmentTop,
                    barWidth,
                    segmentHeight);
            }

            if (_cachedOutlinePath != null)
            {
                AddRoundRectToPath(
                    _cachedOutlinePath,
                    x,
                    segmentTop,
                    barWidth,
                    segmentHeight);
            }
        }
    }

    private SKPath? DetermineSegmentColorPath(
        int segmentIndex,
        int greenCount,
        int yellowCount)
    {
        return segmentIndex < greenCount
            ? _cachedGreenSegmentsPath
            : segmentIndex < greenCount + yellowCount
                ? _cachedYellowSegmentsPath
                : _cachedRedSegmentsPath;
    }

    private void BuildReflectionPath(
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        float totalBarWidth,
        float segmentHeight,
        int renderCount,
        float minVisibleValue)
    {
        if (_renderBarValues == null || _renderPeaks == null ||
            _cachedReflectionPath == null)
            return;

        int reflectionBarStep = (int)MathF.Max(1, renderCount / 60);

        for (int i = 0; i < renderCount; i += reflectionBarStep)
        {
            if (i < _renderBarValues.Length)
            {
                BuildSingleBarReflection(
                    info,
                    i,
                    barWidth,
                    barSpacing,
                    totalBarWidth,
                    segmentHeight,
                    minVisibleValue);
            }
        }
    }

    private void BuildSingleBarReflection(
        SKImageInfo info,
        int barIndex,
        float barWidth,
        float barSpacing,
        float totalBarWidth,
        float segmentHeight,
        float minVisibleValue)
    {
        if (_renderBarValues == null || _renderPeaks == null ||
            _cachedReflectionPath == null || barIndex >= _renderBarValues.Length)
            return;

        float x = barIndex * totalBarWidth + barSpacing / 2;
        float barValue = _renderBarValues[barIndex];

        if (barValue > minVisibleValue)
        {
            BuildSegmentReflections(
                info,
                x,
                barWidth,
                barValue,
                segmentHeight);
        }

        if (barIndex < _renderPeaks.Length)
        {
            BuildPeakReflection(
                info,
                barIndex,
                x,
                barWidth,
                minVisibleValue);
        }
    }

    private void BuildSegmentReflections(
        SKImageInfo info,
        float x,
        float barWidth,
        float barValue,
        float segmentHeight)
    {
        if (_cachedReflectionPath == null)
            return;

        int maxReflectionSegments = (int)MathF.Min(3, _currentSegmentCount);
        int segmentsToRender = (int)MathF.Min(
            (int)MathF.Ceiling(barValue / (segmentHeight + SEGMENT_GAP)),
            maxReflectionSegments);

        for (int segIndex = 0; segIndex < segmentsToRender; segIndex++)
        {
            float segmentBottom = info.Height - segIndex * (segmentHeight + SEGMENT_GAP);
            float segmentTop = segmentBottom - segmentHeight;
            var rect = SKRect.Create(
                x,
                segmentTop,
                barWidth,
                segmentHeight * _reflectionHeight);

            if (_segmentRoundness > 0)
                _cachedReflectionPath.AddRoundRect(
                    rect,
                    _segmentRoundness,
                    _segmentRoundness);
            else
                _cachedReflectionPath.AddRect(rect);
        }
    }

    private void BuildPeakReflection(
        SKImageInfo info,
        int barIndex,
        float x,
        float barWidth,
        float minVisibleValue)
    {
        if (_renderPeaks == null || _cachedReflectionPath == null ||
            barIndex >= _renderPeaks.Length)
            return;

        if (_renderPeaks[barIndex] > minVisibleValue)
        {
            float peakY = info.Height - _renderPeaks[barIndex];
            float peakHeight = 3f;
            _cachedReflectionPath.AddRect(
                SKRect.Create(
                    x,
                    peakY - peakHeight,
                    barWidth,
                    peakHeight * _reflectionHeight));
        }
    }

    private void AddRoundRectToPath(
        SKPath path,
        float x,
        float y,
        float width,
        float height)
    {
        if (path == null)
            return;

        var rect = SKRect.Create(x, y, width, height);
        if (_segmentRoundness > 0.5f)
            path.AddRoundRect(
                rect,
                _segmentRoundness,
                _segmentRoundness);
        else
            path.AddRect(rect);
    }

    private void ResetAllPaths()
    {
        _cachedBarPath?.Reset();
        _cachedGreenSegmentsPath?.Reset();
        _cachedYellowSegmentsPath?.Reset();
        _cachedRedSegmentsPath?.Reset();
        _cachedOutlinePath?.Reset();
        _cachedPeakPath?.Reset();
        _cachedShadowPath?.Reset();
        _cachedReflectionPath?.Reset();
    }

    private void RenderBarElements(
        SKCanvas canvas,
        SKImageInfo info)
    {
        if (_useSegmentedBars)
        {
            RenderSegmentedBars(canvas);
        }
        else
        {
            RenderSimpleBars(canvas);
        }

        RenderPeaks(canvas);

        if (_enableReflections && info.Height > 200 && _cachedReflectionPath != null)
        {
            RenderReflection(
                canvas,
                info);
        }
    }

    private void RenderPeaks(SKCanvas canvas)
    {
        if (_cachedPeakPath != null)
            canvas.DrawPath(_cachedPeakPath, _peakPaint);
    }

    private void RenderSegmentedBars(SKCanvas canvas)
    {
        RenderShadows(canvas);
        RenderColorSegments(canvas);
        RenderOutlines(canvas);
    }

    private void RenderShadows(SKCanvas canvas)
    {
        if (_enableShadows && _cachedShadowPath != null)
            canvas.DrawPath(_cachedShadowPath, _segmentShadowPaint);
    }

    private void RenderColorSegments(SKCanvas canvas)
    {
        if (_cachedGreenSegmentsPath != null)
            canvas.DrawPath(_cachedGreenSegmentsPath, EnsureSegmentPaint(0, _greenGradient));

        if (_cachedYellowSegmentsPath != null)
            canvas.DrawPath(_cachedYellowSegmentsPath, EnsureSegmentPaint(1, _yellowGradient));

        if (_cachedRedSegmentsPath != null)
            canvas.DrawPath(_cachedRedSegmentsPath, EnsureSegmentPaint(2, _redGradient));
    }

    private void RenderOutlines(SKCanvas canvas)
    {
        if (_cachedOutlinePath != null)
            canvas.DrawPath(_cachedOutlinePath, _outlinePaint);
    }

    private void RenderSimpleBars(SKCanvas canvas)
    {
        if (_cachedBarPath != null)
        {
            using var barPaint = CreateStandardPaint(SKColors.White);
            barPaint.Shader = _barGradient;
            canvas.DrawPath(_cachedBarPath, barPaint);
        }
    }

    private void RenderReflection(
        SKCanvas canvas,
        SKImageInfo info)
    {
        canvas.Save();
        canvas.Scale(1, -1);
        canvas.Translate(0, -info.Height * 2);

        PrepareReflectionPaint();

        canvas.DrawPath(_cachedReflectionPath, _reflectionPaint);
        canvas.Restore();
    }

    private void PrepareReflectionPaint()
    {
        if (_reflectionPaint.Color.Alpha == 0)
            _reflectionPaint.Color = new SKColor(255, 255, 255, (byte)(_reflectionOpacity * 255));

        if (_reflectionGradient != null)
            _reflectionPaint.Shader = _reflectionGradient;
    }

    private void CreateGradients(float height)
    {
        ExecuteSafely(
            () =>
            {
                DisposeGradients();
                CreateSegmentGradients(height);
                CreateCombinedGradients(height);
            },
            "CreateGradients",
            "Failed to create gradients"
        );
    }

    private void DisposeGradients()
    {
        _greenGradient?.Dispose();
        _yellowGradient?.Dispose();
        _redGradient?.Dispose();
        _reflectionGradient?.Dispose();
        _barGradient?.Dispose();
    }

    private void CreateSegmentGradients(float height)
    {
        _greenGradient = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(0, height / 3),
            _greenColors,
            null,
            SKShaderTileMode.Clamp);

        _yellowGradient = SKShader.CreateLinearGradient(
            new SKPoint(0, height / 3),
            new SKPoint(0, height * 2 / 3),
            _yellowColors,
            null,
            SKShaderTileMode.Clamp);

        _redGradient = SKShader.CreateLinearGradient(
            new SKPoint(0, height * 2 / 3),
            new SKPoint(0, height),
            _redColors,
            null,
            SKShaderTileMode.Clamp);
    }

    private void CreateCombinedGradients(float height)
    {
        SKColor[] reflectionColors = [
            new SKColor(255, 255, 255, (byte)(_reflectionOpacity * 255)),
            SKColors.Transparent
        ];

        _reflectionGradient = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(0, height * _reflectionHeight),
            reflectionColors,
            null,
            SKShaderTileMode.Clamp);

        _barGradient = SKShader.CreateLinearGradient(
            new SKPoint(0, height),
            new SKPoint(0, 0),
            _barColors,
            _barColorPositions,
            SKShaderTileMode.Clamp);
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
        if (!_buffersInitialized || _previousSpectrum == null || _velocities == null ||
            _peaks == null || _peakHoldTimes == null || _previousSpectrum.Length < actualBarCount)
        {
            Log(LogLevel.Debug, LOG_PREFIX, $"Reinitializing buffers for {actualBarCount} bars");

            _dataSemaphore.Wait();
            try
            {
                ReturnBufferToPool(_previousSpectrum);
                ReturnBufferToPool(_peaks);
                ReturnBufferToPool(_peakHoldTimes);
                ReturnBufferToPool(_velocities);

                _previousSpectrum = GetFloatBuffer(actualBarCount);
                _peaks = GetFloatBuffer(actualBarCount);
                _peakHoldTimes = GetDateTimeBuffer(actualBarCount);
                _velocities = GetFloatBuffer(actualBarCount);

                Array.Fill(_peakHoldTimes, DateTime.MinValue);
                Array.Fill(_velocities, 0f);
                _buffersInitialized = true;
            }
            finally
            {
                _dataSemaphore.Release();
            }
        }
    }

    private void ProcessSpectrumAnimation(
        float[] scaledSpectrum,
        float[] computedBarValues,
        float[] computedPeaks,
        DateTime currentTime,
        float canvasHeight,
        int actualBarCount)
    {
        if (!_buffersInitialized || _velocities == null || _previousSpectrum == null ||
            _peaks == null || _peakHoldTimes == null)
        {
            InitializeBuffersIfNeeded(actualBarCount);
            if (!_buffersInitialized)
                return;
        }

        float smoothFactor = _smoothingFactor * _animationSpeed;
        float peakFallRate = _peakFallSpeed * canvasHeight * _animationSpeed;
        double peakHoldTimeMs = PEAK_HOLD_TIME_MS;

        float maxChangeThreshold = canvasHeight * 0.3f;
        float velocityDamping = 0.8f * _transitionSmoothness;
        float springStiffness = 0.2f * (1 - _transitionSmoothness);

        const int batchSize = 64;
        for (int batchStart = 0; batchStart < actualBarCount; batchStart += batchSize)
        {
            int batchEnd = (int)MathF.Min(batchStart + batchSize, actualBarCount);
            ProcessSpectrumBatch(
                batchStart,
                batchEnd,
                scaledSpectrum,
                computedBarValues,
                computedPeaks,
                currentTime,
                canvasHeight,
                smoothFactor,
                peakFallRate,
                peakHoldTimeMs,
                maxChangeThreshold,
                velocityDamping,
                springStiffness);
        }
    }

    private void ProcessSpectrumBatch(
        int batchStart,
        int batchEnd,
        float[] scaledSpectrum,
        float[] computedBarValues,
        float[] computedPeaks,
        DateTime currentTime,
        float canvasHeight,
        float smoothFactor,
        float peakFallRate,
        double peakHoldTimeMs,
        float maxChangeThreshold,
        float velocityDamping,
        float springStiffness)
    {
        if (_velocities == null || _previousSpectrum == null ||
            _peaks == null || _peakHoldTimes == null)
            return;

        for (int i = batchStart; i < batchEnd; i++)
        {
            if (i >= _velocities.Length || i >= _previousSpectrum.Length)
                continue;

            ProcessSingleSpectrumValue(
                i,
                scaledSpectrum[i],
                _previousSpectrum[i],
                canvasHeight,
                smoothFactor,
                maxChangeThreshold,
                velocityDamping,
                springStiffness,
                computedBarValues);

            ProcessPeakValue(
                i,
                computedBarValues[i],
                computedPeaks,
                currentTime,
                peakFallRate,
                peakHoldTimeMs,
                canvasHeight,
                maxChangeThreshold);
        }
    }

    private void ProcessSingleSpectrumValue(
        int index,
        float targetValue,
        float currentValue,
        float canvasHeight,
        float smoothFactor,
        float maxChangeThreshold,
        float velocityDamping,
        float springStiffness,
        float[] computedBarValues)
    {
        if (_velocities == null || _previousSpectrum == null ||
            index >= _velocities.Length || index >= _previousSpectrum.Length)
            return;

        float delta = targetValue - currentValue;
        float adaptiveSmoothFactor = smoothFactor;
        float absDelta = MathF.Abs(delta * canvasHeight);

        if (absDelta > maxChangeThreshold)
        {
            float changeRatio = MathF.Min(1.0f, absDelta / (canvasHeight * 0.7f));
            adaptiveSmoothFactor = MathF.Max(
                smoothFactor * 0.3f,
                smoothFactor * (1.0f + changeRatio * 0.5f));
        }

        _velocities[index] = _velocities[index] * velocityDamping + delta * springStiffness;
        float newValue = currentValue + _velocities[index] + delta * adaptiveSmoothFactor;
        newValue = MathF.Max(0f, MathF.Min(1f, newValue));

        _previousSpectrum[index] = newValue;
        computedBarValues[index] = newValue * canvasHeight;
    }

    private void ProcessPeakValue(
        int index,
        float barValue,
        float[] computedPeaks,
        DateTime currentTime,
        float peakFallRate,
        double peakHoldTimeMs,
        float canvasHeight,
        float maxChangeThreshold)
    {
        if (_peaks == null || _peakHoldTimes == null ||
            index >= _peaks.Length || index >= _peakHoldTimes.Length)
            return;

        float currentPeak = _peaks[index];

        if (barValue > currentPeak)
        {
            UpdateNewPeak(
                index,
                barValue,
                currentTime);
        }
        else if ((currentTime - _peakHoldTimes[index]).TotalMilliseconds > peakHoldTimeMs)
        {
            UpdateFallingPeak(
                index,
                currentPeak,
                barValue,
                canvasHeight,
                maxChangeThreshold,
                peakFallRate);
        }

        computedPeaks[index] = _peaks[index];
    }

    private void UpdateNewPeak(
        int index,
        float barValue,
        DateTime currentTime)
    {
        if (_peaks == null || _peakHoldTimes == null ||
            index >= _peaks.Length || index >= _peakHoldTimes.Length)
            return;

        _peaks[index] = barValue;
        _peakHoldTimes[index] = currentTime;
    }

    private void UpdateFallingPeak(
        int index,
        float currentPeak,
        float barValue,
        float canvasHeight,
        float maxChangeThreshold,
        float peakFallRate)
    {
        if (_peaks == null || index >= _peaks.Length)
            return;

        float peakDelta = currentPeak - barValue;
        float adaptiveFallRate = peakFallRate;

        if (peakDelta > maxChangeThreshold)
            adaptiveFallRate = peakFallRate * (1.0f + peakDelta / canvasHeight * 2.0f);

        _peaks[index] = MathF.Max(0f, currentPeak - adaptiveFallRate);
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
            CopyComputedValues(
                computedBarValues,
                computedPeaks,
                actualBarCount);
            EnsureRenderingBuffers(actualBarCount);
        }
        finally
        {
            _dataSemaphore.Release();
        }
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

    private void CopyComputedValues(
        float[] computedBarValues,
        float[] computedPeaks,
        int actualBarCount)
    {
        if (_processingBarValues == null || _processingPeaks == null)
            return;

        int bytesToCopy = (int)(MathF.Min(actualBarCount,
            MathF.Min(_processingBarValues.Length, computedBarValues.Length)) * sizeof(float));

        Buffer.BlockCopy(
            computedBarValues,
            0,
            _processingBarValues,
            0,
            bytesToCopy);

        Buffer.BlockCopy(
            computedPeaks,
            0,
            _processingPeaks,
            0,
            bytesToCopy);

        _currentBarCount = actualBarCount;
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

        if (_processingBarValues != null && _processingPeaks != null &&
            _renderBarValues != null && _renderPeaks != null)
        {
            int bytesToCopy = (int)(MathF.Min(actualBarCount,
                MathF.Min(_processingBarValues.Length, _renderBarValues.Length)) * sizeof(float));

            Buffer.BlockCopy(
                _processingBarValues,
                0,
                _renderBarValues,
                0,
                bytesToCopy);

            Buffer.BlockCopy(
                _processingPeaks,
                0,
                _renderPeaks,
                0,
                bytesToCopy);
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
        int maxThreads = (int)MathF.Max(2, Environment.ProcessorCount / 2);

        Parallel.For(0, barCount, new ParallelOptions { MaxDegreeOfParallelism = maxThreads }, i =>
        {
            ScaleSpectrumForBar(
                i,
                spectrum,
                scaledSpectrum,
                barCount,
                spectrumLength,
                blockSize);
        });
    }

    private static void ScaleSpectrumForBar(
        int barIndex,
        float[] spectrum,
        float[] scaledSpectrum,
        int barCount,
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

        float average = sum / count;
        float weight = SPECTRUM_WEIGHT;
        scaledSpectrum[barIndex] = average * (1.0f - weight) + peak * weight;

        ApplyFrequencyBoost(barIndex, barCount, scaledSpectrum);

        scaledSpectrum[barIndex] = MathF.Min(1.0f, scaledSpectrum[barIndex]);
    }

    private static void ApplyFrequencyBoost(int barIndex, int barCount, float[] scaledSpectrum)
    {
        if (barIndex > barCount / 2)
        {
            float boost = 1.0f + (float)barIndex / barCount * BOOST_FACTOR;
            scaledSpectrum[barIndex] *= boost;
        }
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
        ReturnBufferToPool(_previousSpectrum);
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
        foreach (var paint in _segmentPaints.Values)
            paint.Dispose();
        _segmentPaints.Clear();

        _peakPaint.Dispose();
        _segmentShadowPaint.Dispose();
        _outlinePaint.Dispose();
        _reflectionPaint.Dispose();

        _cachedBarPath?.Dispose();
        _cachedGreenSegmentsPath?.Dispose();
        _cachedYellowSegmentsPath?.Dispose();
        _cachedRedSegmentsPath?.Dispose();
        _cachedOutlinePath?.Dispose();
        _cachedPeakPath?.Dispose();
        _cachedShadowPath?.Dispose();
        _cachedReflectionPath?.Dispose();
    }

    private void DisposeShaders()
    {
        _greenGradient?.Dispose();
        _yellowGradient?.Dispose();
        _redGradient?.Dispose();
        _reflectionGradient?.Dispose();
        _barGradient?.Dispose();
    }

    private SKPaint EnsureSegmentPaint(int index, SKShader? shader)
    {
        if (!_segmentPaints.TryGetValue(index, out var paint) || paint == null)
        {
            paint = new SKPaint
            {
                IsAntialias = UseAntiAlias,
                Style = SKPaintStyle.Fill,
                Shader = shader
            };
            _segmentPaints[index] = paint;
        }
        return paint;
    }
}