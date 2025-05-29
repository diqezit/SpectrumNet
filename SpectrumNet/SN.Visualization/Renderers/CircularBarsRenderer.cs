#nullable enable

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class CircularBarsRenderer : EffectSpectrumRenderer<CircularBarsRenderer.QualitySettings>
{
    private static readonly Lazy<CircularBarsRenderer> _instance =
        new(() => new CircularBarsRenderer());

    public static CircularBarsRenderer GetInstance() => _instance.Value;

    private SKPoint[]? _barDirections;

    public sealed class QualitySettings
    {
        public bool UseGlow { get; init; }
        public bool UseHighlight { get; init; }
        public float GlowIntensity { get; init; }
        public float HighlightIntensity { get; init; }
        public float InnerCircleAlpha { get; init; }
        public float BarSpacingRatio { get; init; }
        public float MinBarLength { get; init; }
    }

    protected override IReadOnlyDictionary<RenderQuality, QualitySettings>
        QualitySettingsPresets
    { get; } = new Dictionary<RenderQuality, QualitySettings>
    {
        [RenderQuality.Low] = new()
        {
            UseGlow = false,
            UseHighlight = false,
            GlowIntensity = 0f,
            HighlightIntensity = 0f,
            InnerCircleAlpha = 0.3f,
            BarSpacingRatio = 0.7f,
            MinBarLength = 0.02f
        },
        [RenderQuality.Medium] = new()
        {
            UseGlow = true,
            UseHighlight = false,
            GlowIntensity = 0.4f,
            HighlightIntensity = 0f,
            InnerCircleAlpha = 0.4f,
            BarSpacingRatio = 0.75f,
            MinBarLength = 0.03f
        },
        [RenderQuality.High] = new()
        {
            UseGlow = true,
            UseHighlight = true,
            GlowIntensity = 0.6f,
            HighlightIntensity = 0.5f,
            InnerCircleAlpha = 0.5f,
            BarSpacingRatio = 0.8f,
            MinBarLength = 0.04f
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

        var center = new SKPoint(info.Width * 0.5f, info.Height * 0.5f);
        float radius = Math.Min(center.X, center.Y) * 0.8f;

        EnsureBarDirections(renderParams.EffectiveBarCount);

        RenderCircularVisualization(
            canvas,
            processedSpectrum,
            center,
            radius,
            renderParams,
            passedInPaint);
    }

    private void EnsureBarDirections(int barCount)
    {
        if (_barDirections?.Length != barCount)
        {
            _barDirections = new SKPoint[barCount];
            float angleStep = 2 * MathF.PI / barCount;

            for (int i = 0; i < barCount; i++)
            {
                float angle = i * angleStep;
                _barDirections[i] = new SKPoint(
                    MathF.Cos(angle),
                    MathF.Sin(angle));
            }
        }
    }

    private void RenderCircularVisualization(
        SKCanvas canvas,
        float[] spectrum,
        SKPoint center,
        float radius,
        RenderParameters renderParams,
        SKPaint basePaint)
    {
        var settings = CurrentQualitySettings!;

        RenderWithOverlay(canvas, () =>
        {
            if (UseAdvancedEffects)
                RenderInnerCircle(canvas, center, radius, basePaint, settings);

            if (UseAdvancedEffects && settings.UseGlow)
                RenderGlowLayer(canvas, spectrum, center, radius, renderParams, basePaint, settings);

            RenderMainBars(canvas, spectrum, center, radius, renderParams, basePaint, settings);

            if (UseAdvancedEffects && settings.UseHighlight)
                RenderHighlightLayer(canvas, spectrum, center, radius, renderParams, settings);
        });
    }

    private void RenderInnerCircle(
        SKCanvas canvas,
        SKPoint center,
        float radius,
        SKPaint basePaint,
        QualitySettings settings)
    {
        byte alpha = CalculateAlpha(settings.InnerCircleAlpha);
        var circlePaint = CreatePaint(
            basePaint.Color.WithAlpha(alpha),
            SKPaintStyle.Stroke);

        circlePaint.StrokeWidth = 2f;

        try
        {
            canvas.DrawCircle(center, radius * 0.9f, circlePaint);
        }
        finally
        {
            ReturnPaint(circlePaint);
        }
    }

    private void RenderGlowLayer(
        SKCanvas canvas,
        float[] spectrum,
        SKPoint center,
        float radius,
        RenderParameters renderParams,
        SKPaint basePaint,
        QualitySettings settings)
    {
        byte glowAlpha = CalculateAlpha(settings.GlowIntensity);
        var glowPaint = CreatePaint(
            basePaint.Color.WithAlpha(glowAlpha),
            SKPaintStyle.Stroke);

        float barWidth = CalculateBarWidth(renderParams.EffectiveBarCount, radius, settings);
        glowPaint.StrokeWidth = barWidth * 1.5f;
        glowPaint.StrokeCap = SKStrokeCap.Round;

        using var blurFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3f);
        glowPaint.MaskFilter = blurFilter;

        try
        {
            RenderBarsPaths(
                canvas,
                spectrum,
                center,
                radius,
                glowPaint,
                settings,
                magnitudeThreshold: 0.6f);
        }
        finally
        {
            ReturnPaint(glowPaint);
        }
    }

    private void RenderMainBars(
        SKCanvas canvas,
        float[] spectrum,
        SKPoint center,
        float radius,
        RenderParameters renderParams,
        SKPaint basePaint,
        QualitySettings settings)
    {
        var barPaint = CreatePaint(basePaint.Color, SKPaintStyle.Stroke);
        float barWidth = CalculateBarWidth(renderParams.EffectiveBarCount, radius, settings);
        barPaint.StrokeWidth = barWidth;
        barPaint.StrokeCap = SKStrokeCap.Round;

        try
        {
            RenderBarsPaths(
                canvas,
                spectrum,
                center,
                radius,
                barPaint,
                settings,
                magnitudeThreshold: 0f);
        }
        finally
        {
            ReturnPaint(barPaint);
        }
    }

    private void RenderHighlightLayer(
        SKCanvas canvas,
        float[] spectrum,
        SKPoint center,
        float radius,
        RenderParameters renderParams,
        QualitySettings settings)
    {
        byte highlightAlpha = CalculateAlpha(settings.HighlightIntensity);
        var highlightPaint = CreatePaint(
            SKColors.White.WithAlpha(highlightAlpha),
            SKPaintStyle.Stroke);

        float barWidth = CalculateBarWidth(renderParams.EffectiveBarCount, radius, settings);
        highlightPaint.StrokeWidth = barWidth * 0.5f;
        highlightPaint.StrokeCap = SKStrokeCap.Round;

        try
        {
            RenderBarsPaths(
                canvas,
                spectrum,
                center,
                radius,
                highlightPaint,
                settings,
                magnitudeThreshold: 0.4f,
                startOffset: 0.7f);
        }
        finally
        {
            ReturnPaint(highlightPaint);
        }
    }

    private void RenderBarsPaths(
        SKCanvas canvas,
        float[] spectrum,
        SKPoint center,
        float radius,
        SKPaint paint,
        QualitySettings settings,
        float magnitudeThreshold = 0f,
        float startOffset = 0f)
    {
        if (_barDirections == null) return;

        RenderPath(canvas, path =>
        {
            for (int i = 0; i < _barDirections.Length && i < spectrum.Length; i++)
            {
                float magnitude = spectrum[i];
                if (magnitude <= magnitudeThreshold) continue;

                float barLength = Math.Max(
                    magnitude * radius * 0.5f,
                    radius * settings.MinBarLength);

                float innerRadius = radius;
                float outerRadius = radius + barLength;

                if (startOffset > 0)
                {
                    innerRadius = radius + barLength * startOffset;
                }

                var direction = _barDirections[i];

                path.MoveTo(
                    center.X + innerRadius * direction.X,
                    center.Y + innerRadius * direction.Y);

                path.LineTo(
                    center.X + outerRadius * direction.X,
                    center.Y + outerRadius * direction.Y);
            }
        }, paint);
    }

    private static float CalculateBarWidth(
        int barCount,
        float radius,
        QualitySettings settings)
    {
        if (barCount <= 0) return 2f;

        float circumference = 2 * MathF.PI * radius;
        float maxWidth = circumference / barCount * settings.BarSpacingRatio;

        return Math.Clamp(maxWidth, 2f, 20f);
    }

    protected override void OnDispose()
    {
        _barDirections = null;
        base.OnDispose();
    }

    protected override int GetMaxBarsForQuality()
    {
        return Quality switch
        {
            RenderQuality.Low => 64,
            RenderQuality.Medium => 128,
            RenderQuality.High => 256,
            _ => 128
        };
    }

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();

        float smoothingFactor = Quality switch
        {
            RenderQuality.Low => 0.35f,
            RenderQuality.Medium => 0.25f,
            RenderQuality.High => 0.2f,
            _ => 0.25f
        };

        SetProcessingSmoothingFactor(smoothingFactor);
        RequestRedraw();
    }
}