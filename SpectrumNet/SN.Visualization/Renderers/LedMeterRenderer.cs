#nullable enable

using System.Security.Cryptography.Xml;

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class LedMeterRenderer : EffectSpectrumRenderer<LedMeterRenderer.QualitySettings>
{
    private static readonly Lazy<LedMeterRenderer> _instance =
        new(() => new LedMeterRenderer());

    public static LedMeterRenderer GetInstance() => _instance.Value;

    private const float ANIMATION_SPEED = 0.015f,
        SMOOTHING_FACTOR_NORMAL = 0.3f,
        SMOOTHING_FACTOR_OVERLAY = 0.5f,
        PEAK_DECAY_RATE = 0.04f,
        GLOW_INTENSITY = 0.3f,
        MIN_LOUDNESS_THRESHOLD = 0.001f,
        HIGH_LOUDNESS_THRESHOLD = 0.7f,
        MEDIUM_LOUDNESS_THRESHOLD = 0.4f,
        LED_SPACING = 0.1f,
        LED_ROUNDING_RADIUS = 2.5f,
        PANEL_PADDING = 12f,
        TICK_MARK_WIDTH = 22f,
        BEVEL_SIZE = 3f,
        CORNER_RADIUS = 14f,
        VIBRATION_INTENSITY = 2f,
        VIBRATION_FREQUENCY = 8f,
        SCREW_CORNER_OFFSET = 4f,
        SCREW_SIZE = 24f,
        SCREW_HALF_SIZE = 12f,
        BRUSHED_METAL_SCALE = 1.5f,
        LED_INSET = 1f,
        LED_HIGHLIGHT_WIDTH_RATIO = 0.9f,
        LED_HIGHLIGHT_HEIGHT_RATIO = 0.4f,
        LED_HIGHLIGHT_Y_OFFSET = 0.05f,
        LED_GLOW_BLUR = 2f,
        LED_PULSE_AMPLITUDE = 0.3f,
        LED_BRIGHTNESS_VARIATION = 0.2f,
        PANEL_VIGNETTE_RADIUS_MULTIPLIER = 0.75f,
        TICK_AREA_PADDING = 2f,
        TICK_MINOR_WIDTH_RATIO = 0.6f,
        METER_TOP_OFFSET = 20f,
        METER_RIGHT_PADDING = 15f,
        METER_TICK_SPACING = 5f,
        LED_PANEL_INSET = 3f,
        LED_TOTAL_SPACE_RATIO = 0.95f,
        LED_SPACING_RATIO = 0.05f,
        RECESS_RADIUS = 6f,
        RECESS_SHADOW_HEIGHT_RATIO = 0.2f;

    private const int DEFAULT_LED_COUNT = 22,
        MIN_LED_COUNT = 10,
        LED_HEIGHT_DIVISOR = 12,
        SCREW_TEXTURE_SIZE = 24,
        BRUSHED_METAL_TEXTURE_SIZE = 100,
        BRUSHED_METAL_LINE_COUNT = 150,
        BRUSHED_METAL_DARK_LINE_COUNT = 30,
        TICK_MINOR_COUNT = 10,
        LED_VARIATION_COUNT = 30,
        COLOR_VARIATION_COUNT = 10,
        COLOR_VARIATION_RANGE = 10;

    private static readonly float[] _screwAngles = [45f, 120f, 10f, 80f];
    private static readonly string[] _dbValues = ["0", "-3", "-6", "-10", "-20", "-40"];
    private static readonly float[] _dbPositions = [1.0f, 0.85f, 0.7f, 0.55f, 0.3f, 0.0f];
    private static readonly SKColor[] _baseColors =
        [new(30, 200, 30), new(220, 200, 0), new(230, 30, 30)];

    private float _vibrationOffset;
    private float _previousLoudness;
    private float _peakLoudness;
    private float[] _ledAnimationPhases = [];
    private int _currentWidth;
    private int _currentHeight;
    private int _ledCount = DEFAULT_LED_COUNT;
    private SKBitmap? _screwBitmap;
    private SKBitmap? _staticBitmap;
    private SKBitmap? _brushedMetalBitmap;
    private readonly List<float> _ledVariations = new(LED_VARIATION_COUNT);
    private readonly List<SKColor> _ledColorVariations = new(LED_VARIATION_COUNT);
    private float _animationTime;

    public sealed class QualitySettings
    {
        public bool UseGlow { get; init; }
        public bool UseHighlights { get; init; }
        public bool UseMinorTicks { get; init; }
        public bool UseVignette { get; init; }
        public bool UseShadows { get; init; }
        public SKFilterMode FilterMode { get; init; }
    }

    protected override IReadOnlyDictionary<RenderQuality, QualitySettings>
        QualitySettingsPresets
    { get; } = new Dictionary<RenderQuality, QualitySettings>
    {
        [RenderQuality.Low] = new()
        {
            UseGlow = false,
            UseHighlights = false,
            UseMinorTicks = false,
            UseVignette = false,
            UseShadows = false,
            FilterMode = SKFilterMode.Nearest
        },
        [RenderQuality.Medium] = new()
        {
            UseGlow = true,
            UseHighlights = true,
            UseMinorTicks = true,
            UseVignette = true,
            UseShadows = true,
            FilterMode = SKFilterMode.Linear
        },
        [RenderQuality.High] = new()
        {
            UseGlow = true,
            UseHighlights = true,
            UseMinorTicks = true,
            UseVignette = true,
            UseShadows = true,
            FilterMode = SKFilterMode.Linear
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

        var meterData = CalculateMeterData(processedSpectrum, info);

        if (!ValidateMeterData(meterData))
            return;

        RenderMeterVisualization(
            canvas,
            meterData,
            renderParams,
            passedInPaint);
    }

    private MeterData CalculateMeterData(float[] spectrum, SKImageInfo info)
    {
        UpdateDimensions(info);
        UpdateLoudness(spectrum);
        UpdateAnimation();

        var dimensions = CalculateDimensions(info);

        return new MeterData(
            Dimensions: dimensions,
            Loudness: _previousLoudness,
            PeakLoudness: _peakLoudness,
            VibrationOffset: _vibrationOffset,
            LedCount: _ledCount);
    }

    private static bool ValidateMeterData(MeterData data) =>
        data.Dimensions.OuterRect.Width > 0 &&
        data.Dimensions.OuterRect.Height > 0 &&
        data.LedCount > 0;

    private void RenderMeterVisualization(
        SKCanvas canvas,
        MeterData data,
        RenderParameters renderParams,
        SKPaint basePaint)
    {
        if (data.Loudness < MIN_LOUDNESS_THRESHOLD && !IsOverlayActive)
            return;

        canvas.Save();

        if (!IsOverlayActive)
            canvas.Translate(data.VibrationOffset, 0);

        try
        {
            if (_staticBitmap != null)
                canvas.DrawBitmap(_staticBitmap, 0, 0);

            RenderWithOverlay(canvas, () =>
            {
                RenderLedSystem(canvas, data);
            });
        }
        finally
        {
            canvas.Restore();
        }
    }

    private void UpdateDimensions(SKImageInfo info)
    {
        if (_currentWidth != info.Width || _currentHeight != info.Height)
        {
            _currentWidth = info.Width;
            _currentHeight = info.Height;
            float panelHeight = info.Height - PANEL_PADDING * 2;
            _ledCount = (int)MathF.Max(MIN_LED_COUNT,
                MathF.Min(DEFAULT_LED_COUNT, (int)(panelHeight / LED_HEIGHT_DIVISOR)));
            InitializeLedAnimationPhases();
            CreateStaticBitmap(info);
        }
    }

    private void UpdateLoudness(float[] spectrum)
    {
        float loudness = CalculateAndSmoothLoudness(spectrum);

        if (loudness > _peakLoudness)
            _peakLoudness = loudness;
        else
            _peakLoudness = MathF.Max(0, _peakLoudness - PEAK_DECAY_RATE);
    }

    private void UpdateAnimation()
    {
        _animationTime += ANIMATION_SPEED;

        if (_previousLoudness > HIGH_LOUDNESS_THRESHOLD)
            _vibrationOffset = MathF.Sin(_animationTime * VIBRATION_FREQUENCY) *
                VIBRATION_INTENSITY * _previousLoudness;
        else
            _vibrationOffset = 0f;
    }

    private float CalculateAndSmoothLoudness(float[] spectrum)
    {
        float rawLoudness = CalculateLoudness(spectrum);
        float smoothedLoudness = Lerp(_previousLoudness, rawLoudness,
            IsOverlayActive ? SMOOTHING_FACTOR_OVERLAY : SMOOTHING_FACTOR_NORMAL);
        _previousLoudness = Clamp(smoothedLoudness, MIN_LOUDNESS_THRESHOLD, 1f);
        return _previousLoudness;
    }

    private static float CalculateLoudness(float[] spectrum)
    {
        if (spectrum.Length == 0) return 0f;

        float sum = 0f;
        for (int i = 0; i < spectrum.Length; i++)
            sum += MathF.Abs(spectrum[i]);

        return Clamp(sum / spectrum.Length, 0f, 1f);
    }

    private void InitializeLedAnimationPhases()
    {
        _ledAnimationPhases = new float[_ledCount];
        var phaseRandom = new Random(42);

        for (int i = 0; i < _ledCount; i++)
            _ledAnimationPhases[i] = (float)phaseRandom.NextDouble();
    }

    private static MeterDimensions CalculateDimensions(SKImageInfo info)
    {
        float outerPadding = 5f;
        float panelLeft = PANEL_PADDING;
        float panelTop = PANEL_PADDING;
        float panelWidth = info.Width - PANEL_PADDING * 2;
        float panelHeight = info.Height - PANEL_PADDING * 2;

        var outerRect = new SKRect(
            outerPadding,
            outerPadding,
            info.Width - outerPadding,
            info.Height - outerPadding);

        var panelRect = new SKRect(
            panelLeft,
            panelTop,
            panelLeft + panelWidth,
            panelTop + panelHeight);

        float meterLeft = panelLeft + TICK_MARK_WIDTH + METER_TICK_SPACING;
        float meterTop = panelTop + METER_TOP_OFFSET;
        float meterWidth = panelWidth - (TICK_MARK_WIDTH + METER_RIGHT_PADDING);
        float meterHeight = panelHeight - 25;

        var meterRect = new SKRect(
            meterLeft,
            meterTop,
            meterLeft + meterWidth,
            meterTop + meterHeight);

        var ledPanelRect = new SKRect(
            meterLeft - LED_PANEL_INSET,
            meterTop - LED_PANEL_INSET,
            meterLeft + meterWidth + LED_PANEL_INSET * 2,
            meterTop + meterHeight + LED_PANEL_INSET * 2);

        return new MeterDimensions(outerRect, panelRect, meterRect, ledPanelRect);
    }

    private void CreateStaticBitmap(SKImageInfo info)
    {
        _staticBitmap?.Dispose();
        _staticBitmap = new SKBitmap(info.Width, info.Height);

        using var canvas = new SKCanvas(_staticBitmap);
        var dimensions = CalculateDimensions(info);

        RenderOuterCase(canvas, dimensions.OuterRect);
        RenderPanel(canvas, dimensions.PanelRect);
        RenderLabels(canvas, dimensions.PanelRect);
        RenderRecessedLedPanel(canvas, dimensions.LedPanelRect);
        RenderTickMarks(canvas, dimensions.PanelRect, dimensions.MeterRect);
        RenderScrews(canvas, dimensions.PanelRect);
    }

    private void RenderOuterCase(SKCanvas canvas, SKRect rect)
    {
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(0, rect.Height),
            [new SKColor(70, 70, 70), new SKColor(40, 40, 40), new SKColor(55, 55, 55)],
            [0.0f, 0.7f, 1.0f],
            SKShaderTileMode.Clamp);

        var paint = CreatePaint(SKColors.Black, SKPaintStyle.Fill, shader);

        try
        {
            canvas.DrawRoundRect(rect, CORNER_RADIUS, CORNER_RADIUS, paint);
        }
        finally
        {
            ReturnPaint(paint);
        }

        var highlightPaint = CreatePaint(new SKColor(255, 255, 255, 40), SKPaintStyle.Stroke);
        highlightPaint.StrokeWidth = 1.2f;

        try
        {
            canvas.DrawLine(
                rect.Left + CORNER_RADIUS, rect.Top + 1.5f,
                rect.Right - CORNER_RADIUS, rect.Top + 1.5f,
                highlightPaint);
        }
        finally
        {
            ReturnPaint(highlightPaint);
        }
    }

    private void RenderPanel(SKCanvas canvas, SKRect rect)
    {
        using var roundRect = new SKRoundRect(rect, CORNER_RADIUS - 4, CORNER_RADIUS - 4);

        if (_brushedMetalBitmap != null)
        {
            using var shader = SKShader.CreateBitmap(
                _brushedMetalBitmap,
                SKShaderTileMode.Repeat,
                SKShaderTileMode.Repeat,
                SKMatrix.CreateScale(BRUSHED_METAL_SCALE, BRUSHED_METAL_SCALE));

            var paint = CreatePaint(SKColors.White, SKPaintStyle.Fill, shader);

            try
            {
                canvas.DrawRoundRect(roundRect, paint);
            }
            finally
            {
                ReturnPaint(paint);
            }
        }

        RenderPanelBevel(canvas, roundRect);

        if (UseAdvancedEffects && CurrentQualitySettings!.UseVignette)
        {
            using var vignetteShader = SKShader.CreateRadialGradient(
                new SKPoint(rect.MidX, rect.MidY),
                MathF.Max(rect.Width, rect.Height) * PANEL_VIGNETTE_RADIUS_MULTIPLIER,
                [new SKColor(0, 0, 0, 0), new SKColor(0, 0, 0, 30)],
                CreateUniformGradientPositions(2),
                SKShaderTileMode.Clamp);

            var vignettePaint = CreatePaint(SKColors.Black, SKPaintStyle.Fill, vignetteShader);

            try
            {
                canvas.DrawRoundRect(roundRect, vignettePaint);
            }
            finally
            {
                ReturnPaint(vignettePaint);
            }
        }
    }

    private void RenderPanelBevel(SKCanvas canvas, SKRoundRect roundRect)
    {
        var highlightPaint = CreatePaint(new SKColor(255, 255, 255, 120), SKPaintStyle.Stroke);
        highlightPaint.StrokeWidth = BEVEL_SIZE;

        try
        {
            RenderPath(canvas, path =>
            {
                CreateBevelHighlightPath(path, roundRect);
            }, highlightPaint);
        }
        finally
        {
            ReturnPaint(highlightPaint);
        }

        var shadowPaint = CreatePaint(new SKColor(0, 0, 0, 90), SKPaintStyle.Stroke);
        shadowPaint.StrokeWidth = BEVEL_SIZE;

        try
        {
            RenderPath(canvas, path =>
            {
                CreateBevelShadowPath(path, roundRect);
            }, shadowPaint);
        }
        finally
        {
            ReturnPaint(shadowPaint);
        }
    }

    private static void CreateBevelHighlightPath(SKPath path, SKRoundRect roundRect)
    {
        float radOffset = BEVEL_SIZE / 2;

        path.MoveTo(
            roundRect.Rect.Left + CORNER_RADIUS,
            roundRect.Rect.Bottom - radOffset);

        path.ArcTo(new SKRect(
            roundRect.Rect.Left + radOffset,
            roundRect.Rect.Bottom - CORNER_RADIUS * 2 + radOffset,
            roundRect.Rect.Left + CORNER_RADIUS * 2 - radOffset,
            roundRect.Rect.Bottom),
            90, 90, false);

        path.LineTo(
            roundRect.Rect.Left + radOffset,
            roundRect.Rect.Top + CORNER_RADIUS);

        path.ArcTo(new SKRect(
            roundRect.Rect.Left + radOffset,
            roundRect.Rect.Top + radOffset,
            roundRect.Rect.Left + CORNER_RADIUS * 2 - radOffset,
            roundRect.Rect.Top + CORNER_RADIUS * 2 - radOffset),
            180, 90, false);

        path.LineTo(
            roundRect.Rect.Right - CORNER_RADIUS,
            roundRect.Rect.Top + radOffset);
    }

    private static void CreateBevelShadowPath(SKPath path, SKRoundRect roundRect)
    {
        float radOffset = BEVEL_SIZE / 2;

        path.MoveTo(
            roundRect.Rect.Right - CORNER_RADIUS,
            roundRect.Rect.Top + radOffset);

        path.ArcTo(new SKRect(
            roundRect.Rect.Right - CORNER_RADIUS * 2 + radOffset,
            roundRect.Rect.Top + radOffset,
            roundRect.Rect.Right - radOffset,
            roundRect.Rect.Top + CORNER_RADIUS * 2 - radOffset),
            270, 90, false);

        path.LineTo(
            roundRect.Rect.Right - radOffset,
            roundRect.Rect.Bottom - CORNER_RADIUS);

        path.ArcTo(new SKRect(
            roundRect.Rect.Right - CORNER_RADIUS * 2 + radOffset,
            roundRect.Rect.Bottom - CORNER_RADIUS * 2 + radOffset,
            roundRect.Rect.Right - radOffset,
            roundRect.Rect.Bottom - radOffset),
            0, 90, false);

        path.LineTo(
            roundRect.Rect.Left + CORNER_RADIUS,
            roundRect.Rect.Bottom - radOffset);
    }

    private void RenderRecessedLedPanel(SKCanvas canvas, SKRect rect)
    {
        using var recessRoundRect = new SKRoundRect(rect, RECESS_RADIUS, RECESS_RADIUS);

        var backgroundPaint = CreatePaint(new SKColor(12, 12, 12), SKPaintStyle.Fill);

        try
        {
            canvas.DrawRoundRect(recessRoundRect, backgroundPaint);
        }
        finally
        {
            ReturnPaint(backgroundPaint);
        }

        if (UseAdvancedEffects && CurrentQualitySettings!.UseShadows)
        {
            using var shadowShader = SKShader.CreateLinearGradient(
                new SKPoint(rect.Left, rect.Top),
                new SKPoint(rect.Left, rect.Top + rect.Height * RECESS_SHADOW_HEIGHT_RATIO),
                [new SKColor(0, 0, 0, 120), new SKColor(0, 0, 0, 0)],
                CreateUniformGradientPositions(2),
                SKShaderTileMode.Clamp);

            var shadowPaint = CreatePaint(SKColors.Black, SKPaintStyle.Fill, shadowShader);

            try
            {
                canvas.DrawRoundRect(recessRoundRect, shadowPaint);
            }
            finally
            {
                ReturnPaint(shadowPaint);
            }
        }

        var borderPaint = CreatePaint(new SKColor(0, 0, 0, 180), SKPaintStyle.Stroke);
        borderPaint.StrokeWidth = 1;

        try
        {
            canvas.DrawRoundRect(recessRoundRect, borderPaint);
        }
        finally
        {
            ReturnPaint(borderPaint);
        }
    }

    private void RenderLabels(SKCanvas canvas, SKRect panelRect)
    {
        using var boldTypeface = SKTypeface.FromFamilyName(
            "Arial",
            SKFontStyleWeight.Bold,
            SKFontStyleWidth.Normal,
            SKFontStyleSlant.Upright);

        using var font14 = new SKFont(boldTypeface, 14);
        using var font10 = new SKFont(boldTypeface, 10);
        using var font8 = new SKFont(boldTypeface, 8);

        float labelX = panelRect.Left + 10;
        float labelY = panelRect.Top + 14;

        var shadowPaint = CreatePaint(new SKColor(30, 30, 30, 180), SKPaintStyle.Fill);

        try
        {
            canvas.DrawText("VU", labelX + 1, labelY + 1, SKTextAlign.Left, font14, shadowPaint);
        }
        finally
        {
            ReturnPaint(shadowPaint);
        }

        var mainPaint = CreatePaint(new SKColor(230, 230, 230, 200), SKPaintStyle.Fill);

        try
        {
            canvas.DrawText("VU", labelX, labelY, SKTextAlign.Left, font14, mainPaint);
        }
        finally
        {
            ReturnPaint(mainPaint);
        }

        var secondaryPaint = CreatePaint(new SKColor(200, 200, 200, 150), SKPaintStyle.Fill);

        try
        {
            canvas.DrawText("METER", labelX + 30, labelY, SKTextAlign.Left, font10, secondaryPaint);
        }
        finally
        {
            ReturnPaint(secondaryPaint);
        }

        var tertiaryPaint = CreatePaint(new SKColor(200, 200, 200, 120), SKPaintStyle.Fill);

        try
        {
            canvas.DrawText(
                "PRO SERIES",
                panelRect.Right - 10,
                panelRect.Top + 14,
                SKTextAlign.Right,
                font8,
                tertiaryPaint);

            canvas.DrawText(
                "dB",
                panelRect.Left + 10,
                panelRect.Bottom - 10,
                SKTextAlign.Left,
                font8,
                tertiaryPaint);
        }
        finally
        {
            ReturnPaint(tertiaryPaint);
        }
    }

    private void RenderTickMarks(
        SKCanvas canvas,
        SKRect panelRect,
        SKRect meterRect)
    {
        var tickAreaRect = new SKRect(
            panelRect.Left,
            meterRect.Top,
            panelRect.Left + TICK_MARK_WIDTH - TICK_AREA_PADDING,
            meterRect.Bottom);

        var tickAreaPaint = CreatePaint(new SKColor(30, 30, 30, 70), SKPaintStyle.Fill);

        try
        {
            canvas.DrawRect(tickAreaRect, tickAreaPaint);
        }
        finally
        {
            ReturnPaint(tickAreaPaint);
        }

        var tickPaint = CreatePaint(SKColors.LightGray.WithAlpha(150), SKPaintStyle.Stroke);
        tickPaint.StrokeWidth = 1;

        using var font = new SKFont(
            SKTypeface.FromFamilyName(
                "Arial",
                SKFontStyleWeight.Normal,
                SKFontStyleWidth.Normal,
                SKFontStyleSlant.Upright),
            9);

        try
        {
            RenderMajorTicks(canvas, meterRect, tickPaint, font);

            if (UseAdvancedEffects && CurrentQualitySettings!.UseMinorTicks)
                RenderMinorTicks(canvas, meterRect, tickPaint);
        }
        finally
        {
            ReturnPaint(tickPaint);
        }
    }

    private void RenderMajorTicks(
        SKCanvas canvas,
        SKRect meterRect,
        SKPaint tickPaint,
        SKFont font)
    {
        float x = meterRect.Left - TICK_MARK_WIDTH;
        float width = TICK_MARK_WIDTH;
        float height = meterRect.Height;
        float y = meterRect.Top;

        for (int i = 0; i < _dbValues.Length; i++)
        {
            float yPos = y + height - _dbPositions[i] * height;
            canvas.DrawLine(x, yPos, x + width - 5, yPos, tickPaint);

            if (UseAdvancedEffects && CurrentQualitySettings!.UseShadows)
            {
                var shadowPaint = CreatePaint(SKColors.Black.WithAlpha(80), SKPaintStyle.Fill);

                try
                {
                    canvas.DrawText(
                        _dbValues[i],
                        x + width - 7,
                        yPos + 3.5f,
                        SKTextAlign.Right,
                        font,
                        shadowPaint);
                }
                finally
                {
                    ReturnPaint(shadowPaint);
                }
            }

            var textPaint = CreatePaint(SKColors.LightGray.WithAlpha(180), SKPaintStyle.Fill);

            try
            {
                canvas.DrawText(
                    _dbValues[i],
                    x + width - 8,
                    yPos + 3,
                    SKTextAlign.Right,
                    font,
                    textPaint);
            }
            finally
            {
                ReturnPaint(textPaint);
            }
        }
    }

    private void RenderMinorTicks(
        SKCanvas canvas,
        SKRect meterRect,
        SKPaint tickPaint)
    {
        float x = meterRect.Left - TICK_MARK_WIDTH;
        float width = TICK_MARK_WIDTH;
        float height = meterRect.Height;
        float y = meterRect.Top;

        tickPaint.Color = SKColors.LightGray.WithAlpha(80);

        for (int i = 0; i < TICK_MINOR_COUNT; i++)
        {
            float ratio = i / (float)TICK_MINOR_COUNT;
            float yPos = y + ratio * height;
            canvas.DrawLine(x, yPos, x + width * TICK_MINOR_WIDTH_RATIO, yPos, tickPaint);
        }
    }

    private void RenderScrews(SKCanvas canvas, SKRect panelRect)
    {
        if (_screwBitmap == null) return;

        float cornerOffset = CORNER_RADIUS - SCREW_CORNER_OFFSET;

        DrawScrew(canvas, panelRect.Left + cornerOffset, panelRect.Top + cornerOffset, _screwAngles[0]);
        DrawScrew(canvas, panelRect.Right - cornerOffset, panelRect.Top + cornerOffset, _screwAngles[1]);
        DrawScrew(canvas, panelRect.Left + cornerOffset, panelRect.Bottom - cornerOffset, _screwAngles[2]);
        DrawScrew(canvas, panelRect.Right - cornerOffset, panelRect.Bottom - cornerOffset, _screwAngles[3]);

        RenderBrandingText(canvas, panelRect);
    }

    private void DrawScrew(SKCanvas canvas, float x, float y, float angle)
    {
        if (_screwBitmap == null) return;

        canvas.Save();
        try
        {
            canvas.Translate(x, y);
            canvas.RotateDegrees(angle);
            canvas.Translate(-SCREW_HALF_SIZE, -SCREW_HALF_SIZE);
            canvas.DrawBitmap(_screwBitmap, 0, 0);
        }
        finally
        {
            canvas.Restore();
        }
    }

    private void RenderBrandingText(SKCanvas canvas, SKRect panelRect)
    {
        using var font = new SKFont(
            SKTypeface.FromFamilyName(
                "Arial",
                SKFontStyleWeight.Normal,
                SKFontStyleWidth.Normal,
                SKFontStyleSlant.Upright),
            8);

        var paint = CreatePaint(new SKColor(230, 230, 230, 120), SKPaintStyle.Fill);

        try
        {
            canvas.DrawText(
                "SpectrumNet™ Audio",
                panelRect.Right - 65,
                panelRect.Bottom - 8,
                SKTextAlign.Right,
                font,
                paint);
        }
        finally
        {
            ReturnPaint(paint);
        }
    }

    private void RenderLedSystem(SKCanvas canvas, MeterData data)
    {
        int activeLedCount = (int)(data.Loudness * data.LedCount);
        int peakLedIndex = (int)(data.PeakLoudness * data.LedCount);

        var ledDimensions = CalculateLedDimensions(data.Dimensions.MeterRect, data.LedCount);

        for (int i = 0; i < data.LedCount; i++)
        {
            var ledInfo = CalculateLedInfo(i, data.Dimensions.MeterRect, ledDimensions, data.LedCount);
            var color = GetLedColorForPosition(ledInfo.NormalizedPosition, i);

            if (i < activeLedCount || i == peakLedIndex)
            {
                bool isPeak = i == peakLedIndex;
                UpdateLedAnimation(i, ledInfo.NormalizedPosition);
                RenderActiveLed(canvas, ledInfo, color, isPeak, i);
            }
            else
            {
                RenderInactiveLed(canvas, ledInfo, color);
            }
        }
    }

    private static LedDimensions CalculateLedDimensions(SKRect meterRect, int ledCount)
    {
        float totalLedSpace = meterRect.Height * LED_TOTAL_SPACE_RATIO;
        float totalSpacingSpace = meterRect.Height * LED_SPACING_RATIO;
        float ledHeight = (totalLedSpace - totalSpacingSpace) / ledCount;
        float spacing = ledCount > 1 ? totalSpacingSpace / (ledCount - 1) : 0;
        float ledWidth = meterRect.Width;

        return new LedDimensions(ledHeight, spacing, ledWidth);
    }

    private static LedInfo CalculateLedInfo(
        int index,
        SKRect meterRect,
        LedDimensions ledDimensions,
        int ledCount)
    {
        float normalizedPosition = (float)index / ledCount;
        float ledY = meterRect.Top + (ledCount - index - 1) *
            (ledDimensions.Height + ledDimensions.Spacing);

        return new LedInfo(
            meterRect.Left,
            ledY,
            ledDimensions.Width,
            ledDimensions.Height,
            normalizedPosition);
    }

    private void UpdateLedAnimation(int index, float normalizedPosition)
    {
        _ledAnimationPhases[index] = (_ledAnimationPhases[index] +
            ANIMATION_SPEED * (0.5f + normalizedPosition)) % 1.0f;
    }

    private void RenderInactiveLed(
        SKCanvas canvas,
        LedInfo ledInfo,
        SKColor color)
    {
        using var ledRect = new SKRoundRect(
            new SKRect(ledInfo.X, ledInfo.Y, ledInfo.X + ledInfo.Width, ledInfo.Y + ledInfo.Height),
            LED_ROUNDING_RADIUS,
            LED_ROUNDING_RADIUS);

        var basePaint = CreatePaint(new SKColor(8, 8, 8), SKPaintStyle.Fill);

        try
        {
            canvas.DrawRoundRect(ledRect, basePaint);
        }
        finally
        {
            ReturnPaint(basePaint);
        }

        using var surfaceRect = new SKRoundRect(
            new SKRect(
                ledInfo.X + LED_INSET,
                ledInfo.Y + LED_INSET,
                ledInfo.X + ledInfo.Width - LED_INSET,
                ledInfo.Y + ledInfo.Height - LED_INSET),
            MathF.Max(1, LED_ROUNDING_RADIUS - LED_INSET * 0.5f),
            MathF.Max(1, LED_ROUNDING_RADIUS - LED_INSET * 0.5f));

        var surfacePaint = CreatePaint(MultiplyColor(color, 0.10f), SKPaintStyle.Fill);

        try
        {
            canvas.DrawRoundRect(surfaceRect, surfacePaint);
        }
        finally
        {
            ReturnPaint(surfacePaint);
        }
    }

    private void RenderActiveLed(
        SKCanvas canvas,
        LedInfo ledInfo,
        SKColor color,
        bool isPeak,
        int index)
    {
        using var ledRect = new SKRoundRect(
            new SKRect(ledInfo.X, ledInfo.Y, ledInfo.X + ledInfo.Width, ledInfo.Y + ledInfo.Height),
            LED_ROUNDING_RADIUS,
            LED_ROUNDING_RADIUS);

        var basePaint = CreatePaint(new SKColor(8, 8, 8), SKPaintStyle.Fill);

        try
        {
            canvas.DrawRoundRect(ledRect, basePaint);
        }
        finally
        {
            ReturnPaint(basePaint);
        }

        float brightnessVariation = _ledVariations[index % _ledVariations.Count];
        float animPhase = _ledAnimationPhases[index % _ledAnimationPhases.Length];
        float pulse = isPeak ?
            0.7f + MathF.Sin(animPhase * MathF.PI * 2) * LED_PULSE_AMPLITUDE :
            brightnessVariation;

        var ledOnColor = MultiplyColor(color, pulse);

        if (UseAdvancedEffects && CurrentQualitySettings!.UseGlow && index <= _ledCount * 0.7f)
        {
            float glowIntensity = GLOW_INTENSITY *
                (0.8f + MathF.Sin(animPhase * MathF.PI * 2) * LED_BRIGHTNESS_VARIATION * brightnessVariation);

            using var blurFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, LED_GLOW_BLUR);

            var glowPaint = CreatePaint(
                ledOnColor.WithAlpha((byte)(glowIntensity * 160 * brightnessVariation)),
                SKPaintStyle.Fill);
            glowPaint.MaskFilter = blurFilter;

            try
            {
                canvas.DrawRoundRect(ledRect, glowPaint);
            }
            finally
            {
                ReturnPaint(glowPaint);
            }
        }

        using var ledSurfaceRect = new SKRoundRect(
            new SKRect(
                ledInfo.X + LED_INSET,
                ledInfo.Y + LED_INSET,
                ledInfo.X + ledInfo.Width - LED_INSET,
                ledInfo.Y + ledInfo.Height - LED_INSET),
            MathF.Max(1, LED_ROUNDING_RADIUS - LED_INSET * 0.5f),
            MathF.Max(1, LED_ROUNDING_RADIUS - LED_INSET * 0.5f));

        using var ledShader = SKShader.CreateLinearGradient(
            new SKPoint(ledInfo.X, ledInfo.Y),
            new SKPoint(ledInfo.X, ledInfo.Y + ledInfo.Height),
            [ledOnColor, MultiplyColor(ledOnColor, 0.9f), new(10, 10, 10, 220)],
            [0.0f, 0.7f, 1.0f],
            SKShaderTileMode.Clamp);

        var ledPaint = CreatePaint(ledOnColor, SKPaintStyle.Fill, ledShader);

        try
        {
            canvas.DrawRoundRect(ledSurfaceRect, ledPaint);
        }
        finally
        {
            ReturnPaint(ledPaint);
        }

        if (UseAdvancedEffects && CurrentQualitySettings!.UseHighlights)
            RenderLedHighlight(canvas, ledInfo);
    }

    private void RenderLedHighlight(SKCanvas canvas, LedInfo ledInfo)
    {
        float arcWidth = ledInfo.Width * LED_HIGHLIGHT_WIDTH_RATIO;
        float arcHeight = ledInfo.Height * LED_HIGHLIGHT_HEIGHT_RATIO;
        float arcX = ledInfo.X + (ledInfo.Width - arcWidth) / 2;
        float arcY = ledInfo.Y + ledInfo.Height * LED_HIGHLIGHT_Y_OFFSET;

        using var highlightRect = new SKRoundRect(
            new SKRect(arcX, arcY, arcX + arcWidth, arcY + arcHeight),
            LED_ROUNDING_RADIUS,
            LED_ROUNDING_RADIUS);

        var fillPaint = CreatePaint(new SKColor(255, 255, 255, 50), SKPaintStyle.Fill);

        try
        {
            canvas.DrawRoundRect(highlightRect, fillPaint);
        }
        finally
        {
            ReturnPaint(fillPaint);
        }

        var strokePaint = CreatePaint(new SKColor(255, 255, 255, 180), SKPaintStyle.Stroke);
        strokePaint.StrokeWidth = 0.7f;

        try
        {
            canvas.DrawRoundRect(highlightRect, strokePaint);
        }
        finally
        {
            ReturnPaint(strokePaint);
        }
    }

    private SKColor GetLedColorForPosition(float normalizedPosition, int index)
    {
        int colorGroup;
        if (normalizedPosition >= HIGH_LOUDNESS_THRESHOLD)
            colorGroup = 2;
        else if (normalizedPosition >= MEDIUM_LOUDNESS_THRESHOLD)
            colorGroup = 1;
        else
            colorGroup = 0;

        int variationIndex = index % COLOR_VARIATION_COUNT;
        int colorIndex = colorGroup * COLOR_VARIATION_COUNT + variationIndex;

        if (colorIndex < _ledColorVariations.Count)
            return _ledColorVariations[colorIndex];

        return colorGroup switch
        {
            2 => new SKColor(220, 30, 30),
            1 => new SKColor(230, 200, 0),
            _ => new SKColor(40, 200, 40)
        };
    }

    private static SKColor MultiplyColor(SKColor color, float factor)
    {
        return new SKColor(
            (byte)Clamp(color.Red * factor, 0, 255),
            (byte)Clamp(color.Green * factor, 0, 255),
            (byte)Clamp(color.Blue * factor, 0, 255),
            color.Alpha);
    }

    private void InitializeVariations()
    {
        var fixedRandom = new Random(42);
        _ledVariations.Clear();

        for (int i = 0; i < LED_VARIATION_COUNT; i++)
            _ledVariations.Add(0.85f + (float)fixedRandom.NextDouble() * 0.3f);

        _ledColorVariations.Clear();

        foreach (var baseColor in _baseColors)
        {
            for (int j = 0; j < COLOR_VARIATION_COUNT; j++)
            {
                _ledColorVariations.Add(new SKColor(
                    (byte)Clamp(baseColor.Red + fixedRandom.Next(-COLOR_VARIATION_RANGE, COLOR_VARIATION_RANGE), 0, 255),
                    (byte)Clamp(baseColor.Green + fixedRandom.Next(-COLOR_VARIATION_RANGE, COLOR_VARIATION_RANGE), 0, 255),
                    (byte)Clamp(baseColor.Blue + fixedRandom.Next(-COLOR_VARIATION_RANGE, COLOR_VARIATION_RANGE), 0, 255)
                ));
            }
        }
    }

    private void CreateCachedTextures()
    {
        _screwBitmap?.Dispose();
        _brushedMetalBitmap?.Dispose();

        _screwBitmap = CreateScrewTexture();
        _brushedMetalBitmap = CreateBrushedMetalTexture();
    }

    private SKBitmap CreateScrewTexture()
    {
        var bitmap = new SKBitmap(SCREW_TEXTURE_SIZE, SCREW_TEXTURE_SIZE);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(4, 4),
            new SKPoint(20, 20),
            [new SKColor(220, 220, 220), new SKColor(140, 140, 140)],
            [0.0f, 1.0f],
            SKShaderTileMode.Clamp);

        var circlePaint = CreatePaint(SKColors.White, SKPaintStyle.Fill, shader);

        try
        {
            canvas.DrawCircle(12, 12, 10, circlePaint);
        }
        finally
        {
            ReturnPaint(circlePaint);
        }

        var slotPaint = CreatePaint(new SKColor(50, 50, 50, 180), SKPaintStyle.Stroke);
        slotPaint.StrokeWidth = 2.5f;

        try
        {
            canvas.DrawLine(7, 12, 17, 12, slotPaint);
        }
        finally
        {
            ReturnPaint(slotPaint);
        }

        var highlightPaint = CreatePaint(new SKColor(255, 255, 255, 100), SKPaintStyle.Stroke);
        highlightPaint.StrokeWidth = 1;

        try
        {
            canvas.DrawArc(new SKRect(4, 4, 20, 20), 200, 160, false, highlightPaint);
        }
        finally
        {
            ReturnPaint(highlightPaint);
        }

        var shadowPaint = CreatePaint(new SKColor(0, 0, 0, 100), SKPaintStyle.Stroke);
        shadowPaint.StrokeWidth = 1.5f;

        try
        {
            canvas.DrawCircle(12, 12, 9, shadowPaint);
        }
        finally
        {
            ReturnPaint(shadowPaint);
        }

        return bitmap;
    }

    private SKBitmap CreateBrushedMetalTexture()
    {
        var bitmap = new SKBitmap(BRUSHED_METAL_TEXTURE_SIZE, BRUSHED_METAL_TEXTURE_SIZE);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(190, 190, 190));

        var texRandom = new Random(42);

        var linePaint = CreatePaint(SKColors.White, SKPaintStyle.Stroke);
        linePaint.StrokeWidth = 1;

        try
        {
            for (int i = 0; i < BRUSHED_METAL_LINE_COUNT; i++)
            {
                float y = (float)texRandom.NextDouble() * BRUSHED_METAL_TEXTURE_SIZE;
                linePaint.Color = new SKColor(210, 210, 210).WithAlpha((byte)texRandom.Next(10, 20));
                canvas.DrawLine(0, y, BRUSHED_METAL_TEXTURE_SIZE, y, linePaint);
            }

            for (int i = 0; i < BRUSHED_METAL_DARK_LINE_COUNT; i++)
            {
                float y = (float)texRandom.NextDouble() * BRUSHED_METAL_TEXTURE_SIZE;
                linePaint.Color = new SKColor(100, 100, 100).WithAlpha((byte)texRandom.Next(5, 10));
                canvas.DrawLine(0, y, BRUSHED_METAL_TEXTURE_SIZE, y, linePaint);
            }
        }
        finally
        {
            ReturnPaint(linePaint);
        }

        using var gradientShader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(BRUSHED_METAL_TEXTURE_SIZE, BRUSHED_METAL_TEXTURE_SIZE),
            [new SKColor(255, 255, 255, 20), new SKColor(0, 0, 0, 20)],
            [0.0f, 1.0f],
            SKShaderTileMode.Clamp);

        var gradientPaint = CreatePaint(SKColors.White, SKPaintStyle.Fill, gradientShader);

        try
        {
            canvas.DrawRect(
                0, 0,
                BRUSHED_METAL_TEXTURE_SIZE,
                BRUSHED_METAL_TEXTURE_SIZE,
                gradientPaint);
        }
        finally
        {
            ReturnPaint(gradientPaint);
        }

        return bitmap;
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

        InitializeVariations();
        CreateCachedTextures();

        if (_currentWidth > 0 && _currentHeight > 0)
            CreateStaticBitmap(new SKImageInfo(_currentWidth, _currentHeight));

        RequestRedraw();
    }

    protected override void OnDispose()
    {
        _screwBitmap?.Dispose();
        _screwBitmap = null;

        _brushedMetalBitmap?.Dispose();
        _brushedMetalBitmap = null;

        _staticBitmap?.Dispose();
        _staticBitmap = null;

        _ledColorVariations.Clear();
        _ledVariations.Clear();

        _vibrationOffset = 0f;
        _previousLoudness = 0f;
        _peakLoudness = 0f;
        _currentWidth = 0;
        _currentHeight = 0;
        _ledCount = DEFAULT_LED_COUNT;
        _animationTime = 0f;

        base.OnDispose();
    }

    private record MeterDimensions(
        SKRect OuterRect,
        SKRect PanelRect,
        SKRect MeterRect,
        SKRect LedPanelRect);

    private record MeterData(
        MeterDimensions Dimensions,
        float Loudness,
        float PeakLoudness,
        float VibrationOffset,
        int LedCount);

    private record LedDimensions(
        float Height,
        float Spacing,
        float Width);

    private record LedInfo(
        float X,
        float Y,
        float Width,
        float Height,
        float NormalizedPosition);
}