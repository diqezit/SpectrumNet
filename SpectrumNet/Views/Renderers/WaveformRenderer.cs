#nullable enable

using static System.MathF;
using static SpectrumNet.Views.Renderers.WaveformRenderer.Constants;

namespace SpectrumNet.Views.Renderers;

public sealed class WaveformRenderer : EffectSpectrumRenderer
{
    public record Constants
    {
        public const string LOG_PREFIX = "WaveformRenderer";

        public const float
            SMOOTHING_FACTOR_NORMAL = 0.3f,
            SMOOTHING_FACTOR_OVERLAY = 0.5f,
            MIN_MAGNITUDE_THRESHOLD = 0.01f,
            MAX_SPECTRUM_VALUE = 1.5f,
            MIN_STROKE_WIDTH = 2.0f,
            GLOW_INTENSITY = 0.4f,
            GLOW_RADIUS = 3.0f,
            HIGHLIGHT_ALPHA = 0.7f,
            HIGH_AMPLITUDE_THRESHOLD = 0.6f;

        public const byte FILL_ALPHA = 64;
    }

    private static readonly Lazy<WaveformRenderer> _instance = new(() => new WaveformRenderer());
    private readonly SKPath _topPath = new();
    private readonly SKPath _bottomPath = new();
    private readonly SKPath _fillPath = new();

    private WaveformRenderer() { }

    public static WaveformRenderer GetInstance() => _instance.Value;

    public override void Initialize()
    {
        base.Initialize();
        ApplyQualitySettings();
        Log(LogLevel.Debug, LOG_PREFIX, "Initialized");
    }

    public override void Configure(
        bool isOverlayActive,
        RenderQuality quality = RenderQuality.Medium)
    {
        base.Configure(isOverlayActive, quality);

        _smoothingFactor = isOverlayActive
            ? SMOOTHING_FACTOR_OVERLAY
            : SMOOTHING_FACTOR_NORMAL;

        if (_quality != quality)
        {
            _quality = quality;
            ApplyQualitySettings();
        }
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
        float midY = info.Height / 2;
        float xStep = (float)info.Width / spectrum.Length;

        CreateWavePaths(spectrum, midY, xStep);
        CreateFillPath(spectrum, midY, xStep);

        var paints = CreatePaints(paint, spectrum.Length);
        DrawWaveform(canvas, spectrum, midY, xStep, paints);
    }

    private (SKPaint wave, SKPaint fill, SKPaint? glow, SKPaint? highlight) CreatePaints(
        SKPaint basePaint,
        int spectrumLength)
    {
        var waveformPaint = ConfigureWaveformPaint(basePaint, spectrumLength);
        var fillPaint = ConfigureFillPaint(basePaint);
        var glowPaint = UseAdvancedEffects ? ConfigureGlowPaint(basePaint, spectrumLength) : null;
        var highlightPaint = UseAdvancedEffects ? ConfigureHighlightPaint(spectrumLength) : null;

        return (waveformPaint, fillPaint, glowPaint, highlightPaint);
    }

    private SKPaint ConfigureWaveformPaint(SKPaint basePaint, int spectrumLength)
    {
        var paint = _paintPool.Get();
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = MathF.Max(MIN_STROKE_WIDTH, 50f / spectrumLength);
        paint.IsAntialias = UseAntiAlias;
        paint.StrokeCap = SKStrokeCap.Round;
        paint.StrokeJoin = SKStrokeJoin.Round;
        paint.Color = basePaint.Color;
        return paint;
    }

    private SKPaint ConfigureFillPaint(SKPaint basePaint)
    {
        var paint = _paintPool.Get();
        paint.Style = SKPaintStyle.Fill;
        paint.Color = basePaint.Color.WithAlpha(FILL_ALPHA);
        paint.IsAntialias = UseAntiAlias;
        return paint;
    }

    private SKPaint ConfigureGlowPaint(SKPaint basePaint, int spectrumLength)
    {
        var paint = _paintPool.Get();
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = MathF.Max(MIN_STROKE_WIDTH, 50f / spectrumLength) * 1.5f;
        paint.Color = basePaint.Color;
        paint.IsAntialias = UseAntiAlias;
        paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, GLOW_RADIUS);
        return paint;
    }

    private SKPaint ConfigureHighlightPaint(int spectrumLength)
    {
        var paint = _paintPool.Get();
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = MathF.Max(MIN_STROKE_WIDTH, 50f / spectrumLength) * 0.6f;
        paint.Color = SKColors.White.WithAlpha((byte)(255 * HIGHLIGHT_ALPHA));
        paint.IsAntialias = UseAntiAlias;
        return paint;
    }

    private void DrawWaveform(
        SKCanvas canvas,
        float[] spectrum,
        float midY,
        float xStep,
        (SKPaint wave, SKPaint fill, SKPaint? glow, SKPaint? highlight) paints)
    {
        if (paints.glow != null && UseAdvancedEffects)
        {
            DrawGlowEffects(canvas, spectrum, paints.glow);
        }

        canvas.DrawPath(_fillPath, paints.fill);
        canvas.DrawPath(_topPath, paints.wave);
        canvas.DrawPath(_bottomPath, paints.wave);

        if (paints.highlight != null && UseAdvancedEffects)
        {
            RenderHighlights(canvas, spectrum, midY, xStep, paints.highlight);
        }
    }

    private void DrawGlowEffects(SKCanvas canvas, float[] spectrum, SKPaint glowPaint)
    {
        bool hasHighAmplitude = HasHighAmplitude(spectrum);

        if (hasHighAmplitude)
        {
            glowPaint.Color = glowPaint.Color.WithAlpha((byte)(255 * GLOW_INTENSITY));
            canvas.DrawPath(_topPath, glowPaint);
            canvas.DrawPath(_bottomPath, glowPaint);
        }
    }

    private bool HasHighAmplitude(float[] spectrum)
    {
        if (IsHardwareAccelerated && spectrum.Length >= Vector<float>.Count)
        {
            return CheckHighAmplitudeVectorized(spectrum);
        }
        else
        {
            return CheckHighAmplitudeSequential(spectrum);
        }
    }

    private static bool CheckHighAmplitudeVectorized(float[] spectrum)
    {
        Vector<float> threshold = new(HIGH_AMPLITUDE_THRESHOLD);
        int vectorSize = Vector<float>.Count;
        int vectorizedLength = spectrum.Length - spectrum.Length % vectorSize;

        for (int i = 0; i < vectorizedLength; i += vectorSize)
        {
            Vector<float> values = new(spectrum, i);
            if (GreaterThanAny(values, threshold))
            {
                return true;
            }
        }

        // Process remaining elements
        for (int i = vectorizedLength; i < spectrum.Length; i++)
        {
            if (spectrum[i] > HIGH_AMPLITUDE_THRESHOLD)
            {
                return true;
            }
        }

        return false;
    }

    private static bool CheckHighAmplitudeSequential(float[] spectrum)
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

    private void CreateWavePaths(float[] spectrum, float midY, float xStep)
    {
        _topPath.Reset();
        _bottomPath.Reset();

        float x = 0;
        float topY = midY - spectrum[0] * midY;
        float bottomY = midY + spectrum[0] * midY;

        _topPath.MoveTo(x, topY);
        _bottomPath.MoveTo(x, bottomY);

        for (int i = 1; i < spectrum.Length; i++)
        {
            float prevX = (i - 1) * xStep;
            float prevTopY = midY - spectrum[i - 1] * midY;
            float prevBottomY = midY + spectrum[i - 1] * midY;

            x = i * xStep;
            topY = midY - spectrum[i] * midY;
            bottomY = midY + spectrum[i] * midY;

            float controlX = (prevX + x) / 2;
            _topPath.CubicTo(controlX, prevTopY, controlX, topY, x, topY);
            _bottomPath.CubicTo(controlX, prevBottomY, controlX, bottomY, x, bottomY);
        }
    }

    private void CreateFillPath(
        float[] spectrum,
        float midY,
        float xStep)
    {
        _fillPath.Reset();

        float startX = 0;
        float startTopY = midY - spectrum[0] * midY;
        _fillPath.MoveTo(startX, startTopY);

        for (int i = 1; i < spectrum.Length; i++)
        {
            float prevX = (i - 1) * xStep;
            float prevTopY = midY - spectrum[i - 1] * midY;

            float x = i * xStep;
            float topY = midY - spectrum[i] * midY;

            float controlX = (prevX + x) / 2;
            _fillPath.CubicTo(controlX, prevTopY, controlX, topY, x, topY);
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
            _fillPath.CubicTo(controlX, prevBottomY, controlX, bottomY, x, bottomY);
        }

        _fillPath.Close();
    }

    protected override void OnDispose()
    {
        _topPath.Dispose();
        _bottomPath.Dispose();
        _fillPath.Dispose();
        base.OnDispose();
    }
}