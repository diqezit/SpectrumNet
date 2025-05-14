#nullable enable

using static SpectrumNet.Views.Renderers.PolarRenderer.Constants;
using static SpectrumNet.Views.Renderers.PolarRenderer.Constants.Quality;
using Vector = System.Numerics.Vector;

namespace SpectrumNet.Views.Renderers;

public sealed class PolarRenderer : EffectSpectrumRenderer
{
    private static readonly Lazy<PolarRenderer> _instance = new(() => new PolarRenderer());
    private const string LOG_PREFIX = "PolarRenderer";

    private PolarRenderer() { }

    public static PolarRenderer GetInstance() => _instance.Value;

    public record Constants
    {
        public const string LOG_PREFIX = "PolarRenderer";

        public const float
            MIN_RADIUS = 30f,
            RADIUS_MULTIPLIER = 200f,
            INNER_RADIUS_RATIO = 0.5f,
            MAX_SPECTRUM_VALUE = 1.0f,
            SPECTRUM_SCALE = 2.0f,
            CHANGE_THRESHOLD = 0.01f,
            DEG_TO_RAD = (float)(PI / 180.0);

        public const float
            ROTATION_SPEED = 0.3f,
            TIME_STEP = 0.016f,
            TIME_MODIFIER = 0.01f,
            MODULATION_FACTOR = 0.3f,
            MODULATION_FREQ = 5f,
            PULSE_SPEED = 2.0f,
            PULSE_AMPLITUDE = 0.2f,
            PULSE_SCALE_MULTIPLIER = 0.1f,
            DASH_PHASE_SPEED = 0.5f;

        public const float
            DEFAULT_STROKE_WIDTH = 1.5f,
            CENTER_CIRCLE_SIZE = 6f,
            CENTER_CIRCLE_GLOW_MULTIPLIER = 0.8f,
            CENTER_CIRCLE_MAIN_MULTIPLIER = 0.7f,
            CENTER_CIRCLE_HIGHLIGHT_OFFSET_MULTIPLIER = 0.25f,
            CENTER_CIRCLE_HIGHLIGHT_SIZE_MULTIPLIER = 0.2f,
            DASH_LENGTH = 6.0f,
            HIGHLIGHT_FACTOR = 1.4f,
            GLOW_RADIUS = 8.0f,
            GLOW_SIGMA = 2.5f,
            GRADIENT_COLOR_MULTIPLIER = 0.7f;

        public const byte
            FILL_ALPHA = 120,
            GLOW_ALPHA = 80,
            HIGHLIGHT_ALPHA = 160,
            GRADIENT_END_ALPHA = 20;

        public const int
            MAX_POINT_COUNT = 120,
            POINT_COUNT_OVERLAY = 80,
            MIN_POINT_COUNT = 24,
            FRAME_AVERAGE_COUNT = 30;

        public const float
            MIN_BAR_WIDTH = 0.5f,
            MAX_BAR_WIDTH = 4.0f;

        public const float
            SMOOTHING_FACTOR_LOW = 0.10f,
            SMOOTHING_FACTOR_MEDIUM = 0.15f,
            SMOOTHING_FACTOR_HIGH = 0.20f;

        public const int
            MAX_POINTS_LOW = 40,
            MAX_POINTS_MEDIUM = 80,
            MAX_POINTS_HIGH = 120;

        public const float
            STROKE_MULTIPLIER_LOW = 0.75f,
            STROKE_MULTIPLIER_MEDIUM = 1.0f,
            STROKE_MULTIPLIER_HIGH = 1.25f;

        public const float
            PATH_SIMPLIFICATION_LOW = 0.5f,
            PATH_SIMPLIFICATION_MEDIUM = 0.25f,
            PATH_SIMPLIFICATION_HIGH = 0.0f;

        public static class Quality
        {
            public const bool
                LOW_USE_ADVANCED_EFFECTS = false,
                MEDIUM_USE_ADVANCED_EFFECTS = true,
                HIGH_USE_ADVANCED_EFFECTS = true;

            public const bool
                LOW_USE_ANTI_ALIAS = false,
                MEDIUM_USE_ANTI_ALIAS = true,
                HIGH_USE_ANTI_ALIAS = true;

            public const bool
                LOW_USE_GLOW = false,
                MEDIUM_USE_GLOW = false,
                HIGH_USE_GLOW = true;

            public const bool
                LOW_USE_HIGHLIGHT = false,
                MEDIUM_USE_HIGHLIGHT = true,
                HIGH_USE_HIGHLIGHT = true;

            public const bool
                LOW_USE_PULSE_EFFECT = false,
                MEDIUM_USE_PULSE_EFFECT = true,
                HIGH_USE_PULSE_EFFECT = true;

            public const bool
                LOW_USE_DASH_EFFECT = false,
                MEDIUM_USE_DASH_EFFECT = true,
                HIGH_USE_DASH_EFFECT = true;
        }
    }

    private readonly SemaphoreSlim _pathUpdateSemaphore = new(1, 1);

    private SKPath? _outerPath;
    private SKPath? _innerPath;
    private SKPaint? _fillPaint;
    private SKPaint? _strokePaint;
    private SKPaint? _centerPaint;
    private SKPaint? _glowPaint;
    private SKPaint? _highlightPaint;

    private SKShader? _gradientShader;
    private SKPathEffect? _dashEffect;
    private SKImageFilter? _glowFilter;
    private SKPicture? _cachedCenterCircle;

    private SKPoint[]? _outerPoints;
    private SKPoint[]? _innerPoints;
    private SKColor _lastBaseColor;
    private SKRect _centerCircleBounds;
    private SKRect _clipBounds;
    private float[]? _tempSpectrum;

    private float _rotation;
    private float _pulseEffect;
    private bool _pathsNeedUpdate;
    private int _currentPointCount;

    private float _localSmoothingFactor;
    private bool _useGlow;
    private bool _useHighlight;
    private bool _usePulseEffect;
    private bool _useDashEffect;
    private float _pathSimplification;
    private float _strokeMultiplier;
    private int _maxPoints;

    private Vector<float> _smoothingVec;
    private Vector<float> _oneMinusSmoothing;

    protected override void OnInitialize()
    {
        base.OnInitialize();
        InitializeResources();
        Log(LogLevel.Debug, LOG_PREFIX, "Initialized");
    }

    private void InitializeResources()
    {
        InitializePoints();
        InitializePaths();
        InitializePaints();
        InitializeSpectrum();
        InitializeRenderState();
        InitializeCenterCircle();
    }

    private void InitializePoints()
    {
        _outerPoints = new SKPoint[MAX_POINT_COUNT + 1];
        _innerPoints = new SKPoint[MAX_POINT_COUNT + 1];
    }

    private void InitializePaths()
    {
        _outerPath = _pathPool.Get();
        _innerPath = _pathPool.Get();
    }

    private void InitializePaints()
    {
        CreateFillPaint();
        CreateStrokePaint();
        CreateCenterPaint();
        CreateGlowPaint();
        CreateHighlightPaint();
    }

    private void CreateFillPaint() =>
        _fillPaint = new SKPaint
        {
            IsAntialias = base.UseAntiAlias,
            Style = SKPaintStyle.Fill
        };

    private void CreateStrokePaint() =>
        _strokePaint = new SKPaint
        {
            IsAntialias = base.UseAntiAlias,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = DEFAULT_STROKE_WIDTH * _strokeMultiplier,
            StrokeJoin = SKStrokeJoin.Round,
            StrokeCap = SKStrokeCap.Round
        };

    private void CreateCenterPaint() =>
        _centerPaint = new SKPaint
        {
            IsAntialias = base.UseAntiAlias,
            Style = SKPaintStyle.Fill
        };

    private void CreateGlowPaint() =>
        _glowPaint = new SKPaint
        {
            IsAntialias = base.UseAntiAlias,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = DEFAULT_STROKE_WIDTH * 1.5f * _strokeMultiplier,
            BlendMode = SKBlendMode.SrcOver
        };

    private void CreateHighlightPaint() =>
        _highlightPaint = new SKPaint
        {
            IsAntialias = base.UseAntiAlias,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = DEFAULT_STROKE_WIDTH * 0.5f * _strokeMultiplier,
            StrokeCap = SKStrokeCap.Round
        };

    private void InitializeSpectrum()
    {
        CreateSpectrumArrays();
        InitializeSmoothingVectors();
    }

    private void CreateSpectrumArrays()
    {
        base._processedSpectrum = new float[MAX_POINT_COUNT];
        base._previousSpectrum = new float[MAX_POINT_COUNT];
        _tempSpectrum = new float[MAX_POINT_COUNT];
    }

    private void InitializeSmoothingVectors()
    {
        _smoothingVec = new Vector<float>(_localSmoothingFactor);
        _oneMinusSmoothing = new Vector<float>(1 - _localSmoothingFactor);
    }

    private void InitializeRenderState()
    {
        _currentPointCount = MAX_POINT_COUNT;
        _pathsNeedUpdate = true;
    }

    private void InitializeCenterCircle()
    {
        _centerCircleBounds = new SKRect(
            -CENTER_CIRCLE_SIZE * 1.5f,
            -CENTER_CIRCLE_SIZE * 1.5f,
            CENTER_CIRCLE_SIZE * 1.5f,
            CENTER_CIRCLE_SIZE * 1.5f);

        UpdateCenterCircle(SKColors.White);
    }

    protected override void OnConfigurationChanged()
    {
        base.OnConfigurationChanged();

        bool isOverlayActive = base.IsOverlayActive;
        UpdateOverlayState(isOverlayActive);
        InvalidatePolarCachedResources();

        Log(LogLevel.Information, LOG_PREFIX,
            $"Configuration changed. Quality: {Quality}, Overlay: {isOverlayActive}");
    }

    private void UpdateOverlayState(bool isOverlayActive)
    {
        UpdatePointCountForOverlay();
        MarkPathsForUpdate();
    }

    private void UpdatePointCountForOverlay() =>
        _currentPointCount = base.IsOverlayActive
            ? POINT_COUNT_OVERLAY
            : Min(_maxPoints, MAX_POINT_COUNT);

    private void MarkPathsForUpdate() =>
        _pathsNeedUpdate = true;

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();

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

        ApplyQualityDependentChanges();

        Log(LogLevel.Debug, LOG_PREFIX, $"Quality settings applied. Quality: {Quality}");
    }

    private void ApplyQualityDependentChanges()
    {
        UpdatePaintSettings();
        UpdateSmoothingVectors();
        UpdateVisualEffects(_lastBaseColor);
        InvalidatePolarCachedResources();
        UpdatePointCountBasedOnOverlay();
        MarkPathsForUpdate();

        Log(LogLevel.Debug, LOG_PREFIX,
            $"Quality settings applied. Quality: {Quality}, " +
            $"UseGlow: {_useGlow}, UseHighlight: {_useHighlight}, " +
            $"UsePulseEffect: {_usePulseEffect}, UseDashEffect: {_useDashEffect}");
    }

    private void ApplyLowQualitySettings()
    {
        _localSmoothingFactor = SMOOTHING_FACTOR_LOW;
        _maxPoints = MAX_POINTS_LOW;
        _strokeMultiplier = STROKE_MULTIPLIER_LOW;
        _useGlow = LOW_USE_GLOW;
        _useHighlight = LOW_USE_HIGHLIGHT;
        _usePulseEffect = LOW_USE_PULSE_EFFECT;
        _useDashEffect = LOW_USE_DASH_EFFECT;
        _pathSimplification = PATH_SIMPLIFICATION_LOW;
    }

    private void ApplyMediumQualitySettings()
    {
        _localSmoothingFactor = SMOOTHING_FACTOR_MEDIUM;
        _maxPoints = MAX_POINTS_MEDIUM;
        _strokeMultiplier = STROKE_MULTIPLIER_MEDIUM;
        _useGlow = MEDIUM_USE_GLOW;
        _useHighlight = MEDIUM_USE_HIGHLIGHT;
        _usePulseEffect = MEDIUM_USE_PULSE_EFFECT;
        _useDashEffect = MEDIUM_USE_DASH_EFFECT;
        _pathSimplification = PATH_SIMPLIFICATION_MEDIUM;
    }

    private void ApplyHighQualitySettings()
    {
        _localSmoothingFactor = SMOOTHING_FACTOR_HIGH;
        _maxPoints = MAX_POINTS_HIGH;
        _strokeMultiplier = STROKE_MULTIPLIER_HIGH;
        _useGlow = HIGH_USE_GLOW;
        _useHighlight = HIGH_USE_HIGHLIGHT;
        _usePulseEffect = HIGH_USE_PULSE_EFFECT;
        _useDashEffect = HIGH_USE_DASH_EFFECT;
        _pathSimplification = PATH_SIMPLIFICATION_HIGH;
    }

    private void UpdatePointCountBasedOnOverlay() =>
        _currentPointCount = base.IsOverlayActive
            ? POINT_COUNT_OVERLAY
            : Min(_maxPoints, MAX_POINT_COUNT);

    private void UpdateSmoothingVectors()
    {
        _smoothingVec = new Vector<float>(_localSmoothingFactor);
        _oneMinusSmoothing = new Vector<float>(1 - _localSmoothingFactor);
    }

    private void UpdatePaintSettings()
    {
        if (_fillPaint == null
            || _strokePaint == null
            || _centerPaint == null
            || _glowPaint == null
            || _highlightPaint == null)
            return;

        UpdateAllPaintSettings();
    }

    private void UpdateAllPaintSettings()
    {
        UpdateFillPaintSettings();
        UpdateStrokePaintSettings();
        UpdateCenterPaintSettings();
        UpdateGlowPaintSettings();
        UpdateHighlightPaintSettings();
    }

    private void UpdateFillPaintSettings() =>
        _fillPaint!.IsAntialias = base.UseAntiAlias;

    private void UpdateStrokePaintSettings()
    {
        _strokePaint!.IsAntialias = base.UseAntiAlias;
        _strokePaint!.StrokeWidth = DEFAULT_STROKE_WIDTH * _strokeMultiplier;
    }

    private void UpdateCenterPaintSettings() =>
        _centerPaint!.IsAntialias = base.UseAntiAlias;

    private void UpdateGlowPaintSettings()
    {
        _glowPaint!.IsAntialias = base.UseAntiAlias;
        _glowPaint!.StrokeWidth = DEFAULT_STROKE_WIDTH * 1.5f * _strokeMultiplier;
    }

    private void UpdateHighlightPaintSettings()
    {
        _highlightPaint!.IsAntialias = base.UseAntiAlias;
        _highlightPaint!.StrokeWidth = DEFAULT_STROKE_WIDTH * 0.5f * _strokeMultiplier;
    }

    private void InvalidatePolarCachedResources()
    {
        DisposeAllCachedResources();
        MarkPathsForUpdate();
    }

    protected override void OnInvalidateCachedResources()
    {
        base.OnInvalidateCachedResources();
        InvalidatePolarCachedResources();
        Log(LogLevel.Debug, LOG_PREFIX, "Cached resources invalidated");
    }

    private void DisposeAllCachedResources()
    {
        DisposeCircleCache();
        DisposeDashEffect();
        DisposeGradientShader();
        DisposeGlowFilter();
    }

    private void DisposeCircleCache()
    {
        _cachedCenterCircle?.Dispose();
        _cachedCenterCircle = null;
    }

    private void DisposeDashEffect()
    {
        _dashEffect?.Dispose();
        _dashEffect = null;
    }

    private void DisposeGradientShader()
    {
        _gradientShader?.Dispose();
        _gradientShader = null;
    }

    private void DisposeGlowFilter()
    {
        _glowFilter?.Dispose();
        _glowFilter = null;
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
        ExecuteSafely(
            () =>
            {
                UpdateState(spectrum, info, barWidth, barCount, paint);
                RenderFrame(canvas, info);
            },
            nameof(RenderEffect),
            "Error during rendering");
    }

    private void UpdateState(
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        int barCount,
        SKPaint paint)
    {
        int safeBarCount = GetSafeBarCount(barCount);

        PrepareClipBounds();
        ProcessSpectrumAndUpdatePaths(spectrum, safeBarCount, info);
        UpdateVisualEffects(paint.Color);
        UpdateEffectParameters(barWidth);
    }

    private int GetSafeBarCount(int barCount) =>
        Min(Max(barCount, MIN_POINT_COUNT), _maxPoints);

    private void RenderFrame(
        SKCanvas canvas,
        SKImageInfo info)
    {
        if (!AreRenderResourcesValid()) return;
        if (IsCanvasOutOfBounds(canvas, info)) return;

        ApplyDashEffectIfNeeded();

        canvas.Save();
        RenderGraphWithTransform(canvas, info);
        canvas.Restore();
    }

    private void PrepareClipBounds()
    {
        float maxRadius = MIN_RADIUS + MAX_SPECTRUM_VALUE *
            RADIUS_MULTIPLIER * (1 + MODULATION_FACTOR);

        _clipBounds = new SKRect(
            -maxRadius, -maxRadius,
            maxRadius, maxRadius);
    }

    private static bool IsCanvasOutOfBounds(SKCanvas canvas, SKImageInfo info)
    {
        SKRect canvasBounds = new(0, 0, info.Width, info.Height);
        return canvas.QuickReject(canvasBounds);
    }

    private void ProcessSpectrumAndUpdatePaths(
        float[] spectrum,
        int safeBarCount,
        SKImageInfo info)
    {
        Task processTask = Task.Run(() => ProcessSpectrum(spectrum, safeBarCount));
        TryUpdatePolarPathsIfNeeded(info, safeBarCount);
        processTask.Wait();
    }

    private void TryUpdatePolarPathsIfNeeded(SKImageInfo info, int safeBarCount)
    {
        if (!_pathsNeedUpdate) return;

        bool lockAcquired = _pathUpdateSemaphore.Wait(0);
        if (lockAcquired)
        {
            try
            {
                UpdatePolarPaths(info, safeBarCount);
                _pathsNeedUpdate = false;
            }
            finally
            {
                _pathUpdateSemaphore.Release();
            }
        }
    }

    private bool AreRenderResourcesValid() =>
        _outerPath != null && _innerPath != null &&
        _fillPaint != null && _strokePaint != null &&
        _centerPaint != null && _cachedCenterCircle != null &&
        _glowPaint != null && _highlightPaint != null;

    private void UpdateEffectParameters(float barWidth)
    {
        float safeBarWidth = Clamp(barWidth, MIN_BAR_WIDTH, MAX_BAR_WIDTH);
        UpdatePulseEffect();
        UpdateStrokeWidths(safeBarWidth);
    }

    private void UpdatePulseEffect() =>
        _pulseEffect = _usePulseEffect
            ? MathF.Sin(base._time * PULSE_SPEED) * PULSE_AMPLITUDE + 1.0f
            : 1.0f;

    private void UpdateStrokeWidths(float barWidth)
    {
        if (_strokePaint == null) return;

        UpdateMainStrokeWidth(barWidth);
        UpdateGlowStrokeWidth(barWidth);
        UpdateHighlightStrokeWidth(barWidth);
    }

    private void UpdateMainStrokeWidth(float barWidth) =>
        _strokePaint!.StrokeWidth = barWidth * _pulseEffect * _strokeMultiplier;

    private void UpdateGlowStrokeWidth(float barWidth)
    {
        if (_useGlow && _glowPaint != null)
        {
            _glowPaint.StrokeWidth = barWidth * 1.5f * _pulseEffect * _strokeMultiplier;
        }
    }

    private void UpdateHighlightStrokeWidth(float barWidth)
    {
        if (_useHighlight && _highlightPaint != null)
        {
            _highlightPaint.StrokeWidth = barWidth * 0.5f * _pulseEffect * _strokeMultiplier;
        }
    }

    private void ApplyDashEffectIfNeeded()
    {
        if (_strokePaint == null) return;

        _strokePaint.PathEffect = (_useDashEffect && _dashEffect != null)
            ? _dashEffect
            : null;
    }

    private void RenderGraphWithTransform(SKCanvas canvas, SKImageInfo info)
    {
        canvas.Translate(info.Width / 2f, info.Height / 2f);
        canvas.RotateDegrees(_rotation);

        if (!canvas.QuickReject(_clipBounds))
        {
            RenderGraphElements(canvas);
            RenderCenterCircleWithPulse(canvas);
        }
    }

    private void RenderGraphElements(SKCanvas canvas)
    {
        if (_useGlow
            && _useHighlight
            && Quality == RenderQuality.High)
        {
            RenderHighQualityGraph(canvas);
        }
        else
        {
            RenderStandardGraph(canvas);
        }
    }

    private void RenderHighQualityGraph(SKCanvas canvas)
    {
        using var recorder = new SKPictureRecorder();
        SKCanvas pictureCanvas = recorder.BeginRecording(_clipBounds);

        RenderGraphElementsOnCanvas(pictureCanvas);

        using var combinedPicture = recorder.EndRecording();
        canvas.DrawPicture(combinedPicture);
    }

    private void RenderGraphElementsOnCanvas(SKCanvas canvas)
    {
        RenderGlowEffectIfNeeded(canvas);
        RenderOuterPath(canvas);
        RenderInnerPathWithDash(canvas);
        RenderHighlightIfNeeded(canvas);
    }

    private void RenderGlowEffectIfNeeded(SKCanvas canvas)
    {
        if (_useGlow && _glowPaint != null && _outerPath != null)
        {
            canvas.DrawPath(_outerPath, _glowPaint);
        }
    }

    private void RenderOuterPath(SKCanvas canvas)
    {
        if (_outerPath == null || _fillPaint == null || _strokePaint == null) return;

        canvas.DrawPath(_outerPath, _fillPaint);
        canvas.DrawPath(_outerPath, _strokePaint);
    }

    private void RenderInnerPathWithDash(SKCanvas canvas)
    {
        if (_innerPath == null || _strokePaint == null) return;

        SaveAndApplyDashEffect(canvas);
    }

    private void SaveAndApplyDashEffect(SKCanvas canvas)
    {
        if (_strokePaint == null || _innerPath == null) return;

        SKPathEffect? originalEffect = _strokePaint.PathEffect;

        if (_dashEffect != null)
            _strokePaint.PathEffect = _dashEffect;

        canvas.DrawPath(_innerPath, _strokePaint);
        _strokePaint.PathEffect = originalEffect;
    }

    private void RenderHighlightIfNeeded(SKCanvas canvas)
    {
        if (_useHighlight && _highlightPaint != null && _innerPath != null)
        {
            canvas.DrawPath(_innerPath, _highlightPaint);
        }
    }

    private void RenderStandardGraph(SKCanvas canvas)
    {
        RenderGlowEffectIfNeeded(canvas);
        RenderMainPaths(canvas);
        RenderHighlightIfNeeded(canvas);
    }

    private void RenderMainPaths(SKCanvas canvas)
    {
        if (_outerPath == null || _innerPath == null ||
            _fillPaint == null || _strokePaint == null) return;

        DrawOuterPathWithFillAndStroke(canvas);
        DrawInnerPathWithDash(canvas);
    }

    private void DrawOuterPathWithFillAndStroke(SKCanvas canvas)
    {
        if (_outerPath == null || _fillPaint == null || _strokePaint == null) return;

        canvas.DrawPath(_outerPath, _fillPaint);
        canvas.DrawPath(_outerPath, _strokePaint);
    }

    private void DrawInnerPathWithDash(SKCanvas canvas)
    {
        if (_innerPath == null || _strokePaint == null) return;

        SKPathEffect? originalEffect = null;

        if (_useDashEffect && _dashEffect != null)
        {
            originalEffect = _strokePaint.PathEffect;
            _strokePaint.PathEffect = _dashEffect;
        }

        canvas.DrawPath(_innerPath, _strokePaint);

        if (originalEffect != null)
        {
            _strokePaint.PathEffect = null;
        }
    }

    private void RenderCenterCircleWithPulse(SKCanvas canvas)
    {
        if (_cachedCenterCircle == null) return;

        float pulseScale = CalculatePulseScale();

        canvas.Save();
        canvas.Scale(pulseScale, pulseScale);
        canvas.DrawPicture(_cachedCenterCircle);
        canvas.Restore();
    }

    private float CalculatePulseScale() =>
        _usePulseEffect
            ? 1.0f + MathF.Sin(base._time * PULSE_SPEED * 0.5f) * PULSE_SCALE_MULTIPLIER
            : 1.0f;

    private void UpdatePolarPaths(SKImageInfo _, int barCount)
    {
        if (!ArePathResourcesValid()) return;

        UpdateAnimationState();

        CalculatePathParameters(
            barCount,
            out int effectivePointCount,
            out int skipFactor,
            out int actualPoints,
            out float angleStep);

        GeneratePathPoints(effectivePointCount, skipFactor, actualPoints, angleStep);
        CreatePathsFromPoints(effectivePointCount, skipFactor, actualPoints);
    }

    private bool ArePathResourcesValid() =>
        base._processedSpectrum != null && _outerPath != null && _innerPath != null &&
        _outerPoints != null && _innerPoints != null;

    private void UpdateAnimationState()
    {
        base._time += TIME_STEP;
        _rotation += ROTATION_SPEED * base._time * TIME_MODIFIER;
    }

    private void CalculatePathParameters(
        int barCount,
        out int effectivePointCount,
        out int skipFactor,
        out int actualPoints,
        out float angleStep)
    {
        effectivePointCount = Min(barCount, _currentPointCount);

        skipFactor = CalculateSkipFactor();

        actualPoints = effectivePointCount / skipFactor;
        angleStep = 360f / actualPoints;
    }

    private int CalculateSkipFactor() =>
        _pathSimplification > 0
            ? Max(1, (int)(1.0f / (1.0f - _pathSimplification)))
            : 1;

    private void GeneratePathPoints(
        int effectivePointCount,
        int skipFactor,
        int _,
        float angleStep)
    {
        for (int i = 0, pointIndex = 0; i <= effectivePointCount; i += skipFactor, pointIndex++)
        {
            if (pointIndex >= _outerPoints!.Length || pointIndex >= _innerPoints!.Length)
                break;

            float angle = pointIndex * angleStep * DEG_TO_RAD;
            float cosAngle = MathF.Cos(angle);
            float sinAngle = MathF.Sin(angle);

            GeneratePointForIndex(
                effectivePointCount,
                pointIndex,
                i,
                cosAngle,
                sinAngle,
                angleStep);
        }
    }

    private void GeneratePointForIndex(
        int effectivePointCount,
        int pointIndex,
        int i,
        float cosAngle,
        float sinAngle,
        float angleStep)
    {
        int spectrumIndex = i % effectivePointCount;
        float spectrumValue = GetSpectrumValueForIndex(spectrumIndex);

        float timeOffset = base._time * 0.5f + pointIndex * 0.1f;

        GenerateOuterPoint(pointIndex, spectrumValue, timeOffset, angleStep, cosAngle, sinAngle);
        GenerateInnerPoint(pointIndex, spectrumValue, timeOffset, angleStep, cosAngle, sinAngle);
    }

    private float GetSpectrumValueForIndex(int spectrumIndex) =>
        spectrumIndex < base._processedSpectrum!.Length
            ? base._processedSpectrum[spectrumIndex]
            : 0f;

    private void GenerateOuterPoint(
        int pointIndex,
        float spectrumValue,
        float timeOffset,
        float angleStep,
        float cosAngle,
        float sinAngle)
    {
        float modulation = CalculateModulation(pointIndex, angleStep, timeOffset);
        float outerRadius = MIN_RADIUS + spectrumValue * modulation * RADIUS_MULTIPLIER;

        if (pointIndex < _outerPoints!.Length)
        {
            _outerPoints[pointIndex] = new SKPoint(
                outerRadius * cosAngle,
                outerRadius * sinAngle);
        }
    }

    private void GenerateInnerPoint(
        int pointIndex,
        float spectrumValue,
        float timeOffset,
        float angleStep,
        float cosAngle,
        float sinAngle)
    {
        float innerSpectrumValue = spectrumValue * INNER_RADIUS_RATIO;
        float innerModulation = CalculateInnerModulation(pointIndex, angleStep, timeOffset);

        float innerRadius = MIN_RADIUS + innerSpectrumValue * innerModulation * RADIUS_MULTIPLIER;

        if (pointIndex < _innerPoints!.Length)
        {
            _innerPoints[pointIndex] = new SKPoint(
                innerRadius * cosAngle,
                innerRadius * sinAngle);
        }
    }

    private float CalculateModulation(int pointIndex, float angleStep, float timeOffset)
    {
        if (Quality == RenderQuality.Low)
        {
            return 1.0f;
        }

        float modulation = 1 + MODULATION_FACTOR *
            MathF.Sin(pointIndex * angleStep * MODULATION_FREQ * DEG_TO_RAD + base._time * 2);

        if (_usePulseEffect)
        {
            modulation += PULSE_AMPLITUDE * 0.5f * MathF.Sin(timeOffset);
        }

        return modulation;
    }

    private float CalculateInnerModulation(int pointIndex, float angleStep, float timeOffset)
    {
        if (Quality == RenderQuality.Low)
        {
            return 1.0f;
        }

        float innerModulation = 1 + MODULATION_FACTOR *
            MathF.Sin(pointIndex * angleStep * MODULATION_FREQ * DEG_TO_RAD + base._time * 2 + MathF.PI);

        if (_usePulseEffect)
        {
            innerModulation += PULSE_AMPLITUDE * 0.5f * MathF.Sin(timeOffset + MathF.PI);
        }

        return innerModulation;
    }

    private void CreatePathsFromPoints(
        int effectivePointCount,
        int skipFactor,
        int actualPoints)
    {
        ResetPaths();

        try
        {
            CreatePathsUsingPoly(actualPoints);
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, LOG_PREFIX, $"Failed to create path: {ex.Message}");
            CreatePathsUsingLines(effectivePointCount, skipFactor);
        }
    }

    private void ResetPaths()
    {
        _outerPath!.Reset();
        _innerPath!.Reset();
    }

    private void CreatePathsUsingPoly(int actualPoints)
    {
        int pointsToUse = Min(actualPoints + 1, _outerPoints!.Length);

        SKPoint[] outerPointsSlice = CreatePointsSlice(_outerPoints!, pointsToUse);
        SKPoint[] innerPointsSlice = CreatePointsSlice(_innerPoints!, pointsToUse);

        _outerPath!.AddPoly(outerPointsSlice, true);
        _innerPath!.AddPoly(innerPointsSlice, true);
    }

    private static SKPoint[] CreatePointsSlice(SKPoint[] sourcePoints, int count)
    {
        SKPoint[] slice = new SKPoint[count];
        Array.Copy(sourcePoints, slice, count);
        return slice;
    }

    private void CreatePathsUsingLines(int effectivePointCount, int skipFactor)
    {
        for (int i = 0, pointIndex = 0; i <= effectivePointCount; i += skipFactor, pointIndex++)
        {
            int safeIndex = Min(pointIndex, _outerPoints!.Length - 1);

            if (pointIndex == 0)
            {
                InitializePathStartPoints(safeIndex);
            }
            else
            {
                AddLineToPathPoints(safeIndex);
            }
        }

        ClosePaths();
    }

    private void InitializePathStartPoints(int safeIndex)
    {
        _outerPath!.MoveTo(_outerPoints![safeIndex]);
        _innerPath!.MoveTo(_innerPoints![safeIndex]);
    }

    private void AddLineToPathPoints(int safeIndex)
    {
        _outerPath!.LineTo(_outerPoints![safeIndex]);
        _innerPath!.LineTo(_innerPoints![safeIndex]);
    }

    private void ClosePaths()
    {
        _outerPath!.Close();
        _innerPath!.Close();
    }

    private void ProcessSpectrum(float[] spectrum, int barCount)
    {
        if (_disposed || _tempSpectrum == null || base._previousSpectrum == null ||
            base._processedSpectrum == null)
            return;

        try
        {
            _spectrumSemaphore.Wait();
            ProcessAndSmoothSpectrum(spectrum, barCount);
        }
        finally
        {
            _spectrumSemaphore.Release();
        }
    }

    private void ProcessAndSmoothSpectrum(float[] spectrum, int barCount)
    {
        int pointCount = Min(barCount, _currentPointCount);
        ExtractSpectrumPoints(spectrum, pointCount);
        float maxChange = SmoothSpectrumSIMD(pointCount);

        if (maxChange > CHANGE_THRESHOLD)
            _pathsNeedUpdate = true;
    }

    private void ExtractSpectrumPoints(float[] spectrum, int pointCount)
    {
        if (_tempSpectrum == null)
        {
            Log(LogLevel.Error, LOG_PREFIX, "Temporary spectrum array is null");
            return;
        }

        for (int i = 0; i < pointCount && i < _tempSpectrum.Length; i++)
        {
            ProcessSpectrumPoint(spectrum, i, pointCount);
        }
    }

    private void ProcessSpectrumPoint(float[] spectrum, int pointIndex, int pointCount)
    {
        float spectrumIndex = pointIndex * spectrum.Length / (2f * pointCount);
        int baseIndex = (int)spectrumIndex;
        float fraction = spectrumIndex - baseIndex;

        _tempSpectrum![pointIndex] = CalculateInterpolatedValue(spectrum, baseIndex, fraction);
        _tempSpectrum[pointIndex] = Min(_tempSpectrum[pointIndex] * SPECTRUM_SCALE, MAX_SPECTRUM_VALUE);
    }

    private static float CalculateInterpolatedValue(
        float[] spectrum,
        int baseIndex,
        float fraction)
    {
        if (baseIndex >= spectrum.Length / 2 - 1)
        {
            return spectrum[Min(spectrum.Length / 2 - 1, spectrum.Length - 1)];
        }
        else if (baseIndex + 1 < spectrum.Length)
        {
            return spectrum[baseIndex] * (1 - fraction) + spectrum[baseIndex + 1] * fraction;
        }
        else
        {
            return spectrum[baseIndex];
        }
    }

    private float SmoothSpectrumSIMD(int pointCount)
    {
        float maxChange = 0f;

        if (_tempSpectrum == null || base._previousSpectrum == null || base._processedSpectrum == null)
        {
            Log(LogLevel.Error, LOG_PREFIX, "Spectrum data arrays are null");
            return maxChange;
        }

        int safePointCount = GetSafeSpectrumPointCount(pointCount);

        if (IsHardwareAccelerated && safePointCount >= Vector<float>.Count)
        {
            ProcessSpectrumSIMD(safePointCount, ref maxChange);
        }
        else
        {
            ProcessSpectrumStandard(safePointCount, ref maxChange);
        }

        return maxChange;
    }

    private int GetSafeSpectrumPointCount(int pointCount) =>
        Min(
            pointCount,
            Min(
                _tempSpectrum!.Length,
                Min(base._previousSpectrum!.Length, base._processedSpectrum!.Length)));

    private void ProcessSpectrumSIMD(int safePointCount, ref float maxChange)
    {
        for (int i = 0; i < safePointCount; i += Vector<float>.Count)
        {
            int remaining = Min(Vector<float>.Count, safePointCount - i);

            if (remaining < Vector<float>.Count)
            {
                ProcessSpectrumRemainder(i, remaining, ref maxChange);
            }
            else
            {
                ProcessSpectrumVectorized(i, ref maxChange);
            }
        }
    }

    private void ProcessSpectrumVectorized(int startIndex, ref float maxChange)
    {
        try
        {
            Vector<float> current = new(_tempSpectrum!, startIndex);
            Vector<float> previous = new(base._previousSpectrum!, startIndex);
            Vector<float> smoothed = previous * _oneMinusSmoothing + current * _smoothingVec;

            UpdateMaxChangeFromVector(ref maxChange, previous, smoothed);
            CopyVectorResults(startIndex, smoothed);
        }
        catch (NullReferenceException)
        {
            Log(LogLevel.Error, LOG_PREFIX, "Null reference in SIMD processing");
            ProcessSpectrumRemainder(startIndex, Vector<float>.Count, ref maxChange);
        }
    }

    private static void UpdateMaxChangeFromVector(
        ref float maxChange,
        Vector<float> previous,
        Vector<float> smoothed)
    {
        Vector<float> change = Vector.Abs(smoothed - previous);
        float batchMaxChange = CalculateMaxChangeFromVector(change);
        maxChange = Max(maxChange, batchMaxChange);
    }

    private static float CalculateMaxChangeFromVector(Vector<float> change)
    {
        float batchMaxChange = 0f;
        for (int j = 0; j < Vector<float>.Count; j++)
        {
            if (change[j] > batchMaxChange)
                batchMaxChange = change[j];
        }
        return batchMaxChange;
    }

    private void CopyVectorResults(int startIndex, Vector<float> smoothed)
    {
        smoothed.CopyTo(base._processedSpectrum!, startIndex);
        smoothed.CopyTo(base._previousSpectrum!, startIndex);
    }

    private void ProcessSpectrumRemainder(int startIndex, int count, ref float maxChange)
    {
        for (int j = 0; j < count; j++)
        {
            float newValue = base._previousSpectrum![startIndex + j] * (1 - _localSmoothingFactor) +
                           _tempSpectrum![startIndex + j] * _localSmoothingFactor;

            float change = Abs(newValue - base._previousSpectrum[startIndex + j]);
            maxChange = Max(maxChange, change);

            base._processedSpectrum![startIndex + j] = newValue;
            base._previousSpectrum[startIndex + j] = newValue;
        }
    }

    private void ProcessSpectrumStandard(int safePointCount, ref float maxChange)
    {
        for (int i = 0; i < safePointCount; i++)
        {
            float newValue = base._previousSpectrum![i] * (1 - _localSmoothingFactor) +
                           _tempSpectrum![i] * _localSmoothingFactor;

            float change = Abs(newValue - base._previousSpectrum[i]);
            maxChange = Max(maxChange, change);

            base._processedSpectrum![i] = newValue;
            base._previousSpectrum[i] = newValue;
        }
    }

    private void UpdateCenterCircle(SKColor baseColor)
    {
        if (_centerPaint == null) return;

        DisposeCircleCache();
        CreateGlowFilter();
        DrawCenterCircle(baseColor);
    }

    private void CreateGlowFilter()
    {
        float effectiveGlowRadius = _useGlow ? GLOW_RADIUS : GLOW_RADIUS * 0.5f;
        float effectiveGlowSigma = _useGlow ? GLOW_SIGMA : GLOW_SIGMA * 0.5f;

        _glowFilter?.Dispose();
        _glowFilter = SKImageFilter.CreateBlur(effectiveGlowRadius, effectiveGlowSigma);
    }

    private void DrawCenterCircle(SKColor baseColor)
    {
        using var recorder = new SKPictureRecorder();
        SKCanvas pictureCanvas = recorder.BeginRecording(_centerCircleBounds);

        DrawGlowCircleIfNeeded(pictureCanvas, baseColor);
        DrawMainCenterCircle(pictureCanvas);
        DrawHighlightCircleIfNeeded(pictureCanvas);

        _cachedCenterCircle = recorder.EndRecording();
    }

    private void DrawGlowCircleIfNeeded(SKCanvas pictureCanvas, SKColor baseColor)
    {
        if (!_useGlow) return;

        using var glowPaint = new SKPaint
        {
            IsAntialias = base.UseAntiAlias,
            Style = SKPaintStyle.Fill,
            Color = baseColor.WithAlpha(GLOW_ALPHA),
            ImageFilter = _glowFilter
        };

        pictureCanvas.DrawCircle(
            0,
            0,
            CENTER_CIRCLE_SIZE * CENTER_CIRCLE_GLOW_MULTIPLIER,
            glowPaint);
    }

    private void DrawMainCenterCircle(SKCanvas pictureCanvas) =>
        pictureCanvas.DrawCircle(
            0,
            0,
            CENTER_CIRCLE_SIZE * CENTER_CIRCLE_MAIN_MULTIPLIER,
            _centerPaint!);

    private void DrawHighlightCircleIfNeeded(SKCanvas pictureCanvas)
    {
        if (!_useHighlight) return;

        using var highlightPaint = new SKPaint
        {
            IsAntialias = base.UseAntiAlias,
            Style = SKPaintStyle.Fill,
            Color = SKColors.White.WithAlpha(HIGHLIGHT_ALPHA)
        };

        pictureCanvas.DrawCircle(
            -CENTER_CIRCLE_SIZE * CENTER_CIRCLE_HIGHLIGHT_OFFSET_MULTIPLIER,
            -CENTER_CIRCLE_SIZE * CENTER_CIRCLE_HIGHLIGHT_OFFSET_MULTIPLIER,
            CENTER_CIRCLE_SIZE * CENTER_CIRCLE_HIGHLIGHT_SIZE_MULTIPLIER,
            highlightPaint);
    }

    private void UpdateVisualEffects(SKColor baseColor)
    {
        if (_fillPaint == null || _strokePaint == null ||
            _glowPaint == null || _highlightPaint == null)
            return;

        if (!ShouldUpdateVisualEffects(baseColor)) return;

        UpdateAllVisualEffects(baseColor);
    }

    private void UpdateAllVisualEffects(SKColor baseColor)
    {
        UpdateBaseColor(baseColor);
        CreateGradientShader(baseColor);
        UpdateStrokeColor(baseColor);
        UpdateGlowEffectIfNeeded(baseColor);
        UpdateHighlightEffectIfNeeded(baseColor);
        UpdateDashEffectIfNeeded();
        UpdateCenterPaintAndCircle(baseColor);
    }

    private bool ShouldUpdateVisualEffects(SKColor baseColor)
    {
        bool colorChanged =
            baseColor.Red != _lastBaseColor.Red ||
            baseColor.Green != _lastBaseColor.Green ||
            baseColor.Blue != _lastBaseColor.Blue;

        return colorChanged || _gradientShader == null;
    }

    private void UpdateBaseColor(SKColor baseColor) =>
        _lastBaseColor = baseColor;

    private void CreateGradientShader(SKColor baseColor)
    {
        SKColor gradientStart = baseColor.WithAlpha(FILL_ALPHA);
        SKColor gradientEnd = CreateGradientEndColor(baseColor);

        _gradientShader?.Dispose();
        _gradientShader = SKShader.CreateRadialGradient(
            new SKPoint(0, 0),
            MIN_RADIUS + MAX_SPECTRUM_VALUE * RADIUS_MULTIPLIER,
            [gradientStart, gradientEnd],
            SKShaderTileMode.Clamp);

        _fillPaint!.Shader = _gradientShader;
    }

    private static SKColor CreateGradientEndColor(SKColor baseColor) =>
        new(
            (byte)Min(255, baseColor.Red * GRADIENT_COLOR_MULTIPLIER),
            (byte)Min(255, baseColor.Green * GRADIENT_COLOR_MULTIPLIER),
            (byte)Min(255, baseColor.Blue * GRADIENT_COLOR_MULTIPLIER),
            GRADIENT_END_ALPHA);

    private void UpdateStrokeColor(SKColor baseColor) =>
        _strokePaint!.Color = baseColor;

    private void UpdateGlowEffectIfNeeded(SKColor baseColor)
    {
        if (!_useGlow || _glowPaint == null) return;

        _glowFilter?.Dispose();
        _glowFilter = SKImageFilter.CreateBlur(GLOW_RADIUS, GLOW_SIGMA);
        _glowPaint.Color = baseColor.WithAlpha(GLOW_ALPHA);
        _glowPaint.ImageFilter = _glowFilter;
    }

    private void UpdateHighlightEffectIfNeeded(SKColor baseColor)
    {
        if (!_useHighlight || _highlightPaint == null) return;

        _highlightPaint.Color = CreateHighlightColor(baseColor);
    }

    private static SKColor CreateHighlightColor(SKColor baseColor) =>
        new(
            (byte)Min(255, baseColor.Red * HIGHLIGHT_FACTOR),
            (byte)Min(255, baseColor.Green * HIGHLIGHT_FACTOR),
            (byte)Min(255, baseColor.Blue * HIGHLIGHT_FACTOR),
            HIGHLIGHT_ALPHA);

    private void UpdateDashEffectIfNeeded()
    {
        if (!_useDashEffect) return;

        float[] intervals = [DASH_LENGTH, DASH_LENGTH * 2];
        _dashEffect?.Dispose();
        _dashEffect = SKPathEffect.CreateDash(
            intervals,
            base._time * DASH_PHASE_SPEED % (DASH_LENGTH * 3));
    }

    private void UpdateCenterPaintAndCircle(SKColor baseColor)
    {
        if (_centerPaint == null) return;

        _centerPaint.Color = baseColor;
        UpdateCenterCircle(baseColor);
    }



    protected override void OnDispose()
    {
        DisposeManagedResources();
        base.OnDispose();
        Log(LogLevel.Debug, LOG_PREFIX, "Disposed");
    }

    private void DisposeManagedResources()
    {
        _pathUpdateSemaphore?.Dispose();

        DisposeRenderResources();
        ClearResourceReferences();
    }

    private void DisposeRenderResources()
    {
        _outerPath?.Dispose();
        _innerPath?.Dispose();
        _fillPaint?.Dispose();
        _strokePaint?.Dispose();
        _centerPaint?.Dispose();
        _cachedCenterCircle?.Dispose();
        _glowPaint?.Dispose();
        _highlightPaint?.Dispose();
        _gradientShader?.Dispose();
        _dashEffect?.Dispose();
        _glowFilter?.Dispose();
    }

    private void ClearResourceReferences()
    {
        _outerPath = null;
        _innerPath = null;
        _fillPaint = null;
        _strokePaint = null;
        _centerPaint = null;
        _cachedCenterCircle = null;
        _tempSpectrum = null;
        _outerPoints = null;
        _innerPoints = null;
        _glowPaint = null;
        _highlightPaint = null;
        _gradientShader = null;
        _dashEffect = null;
        _glowFilter = null;
    }
}