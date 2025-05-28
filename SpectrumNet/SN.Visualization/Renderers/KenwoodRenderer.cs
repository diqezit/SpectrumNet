#nullable enable

using static SpectrumNet.SN.Visualization.Renderers.KenwoodBarsRenderer.Constants;
using static System.MathF;

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class KenwoodBarsRenderer() : EffectSpectrumRenderer
{
    private const string LogPrefix = nameof(KenwoodBarsRenderer);

    private static readonly Lazy<KenwoodBarsRenderer> _instance =
        new(() => new KenwoodBarsRenderer());

    public static KenwoodBarsRenderer GetInstance() => _instance.Value;

    public static class Constants
    {
        public const float
            ANIMATION_SPEED = 0.85f,
            PEAK_FALL_SPEED = 0.25f,
            PEAK_HEIGHT = 3f,
            PEAK_HOLD_TIME_MS = 300f,
            MIN_BAR_HEIGHT = 1f,
            GLOW_EFFECT_ALPHA = 0.3f,
            ALPHA_MULTIPLIER = 1.5f,
            HIGH_INTENSITY_THRESHOLD = 0.4f,
            CORNER_RADIUS_FACTOR = 0.15f,
            MAX_CORNER_RADIUS = 5f,
            BAR_WIDTH_RATIO = 0.8f,
            MIN_BAR_WIDTH = 1f,
            MAX_BAR_WIDTH = 20f;

        public const int
            MAX_BARS_LOW = 150,
            MAX_BARS_MEDIUM = 75,
            MAX_BARS_HIGH = 75;

        public static readonly SKColor[] BarColors = [
            new(0, 230, 120, 255),
            new(0, 255, 0, 255),
            new(255, 230, 0, 255),
            new(255, 180, 0, 255),
            new(255, 80, 0, 255),
            new(255, 30, 0, 255)
        ];

        public static readonly float[] BarColorPositions =
            [0f, 0.6f, 0.6f, 0.85f, 0.85f, 1f];

        public static readonly Dictionary<RenderQuality, QualitySettings> QualityPresets = new()
        {
            [RenderQuality.Low] = new(
                UseGlow: false,
                UseEdge: false,
                GlowRadius: 1.0f,
                GlowAlpha: GLOW_EFFECT_ALPHA * 0.5f,
                EdgeStrokeWidth: 0f,
                SmoothingFactor: 0.3f
            ),
            [RenderQuality.Medium] = new(
                UseGlow: true,
                UseEdge: true,
                GlowRadius: 2.0f,
                GlowAlpha: GLOW_EFFECT_ALPHA * 0.8f,
                EdgeStrokeWidth: 1.5f,
                SmoothingFactor: 0.8f
            ),
            [RenderQuality.High] = new(
                UseGlow: true,
                UseEdge: true,
                GlowRadius: 3.0f,
                GlowAlpha: GLOW_EFFECT_ALPHA,
                EdgeStrokeWidth: 2.5f,
                SmoothingFactor: 1.0f
            )
        };

        public record QualitySettings(
            bool UseGlow,
            bool UseEdge,
            float GlowRadius,
            float GlowAlpha,
            float EdgeStrokeWidth,
            float SmoothingFactor
        );
    }

    private QualitySettings _currentSettings = QualityPresets[RenderQuality.Medium];
    private readonly SKPaint _peakPaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Fill,
        Color = SKColors.White
    };

    private float[] _peaks = [];
    private float[] _timers = [];
    private readonly float _holdTime = PEAK_HOLD_TIME_MS / 1000f;
    private readonly float _fallSpeed = PEAK_FALL_SPEED;

    protected override int GetMaxBarsForQuality() => Quality switch
    {
        RenderQuality.Low => MAX_BARS_LOW,
        RenderQuality.Medium => MAX_BARS_MEDIUM,
        RenderQuality.High => MAX_BARS_HIGH,
        _ => MAX_BARS_MEDIUM
    };

    protected override RenderParameters CalculateRenderParameters(
        SKImageInfo info,
        int requestedBarCount)
    {
        int maxBars = GetMaxBarsForQuality();
        int effectiveBarCount = Min(requestedBarCount, maxBars);

        float totalWidth = info.Width;
        float totalSpacePerBar = totalWidth / effectiveBarCount;

        float barWidth = totalSpacePerBar * BAR_WIDTH_RATIO;
        barWidth = Clamp(barWidth, MIN_BAR_WIDTH, MAX_BAR_WIDTH);

        float barSpacing = (totalWidth - (barWidth * effectiveBarCount)) /
                          Max(1, effectiveBarCount - 1);

        if (barSpacing < 0)
        {
            barSpacing = 0;
            barWidth = totalWidth / effectiveBarCount;
        }

        float actualTotalWidth = (barWidth * effectiveBarCount) +
                                (barSpacing * (effectiveBarCount - 1));
        float startOffset = (totalWidth - actualTotalWidth) / 2f;

        return new RenderParameters(
            effectiveBarCount,
            barWidth,
            barSpacing,
            startOffset);
    }

    protected override void OnInitialize()
    {
        base.OnInitialize();
        CreateSpectrumGradient(100, BarColors, BarColorPositions);
        RegisterPaintConfigs();
        ApplyQualitySettings();
    }

    protected override void OnQualitySettingsApplied()
    {
        _currentSettings = QualityPresets[Quality];
        ApplyQualitySettings();
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
        var renderParams = CalculateRenderParameters(info, barCount);

        InvalidateGradientIfNeeded(info.Height);
        CreateSpectrumGradient(info.Height, BarColors, BarColorPositions);

        var processedSpectrum = ProcessSpectrumBands(
            spectrum,
            renderParams.EffectiveBarCount);
        AnimateValues(processedSpectrum, ANIMATION_SPEED);
        EnsurePeakArraySize(spectrum.Length);
        for (int i = 0; i < spectrum.Length; i++) 
            UpdatePeak(i, spectrum[i], DeltaTime);
        
        RenderBars(canvas, info, renderParams);
        RenderPeaks(canvas, info, renderParams);
    }

    private void RegisterPaintConfigs()
    {
        RegisterPaintConfig("bar", CreateDefaultPaintConfig(SKColors.White));
        RegisterPaintConfig("glow", CreateGlowPaintConfig(SKColors.White, 3f));
        RegisterPaintConfig("edge", CreateEdgePaintConfig(SKColors.White, 2f));
    }

    private void ApplyQualitySettings()
    {
        SetProcessingSmoothingFactor(_currentSettings.SmoothingFactor);
        _peakPaint.IsAntialias = UseAntiAlias;
    }

    private void RenderBars(
        SKCanvas canvas,
        SKImageInfo info,
        RenderParameters renderParams)
    {
        var animatedValues = GetAnimatedValues();
        float cornerRadius = MathF.Min(
            renderParams.BarWidth * CORNER_RADIUS_FACTOR,
            MAX_CORNER_RADIUS);

        if (ShouldRenderGlow())
            RenderGlowBatch(
                canvas,
                animatedValues,
                info,
                renderParams,
                cornerRadius);

        RenderBarBatch(
            canvas,
            animatedValues,
            info,
            renderParams,
            cornerRadius);
    }

    private void RenderGlowBatch(
        SKCanvas canvas,
        float[] values,
        SKImageInfo info,
        RenderParameters renderParams,
        float cornerRadius)
    {
        var glowPaint = CreateGlowPaint(
            SKColors.White,
            _currentSettings.GlowRadius);
        glowPaint.Color = glowPaint.Color.WithAlpha(
            (byte)(_currentSettings.GlowAlpha * 255));
        glowPaint.Shader = GetSpectrumGradient();

        RenderBatch(canvas, path =>
        {
            for (int i = 0; i < values.Length && i < renderParams.EffectiveBarCount; i++)
            {
                if (values[i] > HIGH_INTENSITY_THRESHOLD)
                {
                    float x = CalculateBarX(
                        i,
                        renderParams.BarWidth,
                        renderParams.BarSpacing,
                        renderParams.StartOffset);
                    var rect = GetBarRect(
                        x,
                        values[i],
                        renderParams.BarWidth,
                        info.Height,
                        MIN_BAR_HEIGHT);
                    path.AddRoundRect(rect, cornerRadius, cornerRadius);
                }
            }
        }, glowPaint);

        ReturnPaint(glowPaint);
    }

    private void RenderBarBatch(
        SKCanvas canvas,
        float[] values,
        SKImageInfo info,
        RenderParameters renderParams,
        float cornerRadius)
    {
        var barPaint = CreateStandardPaint(SKColors.White);
        barPaint.Shader = GetSpectrumGradient();

        var rects = new List<SKRect>();
        for (int i = 0; i < values.Length && i < renderParams.EffectiveBarCount; i++)
        {
            if (values[i] > MIN_MAGNITUDE_THRESHOLD)
            {
                float x = CalculateBarX(
                    i,
                    renderParams.BarWidth,
                    renderParams.BarSpacing,
                    renderParams.StartOffset);
                rects.Add(GetBarRect(
                    x,
                    values[i],
                    renderParams.BarWidth,
                    info.Height,
                    MIN_BAR_HEIGHT));
            }
        }

        RenderRects(canvas, rects, barPaint, cornerRadius);

        if (ShouldRenderEdge())
            RenderEdges(canvas, rects, values, cornerRadius);

        ReturnPaint(barPaint);
    }

    private void RenderEdges(
        SKCanvas canvas,
        List<SKRect> rects,
        float[] values,
        float cornerRadius)
    {
        var edgePaint = CreateStrokePaint(
            SKColors.White,
            _currentSettings.EdgeStrokeWidth);

        for (int i = 0; i < rects.Count; i++)
        {
            if (i < values.Length)
            {
                edgePaint.Color = ApplyAlpha(
                    SKColors.White,
                    values[i],
                    ALPHA_MULTIPLIER);
                canvas.DrawRoundRect(
                    rects[i],
                    cornerRadius,
                    cornerRadius,
                    edgePaint);
            }
        }

        ReturnPaint(edgePaint);
    }

    private void RenderPeaks(
        SKCanvas canvas,
        SKImageInfo info,
        RenderParameters renderParams)
    {
        float cornerRadius = MathF.Min(
            renderParams.BarWidth * CORNER_RADIUS_FACTOR,
            MAX_CORNER_RADIUS);

        for (int i = 0; i < renderParams.EffectiveBarCount; i++)
        {
            float peakValue = GetPeakValue(i);
            if (peakValue > MIN_MAGNITUDE_THRESHOLD)
            {
                float x = CalculateBarX(
                    i,
                    renderParams.BarWidth,
                    renderParams.BarSpacing,
                    renderParams.StartOffset);
                float peakY = info.Height - (peakValue * info.Height);

                var peakRect = new SKRect(
                    x,
                    peakY - PEAK_HEIGHT,
                    x + renderParams.BarWidth,
                    peakY);

                canvas.DrawRoundRect(
                    peakRect,
                    cornerRadius,
                    cornerRadius,
                    _peakPaint);
            }
        }
    }

    private void UpdatePeak(int index, float value, float deltaTime)
    {
        if (index < 0 || index >= _peaks.Length) return;

        if (value > _peaks[index])
        {
            _peaks[index] = value;
            _timers[index] = _holdTime;
        }
        else if (_timers[index] > 0)
        {
            _timers[index] -= deltaTime;
        }
        else
        {
            _peaks[index] = MathF.Max(0, _peaks[index] - _fallSpeed * deltaTime);
        }
    }

    private float GetPeakValue(int index) =>
        index >= 0 && index < _peaks.Length ? _peaks[index] : 0f;

    private void EnsurePeakArraySize(int size)
    {
        if (_peaks.Length < size)
        {
            Array.Resize(ref _peaks, size);
            Array.Resize(ref _timers, size);
        }
    }

    private bool ShouldRenderGlow() =>
        UseAdvancedEffects && _currentSettings.UseGlow;

    private bool ShouldRenderEdge() =>
        UseAdvancedEffects &&
        _currentSettings.UseEdge &&
        _currentSettings.EdgeStrokeWidth > 0;

    protected override void OnDispose()
    {
        _peakPaint.Dispose();
        base.OnDispose();
    }
}