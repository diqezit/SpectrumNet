#nullable enable

using static System.MathF;

namespace SpectrumNet.Views.Renderers;

public sealed class BarsRenderer : EffectSpectrumRenderer
{
    private static class Constants
    {
        public const string
            LOG_PREFIX = "BarsRenderer";

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

        public const int
            BATCH_SIZE = 32;
    }

    public readonly record struct RenderConfig(float BarWidth, float BarSpacing, int BarCount);

    private static readonly Lazy<BarsRenderer> _instance = new(() => new BarsRenderer());

    private float _glowRadius = Constants.GLOW_BLUR_RADIUS_MEDIUM;
    private bool _useGlowEffect = true;

    private BarsRenderer() { }

    public static BarsRenderer GetInstance() => _instance.Value;

    protected override void OnQualitySettingsApplied()
    {
        Safe(
           () =>
           {
               base.OnQualitySettingsApplied();
               switch (base.Quality)
               {
                   case RenderQuality.Low:
                       _useGlowEffect = false;
                       _glowRadius = Constants.GLOW_BLUR_RADIUS_LOW;
                       break;

                   case RenderQuality.Medium:
                       _useGlowEffect = true;
                       _glowRadius = Constants.GLOW_BLUR_RADIUS_MEDIUM;
                       break;

                   case RenderQuality.High:
                       _useGlowEffect = true;
                       _glowRadius = Constants.GLOW_BLUR_RADIUS_HIGH;
                       break;
               }

               Log(LogLevel.Debug, Constants.LOG_PREFIX, $"Quality set to {base.Quality}");
           },
           new ErrorHandlingOptions
           {
               Source = $"{GetType().Name}.OnQualitySettingsApplied",
               ErrorMessage = "Failed to apply bars specific quality settings"
           }
       );
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
        float totalBarWidth = barWidth + barSpacing;
        float canvasHeight = info.Height;
        float cornerRadius = CalculateCornerRadius(barWidth);

        for (int i = 0; i < spectrum.Length; i++)
        {
            float magnitude = spectrum[i];
            if (magnitude < MIN_MAGNITUDE_THRESHOLD) continue;

            float barHeight = CalculateBarHeight(magnitude, canvasHeight);
            byte alpha = CalculateBarAlpha(magnitude);

            using var barPaint = _paintPool.Get();
            barPaint.Color = paint.Color.WithAlpha(alpha);
            barPaint.IsAntialias = UseAntiAlias;

            float x = i * totalBarWidth;

            if (IsRenderAreaVisible(canvas, x, 0, barWidth, canvasHeight))
            {
                RenderBarWithEffects(
                    canvas,
                    x,
                    barWidth,
                    barHeight,
                    canvasHeight,
                    cornerRadius,
                    barPaint,
                    magnitude);
            }
        }
    }

    private void RenderBarWithEffects(
        SKCanvas canvas,
        float x,
        float barWidth,
        float barHeight,
        float canvasHeight,
        float cornerRadius,
        SKPaint barPaint,
        float magnitude)
    {
        if (_useGlowEffect && UseAdvancedEffects &&
            magnitude > Constants.HIGH_INTENSITY_THRESHOLD)
        {
            RenderGlowEffect(canvas, x, barWidth, barHeight, canvasHeight, cornerRadius, magnitude);
        }

        RenderBar(canvas, x, barWidth, barHeight, canvasHeight, cornerRadius, barPaint);

        if (barHeight > cornerRadius * 2 && base.Quality != RenderQuality.Low)
        {
            RenderBarHighlight(
                canvas,
                x,
                barWidth,
                barHeight,
                canvasHeight,
                barPaint.Color.Alpha);
        }
    }

    private void RenderGlowEffect(
        SKCanvas canvas,
        float x,
        float barWidth,
        float barHeight,
        float canvasHeight,
        float cornerRadius,
        float magnitude)
    {
        using var glowPaint = CreateGlowPaint(
            SKColors.White,
            _glowRadius,
            (byte)(magnitude * 255f * Constants.GLOW_EFFECT_ALPHA));

        DrawRoundedRect(
            canvas,
            x,
            canvasHeight - barHeight,
            barWidth,
            barHeight,
            cornerRadius,
            glowPaint);
    }

    private void RenderBar(
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

    private void RenderBarHighlight(
        SKCanvas canvas,
        float x,
        float barWidth,
        float barHeight,
        float canvasHeight,
        byte baseAlpha)
    {
        float highlightWidth = barWidth * Constants.HIGHLIGHT_WIDTH_PROPORTION;
        float highlightHeight = MathF.Min(
            barHeight * Constants.HIGHLIGHT_HEIGHT_PROPORTION,
            Constants.MAX_HIGHLIGHT_HEIGHT);

        float highlightX = x + (barWidth - highlightWidth) / 2;

        using var highlightPaint = _paintPool.Get();
        highlightPaint.Color = SKColors.White.WithAlpha(
            (byte)(baseAlpha / Constants.HIGHLIGHT_ALPHA_DIVISOR));
        highlightPaint.IsAntialias = UseAntiAlias;

        canvas.DrawRect(
            highlightX,
            canvasHeight - barHeight,
            highlightWidth,
            highlightHeight,
            highlightPaint);
    }

    [MethodImpl(AggressiveInlining)]
    private static float CalculateCornerRadius(float barWidth) =>
        MathF.Min(barWidth * Constants.DEFAULT_CORNER_RADIUS_FACTOR,
            Constants.MAX_CORNER_RADIUS);

    [MethodImpl(AggressiveInlining)]
    private static float CalculateBarHeight(float magnitude, float canvasHeight) =>
        MathF.Max(magnitude * canvasHeight, Constants.MIN_BAR_HEIGHT);

    [MethodImpl(AggressiveInlining)]
    private static byte CalculateBarAlpha(float magnitude) =>
        (byte)MathF.Min(magnitude * Constants.ALPHA_MULTIPLIER * 255f, 255f);

    [MethodImpl(AggressiveInlining)]
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

    protected override void OnDispose()
    {
        Safe(() =>
        {
            base.OnDispose();
        },
        new ErrorHandlingOptions
        {
            Source = $"{GetType().Name}.OnDispose",
            ErrorMessage = "Error during CircularBarsRenderer disposal"
        });
    }
}