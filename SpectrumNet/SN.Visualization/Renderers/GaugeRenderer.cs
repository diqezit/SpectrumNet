//#nullable enable

//using static SpectrumNet.SN.Visualization.Renderers.GaugeRenderer.Constants;
//using static System.MathF;

//namespace SpectrumNet.SN.Visualization.Renderers;

//public sealed class GaugeRenderer() : EffectSpectrumRenderer
//{
//    private const string LogPrefix = nameof(GaugeRenderer);

//    private static readonly Lazy<GaugeRenderer> _instance =
//        new(() => new GaugeRenderer());

//    public static GaugeRenderer GetInstance() => _instance.Value;

//    public static class Constants
//    {
//        public const float
//            DB_MAX = 5f,
//            DB_MIN = -30f,
//            DB_PEAK_THRESHOLD = 5f,
//            ANGLE_START = -150f,
//            ANGLE_END = -30f,
//            ANGLE_TOTAL_RANGE = ANGLE_END - ANGLE_START,
//            NEEDLE_LENGTH_MULTIPLIER = 1.55f,
//            NEEDLE_CENTER_Y_OFFSET = 0.4f,
//            NEEDLE_STROKE_WIDTH = 2.25f,
//            NEEDLE_CENTER_RADIUS_OVERLAY = 0.015f,
//            NEEDLE_CENTER_RADIUS = 0.02f,
//            NEEDLE_BASE_WIDTH = 2.5f,
//            BG_OUTER_CORNER_RADIUS = 8f,
//            BG_INNER_PADDING = 4f,
//            BG_INNER_CORNER_RADIUS = 6f,
//            BG_BACKGROUND_PADDING = 4f,
//            BG_BACKGROUND_CORNER_RADIUS = 4f,
//            BG_VU_TEXT_SIZE = 0.2f,
//            BG_VU_TEXT_BOTTOM_OFFSET = 0.2f,
//            SCALE_CENTER_Y_OFFSET = 0.15f,
//            SCALE_RADIUS_X_OVERLAY = 0.4f,
//            SCALE_RADIUS_X = 0.45f,
//            SCALE_RADIUS_Y_OVERLAY = 0.45f,
//            SCALE_RADIUS_Y = 0.5f,
//            SCALE_TEXT_OFFSET_OVERLAY = 0.1f,
//            SCALE_TEXT_OFFSET = 0.12f,
//            SCALE_TEXT_SIZE_OVERLAY = 0.08f,
//            SCALE_TEXT_SIZE = 0.1f,
//            SCALE_TICK_LENGTH_ZERO_OVERLAY = 0.12f,
//            SCALE_TICK_LENGTH_ZERO = 0.15f,
//            SCALE_TICK_LENGTH_OVERLAY = 0.07f,
//            SCALE_TICK_LENGTH = 0.08f,
//            SCALE_TICK_LENGTH_MINOR_OVERLAY = 0.05f,
//            SCALE_TICK_LENGTH_MINOR = 0.06f,
//            SCALE_TICK_STROKE_WIDTH = 1.8f,
//            SCALE_TEXT_SIZE_ZERO_MULTIPLIER = 1.15f,
//            PEAK_LAMP_RADIUS_OVERLAY = 0.04f,
//            PEAK_LAMP_RADIUS = 0.05f,
//            PEAK_LAMP_X_OFFSET_OVERLAY = 0.12f,
//            PEAK_LAMP_X_OFFSET = 0.1f,
//            PEAK_LAMP_Y_OFFSET_OVERLAY = 0.18f,
//            PEAK_LAMP_Y_OFFSET = 0.2f,
//            PEAK_LAMP_TEXT_SIZE_OVERLAY = 1.2f,
//            PEAK_LAMP_TEXT_SIZE = 1.5f,
//            PEAK_LAMP_TEXT_Y_OFFSET = 2.5f,
//            PEAK_LAMP_RIM_STROKE_WIDTH = 1f,
//            PEAK_LAMP_GLOW_RADIUS = 1.5f,
//            PEAK_LAMP_INNER_RADIUS = 0.8f,
//            RENDERING_ASPECT_RATIO = 2.0f,
//            RENDERING_GAUGE_RECT_PADDING = 0.8f,
//            RENDERING_MIN_DB_CLAMP = 1e-10f,
//            RENDERING_MARGIN = 0.05f;

//        public const int
//            MINOR_MARKS_DIVISOR = 3,
//            PEAK_HOLD_DURATION = 15;

//        public static readonly Dictionary<RenderQuality, QualitySettings> QualityPresets = new()
//        {
//            [RenderQuality.Low] = new(
//                UseAdvancedEffects: false,
//                UseAntialiasing: false
//            ),
//            [RenderQuality.Medium] = new(
//                UseAdvancedEffects: true,
//                UseAntialiasing: true
//            ),
//            [RenderQuality.High] = new(
//                UseAdvancedEffects: true,
//                UseAntialiasing: true
//            )
//        };

//        public record QualitySettings(
//            bool UseAdvancedEffects,
//            bool UseAntialiasing
//        );
//    }

//    private readonly record struct GaugeConfig(
//        float SmoothingFactorIncrease,
//        float SmoothingFactorDecrease,
//        float RiseSpeed,
//        float FallSpeed,
//        float Damping,
//        float NeedleLengthMultiplier,
//        float NeedleCenterYOffsetMultiplier);

//    private static readonly (float Value, string Label)[] _majorMarks =
//    [
//        (-30f, "-30"), (-20f, "-20"), (-10f, "-10"),
//        (-7f, "-7"), (-5f, "-5"), (-3f, "-3"),
//        (0f, "0"), (3f, "+3"), (5f, "+5")
//    ];

//    private static readonly List<float> _minorMarkValues =
//        InitializeMinorMarks();

//    private static readonly SKColor[] _gaugeBackgroundColors =
//        [new(250, 250, 240), new(230, 230, 215)];
//    private static readonly SKColor[] _needleCenterColors =
//        [SKColors.White, new(180, 180, 180), new(60, 60, 60)];
//    private static readonly float[] _centerColorStops =
//        [0.0f, 0.3f, 1.0f];
//    private static readonly SKColor[] _redTickColors =
//        [new(200, 0, 0), SKColors.Red];
//    private static readonly SKColor[] _grayTickColors =
//        [new(60, 60, 60), new(100, 100, 100)];
//    private static readonly SKColor[] _activeLampColors =
//        [SKColors.White, new(255, 180, 180), SKColors.Red];
//    private static readonly SKColor[] _inactiveLampColors =
//        [new(220, 220, 220), new(180, 0, 0), new(80, 0, 0)];

//    private GaugeConfig _config = new(
//        SmoothingFactorIncrease: 0.2f,
//        SmoothingFactorDecrease: 0.05f,
//        RiseSpeed: 0.15f,
//        FallSpeed: 0.03f,
//        Damping: 0.7f,
//        NeedleLengthMultiplier: NEEDLE_LENGTH_MULTIPLIER,
//        NeedleCenterYOffsetMultiplier: NEEDLE_CENTER_Y_OFFSET
//    );
//    private QualitySettings _currentSettings = QualityPresets[RenderQuality.Medium];
//    private int _peakHoldCounter;
//    private bool _peakActive;

//    protected override void OnInitialize()
//    {
//        base.OnInitialize();
//        AnimateValues([0f, DB_MIN], 1f);
//    }

//    protected override void OnQualitySettingsApplied() =>
//        _currentSettings = QualityPresets[Quality];

//    protected override void OnConfigurationChanged()
//    {
//        _config = _config with
//        {
//            SmoothingFactorIncrease = IsOverlayActive ? 0.1f : 0.2f,
//            SmoothingFactorDecrease = IsOverlayActive ? 0.02f : 0.05f,
//            NeedleLengthMultiplier = IsOverlayActive ? 1.6f : NEEDLE_LENGTH_MULTIPLIER,
//            NeedleCenterYOffsetMultiplier = IsOverlayActive ? 0.35f : NEEDLE_CENTER_Y_OFFSET
//        };

//        if (IsOverlayActive)
//            SetOverlayTransparency(0.75f);
//    }

//    protected override void RenderEffect(
//        SKCanvas canvas,
//        float[] spectrum,
//        SKImageInfo info,
//        float barWidth,
//        float barSpacing,
//        int barCount,
//        SKPaint paint)
//    {
//        float dbValue = CalculateLoudness(spectrum);
//        var animated = GetAnimatedValues();
//        float previousValue = animated.Length > 1 ? animated[1] : DB_MIN;

//        float smoothingFactor = dbValue > previousValue ?
//            _config.SmoothingFactorIncrease :
//            _config.SmoothingFactorDecrease;
//        float smoothedDb = Lerp(previousValue, dbValue, smoothingFactor);
//        float targetNeedlePosition = CalculateNeedlePosition(smoothedDb);

//        AnimateValues([targetNeedlePosition, smoothedDb], _config.RiseSpeed);
//        UpdatePeakState(targetNeedlePosition);

//        var gaugeRect = CalculateGaugeRect(info);
//        DrawGaugeBackground(canvas, gaugeRect);
//        DrawScale(canvas, gaugeRect);
//        DrawNeedle(canvas, gaugeRect, animated[0]);
//        DrawPeakLamp(canvas, gaugeRect);
//    }

//    private void UpdatePeakState(float needlePosition)
//    {
//        float thresholdPosition = CalculateNeedlePosition(DB_PEAK_THRESHOLD);

//        if (needlePosition >= thresholdPosition)
//        {
//            _peakActive = true;
//            _peakHoldCounter = PEAK_HOLD_DURATION;
//        }
//        else if (_peakHoldCounter > 0)
//        {
//            _peakHoldCounter--;
//        }
//        else
//        {
//            _peakActive = false;
//        }
//    }

//    private void DrawGaugeBackground(SKCanvas canvas, SKRect rect)
//    {
//        canvas.DrawRoundRect(
//            rect,
//            BG_OUTER_CORNER_RADIUS,
//            BG_OUTER_CORNER_RADIUS,
//            CreateStandardPaint(new SKColor(80, 80, 80)));

//        var innerFrameRect = new SKRect(
//            rect.Left + BG_INNER_PADDING,
//            rect.Top + BG_INNER_PADDING,
//            rect.Right - BG_INNER_PADDING,
//            rect.Bottom - BG_INNER_PADDING);

//        canvas.DrawRoundRect(
//            innerFrameRect,
//            BG_INNER_CORNER_RADIUS,
//            BG_INNER_CORNER_RADIUS,
//            CreateStandardPaint(new SKColor(105, 105, 105)));

//        var backgroundRect = new SKRect(
//            innerFrameRect.Left + BG_BACKGROUND_PADDING,
//            innerFrameRect.Top + BG_BACKGROUND_PADDING,
//            innerFrameRect.Right - BG_BACKGROUND_PADDING,
//            innerFrameRect.Bottom - BG_BACKGROUND_PADDING);

//        var backgroundPaint = CreateStandardPaint(SKColors.White);
//        backgroundPaint.Shader = SKShader.CreateLinearGradient(
//            new SKPoint(backgroundRect.Left, backgroundRect.Top),
//            new SKPoint(backgroundRect.Left, backgroundRect.Bottom),
//            _gaugeBackgroundColors,
//            null,
//            SKShaderTileMode.Clamp);

//        canvas.DrawRoundRect(
//            backgroundRect,
//            BG_BACKGROUND_CORNER_RADIUS,
//            BG_BACKGROUND_CORNER_RADIUS,
//            backgroundPaint);

//        ReturnPaint(backgroundPaint);

//        DrawVuText(canvas, backgroundRect, rect.Height);
//    }

//    private void DrawVuText(
//        SKCanvas canvas,
//        SKRect backgroundRect,
//        float rectHeight)
//    {
//        using var font = new SKFont(
//            SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold),
//            rectHeight * BG_VU_TEXT_SIZE);

//        canvas.DrawText(
//            "VU",
//            backgroundRect.MidX,
//            backgroundRect.Bottom -
//                backgroundRect.Height * BG_VU_TEXT_BOTTOM_OFFSET,
//            SKTextAlign.Center,
//            font,
//            CreateStandardPaint(SKColors.Black));
//    }

//    private void DrawScale(SKCanvas canvas, SKRect rect)
//    {
//        float centerX = rect.MidX;
//        float centerY = rect.MidY + rect.Height * SCALE_CENTER_Y_OFFSET;
//        float radiusX = rect.Width *
//            (IsOverlayActive ? SCALE_RADIUS_X_OVERLAY : SCALE_RADIUS_X);
//        float radiusY = rect.Height *
//            (IsOverlayActive ? SCALE_RADIUS_Y_OVERLAY : SCALE_RADIUS_Y);

//        var tickPaint = CreateStrokePaint(
//            SKColors.Black,
//            SCALE_TICK_STROKE_WIDTH);

//        var textPaint = CreateStandardPaint(SKColors.Black);

//        foreach (var (value, label) in _majorMarks)
//        {
//            DrawMark(
//                canvas,
//                centerX, centerY,
//                radiusX, radiusY,
//                value, label,
//                tickPaint, textPaint);
//        }

//        foreach (float value in _minorMarkValues)
//        {
//            DrawMark(
//                canvas,
//                centerX, centerY,
//                radiusX, radiusY,
//                value, null,
//                tickPaint, null);
//        }

//        ReturnPaint(tickPaint);
//        ReturnPaint(textPaint);
//    }

//    private void DrawMark(
//        SKCanvas canvas,
//        float centerX, float centerY,
//        float radiusX, float radiusY,
//        float value, string? label,
//        SKPaint tickPaint, SKPaint? textPaint)
//    {
//        float normalizedValue = (value - DB_MIN) / (DB_MAX - DB_MIN);
//        float angle = ANGLE_START + normalizedValue * ANGLE_TOTAL_RANGE;
//        float radian = angle * (MathF.PI / 180.0f);

//        float tickLength = radiusY * GetTickLength(value, label != null);

//        float x1 = centerX + (radiusX - tickLength) * Cos(radian);
//        float y1 = centerY + (radiusY - tickLength) * Sin(radian);
//        float x2 = centerX + radiusX * Cos(radian);
//        float y2 = centerY + radiusY * Sin(radian);

//        if (label != null)
//        {
//            using var tickGradient = SKShader.CreateLinearGradient(
//                new SKPoint(x1, y1),
//                new SKPoint(x2, y2),
//                value >= 0 ? _redTickColors : _grayTickColors,
//                null,
//                SKShaderTileMode.Clamp);
//            tickPaint.Shader = tickGradient;
//            canvas.DrawLine(x1, y1, x2, y2, tickPaint);
//            tickPaint.Shader = null;
//        }
//        else
//        {
//            tickPaint.Color = value >= 0 ?
//                new SKColor(220, 0, 0) :
//                new SKColor(80, 80, 80);
//            canvas.DrawLine(x1, y1, x2, y2, tickPaint);
//        }

//        if (label != null && textPaint != null)
//        {
//            DrawTickLabel(
//                canvas,
//                centerX, centerY,
//                radiusX, radiusY,
//                value, label,
//                angle, radian,
//                textPaint);
//        }
//    }

//    private float GetTickLength(float value, bool isMajor)
//    {
//        if (!isMajor)
//        {
//            return IsOverlayActive ?
//                SCALE_TICK_LENGTH_MINOR_OVERLAY :
//                SCALE_TICK_LENGTH_MINOR;
//        }

//        if (value == 0)
//        {
//            return IsOverlayActive ?
//                SCALE_TICK_LENGTH_ZERO_OVERLAY :
//                SCALE_TICK_LENGTH_ZERO;
//        }

//        return IsOverlayActive ?
//            SCALE_TICK_LENGTH_OVERLAY :
//            SCALE_TICK_LENGTH;
//    }

//    private void DrawTickLabel(
//        SKCanvas canvas,
//        float centerX, float centerY,
//        float radiusX, float radiusY,
//        float value, string label,
//        float angle, float radian,
//        SKPaint textPaint)
//    {
//        float textOffset = radiusY *
//            (IsOverlayActive ? SCALE_TEXT_OFFSET_OVERLAY : SCALE_TEXT_OFFSET);
//        float textSize = radiusY *
//            (IsOverlayActive ? SCALE_TEXT_SIZE_OVERLAY : SCALE_TEXT_SIZE);

//        using var font = new SKFont(
//            SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold),
//            textSize);

//        if (value == 0)
//        {
//            font.Size *= SCALE_TEXT_SIZE_ZERO_MULTIPLIER;
//            font.Embolden = true;
//        }

//        textPaint.Color = value >= 0 ?
//            new SKColor(200, 0, 0) :
//            SKColors.Black;

//        float textX = centerX + (radiusX + textOffset) * Cos(radian);
//        float textY = centerY + (radiusY + textOffset) * Sin(radian) +
//            font.Metrics.Descent;

//        SKTextAlign textAlign = angle < -120f ? SKTextAlign.Right :
//                               angle > -60f ? SKTextAlign.Left :
//                               SKTextAlign.Center;

//        canvas.DrawText(label, textX, textY, textAlign, font, textPaint);
//    }

//    private void DrawNeedle(
//        SKCanvas canvas,
//        SKRect rect,
//        float currentNeedlePosition)
//    {
//        float centerX = rect.MidX;
//        float centerY = rect.MidY +
//            rect.Height * _config.NeedleCenterYOffsetMultiplier;
//        float radiusX = rect.Width *
//            (IsOverlayActive ? SCALE_RADIUS_X_OVERLAY : SCALE_RADIUS_X);
//        float radiusY = rect.Height *
//            (IsOverlayActive ? SCALE_RADIUS_Y_OVERLAY : SCALE_RADIUS_Y);
//        float angle = ANGLE_START +
//            currentNeedlePosition * ANGLE_TOTAL_RANGE;
//        float needleLength = MathF.Min(radiusX, radiusY) *
//            _config.NeedleLengthMultiplier;

//        DrawNeedleShape(
//            canvas,
//            centerX, centerY,
//            radiusX, radiusY,
//            angle, needleLength);
//        DrawNeedleCenter(canvas, centerX, centerY, radiusX);
//    }

//    private void DrawNeedleShape(
//        SKCanvas canvas,
//        float centerX, float centerY,
//        float radiusX, float radiusY,
//        float angle, float needleLength)
//    {
//        var needlePath = GetPath();

//        float radian = angle * (MathF.PI / 180.0f);
//        float ellipseX = centerX + radiusX * Cos(radian);
//        float ellipseY = centerY + radiusY * Sin(radian);

//        float dx = ellipseX - centerX;
//        float dy = ellipseY - centerY;
//        float length = Sqrt(dx * dx + dy * dy);

//        if (length > 0)
//        {
//            float unitX = dx / length;
//            float unitY = dy / length;
//            float baseWidth = NEEDLE_STROKE_WIDTH * NEEDLE_BASE_WIDTH;

//            float tipX = centerX + unitX * needleLength;
//            float tipY = centerY + unitY * needleLength;
//            float baseLeftX = centerX - unitY * baseWidth;
//            float baseLeftY = centerY + unitX * baseWidth;
//            float baseRightX = centerX + unitY * baseWidth;
//            float baseRightY = centerY - unitX * baseWidth;

//            needlePath.MoveTo(tipX, tipY);
//            needlePath.LineTo(baseLeftX, baseLeftY);
//            needlePath.LineTo(baseRightX, baseRightY);
//            needlePath.Close();

//            var needlePaint = CreateStandardPaint(SKColors.Black);
//            float normalizedAngle = (angle - ANGLE_START) / ANGLE_TOTAL_RANGE;

//            needlePaint.Shader = SKShader.CreateLinearGradient(
//                new SKPoint(centerX, centerY),
//                new SKPoint(tipX, tipY),
//                new[] {
//                    new SKColor(40, 40, 40),
//                    normalizedAngle > 0.75f ?
//                        SKColors.Red :
//                        new SKColor(180, 0, 0)
//                },
//                null,
//                SKShaderTileMode.Clamp);

//            canvas.DrawPath(needlePath, needlePaint);
//            ReturnPaint(needlePaint);

//            var outlinePaint = CreateStrokePaint(
//                SKColors.Black.WithAlpha(180),
//                0.8f);
//            canvas.DrawPath(needlePath, outlinePaint);
//            ReturnPaint(outlinePaint);
//        }

//        ReturnPath(needlePath);
//    }

//    private void DrawNeedleCenter(
//        SKCanvas canvas,
//        float centerX,
//        float centerY,
//        float radiusX)
//    {
//        float centerCircleRadius = radiusX *
//            (IsOverlayActive ? NEEDLE_CENTER_RADIUS_OVERLAY : NEEDLE_CENTER_RADIUS);

//        var centerCirclePaint = CreateStandardPaint(SKColors.Black);
//        centerCirclePaint.Shader = SKShader.CreateRadialGradient(
//            new SKPoint(
//                centerX - centerCircleRadius * 0.3f,
//                centerY - centerCircleRadius * 0.3f),
//            centerCircleRadius * 2,
//            _needleCenterColors,
//            _centerColorStops,
//            SKShaderTileMode.Clamp);

//        canvas.DrawCircle(centerX, centerY, centerCircleRadius, centerCirclePaint);
//        ReturnPaint(centerCirclePaint);

//        canvas.DrawCircle(
//            centerX - centerCircleRadius * 0.25f,
//            centerY - centerCircleRadius * 0.25f,
//            centerCircleRadius * 0.4f,
//            CreateStandardPaint(SKColors.White.WithAlpha(150)));
//    }

//    private void DrawPeakLamp(SKCanvas canvas, SKRect rect)
//    {
//        float radiusX = rect.Width *
//            (IsOverlayActive ? SCALE_RADIUS_X_OVERLAY : SCALE_RADIUS_X);
//        float radiusY = rect.Height *
//            (IsOverlayActive ? SCALE_RADIUS_Y_OVERLAY : SCALE_RADIUS_Y);
//        float lampRadius = MathF.Min(radiusX, radiusY) *
//            (IsOverlayActive ? PEAK_LAMP_RADIUS_OVERLAY : PEAK_LAMP_RADIUS);
//        float lampX = rect.Right - rect.Width *
//            (IsOverlayActive ? PEAK_LAMP_X_OFFSET_OVERLAY : PEAK_LAMP_X_OFFSET);
//        float lampY = rect.Top + rect.Height *
//            (IsOverlayActive ? PEAK_LAMP_Y_OFFSET_OVERLAY : PEAK_LAMP_Y_OFFSET);

//        if (_peakActive && UseAdvancedEffects)
//        {
//            RenderGlow(
//                canvas,
//                new SKRect(
//                    lampX - lampRadius * PEAK_LAMP_GLOW_RADIUS,
//                    lampY - lampRadius * PEAK_LAMP_GLOW_RADIUS,
//                    lampX + lampRadius * PEAK_LAMP_GLOW_RADIUS,
//                    lampY + lampRadius * PEAK_LAMP_GLOW_RADIUS),
//                SKColors.Red,
//                lampRadius * PEAK_LAMP_GLOW_RADIUS,
//                0.3f);
//        }

//        var innerPaint = CreateStandardPaint(SKColors.White);
//        innerPaint.Shader = SKShader.CreateRadialGradient(
//            new SKPoint(
//                lampX - lampRadius * 0.2f,
//                lampY - lampRadius * 0.2f),
//            lampRadius * PEAK_LAMP_INNER_RADIUS,
//            _peakActive ? _activeLampColors : _inactiveLampColors,
//            _centerColorStops,
//            SKShaderTileMode.Clamp);

//        canvas.DrawCircle(
//            lampX, lampY,
//            lampRadius * PEAK_LAMP_INNER_RADIUS,
//            innerPaint);

//        ReturnPaint(innerPaint);

//        canvas.DrawCircle(
//            lampX - lampRadius * 0.3f,
//            lampY - lampRadius * 0.3f,
//            lampRadius * 0.25f,
//            CreateStandardPaint(SKColors.White.WithAlpha(180)));

//        canvas.DrawCircle(
//            lampX, lampY, lampRadius,
//            CreateStrokePaint(
//                new SKColor(40, 40, 40),
//                PEAK_LAMP_RIM_STROKE_WIDTH * 1.2f));

//        DrawPeakLampLabel(canvas, lampX, lampY, lampRadius);
//    }

//    private void DrawPeakLampLabel(
//        SKCanvas canvas,
//        float lampX,
//        float lampY,
//        float lampRadius)
//    {
//        float textSize = lampRadius *
//            (IsOverlayActive ? PEAK_LAMP_TEXT_SIZE_OVERLAY : PEAK_LAMP_TEXT_SIZE) *
//            1.2f;

//        using var font = new SKFont(
//            SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold),
//            textSize);

//        float textYOffset = lampRadius * PEAK_LAMP_TEXT_Y_OFFSET +
//            font.Metrics.Descent;

//        canvas.DrawText(
//            "PEAK",
//            lampX,
//            lampY + textYOffset,
//            SKTextAlign.Center,
//            font,
//            CreateStandardPaint(
//                _peakActive ? SKColors.Red : new SKColor(180, 0, 0)));
//    }

//    private static float CalculateLoudness(float[] spectrum)
//    {
//        if (spectrum.Length == 0) return DB_MIN;

//        float sumOfSquares = 0f;
//        for (int i = 0; i < spectrum.Length; i++)
//            sumOfSquares += spectrum[i] * spectrum[i];

//        float rms = Sqrt(sumOfSquares / spectrum.Length);
//        float db = 20f * Log10(MathF.Max(rms, RENDERING_MIN_DB_CLAMP));
//        return Clamp(db, DB_MIN, DB_MAX);
//    }

//    private static float CalculateNeedlePosition(float db)
//    {
//        float normalizedPosition = (Clamp(db, DB_MIN, DB_MAX) - DB_MIN) /
//                                  (DB_MAX - DB_MIN);
//        return Clamp(normalizedPosition, RENDERING_MARGIN, 1f - RENDERING_MARGIN);
//    }

//    private static List<float> InitializeMinorMarks()
//    {
//        var minorMarks = new List<float>();
//        var majorValues = _majorMarks
//            .Select(m => m.Value)
//            .OrderBy(v => v)
//            .ToList();

//        for (int i = 0; i < majorValues.Count - 1; i++)
//        {
//            float start = majorValues[i];
//            float end = majorValues[i + 1];
//            float step = (end - start) / MINOR_MARKS_DIVISOR;

//            for (float value = start + step; value < end; value += step)
//                minorMarks.Add(value);
//        }

//        return minorMarks;
//    }

//    private static SKRect CalculateGaugeRect(SKImageInfo info)
//    {
//        float aspectRatio = RENDERING_ASPECT_RATIO;
//        float width = info.Width / (float)info.Height > aspectRatio
//            ? info.Height * RENDERING_GAUGE_RECT_PADDING * aspectRatio
//            : info.Width * RENDERING_GAUGE_RECT_PADDING;
//        float height = width / aspectRatio;
//        float left = (info.Width - width) / 2;
//        float top = (info.Height - height) / 2;
//        return new SKRect(left, top, left + width, top + height);
//    }
//}