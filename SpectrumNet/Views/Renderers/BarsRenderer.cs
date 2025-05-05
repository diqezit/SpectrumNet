#nullable enable

using static SpectrumNet.Views.Renderers.BarsRenderer.Constants;
using static System.MathF;

namespace SpectrumNet.Views.Renderers;

public sealed class BarsRenderer : EffectSpectrumRenderer
{
    private static readonly Lazy<BarsRenderer> _instance = new(() => new BarsRenderer());

    private float _glowRadius = GLOW_BLUR_RADIUS_MEDIUM;
    private bool _useGlowEffect = true;
    private new bool _useAntiAlias = true;
    private new bool _useAdvancedEffects = true;

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
                MEDIUM_USE_ADVANCED_EFFECTS = true,
                HIGH_USE_ADVANCED_EFFECTS = true;
        }
    }

    public override void Initialize()
    {
        ExecuteSafely(PerformInitialization,
                      "Initialize",
                      "Failed to initialize renderer");
    }

    private void PerformInitialization()
    {
        base.Initialize();
        if (_disposed) ResetRendererState();
        Log(LogLevel.Debug, LOG_PREFIX, "Initialized");
    }

    private void ResetRendererState()
    {
        _disposed = false;
    }

    public override void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium)
    {
        ExecuteSafely(() => PerformConfiguration(isOverlayActive, quality),
                      "Configure",
                      "Failed to configure renderer");
    }

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

    protected override void ApplyQualitySettings()
    {
        ExecuteSafely(ConfigureQualitySettings,
                      "ApplyQualitySettings",
                      "Failed to apply quality settings");
    }

    private void ConfigureQualitySettings()
    {
        base.ApplyQualitySettings();
        switch (_quality)
        {
            case RenderQuality.Low:
                SetLowQualitySettings();
                break;
            case RenderQuality.Medium:
                SetMediumQualitySettings();
                break;
            case RenderQuality.High:
                SetHighQualitySettings();
                break;
        }
    }

    private void SetLowQualitySettings()
    {
        _useAntiAlias = false;
        _useAdvancedEffects = Constants.Quality.LOW_USE_ADVANCED_EFFECTS;
        _useGlowEffect = false;
        _glowRadius = GLOW_BLUR_RADIUS_LOW;
    }

    private void SetMediumQualitySettings()
    {
        _useAntiAlias = true;
        _useAdvancedEffects = Constants.Quality.MEDIUM_USE_ADVANCED_EFFECTS;
        _useGlowEffect = true;
        _glowRadius = GLOW_BLUR_RADIUS_MEDIUM;
    }

    private void SetHighQualitySettings()
    {
        _useAntiAlias = true;
        _useAdvancedEffects = Constants.Quality.HIGH_USE_ADVANCED_EFFECTS;
        _useGlowEffect = true;
        _glowRadius = GLOW_BLUR_RADIUS_HIGH;
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
                      "RenderEffect",
                      "Error during rendering");
    }

    private void PerformRenderEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int _, // barcount
        SKPaint paint)
    {
        float canvasHeight = info.Height;
        float cornerRadius = CalculateCornerRadius(barWidth);
        RenderBars(canvas, spectrum, barWidth, barSpacing, canvasHeight, cornerRadius, paint);
    }

    private void RenderBars(
        SKCanvas canvas,
        float[] spectrum,
        float barWidth,
        float barSpacing,
        float canvasHeight,
        float cornerRadius,
        SKPaint basePaint)
    {
        for (int i = 0; i < spectrum.Length; i++)
        {
            float magnitude = spectrum[i];
            if (magnitude < MIN_MAGNITUDE_THRESHOLD) continue;
            float x = i * (barWidth + barSpacing);
            if (IsRenderAreaVisible(canvas, x, 0, barWidth, canvasHeight))
            {
                RenderSingleBar(canvas,
                    i,
                    magnitude,
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
            RenderGlowEffect(canvas, x, barWidth, barHeight, canvasHeight, cornerRadius, glowPaint);
        }
        RenderBar(canvas, x, barWidth, barHeight, canvasHeight, cornerRadius, barPaint);
        if (barHeight > cornerRadius * 2 && _quality != RenderQuality.Low)
        {
            using var highlightPaint = ConfigureHighlightPaint(barPaint.Color.Alpha);
            RenderBarHighlight(canvas, x, barWidth, barHeight, canvasHeight, highlightPaint);
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
        DrawRoundedRect(canvas,
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
        DrawRoundedRect(canvas,
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
        canvas.DrawRect(highlightX, canvasHeight - barHeight, highlightWidth, highlightHeight, highlightPaint);
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
        canvas.DrawRoundRect(new SKRect(x, y, x + width, y + height), cornerRadius, cornerRadius, paint);
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