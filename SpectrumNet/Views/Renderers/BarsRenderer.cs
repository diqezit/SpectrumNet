#nullable enable

using static SpectrumNet.Views.Renderers.BarsRenderer.Constants;
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
            MAX_CORNER_RADIUS = 10f,
            DEFAULT_CORNER_RADIUS_FACTOR = 0.05f,
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
                LOW_USE_GLOW_EFFECT = false;

            public const bool
                MEDIUM_USE_ADVANCED_EFFECTS = true,
                MEDIUM_USE_ANTI_ALIAS = true,
                MEDIUM_USE_GLOW_EFFECT = true;

            public const bool
                HIGH_USE_ADVANCED_EFFECTS = true,
                HIGH_USE_ANTI_ALIAS = true,
                HIGH_USE_GLOW_EFFECT = true;

            public const float
                LOW_GLOW_RADIUS = GLOW_BLUR_RADIUS_LOW,
                MEDIUM_GLOW_RADIUS = GLOW_BLUR_RADIUS_MEDIUM,
                HIGH_GLOW_RADIUS = GLOW_BLUR_RADIUS_HIGH;
        }
    }

    private float _glowRadius;
    private bool _useGlowEffect;
    private new bool _useAntiAlias;
    private new bool _useAdvancedEffects;

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
        ExecuteSafely(
            () =>
            {
                bool configChanged = _isOverlayActive != isOverlayActive || Quality != quality;
                base.Configure(isOverlayActive, quality);

                if (configChanged)
                {
                    OnConfigurationChanged();
                }
            },
            "Configure",
            "Failed to configure renderer"
        );
    }

    protected override void OnConfigurationChanged()
    {
        ExecuteSafely(
            () =>
            {
            },
            "OnConfigurationChanged",
            "Failed to apply configuration changes"
        );
    }

    protected override void ApplyQualitySettings()
    {
        ExecuteSafely(
            () =>
            {
                base.ApplyQualitySettings();
                ApplyQualityBasedSettings();
                Log(LogLevel.Debug, LOG_PREFIX, $"Quality changed to {Quality}");
            },
            "ApplyQualitySettings",
            "Failed to apply quality settings"
        );
    }

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
        _useGlowEffect = Constants.Quality.LOW_USE_GLOW_EFFECT;
        _glowRadius = Constants.Quality.LOW_GLOW_RADIUS;
    }

    private void ApplyMediumQualitySettings()
    {
        _useAntiAlias = Constants.Quality.MEDIUM_USE_ANTI_ALIAS;
        _useAdvancedEffects = Constants.Quality.MEDIUM_USE_ADVANCED_EFFECTS;
        _useGlowEffect = Constants.Quality.MEDIUM_USE_GLOW_EFFECT;
        _glowRadius = Constants.Quality.MEDIUM_GLOW_RADIUS;
    }

    private void ApplyHighQualitySettings()
    {
        _useAntiAlias = Constants.Quality.HIGH_USE_ANTI_ALIAS;
        _useAdvancedEffects = Constants.Quality.HIGH_USE_ADVANCED_EFFECTS;
        _useGlowEffect = Constants.Quality.HIGH_USE_GLOW_EFFECT;
        _glowRadius = Constants.Quality.HIGH_GLOW_RADIUS;
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

        if (_useGlowEffect && _useAdvancedEffects && magnitude > HIGH_INTENSITY_THRESHOLD)
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

        if (barHeight > cornerRadius * 2 && Quality != RenderQuality.Low)
        {
            using var highlightPaint = ConfigureHighlightPaint(barPaint.Color.Alpha);
            RenderBarHighlight(
                canvas,
                x,
                barWidth,
                barHeight,
                canvasHeight,
                highlightPaint);
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
        paint.Color = SKColors.White.WithAlpha((byte)(magnitude * 255f * GLOW_EFFECT_ALPHA));
        paint.ImageFilter = SKImageFilter.CreateBlur(_glowRadius, _glowRadius);
        paint.IsAntialias = _useAntiAlias;
        return paint;
    }

    private SKPaint ConfigureHighlightPaint(byte baseAlpha)
    {
        var paint = _paintPool.Get();
        paint.Color = SKColors.White.WithAlpha((byte)(baseAlpha / HIGHLIGHT_ALPHA_DIVISOR));
        paint.IsAntialias = _useAntiAlias;
        return paint;
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

    private static void RenderBarHighlight(
        SKCanvas canvas,
        float x,
        float barWidth,
        float barHeight,
        float canvasHeight,
        SKPaint highlightPaint)
    {
        float highlightWidth = barWidth * HIGHLIGHT_WIDTH_PROPORTION;
        float highlightHeight = MathF.Min(barHeight * HIGHLIGHT_HEIGHT_PROPORTION, MAX_HIGHLIGHT_HEIGHT);
        float highlightX = x + (barWidth - highlightWidth) / 2;

        canvas.DrawRect(
            highlightX,
            canvasHeight - barHeight,
            highlightWidth,
            highlightHeight,
            highlightPaint);
    }

    private static float CalculateCornerRadius(float barWidth) =>
        MathF.Min(barWidth * DEFAULT_CORNER_RADIUS_FACTOR, MAX_CORNER_RADIUS);

    private static float CalculateBarHeight(float magnitude, float canvasHeight) =>
        MathF.Max(magnitude * canvasHeight, MIN_BAR_HEIGHT);

    private static byte CalculateBarAlpha(float magnitude) =>
        (byte)MathF.Min(magnitude * ALPHA_MULTIPLIER * 255f, 255f);

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

    public override void Dispose()
    {
        if (_disposed) return;
        ExecuteSafely(
            () =>
            {
                OnDispose();
            },
            "Dispose",
            "Error during disposal"
        );
        _disposed = true;
        GC.SuppressFinalize(this);
        Log(LogLevel.Debug, LOG_PREFIX, "Disposed");
    }

    protected override void OnDispose()
    {
        ExecuteSafely(
            () =>
            {
                base.OnDispose();
            },
            "OnDispose",
            "Error during specific disposal"
        );
    }
}