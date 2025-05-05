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
                LOW_USE_ANTI_ALIAS = false,

                MEDIUM_USE_ADVANCED_EFFECTS = true,
                MEDIUM_USE_ANTI_ALIAS = true,

                HIGH_USE_ADVANCED_EFFECTS = true,
                HIGH_USE_ANTI_ALIAS = true;
        }
    }

    // Rendering resources
    private Vector2[]? _barVectors;
    private int _previousBarCount;

    // Quality settings
    private new bool _useAntiAlias;
    private new bool _useAdvancedEffects;
    private new bool _isOverlayActive;

    protected override void OnInitialize() =>
        ExecuteSafely(
            () =>
            {
                base.OnInitialize();
                _barVectors = null;
                _previousBarCount = 0;
                ApplyQualityBasedSettings();
                Log(LogLevel.Debug, LOG_PREFIX, "Initialized");
            },
            nameof(OnInitialize),
            "Failed to initialize renderer"
        );

    public override void Configure(
        bool isOverlayActive,
        RenderQuality quality = RenderQuality.Medium) =>
        ExecuteSafely(
            () =>
            {
                bool configChanged = _isOverlayActive != isOverlayActive || Quality != quality;
                base.Configure(isOverlayActive, quality);

                _isOverlayActive = isOverlayActive;

                if (configChanged)
                {
                    Log(LogLevel.Debug,
                        LOG_PREFIX,
                        $"Configuration changed. New Quality: {Quality}");
                    OnConfigurationChanged();
                }
            },
            nameof(Configure),
            "Failed to configure renderer"
        );

    protected override void OnConfigurationChanged() =>
        ExecuteSafely(
            () =>
            {
                base.OnConfigurationChanged();
                // any additional configuration changes here
            },
            nameof(OnConfigurationChanged),
            "Failed to apply configuration changes"
        );

    protected override void OnQualitySettingsApplied() =>
        ExecuteSafely(
            () =>
            {
                base.OnQualitySettingsApplied();
                ApplyQualityBasedSettings();
                Log(LogLevel.Debug, LOG_PREFIX, $"Quality settings applied. New Quality: {Quality}");
            },
            nameof(OnQualitySettingsApplied),
            "Failed to apply specific quality settings"
        );

    private void ApplyQualityBasedSettings()
    {
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

    protected override void RenderEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint paint) =>
        ExecuteSafely(
            () =>
            {
                if (!ValidateRenderParameters(canvas, spectrum, info, paint))
                    return;

                UpdateState(canvas, spectrum, info, barCount);
                RenderFrame(canvas, spectrum, info, barWidth, barSpacing, barCount, paint);
            },
            nameof(RenderEffect),
            "Error during rendering"
        );

    private bool ValidateRenderParameters(
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

    private void UpdateState(
        SKCanvas ___,
        float[] __,
        SKImageInfo _,
        int barCount) =>
        ExecuteSafely(
            () =>
            {
                EnsureBarVectors(barCount);
            },
            nameof(UpdateState),
            "Error during state update"
        );

    private void RenderFrame(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float _,
        int barCount,
        SKPaint paint) =>
        ExecuteSafely(
            () =>
            {
                float centerX = info.Width / 2f;
                float centerY = info.Height / 2f;
                float mainRadius = MathF.Min(centerX, centerY) * RADIUS_PROPORTION;
                float adjustedBarWidth = AdjustBarWidthForBarCount(barWidth, barCount, Min(info.Width, info.Height));

                RenderCircularBars(canvas, spectrum, barCount, centerX, centerY, mainRadius, adjustedBarWidth, paint);
            },
            nameof(RenderFrame),
            "Error during frame rendering"
        );

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
        SKPaint basePaint) =>
        ExecuteSafely(
            () =>
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
            },
            nameof(RenderCircularBars),
            "Error rendering circular bars"
        );

    private void RenderInnerCircle(
        SKCanvas canvas,
        float centerX,
        float centerY,
        float mainRadius,
        float barWidth,
        SKPaint basePaint) =>
        ExecuteSafely(
            () =>
            {
                using var innerCirclePaint = ConfigureInnerCirclePaint(basePaint, barWidth);
                canvas.DrawCircle(centerX, centerY, mainRadius * INNER_RADIUS_FACTOR, innerCirclePaint);
            },
            nameof(RenderInnerCircle),
            "Error rendering inner circle"
        );

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
        ExecuteSafely(
            () =>
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
            },
            nameof(EnsureBarVectors),
            "Error calculating bar vectors"
        );

    private void RenderGlowEffects(
        SKCanvas canvas,
        float[] spectrum,
        int barCount,
        float centerX,
        float centerY,
        float mainRadius,
        float barWidth,
        SKPaint basePaint) =>
        ExecuteSafely(
            () =>
            {
                using var batchPath = new SKPath();

                for (int i = 0; i < barCount; i++)
                {
                    if (spectrum[i] <= GLOW_THRESHOLD)
                        continue;

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
            },
            nameof(RenderGlowEffects),
            "Error rendering glow effects"
        );

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
        ExecuteSafely(
            () =>
            {
                using var batchPath = new SKPath();

                for (int i = 0; i < barCount; i++)
                {
                    if (spectrum[i] < MIN_MAGNITUDE_THRESHOLD)
                        continue;

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
            },
            nameof(RenderMainBars),
            "Error rendering main bars"
        );

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
        ExecuteSafely(
            () =>
            {
                using var batchPath = new SKPath();

                for (int i = 0; i < barCount; i++)
                {
                    if (spectrum[i] <= HIGHLIGHT_THRESHOLD)
                        continue;

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
            },
            nameof(RenderHighlights),
            "Error rendering highlights"
        );

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
        ExecuteSafely(
            () =>
            {
                if (_barVectors == null) return;

                Vector2 vector = _barVectors[index];
                path.MoveTo(centerX + innerRadius * vector.X, centerY + innerRadius * vector.Y);
                path.LineTo(centerX + outerRadius * vector.X, centerY + outerRadius * vector.Y);
            },
            nameof(AddBarToPath),
            "Error adding bar to path"
        );

    public override void Dispose()
    {
        if (_disposed) return;

        ExecuteSafely(
            () =>
            {
                OnDispose();
            },
            nameof(Dispose),
            "Error during disposal"
        );

        _disposed = true;
        GC.SuppressFinalize(this);
        Log(LogLevel.Debug, LOG_PREFIX, "Disposed");
    }

    protected override void OnDispose() =>
        ExecuteSafely(
            () =>
            {
                DisposeManagedResources();
                base.OnDispose();
            },
            nameof(OnDispose),
            "Error during specific disposal"
        );

    private void DisposeManagedResources()
    {
        _barVectors = null;
    }
}