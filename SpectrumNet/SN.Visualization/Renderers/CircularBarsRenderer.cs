#nullable enable

using static SpectrumNet.SN.Visualization.Renderers.CircularBarsRenderer.Constants;
using static SpectrumNet.SN.Visualization.Renderers.CircularBarsRenderer.Constants.Quality;
using static System.MathF;

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class CircularBarsRenderer : EffectSpectrumRenderer
{
    private const string LogPrefix = nameof(CircularBarsRenderer);

    private static readonly Lazy<CircularBarsRenderer> _instance = new(() => new CircularBarsRenderer());

    private CircularBarsRenderer() { }

    public static CircularBarsRenderer GetInstance() => _instance.Value;

    public record Constants
    {
        public const float
            RADIUS_PROPORTION = 0.8f,
            INNER_RADIUS_FACTOR = 0.9f,
            BAR_SPACING_FACTOR = 0.7f,
            MIN_STROKE_WIDTH = 2f,
            SPECTRUM_MULTIPLIER = 0.5f,
            MAX_BAR_HEIGHT = 1.5f,
            MIN_BAR_HEIGHT = 0.01f,
            MIN_MAGNITUDE_THRESHOLD_BAR = 0.01f,
            CENTER_PROPORTION = 0.5f,
            BASE_COLOR_FACTOR = 1f;

        public const byte INNER_CIRCLE_ALPHA = 80;
        public const byte MAX_ALPHA_BYTE = 255;

        public static class Quality
        {
            // Константы для эффектов в зависимости от качества
            public const float
                LOW_GLOW_RADIUS = 1.5f,
                MEDIUM_GLOW_RADIUS = 3.0f,
                HIGH_GLOW_RADIUS = 6.0f,

                LOW_GLOW_INTENSITY = 0.2f,
                MEDIUM_GLOW_INTENSITY = 0.4f,
                HIGH_GLOW_INTENSITY = 0.6f,

                LOW_HIGHLIGHT_INTENSITY = 0.3f,
                MEDIUM_HIGHLIGHT_INTENSITY = 0.5f,
                HIGH_HIGHLIGHT_INTENSITY = 0.7f,

                GLOW_THRESHOLD = 0.6f,
                HIGHLIGHT_POSITION = 0.7f,
                HIGHLIGHT_THRESHOLD = 0.4f;

            // Параметры настроек качества
            public const bool
                LOW_USE_ADVANCED_EFFECTS = false,
                LOW_USE_ANTI_ALIAS = false,
                LOW_USE_GLOW_EFFECT = false,
                LOW_USE_HIGHLIGHT_EFFECT = false;

            public const bool
                MEDIUM_USE_ADVANCED_EFFECTS = true,
                MEDIUM_USE_ANTI_ALIAS = true,
                MEDIUM_USE_GLOW_EFFECT = true,
                MEDIUM_USE_HIGHLIGHT_EFFECT = true;

            public const bool
                HIGH_USE_ADVANCED_EFFECTS = true,
                HIGH_USE_ANTI_ALIAS = true,
                HIGH_USE_GLOW_EFFECT = true,
                HIGH_USE_HIGHLIGHT_EFFECT = true;
        }
    }

    private Vector2[]? _barVectors;
    private int _previousBarCount;

    // Настройки качества
    private float _glowRadius;
    private float _glowIntensity;
    private float _highlightIntensity;
    private bool _useGlowEffect;
    private bool _useHighlightEffect;

    protected override void OnInitialize()
    {
        base.OnInitialize();
        _barVectors = null;
        _previousBarCount = 0;
        _logger.Log(LogLevel.Debug, LogPrefix, "Initialized");
    }

    protected override void OnConfigurationChanged()
    {
        _logger.Log(LogLevel.Information, LogPrefix,
            $"Configuration changed. New Quality: {Quality}, AntiAlias: {_useAntiAlias}, " +
            $"AdvancedEffects: {_useAdvancedEffects}, " +
            $"GlowEffect: {_useGlowEffect}, HighlightEffect: {_useHighlightEffect}");
    }

    protected override void OnQualitySettingsApplied()
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

        _logger.Log(LogLevel.Information, LogPrefix,
            $"Quality settings applied: {Quality}, AntiAlias: {_useAntiAlias}, " +
            $"AdvancedEffects: {_useAdvancedEffects}, GlowRadius: {_glowRadius}, " +
            $"GlowEffect: {_useGlowEffect}, HighlightEffect: {_useHighlightEffect}");
    }

    private void LowQualitySettings()
    {
        _useAntiAlias = LOW_USE_ANTI_ALIAS;
        _useAdvancedEffects = LOW_USE_ADVANCED_EFFECTS;
        _useGlowEffect = LOW_USE_GLOW_EFFECT;
        _useHighlightEffect = LOW_USE_HIGHLIGHT_EFFECT;
        _glowRadius = LOW_GLOW_RADIUS;
        _glowIntensity = LOW_GLOW_INTENSITY;
        _highlightIntensity = LOW_HIGHLIGHT_INTENSITY;
    }

    private void MediumQualitySettings()
    {
        _useAntiAlias = MEDIUM_USE_ANTI_ALIAS;
        _useAdvancedEffects = MEDIUM_USE_ADVANCED_EFFECTS;
        _useGlowEffect = MEDIUM_USE_GLOW_EFFECT;
        _useHighlightEffect = MEDIUM_USE_HIGHLIGHT_EFFECT;
        _glowRadius = MEDIUM_GLOW_RADIUS;
        _glowIntensity = MEDIUM_GLOW_INTENSITY;
        _highlightIntensity = MEDIUM_HIGHLIGHT_INTENSITY;
    }

    private void HighQualitySettings()
    {
        _useAntiAlias = HIGH_USE_ANTI_ALIAS;
        _useAdvancedEffects = HIGH_USE_ADVANCED_EFFECTS;
        _useGlowEffect = HIGH_USE_GLOW_EFFECT;
        _useHighlightEffect = HIGH_USE_HIGHLIGHT_EFFECT;
        _glowRadius = HIGH_GLOW_RADIUS;
        _glowIntensity = HIGH_GLOW_INTENSITY;
        _highlightIntensity = HIGH_HIGHLIGHT_INTENSITY;
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
            () =>
            {
                float centerX = info.Width * CENTER_PROPORTION;
                float centerY = info.Height * CENTER_PROPORTION;
                float mainRadius = MathF.Min(centerX, centerY) * RADIUS_PROPORTION;
                float adjustedBarWidth = AdjustBarWidthForBarCount(barWidth, barCount, Min(info.Width, info.Height));

                EnsureBarVectors(barCount);
                RenderCircularBarsInternal(canvas, spectrum, barCount, centerX,
                    centerY, mainRadius, adjustedBarWidth, paint);
            },
            LogPrefix,
            "Error during rendering"
        );
    }

    private static float AdjustBarWidthForBarCount(float barWidth, int barCount, float minDimension)
    {
        if (barCount <= 0) return MIN_STROKE_WIDTH;
        float maxPossibleWidth = 2 * MathF.PI * RADIUS_PROPORTION * minDimension / 2 / barCount * BAR_SPACING_FACTOR;
        return MathF.Max(MathF.Min(barWidth, maxPossibleWidth), MIN_STROKE_WIDTH);
    }

    private void RenderCircularBarsInternal(
        SKCanvas canvas,
        float[] spectrum,
        int barCount,
        float centerX,
        float centerY,
        float mainRadius,
        float barWidth,
        SKPaint basePaint)
    {
        _logger.Safe(() =>
        {
            // Рендеринг внутреннего круга (всегда)
            RenderInnerCircle(canvas, centerX, centerY, mainRadius, barWidth, basePaint);

            // Рендеринг свечения (только если включены продвинутые эффекты и свечение)
            if (_useAdvancedEffects && _useGlowEffect)
            {
                RenderGlowEffects(canvas, spectrum, barCount, centerX, centerY, mainRadius, barWidth, basePaint);
            }

            // Рендеринг основных полос (всегда)
            RenderMainBars(canvas, spectrum, barCount, centerX, centerY, mainRadius, barWidth, basePaint);

            // Рендеринг подсветки (только если включены продвинутые эффекты и подсветка)
            if (_useAdvancedEffects && _useHighlightEffect)
            {
                RenderHighlights(canvas, spectrum, barCount, centerX, centerY, mainRadius, barWidth, basePaint);
            }
        }, LogPrefix, "Error rendering circular bars");
    }

    private void RenderInnerCircle(
        SKCanvas canvas,
        float centerX,
        float centerY,
        float mainRadius,
        float barWidth,
        SKPaint basePaint)
    {
        _logger.Safe(() =>
        {
            using var innerCirclePaint = ConfigureInnerCirclePaint(basePaint, barWidth);
            canvas.DrawCircle(centerX, centerY, mainRadius * INNER_RADIUS_FACTOR, innerCirclePaint);
        }, LogPrefix, "Error rendering inner circle");
    }

    private SKPaint ConfigureInnerCirclePaint(SKPaint basePaint, float barWidth)
    {
        var paint = _paintPool.Get();
        paint.IsAntialias = _useAntiAlias;
        paint.Style = SKPaintStyle.Stroke;
        paint.Color = basePaint.Color.WithAlpha(INNER_CIRCLE_ALPHA);
        paint.StrokeWidth = barWidth * 0.5f;
        return paint;
    }

    private void EnsureBarVectors(int barCount)
    {
        _logger.Safe(() =>
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
                _logger.Log(LogLevel.Debug, LogPrefix, $"Bar vectors cache created with {barCount} bars");
            }
        }, LogPrefix, "Error calculating bar vectors");
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
        _logger.Safe(() =>
        {
            if (!_useAdvancedEffects || !_useGlowEffect) return;

            using var batchPath = new SKPath();
            for (int i = 0; i < barCount; i++)
            {
                if (spectrum[i] <= GLOW_THRESHOLD) continue;
                float radius = mainRadius + spectrum[i] * mainRadius * SPECTRUM_MULTIPLIER;
                using var path = _pathPool.Get();
                AddBarToPath(path, i, centerX, centerY, mainRadius, radius);
                batchPath.AddPath(path);
            }

            if (!batchPath.IsEmpty)
            {
                using var glowPaint = ConfigureGlowPaint(basePaint, barWidth);
                canvas.DrawPath(batchPath, glowPaint);
            }
        }, LogPrefix, "Error rendering glow effects");
    }

    private SKPaint ConfigureGlowPaint(SKPaint basePaint, float barWidth)
    {
        var paint = _paintPool.Get();
        paint.IsAntialias = _useAntiAlias;
        paint.Style = SKPaintStyle.Stroke;
        paint.Color = basePaint.Color.WithAlpha((byte)(MAX_ALPHA_BYTE * _glowIntensity));
        paint.StrokeWidth = barWidth * 1.2f;
        paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, _glowRadius);
        return paint;
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
        _logger.Safe(() =>
        {
            using var batchPath = new SKPath();
            for (int i = 0; i < barCount; i++)
            {
                if (spectrum[i] < MIN_MAGNITUDE_THRESHOLD_BAR) continue;
                float radius = mainRadius + spectrum[i] * mainRadius * SPECTRUM_MULTIPLIER;
                using var path = _pathPool.Get();
                AddBarToPath(path, i, centerX, centerY, mainRadius, radius);
                batchPath.AddPath(path);
            }

            if (!batchPath.IsEmpty)
            {
                using var barPaint = ConfigureBarPaint(basePaint, barWidth);
                canvas.DrawPath(batchPath, barPaint);
            }
        }, LogPrefix, "Error rendering main bars");
    }

    private SKPaint ConfigureBarPaint(SKPaint basePaint, float barWidth)
    {
        var paint = _paintPool.Get();
        paint.IsAntialias = _useAntiAlias;
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeCap = SKStrokeCap.Round;
        paint.Color = basePaint.Color;
        paint.StrokeWidth = barWidth;
        return paint;
    }

    private void RenderHighlights(
        SKCanvas canvas,
        float[] spectrum,
        int barCount,
        float centerX,
        float centerY,
        float mainRadius,
        float barWidth,
        SKPaint _)
    {
        _logger.Safe(() =>
        {
            if (!_useAdvancedEffects || !_useHighlightEffect) return;

            using var batchPath = new SKPath();
            for (int i = 0; i < barCount; i++)
            {
                if (spectrum[i] <= HIGHLIGHT_THRESHOLD) continue;
                float radius = mainRadius + spectrum[i] * mainRadius * SPECTRUM_MULTIPLIER;
                float innerPoint = mainRadius + (radius - mainRadius) * HIGHLIGHT_POSITION;
                using var path = _pathPool.Get();
                AddBarToPath(path, i, centerX, centerY, innerPoint, radius);
                batchPath.AddPath(path);
            }

            if (!batchPath.IsEmpty)
            {
                using var highlightPaint = ConfigureHighlightPaint(barWidth);
                canvas.DrawPath(batchPath, highlightPaint);
            }
        }, LogPrefix, "Error rendering highlights");
    }

    private SKPaint ConfigureHighlightPaint(float barWidth)
    {
        var paint = _paintPool.Get();
        paint.IsAntialias = _useAntiAlias;
        paint.Style = SKPaintStyle.Stroke;
        paint.Color = SKColors.White.WithAlpha((byte)(MAX_ALPHA_BYTE * _highlightIntensity));
        paint.StrokeWidth = barWidth * 0.6f;
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
        _logger.Safe(() =>
        {
            if (_barVectors == null || index < 0 || index >= _barVectors.Length) return;
            Vector2 vector = _barVectors[index];
            path.MoveTo(centerX + innerRadius * vector.X, centerY + innerRadius * vector.Y);
            path.LineTo(centerX + outerRadius * vector.X, centerY + outerRadius * vector.Y);
        }, LogPrefix, "Error adding bar to path");
    }

    protected override void OnInvalidateCachedResources()
    {
        base.OnInvalidateCachedResources();
        _barVectors = null;
        _previousBarCount = 0;
        _logger.Log(LogLevel.Debug, LogPrefix, "Cached resources invalidated");
    }

    protected override void OnDispose()
    {
        _barVectors = null;
        base.OnDispose();
        _logger.Log(LogLevel.Debug, LogPrefix, "Disposed");
    }
}