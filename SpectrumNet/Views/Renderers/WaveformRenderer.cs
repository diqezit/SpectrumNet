#nullable enable

using static SpectrumNet.Views.Renderers.WaveformRenderer.Constants;
using static SpectrumNet.Views.Renderers.WaveformRenderer.Constants.Quality;

namespace SpectrumNet.Views.Renderers;

public sealed class WaveformRenderer : EffectSpectrumRenderer
{
    private static readonly Lazy<WaveformRenderer> _instance = new(() => new WaveformRenderer());

    public static WaveformRenderer GetInstance() => _instance.Value;

    public record Constants
    {
        public const string LOG_PREFIX = "WaveformRenderer";

        public const float
            SMOOTHING_FACTOR_NORMAL = 0.3f,
            SMOOTHING_FACTOR_OVERLAY = 0.5f,
            MAX_SPECTRUM_VALUE = 1.5f,
            MIN_STROKE_WIDTH = 2.0f,
            GLOW_INTENSITY = 0.4f,
            GLOW_RADIUS_LOW = 1.5f,
            GLOW_RADIUS_MEDIUM = 3.0f,
            GLOW_RADIUS_HIGH = 4.5f,
            HIGHLIGHT_ALPHA = 0.7f,
            HIGH_AMPLITUDE_THRESHOLD = 0.6f,
            FILL_OPACITY = 0.25f;

        public const int BATCH_SIZE = 64;

        public static class Quality
        {
            public const bool
                LOW_USE_ANTI_ALIAS = false,
                MEDIUM_USE_ANTI_ALIAS = true,
                HIGH_USE_ANTI_ALIAS = true;

            public const bool
                LOW_USE_ADVANCED_EFFECTS = false,
                MEDIUM_USE_ADVANCED_EFFECTS = true,
                HIGH_USE_ADVANCED_EFFECTS = true;

            public const float
                LOW_GLOW_RADIUS = GLOW_RADIUS_LOW,
                MEDIUM_GLOW_RADIUS = GLOW_RADIUS_MEDIUM,
                HIGH_GLOW_RADIUS = GLOW_RADIUS_HIGH;

            public const int
                LOW_SMOOTHING_PASSES = 1,
                MEDIUM_SMOOTHING_PASSES = 2,
                HIGH_SMOOTHING_PASSES = 3;
        }
    }

    private readonly SKPath _topPath = new();
    private readonly SKPath _bottomPath = new();
    private readonly SKPath _fillPath = new();

    private float _glowRadius;

    private WaveformRenderer() { }

    protected override void OnInitialize() =>
        ExecuteSafely(
            () =>
            {
                base.OnInitialize();
                _smoothingFactor = SMOOTHING_FACTOR_NORMAL;
                Log(LogLevel.Debug, LOG_PREFIX, "Initialized");
            },
            nameof(OnInitialize),
            "Failed to initialize renderer"
        );

    protected override void OnConfigurationChanged() =>
        ExecuteSafely(
            () =>
            {
                _smoothingFactor = _isOverlayActive ?
                    SMOOTHING_FACTOR_OVERLAY :
                    SMOOTHING_FACTOR_NORMAL;

                Log(LogLevel.Debug,
                    LOG_PREFIX,
                    $"Configuration changed. New Quality: {Quality}");
            },
            nameof(OnConfigurationChanged),
            "Failed to handle configuration change"
        );

    protected override void OnQualitySettingsApplied() =>
        ExecuteSafely(
            () =>
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

                Log(LogLevel.Debug,
                    LOG_PREFIX,
                    $"Quality changed to {Quality}");
            },
            nameof(OnQualitySettingsApplied),
            "Failed to apply quality settings"
        );

    private void ApplyLowQualitySettings()
    {
        _useAntiAlias = LOW_USE_ANTI_ALIAS;
        _useAdvancedEffects = LOW_USE_ADVANCED_EFFECTS;
        _glowRadius = LOW_GLOW_RADIUS;
    }

    private void ApplyMediumQualitySettings()
    {
        _useAntiAlias = MEDIUM_USE_ANTI_ALIAS;
        _useAdvancedEffects = MEDIUM_USE_ADVANCED_EFFECTS;
        _glowRadius = MEDIUM_GLOW_RADIUS;
    }

    private void ApplyHighQualitySettings()
    {
        _useAntiAlias = HIGH_USE_ANTI_ALIAS;
        _useAdvancedEffects = HIGH_USE_ADVANCED_EFFECTS;
        _glowRadius = HIGH_GLOW_RADIUS;
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
                float midY = info.Height / 2;
                float xStep = info.Width / (float)spectrum.Length;

                UpdateWavePaths(spectrum, midY, xStep);
                RenderWaveform(canvas, spectrum, info, paint);
            },
            nameof(RenderEffect),
            "Error during rendering"
        );

    private void UpdateWavePaths(float[] spectrum, float midY, float xStep) =>
        ExecuteSafely(
            () =>
            {
                _topPath.Reset();
                _bottomPath.Reset();
                _fillPath.Reset();

                float startX = 0;
                float startTopY = midY - spectrum[0] * midY;
                float startBottomY = midY + spectrum[0] * midY;

                _topPath.MoveTo(startX, startTopY);
                _bottomPath.MoveTo(startX, startBottomY);
                _fillPath.MoveTo(startX, startTopY);

                for (int i = 1; i < spectrum.Length; i++)
                {
                    float prevX = (i - 1) * xStep;
                    float prevTopY = midY - spectrum[i - 1] * midY;
                    float prevBottomY = midY + spectrum[i - 1] * midY;

                    float x = i * xStep;
                    float topY = midY - spectrum[i] * midY;
                    float bottomY = midY + spectrum[i] * midY;

                    float controlX = (prevX + x) / 2;

                    if (Quality == RenderQuality.Low)
                    {
                        _topPath.LineTo(x, topY);
                        _bottomPath.LineTo(x, bottomY);
                        _fillPath.LineTo(x, topY);
                    }
                    else
                    {
                        _topPath.CubicTo(controlX, prevTopY, controlX, topY, x, topY);
                        _bottomPath.CubicTo(controlX, prevBottomY, controlX, bottomY, x, bottomY);
                        _fillPath.CubicTo(controlX, prevTopY, controlX, topY, x, topY);
                    }
                }

                float endX = (spectrum.Length - 1) * xStep;
                float endBottomY = midY + spectrum[^1] * midY;

                _fillPath.LineTo(endX, endBottomY);

                for (int i = spectrum.Length - 2; i >= 0; i--)
                {
                    float prevX = (i + 1) * xStep;
                    float prevBottomY = midY + spectrum[i + 1] * midY;

                    float x = i * xStep;
                    float bottomY = midY + spectrum[i] * midY;

                    float controlX = (prevX + x) / 2;

                    if (Quality == RenderQuality.Low)
                    {
                        _fillPath.LineTo(x, bottomY);
                    }
                    else
                    {
                        _fillPath.CubicTo(controlX, prevBottomY, controlX, bottomY, x, bottomY);
                    }
                }

                _fillPath.Close();
            },
            nameof(UpdateWavePaths),
            "Failed to update wave paths"
        );

    private void RenderWaveform(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        SKPaint basePaint) =>
        ExecuteSafely(
            () =>
            {
                using var wavePaint = CreateWaveformPaint(basePaint, spectrum.Length);
                using var fillPaint = CreateFillPaint(basePaint);

                canvas.DrawPath(_fillPath, fillPaint);
                canvas.DrawPath(_topPath, wavePaint);
                canvas.DrawPath(_bottomPath, wavePaint);

                if (_useAdvancedEffects && HasHighAmplitude(spectrum))
                {
                    using var glowPaint = CreateGlowPaint(basePaint, spectrum.Length);
                    canvas.DrawPath(_topPath, glowPaint);
                    canvas.DrawPath(_bottomPath, glowPaint);

                    using var highlightPaint = CreateHighlightPaint(spectrum.Length);

                    RenderHighlights(
                        canvas,
                        spectrum,
                        info.Height / 2,
                        info.Width / (float)spectrum.Length,
                        highlightPaint);
                }
            },
            nameof(RenderWaveform),
            "Failed to render waveform"
        );

    private SKPaint CreateWaveformPaint(SKPaint basePaint, int spectrumLength)
    {
        var paint = _paintPool.Get();
        paint.Reset();
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = Max(MIN_STROKE_WIDTH, 50f / spectrumLength);
        paint.IsAntialias = _useAntiAlias;
        paint.StrokeCap = SKStrokeCap.Round;
        paint.StrokeJoin = SKStrokeJoin.Round;
        paint.Color = basePaint.Color;
        return paint;
    }

    private SKPaint CreateFillPaint(SKPaint basePaint)
    {
        var paint = _paintPool.Get();
        paint.Reset();
        paint.Style = SKPaintStyle.Fill;
        paint.Color = basePaint.Color.WithAlpha((byte)(255 * FILL_OPACITY));
        paint.IsAntialias = _useAntiAlias;
        return paint;
    }

    private SKPaint CreateGlowPaint(SKPaint basePaint, int spectrumLength)
    {
        var paint = _paintPool.Get();
        paint.Reset();
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = Max(MIN_STROKE_WIDTH, 50f / spectrumLength) * 1.5f;
        paint.Color = basePaint.Color.WithAlpha((byte)(255 * GLOW_INTENSITY));
        paint.IsAntialias = _useAntiAlias;
        paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, _glowRadius);
        return paint;
    }

    private SKPaint CreateHighlightPaint(int spectrumLength)
    {
        var paint = _paintPool.Get();
        paint.Reset();
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = Max(MIN_STROKE_WIDTH, 50f / spectrumLength) * 0.6f;
        paint.Color = SKColors.White.WithAlpha((byte)(255 * HIGHLIGHT_ALPHA));
        paint.IsAntialias = _useAntiAlias;
        return paint;
    }

    private static bool HasHighAmplitude(float[] spectrum)
    {
        for (int i = 0; i < spectrum.Length; i++)
        {
            if (spectrum[i] > HIGH_AMPLITUDE_THRESHOLD)
            {
                return true;
            }
        }
        return false;
    }

    private static void RenderHighlights(
        SKCanvas canvas,
        float[] spectrum,
        float midY,
        float xStep,
        SKPaint highlightPaint)
    {
        for (int i = 0; i < spectrum.Length; i++)
        {
            if (spectrum[i] > HIGH_AMPLITUDE_THRESHOLD)
            {
                float x = i * xStep;
                float topY = midY - spectrum[i] * midY;
                float bottomY = midY + spectrum[i] * midY;

                canvas.DrawPoint(x, topY, highlightPaint);
                canvas.DrawPoint(x, bottomY, highlightPaint);
            }
        }
    }

    protected override void OnInvalidateCachedResources() =>
        ExecuteSafely(
            () =>
            {
                base.OnInvalidateCachedResources();
                Log(LogLevel.Debug, LOG_PREFIX, "Cached resources invalidated");
            },
            nameof(OnInvalidateCachedResources),
            "Failed to invalidate cached resources"
        );

    protected override void OnDispose() =>
        ExecuteSafely(
            () =>
            {
                _topPath?.Dispose();
                _bottomPath?.Dispose();
                _fillPath?.Dispose();

                base.OnDispose();
                Log(LogLevel.Debug, LOG_PREFIX, "Disposed");
            },
            nameof(OnDispose),
            "Error during specific disposal"
        );
}