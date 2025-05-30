#nullable enable

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class GaugeRenderer : EffectSpectrumRenderer<GaugeRenderer.QualitySettings>
{
    private static readonly Lazy<GaugeRenderer> _instance =
        new(() => new GaugeRenderer());

    public static GaugeRenderer GetInstance() => _instance.Value;

    private const float 
        DB_MAX = 5f,
        DB_MIN = -30f,
        DB_PEAK_THRESHOLD = 5f,
        ANGLE_START = -150f,
        ANGLE_END = -30f,
        ANGLE_TOTAL_RANGE = ANGLE_END - ANGLE_START,
        NEEDLE_LENGTH_MULTIPLIER = 1.55f,
        NEEDLE_LENGTH_MULTIPLIER_OVERLAY = 1.6f,
        NEEDLE_CENTER_Y_OFFSET = 0.4f,
        NEEDLE_CENTER_Y_OFFSET_OVERLAY = 0.35f,
        NEEDLE_STROKE_WIDTH = 2.25f,
        NEEDLE_CENTER_RADIUS_OVERLAY = 0.015f,
        NEEDLE_CENTER_RADIUS = 0.02f,
        NEEDLE_BASE_WIDTH = 2.5f,
        BG_OUTER_CORNER_RADIUS = 8f,
        BG_INNER_PADDING = 4f,
        BG_INNER_CORNER_RADIUS = 6f,
        BG_BACKGROUND_PADDING = 4f,
        BG_BACKGROUND_CORNER_RADIUS = 4f,
        BG_VU_TEXT_SIZE = 0.2f,
        BG_VU_TEXT_BOTTOM_OFFSET = 0.2f,
        SCALE_CENTER_Y_OFFSET = 0.15f,
        SCALE_RADIUS_X_OVERLAY = 0.4f,
        SCALE_RADIUS_X = 0.45f,
        SCALE_RADIUS_Y_OVERLAY = 0.45f,
        SCALE_RADIUS_Y = 0.5f,
        SCALE_TEXT_OFFSET_OVERLAY = 0.1f,
        SCALE_TEXT_OFFSET = 0.12f,
        SCALE_TEXT_SIZE_OVERLAY = 0.08f,
        SCALE_TEXT_SIZE = 0.1f,
        SCALE_TICK_LENGTH_ZERO_OVERLAY = 0.12f,
        SCALE_TICK_LENGTH_ZERO = 0.15f,
        SCALE_TICK_LENGTH_OVERLAY = 0.07f,
        SCALE_TICK_LENGTH = 0.08f,
        SCALE_TICK_LENGTH_MINOR_OVERLAY = 0.05f,
        SCALE_TICK_LENGTH_MINOR = 0.06f,
        SCALE_TICK_STROKE_WIDTH = 1.8f,
        SCALE_TEXT_SIZE_ZERO_MULTIPLIER = 1.15f,
        PEAK_LAMP_RADIUS_OVERLAY = 0.04f,
        PEAK_LAMP_RADIUS = 0.05f,
        PEAK_LAMP_X_OFFSET_OVERLAY = 0.12f,
        PEAK_LAMP_X_OFFSET = 0.1f,
        PEAK_LAMP_Y_OFFSET_OVERLAY = 0.18f,
        PEAK_LAMP_Y_OFFSET = 0.2f,
        PEAK_LAMP_TEXT_SIZE_OVERLAY = 1.2f,
        PEAK_LAMP_TEXT_SIZE = 1.5f,
        PEAK_LAMP_TEXT_Y_OFFSET = 2.5f,
        PEAK_LAMP_RIM_STROKE_WIDTH = 1f,
        PEAK_LAMP_GLOW_RADIUS = 1.5f,
        PEAK_LAMP_INNER_RADIUS = 0.8f,
        RENDERING_ASPECT_RATIO = 2.0f,
        RENDERING_GAUGE_RECT_PADDING = 0.8f,
        RENDERING_MIN_DB_CLAMP = 1e-10f,
        RENDERING_MARGIN = 0.05f;

    private const int MINOR_MARKS_DIVISOR = 3,
        PEAK_HOLD_DURATION = 15;

    private static readonly (float Value, string Label)[] _majorMarks =
    [
        (-30f, "-30"),
        (-20f, "-20"),
        (-10f, "-10"),
        (-7f, "-7"),
        (-5f, "-5"),
        (-3f, "-3"),
        (0f, "0"),
        (3f, "+3"),
        (5f, "+5")
    ];

    private static readonly List<float> _minorMarkValues = InitializeMinorMarks();

    private static readonly SKColor[] _gaugeBackgroundColors =
        [new(250, 250, 240), new(230, 230, 215)];
    private static readonly SKColor[] _needleCenterColors =
        [SKColors.White, new(180, 180, 180), new(60, 60, 60)];
    private static readonly float[] _centerColorStops =
        [0.0f, 0.3f, 1.0f];
    private static readonly SKColor[] _redTickColors =
        [new(200, 0, 0), SKColors.Red];
    private static readonly SKColor[] _grayTickColors =
        [new(60, 60, 60), new(100, 100, 100)];
    private static readonly SKColor[] _activeLampColors =
        [SKColors.White, new(255, 180, 180), SKColors.Red];
    private static readonly SKColor[] _inactiveLampColors =
        [new(220, 220, 220), new(180, 0, 0), new(80, 0, 0)];

    private float _currentNeedlePosition = 0f;
    private float _currentDbValue = DB_MIN;
    private int _peakHoldCounter;
    private bool _peakActive;

    public sealed class QualitySettings
    {
        public bool UseGlow { get; init; }
        public bool UseGradients { get; init; }
        public bool UseHighlights { get; init; }
        public float SmoothingFactorIncrease { get; init; }
        public float SmoothingFactorDecrease { get; init; }
        public float RiseSpeed { get; init; }
    }

    protected override IReadOnlyDictionary<RenderQuality, QualitySettings>
        QualitySettingsPresets
    { get; } = new Dictionary<RenderQuality, QualitySettings>
    {
        [RenderQuality.Low] = new()
        {
            UseGlow = false,
            UseGradients = false,
            UseHighlights = false,
            SmoothingFactorIncrease = 0.2f,
            SmoothingFactorDecrease = 0.05f,
            RiseSpeed = 0.15f
        },
        [RenderQuality.Medium] = new()
        {
            UseGlow = true,
            UseGradients = true,
            UseHighlights = true,
            SmoothingFactorIncrease = 0.2f,
            SmoothingFactorDecrease = 0.05f,
            RiseSpeed = 0.15f
        },
        [RenderQuality.High] = new()
        {
            UseGlow = true,
            UseGradients = true,
            UseHighlights = true,
            SmoothingFactorIncrease = 0.15f,
            SmoothingFactorDecrease = 0.04f,
            RiseSpeed = 0.2f
        }
    };

    protected override void RenderEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        RenderParameters renderParams,
        SKPaint passedInPaint)
    {
        if (CurrentQualitySettings == null || renderParams.EffectiveBarCount <= 0)
            return;

        var (isValid, processedSpectrum) = ProcessSpectrum(
            spectrum,
            renderParams.EffectiveBarCount,
            applyTemporalSmoothing: true);

        if (!isValid || processedSpectrum == null)
            return;

        var gaugeData = CalculateGaugeData(processedSpectrum, info);

        if (!ValidateGaugeData(gaugeData))
            return;

        RenderGaugeVisualization(
            canvas,
            gaugeData,
            renderParams,
            passedInPaint);
    }

    private GaugeData CalculateGaugeData(float[] spectrum, SKImageInfo info)
    {
        float dbValue = CalculateLoudness(spectrum);
        UpdateNeedlePosition(dbValue);
        var gaugeRect = CalculateGaugeRect(info);

        return new GaugeData(
            GaugeRect: gaugeRect,
            DbValue: _currentDbValue,
            NeedlePosition: _currentNeedlePosition,
            IsPeakActive: _peakActive);
    }

    private static bool ValidateGaugeData(GaugeData data) => 
        data.GaugeRect.Width > 0 && data.GaugeRect.Height > 0;
    
    private void RenderGaugeVisualization(
        SKCanvas canvas,
        GaugeData data,
        RenderParameters renderParams,
        SKPaint basePaint)
    {
        var settings = CurrentQualitySettings!;

        DrawGaugeBackground(canvas, data.GaugeRect);

        RenderWithOverlay(canvas, () =>
        {
            DrawScale(canvas, data.GaugeRect);
            DrawNeedle(canvas, data, settings);
            DrawPeakLamp(canvas, data, settings);
        });
    }

    private void UpdateNeedlePosition(float dbValue)
    {
        var settings = CurrentQualitySettings!;

        float smoothingFactor = dbValue > _currentDbValue
            ? settings.SmoothingFactorIncrease
            : settings.SmoothingFactorDecrease;

        if (IsOverlayActive)
            smoothingFactor *= 0.5f;
        
        _currentDbValue = Lerp(_currentDbValue, dbValue, smoothingFactor);
        float targetPosition = CalculateNeedlePosition(_currentDbValue);

        _currentNeedlePosition = Lerp(
            _currentNeedlePosition,
            targetPosition,
            settings.RiseSpeed);

        UpdatePeakState(targetPosition);
    }

    private void UpdatePeakState(float needlePosition)
    {
        float thresholdPosition = CalculateNeedlePosition(DB_PEAK_THRESHOLD);

        if (needlePosition >= thresholdPosition)
        {
            _peakActive = true;
            _peakHoldCounter = PEAK_HOLD_DURATION;
        }
        else if (_peakHoldCounter > 0)
        {
            _peakHoldCounter--;
        }
        else
        {
            _peakActive = false;
        }
    }

    private void DrawGaugeBackground(SKCanvas canvas, SKRect rect)
    {
        var outerPaint = CreatePaint(new SKColor(80, 80, 80), SKPaintStyle.Fill);

        try
        {
            canvas.DrawRoundRect(
                rect,
                BG_OUTER_CORNER_RADIUS,
                BG_OUTER_CORNER_RADIUS,
                outerPaint);
        }
        finally
        {
            ReturnPaint(outerPaint);
        }

        var innerFrameRect = new SKRect(
            rect.Left + BG_INNER_PADDING,
            rect.Top + BG_INNER_PADDING,
            rect.Right - BG_INNER_PADDING,
            rect.Bottom - BG_INNER_PADDING);

        var innerPaint = CreatePaint(new SKColor(105, 105, 105), SKPaintStyle.Fill);

        try
        {
            canvas.DrawRoundRect(
                innerFrameRect,
                BG_INNER_CORNER_RADIUS,
                BG_INNER_CORNER_RADIUS,
                innerPaint);
        }
        finally
        {
            ReturnPaint(innerPaint);
        }

        var backgroundRect = new SKRect(
            innerFrameRect.Left + BG_BACKGROUND_PADDING,
            innerFrameRect.Top + BG_BACKGROUND_PADDING,
            innerFrameRect.Right - BG_BACKGROUND_PADDING,
            innerFrameRect.Bottom - BG_BACKGROUND_PADDING);

        using var backgroundShader = SKShader.CreateLinearGradient(
            new SKPoint(backgroundRect.Left, backgroundRect.Top),
            new SKPoint(backgroundRect.Left, backgroundRect.Bottom),
            _gaugeBackgroundColors,
            null,
            SKShaderTileMode.Clamp);

        var backgroundPaint = CreatePaint(SKColors.White, SKPaintStyle.Fill, backgroundShader);

        try
        {
            canvas.DrawRoundRect(
                backgroundRect,
                BG_BACKGROUND_CORNER_RADIUS,
                BG_BACKGROUND_CORNER_RADIUS,
                backgroundPaint);
        }
        finally
        {
            ReturnPaint(backgroundPaint);
        }

        DrawVuText(canvas, backgroundRect, rect.Height);
    }

    private void DrawVuText(
        SKCanvas canvas,
        SKRect backgroundRect,
        float rectHeight)
    {
        using var font = new SKFont(
            SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold),
            rectHeight * BG_VU_TEXT_SIZE);

        var textPaint = CreatePaint(SKColors.Black, SKPaintStyle.Fill);

        try
        {
            canvas.DrawText(
                "VU",
                backgroundRect.MidX,
                backgroundRect.Bottom - backgroundRect.Height * BG_VU_TEXT_BOTTOM_OFFSET,
                SKTextAlign.Center,
                font,
                textPaint);
        }
        finally
        {
            ReturnPaint(textPaint);
        }
    }

    private void DrawScale(SKCanvas canvas, SKRect rect)
    {
        float centerX = rect.MidX;
        float centerY = rect.MidY + rect.Height * SCALE_CENTER_Y_OFFSET;
        float radiusX = rect.Width * (IsOverlayActive ? SCALE_RADIUS_X_OVERLAY : SCALE_RADIUS_X);
        float radiusY = rect.Height * (IsOverlayActive ? SCALE_RADIUS_Y_OVERLAY : SCALE_RADIUS_Y);

        var tickPaint = CreatePaint(SKColors.Black, SKPaintStyle.Stroke);
        tickPaint.StrokeWidth = SCALE_TICK_STROKE_WIDTH;

        var textPaint = CreatePaint(SKColors.Black, SKPaintStyle.Fill);

        try
        {
            foreach (var (value, label) in _majorMarks)
            {
                DrawMark(canvas, centerX, centerY, radiusX, 
                    radiusY, value, label, tickPaint, textPaint);
            }

            foreach (float value in _minorMarkValues)
            {
                DrawMark(canvas, centerX, centerY, radiusX, 
                    radiusY, value, null, tickPaint, null);
            }
        }
        finally
        {
            ReturnPaint(tickPaint);
            ReturnPaint(textPaint);
        }
    }

    private void DrawMark(
        SKCanvas canvas,
        float centerX,
        float centerY,
        float radiusX,
        float radiusY,
        float value,
        string? label,
        SKPaint tickPaint,
        SKPaint? textPaint)
    {
        float normalizedValue = (value - DB_MIN) / (DB_MAX - DB_MIN);
        float angle = ANGLE_START + normalizedValue * ANGLE_TOTAL_RANGE;
        float radian = angle * (MathF.PI / 180.0f);

        float tickLength = radiusY * GetTickLength(value, label != null);

        float x1 = centerX + (radiusX - tickLength) * MathF.Cos(radian);
        float y1 = centerY + (radiusY - tickLength) * MathF.Sin(radian);
        float x2 = centerX + radiusX * MathF.Cos(radian);
        float y2 = centerY + radiusY * MathF.Sin(radian);

        if (label != null && CurrentQualitySettings!.UseGradients)
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
            tickPaint.Color = value >= 0
                ? new SKColor(220, 0, 0)
                : new SKColor(80, 80, 80);
            canvas.DrawLine(x1, y1, x2, y2, tickPaint);
        }

        if (label != null && textPaint != null)
        {
            DrawTickLabel(
                canvas,
                centerX,
                centerY,
                radiusX,
                radiusY,
                value,
                label,
                angle,
                radian,
                textPaint);
        }
    }

    private void DrawTickLabel(
        SKCanvas canvas,
        float centerX,
        float centerY,
        float radiusX,
        float radiusY,
        float value,
        string label,
        float angle,
        float radian,
        SKPaint textPaint)
    {
        float textOffset = radiusY * (IsOverlayActive ? SCALE_TEXT_OFFSET_OVERLAY : SCALE_TEXT_OFFSET);
        float textSize = radiusY * (IsOverlayActive ? SCALE_TEXT_SIZE_OVERLAY : SCALE_TEXT_SIZE);

        using var font = new SKFont(
            SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold),
            textSize);

        if (value == 0)
        {
            font.Size *= SCALE_TEXT_SIZE_ZERO_MULTIPLIER;
            font.Embolden = true;
        }

        textPaint.Color = value >= 0
            ? new SKColor(200, 0, 0)
            : SKColors.Black;

        float textX = centerX + (radiusX + textOffset) * MathF.Cos(radian);
        float textY = centerY + (radiusY + textOffset) * MathF.Sin(radian) + font.Metrics.Descent;

        SKTextAlign textAlign = angle < -120f ? SKTextAlign.Right :
                               angle > -60f ? SKTextAlign.Left :
                               SKTextAlign.Center;

        canvas.DrawText(label, textX, textY, textAlign, font, textPaint);
    }

    private void DrawNeedle(
        SKCanvas canvas,
        GaugeData data,
        QualitySettings settings)
    {
        float centerX = data.GaugeRect.MidX;

        float centerY = data.GaugeRect.MidY + data.GaugeRect.Height 
            * (IsOverlayActive ? NEEDLE_CENTER_Y_OFFSET_OVERLAY : NEEDLE_CENTER_Y_OFFSET);

        float radiusX = data.GaugeRect.Width 
            * (IsOverlayActive ? SCALE_RADIUS_X_OVERLAY : SCALE_RADIUS_X);

        float radiusY = data.GaugeRect.Height
            * (IsOverlayActive ? SCALE_RADIUS_Y_OVERLAY : SCALE_RADIUS_Y);

        float angle = ANGLE_START + data.NeedlePosition * ANGLE_TOTAL_RANGE;

        float needleLength = MathF.Min(radiusX, radiusY) 
            * (IsOverlayActive ? NEEDLE_LENGTH_MULTIPLIER_OVERLAY : NEEDLE_LENGTH_MULTIPLIER);

        DrawNeedleShape(
            canvas,
            centerX,
            centerY,
            radiusX,
            radiusY,
            angle,
            needleLength,
            settings);

        DrawNeedleCenter(canvas, centerX, centerY, radiusX, settings);
    }

    private void DrawNeedleShape(
        SKCanvas canvas,
        float centerX,
        float centerY,
        float radiusX,
        float radiusY,
        float angle,
        float needleLength,
        QualitySettings settings)
    {
        float radian = angle * (MathF.PI / 180.0f);
        float ellipseX = centerX + radiusX * MathF.Cos(radian);
        float ellipseY = centerY + radiusY * MathF.Sin(radian);

        float dx = ellipseX - centerX;
        float dy = ellipseY - centerY;
        float length = MathF.Sqrt(dx * dx + dy * dy);

        if (length <= 0)
            return;

        float unitX = dx / length;
        float unitY = dy / length;
        float baseWidth = NEEDLE_STROKE_WIDTH * NEEDLE_BASE_WIDTH;

        float tipX = centerX + unitX * needleLength;
        float tipY = centerY + unitY * needleLength;
        float baseLeftX = centerX - unitY * baseWidth;
        float baseLeftY = centerY + unitX * baseWidth;
        float baseRightX = centerX + unitY * baseWidth;
        float baseRightY = centerY - unitX * baseWidth;

        if (settings.UseGradients)
        {
            float normalizedAngle = (angle - ANGLE_START) / ANGLE_TOTAL_RANGE;
            using var needleGradient = SKShader.CreateLinearGradient(
                new SKPoint(centerX, centerY),
                new SKPoint(tipX, tipY),
                new[] {
                    new SKColor(40, 40, 40),
                    normalizedAngle > 0.75f ? SKColors.Red : new SKColor(180, 0, 0)
                },
                null,
                SKShaderTileMode.Clamp);

            var needlePaint = CreatePaint(SKColors.Black, SKPaintStyle.Fill, needleGradient);

            try
            {
                RenderPath(canvas, path =>
                {
                    path.MoveTo(tipX, tipY);
                    path.LineTo(baseLeftX, baseLeftY);
                    path.LineTo(baseRightX, baseRightY);
                    path.Close();
                }, needlePaint);
            }
            finally
            {
                ReturnPaint(needlePaint);
            }
        }
        else
        {
            var needlePaint = CreatePaint(SKColors.Black, SKPaintStyle.Fill);

            try
            {
                RenderPath(canvas, path =>
                {
                    path.MoveTo(tipX, tipY);
                    path.LineTo(baseLeftX, baseLeftY);
                    path.LineTo(baseRightX, baseRightY);
                    path.Close();
                }, needlePaint);
            }
            finally
            {
                ReturnPaint(needlePaint);
            }
        }

        var outlinePaint = CreatePaint(SKColors.Black.WithAlpha(180), SKPaintStyle.Stroke);
        outlinePaint.StrokeWidth = 0.8f;

        try
        {
            RenderPath(canvas, path =>
            {
                path.MoveTo(tipX, tipY);
                path.LineTo(baseLeftX, baseLeftY);
                path.LineTo(baseRightX, baseRightY);
                path.Close();
            }, outlinePaint);
        }
        finally
        {
            ReturnPaint(outlinePaint);
        }
    }

    private void DrawNeedleCenter(
        SKCanvas canvas,
        float centerX,
        float centerY,
        float radiusX,
        QualitySettings settings)
    {
        float centerCircleRadius = radiusX * (IsOverlayActive ? NEEDLE_CENTER_RADIUS_OVERLAY : NEEDLE_CENTER_RADIUS);

        if (settings.UseGradients)
        {
            using var centerShader = SKShader.CreateRadialGradient(
                new SKPoint(
                    centerX - centerCircleRadius * 0.3f,
                    centerY - centerCircleRadius * 0.3f),
                centerCircleRadius * 2,
                _needleCenterColors,
                _centerColorStops,
                SKShaderTileMode.Clamp);

            var centerPaint = CreatePaint(SKColors.Black, SKPaintStyle.Fill, centerShader);

            try
            {
                canvas.DrawCircle(centerX, centerY, centerCircleRadius, centerPaint);
            }
            finally
            {
                ReturnPaint(centerPaint);
            }
        }
        else
        {
            var centerPaint = CreatePaint(new SKColor(60, 60, 60), SKPaintStyle.Fill);

            try
            {
                canvas.DrawCircle(centerX, centerY, centerCircleRadius, centerPaint);
            }
            finally
            {
                ReturnPaint(centerPaint);
            }
        }

        if (settings.UseHighlights)
        {
            var highlightPaint = CreatePaint(SKColors.White.WithAlpha(150), SKPaintStyle.Fill);

            try
            {
                canvas.DrawCircle(
                    centerX - centerCircleRadius * 0.25f,
                    centerY - centerCircleRadius * 0.25f,
                    centerCircleRadius * 0.4f,
                    highlightPaint);
            }
            finally
            {
                ReturnPaint(highlightPaint);
            }
        }
    }

    private void DrawPeakLamp(
        SKCanvas canvas,
        GaugeData data,
        QualitySettings settings)
    {
        float radiusX = data.GaugeRect.Width
            * (IsOverlayActive ? SCALE_RADIUS_X_OVERLAY : SCALE_RADIUS_X);

        float radiusY = data.GaugeRect.Height
            * (IsOverlayActive ? SCALE_RADIUS_Y_OVERLAY : SCALE_RADIUS_Y);

        float lampRadius = MathF.Min(radiusX, radiusY) 
            * (IsOverlayActive ? PEAK_LAMP_RADIUS_OVERLAY : PEAK_LAMP_RADIUS);

        float lampX = data.GaugeRect.Right
            - data.GaugeRect.Width
            * (IsOverlayActive ? PEAK_LAMP_X_OFFSET_OVERLAY : PEAK_LAMP_X_OFFSET);

        float lampY = data.GaugeRect.Top
            + data.GaugeRect.Height
            * (IsOverlayActive ? PEAK_LAMP_Y_OFFSET_OVERLAY : PEAK_LAMP_Y_OFFSET);

        if (data.IsPeakActive && UseAdvancedEffects && settings.UseGlow)
        {
            var glowPaint = CreatePaint(SKColors.Red.WithAlpha(77), SKPaintStyle.Fill);

            using var blurFilter = SKMaskFilter.CreateBlur(
                SKBlurStyle.Normal,
                lampRadius * PEAK_LAMP_GLOW_RADIUS);
            glowPaint.MaskFilter = blurFilter;

            try
            {
                canvas.DrawCircle(
                    lampX,
                    lampY,
                    lampRadius * PEAK_LAMP_GLOW_RADIUS,
                    glowPaint);
            }
            finally
            {
                ReturnPaint(glowPaint);
            }
        }

        if (settings.UseGradients)
        {
            using var innerShader = SKShader.CreateRadialGradient(
                new SKPoint(
                    lampX - lampRadius * 0.2f,
                    lampY - lampRadius * 0.2f),
                lampRadius * PEAK_LAMP_INNER_RADIUS,
                data.IsPeakActive ? _activeLampColors : _inactiveLampColors,
                _centerColorStops,
                SKShaderTileMode.Clamp);

            var innerPaint = CreatePaint(SKColors.White, SKPaintStyle.Fill, innerShader);

            try
            {
                canvas.DrawCircle(
                    lampX,
                    lampY,
                    lampRadius * PEAK_LAMP_INNER_RADIUS,
                    innerPaint);
            }
            finally
            {
                ReturnPaint(innerPaint);
            }
        }
        else
        {
            var lampColor = data.IsPeakActive ? SKColors.Red : new SKColor(80, 0, 0);
            var innerPaint = CreatePaint(lampColor, SKPaintStyle.Fill);

            try
            {
                canvas.DrawCircle(
                    lampX,
                    lampY,
                    lampRadius * PEAK_LAMP_INNER_RADIUS,
                    innerPaint);
            }
            finally
            {
                ReturnPaint(innerPaint);
            }
        }

        if (settings.UseHighlights)
        {
            var highlightPaint = CreatePaint(SKColors.White.WithAlpha(180), SKPaintStyle.Fill);

            try
            {
                canvas.DrawCircle(
                    lampX - lampRadius * 0.3f,
                    lampY - lampRadius * 0.3f,
                    lampRadius * 0.25f,
                    highlightPaint);
            }
            finally
            {
                ReturnPaint(highlightPaint);
            }
        }

        var rimPaint = CreatePaint(new SKColor(40, 40, 40), SKPaintStyle.Stroke);
        rimPaint.StrokeWidth = PEAK_LAMP_RIM_STROKE_WIDTH * 1.2f;

        try
        {
            canvas.DrawCircle(lampX, lampY, lampRadius, rimPaint);
        }
        finally
        {
            ReturnPaint(rimPaint);
        }

        DrawPeakLampLabel(canvas, lampX, lampY, lampRadius, data.IsPeakActive);
    }

    private void DrawPeakLampLabel(
        SKCanvas canvas,
        float lampX,
        float lampY,
        float lampRadius,
        bool isPeakActive)
    {
        float textSize = lampRadius * (IsOverlayActive ? PEAK_LAMP_TEXT_SIZE_OVERLAY : PEAK_LAMP_TEXT_SIZE) * 1.2f;

        using var font = new SKFont(
            SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold),
            textSize);

        float textYOffset = lampRadius * PEAK_LAMP_TEXT_Y_OFFSET + font.Metrics.Descent;

        var textPaint = CreatePaint(
            isPeakActive ? SKColors.Red : new SKColor(180, 0, 0),
            SKPaintStyle.Fill);

        try
        {
            canvas.DrawText("PEAK",
                lampX,
                lampY + textYOffset,
                SKTextAlign.Center,
                font,
                textPaint);
        }
        finally
        {
            ReturnPaint(textPaint);
        }
    }

    private float GetTickLength(float value, bool isMajor)
    {
        if (!isMajor)
        {
            return IsOverlayActive
                ? SCALE_TICK_LENGTH_MINOR_OVERLAY
                : SCALE_TICK_LENGTH_MINOR;
        }

        if (value == 0)
        {
            return IsOverlayActive
                ? SCALE_TICK_LENGTH_ZERO_OVERLAY
                : SCALE_TICK_LENGTH_ZERO;
        }

        return IsOverlayActive
            ? SCALE_TICK_LENGTH_OVERLAY
            : SCALE_TICK_LENGTH;
    }

    private static float CalculateLoudness(float[] spectrum)
    {
        if (spectrum.Length == 0)
            return DB_MIN;

        float sumOfSquares = 0f;
        for (int i = 0; i < spectrum.Length; i++)
        {
            sumOfSquares += spectrum[i] * spectrum[i];
        }

        float rms = MathF.Sqrt(sumOfSquares / spectrum.Length);
        float db = 20f * MathF.Log10(MathF.Max(rms, RENDERING_MIN_DB_CLAMP));
        return Clamp(db, DB_MIN, DB_MAX);
    }

    private static float CalculateNeedlePosition(float db)
    {
        float normalizedPosition = (Clamp(db, DB_MIN, DB_MAX) - DB_MIN) / (DB_MAX - DB_MIN);
        return Clamp(normalizedPosition, RENDERING_MARGIN, 1f - RENDERING_MARGIN);
    }

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

    private static List<float> InitializeMinorMarks()
    {
        var minorMarks = new List<float>();
        var majorValues = _majorMarks
            .Select(m => m.Value)
            .OrderBy(v => v)
            .ToList();

        for (int i = 0; i < majorValues.Count - 1; i++)
        {
            float start = majorValues[i];
            float end = majorValues[i + 1];
            float step = (end - start) / MINOR_MARKS_DIVISOR;

            for (float value = start + step; value < end; value += step)
            {
                minorMarks.Add(value);
            }
        }

        return minorMarks;
    }

    protected override int GetMaxBarsForQuality() => Quality switch
    {
        RenderQuality.Low => 32,
        RenderQuality.Medium => 64,
        RenderQuality.High => 128,
        _ => 64
    };

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();

        float smoothingFactor = Quality switch
        {
            RenderQuality.Low => 0.4f,
            RenderQuality.Medium => 0.3f,
            RenderQuality.High => 0.25f,
            _ => 0.3f
        };

        if (IsOverlayActive)
            smoothingFactor *= 1.2f;
        
        SetProcessingSmoothingFactor(smoothingFactor);
        RequestRedraw();
    }

    protected override void OnDispose()
    {
        _currentNeedlePosition = 0f;
        _currentDbValue = DB_MIN;
        _peakHoldCounter = 0;
        _peakActive = false;
        base.OnDispose();
    }

    private record GaugeData(
        SKRect GaugeRect,
        float DbValue,
        float NeedlePosition,
        bool IsPeakActive);
}