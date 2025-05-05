#nullable enable

using static SpectrumNet.Views.Renderers.PolarRenderer.Constants;
using static System.MathF;
using Vector = System.Numerics.Vector;

namespace SpectrumNet.Views.Renderers;

public sealed class PolarRenderer : EffectSpectrumRenderer
{
    private static readonly Lazy<PolarRenderer> _instance = new(() => new PolarRenderer());

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

    private readonly new bool _isOverlayActive;
    private new float _smoothingFactor = SMOOTHING_FACTOR_MEDIUM;
    private new bool _useAntiAlias = true;
    private new bool _useAdvancedEffects = true;
    private bool _useGlow = false;
    private bool _useHighlight = true;
    private bool _usePulseEffect = true;
    private bool _useDashEffect = true;
    private float _pathSimplification = PATH_SIMPLIFICATION_MEDIUM;
    private float _strokeMultiplier = STROKE_MULTIPLIER_MEDIUM;
    private int _maxPoints = MAX_POINTS_MEDIUM;

    private Vector<float> _smoothingVec;
    private Vector<float> _oneMinusSmoothing;

    private readonly SemaphoreSlim _pathUpdateSemaphore = new(1, 1);
    private int _frameCounter;
    private float _lastFrameTime;
    private float _avgFrameTime;

    private PolarRenderer() { }

    public static PolarRenderer GetInstance() => _instance.Value;

    public record Constants
    {
        public const string LOG_PREFIX = "PolarRenderer";

        // Core rendering constants
        public const float
            MIN_RADIUS = 30f,
            RADIUS_MULTIPLIER = 200f,
            INNER_RADIUS_RATIO = 0.5f,
            MAX_SPECTRUM_VALUE = 1.0f,
            SPECTRUM_SCALE = 2.0f,
            CHANGE_THRESHOLD = 0.01f,
            DEG_TO_RAD = (float)(MathF.PI / 180.0);

        // Animation constants
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

        // Visual elements constants
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

        // Quality-specific constants
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

    protected override void OnInitialize()
    {
        ExecuteSafely(
            () =>
            {
                base.OnInitialize();
                InitializeResources();
                Log(LogLevel.Debug, LOG_PREFIX, "Initialized");
            },
            "OnInitialize",
            "Failed during renderer initialization"
        );
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
        _fillPaint = new SKPaint()
        {
            IsAntialias = _useAntiAlias,
            Style = SKPaintStyle.Fill
        };

    private void CreateStrokePaint() =>
        _strokePaint = new SKPaint()
        {
            IsAntialias = _useAntiAlias,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = DEFAULT_STROKE_WIDTH * _strokeMultiplier,
            StrokeJoin = SKStrokeJoin.Round,
            StrokeCap = SKStrokeCap.Round
        };

    private void CreateCenterPaint() =>
        _centerPaint = new SKPaint()
        {
            IsAntialias = _useAntiAlias,
            Style = SKPaintStyle.Fill
        };

    private void CreateGlowPaint() =>
        _glowPaint = new SKPaint()
        {
            IsAntialias = _useAntiAlias,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = DEFAULT_STROKE_WIDTH * 1.5f * _strokeMultiplier,
            BlendMode = SKBlendMode.SrcOver
        };

    private void CreateHighlightPaint() =>
        _highlightPaint = new SKPaint()
        {
            IsAntialias = _useAntiAlias,
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
        _processedSpectrum = new float[MAX_POINT_COUNT];
        _previousSpectrum = new float[MAX_POINT_COUNT];
        _tempSpectrum = new float[MAX_POINT_COUNT];
    }

    private void InitializeSmoothingVectors()
    {
        _smoothingVec = new Vector<float>(_smoothingFactor);
        _oneMinusSmoothing = new Vector<float>(1 - _smoothingFactor);
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
            CENTER_CIRCLE_SIZE * 1.5f
        );

        UpdateCenterCircle(SKColors.White);
    }

    public override void Configure(
        bool isOverlayActive,
        RenderQuality quality = RenderQuality.Medium)
    {
        ExecuteSafely(
            () =>
            {
                bool configChanged = _isOverlayActive != isOverlayActive || Quality != quality;
                base.Configure(isOverlayActive, quality);

                UpdateOverlayStateIfNeeded(isOverlayActive);

                if (configChanged)
                {
                    Log(LogLevel.Debug, LOG_PREFIX, $"Configuration changed. New Quality: {Quality}");
                    OnConfigurationChanged();
                }
            },
            "Configure",
            "Failed to configure renderer"
        );
    }

    private void UpdateOverlayStateIfNeeded(bool isOverlayActive)
    {
        if (isOverlayActive != _isOverlayActive)
        {
            UpdatePointCountForOverlay(isOverlayActive);
            MarkPathsForUpdate();
        }
    }

    private void UpdatePointCountForOverlay(bool isOverlayActive) =>
        _currentPointCount = isOverlayActive
            ? POINT_COUNT_OVERLAY
            : (int)MathF.Min(_maxPoints, MAX_POINT_COUNT);

    private void MarkPathsForUpdate() =>
        _pathsNeedUpdate = true;

    protected override void OnConfigurationChanged()
    {
        ExecuteSafely(
            () =>
            {
                base.OnConfigurationChanged();
                InvalidateCachedResources();
            },
            "OnConfigurationChanged",
            "Failed to handle configuration change"
        );
    }

    protected override void OnQualitySettingsApplied()
    {
        ExecuteSafely(
            () =>
            {
                base.OnQualitySettingsApplied();
                ApplyQualitySpecificSettings();
                Log(LogLevel.Debug, LOG_PREFIX, $"Quality settings applied. New Quality: {Quality}");
            },
            "OnQualitySettingsApplied",
            "Failed to apply specific quality settings"
        );
    }

    private void ApplyQualitySpecificSettings()
    {
        InvalidateCachedResources();
        ApplyQualityBasedSettings();
        UpdatePointCountBasedOnOverlay();
        UpdateSmoothingVectors();
        UpdatePaintSettings();
        MarkPathsForUpdate();
    }

    private void ApplyQualityBasedSettings()
    {
        switch (Quality)
        {
            case RenderQuality.Low:
                SetLowQualitySettings();
                break;
            case RenderQuality.Medium:
                SetMediumQualitySettings();
                break;
            case RenderQuality.High:
                SetHighQualitySettings();
                break;
        }
    }

    private void SetLowQualitySettings()
    {
        _smoothingFactor = SMOOTHING_FACTOR_LOW;
        _maxPoints = MAX_POINTS_LOW;
        _useAntiAlias = Constants.Quality.LOW_USE_ANTI_ALIAS;
        _useAdvancedEffects = Constants.Quality.LOW_USE_ADVANCED_EFFECTS;
        _samplingOptions = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);
        _strokeMultiplier = STROKE_MULTIPLIER_LOW;
        _useGlow = Constants.Quality.LOW_USE_GLOW;
        _useHighlight = Constants.Quality.LOW_USE_HIGHLIGHT;
        _usePulseEffect = Constants.Quality.LOW_USE_PULSE_EFFECT;
        _useDashEffect = Constants.Quality.LOW_USE_DASH_EFFECT;
        _pathSimplification = PATH_SIMPLIFICATION_LOW;
    }

    private void SetMediumQualitySettings()
    {
        _smoothingFactor = SMOOTHING_FACTOR_MEDIUM;
        _maxPoints = MAX_POINTS_MEDIUM;
        _useAntiAlias = Constants.Quality.MEDIUM_USE_ANTI_ALIAS;
        _useAdvancedEffects = Constants.Quality.MEDIUM_USE_ADVANCED_EFFECTS;
        _samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
        _strokeMultiplier = STROKE_MULTIPLIER_MEDIUM;
        _useGlow = Constants.Quality.MEDIUM_USE_GLOW;
        _useHighlight = Constants.Quality.MEDIUM_USE_HIGHLIGHT;
        _usePulseEffect = Constants.Quality.MEDIUM_USE_PULSE_EFFECT;
        _useDashEffect = Constants.Quality.MEDIUM_USE_DASH_EFFECT;
        _pathSimplification = PATH_SIMPLIFICATION_MEDIUM;
    }

    private void SetHighQualitySettings()
    {
        _smoothingFactor = SMOOTHING_FACTOR_HIGH;
        _maxPoints = MAX_POINTS_HIGH;
        _useAntiAlias = Constants.Quality.HIGH_USE_ANTI_ALIAS;
        _useAdvancedEffects = Constants.Quality.HIGH_USE_ADVANCED_EFFECTS;
        _samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
        _strokeMultiplier = STROKE_MULTIPLIER_HIGH;
        _useGlow = Constants.Quality.HIGH_USE_GLOW;
        _useHighlight = Constants.Quality.HIGH_USE_HIGHLIGHT;
        _usePulseEffect = Constants.Quality.HIGH_USE_PULSE_EFFECT;
        _useDashEffect = Constants.Quality.HIGH_USE_DASH_EFFECT;
        _pathSimplification = PATH_SIMPLIFICATION_HIGH;
    }

    private void UpdatePointCountBasedOnOverlay() =>
        _currentPointCount = _isOverlayActive
            ? POINT_COUNT_OVERLAY
            : (int)MathF.Min(_maxPoints, MAX_POINT_COUNT);

    private void UpdateSmoothingVectors()
    {
        _smoothingVec = new Vector<float>(_smoothingFactor);
        _oneMinusSmoothing = new Vector<float>(1 - _smoothingFactor);
    }

    private void UpdatePaintSettings()
    {
        if (_fillPaint == null || _strokePaint == null ||
            _centerPaint == null || _glowPaint == null || _highlightPaint == null)
            return;

        UpdateFillPaintSettings();
        UpdateStrokePaintSettings();
        UpdateCenterPaintSettings();
        UpdateGlowPaintSettings();
        UpdateHighlightPaintSettings();
    }

    private void UpdateFillPaintSettings() =>
        _fillPaint!.IsAntialias = _useAntiAlias;

    private void UpdateStrokePaintSettings()
    {
        _strokePaint!.IsAntialias = _useAntiAlias;
        _strokePaint!.StrokeWidth = DEFAULT_STROKE_WIDTH * _strokeMultiplier;
    }

    private void UpdateCenterPaintSettings() =>
        _centerPaint!.IsAntialias = _useAntiAlias;

    private void UpdateGlowPaintSettings()
    {
        _glowPaint!.IsAntialias = _useAntiAlias;
        _glowPaint!.StrokeWidth = DEFAULT_STROKE_WIDTH * 1.5f * _strokeMultiplier;
    }

    private void UpdateHighlightPaintSettings()
    {
        _highlightPaint!.IsAntialias = _useAntiAlias;
        _highlightPaint!.StrokeWidth = DEFAULT_STROKE_WIDTH * 0.5f * _strokeMultiplier;
    }

    private new void InvalidateCachedResources()
    {
        DisposeCircleCache();
        DisposeDashEffect();
        DisposeGradientShader();
        DisposeGlowFilter();
        MarkPathsForUpdate();
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

        using var glowPaint = new SKPaint()
        {
            IsAntialias = _useAntiAlias,
            Style = SKPaintStyle.Fill,
            Color = baseColor.WithAlpha(GLOW_ALPHA),
            ImageFilter = _glowFilter
        };

        pictureCanvas.DrawCircle(
            0,
            0,
            CENTER_CIRCLE_SIZE * CENTER_CIRCLE_GLOW_MULTIPLIER,
            glowPaint
        );
    }

    private void DrawMainCenterCircle(SKCanvas pictureCanvas) =>
        pictureCanvas.DrawCircle(
            0,
            0,
            CENTER_CIRCLE_SIZE * CENTER_CIRCLE_MAIN_MULTIPLIER,
            _centerPaint!
        );

    private void DrawHighlightCircleIfNeeded(SKCanvas pictureCanvas)
    {
        if (!_useHighlight) return;

        using var highlightPaint = new SKPaint()
        {
            IsAntialias = _useAntiAlias,
            Style = SKPaintStyle.Fill,
            Color = SKColors.White.WithAlpha(HIGHLIGHT_ALPHA)
        };

        pictureCanvas.DrawCircle(
            -CENTER_CIRCLE_SIZE * CENTER_CIRCLE_HIGHLIGHT_OFFSET_MULTIPLIER,
            -CENTER_CIRCLE_SIZE * CENTER_CIRCLE_HIGHLIGHT_OFFSET_MULTIPLIER,
            CENTER_CIRCLE_SIZE * CENTER_CIRCLE_HIGHLIGHT_SIZE_MULTIPLIER,
            highlightPaint
        );
    }

    private void UpdateVisualEffects(SKColor baseColor)
    {
        if (_fillPaint == null || _strokePaint == null ||
            _glowPaint == null || _highlightPaint == null)
            return;

        if (!ShouldUpdateVisualEffects(baseColor)) return;

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
        SKColor gradientEnd = new(
            (byte)MathF.Min(255, baseColor.Red * GRADIENT_COLOR_MULTIPLIER),
            (byte)MathF.Min(255, baseColor.Green * GRADIENT_COLOR_MULTIPLIER),
            (byte)MathF.Min(255, baseColor.Blue * GRADIENT_COLOR_MULTIPLIER),
            GRADIENT_END_ALPHA
        );

        _gradientShader?.Dispose();
        _gradientShader = SKShader.CreateRadialGradient(
            new SKPoint(0, 0),
            MIN_RADIUS + MAX_SPECTRUM_VALUE * RADIUS_MULTIPLIER,
            [gradientStart, gradientEnd],
            SKShaderTileMode.Clamp
        );

        _fillPaint!.Shader = _gradientShader;
    }

    private void UpdateStrokeColor(SKColor baseColor) =>
        _strokePaint!.Color = baseColor;

    private void UpdateGlowEffectIfNeeded(SKColor baseColor)
    {
        if (!_useGlow) return;

        _glowFilter?.Dispose();
        _glowFilter = SKImageFilter.CreateBlur(GLOW_RADIUS, GLOW_SIGMA);
        _glowPaint!.Color = baseColor.WithAlpha(GLOW_ALPHA);
        _glowPaint!.ImageFilter = _glowFilter;
    }

    private void UpdateHighlightEffectIfNeeded(SKColor baseColor)
    {
        if (!_useHighlight) return;

        _highlightPaint!.Color = new SKColor(
            (byte)MathF.Min(255, baseColor.Red * HIGHLIGHT_FACTOR),
            (byte)MathF.Min(255, baseColor.Green * HIGHLIGHT_FACTOR),
            (byte)MathF.Min(255, baseColor.Blue * HIGHLIGHT_FACTOR),
            HIGHLIGHT_ALPHA
        );
    }

    private void UpdateDashEffectIfNeeded()
    {
        if (!_useDashEffect) return;

        float[] intervals = [DASH_LENGTH, DASH_LENGTH * 2];
        _dashEffect?.Dispose();
        _dashEffect = SKPathEffect.CreateDash(
            intervals,
            _time * DASH_PHASE_SPEED % (DASH_LENGTH * 3)
        );
    }

    private void UpdateCenterPaintAndCircle(SKColor baseColor)
    {
        if (_centerPaint == null) return;

        _centerPaint.Color = baseColor;
        UpdateCenterCircle(baseColor);
    }

    public override void Render(
        SKCanvas? canvas,
        float[]? spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint? paint,
        Action<SKCanvas, SKImageInfo>? drawPerformanceInfo)
    {
        if (!ValidateRenderParameters(canvas, spectrum, info, paint))
        {
            drawPerformanceInfo?.Invoke(canvas!, info);
            return;
        }

        float frameStartTime = (float)Now.Ticks / TimeSpan.TicksPerSecond;

        ExecuteSafely(
            () => PerformRender(canvas!, spectrum!, info, barWidth, barCount, paint!),
            "Render",
            "Error during rendering"
        );

        drawPerformanceInfo?.Invoke(canvas!, info);
        TrackFrameTime(frameStartTime);
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
        if (!ValidateRenderParameters(canvas, spectrum, info, paint)) return;

        float frameStartTime = (float)Now.Ticks / TimeSpan.TicksPerSecond;

        ExecuteSafely(
            () => PerformRender(canvas, spectrum, info, barWidth, barCount, paint),
            "RenderEffect",
            "Error during rendering"
        );

        TrackFrameTime(frameStartTime);
    }

    private void PerformRender(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        int barCount,
        SKPaint paint)
    {
        int safeBarCount = (int)MathF.Min(MathF.Max(barCount, MIN_POINT_COUNT), _maxPoints);
        float safeBarWidth = Clamp(barWidth, MIN_BAR_WIDTH, MAX_BAR_WIDTH);

        PrepareClipBounds();

        if (IsCanvasOutOfBounds(canvas, info)) return;

        ProcessSpectrumAndUpdatePaths(spectrum, safeBarCount, info);
        UpdateVisualEffects(paint.Color);
        RenderPolarGraph(canvas, info, paint, safeBarWidth);
    }

    private void PrepareClipBounds()
    {
        float maxRadius = MIN_RADIUS + MAX_SPECTRUM_VALUE *
            RADIUS_MULTIPLIER * (1 + MODULATION_FACTOR);

        _clipBounds = new SKRect(
            -maxRadius, -maxRadius,
            maxRadius, maxRadius
        );
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

    private void RenderPolarGraph(
        SKCanvas canvas,
        SKImageInfo info,
        SKPaint basePaint,
        float barWidth)
    {
        if (!AreRenderResourcesValid()) return;

        UpdateEffectParameters(barWidth);
        ApplyDashEffectIfNeeded();

        canvas.Save();
        RenderGraphWithTransform(canvas, info);
        canvas.Restore();
    }

    private bool AreRenderResourcesValid() =>
        _outerPath != null && _innerPath != null &&
        _fillPaint != null && _strokePaint != null &&
        _centerPaint != null && _cachedCenterCircle != null &&
        _glowPaint != null && _highlightPaint != null;

    private void UpdateEffectParameters(float barWidth)
    {
        UpdatePulseEffect();
        UpdateStrokeWidths(barWidth);
    }

    private void UpdatePulseEffect() =>
        _pulseEffect = _usePulseEffect
            ? MathF.Sin(_time * PULSE_SPEED) * PULSE_AMPLITUDE + 1.0f
            : 1.0f;

    private void UpdateStrokeWidths(float barWidth)
    {
        _strokePaint!.StrokeWidth = barWidth * _pulseEffect * _strokeMultiplier;

        if (_useGlow)
        {
            _glowPaint!.StrokeWidth = barWidth * 1.5f * _pulseEffect * _strokeMultiplier;
        }

        if (_useHighlight)
        {
            _highlightPaint!.StrokeWidth = barWidth * 0.5f * _pulseEffect * _strokeMultiplier;
        }
    }

    private void ApplyDashEffectIfNeeded() =>
        _strokePaint!.PathEffect = (_useDashEffect && _dashEffect != null)
            ? _dashEffect
            : null;

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
        if (_useAdvancedEffects && Quality == RenderQuality.High)
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
        if (_useGlow)
        {
            canvas.DrawPath(_outerPath!, _glowPaint!);
        }
    }

    private void RenderOuterPath(SKCanvas canvas)
    {
        canvas.DrawPath(_outerPath!, _fillPaint!);
        canvas.DrawPath(_outerPath!, _strokePaint!);
    }

    private void RenderInnerPathWithDash(SKCanvas canvas)
    {
        SKPathEffect? originalEffect = _strokePaint!.PathEffect;
        _strokePaint!.PathEffect = _dashEffect;
        canvas.DrawPath(_innerPath!, _strokePaint!);
        _strokePaint!.PathEffect = originalEffect;
    }

    private void RenderHighlightIfNeeded(SKCanvas canvas)
    {
        if (_useHighlight)
        {
            canvas.DrawPath(_innerPath!, _highlightPaint!);
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
        canvas.DrawPath(_outerPath!, _fillPaint!);
        canvas.DrawPath(_outerPath!, _strokePaint!);

        if (_useDashEffect && _dashEffect != null)
        {
            _strokePaint!.PathEffect = _dashEffect;
        }
        canvas.DrawPath(_innerPath!, _strokePaint!);
        _strokePaint!.PathEffect = null;
    }

    private void RenderCenterCircleWithPulse(SKCanvas canvas)
    {
        float pulseScale = CalculatePulseScale();

        canvas.Save();
        canvas.Scale(pulseScale, pulseScale);
        canvas.DrawPicture(_cachedCenterCircle!);
        canvas.Restore();
    }

    private float CalculatePulseScale() =>
        _usePulseEffect
            ? 1.0f + MathF.Sin(_time * PULSE_SPEED * 0.5f) * PULSE_SCALE_MULTIPLIER
            : 1.0f;

    private void TrackFrameTime(float frameStartTime)
    {
        float frameEndTime = (float)Now.Ticks / TimeSpan.TicksPerSecond;
        float frameTime = frameEndTime - frameStartTime;

        UpdateFrameTimeStatistics(frameTime);
    }

    private void UpdateFrameTimeStatistics(float frameTime)
    {
        _lastFrameTime = frameTime;
        _avgFrameTime = (_avgFrameTime * _frameCounter + frameTime) / (_frameCounter + 1);
        _frameCounter = (_frameCounter + 1) % FRAME_AVERAGE_COUNT;
    }

    private void UpdatePolarPaths(SKImageInfo info, int barCount)
    {
        if (!ArePathResourcesValid()) return;

        UpdateAnimationState();
        CalculatePathParameters(
            barCount,
            out int effectivePointCount,
            out int skipFactor,
            out int actualPoints,
            out float angleStep
        );
        GeneratePointCoordinates(effectivePointCount, skipFactor, actualPoints, angleStep);
        CreatePathsFromPoints(effectivePointCount, skipFactor, actualPoints);
    }

    private bool ArePathResourcesValid() =>
        _processedSpectrum != null && _outerPath != null && _innerPath != null &&
        _outerPoints != null && _innerPoints != null;

    private void UpdateAnimationState()
    {
        _time += TIME_STEP;
        _rotation += ROTATION_SPEED * _time * TIME_MODIFIER;
    }

    private void CalculatePathParameters(
        int barCount,
        out int effectivePointCount,
        out int skipFactor,
        out int actualPoints,
        out float angleStep)
    {
        effectivePointCount = (int)MathF.Min(barCount, _currentPointCount);

        skipFactor = _pathSimplification > 0
            ? (int)MathF.Max(1, (int)(1.0f / (1.0f - _pathSimplification)))
            : 1;

        actualPoints = effectivePointCount / skipFactor;
        angleStep = 360f / actualPoints;
    }

    private void GeneratePointCoordinates(
        int effectivePointCount,
        int skipFactor,
        int actualPoints,
        float angleStep)
    {
        for (int i = 0, pointIndex = 0; i <= effectivePointCount; i += skipFactor, pointIndex++)
        {
            float angle = pointIndex * angleStep * DEG_TO_RAD;
            float cosAngle = MathF.Cos(angle);
            float sinAngle = MathF.Sin(angle);

            GeneratePointForIndex(
                effectivePointCount,
                pointIndex,
                i,
                cosAngle,
                sinAngle,
                angleStep
            );
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

        float timeOffset = _time * 0.5f + pointIndex * 0.1f;

        GenerateOuterPoint(pointIndex, spectrumValue, timeOffset, angleStep, cosAngle, sinAngle);
        GenerateInnerPoint(pointIndex, spectrumValue, timeOffset, angleStep, cosAngle, sinAngle);
    }

    private float GetSpectrumValueForIndex(int spectrumIndex) =>
        spectrumIndex < _processedSpectrum!.Length
            ? _processedSpectrum[spectrumIndex]
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
                outerRadius * sinAngle
            );
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
                innerRadius * sinAngle
            );
        }
    }

    private float CalculateModulation(int pointIndex, float angleStep, float timeOffset)
    {
        if (Quality == RenderQuality.Low)
        {
            return 1.0f;
        }

        float modulation = 1 + MODULATION_FACTOR *
            MathF.Sin(pointIndex * angleStep * MODULATION_FREQ * DEG_TO_RAD + _time * 2);

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
            MathF.Sin(pointIndex * angleStep * MODULATION_FREQ * DEG_TO_RAD + _time * 2 + MathF.PI);

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
        int pointsToUse = (int)MathF.Min(actualPoints + 1, _outerPoints!.Length);

        SKPoint[] outerPointsSlice = new SKPoint[pointsToUse];
        SKPoint[] innerPointsSlice = new SKPoint[pointsToUse];

        Array.Copy(_outerPoints!, outerPointsSlice, pointsToUse);
        Array.Copy(_innerPoints!, innerPointsSlice, pointsToUse);

        _outerPath!.AddPoly(outerPointsSlice, true);
        _innerPath!.AddPoly(innerPointsSlice, true);
    }

    private void CreatePathsUsingLines(int effectivePointCount, int skipFactor)
    {
        for (int i = 0, pointIndex = 0; i <= effectivePointCount; i += skipFactor, pointIndex++)
        {
            int safeIndex = (int)MathF.Min(pointIndex, _outerPoints!.Length - 1);

            if (pointIndex == 0)
            {
                _outerPath!.MoveTo(_outerPoints[safeIndex]);
                _innerPath!.MoveTo(_innerPoints![safeIndex]);
            }
            else
            {
                _outerPath!.LineTo(_outerPoints[safeIndex]);
                _innerPath!.LineTo(_innerPoints![safeIndex]);
            }
        }

        _outerPath!.Close();
        _innerPath!.Close();
    }

    private void ProcessSpectrum(float[] spectrum, int barCount)
    {
        if (_disposed || _tempSpectrum == null || _previousSpectrum == null ||
            _processedSpectrum == null)
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
        int pointCount = (int)MathF.Min(barCount, _currentPointCount);
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
        _tempSpectrum[pointIndex] = MathF.Min(_tempSpectrum[pointIndex] * SPECTRUM_SCALE, MAX_SPECTRUM_VALUE);
    }

    private static float CalculateInterpolatedValue(
        float[] spectrum,
        int baseIndex,
        float fraction)
    {
        if (baseIndex >= spectrum.Length / 2 - 1)
        {
            return spectrum[(int)MathF.Min(spectrum.Length / 2 - 1, spectrum.Length - 1)];
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

        if (_tempSpectrum == null || _previousSpectrum == null || _processedSpectrum == null)
        {
            Log(LogLevel.Error, LOG_PREFIX, "Spectrum data arrays are null");
            return maxChange;
        }

        int safePointCount = (int)MathF.Min(
            pointCount,
            MathF.Min(
                _tempSpectrum.Length,
                MathF.Min(_previousSpectrum.Length, _processedSpectrum.Length)
            )
        );

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

    private void ProcessSpectrumSIMD(int safePointCount, ref float maxChange)
    {
        for (int i = 0; i < safePointCount; i += Vector<float>.Count)
        {
            int remaining = (int)MathF.Min(Vector<float>.Count, safePointCount - i);

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
            Vector<float> previous = new(_previousSpectrum!, startIndex);
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
        maxChange = MathF.Max(maxChange, batchMaxChange);
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
        smoothed.CopyTo(_processedSpectrum!, startIndex);
        smoothed.CopyTo(_previousSpectrum!, startIndex);
    }

    private void ProcessSpectrumRemainder(int startIndex, int count, ref float maxChange)
    {
        for (int j = 0; j < count; j++)
        {
            float newValue = _previousSpectrum![startIndex + j] * (1 - _smoothingFactor) +
                           _tempSpectrum![startIndex + j] * _smoothingFactor;
            float change = MathF.Abs(newValue - _previousSpectrum[startIndex + j]);
            maxChange = MathF.Max(maxChange, change);
            _processedSpectrum![startIndex + j] = newValue;
            _previousSpectrum[startIndex + j] = newValue;
        }
    }

    private void ProcessSpectrumStandard(int safePointCount, ref float maxChange)
    {
        for (int i = 0; i < safePointCount; i++)
        {
            float newValue = _previousSpectrum![i] * (1 - _smoothingFactor) +
                           _tempSpectrum![i] * _smoothingFactor;
            float change = MathF.Abs(newValue - _previousSpectrum[i]);
            maxChange = MathF.Max(maxChange, change);
            _processedSpectrum![i] = newValue;
            _previousSpectrum[i] = newValue;
        }
    }

    private bool ValidateRenderParameters(
        SKCanvas? canvas,
        float[]? spectrum,
        SKImageInfo info,
        SKPaint? paint)
    {
        if (!IsCanvasValid(canvas)) return false;
        if (!IsSpectrumValid(spectrum)) return false;
        if (!IsPaintValid(paint)) return false;
        if (!AreDimensionsValid(info)) return false;
        if (IsDisposed()) return false;
        return true;
    }

    private static bool IsCanvasValid(SKCanvas? canvas)
    {
        if (canvas != null) return true;
        Log(LogLevel.Error, LOG_PREFIX, "Canvas is null");
        return false;
    }

    private static bool IsSpectrumValid(float[]? spectrum)
    {
        if (spectrum != null && spectrum.Length > 1) return true;
        Log(LogLevel.Error, LOG_PREFIX, "Spectrum is null or too small");
        return false;
    }

    private static bool IsPaintValid(SKPaint? paint)
    {
        if (paint != null) return true;
        Log(LogLevel.Error, LOG_PREFIX, "Paint is null");
        return false;
    }

    private static bool AreDimensionsValid(SKImageInfo info)
    {
        if (info.Width > 0 && info.Height > 0) return true;
        Log(LogLevel.Error, LOG_PREFIX, $"Invalid image dimensions: {info.Width}x{info.Height}");
        return false;
    }

    private bool IsDisposed()
    {
        if (!_disposed) return false;
        Log(LogLevel.Error, LOG_PREFIX, "Renderer is disposed");
        return true;
    }

    protected override void OnDispose()
    {
        ExecuteSafely(
            () =>
            {
                DisposeManagedResources();
                base.OnDispose();
            },
            "OnDispose",
            "Error during specific disposal"
        );
    }

    private void DisposeManagedResources()
    {
        DisposeInternalResources();
        ClearReferences();
    }

    private void DisposeInternalResources()
    {
        _pathUpdateSemaphore?.Dispose();

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

    private void ClearReferences()
    {
        _outerPath = null;
        _innerPath = null;
        _fillPaint = null;
        _strokePaint = null;
        _centerPaint = null;
        _cachedCenterCircle = null;
        _processedSpectrum = null;
        _previousSpectrum = null;
        _tempSpectrum = null;
        _outerPoints = null;
        _innerPoints = null;
        _glowPaint = null;
        _highlightPaint = null;
        _gradientShader = null;
        _dashEffect = null;
        _glowFilter = null;
    }

    public override void Dispose()
    {
        if (_disposed) return;
        ExecuteSafely(
            () =>
            {
                OnDispose();
            },
            "Dispose",
            "Error during disposal"
        );
        _disposed = true;
        GC.SuppressFinalize(this);
        Log(LogLevel.Debug, LOG_PREFIX, "Disposed");
    }
}