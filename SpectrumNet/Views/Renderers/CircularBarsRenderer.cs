#nullable enable

using static SpectrumNet.Views.Renderers.CircularBarsRenderer.Constants;
using static System.MathF;

namespace SpectrumNet.Views.Renderers;

public sealed class CircularBarsRenderer : EffectSpectrumRenderer
{
    private static readonly Lazy<CircularBarsRenderer> _instance = new(() => new CircularBarsRenderer());

    private CircularBarsRenderer() { }

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
            HIGHLIGHT_POSITION = 0.7f,
            HIGHLIGHT_INTENSITY = 0.5f,
            HIGHLIGHT_THRESHOLD = 0.4f;

        public const byte INNER_CIRCLE_ALPHA = 80;

        public static class Quality
        {
            public const bool
                LOW_USE_ADVANCED_EFFECTS = false,
                LOW_USE_ANTI_ALIAS = false;

            public const bool
                MEDIUM_USE_ADVANCED_EFFECTS = true,
                MEDIUM_USE_ANTI_ALIAS = true;

            public const bool
                HIGH_USE_ADVANCED_EFFECTS = true,
                HIGH_USE_ANTI_ALIAS = true;
        }
    }

    private Vector2[]? _barVectors;
    private int _previousBarCount;

    protected override void OnInitialize()
    {
        base.OnInitialize();
        _barVectors = null;
        _previousBarCount = 0;
        Log(LogLevel.Debug, LOG_PREFIX, "Initialized");
    }

    public override void Configure(
        bool isOverlayActive,
        RenderQuality quality = RenderQuality.Medium) =>
        ExecuteSafely(() =>
        {
            base.Configure(isOverlayActive, quality);
            bool overlayChanged = _isOverlayActive != isOverlayActive;
            _isOverlayActive = isOverlayActive;
        }, nameof(Configure), "Failed to configure renderer");

    protected override void OnConfigurationChanged() =>
        base.OnConfigurationChanged();

    protected override void ApplyQualitySettings() =>
        ExecuteSafely(() =>
        {
            if (_isApplyingQuality) return;
            try
            {
                _isApplyingQuality = true;
                base.ApplyQualitySettings();
                switch (Quality)
                {
                    case RenderQuality.Low:
                        ApplyLowQualitySettings();
                        break;
                    case RenderQuality.Medium:
                        ApplyMediumQualitySettings();
                        break;
                    case RenderQuality.High:
                        ApplyHighQualitySettings();
                        break;
                }
            }
            finally
            {
                _isApplyingQuality = false;
            }
        }, nameof(ApplyQualitySettings), "Failed to apply quality settings");

    private void ApplyLowQualitySettings()
    {
        _useAntiAlias = Constants.Quality.LOW_USE_ANTI_ALIAS;
        _useAdvancedEffects = Constants.Quality.LOW_USE_ADVANCED_EFFECTS;
    }

    private void ApplyMediumQualitySettings()
    {
        _useAntiAlias = Constants.Quality.MEDIUM_USE_ANTI_ALIAS;
        _useAdvancedEffects = Constants.Quality.MEDIUM_USE_ADVANCED_EFFECTS;
    }

    private void ApplyHighQualitySettings()
    {
        _useAntiAlias = Constants.Quality.HIGH_USE_ANTI_ALIAS;
        _useAdvancedEffects = Constants.Quality.HIGH_USE_ADVANCED_EFFECTS;
    }

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();
        Log(LogLevel.Debug, LOG_PREFIX, $"Quality settings applied. New Quality: {Quality}");
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
        EnsureBarVectors(barCount);
        float centerX = info.Width / 2f;
        float centerY = info.Height / 2f;
        float mainRadius = MathF.Min(centerX, centerY) * RADIUS_PROPORTION;
        float adjustedBarWidth = AdjustBarWidthForBarCount(barWidth, barCount, MathF.Min(info.Width, info.Height));

        RenderCircularBarsInternal(canvas, spectrum, barCount, centerX, centerY, mainRadius, adjustedBarWidth, paint);
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
        SKPaint basePaint) =>
        ExecuteSafely(() =>
        {
            RenderInnerCircle(canvas, centerX, centerY, mainRadius, barWidth, basePaint);

            if (_useAdvancedEffects)
            {
                RenderGlowEffects(canvas, spectrum, barCount, centerX, centerY, mainRadius, barWidth, basePaint);
            }

            RenderMainBars(canvas, spectrum, barCount, centerX, centerY, mainRadius, barWidth, basePaint);

            if (_useAdvancedEffects)
            {
                RenderHighlights(canvas, spectrum, barCount, centerX, centerY, mainRadius, barWidth, basePaint);
            }
        }, nameof(RenderCircularBarsInternal), "Error rendering circular bars");

    private void RenderInnerCircle(
        SKCanvas canvas,
        float centerX,
        float centerY,
        float mainRadius,
        float barWidth,
        SKPaint basePaint) =>
        ExecuteSafely(() =>
        {
            using var innerCirclePaint = ConfigureInnerCirclePaint(basePaint, barWidth);
            canvas.DrawCircle(centerX, centerY, mainRadius * INNER_RADIUS_FACTOR, innerCirclePaint);
        }, nameof(RenderInnerCircle), "Error rendering inner circle");

    private SKPaint ConfigureInnerCirclePaint(SKPaint basePaint, float barWidth)
    {
        var paint = _paintPool.Get();
        paint.IsAntialias = _useAntiAlias;
        paint.Style = SKPaintStyle.Stroke;
        paint.Color = basePaint.Color.WithAlpha(INNER_CIRCLE_ALPHA);
        paint.StrokeWidth = barWidth * 0.5f;
        return paint;
    }

    private void EnsureBarVectors(int barCount) =>
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
        }, nameof(EnsureBarVectors), "Error calculating bar vectors");

    private void RenderGlowEffects(
        SKCanvas canvas,
        float[] spectrum,
        int barCount,
        float centerX,
        float centerY,
        float mainRadius,
        float barWidth,
        SKPaint basePaint) =>
        ExecuteSafely(() =>
        {
            if (!_useAdvancedEffects) return;
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
        }, nameof(RenderGlowEffects), "Error rendering glow effects");

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
        SKPaint basePaint) =>
        ExecuteSafely(() =>
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
        }, nameof(RenderMainBars), "Error rendering main bars");

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
        SKPaint _) =>
        ExecuteSafely(() =>
        {
            if (!_useAdvancedEffects) return;
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
        }, nameof(RenderHighlights), "Error rendering highlights");

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
        float outerRadius) =>
        ExecuteSafely(() =>
        {
            if (_barVectors == null || index < 0 || index >= _barVectors.Length) return;
            Vector2 vector = _barVectors[index];
            path.MoveTo(centerX + innerRadius * vector.X, centerY + innerRadius * vector.Y);
            path.LineTo(centerX + outerRadius * vector.X, centerY + outerRadius * vector.Y);
        }, nameof(AddBarToPath), "Error adding bar to path");

    public override void Dispose()
    {
        if (_disposed) return;
        ExecuteSafely(() =>
        {
            OnDispose();
            base.Dispose();
            _disposed = true;
        }, nameof(Dispose), "Error during disposal");
        GC.SuppressFinalize(this);
        Log(LogLevel.Debug, LOG_PREFIX, "Disposed");
    }

    protected override void OnDispose() =>
        ExecuteSafely(() =>
        {
            DisposeManagedResources();
            base.OnDispose();
        }, nameof(OnDispose), "Error during specific disposal");

    private void DisposeManagedResources() => _barVectors = null;

    protected override void OnInvalidateCachedResources()
    {
        base.OnInvalidateCachedResources();
        _barVectors = null;
        _previousBarCount = 0;
    }
}