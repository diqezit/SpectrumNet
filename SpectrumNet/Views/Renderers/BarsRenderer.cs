#nullable enable

using static SpectrumNet.Views.Renderers.BarsRenderer.Constants;
using static SpectrumNet.Views.Renderers.BarsRenderer.Constants.Quality;
using static System.MathF;

namespace SpectrumNet.Views.Renderers;

public sealed class BarsRenderer : EffectSpectrumRenderer
{
    private static readonly Lazy<BarsRenderer> _instance = new(() => new BarsRenderer());

    private BarsRenderer() { }

    public static BarsRenderer GetInstance() => _instance.Value;

    public record Constants
    {
        public const string LOG_PREFIX = "BarsRenderer";

        public const float
            MAX_CORNER_RADIUS = 125f,
            DEFAULT_CORNER_RADIUS_FACTOR = 0.5f,
            MIN_BAR_HEIGHT = 1f,
            HIGHLIGHT_WIDTH_PROPORTION = 0.6f,
            HIGHLIGHT_HEIGHT_PROPORTION = 0.1f,
            MAX_HIGHLIGHT_HEIGHT = 5f,
            HIGHLIGHT_ALPHA_DIVISOR = 3f,
            ALPHA_MULTIPLIER = 1.5f,
            HIGH_INTENSITY_THRESHOLD = 0.6f,
            GLOW_EFFECT_ALPHA = 0.25f,
            GLOW_BLUR_RADIUS_LOW = 1.0f,
            GLOW_BLUR_RADIUS_MEDIUM = 2.0f,
            GLOW_BLUR_RADIUS_HIGH = 3.0f;

        public const int BATCH_SIZE = 32;

        public static class Quality
        {
            public const bool
                LOW_USE_ADVANCED_EFFECTS = false,
                LOW_USE_ANTI_ALIAS = false,
                LOW_USE_GLOW_EFFECT = false,
                LOW_USE_EDGE_EFFECT = false;

            public const bool
                MEDIUM_USE_ADVANCED_EFFECTS = true,
                MEDIUM_USE_ANTI_ALIAS = true,
                MEDIUM_USE_GLOW_EFFECT = true,
                MEDIUM_USE_EDGE_EFFECT = true;

            public const bool
                HIGH_USE_ADVANCED_EFFECTS = true,
                HIGH_USE_ANTI_ALIAS = true,
                HIGH_USE_GLOW_EFFECT = true,
                HIGH_USE_EDGE_EFFECT = true;

            public const float
                LOW_GLOW_RADIUS = GLOW_BLUR_RADIUS_LOW,
                MEDIUM_GLOW_RADIUS = GLOW_BLUR_RADIUS_MEDIUM,
                HIGH_GLOW_RADIUS = GLOW_BLUR_RADIUS_HIGH;

            public const float
                LOW_GLOW_ALPHA_QUALITY = GLOW_EFFECT_ALPHA * 0.5f,
                MEDIUM_GLOW_ALPHA_QUALITY = GLOW_EFFECT_ALPHA * 0.8f,
                HIGH_GLOW_ALPHA_QUALITY = GLOW_EFFECT_ALPHA;

            public const float
                LOW_INTENSITY_THRESHOLD_QUALITY = HIGH_INTENSITY_THRESHOLD * 1.2f,
                MEDIUM_INTENSITY_THRESHOLD_QUALITY = HIGH_INTENSITY_THRESHOLD * 1.05f,
                HIGH_INTENSITY_THRESHOLD_QUALITY = HIGH_INTENSITY_THRESHOLD;

            public const float
                LOW_ALPHA_MULTIPLIER = ALPHA_MULTIPLIER * 0.8f,
                MEDIUM_ALPHA_MULTIPLIER = ALPHA_MULTIPLIER,
                HIGH_ALPHA_MULTIPLIER = ALPHA_MULTIPLIER * 1.2f;

            public const float
               LOW_EDGE_STROKE_WIDTH = 0f,
               MEDIUM_EDGE_STROKE_WIDTH = 1.5f,
               HIGH_EDGE_STROKE_WIDTH = 2.5f;

            public const float
               LOW_EDGE_BLUR_RADIUS = 0f,
               MEDIUM_EDGE_BLUR_RADIUS = 1f,
               HIGH_EDGE_BLUR_RADIUS = 2f;
        }
    }

    private float _glowRadius;
    private bool _useGlowEffect;
    private volatile bool _isConfiguring;
    private float _glowAlpha;
    private float _intensityThreshold;
    private float _alphaMultiplier;
    private bool _useEdgeEffect;
    private float _edgeStrokeWidth;
    private float _edgeBlurRadius;


    private SKRect _lastRenderArea = SKRect.Empty;
    private SKMatrix _lastTransform = SKMatrix.Identity;

    protected override void OnInitialize()
    {
        ExecuteSafely(
            () =>
            {
                base.OnInitialize();
                ApplyQualitySettings();
                Log(LogLevel.Debug, LOG_PREFIX, "Initialized");
            },
            "OnInitialize",
            "Failed to initialize renderer"
        );
    }

    public override void Configure(
        bool isOverlayActive,
        RenderQuality quality = RenderQuality.Medium)
    {
        if (_isConfiguring) return;

        ExecuteSafely(
            () =>
            {
                try
                {
                    _isConfiguring = true;
                    bool configChanged = _isOverlayActive != isOverlayActive
                                         || Quality != quality;

                    _isOverlayActive = isOverlayActive;
                    Quality = quality;
                    _smoothingFactor = isOverlayActive ? 0.5f : 0.3f;

                    if (configChanged)
                    {
                        ApplyQualitySettings();
                        OnConfigurationChanged();
                    }
                }
                finally
                {
                    _isConfiguring = false;
                }
            },
            "Configure",
            "Failed to configure renderer"
        );
    }

    protected override void OnConfigurationChanged()
    {
        ExecuteSafely(
            () => { },
            "OnConfigurationChanged",
            "Failed to apply configuration changes"
        );
    }

    protected override void ApplyQualitySettings()
    {
        if (_isConfiguring) return;

        ExecuteSafely(
            () =>
            {
                try
                {
                    _isConfiguring = true;

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

                    _samplingOptions = QualityBasedSamplingOptions();

                    Log(LogLevel.Debug,
                        LOG_PREFIX,
                        $"Quality changed to {Quality}");
                }
                finally
                {
                    _isConfiguring = false;
                }
            },
            "ApplyQualitySettings",
            "Failed to apply quality settings"
        );
    }

    private void HighQualitySettings()
    {
        _useAntiAlias = HIGH_USE_ANTI_ALIAS;
        _useAdvancedEffects = HIGH_USE_ADVANCED_EFFECTS;
        _useGlowEffect = HIGH_USE_GLOW_EFFECT;
        _glowRadius = HIGH_GLOW_RADIUS;
        _glowAlpha = HIGH_GLOW_ALPHA_QUALITY;
        _intensityThreshold = HIGH_INTENSITY_THRESHOLD_QUALITY;
        _alphaMultiplier = HIGH_ALPHA_MULTIPLIER;
        _useEdgeEffect = HIGH_USE_EDGE_EFFECT;
        _edgeStrokeWidth = HIGH_EDGE_STROKE_WIDTH;
        _edgeBlurRadius = HIGH_EDGE_BLUR_RADIUS;
    }

    private void MediumQualitySettings()
    {
        _useAntiAlias = MEDIUM_USE_ANTI_ALIAS;
        _useAdvancedEffects = MEDIUM_USE_ADVANCED_EFFECTS;
        _useGlowEffect = MEDIUM_USE_GLOW_EFFECT;
        _glowRadius = MEDIUM_GLOW_RADIUS;
        _glowAlpha = MEDIUM_GLOW_ALPHA_QUALITY;
        _intensityThreshold = MEDIUM_INTENSITY_THRESHOLD_QUALITY;
        _alphaMultiplier = MEDIUM_ALPHA_MULTIPLIER;
        _useEdgeEffect = MEDIUM_USE_EDGE_EFFECT;
        _edgeStrokeWidth = MEDIUM_EDGE_STROKE_WIDTH;
        _edgeBlurRadius = MEDIUM_EDGE_BLUR_RADIUS;
    }

    private void LowQualitySettings()
    {
        _useAntiAlias = LOW_USE_ANTI_ALIAS;
        _useAdvancedEffects = LOW_USE_ADVANCED_EFFECTS;
        _useGlowEffect = LOW_USE_GLOW_EFFECT;
        _glowRadius = LOW_GLOW_RADIUS;
        _glowAlpha = LOW_GLOW_ALPHA_QUALITY;
        _intensityThreshold = LOW_INTENSITY_THRESHOLD_QUALITY;
        _alphaMultiplier = LOW_ALPHA_MULTIPLIER;
        _useEdgeEffect = LOW_USE_EDGE_EFFECT;
        _edgeStrokeWidth = LOW_EDGE_STROKE_WIDTH;
        _edgeBlurRadius = LOW_EDGE_BLUR_RADIUS;
    }

    protected override SKSamplingOptions QualityBasedSamplingOptions() =>
        Quality switch
        {
            RenderQuality.Low => new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None),
            RenderQuality.Medium => new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),
            RenderQuality.High => new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),
            _ => new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)
        };

    protected override void RenderEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint paint)
    {
        if (!ValidateRenderParameters(canvas, spectrum, info, paint, barCount)) return;

        ExecuteSafely(
            () =>
            {
                UpdateState(canvas, spectrum, info, barCount);
                RenderFrame(canvas, spectrum, info, barWidth, barSpacing, barCount, paint);
            },
            "RenderEffect",
            "Error during rendering"
        );
    }

    private bool ValidateRenderParameters(
        SKCanvas? canvas,
        float[]? spectrum,
        SKImageInfo info,
        SKPaint? paint,
        int barCount)
    {
        if (!IsCanvasValid(canvas)) return false;
        if (!IsSpectrumValid(spectrum)) return false;
        if (!IsPaintValid(paint)) return false;
        if (!AreDimensionsValid(info)) return false;
        if (barCount <= 0) return false;
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
        SKCanvas canvas,
        float[] _,
        SKImageInfo info,
        int ___)
    {
        if (canvas.TotalMatrix != _lastTransform)
        {
            _lastTransform = canvas.TotalMatrix;
        }

        bool canvasSizeChanged =
            MathF.Abs(_lastRenderArea.Width - info.Width) > 0.5f ||
            MathF.Abs(_lastRenderArea.Height - info.Height) > 0.5f;

        if (canvasSizeChanged)
        {
            _lastRenderArea = info.Rect;
        }
    }

    private void RenderFrame(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint basePaint)
    {
        float canvasHeight = info.Height;
        float cornerRadius = CalculateCornerRadius(barWidth);

        int batchSize = Constants.BATCH_SIZE;
        int spectrumLength = Min(barCount, spectrum.Length);

        for (int batchStart = 0; batchStart < spectrumLength; batchStart += batchSize)
        {
            int batchEnd = Min(batchStart + batchSize, spectrumLength);
            RenderBarsBatch(
                canvas,
                spectrum,
                batchStart,
                batchEnd,
                basePaint,
                barWidth,
                barSpacing,
                canvasHeight,
                cornerRadius);
        }
    }

    private void RenderBarsBatch(
        SKCanvas canvas,
        float[] spectrum,
        int startIndex,
        int endIndex,
        SKPaint basePaint,
        float barWidth,
        float barSpacing,
        float canvasHeight,
        float cornerRadius)
    {
        for (int i = startIndex; i < endIndex; i++)
        {
            float x = i * (barWidth + barSpacing);
            float barValue = spectrum[i];

            if (barValue < MIN_MAGNITUDE_THRESHOLD) continue;

            if (IsRenderAreaVisible(canvas, x, 0, barWidth, canvasHeight))
            {
                RenderSingleBar(
                    canvas,
                    i,
                    barValue,
                    barWidth,
                    barSpacing,
                    canvasHeight,
                    cornerRadius,
                    basePaint);
            }
        }
    }

    private void RenderSingleBar(
        SKCanvas canvas,
        int index,
        float magnitude,
        float barWidth,
        float barSpacing,
        float canvasHeight,
        float cornerRadius,
        SKPaint basePaint)
    {
        float barHeight = CalculateBarHeight(magnitude, canvasHeight);
        byte alpha = CalculateBarAlpha(magnitude);
        float x = index * (barWidth + barSpacing);

        using var barPaint = ConfigureBarPaint(basePaint, alpha);

        if (_useGlowEffect
            && _useAdvancedEffects
            && magnitude > _intensityThreshold)
        {
            using var glowPaint = ConfigureGlowPaint(magnitude);
            RenderGlowEffect(
                canvas,
                x,
                barWidth,
                barHeight,
                canvasHeight,
                cornerRadius,
                glowPaint);
        }

        RenderBar(
            canvas,
            x,
            barWidth,
            barHeight,
            canvasHeight,
            cornerRadius,
            barPaint);

        if (_useEdgeEffect
            && _useAdvancedEffects
            && barHeight > 0
            && _edgeStrokeWidth > 0)
        {
            using var edgePaint = ConfigureEdgePaint(alpha);
            RenderBarEdgeEffect(
                canvas,
                x,
                barWidth,
                barHeight,
                canvasHeight,
                cornerRadius,
                edgePaint);
        }
    }

    private SKPaint ConfigureBarPaint(SKPaint basePaint, byte alpha)
    {
        var paint = _paintPool.Get();
        paint.Color = basePaint.Color.WithAlpha(alpha);
        paint.IsAntialias = _useAntiAlias;
        return paint;
    }

    private SKPaint ConfigureGlowPaint(float magnitude)
    {
        var paint = _paintPool.Get();
        paint.Color = SKColors.White.WithAlpha((byte)(magnitude * 255f * _glowAlpha));
        paint.ImageFilter = _edgeBlurRadius > 0
            && _useAdvancedEffects ? SKImageFilter.CreateBlur(_glowRadius, _glowRadius) : null;

        paint.IsAntialias = _useAntiAlias;
        return paint;
    }

    private SKPaint ConfigureEdgePaint(byte barAlpha)
    {
        var paint = _paintPool.Get();
        paint.Color = SKColors.White.WithAlpha(barAlpha);
        paint.IsAntialias = _useAntiAlias;
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = _edgeStrokeWidth;
        paint.StrokeCap = SKStrokeCap.Round;
        paint.StrokeJoin = SKStrokeJoin.Round;

        paint.MaskFilter = _edgeBlurRadius > 0 && _useAdvancedEffects
            ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, _edgeBlurRadius)
            : null;

        return paint;
    }

    private static float CalculateCornerRadius(float barWidth) =>
        MathF.Min(barWidth * DEFAULT_CORNER_RADIUS_FACTOR, MAX_CORNER_RADIUS);

    private byte CalculateBarAlpha(float magnitude) =>
        (byte)MathF.Min(magnitude * _alphaMultiplier * 255f, 255f);

    private static float CalculateBarHeight(float magnitude, float canvasHeight) =>
        MathF.Max(magnitude * canvasHeight, MIN_BAR_HEIGHT);

    private static void DrawRoundedRect(
        SKCanvas canvas,
        float x,
        float y,
        float width,
        float height,
        float cornerRadius,
        SKPaint paint)
    {
        canvas.DrawRoundRect(
            new SKRect(x, y, x + width, y + height),
            cornerRadius,
            cornerRadius,
            paint);
    }

    private static void RenderGlowEffect(
        SKCanvas canvas,
        float x,
        float barWidth,
        float barHeight,
        float canvasHeight,
        float cornerRadius,
        SKPaint glowPaint)
    {
        DrawRoundedRect(
            canvas,
            x,
            canvasHeight - barHeight,
            barWidth,
            barHeight,
            cornerRadius,
            glowPaint);
    }

    private static void RenderBar(
        SKCanvas canvas,
        float x,
        float barWidth,
        float barHeight,
        float canvasHeight,
        float cornerRadius,
        SKPaint barPaint)
    {
        DrawRoundedRect(
            canvas,
            x,
            canvasHeight - barHeight,
            barWidth,
            barHeight,
            cornerRadius,
            barPaint);
    }

    private static void RenderBarEdgeEffect(
       SKCanvas canvas,
       float x,
       float barWidth,
       float barHeight,
       float canvasHeight,
       float cornerRadius,
       SKPaint edgePaint)
    {
        DrawRoundedRect(
           canvas,
           x,
           canvasHeight - barHeight,
           barWidth,
           barHeight,
           cornerRadius,
           edgePaint);
    }


    public override void Dispose()
    {
        if (_disposed) return;

        ExecuteSafely(
            () => OnDispose(),
            "Dispose",
            "Error during disposal"
        );

        _disposed = true;
        base.Dispose();
        GC.SuppressFinalize(this);

        Log(LogLevel.Debug, LOG_PREFIX, "Disposed");
    }

    protected override void OnDispose()
    {
        ExecuteSafely(
            () => base.OnDispose(),
            "OnDispose",
            "Error during specific disposal"
        );
    }
}