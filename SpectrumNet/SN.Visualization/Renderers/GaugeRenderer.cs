#nullable enable

using static SpectrumNet.SN.Visualization.Renderers.GaugeRenderer.Constants;
using static SpectrumNet.SN.Visualization.Renderers.GaugeRenderer.Constants.Quality;
using static System.MathF;

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class GaugeRenderer : EffectSpectrumRenderer
{
    private static readonly Lazy<GaugeRenderer> _instance = new(() => new GaugeRenderer());
    private const string LogPrefix = nameof(GaugeRenderer);

    private GaugeRenderer() { }

    public static GaugeRenderer GetInstance() => _instance.Value;

    public record Constants
    {
        public const float
            DB_MAX = 5f,
            DB_MIN = -30f,
            DB_PEAK_THRESHOLD = 5f;

        public const float
            ANGLE_START = -150f,
            ANGLE_END = -30f,
            ANGLE_TOTAL_RANGE = ANGLE_END - ANGLE_START;

        public const float
            NEEDLE_DEFAULT_LENGTH_MULTIPLIER = 1.55f,
            NEEDLE_DEFAULT_CENTER_Y_OFFSET_MULTIPLIER = 0.4f,
            NEEDLE_STROKE_WIDTH = 2.25f,
            NEEDLE_CENTER_CIRCLE_RADIUS_OVERLAY = 0.015f,
            NEEDLE_CENTER_CIRCLE_RADIUS = 0.02f,
            NEEDLE_BASE_WIDTH_MULTIPLIER = 2.5f;

        public const float
            BG_OUTER_FRAME_CORNER_RADIUS = 8f,
            BG_INNER_FRAME_PADDING = 4f,
            BG_INNER_FRAME_CORNER_RADIUS = 6f,
            BG_BACKGROUND_PADDING = 4f,
            BG_BACKGROUND_CORNER_RADIUS = 4f,
            BG_VU_TEXT_SIZE_FACTOR = 0.2f,
            BG_VU_TEXT_BOTTOM_OFFSET_FACTOR = 0.2f;

        public const float
            SCALE_CENTER_Y_OFFSET_FACTOR = 0.15f,
            SCALE_RADIUS_X_FACTOR_OVERLAY = 0.4f,
            SCALE_RADIUS_X_FACTOR = 0.45f,
            SCALE_RADIUS_Y_FACTOR_OVERLAY = 0.45f,
            SCALE_RADIUS_Y_FACTOR = 0.5f,
            SCALE_TEXT_OFFSET_FACTOR_OVERLAY = 0.1f,
            SCALE_TEXT_OFFSET_FACTOR = 0.12f,
            SCALE_TEXT_SIZE_FACTOR_OVERLAY = 0.08f,
            SCALE_TEXT_SIZE_FACTOR = 0.1f,
            SCALE_TICK_LENGTH_ZERO_FACTOR_OVERLAY = 0.12f,
            SCALE_TICK_LENGTH_ZERO_FACTOR = 0.15f,
            SCALE_TICK_LENGTH_FACTOR_OVERLAY = 0.07f,
            SCALE_TICK_LENGTH_FACTOR = 0.08f,
            SCALE_TICK_LENGTH_MINOR_FACTOR_OVERLAY = 0.05f,
            SCALE_TICK_LENGTH_MINOR_FACTOR = 0.06f,
            SCALE_TICK_STROKE_WIDTH = 1.8f,
            SCALE_TEXT_SIZE_MULTIPLIER_ZERO = 1.15f;

        public const float
            PEAK_LAMP_RADIUS_FACTOR_OVERLAY = 0.04f,
            PEAK_LAMP_RADIUS_FACTOR = 0.05f,
            PEAK_LAMP_X_OFFSET_FACTOR_OVERLAY = 0.12f,
            PEAK_LAMP_X_OFFSET_FACTOR = 0.1f,
            PEAK_LAMP_Y_OFFSET_FACTOR_OVERLAY = 0.18f,
            PEAK_LAMP_Y_OFFSET_FACTOR = 0.2f,
            PEAK_LAMP_TEXT_SIZE_FACTOR_OVERLAY = 1.2f,
            PEAK_LAMP_TEXT_SIZE_FACTOR = 1.5f,
            PEAK_LAMP_TEXT_Y_OFFSET_FACTOR = 2.5f,
            PEAK_LAMP_RIM_STROKE_WIDTH = 1f,
            PEAK_LAMP_GLOW_RADIUS_MULTIPLIER = 1.5f,
            PEAK_LAMP_INNER_RADIUS_MULTIPLIER = 0.8f;

        public const float
            RENDERING_ASPECT_RATIO = 2.0f,
            RENDERING_GAUGE_RECT_PADDING = 0.8f,
            RENDERING_MIN_DB_CLAMP = 1e-10f,
            RENDERING_MARGIN = 0.05f;

        public const int
            MINOR_MARKS_DIVISOR = 3,
            PEAK_HOLD_DURATION = 15;

        public static class Quality
        {
            public const bool
                LOW_USE_ADVANCED_EFFECTS = false,
                MEDIUM_USE_ADVANCED_EFFECTS = true,
                HIGH_USE_ADVANCED_EFFECTS = true;

            public const bool
                LOW_USE_ANTIALIASING = false,
                MEDIUM_USE_ANTIALIASING = true,
                HIGH_USE_ANTIALIASING = true;
        }
    }

    private readonly record struct GaugeState(
        float CurrentNeedlePosition,
        float TargetNeedlePosition,
        float PreviousValue,
        bool PeakActive)
    {
        public GaugeState() : this(0f, 0f, DB_MIN, false) { }
    }

    private readonly record struct GaugeConfig(
        float SmoothingFactorIncrease,
        float SmoothingFactorDecrease,
        float RiseSpeed,
        float FallSpeed,
        float Damping,
        float NeedleLengthMultiplier,
        float NeedleCenterYOffsetMultiplier)
    {
        public static GaugeConfig Default => new(
            SmoothingFactorIncrease: 0.2f,
            SmoothingFactorDecrease: 0.05f,
            RiseSpeed: 0.15f,
            FallSpeed: 0.03f,
            Damping: 0.7f,
            NeedleLengthMultiplier: NEEDLE_DEFAULT_LENGTH_MULTIPLIER,
            NeedleCenterYOffsetMultiplier: NEEDLE_DEFAULT_CENTER_Y_OFFSET_MULTIPLIER
        );

        public GaugeConfig WithOverlayMode(bool isOverlayActive) => this with
        {
            SmoothingFactorIncrease = isOverlayActive ? 0.1f : 0.2f,
            SmoothingFactorDecrease = isOverlayActive ? 0.02f : 0.05f,
            NeedleLengthMultiplier = isOverlayActive ? 1.6f : NEEDLE_DEFAULT_LENGTH_MULTIPLIER,
            NeedleCenterYOffsetMultiplier = isOverlayActive ? 0.35f : NEEDLE_DEFAULT_CENTER_Y_OFFSET_MULTIPLIER
        };
    }

    private static readonly (float Value, string Label)[] _majorMarks =
    [
        (-30f, "-30"), (-20f, "-20"), (-10f, "-10"),
        (-7f, "-7"), (-5f, "-5"), (-3f, "-3"),
        (0f, "0"), (3f, "+3"), (5f, "+5")
    ];

    private static readonly List<float> _minorMarkValues = [];

    private static readonly SKColor[] _gaugeBackgroundColors = [new(250, 250, 240), new(230, 230, 215)];
    private static readonly SKColor[] _needleCenterColors = [SKColors.White, new(180, 180, 180), new(60, 60, 60)];
    private static readonly float[] _centerColorStops = [0.0f, 0.3f, 1.0f];
    private static readonly SKColor[] _redTickColors = [new(200, 0, 0), SKColors.Red];
    private static readonly SKColor[] _grayTickColors = [new(60, 60, 60), new(100, 100, 100)];
    private static readonly SKColor[] _activeLampColors = [SKColors.White, new(255, 180, 180), SKColors.Red];
    private static readonly SKColor[] _inactiveLampColors = [new(220, 220, 220), new(180, 0, 0), new(80, 0, 0)];

    private GaugeState _state = new();
    private GaugeConfig _config = GaugeConfig.Default;
    private int _peakHoldCounter;

    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _renderSemaphore = new(1, 1);

    static GaugeRenderer()
    {
        InitializeMinorMarks();
    }

    protected override void OnInitialize()
    {
        base.OnInitialize();
        _state = new GaugeState();
        _config = GaugeConfig.Default;
        _logger.Debug(LogPrefix, "Initialized");
    }

    public override void SetOverlayTransparency(float level) =>
        _logger.Safe(() => HandleSetOverlayTransparency(level),
                     LogPrefix,
                     "Error setting overlay transparency");

    private void HandleSetOverlayTransparency(float level)
    {
        if (Math.Abs(_overlayAlphaFactor - level) < float.Epsilon)
            return;

        _overlayAlphaFactor = level;
        _overlayStateChangeRequested = true;
        _overlayStateChanged = true;
    }

    protected override void OnConfigurationChanged()
    {
        _logger.Info(LogPrefix,
            $"Configuration changed. New Quality: {Quality}, Overlay: {_isOverlayActive}");

        _config = _config.WithOverlayMode(_isOverlayActive);

        if (_isOverlayActive)
        {
            _overlayAlphaFactor = 0.75f;
            _overlayStateChanged = true;
            _overlayStateChangeRequested = true;
        }
    }

    protected override void OnQualitySettingsApplied() =>
        _logger.Safe(() => HandleQualitySettingsApplied(),
                     LogPrefix,
                     "Error applying quality settings");

    private void HandleQualitySettingsApplied()
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

        _logger.Debug(LogPrefix,
            $"Quality settings applied. Quality: {Quality}, " +
            $"AntiAlias: {UseAntiAlias}, AdvancedEffects: {UseAdvancedEffects}");
    }

    private void LowQualitySettings()
    {
        _useAntiAlias = LOW_USE_ANTIALIASING;
        _useAdvancedEffects = LOW_USE_ADVANCED_EFFECTS;
    }

    private void MediumQualitySettings()
    {
        _useAntiAlias = MEDIUM_USE_ANTIALIASING;
        _useAdvancedEffects = MEDIUM_USE_ADVANCED_EFFECTS;
    }

    private void HighQualitySettings()
    {
        _useAntiAlias = HIGH_USE_ANTIALIASING;
        _useAdvancedEffects = HIGH_USE_ADVANCED_EFFECTS;
    }

    protected override void RenderEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint paint) =>
        _logger.Safe(() => HandleRenderEffect(canvas, spectrum, info, barWidth, barSpacing, barCount, paint),
                    LogPrefix,
                    "Error during rendering");

    private void HandleRenderEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint paint)
    {
        if (_overlayStateChangeRequested)
        {
            _overlayStateChangeRequested = false;
            _overlayStateChanged = true;
        }

        UpdateState(spectrum);
        RenderWithOverlay(canvas, () => RenderFrame(canvas, info, paint));

        if (_overlayStateChanged)
        {
            _overlayStateChanged = false;
        }
    }

    private void UpdateState(float[] spectrum) =>
        _logger.Safe(() => HandleUpdateState(spectrum),
                     LogPrefix,
                     "Error updating gauge state");

    private void HandleUpdateState(float[] spectrum)
    {
        bool semaphoreAcquired = false;
        try
        {
            semaphoreAcquired = _renderSemaphore.Wait(0);
            if (semaphoreAcquired)
            {
                UpdateGaugeState(spectrum);
            }
        }
        finally
        {
            if (semaphoreAcquired)
                _renderSemaphore.Release();
        }
    }

    private void RenderFrame(SKCanvas canvas, SKImageInfo info, SKPaint paint) =>
        _logger.Safe(() => HandleRenderFrame(canvas, info, paint),
                     LogPrefix,
                     "Error rendering gauge frame");

    private void HandleRenderFrame(SKCanvas canvas, SKImageInfo info, SKPaint paint)
    {
        var gaugeRect = CalculateGaugeRect(info);
        bool isOverlayActive = _isOverlayActive;

        DrawGaugeBackground(canvas, gaugeRect);
        DrawScale(canvas, gaugeRect, isOverlayActive);
        DrawNeedle(canvas, gaugeRect, _state.CurrentNeedlePosition, isOverlayActive, paint);
        DrawPeakLamp(canvas, gaugeRect, isOverlayActive);
    }

    private void DrawGaugeBackground(SKCanvas canvas, SKRect rect) =>
        _logger.Safe(() => HandleDrawGaugeBackground(canvas, rect),
                     LogPrefix,
                     "Error drawing gauge background");

    private void HandleDrawGaugeBackground(SKCanvas canvas, SKRect rect)
    {
        using var outerFramePaint = _paintPool.Get();
        ConfigureOuterFramePaint(outerFramePaint);
        canvas.DrawRoundRect(
            rect,
            BG_OUTER_FRAME_CORNER_RADIUS,
            BG_OUTER_FRAME_CORNER_RADIUS,
            outerFramePaint);

        var innerFrameRect = GetInnerFrameRect(rect);
        using var innerFramePaint = _paintPool.Get();
        ConfigureInnerFramePaint(innerFramePaint);
        canvas.DrawRoundRect(
            innerFrameRect,
            BG_INNER_FRAME_CORNER_RADIUS,
            BG_INNER_FRAME_CORNER_RADIUS,
            innerFramePaint);

        var backgroundRect = GetBackgroundRect(innerFrameRect);
        using var backgroundPaint = _paintPool.Get();
        ConfigureBackgroundPaint(backgroundPaint, backgroundRect);
        canvas.DrawRoundRect(
            backgroundRect,
            BG_BACKGROUND_CORNER_RADIUS,
            BG_BACKGROUND_CORNER_RADIUS,
            backgroundPaint);

        DrawVuText(canvas, backgroundRect, rect.Height);
    }

    [MethodImpl(AggressiveInlining)]
    private void ConfigureOuterFramePaint(SKPaint paint)
    {
        paint.Style = SKPaintStyle.Fill;
        paint.Color = new SKColor(80, 80, 80);
        paint.IsAntialias = UseAntiAlias;
    }

    [MethodImpl(AggressiveInlining)]
    private static SKRect GetInnerFrameRect(SKRect outerRect)
    {
        return new SKRect(
            outerRect.Left + BG_INNER_FRAME_PADDING,
            outerRect.Top + BG_INNER_FRAME_PADDING,
            outerRect.Right - BG_INNER_FRAME_PADDING,
            outerRect.Bottom - BG_INNER_FRAME_PADDING);
    }

    [MethodImpl(AggressiveInlining)]
    private void ConfigureInnerFramePaint(SKPaint paint)
    {
        paint.Style = SKPaintStyle.Fill;
        paint.Color = new SKColor(105, 105, 105);
        paint.IsAntialias = UseAntiAlias;
    }

    [MethodImpl(AggressiveInlining)]
    private static SKRect GetBackgroundRect(SKRect innerFrameRect)
    {
        return new SKRect(
            innerFrameRect.Left + BG_BACKGROUND_PADDING,
            innerFrameRect.Top + BG_BACKGROUND_PADDING,
            innerFrameRect.Right - BG_BACKGROUND_PADDING,
            innerFrameRect.Bottom - BG_BACKGROUND_PADDING);
    }

    [MethodImpl(AggressiveInlining)]
    private void ConfigureBackgroundPaint(SKPaint paint, SKRect backgroundRect)
    {
        paint.Style = SKPaintStyle.Fill;
        paint.IsAntialias = UseAntiAlias;
        paint.Shader = SKShader.CreateLinearGradient(
            new SKPoint(backgroundRect.Left, backgroundRect.Top),
            new SKPoint(backgroundRect.Left, backgroundRect.Bottom),
            _gaugeBackgroundColors,
            null,
            SKShaderTileMode.Clamp);
    }

    private void DrawVuText(SKCanvas canvas, SKRect backgroundRect, float rectHeight) =>
        _logger.Safe(() => HandleDrawVuText(canvas, backgroundRect, rectHeight),
                     LogPrefix,
                     "Error drawing VU text");

    private void HandleDrawVuText(SKCanvas canvas, SKRect backgroundRect, float rectHeight)
    {
        using var textPaint = _paintPool.Get();
        ConfigureVuTextPaint(textPaint);

        using var font = new SKFont(
            SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold),
            rectHeight * BG_VU_TEXT_SIZE_FACTOR);

        canvas.DrawText(
            "VU",
            backgroundRect.MidX,
            backgroundRect.Bottom - backgroundRect.Height * BG_VU_TEXT_BOTTOM_OFFSET_FACTOR,
            SKTextAlign.Center,
            font,
            textPaint);
    }

    [MethodImpl(AggressiveInlining)]
    private void ConfigureVuTextPaint(SKPaint paint)
    {
        paint.Color = SKColors.Black;
        paint.IsAntialias = UseAntiAlias;
    }

    private void DrawScale(SKCanvas canvas, SKRect rect, bool isOverlayActive) =>
        _logger.Safe(() => HandleDrawScale(canvas, rect, isOverlayActive),
                     LogPrefix,
                     "Error drawing scale");

    private void HandleDrawScale(SKCanvas canvas, SKRect rect, bool isOverlayActive)
    {
        var scaleParams = GetScaleParameters(rect, isOverlayActive);

        using var tickPaint = _paintPool.Get();
        using var textPaint = _paintPool.Get();

        ConfigureScalePaints(tickPaint, textPaint);

        foreach (var (value, label) in _majorMarks)
            DrawMark(canvas, scaleParams, value, label, isOverlayActive, tickPaint, textPaint);

        foreach (float value in _minorMarkValues)
            DrawMark(canvas, scaleParams, value, null, isOverlayActive, tickPaint, null);
    }

    [MethodImpl(AggressiveInlining)]
    private static (float centerX, float centerY, float radiusX, float radiusY) GetScaleParameters(
        SKRect rect, bool isOverlayActive)
    {
        float centerX = rect.MidX;
        float centerY = rect.MidY + rect.Height * SCALE_CENTER_Y_OFFSET_FACTOR;
        float radiusX = rect.Width * (isOverlayActive ? SCALE_RADIUS_X_FACTOR_OVERLAY : SCALE_RADIUS_X_FACTOR);
        float radiusY = rect.Height * (isOverlayActive ? SCALE_RADIUS_Y_FACTOR_OVERLAY : SCALE_RADIUS_Y_FACTOR);

        return (centerX, centerY, radiusX, radiusY);
    }

    [MethodImpl(AggressiveInlining)]
    private void ConfigureScalePaints(SKPaint tickPaint, SKPaint textPaint)
    {
        tickPaint.IsAntialias = UseAntiAlias;
        tickPaint.StrokeWidth = SCALE_TICK_STROKE_WIDTH;

        textPaint.Color = SKColors.Black;
        textPaint.IsAntialias = UseAntiAlias;
        if (UseAdvancedEffects)
            textPaint.ImageFilter = SKImageFilter.CreateDropShadow(
                0.5f, 0.5f, 0.5f, 0.5f, new SKColor(255, 255, 255, 180));
    }

    private static void DrawMark(
        SKCanvas canvas,
        (float centerX, float centerY, float radiusX, float radiusY) scaleParams,
        float value,
        string? label,
        bool isOverlayActive,
        SKPaint tickPaint,
        SKPaint? textPaint) =>
        HandleDrawMark(canvas, scaleParams, value, label, isOverlayActive, tickPaint, textPaint);

    private static void HandleDrawMark(
        SKCanvas canvas,
        (float centerX, float centerY, float radiusX, float radiusY) scaleParams,
        float value,
        string? label,
        bool isOverlayActive,
        SKPaint tickPaint,
        SKPaint? textPaint)
    {
        var (centerX, centerY, radiusX, radiusY) = scaleParams;
        var (angle, radian) = CalculateAngleForValue(value);
        var tickPoints = CalculateTickPoints(centerX, centerY, radiusX, radiusY, radian, value, label, isOverlayActive);

        DrawTick(canvas, tickPoints, value, label, tickPaint);

        if (!string.IsNullOrEmpty(label) && textPaint != null)
        {
            DrawTickLabel(canvas, centerX, centerY, radiusX, radiusY, value, label, angle, radian, isOverlayActive, textPaint);
        }
    }

    [MethodImpl(AggressiveInlining)]
    private static (float angle, float radian) CalculateAngleForValue(float value)
    {
        float normalizedValue = (value - DB_MIN) / (DB_MAX - DB_MIN);
        float angle = ANGLE_START + normalizedValue * ANGLE_TOTAL_RANGE;
        float radian = angle * (MathF.PI / 180.0f);

        return (angle, radian);
    }

    [MethodImpl(AggressiveInlining)]
    private static (float x1, float y1, float x2, float y2) CalculateTickPoints(
        float centerX,
        float centerY,
        float radiusX,
        float radiusY,
        float radian,
        float value,
        string? label,
        bool isOverlayActive)
    {
        float tickLength = radiusY * (label != null
            ? isOverlayActive
                ? value == 0 ? SCALE_TICK_LENGTH_ZERO_FACTOR_OVERLAY : SCALE_TICK_LENGTH_FACTOR_OVERLAY
                : value == 0 ? SCALE_TICK_LENGTH_ZERO_FACTOR : SCALE_TICK_LENGTH_FACTOR
            : isOverlayActive ? SCALE_TICK_LENGTH_MINOR_FACTOR_OVERLAY : SCALE_TICK_LENGTH_MINOR_FACTOR);

        float x1 = centerX + (radiusX - tickLength) * Cos(radian);
        float y1 = centerY + (radiusY - tickLength) * Sin(radian);
        float x2 = centerX + radiusX * Cos(radian);
        float y2 = centerY + radiusY * Sin(radian);

        return (x1, y1, x2, y2);
    }

    [MethodImpl(AggressiveInlining)]
    private static void DrawTick(
        SKCanvas canvas,
        (float x1, float y1, float x2, float y2) points,
        float value,
        string? label,
        SKPaint tickPaint)
    {
        var (x1, y1, x2, y2) = points;

        if (label != null)
        {
            using var tickGradient = SKShader.CreateLinearGradient(
                new SKPoint(x1, y1),
                new SKPoint(x2, y2),
                value >= 0 ? _redTickColors : _grayTickColors,
                null,
                SKShaderTileMode.Clamp);

            tickPaint.Shader = tickGradient;
            canvas.DrawLine(x1, y1, x2, y2, tickPaint);
            tickPaint.Shader = null;
        }
        else
        {
            tickPaint.Color = value >= 0 ? new SKColor(220, 0, 0) : new SKColor(80, 80, 80);
            canvas.DrawLine(x1, y1, x2, y2, tickPaint);
        }
    }

    private static void DrawTickLabel(
        SKCanvas canvas,
        float centerX,
        float centerY,
        float radiusX,
        float radiusY,
        float value,
        string label,
        float angle,
        float radian,
        bool isOverlayActive,
        SKPaint textPaint) =>
        HandleDrawTickLabel(canvas, centerX, centerY, radiusX, radiusY, value, label, angle, radian, isOverlayActive, textPaint);

    private static void HandleDrawTickLabel(
        SKCanvas canvas,
        float centerX,
        float centerY,
        float radiusX,
        float radiusY,
        float value,
        string label,
        float angle,
        float radian,
        bool isOverlayActive,
        SKPaint textPaint)
    {
        float textOffset = radiusY * (isOverlayActive ? SCALE_TEXT_OFFSET_FACTOR_OVERLAY : SCALE_TEXT_OFFSET_FACTOR);
        float textSize = radiusY * (isOverlayActive ? SCALE_TEXT_SIZE_FACTOR_OVERLAY : SCALE_TEXT_SIZE_FACTOR);

        using var font = new SKFont(SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold), textSize);

        if (value == 0)
        {
            font.Size *= SCALE_TEXT_SIZE_MULTIPLIER_ZERO;
            font.Embolden = true;
        }
        else
        {
            font.Embolden = false;
        }

        textPaint.Color = value >= 0 ? new SKColor(200, 0, 0) : SKColors.Black;

        float textX = centerX + (radiusX + textOffset) * Cos(radian);
        float textY = centerY + (radiusY + textOffset) * Sin(radian) + font.Metrics.Descent;

        SKTextAlign textAlign = SKTextAlign.Center;
        if (angle < -120f)
            textAlign = SKTextAlign.Right;
        else if (angle > -60f)
            textAlign = SKTextAlign.Left;

        canvas.DrawText(label, textX, textY, textAlign, font, textPaint);
    }

    private void DrawNeedle(
        SKCanvas canvas,
        SKRect rect,
        float needlePosition,
        bool isOverlayActive,
        SKPaint _) =>
        _logger.Safe(() => HandleDrawNeedle(canvas, rect, needlePosition, isOverlayActive, _),
                     LogPrefix,
                     "Error drawing needle");

    private void HandleDrawNeedle(
        SKCanvas canvas,
        SKRect rect,
        float needlePosition,
        bool isOverlayActive,
        SKPaint _)
    {
        var needleParams = GetNeedleParameters(rect, needlePosition, isOverlayActive);

        DrawNeedleShape(canvas, needleParams);
        DrawNeedleCenter(canvas, needleParams, isOverlayActive);
    }

    [MethodImpl(AggressiveInlining)]
    private (float centerX, float centerY, float radiusX, float radiusY, float angle, float needleLength)
        GetNeedleParameters(SKRect rect, float needlePosition, bool isOverlayActive)
    {
        float centerX = rect.MidX;
        float centerY = rect.MidY + rect.Height * _config.NeedleCenterYOffsetMultiplier;
        float radiusX = rect.Width * (isOverlayActive ? SCALE_RADIUS_X_FACTOR_OVERLAY : SCALE_RADIUS_X_FACTOR);
        float radiusY = rect.Height * (isOverlayActive ? SCALE_RADIUS_Y_FACTOR_OVERLAY : SCALE_RADIUS_Y_FACTOR);
        float angle = ANGLE_START + needlePosition * ANGLE_TOTAL_RANGE;
        float needleLength = MathF.Min(radiusX, radiusY) * _config.NeedleLengthMultiplier;

        return (centerX, centerY, radiusX, radiusY, angle, needleLength);
    }

    private void DrawNeedleShape(
        SKCanvas canvas,
        (float centerX,
        float centerY,
        float radiusX,
        float radiusY,
        float angle,
        float needleLength)
        needleParams) =>
        _logger.Safe(() => HandleDrawNeedleShape(canvas, needleParams),
                     LogPrefix,
                     "Error drawing needle shape");

    private void HandleDrawNeedleShape(
        SKCanvas canvas,
        (float centerX,
        float centerY,
        float radiusX,
        float radiusY,
        float angle,
        float needleLength)
        needleParams)
    {
        var (centerX, centerY, radiusX, radiusY, angle, needleLength) = needleParams;

        using var needlePath = _pathPool.Get();
        var (ellipseX, ellipseY) = CalculatePointOnEllipse(centerX, centerY, radiusX, radiusY, angle);
        var (unitX, unitY, _) = NormalizeVector(ellipseX - centerX, ellipseY - centerY);

        float baseWidth = NEEDLE_STROKE_WIDTH * NEEDLE_BASE_WIDTH_MULTIPLIER;
        float perpX = -unitY;
        float perpY = unitX;

        float tipX = centerX + unitX * needleLength;
        float tipY = centerY + unitY * needleLength;
        float baseLeftX = centerX + perpX * baseWidth;
        float baseLeftY = centerY + perpY * baseWidth;
        float baseRightX = centerX - perpX * baseWidth;
        float baseRightY = centerY - perpY * baseWidth;

        needlePath.MoveTo(tipX, tipY);
        needlePath.LineTo(baseLeftX, baseLeftY);
        needlePath.LineTo(baseRightX, baseRightY);
        needlePath.Close();

        using var needlePaint = _paintPool.Get();
        ConfigureNeedlePaint(needlePaint, centerX, centerY, tipX, tipY, needleParams.angle);
        canvas.DrawPath(needlePath, needlePaint);

        using var outlinePaint = _paintPool.Get();
        ConfigureNeedleOutlinePaint(outlinePaint);
        canvas.DrawPath(needlePath, outlinePaint);
    }

    [MethodImpl(AggressiveInlining)]
    private void ConfigureNeedlePaint(
        SKPaint needlePaint,
        float centerX,
        float centerY,
        float tipX,
        float tipY,
        float angle)
    {
        float normalizedAngle = (angle - ANGLE_START) / ANGLE_TOTAL_RANGE;

        needlePaint.Style = SKPaintStyle.Fill;
        needlePaint.IsAntialias = UseAntiAlias;
        needlePaint.Shader = SKShader.CreateLinearGradient(
            new SKPoint(centerX, centerY),
            new SKPoint(tipX, tipY),
            new[] { new SKColor(40, 40, 40), normalizedAngle > 0.75f ? SKColors.Red : new SKColor(180, 0, 0) },
            null,
            SKShaderTileMode.Clamp);

        if (UseAdvancedEffects)
            needlePaint.ImageFilter = SKImageFilter.CreateDropShadow(
                2f, 2f, 1.5f, 1.5f, SKColors.Black.WithAlpha(100));
    }

    [MethodImpl(AggressiveInlining)]
    private void ConfigureNeedleOutlinePaint(SKPaint outlinePaint)
    {
        outlinePaint.Style = SKPaintStyle.Stroke;
        outlinePaint.StrokeWidth = 0.8f;
        outlinePaint.Color = SKColors.Black.WithAlpha(180);
        outlinePaint.IsAntialias = UseAntiAlias;
    }

    private void DrawNeedleCenter(
        SKCanvas canvas,
        (float centerX, float centerY, float radiusX, float radiusY, float angle, float needleLength) needleParams,
        bool isOverlayActive) =>
        _logger.Safe(() => HandleDrawNeedleCenter(canvas, needleParams, isOverlayActive),
                     LogPrefix,
                     "Error drawing needle center");

    private void HandleDrawNeedleCenter(
        SKCanvas canvas,
        (float centerX, float centerY, float radiusX, float radiusY, float angle, float needleLength) needleParams,
        bool isOverlayActive)
    {
        var (centerX, centerY, _, _, _, _) = needleParams;

        float centerCircleRadius = needleParams.radiusX *
            (isOverlayActive ? NEEDLE_CENTER_CIRCLE_RADIUS_OVERLAY : NEEDLE_CENTER_CIRCLE_RADIUS);

        using var centerCirclePaint = _paintPool.Get();
        ConfigureCenterCirclePaint(centerCirclePaint, centerX, centerY, centerCircleRadius);
        canvas.DrawCircle(centerX, centerY, centerCircleRadius, centerCirclePaint);

        using var highlightPaint = _paintPool.Get();
        ConfigureHighlightPaint(highlightPaint);
        canvas.DrawCircle(
            centerX - centerCircleRadius * 0.25f,
            centerY - centerCircleRadius * 0.25f,
            centerCircleRadius * 0.4f,
            highlightPaint);
    }

    [MethodImpl(AggressiveInlining)]
    private void ConfigureCenterCirclePaint(SKPaint paint, float centerX, float centerY, float radius)
    {
        paint.Style = SKPaintStyle.Fill;
        paint.IsAntialias = UseAntiAlias;
        paint.Shader = SKShader.CreateRadialGradient(
            new SKPoint(centerX - radius * 0.3f, centerY - radius * 0.3f),
            radius * 2,
            _needleCenterColors,
            _centerColorStops,
            SKShaderTileMode.Clamp);
    }

    [MethodImpl(AggressiveInlining)]
    private void ConfigureHighlightPaint(SKPaint paint)
    {
        paint.Color = SKColors.White.WithAlpha(150);
        paint.Style = SKPaintStyle.Fill;
        paint.IsAntialias = UseAntiAlias;
    }

    private void DrawPeakLamp(SKCanvas canvas, SKRect rect, bool isOverlayActive) =>
        _logger.Safe(() => HandleDrawPeakLamp(canvas, rect, isOverlayActive),
                     LogPrefix,
                     "Error drawing peak lamp");

    private void HandleDrawPeakLamp(SKCanvas canvas, SKRect rect, bool isOverlayActive)
    {
        var lampParams = GetPeakLampParameters(rect, isOverlayActive);

        if (_state.PeakActive && UseAdvancedEffects)
        {
            DrawPeakLampGlow(canvas, lampParams);
        }

        DrawPeakLampBody(canvas, lampParams);
        DrawPeakLampLabel(canvas, lampParams, isOverlayActive);
    }

    [MethodImpl(AggressiveInlining)]
    private static (float lampX, float lampY, float lampRadius) GetPeakLampParameters(SKRect rect,
                                                                                      bool isOverlayActive)
    {
        float radiusX = rect.Width * (isOverlayActive ? SCALE_RADIUS_X_FACTOR_OVERLAY
            : SCALE_RADIUS_X_FACTOR);

        float radiusY = rect.Height * (isOverlayActive ? SCALE_RADIUS_Y_FACTOR_OVERLAY
            : SCALE_RADIUS_Y_FACTOR);

        float lampRadius = MathF.Min(radiusX, radiusY)
            * (isOverlayActive ? PEAK_LAMP_RADIUS_FACTOR_OVERLAY : PEAK_LAMP_RADIUS_FACTOR);

        float lampX = rect.Right
            - rect.Width
            * (isOverlayActive ? PEAK_LAMP_X_OFFSET_FACTOR_OVERLAY : PEAK_LAMP_X_OFFSET_FACTOR);

        float lampY = rect.Top
            + rect.Height
            * (isOverlayActive ? PEAK_LAMP_Y_OFFSET_FACTOR_OVERLAY : PEAK_LAMP_Y_OFFSET_FACTOR);

        return (lampX, lampY, lampRadius);
    }

    private void DrawPeakLampGlow(
        SKCanvas canvas,
        (float lampX, float lampY, float lampRadius) lampParams) =>
        _logger.Safe(() => HandleDrawPeakLampGlow(canvas, lampParams),
                     LogPrefix,
                     "Error drawing peak lamp glow");

    private void HandleDrawPeakLampGlow(
        SKCanvas canvas,
        (float lampX, float lampY, float lampRadius) lampParams)
    {
        var (lampX, lampY, lampRadius) = lampParams;

        using var glowPaint = _paintPool.Get();
        ConfigureGlowPaint(glowPaint, lampRadius);
        canvas.DrawCircle(
            lampX,
            lampY,
            lampRadius * PEAK_LAMP_GLOW_RADIUS_MULTIPLIER,
            glowPaint);
    }

    [MethodImpl(AggressiveInlining)]
    private void ConfigureGlowPaint(SKPaint paint, float radius)
    {
        paint.Color = SKColors.Red.WithAlpha(80);
        paint.MaskFilter = SKMaskFilter.CreateBlur(
            SKBlurStyle.Normal,
            radius * PEAK_LAMP_GLOW_RADIUS_MULTIPLIER);
        paint.IsAntialias = UseAntiAlias;
    }

    private void DrawPeakLampBody(
        SKCanvas canvas,
        (float lampX, float lampY, float lampRadius) lampParams) =>
        _logger.Safe(() => HandleDrawPeakLampBody(canvas, lampParams),
                     LogPrefix,
                     "Error drawing peak lamp body");

    private void HandleDrawPeakLampBody(
        SKCanvas canvas,
        (float lampX, float lampY, float lampRadius) lampParams)
    {
        var (lampX, lampY, lampRadius) = lampParams;

        using var innerPaint = _paintPool.Get();
        ConfigureInnerLampPaint(innerPaint, lampX, lampY, lampRadius);
        canvas.DrawCircle(
            lampX,
            lampY,
            lampRadius * PEAK_LAMP_INNER_RADIUS_MULTIPLIER,
            innerPaint);

        using var reflectionPaint = _paintPool.Get();
        ConfigureReflectionPaint(reflectionPaint);
        canvas.DrawCircle(
            lampX - lampRadius * 0.3f,
            lampY - lampRadius * 0.3f,
            lampRadius * 0.25f,
            reflectionPaint);

        using var rimPaint = _paintPool.Get();
        ConfigureRimPaint(rimPaint);
        canvas.DrawCircle(lampX, lampY, lampRadius, rimPaint);
    }

    [MethodImpl(AggressiveInlining)]
    private void ConfigureInnerLampPaint(SKPaint paint, float lampX, float lampY, float radius)
    {
        paint.Style = SKPaintStyle.Fill;
        paint.IsAntialias = UseAntiAlias;
        paint.Shader = SKShader.CreateRadialGradient(
            new SKPoint(lampX - radius * 0.2f, lampY - radius * 0.2f),
            radius * PEAK_LAMP_INNER_RADIUS_MULTIPLIER,
            _state.PeakActive ? _activeLampColors : _inactiveLampColors,
            _centerColorStops,
            SKShaderTileMode.Clamp);
    }

    [MethodImpl(AggressiveInlining)]
    private void ConfigureReflectionPaint(SKPaint paint)
    {
        paint.Color = SKColors.White.WithAlpha(180);
        paint.Style = SKPaintStyle.Fill;
        paint.IsAntialias = UseAntiAlias;
    }

    [MethodImpl(AggressiveInlining)]
    private void ConfigureRimPaint(SKPaint paint)
    {
        paint.Color = new SKColor(40, 40, 40);
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = PEAK_LAMP_RIM_STROKE_WIDTH * 1.2f;
        paint.IsAntialias = UseAntiAlias;
    }

    private void DrawPeakLampLabel(
        SKCanvas canvas,
        (float lampX, float lampY, float lampRadius) lampParams,
        bool isOverlayActive) =>
        _logger.Safe(() => HandleDrawPeakLampLabel(canvas, lampParams, isOverlayActive),
                     LogPrefix,
                     "Error drawing peak lamp label");

    private void HandleDrawPeakLampLabel(
        SKCanvas canvas,
        (float lampX, float lampY, float lampRadius) lampParams,
        bool isOverlayActive)
    {
        var (lampX, lampY, lampRadius) = lampParams;

        using var peakTextPaint = _paintPool.Get();
        ConfigurePeakTextPaint(peakTextPaint);

        float textSize = lampRadius *
            (isOverlayActive ? PEAK_LAMP_TEXT_SIZE_FACTOR_OVERLAY : PEAK_LAMP_TEXT_SIZE_FACTOR) * 1.2f;

        using var font = new SKFont(
            SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold), textSize);

        float textYOffset = lampRadius * PEAK_LAMP_TEXT_Y_OFFSET_FACTOR + font.Metrics.Descent;

        canvas.DrawText(
            "PEAK",
            lampX,
            lampY + textYOffset,
            SKTextAlign.Center,
            font,
            peakTextPaint);
    }

    [MethodImpl(AggressiveInlining)]
    private void ConfigurePeakTextPaint(SKPaint paint)
    {
        paint.Color = _state.PeakActive ? SKColors.Red : new SKColor(180, 0, 0);
        paint.IsAntialias = UseAntiAlias;

        if (UseAdvancedEffects)
            paint.ImageFilter = SKImageFilter.CreateDropShadow(
                1, 1, 1, 1, SKColors.Black.WithAlpha(150));
    }

    private void UpdateGaugeState(float[] spectrum) =>
        _logger.Safe(() => HandleUpdateGaugeState(spectrum),
                     LogPrefix,
                     "Error updating gauge state");

    private void HandleUpdateGaugeState(float[] spectrum)
    {
        lock (_stateLock)
        {
            float dbValue = CalculateLoudness(spectrum);
            float smoothedDb = SmoothValue(dbValue);
            float targetNeedlePosition = CalculateNeedlePosition(smoothedDb);

            _state = _state with
            {
                TargetNeedlePosition = targetNeedlePosition,
                PreviousValue = smoothedDb
            };

            UpdateNeedlePosition();
            UpdatePeakState();
        }
    }

    private void UpdatePeakState() =>
        _logger.Safe(() => HandleUpdatePeakState(),
                     LogPrefix,
                     "Error updating peak state");

    private void HandleUpdatePeakState()
    {
        float thresholdPosition = CalculateNeedlePosition(DB_PEAK_THRESHOLD);

        if (_state.CurrentNeedlePosition >= thresholdPosition)
        {
            _state = _state with { PeakActive = true };
            _peakHoldCounter = PEAK_HOLD_DURATION;
        }
        else if (_peakHoldCounter > 0)
        {
            _peakHoldCounter--;
        }
        else
        {
            _state = _state with { PeakActive = false };
        }
    }

    [MethodImpl(AggressiveInlining)]
    private static float CalculateLoudness(float[] spectrum)
    {
        if (spectrum.Length == 0) return DB_MIN;

        float sumOfSquares = 0f;
        if (IsHardwareAccelerated && spectrum.Length >= Vector<float>.Count)
        {
            sumOfSquares = CalculateSumOfSquaresVectorized(spectrum);
        }
        else
        {
            for (int i = 0; i < spectrum.Length; i++)
                sumOfSquares += spectrum[i] * spectrum[i];
        }

        float rms = Sqrt(sumOfSquares / spectrum.Length);
        float db = 20f * Log10(MathF.Max(rms, RENDERING_MIN_DB_CLAMP));
        return Clamp(db, DB_MIN, DB_MAX);
    }

    [MethodImpl(AggressiveInlining)]
    private static float CalculateSumOfSquaresVectorized(float[] spectrum)
    {
        float sumOfSquares = 0f;
        int vectorSize = Vector<float>.Count;
        int vectorizedLength = spectrum.Length - spectrum.Length % vectorSize;

        for (int i = 0; i < vectorizedLength; i += vectorSize)
        {
            Vector<float> values = new(spectrum, i);
            sumOfSquares += Dot(values, values);
        }

        for (int i = vectorizedLength; i < spectrum.Length; i++)
            sumOfSquares += spectrum[i] * spectrum[i];

        return sumOfSquares;
    }

    [MethodImpl(AggressiveInlining)]
    private float SmoothValue(float newValue)
    {
        float smoothingFactor = newValue > _state.PreviousValue ?
            _config.SmoothingFactorIncrease : _config.SmoothingFactorDecrease;
        return _state.PreviousValue + smoothingFactor * (newValue - _state.PreviousValue);
    }

    [MethodImpl(AggressiveInlining)]
    private static float CalculateNeedlePosition(float db)
    {
        float normalizedPosition = (Clamp(db, DB_MIN, DB_MAX) - DB_MIN) /
                                  (DB_MAX - DB_MIN);
        return Clamp(normalizedPosition, RENDERING_MARGIN, 1f - RENDERING_MARGIN);
    }

    private void UpdateNeedlePosition() =>
        _logger.Safe(() => HandleUpdateNeedlePosition(),
                     LogPrefix,
                     "Error updating needle position");

    private void HandleUpdateNeedlePosition()
    {
        float difference = _state.TargetNeedlePosition - _state.CurrentNeedlePosition;
        float speed = difference * (difference > 0 ? _config.RiseSpeed : _config.FallSpeed);
        float easedSpeed = speed * (1 - _config.Damping) * (1 - MathF.Abs(difference));

        _state = _state with
        {
            CurrentNeedlePosition = Clamp(_state.CurrentNeedlePosition + easedSpeed, 0f, 1f)
        };
    }

    private static void InitializeMinorMarks() =>
        HandleInitializeMinorMarks();

    private static void HandleInitializeMinorMarks()
    {
        _minorMarkValues.Clear();
        var majorValues = _majorMarks.Select(m => m.Value).OrderBy(v => v).ToList();
        for (int i = 0; i < majorValues.Count - 1; i++)
        {
            float start = majorValues[i];
            float end = majorValues[i + 1];
            float step = (end - start) / MINOR_MARKS_DIVISOR;
            for (float value = start + step; value < end; value += step)
                _minorMarkValues.Add(value);
        }
    }

    [MethodImpl(AggressiveInlining)]
    private static (float x, float y) CalculatePointOnEllipse(
        float centerX, float centerY, float radiusX, float radiusY, float angleDegrees)
    {
        float radian = angleDegrees * (MathF.PI / 180.0f);
        return (centerX + radiusX * Cos(radian), centerY + radiusY * Sin(radian));
    }

    [MethodImpl(AggressiveInlining)]
    private static (float unitX, float unitY, float length) NormalizeVector(float dx, float dy)
    {
        float length = Sqrt(dx * dx + dy * dy);
        return length > 0 ? (dx / length, dy / length, length) : (0f, 0f, 0f);
    }

    [MethodImpl(AggressiveInlining)]
    private static SKRect CalculateGaugeRect(SKImageInfo info)
    {
        float aspectRatio = RENDERING_ASPECT_RATIO;
        float width = info.Width / (float)info.Height > aspectRatio
            ? info.Height * RENDERING_GAUGE_RECT_PADDING * aspectRatio
            : info.Width * RENDERING_GAUGE_RECT_PADDING;
        float height = width / aspectRatio;
        float left = (info.Width - width) / 2;
        float top = (info.Height - height) / 2;
        return new SKRect(left, top, left + width, top + height);
    }

    protected override void OnDispose()
    {
        _renderSemaphore?.Dispose();
        base.OnDispose();
        _logger.Debug(LogPrefix, "Disposed");
    }
}