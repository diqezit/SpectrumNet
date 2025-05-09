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
    private new bool _useAntiAlias;
    private new bool _useAdvancedEffects;
    private int _smoothingPasses;
    private volatile bool _isConfiguring;

    private WaveformRenderer() { }

    protected override void OnInitialize() =>
        ExecuteSafely(
            () =>
            {
                base.OnInitialize();
                InitializeResources();
                ApplyQualitySettings();
                Log(LogLevel.Debug, LOG_PREFIX, "Initialized");
            },
            nameof(OnInitialize),
            "Failed to initialize renderer"
        );

    private void InitializeResources()
    {
        _smoothingFactor = SMOOTHING_FACTOR_NORMAL;
    }

    public override void Configure(
        bool isOverlayActive,
        RenderQuality quality) =>
        ExecuteSafely(
            () =>
            {
                if (_isConfiguring) return;

                try
                {
                    _isConfiguring = true;
                    bool configChanged = _isOverlayActive != isOverlayActive
                                         || Quality != quality;
                    base.Configure(isOverlayActive, quality);

                    UpdateConfiguration(isOverlayActive);

                    if (configChanged)
                    {
                        OnConfigurationChanged();
                    }
                }
                finally
                {
                    _isConfiguring = false;
                }
            },
            nameof(Configure),
            "Failed to configure renderer"
        );

    private void UpdateConfiguration(bool isOverlayActive)
    {
        _smoothingFactor = isOverlayActive ?
            SMOOTHING_FACTOR_OVERLAY :
            SMOOTHING_FACTOR_NORMAL;
    }

    protected override void OnConfigurationChanged() =>
        ExecuteSafely(
            () =>
            {
                Log(LogLevel.Debug,
                    LOG_PREFIX,
                    $"Configuration changed. New Quality: {Quality}");
            },
            nameof(OnConfigurationChanged),
            "Failed to handle configuration change"
        );

    protected override void ApplyQualitySettings() =>
        ExecuteSafely(
            () =>
            {
                if (_isConfiguring) return;

                try
                {
                    _isConfiguring = true;
                    base.ApplyQualitySettings();
                    ApplyQualityBasedSettings();
                    Log(LogLevel.Debug,
                        LOG_PREFIX,
                        $"Quality changed to {Quality}");
                }
                finally
                {
                    _isConfiguring = false;
                }
            },
            nameof(ApplyQualitySettings),
            "Failed to apply quality settings"
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
        _useAntiAlias = LOW_USE_ANTI_ALIAS;
        _useAdvancedEffects = LOW_USE_ADVANCED_EFFECTS;
        _glowRadius = LOW_GLOW_RADIUS;
        _smoothingPasses = LOW_SMOOTHING_PASSES;
    }

    private void ApplyMediumQualitySettings()
    {
        _useAntiAlias = MEDIUM_USE_ANTI_ALIAS;
        _useAdvancedEffects = MEDIUM_USE_ADVANCED_EFFECTS;
        _glowRadius = MEDIUM_GLOW_RADIUS;
        _smoothingPasses = MEDIUM_SMOOTHING_PASSES;
    }

    private void ApplyHighQualitySettings()
    {
        _useAntiAlias = HIGH_USE_ANTI_ALIAS;
        _useAdvancedEffects = HIGH_USE_ADVANCED_EFFECTS;
        _glowRadius = HIGH_GLOW_RADIUS;
        _smoothingPasses = HIGH_SMOOTHING_PASSES;
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

                float midY = info.Height / 2;
                float xStep = info.Width / (float)spectrum.Length;

                UpdateWavePaths(spectrum, midY, xStep);
                RenderWaveform(canvas, spectrum, info, paint);
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
        if (canvas == null)
        {
            Log(LogLevel.Error, LOG_PREFIX, "Canvas is null");
            return false;
        }

        if (spectrum == null || spectrum.Length == 0)
        {
            Log(LogLevel.Error, LOG_PREFIX, "Spectrum is null or empty");
            return false;
        }

        if (paint == null)
        {
            Log(LogLevel.Error, LOG_PREFIX, "Paint is null");
            return false;
        }

        if (info.Width <= 0 || info.Height <= 0)
        {
            Log(LogLevel.Error, LOG_PREFIX, $"Invalid image dimensions: {info.Width}x{info.Height}");
            return false;
        }

        if (_disposed)
        {
            Log(LogLevel.Error, LOG_PREFIX, "Renderer is disposed");
            return false;
        }

        return true;
    }

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
                    RenderHighlights(canvas, spectrum, info.Height / 2, info.Width / (float)spectrum.Length, highlightPaint);
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
        _topPath?.Dispose();
        _bottomPath?.Dispose();
        _fillPath?.Dispose();
    }
}