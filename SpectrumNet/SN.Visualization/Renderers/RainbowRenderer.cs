#nullable enable

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class RainbowRenderer : EffectSpectrumRenderer<RainbowRenderer.QualitySettings>
{
    private static readonly Lazy<RainbowRenderer> _instance =
        new(() => new RainbowRenderer());

    public static RainbowRenderer GetInstance() => _instance.Value;

    private const float 
        ALPHA_MULTIPLIER = 1.7f,
        CORNER_RADIUS = 8f,
        GLOW_INTENSITY_OVERLAY = 0.2f,
        GLOW_RADIUS_OVERLAY = 3f,
        HIGHLIGHT_ALPHA_OVERLAY = 0.3f,
        REFLECTION_OPACITY_OVERLAY = 0.1f,
        REFLECTION_FACTOR = 0.3f,
        HUE_START = 240f,
        HUE_RANGE = 240f,
        GLOW_THRESHOLD = 0.4f,
        REFLECTION_THRESHOLD = 0.2f,
        HIGHLIGHT_WIDTH_FACTOR = 0.6f,
        HIGHLIGHT_X_OFFSET_FACTOR = 0.2f,
        HIGHLIGHT_MAX_HEIGHT = 10f,
        REFLECTION_HEIGHT_FACTOR = 0.1f,
        BRIGHTNESS_BASE = 90f,
        BRIGHTNESS_RANGE = 10f,
        SATURATION = 100f,
        HUE_WRAP = 360f,
        MIN_MAGNITUDE_THRESHOLD = 0.01f;

    public sealed class QualitySettings
    {
        public bool UseGlow { get; init; }
        public bool UseHighlight { get; init; }
        public bool UseReflection { get; init; }
        public float GlowIntensity { get; init; }
        public float GlowRadius { get; init; }
        public float ReflectionOpacity { get; init; }
        public float HighlightAlpha { get; init; }
    }

    protected override IReadOnlyDictionary<RenderQuality, QualitySettings>
        QualitySettingsPresets
    { get; } = new Dictionary<RenderQuality, QualitySettings>
    {
        [RenderQuality.Low] = new()
        {
            UseGlow = false,
            UseHighlight = false,
            UseReflection = false,
            GlowIntensity = 0f,
            GlowRadius = 0f,
            ReflectionOpacity = 0f,
            HighlightAlpha = 0f
        },
        [RenderQuality.Medium] = new()
        {
            UseGlow = true,
            UseHighlight = true,
            UseReflection = false,
            GlowIntensity = 0.2f,
            GlowRadius = 3f,
            ReflectionOpacity = 0.15f,
            HighlightAlpha = 0.4f
        },
        [RenderQuality.High] = new()
        {
            UseGlow = true,
            UseHighlight = true,
            UseReflection = true,
            GlowIntensity = 0.3f,
            GlowRadius = 5f,
            ReflectionOpacity = 0.2f,
            HighlightAlpha = 0.5f
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

        var rainbowData = CalculateRainbowData(
            processedSpectrum,
            info,
            renderParams);

        if (!ValidateRainbowData(rainbowData))
            return;

        RenderRainbowVisualization(
            canvas,
            rainbowData,
            renderParams,
            passedInPaint);
    }

    private static RainbowData CalculateRainbowData(
        float[] spectrum,
        SKImageInfo info,
        RenderParameters renderParams)
    {
        var bars = new List<BarData>(spectrum.Length);
        float totalBarWidth = renderParams.BarWidth + renderParams.BarSpacing;
        float startX = (info.Width - spectrum.Length * totalBarWidth + renderParams.BarSpacing) / 2f;

        for (int i = 0; i < spectrum.Length; i++)
        {
            float magnitude = spectrum[i];
            if (magnitude < MIN_MAGNITUDE_THRESHOLD)
                continue;

            float x = startX + i * totalBarWidth;
            float barHeight = magnitude * info.Height;
            float y = info.Height - barHeight;

            var barRect = new SKRect(
                x,
                y,
                x + renderParams.BarWidth,
                info.Height);

            bars.Add(new BarData(
                Rect: barRect,
                Magnitude: magnitude,
                Index: i,
                Color: GetRainbowColor(magnitude)));
        }

        return new RainbowData(
            Bars: bars,
            CanvasHeight: info.Height,
            StartX: startX);
    }

    private static bool ValidateRainbowData(RainbowData data) =>
        data.Bars.Count > 0 && data.CanvasHeight > 0;

    private void RenderRainbowVisualization(
        SKCanvas canvas,
        RainbowData data,
        RenderParameters renderParams,
        SKPaint basePaint)
    {
        var settings = CurrentQualitySettings!;

        RenderWithOverlay(canvas, () =>
        {
            if (UseAdvancedEffects && settings.UseGlow)
                RenderGlowLayer(canvas, data, settings);

            RenderMainBars(canvas, data, settings);

            if (UseAdvancedEffects && settings.UseReflection)
                RenderReflectionLayer(canvas, data, renderParams, settings);
        });
    }

    private void RenderGlowLayer(
        SKCanvas canvas,
        RainbowData data,
        QualitySettings settings)
    {
        float glowRadius = GetAdaptiveParameter(settings.GlowRadius, GLOW_RADIUS_OVERLAY);
        float glowIntensity = GetAdaptiveParameter(settings.GlowIntensity, GLOW_INTENSITY_OVERLAY);

        using var blurFilter = SKImageFilter.CreateBlur(glowRadius, glowRadius);

        foreach (var bar in data.Bars.Where(b => b.Magnitude > GLOW_THRESHOLD))
        {
            var glowPaint = CreatePaint(
                bar.Color.WithAlpha((byte)(glowIntensity * 255)),
                SKPaintStyle.Fill);
            glowPaint.ImageFilter = blurFilter;

            try
            {
                canvas.DrawRoundRect(bar.Rect, CORNER_RADIUS, CORNER_RADIUS, glowPaint);
            }
            finally
            {
                ReturnPaint(glowPaint);
            }
        }
    }

    private void RenderMainBars(
        SKCanvas canvas,
        RainbowData data,
        QualitySettings settings)
    {
        foreach (var bar in data.Bars)
        {
            byte alpha = CalculateAlpha(bar.Magnitude * ALPHA_MULTIPLIER);
            var barPaint = CreatePaint(bar.Color.WithAlpha(alpha), SKPaintStyle.Fill);

            try
            {
                canvas.DrawRoundRect(bar.Rect, CORNER_RADIUS, CORNER_RADIUS, barPaint);

                if (UseAdvancedEffects && settings.UseHighlight && bar.Rect.Height > CORNER_RADIUS * 2)
                {
                    RenderHighlight(canvas, bar, settings);
                }
            }
            finally
            {
                ReturnPaint(barPaint);
            }
        }
    }

    private void RenderHighlight(
        SKCanvas canvas,
        BarData bar,
        QualitySettings settings)
    {
        float highlightAlpha = GetAdaptiveParameter(settings.HighlightAlpha, HIGHLIGHT_ALPHA_OVERLAY);
        byte alpha = (byte)(bar.Magnitude * highlightAlpha * 255);

        var highlightPaint = CreatePaint(SKColors.White.WithAlpha(alpha), SKPaintStyle.Fill);

        try
        {
            canvas.DrawRect(
                bar.Rect.Left + bar.Rect.Width * HIGHLIGHT_X_OFFSET_FACTOR,
                bar.Rect.Top,
                bar.Rect.Width * HIGHLIGHT_WIDTH_FACTOR,
                MathF.Min(HIGHLIGHT_MAX_HEIGHT, CORNER_RADIUS),
                highlightPaint);
        }
        finally
        {
            ReturnPaint(highlightPaint);
        }
    }

    private void RenderReflectionLayer(
        SKCanvas canvas,
        RainbowData data,
        RenderParameters renderParams,
        QualitySettings settings)
    {
        float reflectionOpacity = GetAdaptiveParameter(settings.ReflectionOpacity, REFLECTION_OPACITY_OVERLAY);

        foreach (var bar in data.Bars.Where(b => b.Magnitude > REFLECTION_THRESHOLD))
        {
            byte alpha = (byte)(bar.Magnitude * reflectionOpacity * 255);
            var reflectionPaint = CreatePaint(bar.Color.WithAlpha(alpha), SKPaintStyle.Fill);
            reflectionPaint.BlendMode = SKBlendMode.SrcOver;

            try
            {
                float reflectHeight = MathF.Min(
                    bar.Rect.Height * REFLECTION_FACTOR,
                    data.CanvasHeight * REFLECTION_HEIGHT_FACTOR);

                canvas.DrawRect(
                    bar.Rect.Left,
                    data.CanvasHeight,
                    bar.Rect.Width,
                    reflectHeight,
                    reflectionPaint);
            }
            finally
            {
                ReturnPaint(reflectionPaint);
            }
        }
    }

    private float GetAdaptiveParameter(float normalValue, float overlayValue) =>
        IsOverlayActive ? overlayValue : normalValue;

    private static SKColor GetRainbowColor(float normalizedValue)
    {
        float hue = HUE_START - HUE_RANGE * normalizedValue;
        if (hue < 0) hue += HUE_WRAP;
        float brightness = BRIGHTNESS_BASE + normalizedValue * BRIGHTNESS_RANGE;
        return SKColor.FromHsv(hue, SATURATION, brightness);
    }

    protected override int GetMaxBarsForQuality() => Quality switch
    {
        RenderQuality.Low => 100,
        RenderQuality.Medium => 200,
        RenderQuality.High => 300,
        _ => 200
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
        {
            smoothingFactor *= 1.2f;
        }

        SetProcessingSmoothingFactor(smoothingFactor);
        RequestRedraw();
    }

    private record RainbowData(
        List<BarData> Bars,
        float CanvasHeight,
        float StartX);

    private record BarData(
        SKRect Rect,
        float Magnitude,
        int Index,
        SKColor Color);
}