#nullable enable

using static System.MathF;
using static SpectrumNet.SN.Visualization.Renderers.LedMeterRenderer.Constants;

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class LedMeterRenderer : EffectSpectrumRenderer
{
    private const string LogPrefix = nameof(LedMeterRenderer);

    private static readonly Lazy<LedMeterRenderer> _instance =
        new(() => new LedMeterRenderer());

    private LedMeterRenderer() { }

    public static LedMeterRenderer GetInstance() => _instance.Value;

    public static class Constants
    {
        public const float
            ANIMATION_SPEED = 0.015f,
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
            CORNER_RADIUS = 14f;

        public const int
            DEFAULT_LED_COUNT = 22,
            PERFORMANCE_INFO_BOTTOM_MARGIN = 30,
            SCREW_TEXTURE_SIZE = 24,
            BRUSHED_METAL_TEXTURE_SIZE = 100;

        public static readonly Dictionary<RenderQuality, QualitySettings> QualityPresets = new()
        {
            [RenderQuality.Low] = new(
                UseAdvancedEffects: false,
                UseAntialiasing: false,
                FilterMode: SKFilterMode.Nearest,
                MipmapMode: SKMipmapMode.None
            ),
            [RenderQuality.Medium] = new(
                UseAdvancedEffects: true,
                UseAntialiasing: true,
                FilterMode: SKFilterMode.Linear,
                MipmapMode: SKMipmapMode.Linear
            ),
            [RenderQuality.High] = new(
                UseAdvancedEffects: true,
                UseAntialiasing: true,
                FilterMode: SKFilterMode.Linear,
                MipmapMode: SKMipmapMode.Linear
            )
        };

        public record QualitySettings(
            bool UseAdvancedEffects,
            bool UseAntialiasing,
            SKFilterMode FilterMode,
            SKMipmapMode MipmapMode
        );
    }

    private float _animationPhase, _vibrationOffset, _previousLoudness, _peakLoudness;
    private float? _cachedLoudness;
    private float[] _ledAnimationPhases = [];
    private int _currentWidth, _currentHeight, _ledCount = DEFAULT_LED_COUNT;
    private SKBitmap? _screwBitmap, _staticBitmap, _brushedMetalBitmap;
    private readonly List<float> _ledVariations = new(30);
    private readonly List<SKColor> _ledColorVariations = new(30);
    private readonly object _loudnessLock = new();
    private readonly float[] _screwAngles = [45f, 120f, 10f, 80f];
    private QualitySettings _currentSettings = QualityPresets[RenderQuality.Medium];

    protected override void OnInitialize()
    {
        base.OnInitialize();
        InitializeVariations();
        CreateCachedTextures();
        ResetState();
        _logger.Log(LogLevel.Debug, LogPrefix, "Initialized");
    }

    protected override void OnQualitySettingsApplied()
    {
        _currentSettings = QualityPresets[Quality];
        _useAntiAlias = _currentSettings.UseAntialiasing;
        _useAdvancedEffects = _currentSettings.UseAdvancedEffects;
        _samplingOptions = new SKSamplingOptions(
            _currentSettings.FilterMode,
            _currentSettings.MipmapMode);

        if (_currentWidth > 0 && _currentHeight > 0)
        {
            CreateStaticBitmap(new SKImageInfo(_currentWidth, _currentHeight));
        }
        _logger.Log(LogLevel.Debug, LogPrefix, $"Quality changed to {Quality}");
    }

    protected override void OnConfigurationChanged()
    {
        _smoothingFactor = _isOverlayActive ?
            SMOOTHING_FACTOR_OVERLAY :
            SMOOTHING_FACTOR_NORMAL;
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
        _logger.Safe(
            () => RenderLedMeter(canvas, spectrum, info),
            LogPrefix,
            "Error during rendering"
        );
    }

    private void RenderLedMeter(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info)
    {
        UpdateDimensions(info);
        UpdateLoudness(spectrum);
        UpdateAnimation();

        RenderWithOverlay(canvas, () => ExecuteRendering(canvas, info));

        if (_overlayStateChanged)
        {
            _overlayStateChanged = false;
        }
    }

    private void UpdateDimensions(SKImageInfo info)
    {
        if (_currentWidth != info.Width || _currentHeight != info.Height)
        {
            _currentWidth = info.Width;
            _currentHeight = info.Height;
            float panelHeight = info.Height - PANEL_PADDING * 2;
            _ledCount = (int)MathF.Max(10, MathF.Min(DEFAULT_LED_COUNT, (int)(panelHeight / 12)));
            InitializeLedAnimationPhases();
            CreateStaticBitmap(info);
        }
    }

    private void UpdateLoudness(float[] spectrum)
    {
        float loudness = CalculateAndSmoothLoudness(spectrum);
        lock (_loudnessLock)
        {
            _cachedLoudness = loudness;
        }

        if (loudness > _peakLoudness)
        {
            _peakLoudness = loudness;
        }
        else
        {
            _peakLoudness = MathF.Max(0, _peakLoudness - PEAK_DECAY_RATE);
        }
    }

    private void UpdateAnimation()
    {
        _animationPhase = (_animationPhase + ANIMATION_SPEED) % 1.0f;

        float loudness = GetCurrentLoudness();
        if (loudness > HIGH_LOUDNESS_THRESHOLD)
        {
            float vibrationIntensity = (loudness - HIGH_LOUDNESS_THRESHOLD) /
                                     (1 - HIGH_LOUDNESS_THRESHOLD);
            _vibrationOffset = Sin(_animationPhase * MathF.PI * 10) * 0.8f * vibrationIntensity;
        }
        else
        {
            _vibrationOffset = 0;
        }
    }

    private void ExecuteRendering(SKCanvas canvas, SKImageInfo info)
    {
        float loudness = GetCurrentLoudness();
        if (loudness < MIN_LOUDNESS_THRESHOLD && !_isOverlayActive)
            return;

        canvas.Save();
        if (!_isOverlayActive)
        {
            canvas.Translate(_vibrationOffset, 0);
        }

        try
        {
            if (_staticBitmap != null)
            {
                canvas.DrawBitmap(_staticBitmap, 0, 0);
            }

            var dimensions = CalculateDimensions(info);
            RenderLedSystem(canvas, dimensions, loudness, _peakLoudness);
        }
        finally
        {
            canvas.Restore();
        }
    }

    private void InitializeVariations()
    {
        Random fixedRandom = new(42);
        _ledVariations.Clear();
        for (int i = 0; i < 30; i++)
        {
            _ledVariations.Add(0.85f + (float)fixedRandom.NextDouble() * 0.3f);
        }

        _ledColorVariations.Clear();
        SKColor[] baseColors = [
            new(30, 200, 30),
            new(220, 200, 0),
            new(230, 30, 30)
        ];

        foreach (var baseColor in baseColors)
        {
            for (int j = 0; j < 10; j++)
            {
                _ledColorVariations.Add(new SKColor(
                    (byte)Clamp(baseColor.Red + fixedRandom.Next(-10, 10), 0, 255),
                    (byte)Clamp(baseColor.Green + fixedRandom.Next(-10, 10), 0, 255),
                    (byte)Clamp(baseColor.Blue + fixedRandom.Next(-10, 10), 0, 255)
                ));
            }
        }
    }

    private void InitializeLedAnimationPhases()
    {
        _ledAnimationPhases = new float[_ledCount];
        Random phaseRandom = new(42);
        for (int i = 0; i < _ledCount; i++)
        {
            _ledAnimationPhases[i] = (float)phaseRandom.NextDouble();
        }
    }

    private void CreateCachedTextures()
    {
        _screwBitmap = CreateScrewTexture();
        _brushedMetalBitmap = CreateBrushedMetalTexture();
    }

    private void ResetState()
    {
        _animationPhase = 0f;
        _vibrationOffset = 0f;
        _previousLoudness = 0f;
        _peakLoudness = 0f;
        _cachedLoudness = null;
        _currentWidth = 0;
        _currentHeight = 0;
        _overlayStateChanged = true;
        _overlayStateChangeRequested = false;
    }

    private float GetCurrentLoudness()
    {
        lock (_loudnessLock)
        {
            return _cachedLoudness ?? 0f;
        }
    }

    private float CalculateAndSmoothLoudness(float[] spectrum)
    {
        float rawLoudness = CalculateLoudness(spectrum.AsSpan());
        float smoothedLoudness = _previousLoudness +
            (rawLoudness - _previousLoudness) * _smoothingFactor;
        smoothedLoudness = Clamp(smoothedLoudness, MIN_LOUDNESS_THRESHOLD, 1f);
        _previousLoudness = smoothedLoudness;
        return smoothedLoudness;
    }

    private static float CalculateLoudness(ReadOnlySpan<float> spectrum)
    {
        if (spectrum.IsEmpty) return 0f;

        float sum = 0f;
        for (int i = 0; i < spectrum.Length; i++)
        {
            sum += MathF.Abs(spectrum[i]);
        }
        return Clamp(sum / spectrum.Length, 0f, 1f);
    }

    private readonly record struct MeterDimensions(
        SKRect OuterRect,
        SKRect PanelRect,
        SKRect MeterRect,
        SKRect LedPanelRect);

    private static MeterDimensions CalculateDimensions(SKImageInfo info)
    {
        float outerPadding = 5f;
        float panelLeft = PANEL_PADDING;
        float panelTop = PANEL_PADDING;
        float panelWidth = info.Width - PANEL_PADDING * 2;
        float panelHeight = info.Height - PANEL_PADDING * 2;

        SKRect outerRect = new(
            outerPadding,
            outerPadding,
            info.Width - outerPadding,
            info.Height - outerPadding);

        SKRect panelRect = new(
            panelLeft,
            panelTop,
            panelLeft + panelWidth,
            panelTop + panelHeight);

        float meterLeft = panelLeft + TICK_MARK_WIDTH + 5;
        float meterTop = panelTop + 20;
        float meterWidth = panelWidth - (TICK_MARK_WIDTH + 15);
        float meterHeight = panelHeight - 25;

        SKRect meterRect = new(
            meterLeft,
            meterTop,
            meterLeft + meterWidth,
            meterTop + meterHeight);

        SKRect ledPanelRect = new(
            meterLeft - 3,
            meterTop - 3,
            meterLeft + meterWidth + 6,
            meterTop + meterHeight + 6);

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
        using var paint = CreateGradientPaint(
            new SKPoint(0, 0),
            new SKPoint(0, rect.Height),
            [new SKColor(70, 70, 70), new SKColor(40, 40, 40), new SKColor(55, 55, 55)],
            [0.0f, 0.7f, 1.0f]);

        canvas.DrawRoundRect(rect, CORNER_RADIUS, CORNER_RADIUS, paint);

        using var highlightPaint = CreatePaint(
            new SKColor(255, 255, 255, 40),
            SKPaintStyle.Stroke,
            1.2f);

        canvas.DrawLine(
            rect.Left + CORNER_RADIUS, rect.Top + 1.5f,
            rect.Right - CORNER_RADIUS, rect.Top + 1.5f,
            highlightPaint);
    }

    private void RenderPanel(SKCanvas canvas, SKRect rect)
    {
        using var roundRect = new SKRoundRect(rect, CORNER_RADIUS - 4, CORNER_RADIUS - 4);

        if (_brushedMetalBitmap != null)
        {
            using var paint = CreateBitmapPaint(_brushedMetalBitmap, 1.5f);
            canvas.DrawRoundRect(roundRect, paint);
        }

        RenderPanelBevel(canvas, roundRect);

        if (_useAdvancedEffects)
        {
            using var vignettePaint = CreateRadialGradientPaint(
                new SKPoint(rect.MidX, rect.MidY),
                MathF.Max(rect.Width, rect.Height) * 0.75f,
                [new SKColor(0, 0, 0, 0), new SKColor(0, 0, 0, 30)]);

            canvas.DrawRoundRect(roundRect, vignettePaint);
        }
    }

    private void RenderPanelBevel(SKCanvas canvas, SKRoundRect roundRect)
    {
        using var highlightPaint = CreatePaint(
            new SKColor(255, 255, 255, 120),
            SKPaintStyle.Stroke,
            BEVEL_SIZE);

        using var highlightPath = CreateBevelHighlightPath(roundRect);
        canvas.DrawPath(highlightPath, highlightPaint);

        using var shadowPaint = CreatePaint(
            new SKColor(0, 0, 0, 90),
            SKPaintStyle.Stroke,
            BEVEL_SIZE);

        using var shadowPath = CreateBevelShadowPath(roundRect);
        canvas.DrawPath(shadowPath, shadowPaint);
    }

    private static SKPath CreateBevelHighlightPath(SKRoundRect roundRect)
    {
        var path = new SKPath();
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

        return path;
    }

    private static SKPath CreateBevelShadowPath(SKRoundRect roundRect)
    {
        var path = new SKPath();
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

        return path;
    }

    private void RenderRecessedLedPanel(SKCanvas canvas, SKRect rect)
    {
        float recessRadius = 6f;
        using var recessRoundRect = new SKRoundRect(rect, recessRadius, recessRadius);

        using var backgroundPaint = CreatePaint(
            new SKColor(12, 12, 12),
            SKPaintStyle.Fill,
            0);

        canvas.DrawRoundRect(recessRoundRect, backgroundPaint);

        if (_useAdvancedEffects)
        {
            using var shadowPaint = CreateGradientPaint(
                new SKPoint(rect.Left, rect.Top),
                new SKPoint(rect.Left, rect.Top + rect.Height * 0.2f),
                [new SKColor(0, 0, 0, 120), new SKColor(0, 0, 0, 0)],
                null);

            canvas.DrawRoundRect(recessRoundRect, shadowPaint);
        }

        using var borderPaint = CreatePaint(
            new SKColor(0, 0, 0, 180),
            SKPaintStyle.Stroke,
            1);

        canvas.DrawRoundRect(recessRoundRect, borderPaint);
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

        using var shadowPaint = CreatePaint(
            new SKColor(30, 30, 30, 180),
            SKPaintStyle.Fill,
            0);

        canvas.DrawText("VU", labelX + 1, labelY + 1, SKTextAlign.Left, font14, shadowPaint);

        using var mainPaint = CreatePaint(
            new SKColor(230, 230, 230, 200),
            SKPaintStyle.Fill,
            0);

        canvas.DrawText("VU", labelX, labelY, SKTextAlign.Left, font14, mainPaint);

        using var secondaryPaint = CreatePaint(
            new SKColor(200, 200, 200, 150),
            SKPaintStyle.Fill,
            0);

        canvas.DrawText("dB METER", labelX + 30, labelY, SKTextAlign.Left, font10, secondaryPaint);

        using var tertiaryPaint = CreatePaint(
            new SKColor(200, 200, 200, 120),
            SKPaintStyle.Fill,
            0);

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

    private void RenderTickMarks(
        SKCanvas canvas,
        SKRect panelRect,
        SKRect meterRect)
    {
        SKRect tickAreaRect = new(
            panelRect.Left,
            meterRect.Top,
            panelRect.Left + TICK_MARK_WIDTH - 2,
            meterRect.Bottom);

        using var tickAreaPaint = CreatePaint(
            new SKColor(30, 30, 30, 70),
            SKPaintStyle.Fill,
            0);

        canvas.DrawRect(tickAreaRect, tickAreaPaint);

        using var tickPaint = CreatePaint(
            SKColors.LightGray.WithAlpha(150),
            SKPaintStyle.Stroke,
            1);

        using var font = new SKFont(
            SKTypeface.FromFamilyName(
                "Arial",
                SKFontStyleWeight.Normal,
                SKFontStyleWidth.Normal,
                SKFontStyleSlant.Upright),
            9);

        string[] dbValues = ["0", "-3", "-6", "-10", "-20", "-40"];
        float[] dbPositions = [1.0f, 0.85f, 0.7f, 0.55f, 0.3f, 0.0f];

        float x = meterRect.Left - TICK_MARK_WIDTH;
        float width = TICK_MARK_WIDTH;
        float height = meterRect.Height;
        float y = meterRect.Top;

        for (int i = 0; i < dbValues.Length; i++)
        {
            float yPos = y + height - dbPositions[i] * height;
            canvas.DrawLine(x, yPos, x + width - 5, yPos, tickPaint);

            if (_useAdvancedEffects)
            {
                using var shadowPaint = CreatePaint(
                    SKColors.Black.WithAlpha(80),
                    SKPaintStyle.Fill,
                    0);

                canvas.DrawText(
                    dbValues[i],
                    x + width - 7,
                    yPos + 3.5f,
                    SKTextAlign.Right,
                    font,
                    shadowPaint);
            }

            using var textPaint = CreatePaint(
                SKColors.LightGray.WithAlpha(180),
                SKPaintStyle.Fill,
                0);

            canvas.DrawText(
                dbValues[i],
                x + width - 8,
                yPos + 3,
                SKTextAlign.Right,
                font,
                textPaint);
        }

        if (_useAdvancedEffects)
        {
            tickPaint.Color = SKColors.LightGray.WithAlpha(80);
            for (int i = 0; i < 10; i++)
            {
                float ratio = i / 10f;
                float yPos = y + ratio * height;
                canvas.DrawLine(x, yPos, x + width * 0.6f, yPos, tickPaint);
            }
        }
    }

    private void RenderScrews(SKCanvas canvas, SKRect panelRect)
    {
        if (_screwBitmap == null) return;

        float cornerOffset = CORNER_RADIUS - 4;

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
            canvas.Translate(-12, -12);
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

        using var paint = CreatePaint(
            new SKColor(230, 230, 230, 120),
            SKPaintStyle.Fill,
            0);

        canvas.DrawText(
            "SpectrumNet™ Audio",
            panelRect.Right - 65,
            panelRect.Bottom - 8,
            SKTextAlign.Right,
            font,
            paint);
    }

    private void RenderLedSystem(
        SKCanvas canvas,
        MeterDimensions dimensions,
        float loudness,
        float peakLoudness)
    {
        int activeLedCount = (int)(loudness * _ledCount);
        int peakLedIndex = (int)(peakLoudness * _ledCount);

        var ledDimensions = CalculateLedDimensions(dimensions.MeterRect);

        for (int i = 0; i < _ledCount; i++)
        {
            var ledInfo = CalculateLedInfo(i, dimensions.MeterRect, ledDimensions);
            SKColor color = GetLedColorForPosition(ledInfo.NormalizedPosition, i);

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

    private readonly record struct LedDimensions(
        float Height,
        float Spacing,
        float Width);

    private LedDimensions CalculateLedDimensions(SKRect meterRect)
    {
        float totalLedSpace = meterRect.Height * 0.95f;
        float totalSpacingSpace = meterRect.Height * 0.05f;
        float ledHeight = (totalLedSpace - totalSpacingSpace) / _ledCount;
        float spacing = _ledCount > 1 ? totalSpacingSpace / (_ledCount - 1) : 0;
        float ledWidth = meterRect.Width;

        return new LedDimensions(ledHeight, spacing, ledWidth);
    }

    private readonly record struct LedInfo(
        float X,
        float Y,
        float Width,
        float Height,
        float NormalizedPosition);

    private LedInfo CalculateLedInfo(
        int index,
        SKRect meterRect,
        LedDimensions ledDimensions)
    {
        float normalizedPosition = (float)index / _ledCount;
        float ledY = meterRect.Top + (_ledCount - index - 1) *
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

        using var basePaint = CreatePaint(
            new SKColor(8, 8, 8),
            SKPaintStyle.Fill,
            0);

        canvas.DrawRoundRect(ledRect, basePaint);

        float inset = 1f;
        using var surfaceRect = new SKRoundRect(
            new SKRect(
                ledInfo.X + inset,
                ledInfo.Y + inset,
                ledInfo.X + ledInfo.Width - inset,
                ledInfo.Y + ledInfo.Height - inset),
                        MathF.Max(1, LED_ROUNDING_RADIUS - inset * 0.5f),
            MathF.Max(1, LED_ROUNDING_RADIUS - inset * 0.5f));

        using var surfacePaint = CreatePaint(
            MultiplyColor(color, 0.10f),
            SKPaintStyle.Fill,
            0);

        canvas.DrawRoundRect(surfaceRect, surfacePaint);
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

        using var basePaint = CreatePaint(
            new SKColor(8, 8, 8),
            SKPaintStyle.Fill,
            0);

        canvas.DrawRoundRect(ledRect, basePaint);

        float brightnessVariation = _ledVariations[index % _ledVariations.Count];
        float animPhase = _ledAnimationPhases[index % _ledAnimationPhases.Length];
        float pulse = isPeak ?
            0.7f + Sin(animPhase * MathF.PI * 2) * 0.3f :
            brightnessVariation;

        SKColor ledOnColor = MultiplyColor(color, pulse);

        if (_useAdvancedEffects && index <= ledRect.Rect.Height * 0.7f)
        {
            float glowIntensity = GLOW_INTENSITY *
                (0.8f + Sin(animPhase * MathF.PI * 2) * 0.2f * brightnessVariation);

            using var glowPaint = CreatePaint(
                ledOnColor.WithAlpha((byte)(glowIntensity * 160 * brightnessVariation)),
                SKPaintStyle.Fill,
                0,
                createBlur: true,
                blurRadius: 2);

            canvas.DrawRoundRect(ledRect, glowPaint);
        }

        float inset = 1f;
        using var ledSurfaceRect = new SKRoundRect(
            new SKRect(
                ledInfo.X + inset,
                ledInfo.Y + inset,
                ledInfo.X + ledInfo.Width - inset,
                ledInfo.Y + ledInfo.Height - inset),
            MathF.Max(1, LED_ROUNDING_RADIUS - inset * 0.5f),
            MathF.Max(1, LED_ROUNDING_RADIUS - inset * 0.5f));

        using var ledPaint = CreateGradientPaint(
            new SKPoint(ledInfo.X, ledInfo.Y),
            new SKPoint(ledInfo.X, ledInfo.Y + ledInfo.Height),
            [ledOnColor, MultiplyColor(ledOnColor, 0.9f), new(10, 10, 10, 220)],
            [0.0f, 0.7f, 1.0f]);

        canvas.DrawRoundRect(ledSurfaceRect, ledPaint);

        if (_useAdvancedEffects)
        {
            RenderLedHighlight(canvas, ledInfo);
        }
    }

    private void RenderLedHighlight(SKCanvas canvas, LedInfo ledInfo)
    {
        float arcWidth = ledInfo.Width * 0.9f;
        float arcHeight = ledInfo.Height * 0.4f;
        float arcX = ledInfo.X + (ledInfo.Width - arcWidth) / 2;
        float arcY = ledInfo.Y + ledInfo.Height * 0.05f;

        using var highlightRect = new SKRoundRect(
            new SKRect(arcX, arcY, arcX + arcWidth, arcY + arcHeight),
            LED_ROUNDING_RADIUS,
            LED_ROUNDING_RADIUS);

        using var fillPaint = CreatePaint(
            new SKColor(255, 255, 255, 50),
            SKPaintStyle.Fill,
            0);

        canvas.DrawRoundRect(highlightRect, fillPaint);

        using var strokePaint = CreatePaint(
            new SKColor(255, 255, 255, 180),
            SKPaintStyle.Stroke,
            0.7f);

        canvas.DrawRoundRect(highlightRect, strokePaint);
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

        int variationIndex = index % 10;
        int colorIndex = colorGroup * 10 + variationIndex;

        if (colorIndex < _ledColorVariations.Count)
            return _ledColorVariations[colorIndex];

        return colorGroup switch
        {
            2 => new SKColor(220, 30, 30),
            1 => new SKColor(230, 200, 0),
            _ => new SKColor(40, 200, 40)
        };
    }

    private static SKBitmap CreateScrewTexture()
    {
        var bitmap = new SKBitmap(SCREW_TEXTURE_SIZE, SCREW_TEXTURE_SIZE);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        using var circlePaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(4, 4),
                new SKPoint(20, 20),
                [new SKColor(220, 220, 220), new SKColor(140, 140, 140)],
                [0.0f, 1.0f],
                SKShaderTileMode.Clamp)
        };

        canvas.DrawCircle(12, 12, 10, circlePaint);

        using var slotPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.5f,
            Color = new SKColor(50, 50, 50, 180)
        };

        canvas.DrawLine(7, 12, 17, 12, slotPaint);

        using var highlightPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            Color = new SKColor(255, 255, 255, 100)
        };

        canvas.DrawArc(new SKRect(4, 4, 20, 20), 200, 160, false, highlightPaint);

        using var shadowPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f,
            Color = new SKColor(0, 0, 0, 100)
        };

        canvas.DrawCircle(12, 12, 9, shadowPaint);

        return bitmap;
    }

    private SKBitmap CreateBrushedMetalTexture()
    {
        var bitmap = new SKBitmap(BRUSHED_METAL_TEXTURE_SIZE, BRUSHED_METAL_TEXTURE_SIZE);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(190, 190, 190));

        Random texRandom = new(42);

        using var linePaint = _paintPool.Get();
        linePaint.IsAntialias = false;
        linePaint.StrokeWidth = 1;

        for (int i = 0; i < 150; i++)
        {
            float y = (float)texRandom.NextDouble() * BRUSHED_METAL_TEXTURE_SIZE;
            linePaint.Color = new SKColor(210, 210, 210).WithAlpha((byte)texRandom.Next(10, 20));
            canvas.DrawLine(0, y, BRUSHED_METAL_TEXTURE_SIZE, y, linePaint);
        }

        for (int i = 0; i < 30; i++)
        {
            float y = (float)texRandom.NextDouble() * BRUSHED_METAL_TEXTURE_SIZE;
            linePaint.Color = new SKColor(100, 100, 100).WithAlpha((byte)texRandom.Next(5, 10));
            canvas.DrawLine(0, y, BRUSHED_METAL_TEXTURE_SIZE, y, linePaint);
        }

        using var gradientPaint = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(BRUSHED_METAL_TEXTURE_SIZE, BRUSHED_METAL_TEXTURE_SIZE),
                [new SKColor(255, 255, 255, 20), new SKColor(0, 0, 0, 20)],
                [0.0f, 1.0f],
                SKShaderTileMode.Clamp)
        };

        canvas.DrawRect(
            0, 0,
            BRUSHED_METAL_TEXTURE_SIZE,
            BRUSHED_METAL_TEXTURE_SIZE,
            gradientPaint);

        return bitmap;
    }

    private SKPaint CreatePaint(
        SKColor color,
        SKPaintStyle style,
        float strokeWidth,
        SKStrokeCap strokeCap = SKStrokeCap.Butt,
        bool createBlur = false,
        float blurRadius = 0)
    {
        var paint = _paintPool.Get();
        paint.Color = color;
        paint.Style = style;
        paint.IsAntialias = _useAntiAlias;
        paint.StrokeWidth = strokeWidth;
        paint.StrokeCap = strokeCap;

        if (createBlur && blurRadius > 0)
        {
            paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, blurRadius);
        }

        return paint;
    }

    private SKPaint CreateGradientPaint(
        SKPoint start,
        SKPoint end,
        SKColor[] colors,
        float[]? positions)
    {
        var paint = _paintPool.Get();
        paint.Shader = SKShader.CreateLinearGradient(
            start, end, colors, positions ?? new float[colors.Length], SKShaderTileMode.Clamp);
        paint.IsAntialias = _useAntiAlias;
        return paint;
    }

    private SKPaint CreateRadialGradientPaint(
        SKPoint center,
        float radius,
        SKColor[] colors)
    {
        var paint = _paintPool.Get();
        paint.Shader = SKShader.CreateRadialGradient(
            center, radius, colors, new float[colors.Length], SKShaderTileMode.Clamp);
        paint.IsAntialias = _useAntiAlias;
        return paint;
    }

    private SKPaint CreateBitmapPaint(SKBitmap bitmap, float scale)
    {
        var paint = _paintPool.Get();
        paint.Shader = SKShader.CreateBitmap(
            bitmap,
            SKShaderTileMode.Repeat,
            SKShaderTileMode.Repeat,
            SKMatrix.CreateScale(scale, scale));
        paint.IsAntialias = _useAntiAlias;
        return paint;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static SKColor MultiplyColor(SKColor color, float factor)
    {
        return new SKColor(
            (byte)Clamp(color.Red * factor, 0, 255),
            (byte)Clamp(color.Green * factor, 0, 255),
            (byte)Clamp(color.Blue * factor, 0, 255),
            color.Alpha);
    }

    public override bool RequiresRedraw()
    {
        return _overlayStateChanged ||
               _overlayStateChangeRequested ||
               _isOverlayActive ||
               _animationPhase != 0;
    }

    protected override void OnInvalidateCachedResources()
    {
        base.OnInvalidateCachedResources();
        _cachedLoudness = null;
        _overlayStateChanged = true;
    }

    protected override void OnDispose()
    {
        _screwBitmap?.Dispose();
        _brushedMetalBitmap?.Dispose();
        _staticBitmap?.Dispose();
        _ledColorVariations.Clear();
        _ledVariations.Clear();
        base.OnDispose();
        _logger.Log(LogLevel.Debug, LogPrefix, "Disposed");
    }
}