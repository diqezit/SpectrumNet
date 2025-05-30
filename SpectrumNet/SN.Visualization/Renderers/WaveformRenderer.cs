#nullable enable

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class WaveformRenderer : EffectSpectrumRenderer<WaveformRenderer.QualitySettings>
{
    private static readonly Lazy<WaveformRenderer> _instance =
        new(() => new WaveformRenderer());

    public static WaveformRenderer GetInstance() => _instance.Value;

    private const float
        MIN_STROKE_WIDTH = 2.5f,
        MIN_STROKE_WIDTH_OVERLAY = 3.5f,
        STROKE_WIDTH_DIVISOR_BASE = 80f,
        STROKE_WIDTH_DIVISOR_OVERLAY_BASE = 60f,
        CENTER_PROPORTION = 0.5f,
        OVERLAY_SMOOTHING_FACTOR = 1.25f;

    private const float
        FILL_OPACITY_BASE = 0.35f,
        FILL_OPACITY_OVERLAY_BASE = 0.2f,
        GRADIENT_FADE_FACTOR_BASE = 0.4f,
        GRADIENT_FADE_FACTOR_OVERLAY_BASE = 0.3f;

    private const float
        ACCENT_THRESHOLD = 0.65f,
        ACCENT_ALPHA_BASE = 0.6f,
        ACCENT_ALPHA_OVERLAY_BASE = 0.4f,
        ACCENT_RADIUS_BASE = 5f,
        ACCENT_RADIUS_OVERLAY_BASE = 3.5f,
        ACCENT_GLOW_RADIUS_MULTIPLIER = 2.0f,
        ACCENT_GLOW_ALPHA_BASE = 0.3f;

    private const float
        OUTLINE_ALPHA_BASE = 0.15f,
        OUTLINE_ALPHA_OVERLAY_BASE = 0.1f,
        OUTLINE_WIDTH_MULTIPLIER_BASE = 1.5f,
        OUTLINE_WIDTH_MULTIPLIER_OVERLAY_BASE = 1.2f;

    private const float
        MIRROR_ALPHA_BASE = 0.3f;

    private const float
        SHADOW_OFFSET_X = 1.5f,
        SHADOW_OFFSET_Y = 1.5f,
        SHADOW_BLUR_RADIUS = 2.0f,
        SHADOW_ALPHA = 0.25f;

    private const int
        MAX_ACCENT_POINTS_BASE = 30,
        MAX_ACCENT_POINTS_OVERLAY_BASE = 20;

    private WaveformPaths? _cachedPaths;

    public sealed class QualitySettings
    {
        public bool UseCubicCurves { get; init; }
        public bool UseFill { get; init; }
        public bool UseGradientFill { get; init; }
        public bool UseAccent { get; init; }
        public bool UseAccentGlow { get; init; }
        public bool UseOutline { get; init; }
        public bool UseMirror { get; init; }
        public bool UseWaveShadow { get; init; }
        public float StrokeWidthMultiplier { get; init; }
        public int CurveDefinition { get; init; }
        public float FillOpacityMultiplier { get; init; }
        public float AccentGlowIntensity { get; init; }
    }

    protected override IReadOnlyDictionary<RenderQuality, QualitySettings>
        QualitySettingsPresets
    { get; } = new Dictionary<RenderQuality, QualitySettings>
    {
        [RenderQuality.Low] = new()
        {
            UseCubicCurves = false,
            UseFill = true,
            UseGradientFill = false,
            UseAccent = false,
            UseAccentGlow = false,
            UseOutline = false,
            UseMirror = false,
            UseWaveShadow = false,
            StrokeWidthMultiplier = 0.9f,
            CurveDefinition = 1,
            FillOpacityMultiplier = 0.8f,
            AccentGlowIntensity = 0.7f
        },
        [RenderQuality.Medium] = new()
        {
            UseCubicCurves = true,
            UseFill = true,
            UseGradientFill = true,
            UseAccent = true,
            UseAccentGlow = false,
            UseOutline = true,
            UseMirror = false,
            UseWaveShadow = false,
            StrokeWidthMultiplier = 1.0f,
            CurveDefinition = 2,
            FillOpacityMultiplier = 1.0f,
            AccentGlowIntensity = 1.0f
        },
        [RenderQuality.High] = new()
        {
            UseCubicCurves = true,
            UseFill = true,
            UseGradientFill = true,
            UseAccent = true,
            UseAccentGlow = true,
            UseOutline = true,
            UseMirror = true,
            UseWaveShadow = true,
            StrokeWidthMultiplier = 1.1f,
            CurveDefinition = 3,
            FillOpacityMultiplier = 1.1f,
            AccentGlowIntensity = 1.2f
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
        
        var waveformData = CalculateWaveformData(
            processedSpectrum,
            info,
            CurrentQualitySettings);

        if (!ValidateWaveformData(waveformData))
            return;

        RenderWaveformVisualization(
            canvas,
            waveformData,
            renderParams,
            passedInPaint,
            CurrentQualitySettings);
    }

    private WaveformData CalculateWaveformData(
        float[] spectrum,
        SKImageInfo info,
        QualitySettings settings)
    {
        float midY = info.Height * CENTER_PROPORTION;
        float xStep = info.Width / (float)Math.Max(1, spectrum.Length - 1);

        var paths = GetOrCreatePaths();
        UpdateWavePaths(
            paths,
            spectrum,
            midY,
            xStep,
            settings);

        List<AccentPoint>? accentPoints = null;
        if (settings.UseAccent)
        {
            accentPoints = FindAccentPoints(
                spectrum,
                midY,
                xStep);
        }

        return new WaveformData(
            Spectrum: spectrum,
            MidY: midY,
            XStep: xStep,
            Info: info,
            Paths: paths,
            AccentPoints: accentPoints);
    }

    private static bool ValidateWaveformData(WaveformData data) =>
        data.Spectrum.Length > 0 &&
        data.XStep > 0 &&
        data.Paths != null;

    private void RenderWaveformVisualization(
        SKCanvas canvas,
        WaveformData data,
        RenderParameters renderParams,
        SKPaint basePaint,
        QualitySettings settings)
    {
        RenderWithOverlay(canvas, () =>
        {
            if (settings.UseFill)
            {
                RenderFillLayer(
                    canvas,
                    data,
                    basePaint,
                    settings);
            }

            if (UseAdvancedEffects && settings.UseWaveShadow && !IsOverlayActive)
            {
                RenderWaveShadowLayer(
                    canvas,
                    data,
                    basePaint,
                    settings);
            }

            if (UseAdvancedEffects && settings.UseOutline)
            {
                RenderOutlineLayer(
                    canvas,
                    data,
                    basePaint,
                    settings);
            }

            RenderMainWaveform(
                canvas,
                data,
                basePaint,
                settings);

            if (UseAdvancedEffects && settings.UseMirror && !IsOverlayActive)
            {
                RenderMirrorLayer(
                    canvas,
                    data,
                    basePaint,
                    settings);
            }

            if (UseAdvancedEffects && settings.UseAccent && data.AccentPoints?.Count > 0)
            {
                RenderAccentLayer(
                    canvas,
                    data,
                    basePaint,
                    settings);
            }
        });
    }

    private WaveformPaths GetOrCreatePaths()
    {
        _cachedPaths ??= new WaveformPaths(
            TopPath: new SKPath(),
            BottomPath: new SKPath(),
            FillPath: new SKPath());

        return _cachedPaths;
    }

    private void UpdateWavePaths(
        WaveformPaths paths,
        float[] spectrum,
        float midY,
        float xStep,
        QualitySettings settings)
    {
        paths.TopPath.Reset();
        paths.BottomPath.Reset();
        paths.FillPath.Reset();

        if (spectrum.Length < 2)
        {
            if (spectrum.Length == 1)
            {
                float yPos = midY - spectrum[0] * midY;
                float xEnd = xStep * (spectrum.Length > 1 ? (spectrum.Length - 1) : 1);
                paths.TopPath.MoveTo(0, yPos);
                paths.TopPath.LineTo(xEnd, yPos);
                paths.BottomPath.MoveTo(0, midY + spectrum[0] * midY);
                paths.BottomPath.LineTo(xEnd, midY + spectrum[0] * midY);
            }
            return;
        }

        bool useCubic = ShouldUseCubicCurves(settings);

        BuildWaveformPaths(
            paths,
            spectrum,
            midY,
            xStep,
            useCubic,
            settings.CurveDefinition);
    }

    private bool ShouldUseCubicCurves(QualitySettings settings) =>
        settings.UseCubicCurves && (!IsOverlayActive || Quality == RenderQuality.High);

    private static void BuildWaveformPaths(
        WaveformPaths paths,
        float[] spectrum,
        float midY,
        float xStep,
        bool useCubic,
        int curveDefinition)
    {
        float startX = 0;
        float startTopY = midY - spectrum[0] * midY;
        float startBottomY = midY + spectrum[0] * midY;

        paths.TopPath.MoveTo(startX, startTopY);
        paths.BottomPath.MoveTo(startX, startBottomY);
        paths.FillPath.MoveTo(startX, startTopY);

        if (useCubic)
        {
            BuildSmoothCubicPaths(
                paths,
                spectrum,
                midY,
                xStep,
                curveDefinition);
        }
        else
        {
            BuildLinearPaths(
                paths,
                spectrum,
                midY,
                xStep);
        }

        CompleteFillPath(
            paths,
            spectrum,
            midY,
            xStep);
    }

    private static void BuildSmoothCubicPaths(
        WaveformPaths paths,
        float[] spectrum,
        float midY,
        float xStep,
        int curveDefinition)
    {
        float smoothFactor = curveDefinition switch
        {
            1 => 0.5f,
            2 => 0.33f,
            3 => 0.25f,
            _ => 0.33f
        };

        for (int i = 0; i < spectrum.Length - 1; i++)
        {
            float p0x = (i > 0 ? i - 1 : i) * xStep;
            float p0TopY = midY - spectrum[i > 0 ? i - 1 : i] * midY;
            float p0BottomY = midY + spectrum[i > 0 ? i - 1 : i] * midY;

            float p1x = i * xStep;
            float p1TopY = midY - spectrum[i] * midY;
            float p1BottomY = midY + spectrum[i] * midY;

            float p2x = (i + 1) * xStep;
            float p2TopY = midY - spectrum[i + 1] * midY;
            float p2BottomY = midY + spectrum[i + 1] * midY;

            float p3x = (i < spectrum.Length - 2 ? i + 2 : i + 1) * xStep;
            float p3TopY = midY - spectrum[i < spectrum.Length - 2 ? i + 2 : i + 1] * midY;
            float p3BottomY = midY + spectrum[i < spectrum.Length - 2 ? i + 2 : i + 1] * midY;

            float tension = 0.5f;

            float cp1TopX = p1x + (p2x - p0x) * tension * smoothFactor;
            float cp1TopY = p1TopY + (p2TopY - p0TopY) * tension * smoothFactor;
            float cp2TopX = p2x - (p3x - p1x) * tension * smoothFactor;
            float cp2TopY = p2TopY - (p3TopY - p1TopY) * tension * smoothFactor;

            paths.TopPath.CubicTo(
                cp1TopX, cp1TopY,
                cp2TopX, cp2TopY,
                p2x, p2TopY);

            paths.FillPath.CubicTo(
                cp1TopX, cp1TopY,
                cp2TopX, cp2TopY,
                p2x, p2TopY);

            float cp1BottomX = p1x + (p2x - p0x) * tension * smoothFactor;
            float cp1BottomY = p1BottomY + (p2BottomY - p0BottomY) * tension * smoothFactor;
            float cp2BottomX = p2x - (p3x - p1x) * tension * smoothFactor;
            float cp2BottomY = p2BottomY - (p3BottomY - p1BottomY) * tension * smoothFactor;

            paths.BottomPath.CubicTo(
                cp1BottomX, cp1BottomY,
                cp2BottomX, cp2BottomY,
                p2x, p2BottomY);
        }
    }

    private static void BuildLinearPaths(
        WaveformPaths paths,
        float[] spectrum,
        float midY,
        float xStep)
    {
        for (int i = 1; i < spectrum.Length; i++)
        {
            float x = i * xStep;
            float topY = midY - spectrum[i] * midY;
            float bottomY = midY + spectrum[i] * midY;

            paths.TopPath.LineTo(x, topY);
            paths.BottomPath.LineTo(x, bottomY);
            paths.FillPath.LineTo(x, topY);
        }
    }

    private static void CompleteFillPath(
        WaveformPaths paths,
        float[] spectrum,
        float midY,
        float xStep)
    {
        if (spectrum.Length == 0) return;

        float endX = (spectrum.Length - 1) * xStep;
        float endBottomY = midY + spectrum[^1] * midY;

        paths.FillPath.LineTo(endX, endBottomY);

        for (int i = spectrum.Length - 2; i >= 0; i--)
        {
            float x = i * xStep;
            float bottomY = midY + spectrum[i] * midY;
            paths.FillPath.LineTo(x, bottomY);
        }
        paths.FillPath.Close();
    }

    private List<AccentPoint> FindAccentPoints(
        float[] spectrum,
        float midY,
        float xStep)
    {
        var points = new List<AccentPoint>();
        int maxPoints = GetAdaptiveParameter(
            MAX_ACCENT_POINTS_BASE,
            MAX_ACCENT_POINTS_OVERLAY_BASE);

        for (int i = 0; i < spectrum.Length && points.Count < maxPoints; i++)
        {
            if (spectrum[i] > ACCENT_THRESHOLD)
            {
                points.Add(new AccentPoint(
                    X: i * xStep,
                    TopY: midY - spectrum[i] * midY,
                    BottomY: midY + spectrum[i] * midY,
                    Intensity: spectrum[i]));
            }
        }
        return points;
    }

    private void RenderFillLayer(
        SKCanvas canvas,
        WaveformData data,
        SKPaint basePaint,
        QualitySettings settings)
    {
        float fillOpacity = GetAdaptiveParameter(
            FILL_OPACITY_BASE,
            FILL_OPACITY_OVERLAY_BASE) * settings.FillOpacityMultiplier;

        SKPaint fillPaint;

        if (settings.UseGradientFill)
        {
            float fadeFactor = GetAdaptiveParameter(
                GRADIENT_FADE_FACTOR_BASE,
                GRADIENT_FADE_FACTOR_OVERLAY_BASE);

            using var gradient = SKShader.CreateLinearGradient(
                new SKPoint(0, data.MidY - data.MidY * 0.8f),
                new SKPoint(0, data.MidY + data.MidY * 0.8f),
                [
                    basePaint.Color.WithAlpha((byte)Clamp(255 * fillOpacity, 0, 255)),
                    basePaint.Color.WithAlpha((byte)Clamp(255 * fillOpacity * fadeFactor, 0, 255))
                ],
                CreateUniformGradientPositions(2),
                SKShaderTileMode.Clamp);

            fillPaint = CreatePaint(
                basePaint.Color,
                SKPaintStyle.Fill,
                gradient);
        }
        else
        {
            fillPaint = CreatePaint(
                basePaint.Color.WithAlpha((byte)Clamp(255 * fillOpacity, 0, 255)),
                SKPaintStyle.Fill);
        }

        try
        {
            canvas.DrawPath(data.Paths.FillPath, fillPaint);
        }
        finally
        {
            ReturnPaint(fillPaint);
        }
    }

    private void RenderWaveShadowLayer(
        SKCanvas canvas,
        WaveformData data,
        SKPaint basePaint,
        QualitySettings settings)
    {
        var shadowPaint = CreatePaint(
            SKColors.Black.WithAlpha((byte)(255 * SHADOW_ALPHA)),
            SKPaintStyle.Stroke);

        shadowPaint.StrokeWidth = CalculateStrokeWidth(
            data.Spectrum.Length,
            settings) * 1.1f;

        shadowPaint.StrokeCap = SKStrokeCap.Round;
        shadowPaint.StrokeJoin = SKStrokeJoin.Round;

        using (var blurFilter = SKMaskFilter.CreateBlur(
            SKBlurStyle.Normal,
            SHADOW_BLUR_RADIUS))
        {
            shadowPaint.MaskFilter = blurFilter;
        }

        try
        {
            canvas.Save();
            canvas.Translate(SHADOW_OFFSET_X, SHADOW_OFFSET_Y);
            canvas.DrawPath(data.Paths.TopPath, shadowPaint);
            canvas.DrawPath(data.Paths.BottomPath, shadowPaint);
            canvas.Restore();
        }
        finally
        {
            ReturnPaint(shadowPaint);
        }
    }

    private void RenderOutlineLayer(
        SKCanvas canvas,
        WaveformData data,
        SKPaint basePaint,
        QualitySettings settings)
    {
        float outlineAlpha = GetAdaptiveParameter(
            OUTLINE_ALPHA_BASE,
            OUTLINE_ALPHA_OVERLAY_BASE);

        float outlineMultiplier = GetAdaptiveParameter(
            OUTLINE_WIDTH_MULTIPLIER_BASE,
            OUTLINE_WIDTH_MULTIPLIER_OVERLAY_BASE);

        var outlinePaint = CreatePaint(
            basePaint.Color.WithAlpha((byte)Clamp(255 * outlineAlpha, 0, 255)),
            SKPaintStyle.Stroke);

        outlinePaint.StrokeWidth = CalculateStrokeWidth(
            data.Spectrum.Length,
            settings) * outlineMultiplier;

        outlinePaint.StrokeCap = SKStrokeCap.Round;
        outlinePaint.StrokeJoin = SKStrokeJoin.Round;

        try
        {
            canvas.DrawPath(data.Paths.TopPath, outlinePaint);
            canvas.DrawPath(data.Paths.BottomPath, outlinePaint);
        }
        finally
        {
            ReturnPaint(outlinePaint);
        }
    }

    private void RenderMainWaveform(
        SKCanvas canvas,
        WaveformData data,
        SKPaint basePaint,
        QualitySettings settings)
    {
        var wavePaint = CreatePaint(
            basePaint.Color,
            SKPaintStyle.Stroke);

        wavePaint.StrokeWidth = CalculateStrokeWidth(
            data.Spectrum.Length,
            settings);

        wavePaint.StrokeCap = SKStrokeCap.Round;
        wavePaint.StrokeJoin = SKStrokeJoin.Round;

        try
        {
            canvas.DrawPath(data.Paths.TopPath, wavePaint);
            canvas.DrawPath(data.Paths.BottomPath, wavePaint);
        }
        finally
        {
            ReturnPaint(wavePaint);
        }
    }

    private void RenderMirrorLayer(
        SKCanvas canvas,
        WaveformData data,
        SKPaint basePaint,
        QualitySettings settings)
    {
        var mirrorPaint = CreatePaint(
            basePaint.Color.WithAlpha((byte)Clamp(255 * MIRROR_ALPHA_BASE, 0, 255)),
            SKPaintStyle.Stroke);

        mirrorPaint.StrokeWidth = CalculateStrokeWidth(
            data.Spectrum.Length,
            settings) * 0.8f;

        mirrorPaint.StrokeCap = SKStrokeCap.Round;
        mirrorPaint.StrokeJoin = SKStrokeJoin.Round;

        try
        {
            canvas.Save();
            canvas.Scale(1, -1, 0, data.MidY);
            canvas.Translate(0, data.Info.Height * 0.01f);

            canvas.DrawPath(data.Paths.TopPath, mirrorPaint);
            canvas.DrawPath(data.Paths.BottomPath, mirrorPaint);

            canvas.Restore();
        }
        finally
        {
            ReturnPaint(mirrorPaint);
        }
    }

    private void RenderAccentLayer(
        SKCanvas canvas,
        WaveformData data,
        SKPaint basePaint,
        QualitySettings settings)
    {
        if (data.AccentPoints == null || data.AccentPoints.Count == 0)
            return;
        
        float accentAlpha = GetAdaptiveParameter(
            ACCENT_ALPHA_BASE,
            ACCENT_ALPHA_OVERLAY_BASE);
        float radiusBase = GetAdaptiveParameter(
            ACCENT_RADIUS_BASE,
            ACCENT_RADIUS_OVERLAY_BASE);

        if (settings.UseAccentGlow && UseAdvancedEffects)
        {
            RenderAccentGlow(
                canvas,
                data,
                basePaint,
                radiusBase,
                settings);
        }

        var accentPaint = CreatePaint(
            basePaint.Color.WithAlpha((byte)Clamp(255 * accentAlpha, 0, 255)),
            SKPaintStyle.Fill);

        try
        {
            foreach (var point in data.AccentPoints)
            {
                float radius = radiusBase * Normalize(point.Intensity, ACCENT_THRESHOLD, 1f);

                canvas.DrawCircle(
                    point.X,
                    point.TopY,
                    radius,
                    accentPaint);

                canvas.DrawCircle(
                    point.X,
                    point.BottomY,
                    radius,
                    accentPaint);
            }
        }
        finally
        {
            ReturnPaint(accentPaint);
        }
    }

    private void RenderAccentGlow(
        SKCanvas canvas,
        WaveformData data,
        SKPaint basePaint,
        float radiusBase,
        QualitySettings settings)
    {
        if (data.AccentPoints == null)
            return;
        
        float glowAlpha = ACCENT_GLOW_ALPHA_BASE * settings.AccentGlowIntensity;

        var glowPaint = CreatePaint(
            basePaint.Color.WithAlpha((byte)Clamp(255 * glowAlpha, 0, 255)),
            SKPaintStyle.Fill);

        using (var blurFilter = SKMaskFilter.CreateBlur(
            SKBlurStyle.Normal,
            radiusBase * ACCENT_GLOW_RADIUS_MULTIPLIER * 0.5f))
        {
            glowPaint.MaskFilter = blurFilter;
        }

        try
        {
            foreach (var point in data.AccentPoints)
            {
                float dynamicRadius = radiusBase * Normalize(point.Intensity, ACCENT_THRESHOLD, 1f);
                float glowEffectRadius = dynamicRadius * ACCENT_GLOW_RADIUS_MULTIPLIER;

                canvas.DrawCircle(
                    point.X,
                    point.TopY,
                    glowEffectRadius,
                    glowPaint);
                canvas.DrawCircle(
                    point.X,
                    point.BottomY,
                    glowEffectRadius,
                    glowPaint);
            }
        }
        finally
        {
            ReturnPaint(glowPaint);
        }
    }

    private float GetAdaptiveParameter(float normalValue, float overlayValue) =>
        IsOverlayActive ? overlayValue : normalValue;

    private int GetAdaptiveParameter(int normalValue, int overlayValue) =>
        IsOverlayActive ? overlayValue : normalValue;

    private float CalculateStrokeWidth(int spectrumLength, QualitySettings settings)
    {
        float minWidth = GetAdaptiveParameter(
            MIN_STROKE_WIDTH,
            MIN_STROKE_WIDTH_OVERLAY);

        float divisor = GetAdaptiveParameter(
            STROKE_WIDTH_DIVISOR_BASE,
            STROKE_WIDTH_DIVISOR_OVERLAY_BASE);

        float baseWidth = MathF.Max(minWidth, divisor / Max(1, spectrumLength));
        return baseWidth * settings.StrokeWidthMultiplier;
    }

    protected override int GetMaxBarsForQuality() => Quality switch
    {
        RenderQuality.Low => 128,
        RenderQuality.Medium => 256,
        RenderQuality.High => 512,
        _ => 256
    };

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();

        float smoothingFactor = Quality switch
        {
            RenderQuality.Low => 0.35f,
            RenderQuality.Medium => 0.28f,
            RenderQuality.High => 0.22f,
            _ => 0.28f
        };

        if (IsOverlayActive) smoothingFactor *= OVERLAY_SMOOTHING_FACTOR;

        SetProcessingSmoothingFactor(smoothingFactor);
        _cachedPaths = null;
        RequestRedraw();
    }

    protected override void OnDispose()
    {
        _cachedPaths?.TopPath?.Dispose();
        _cachedPaths?.BottomPath?.Dispose();
        _cachedPaths?.FillPath?.Dispose();
        _cachedPaths = null;
        base.OnDispose();
    }

    private record WaveformData(
        float[] Spectrum,
        float MidY,
        float XStep,
        SKImageInfo Info,
        WaveformPaths Paths,
        List<AccentPoint>? AccentPoints);

    private record WaveformPaths(
        SKPath TopPath,
        SKPath BottomPath,
        SKPath FillPath);

    private record AccentPoint(
        float X,
        float TopY,
        float BottomY,
        float Intensity);
}