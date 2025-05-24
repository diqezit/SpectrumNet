#nullable enable

using static System.MathF;
using static SpectrumNet.SN.Visualization.Renderers.WaveformRenderer.Constants;

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class WaveformRenderer : EffectSpectrumRenderer
{
    private const string LogPrefix = nameof(WaveformRenderer);

    private static readonly Lazy<WaveformRenderer> _instance =
        new(() => new WaveformRenderer());

    private WaveformRenderer() { }

    public static WaveformRenderer GetInstance() => _instance.Value;

    public static class Constants
    {
        public const float
            SMOOTHING_FACTOR_NORMAL = 0.3f,
            SMOOTHING_FACTOR_OVERLAY = 0.5f,
            MAX_SPECTRUM_VALUE = 1.5f,
            MIN_STROKE_WIDTH = 2.0f,
            GLOW_INTENSITY = 0.4f,
            HIGHLIGHT_ALPHA = 0.7f,
            HIGH_AMPLITUDE_THRESHOLD = 0.6f,
            FILL_OPACITY = 0.25f,
            GLOW_STROKE_MULTIPLIER = 1.5f,
            HIGHLIGHT_STROKE_MULTIPLIER = 0.6f,
            STROKE_WIDTH_DIVISOR = 50f,
            CENTER_PROPORTION = 0.5f,
            CONTROL_POINT_FACTOR = 0.5f;

        public const int BATCH_SIZE = 64;

        public static readonly Dictionary<RenderQuality, QualitySettings> QualityPresets = new()
        {
            [RenderQuality.Low] = new(
                UseAntiAlias: false,
                UseAdvancedEffects: false,
                GlowRadius: 1.5f,
                SmoothingPasses: 1,
                UseCubicCurves: false
            ),
            [RenderQuality.Medium] = new(
                UseAntiAlias: true,
                UseAdvancedEffects: true,
                GlowRadius: 3.0f,
                SmoothingPasses: 2,
                UseCubicCurves: true
            ),
            [RenderQuality.High] = new(
                UseAntiAlias: true,
                UseAdvancedEffects: true,
                GlowRadius: 4.5f,
                SmoothingPasses: 3,
                UseCubicCurves: true
            )
        };

        public record QualitySettings(
            bool UseAntiAlias,
            bool UseAdvancedEffects,
            float GlowRadius,
            int SmoothingPasses,
            bool UseCubicCurves
        );
    }

    private readonly SKPath _topPath = new();
    private readonly SKPath _bottomPath = new();
    private readonly SKPath _fillPath = new();

    private QualitySettings _currentSettings = QualityPresets[RenderQuality.Medium];

    protected override void OnInitialize()
    {
        base.OnInitialize();
        _processingCoordinator.SetSmoothingFactor(SMOOTHING_FACTOR_NORMAL);
        _logger.Log(LogLevel.Debug, LogPrefix, "Initialized");
    }

    protected override void OnConfigurationChanged()
    {
        _processingCoordinator.SetSmoothingFactor(IsOverlayActive
            ? SMOOTHING_FACTOR_OVERLAY
            : SMOOTHING_FACTOR_NORMAL);
    }

    protected override void OnQualitySettingsApplied()
    {
        _currentSettings = QualityPresets[Quality];
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
        float midY = info.Height * CENTER_PROPORTION;
        float xStep = info.Width / (float)spectrum.Length;

        UpdateWavePaths(spectrum, midY, xStep);
        RenderWaveform(canvas, spectrum, info, paint);
    }

    private void UpdateWavePaths(
        float[] spectrum,
        float midY,
        float xStep)
    {
        _topPath.Reset();
        _bottomPath.Reset();
        _fillPath.Reset();

        if (spectrum.Length == 0) return;

        float startX = 0;
        float startTopY = midY - spectrum[0] * midY;
        float startBottomY = midY + spectrum[0] * midY;

        _topPath.MoveTo(startX, startTopY);
        _bottomPath.MoveTo(startX, startBottomY);
        _fillPath.MoveTo(startX, startTopY);

        if (_currentSettings.UseCubicCurves)
        {
            BuildCubicPaths(spectrum, midY, xStep);
        }
        else
        {
            BuildLinearPaths(spectrum, midY, xStep);
        }

        CompleteFillPath(spectrum, midY, xStep);
    }

    private void BuildCubicPaths(
        float[] spectrum,
        float midY,
        float xStep)
    {
        for (int i = 1; i < spectrum.Length; i++)
        {
            float prevX = (i - 1) * xStep;
            float prevTopY = midY - spectrum[i - 1] * midY;
            float prevBottomY = midY + spectrum[i - 1] * midY;

            float x = i * xStep;
            float topY = midY - spectrum[i] * midY;
            float bottomY = midY + spectrum[i] * midY;

            float controlX = (prevX + x) * CONTROL_POINT_FACTOR;

            _topPath.CubicTo(
                controlX, prevTopY,
                controlX, topY,
                x, topY);

            _bottomPath.CubicTo(
                controlX, prevBottomY,
                controlX, bottomY,
                x, bottomY);

            _fillPath.CubicTo(
                controlX, prevTopY,
                controlX, topY,
                x, topY);
        }
    }

    private void BuildLinearPaths(
        float[] spectrum,
        float midY,
        float xStep)
    {
        for (int i = 1; i < spectrum.Length; i++)
        {
            float x = i * xStep;
            float topY = midY - spectrum[i] * midY;
            float bottomY = midY + spectrum[i] * midY;

            _topPath.LineTo(x, topY);
            _bottomPath.LineTo(x, bottomY);
            _fillPath.LineTo(x, topY);
        }
    }

    private void CompleteFillPath(
        float[] spectrum,
        float midY,
        float xStep)
    {
        float endX = (spectrum.Length - 1) * xStep;
        float endBottomY = midY + spectrum[^1] * midY;

        _fillPath.LineTo(endX, endBottomY);

        if (_currentSettings.UseCubicCurves)
        {
            for (int i = spectrum.Length - 2; i >= 0; i--)
            {
                float prevX = (i + 1) * xStep;
                float prevBottomY = midY + spectrum[i + 1] * midY;

                float x = i * xStep;
                float bottomY = midY + spectrum[i] * midY;

                float controlX = (prevX + x) * CONTROL_POINT_FACTOR;

                _fillPath.CubicTo(
                    controlX, prevBottomY,
                    controlX, bottomY,
                    x, bottomY);
            }
        }
        else
        {
            for (int i = spectrum.Length - 2; i >= 0; i--)
            {
                float x = i * xStep;
                float bottomY = midY + spectrum[i] * midY;
                _fillPath.LineTo(x, bottomY);
            }
        }

        _fillPath.Close();
    }

    private void RenderWaveform(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        SKPaint basePaint)
    {
        using var wavePaint = CreateWaveformPaint(basePaint, spectrum.Length);
        using var fillPaint = CreateFillPaint(basePaint);

        canvas.DrawPath(_fillPath, fillPaint);
        canvas.DrawPath(_topPath, wavePaint);
        canvas.DrawPath(_bottomPath, wavePaint);

        if (UseAdvancedEffects && HasHighAmplitude(spectrum))
        {
            RenderAdvancedEffects(canvas, spectrum, info, basePaint);
        }
    }

    private void RenderAdvancedEffects(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        SKPaint basePaint)
    {
        using var glowPaint = CreateGlowPaint(basePaint, spectrum.Length);
        canvas.DrawPath(_topPath, glowPaint);
        canvas.DrawPath(_bottomPath, glowPaint);

        using var highlightPaint = CreateHighlightPaint(spectrum.Length);

        RenderHighlights(
            canvas,
            spectrum,
            info.Height * CENTER_PROPORTION,
            info.Width / (float)spectrum.Length,
            highlightPaint);
    }

    private SKPaint CreateWaveformPaint(
        SKPaint basePaint,
        int spectrumLength)
    {
        var paint = _resourceManager.GetPaint();
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = MathF.Max(
            MIN_STROKE_WIDTH,
            STROKE_WIDTH_DIVISOR / spectrumLength);
        paint.IsAntialias = UseAntiAlias;
        paint.StrokeCap = SKStrokeCap.Round;
        paint.StrokeJoin = SKStrokeJoin.Round;
        paint.Color = basePaint.Color;
        return paint;
    }

    private SKPaint CreateFillPaint(SKPaint basePaint)
    {
        var paint = _resourceManager.GetPaint();
        paint.Style = SKPaintStyle.Fill;
        paint.Color = basePaint.Color.WithAlpha(
            (byte)(255 * FILL_OPACITY));
        paint.IsAntialias = UseAntiAlias;
        return paint;
    }

    private SKPaint CreateGlowPaint(
        SKPaint basePaint,
        int spectrumLength)
    {
        var paint = _resourceManager.GetPaint();
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = MathF.Max(
            MIN_STROKE_WIDTH,
            STROKE_WIDTH_DIVISOR / spectrumLength) * GLOW_STROKE_MULTIPLIER;
        paint.Color = basePaint.Color.WithAlpha(
            (byte)(255 * GLOW_INTENSITY));
        paint.IsAntialias = UseAntiAlias;
        paint.MaskFilter = SKMaskFilter.CreateBlur(
            SKBlurStyle.Normal,
            _currentSettings.GlowRadius);
        return paint;
    }

    private SKPaint CreateHighlightPaint(int spectrumLength)
    {
        var paint = _resourceManager.GetPaint();
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = MathF.Max(
            MIN_STROKE_WIDTH,
            STROKE_WIDTH_DIVISOR / spectrumLength) * HIGHLIGHT_STROKE_MULTIPLIER;
        paint.Color = SKColors.White.WithAlpha(
            (byte)(255 * HIGHLIGHT_ALPHA));
        paint.IsAntialias = UseAntiAlias;
        return paint;
    }

    private static bool HasHighAmplitude(float[] spectrum)
    {
        for (int i = 0; i < spectrum.Length; i++)
        {
            if (spectrum[i] > HIGH_AMPLITUDE_THRESHOLD)
                return true;
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

    protected override void OnDispose()
    {
        _topPath?.Dispose();
        _bottomPath?.Dispose();
        _fillPath?.Dispose();
        base.OnDispose();
        _logger.Log(LogLevel.Debug, LogPrefix, "Disposed");
    }
}