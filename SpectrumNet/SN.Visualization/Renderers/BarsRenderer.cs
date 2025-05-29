#nullable enable

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class BarsRenderer : EffectSpectrumRenderer<BarsRenderer.QualitySettings>
{
    private static readonly Lazy<BarsRenderer> _instance =
        new(() => new BarsRenderer());

    public static BarsRenderer GetInstance() => _instance.Value;

    public sealed class QualitySettings
    {
        public bool UseGradient { get; init; }
        public bool UseRoundCorners { get; init; }
        public bool UseBorder { get; init; }
        public bool UseShadow { get; init; }
        public float CornerRadiusRatio { get; init; }
        public float BorderWidth { get; init; }
        public float ShadowOffset { get; init; }
        public byte BorderAlpha { get; init; }
        public byte ShadowAlpha { get; init; }
        public float GradientIntensity { get; init; }
    }

    protected override IReadOnlyDictionary<RenderQuality, QualitySettings>
        QualitySettingsPresets
    { get; } = new Dictionary<RenderQuality, QualitySettings>
    {
        [RenderQuality.Low] = new()
        {
            UseGradient = false,
            UseRoundCorners = true,
            UseBorder = false,
            UseShadow = false,
            CornerRadiusRatio = 0.3f,
            BorderWidth = 0f,
            ShadowOffset = 0f,
            BorderAlpha = 0,
            ShadowAlpha = 0,
            GradientIntensity = 0f
        },
        [RenderQuality.Medium] = new()
        {
            UseGradient = true,
            UseRoundCorners = true,
            UseBorder = false,
            UseShadow = true,
            CornerRadiusRatio = 0.4f,
            BorderWidth = 0f,
            ShadowOffset = 2f,
            BorderAlpha = 0,
            ShadowAlpha = 30,
            GradientIntensity = 0.3f
        },
        [RenderQuality.High] = new()
        {
            UseGradient = true,
            UseRoundCorners = true,
            UseBorder = true,
            UseShadow = true,
            CornerRadiusRatio = 0.45f,
            BorderWidth = 1f,
            ShadowOffset = 3f,
            BorderAlpha = 70,
            ShadowAlpha = 40,
            GradientIntensity = 0.4f
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

        var barRects = CalculateBarRectangles(
            processedSpectrum,
            info,
            renderParams);

        if (barRects.Count == 0)
            return;

        RenderBarVisualization(
            canvas,
            barRects,
            renderParams,
            passedInPaint);
    }

    private List<SKRect> CalculateBarRectangles(
        float[] spectrum,
        SKImageInfo info,
        RenderParameters renderParams)
    {
        var barRects = new List<SKRect>(renderParams.EffectiveBarCount);
        float xPosition = renderParams.StartOffset;
        float minHeight = UseAdvancedEffects ? 2f : 1f;

        for (int i = 0; i < renderParams.EffectiveBarCount && i < spectrum.Length; i++)
        {
            float magnitude = Max(spectrum[i], 0f);

            var rect = GetBarRect(
                xPosition,
                magnitude,
                renderParams.BarWidth,
                info.Height,
                minHeight);

            barRects.Add(rect);
            xPosition += renderParams.BarWidth + renderParams.BarSpacing;
        }

        return barRects;
    }

    private void RenderBarVisualization(
        SKCanvas canvas,
        List<SKRect> barRects,
        RenderParameters renderParams,
        SKPaint basePaint)
    {
        var settings = CurrentQualitySettings!;
        var boundingBox = CalculateBoundingBox(barRects);

        if (!IsAreaVisible(canvas, boundingBox))
            return;

        RenderWithOverlay(canvas, () =>
        {
            if (UseAdvancedEffects && settings.UseShadow)
                RenderShadowLayer(canvas, barRects, renderParams, settings);

            RenderMainBars(canvas, barRects, boundingBox, renderParams, basePaint, settings);

            if (settings.UseBorder)
                RenderBorderLayer(canvas, barRects, renderParams, settings);
        });
    }

    private static SKRect CalculateBoundingBox(List<SKRect> barRects)
    {
        if (barRects.Count == 0)
            return SKRect.Empty;

        return barRects.Aggregate((acc, r) => SKRect.Union(acc, r));
    }

    private void RenderShadowLayer(
        SKCanvas canvas,
        List<SKRect> barRects,
        RenderParameters renderParams,
        QualitySettings settings)
    {
        if (settings.ShadowAlpha == 0) return;

        var shadowPaint = CreatePaint(
            new SKColor(0, 0, 0, settings.ShadowAlpha),
            SKPaintStyle.Fill);

        try
        {
            var shadowRects = barRects
                .Select(rect => CreateShadowRect(rect, settings.ShadowOffset))
                .ToList();

            float cornerRadius = CalculateCornerRadius(renderParams, settings);
            RenderRects(canvas, shadowRects, shadowPaint, cornerRadius);
        }
        finally
        {
            ReturnPaint(shadowPaint);
        }
    }

    private void RenderMainBars(
        SKCanvas canvas,
        List<SKRect> barRects,
        SKRect boundingBox,
        RenderParameters renderParams,
        SKPaint basePaint,
        QualitySettings settings)
    {
        float cornerRadius = CalculateCornerRadius(renderParams, settings);

        if (UseAdvancedEffects && settings.UseGradient && settings.GradientIntensity > 0)
        {
            RenderGradientBars(
                canvas,
                barRects,
                boundingBox,
                cornerRadius,
                basePaint,
                settings);
        }
        else
        {
            RenderRects(canvas, barRects, basePaint, cornerRadius);
        }
    }

    private void RenderGradientBars(
        SKCanvas canvas,
        List<SKRect> barRects,
        SKRect boundingBox,
        float cornerRadius,
        SKPaint basePaint,
        QualitySettings settings)
    {
        var topColor = basePaint.Color;
        var bottomAlpha = CalculateAlpha(1f - settings.GradientIntensity);
        var bottomColor = basePaint.Color.WithAlpha(bottomAlpha);

        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(boundingBox.Left, boundingBox.Top),
            new SKPoint(boundingBox.Left, boundingBox.Bottom),
            [topColor, bottomColor],
            CreateUniformGradientPositions(2),
            SKShaderTileMode.Clamp);

        var gradientPaint = CreatePaint(
            basePaint.Color,
            basePaint.Style,
            shader);

        try
        {
            RenderRects(canvas, barRects, gradientPaint, cornerRadius);
        }
        finally
        {
            ReturnPaint(gradientPaint);
        }
    }

    private void RenderBorderLayer(
        SKCanvas canvas,
        List<SKRect> barRects,
        RenderParameters renderParams,
        QualitySettings settings)
    {
        if (settings.BorderAlpha == 0) return;

        var borderPaint = CreatePaint(
            new SKColor(255, 255, 255, settings.BorderAlpha),
            SKPaintStyle.Stroke);

        borderPaint.StrokeWidth = settings.BorderWidth;

        try
        {
            float cornerRadius = CalculateCornerRadius(renderParams, settings);
            RenderRects(canvas, barRects, borderPaint, cornerRadius);
        }
        finally
        {
            ReturnPaint(borderPaint);
        }
    }

    private static SKRect CreateShadowRect(SKRect rect, float offset) =>
        new(
            rect.Left + offset,
            rect.Top + offset,
            rect.Right + offset,
            rect.Bottom);

    private static float CalculateCornerRadius(
        RenderParameters renderParams,
        QualitySettings settings) =>
        settings.UseRoundCorners
            ? renderParams.BarWidth * settings.CornerRadiusRatio
            : 0f;

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

        SetProcessingSmoothingFactor(smoothingFactor);
        RequestRedraw();
    }
}