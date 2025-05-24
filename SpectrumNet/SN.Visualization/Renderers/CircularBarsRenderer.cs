#nullable enable

using static System.MathF;
using static SpectrumNet.SN.Visualization.Renderers.CircularBarsRenderer.Constants;

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class CircularBarsRenderer : EffectSpectrumRenderer
{
    private const string LogPrefix = nameof(CircularBarsRenderer);

    private static readonly Lazy<CircularBarsRenderer> _instance =
        new(() => new CircularBarsRenderer());

    private CircularBarsRenderer() { }

    public static CircularBarsRenderer GetInstance() => _instance.Value;

    public static class Constants
    {
        public const float
            RADIUS_PROPORTION = 0.8f,
            INNER_RADIUS_FACTOR = 0.9f,
            BAR_SPACING_FACTOR = 0.7f,
            MIN_STROKE_WIDTH = 2f,
            SPECTRUM_MULTIPLIER = 0.5f,
            MIN_MAGNITUDE_THRESHOLD_BAR = 0.01f,
            CENTER_PROPORTION = 0.5f,
            GLOW_THRESHOLD = 0.6f,
            HIGHLIGHT_POSITION = 0.7f,
            HIGHLIGHT_THRESHOLD = 0.4f;

        public const byte INNER_CIRCLE_ALPHA = 80;

        public static readonly Dictionary<RenderQuality, QualitySettings> QualityPresets = new()
        {
            [RenderQuality.Low] = new(
                UseGlow: false,
                UseHighlight: false,
                GlowRadius: 1.5f,
                GlowIntensity: 0.2f,
                HighlightIntensity: 0.3f
            ),
            [RenderQuality.Medium] = new(
                UseGlow: true,
                UseHighlight: true,
                GlowRadius: 3.0f,
                GlowIntensity: 0.4f,
                HighlightIntensity: 0.5f
            ),
            [RenderQuality.High] = new(
                UseGlow: true,
                UseHighlight: true,
                GlowRadius: 6.0f,
                GlowIntensity: 0.6f,
                HighlightIntensity: 0.7f
            )
        };

        public record QualitySettings(
            bool UseGlow,
            bool UseHighlight,
            float GlowRadius,
            float GlowIntensity,
            float HighlightIntensity
        );
    }

    private Vector2[]? _barVectors;
    private int _previousBarCount;
    private QualitySettings _currentSettings = QualityPresets[RenderQuality.Medium];

    protected override void OnInitialize()
    {
        base.OnInitialize();
        _barVectors = null;
        _previousBarCount = 0;
        _logger.Log(LogLevel.Debug, LogPrefix, "Initialized");
    }

    protected override void OnQualitySettingsApplied()
    {
        _currentSettings = QualityPresets[Quality];
        _logger.Log(LogLevel.Debug, LogPrefix, $"Quality changed to {Quality}");
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
            () => RenderCircularBars(canvas, spectrum, info, barWidth, barCount, paint),
            LogPrefix,
            "Error during rendering"
        );
    }

    private void RenderCircularBars(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        int barCount,
        SKPaint basePaint)
    {
        float centerX = info.Width * CENTER_PROPORTION;
        float centerY = info.Height * CENTER_PROPORTION;
        float mainRadius = MathF.Min(centerX, centerY) * RADIUS_PROPORTION;
        float adjustedBarWidth = AdjustBarWidthForBarCount(
            barWidth, barCount, Min(info.Width, info.Height));

        EnsureBarVectors(barCount);

        RenderInnerCircle(canvas, centerX, centerY, mainRadius, adjustedBarWidth, basePaint);

        if (UseAdvancedEffects && _currentSettings.UseGlow)
        {
            RenderGlowEffects(
                canvas, spectrum, barCount, centerX, centerY, mainRadius,
                adjustedBarWidth, basePaint);
        }

        RenderMainBars(
            canvas, spectrum, barCount, centerX, centerY, mainRadius,
            adjustedBarWidth, basePaint);

        if (UseAdvancedEffects && _currentSettings.UseHighlight)
        {
            RenderHighlights(
                canvas, spectrum, barCount, centerX, centerY, mainRadius,
                adjustedBarWidth);
        }
    }

    private static float AdjustBarWidthForBarCount(
        float barWidth,
        int barCount,
        float minDimension)
    {
        if (barCount <= 0) return MIN_STROKE_WIDTH;
        float maxPossibleWidth = 2 * MathF.PI * RADIUS_PROPORTION * minDimension / 2 / barCount * BAR_SPACING_FACTOR;
        return MathF.Max(MathF.Min(barWidth, maxPossibleWidth), MIN_STROKE_WIDTH);
    }

    private void RenderInnerCircle(
        SKCanvas canvas,
        float centerX,
        float centerY,
        float mainRadius,
        float barWidth,
        SKPaint basePaint)
    {
        using var innerCirclePaint = CreatePaint(
            basePaint.Color.WithAlpha(INNER_CIRCLE_ALPHA),
            SKPaintStyle.Stroke,
            barWidth * 0.5f
        );
        canvas.DrawCircle(centerX, centerY, mainRadius * INNER_RADIUS_FACTOR, innerCirclePaint);
    }

    private void EnsureBarVectors(int barCount)
    {
        if (_barVectors == null || _barVectors.Length != barCount || _previousBarCount != barCount)
        {
            _barVectors = new Vector2[barCount];
            float angleStep = 2 * MathF.PI / barCount;

            for (int i = 0; i < barCount; i++)
            {
                _barVectors[i] = new Vector2(Cos(angleStep * i), Sin(angleStep * i));
            }
            _previousBarCount = barCount;
        }
    }

    private void RenderGlowEffects(
        SKCanvas canvas,
        float[] spectrum,
        int barCount,
        float centerX,
        float centerY,
        float mainRadius,
        float barWidth,
        SKPaint basePaint)
    {
        var batchPath = _resourceManager.GetPath();
        try
        {
            for (int i = 0; i < barCount; i++)
            {
                if (spectrum[i] <= GLOW_THRESHOLD) continue;
                float radius = mainRadius + spectrum[i] * mainRadius * SPECTRUM_MULTIPLIER;
                AddBarToPath(batchPath, i, centerX, centerY, mainRadius, radius);
            }

            if (!batchPath.IsEmpty)
            {
                using var glowPaint = CreatePaint(
                    basePaint.Color.WithAlpha((byte)(255 * _currentSettings.GlowIntensity)),
                    SKPaintStyle.Stroke,
                    barWidth * 1.2f,
                    createBlur: true,
                    blurRadius: _currentSettings.GlowRadius
                );
                canvas.DrawPath(batchPath, glowPaint);
            }
        }
        finally
        {
            _resourceManager.ReturnPath(batchPath);
        }
    }

    private void RenderMainBars(
        SKCanvas canvas,
        float[] spectrum,
        int barCount,
        float centerX,
        float centerY,
        float mainRadius,
        float barWidth,
        SKPaint basePaint)
    {
        var batchPath = _resourceManager.GetPath();
        try
        {
            for (int i = 0; i < barCount; i++)
            {
                if (spectrum[i] < MIN_MAGNITUDE_THRESHOLD_BAR) continue;
                float radius = mainRadius + spectrum[i] * mainRadius * SPECTRUM_MULTIPLIER;
                AddBarToPath(batchPath, i, centerX, centerY, mainRadius, radius);
            }

            if (!batchPath.IsEmpty)
            {
                using var barPaint = CreatePaint(
                    basePaint.Color,
                    SKPaintStyle.Stroke,
                    barWidth,
                    strokeCap: SKStrokeCap.Round
                );
                canvas.DrawPath(batchPath, barPaint);
            }
        }
        finally
        {
            _resourceManager.ReturnPath(batchPath);
        }
    }

    private void RenderHighlights(
        SKCanvas canvas,
        float[] spectrum,
        int barCount,
        float centerX,
        float centerY,
        float mainRadius,
        float barWidth)
    {
        var batchPath = _resourceManager.GetPath();
        try
        {
            for (int i = 0; i < barCount; i++)
            {
                if (spectrum[i] <= HIGHLIGHT_THRESHOLD) continue;
                float radius = mainRadius + spectrum[i] * mainRadius * SPECTRUM_MULTIPLIER;
                float innerPoint = mainRadius + (radius - mainRadius) * HIGHLIGHT_POSITION;
                AddBarToPath(batchPath, i, centerX, centerY, innerPoint, radius);
            }

            if (!batchPath.IsEmpty)
            {
                using var highlightPaint = CreatePaint(
                    SKColors.White.WithAlpha((byte)(255 * _currentSettings.HighlightIntensity)),
                    SKPaintStyle.Stroke,
                    barWidth * 0.6f
                );
                canvas.DrawPath(batchPath, highlightPaint);
            }
        }
        finally
        {
            _resourceManager.ReturnPath(batchPath);
        }
    }

    private SKPaint CreatePaint(
        SKColor color,
        SKPaintStyle style,
        float strokeWidth,
        SKStrokeCap strokeCap = SKStrokeCap.Butt,
        bool createBlur = false,
        float blurRadius = 0)
    {
        var paint = _resourceManager.GetPaint();
        paint.Color = color;
        paint.Style = style;
        paint.IsAntialias = UseAntiAlias;
        paint.StrokeWidth = strokeWidth;
        paint.StrokeCap = strokeCap;

        if (createBlur && blurRadius > 0)
        {
            paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, blurRadius);
        }

        return paint;
    }

    private void AddBarToPath(
        SKPath path,
        int index,
        float centerX,
        float centerY,
        float innerRadius,
        float outerRadius)
    {
        if (_barVectors == null || index < 0 || index >= _barVectors.Length) return;

        Vector2 vector = _barVectors[index];
        path.MoveTo(centerX + innerRadius * vector.X, centerY + innerRadius * vector.Y);
        path.LineTo(centerX + outerRadius * vector.X, centerY + outerRadius * vector.Y);
    }

    protected override void OnDispose()
    {
        _barVectors = null;
        base.OnDispose();
        _logger.Log(LogLevel.Debug, LogPrefix, "Disposed");
    }
}