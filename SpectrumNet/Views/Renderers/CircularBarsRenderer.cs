#nullable enable

using static SpectrumNet.Views.Renderers.CircularBarsRenderer.Constants;
using static System.MathF;

namespace SpectrumNet.Views.Renderers;

public sealed class CircularBarsRenderer : EffectSpectrumRenderer
{
    private static readonly Lazy<CircularBarsRenderer> _instance = new(() => new CircularBarsRenderer());

    private CircularBarsRenderer()
    {
        InitializeFields();
    }

    public static CircularBarsRenderer GetInstance() => _instance.Value;

    public record Constants
    {
        public const string LOG_PREFIX = "CircularBarsRenderer";

        public const float
            RADIUS_PROPORTION = 0.8f,
            INNER_RADIUS_FACTOR = 0.9f,
            BAR_SPACING_FACTOR = 0.7f,
            MIN_STROKE_WIDTH = 2f,
            SPECTRUM_MULTIPLIER = 0.5f,
            MAX_BAR_HEIGHT = 1.5f,
            MIN_BAR_HEIGHT = 0.01f,
            GLOW_RADIUS = 3f,
            GLOW_INTENSITY = 0.4f,
            GLOW_THRESHOLD = 0.6f,
            HIGHLIGHT_ALPHA = 0.7f,
            HIGHLIGHT_POSITION = 0.7f,
            HIGHLIGHT_INTENSITY = 0.5f,
            HIGHLIGHT_THRESHOLD = 0.4f;

        public const byte INNER_CIRCLE_ALPHA = 80;

        public const int
            PARALLEL_BATCH_SIZE = 32,
            DEFAULT_PATH_POOL_SIZE = 8;

        public static class Quality
        {
            public const bool
                LOW_USE_ADVANCED_EFFECTS = false,
                MEDIUM_USE_ADVANCED_EFFECTS = true,
                HIGH_USE_ADVANCED_EFFECTS = true;
        }
    }

    private Vector2[]? _barVectors;
    private int _previousBarCount;
    private new bool _useAntiAlias = true;
    private new bool _useAdvancedEffects = true;

    private static void InitializeFields() { }

    public override void Initialize() => 
        ExecuteSafely(PerformInitialization, "Initialize", "Failed to initialize renderer");

    private void PerformInitialization()
    {
        base.Initialize();
        if (_disposed) ResetRendererState();
        Log(LogLevel.Debug, LOG_PREFIX, "Initialized");
    }

    private void ResetRendererState() => _disposed = false;

    public override void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium) => 
        ExecuteSafely(() => PerformConfiguration(isOverlayActive, quality), "Configure", "Failed to configure renderer");

    private void PerformConfiguration(bool isOverlayActive, RenderQuality quality)
    {
        base.Configure(isOverlayActive, quality);
        if (_quality != quality) ApplyNewQuality(quality);
    }

    private void ApplyNewQuality(RenderQuality quality)
    {
        _quality = quality;
        ApplyQualitySettings();
    }

    protected override void ApplyQualitySettings() => 
        ExecuteSafely(ConfigureQualitySettings,
            "ApplyQualitySettings",
            "Failed to apply quality settings");

    private void ConfigureQualitySettings()
    {
        base.ApplyQualitySettings();
        switch (_quality)
        {
            case RenderQuality.Low:
                _useAntiAlias = false;
                _useAdvancedEffects = Constants.Quality.LOW_USE_ADVANCED_EFFECTS;
                break;
            case RenderQuality.Medium:
                _useAntiAlias = true;
                _useAdvancedEffects = Constants.Quality.MEDIUM_USE_ADVANCED_EFFECTS;
                break;
            case RenderQuality.High:
                _useAntiAlias = true;
                _useAdvancedEffects = Constants.Quality.HIGH_USE_ADVANCED_EFFECTS;
                break;
        }
        Log(LogLevel.Debug, LOG_PREFIX, $"Quality set to {_quality}");
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
        if (!AreRenderParametersValid(canvas, spectrum, info, paint)) return;
        ExecuteSafely(() => PerformRenderEffect(canvas, spectrum, info, barWidth, barSpacing, barCount, paint),
                      "RenderEffect", "Error during rendering");
    }

    private void PerformRenderEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint paint)
    {
        float centerX = info.Width / 2f;
        float centerY = info.Height / 2f;
        float mainRadius = MathF.Min(centerX, centerY) * RADIUS_PROPORTION;
        float adjustedBarWidth = AdjustBarWidthForBarCount(barWidth, barCount, Min(info.Width, info.Height));
        EnsureBarVectors(barCount);
        RenderCircularBars(canvas, spectrum, barCount, centerX, centerY, mainRadius, adjustedBarWidth, paint);
    }

    private static float AdjustBarWidthForBarCount(float barWidth, int barCount, float minDimension)
    {
        float maxPossibleWidth = 2 * MathF.PI * RADIUS_PROPORTION * minDimension / 2 / barCount * BAR_SPACING_FACTOR;
        return MathF.Max(MathF.Min(barWidth, maxPossibleWidth), MIN_STROKE_WIDTH);
    }

    private void RenderCircularBars(
        SKCanvas canvas,
        float[] spectrum,
        int barCount,
        float centerX,
        float centerY,
        float mainRadius,
        float barWidth,
        SKPaint basePaint)
    {
        using var innerCirclePaint = ConfigureInnerCirclePaint(basePaint, barWidth);
        canvas.DrawCircle(centerX, centerY, mainRadius * INNER_RADIUS_FACTOR, innerCirclePaint);

        if (_useAdvancedEffects)
        {
            RenderGlowEffects(canvas, spectrum, barCount, centerX, centerY, mainRadius, barWidth, basePaint);
        }
        RenderMainBars(canvas, spectrum, barCount, centerX, centerY, mainRadius, barWidth, basePaint);
        if (_useAdvancedEffects)
        {
            RenderHighlights(canvas, spectrum, barCount, centerX, centerY, mainRadius, barWidth, basePaint);
        }
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
        ExecuteSafely(() =>
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
        }, "EnsureBarVectors", "Error calculating bar vectors");
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
    }

    private SKPaint ConfigureGlowPaint(SKPaint basePaint, float barWidth)
    {
        var paint = _paintPool.Get();
        paint.IsAntialias = _useAntiAlias;
        paint.Style = SKPaintStyle.Stroke;
        paint.Color = basePaint.Color.WithAlpha((byte)(255 * GLOW_INTENSITY));
        paint.StrokeWidth = barWidth * 1.2f;
        paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, GLOW_RADIUS);
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
        using var batchPath = new SKPath();
        for (int i = 0; i < barCount; i++)
        {
            if (spectrum[i] < MIN_MAGNITUDE_THRESHOLD) continue;
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
        SKPaint basePaint)
    {
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
    }

    private SKPaint ConfigureHighlightPaint(float barWidth)
    {
        var paint = _paintPool.Get();
        paint.IsAntialias = _useAntiAlias;
        paint.Style = SKPaintStyle.Stroke;
        paint.Color = SKColors.White.WithAlpha((byte)(255 * HIGHLIGHT_INTENSITY));
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
        if (_barVectors == null) return;
        Vector2 vector = _barVectors[index];
        path.MoveTo(centerX + innerRadius * vector.X, centerY + innerRadius * vector.Y);
        path.LineTo(centerX + outerRadius * vector.X, centerY + outerRadius * vector.Y);
    }

    private bool AreRenderParametersValid(
        SKCanvas? canvas,
        float[]? spectrum,
        SKImageInfo info,
        SKPaint? paint)
    {
        if (!IsCanvasValid(canvas)) return false;
        if (!IsSpectrumValid(spectrum)) return false;
        if (!IsPaintValid(paint)) return false;
        if (!AreDimensionsValid(info)) return false;
        if (IsDisposed()) return false;
        return true;
    }

    private static bool IsCanvasValid(SKCanvas? canvas)
    {
        if (canvas != null) return true;
        Log(LogLevel.Error, LOG_PREFIX, "Canvas is null");
        return false;
    }

    private static bool IsSpectrumValid(float[]? spectrum)
    {
        if (spectrum != null && spectrum.Length > 0) return true;
        Log(LogLevel.Error, LOG_PREFIX, "Spectrum is null or empty");
        return false;
    }

    private static bool IsPaintValid(SKPaint? paint)
    {
        if (paint != null) return true;
        Log(LogLevel.Error, LOG_PREFIX, "Paint is null");
        return false;
    }

    private static bool AreDimensionsValid(SKImageInfo info)
    {
        if (info.Width > 0 && info.Height > 0) return true;
        Log(LogLevel.Error, LOG_PREFIX, $"Invalid image dimensions: {info.Width}x{info.Height}");
        return false;
    }

    private bool IsDisposed()
    {
        if (!_disposed) return false;
        Log(LogLevel.Error, LOG_PREFIX, "Renderer is disposed");
        return true;
    }

    public override void Dispose()
    {
        if (_disposed) return;
        ExecuteSafely(PerformDisposal, "Dispose", "Error during disposal");
        FinalizeDisposal();
    }

    private void PerformDisposal() => base.Dispose();

    private void FinalizeDisposal()
    {
        _disposed = true;
        Log(LogLevel.Debug, LOG_PREFIX, "Disposed");
    }
}