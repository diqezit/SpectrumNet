#nullable enable

using static System.MathF;
using static SpectrumNet.SN.Visualization.Renderers.RainbowRenderer.Constants;

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class RainbowRenderer : EffectSpectrumRenderer
{
    private const string LogPrefix = nameof(RainbowRenderer);

    private static readonly Lazy<RainbowRenderer> _instance =
        new(() => new RainbowRenderer());

    private RainbowRenderer() { }

    public static RainbowRenderer GetInstance() => _instance.Value;

    public static class Constants
    {
        public const float
            MIN_MAGNITUDE_THRESHOLD = 0.008f,
            ALPHA_MULTIPLIER = 1.7f,
            SMOOTHING_BASE = 0.3f,
            SMOOTHING_OVERLAY = 0.5f,
            CORNER_RADIUS = 8f,
            GRADIENT_ALPHA_FACTOR = 0.7f,
            GLOW_INTENSITY = 0.45f,
            GLOW_RADIUS = 6f,
            GLOW_LOUDNESS_FACTOR = 0.3f,
            GLOW_RADIUS_THRESHOLD = 0.1f,
            GLOW_MIN_MAGNITUDE = 0.3f,
            GLOW_MAX_MAGNITUDE = 0.95f,
            HIGHLIGHT_ALPHA = 0.8f,
            HIGHLIGHT_HEIGHT_PROP = 0.08f,
            HIGHLIGHT_WIDTH_PROP = 0.7f,
            REFLECTION_OPACITY = 0.3f,
            REFLECTION_HEIGHT = 0.15f,
            REFLECTION_FACTOR = 0.4f,
            REFLECTION_MIN_MAGNITUDE = 0.2f,
            SUB_BASS_WEIGHT = 1.7f,
            BASS_WEIGHT = 1.4f,
            MID_WEIGHT = 1.1f,
            HIGH_WEIGHT = 0.6f,
            LOUDNESS_SCALE = 4.0f,
            HUE_START = 240f,
            HUE_RANGE = 240f,
            SATURATION = 100f,
            BRIGHTNESS_BASE = 90f,
            BRIGHTNESS_RANGE = 10f;

        public const int MAX_ALPHA = 255;

        public static readonly Dictionary<RenderQuality, QualitySettings> QualityPresets = new()
        {
            [RenderQuality.Low] = new(
                UseAntiAlias: false,
                UseAdvancedEffects: false,
                FilterMode: SKFilterMode.Nearest,
                MipmapMode: SKMipmapMode.None
            ),
            [RenderQuality.Medium] = new(
                UseAntiAlias: true,
                UseAdvancedEffects: true,
                FilterMode: SKFilterMode.Linear,
                MipmapMode: SKMipmapMode.Linear
            ),
            [RenderQuality.High] = new(
                UseAntiAlias: true,
                UseAdvancedEffects: true,
                FilterMode: SKFilterMode.Linear,
                MipmapMode: SKMipmapMode.Linear
            )
        };

        public record QualitySettings(
            bool UseAntiAlias,
            bool UseAdvancedEffects,
            SKFilterMode FilterMode,
            SKMipmapMode MipmapMode
        );
    }

    private static readonly SKColor[] GradientColors = new SKColor[2];
    private static readonly float[] GradientPositions = [0f, 1f];

    private QualitySettings _currentSettings = QualityPresets[RenderQuality.Medium];
    private SKColor[]? _colorCache;
    private SKPaint? _glowPaint;
    private readonly SKPath _path = new();
    private readonly SKPaint _barPaint = new() { Style = SKPaintStyle.Fill };
    private readonly SKPaint _highlightPaint = new()
    {
        Style = SKPaintStyle.Fill,
        Color = SKColors.White
    };
    private readonly SKPaint _reflectionPaint = new()
    {
        Style = SKPaintStyle.Fill,
        BlendMode = SKBlendMode.SrcOver
    };

    protected override void OnInitialize()
    {
        InitializeResources();
        _logger.Log(LogLevel.Debug, LogPrefix, "Initialized");
    }

    private void InitializeResources()
    {
        InitializeGlowPaint();
        CreateColorCache();
    }

    private void InitializeGlowPaint()
    {
        _glowPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            IsAntialias = UseAntiAlias,
            ImageFilter = SKImageFilter.CreateBlur(GLOW_RADIUS, GLOW_RADIUS)
        };
    }

    private void CreateColorCache()
    {
        _colorCache = new SKColor[MAX_ALPHA + 1];
        for (int i = 0; i <= MAX_ALPHA; i++)
        {
            float normalizedValue = i / (float)MAX_ALPHA;
            _colorCache[i] = GetRainbowColor(normalizedValue);
        }
    }

    protected override void OnConfigurationChanged()
    {
        _processingCoordinator.SetSmoothingFactor(IsOverlayActive ? SMOOTHING_OVERLAY : SMOOTHING_BASE);
        RequestRedraw();
    }

    protected override void OnQualitySettingsApplied()
    {
        _currentSettings = QualityPresets[Quality];
        UpdatePaintProperties();
        _logger.Log(LogLevel.Debug, LogPrefix, $"Quality changed to {Quality}");
    }

    private void UpdatePaintProperties()
    {
        _barPaint.IsAntialias = UseAntiAlias;
        _highlightPaint.IsAntialias = UseAntiAlias;
        _reflectionPaint.IsAntialias = UseAntiAlias;

        if (_glowPaint != null)
            _glowPaint.IsAntialias = UseAntiAlias;
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
        RenderRainbowBars(canvas, spectrum, info, barWidth, barSpacing);
    }

    private void RenderRainbowBars(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing)
    {
        float totalBarWidth = barWidth + barSpacing;
        float startX = CalculateStartX(
            info.Width,
            spectrum.Length,
            totalBarWidth,
            barSpacing
        );
        float loudness = CalculateLoudness(spectrum);
        float reflectionHeight = info.Height * REFLECTION_HEIGHT;

        for (int i = 0; i < spectrum.Length; i++)
        {
            float magnitude = Clamp(spectrum[i], 0f, 1f);
            if (magnitude < MIN_MAGNITUDE_THRESHOLD) continue;

            RenderBar(
                canvas,
                i,
                magnitude,
                startX,
                totalBarWidth,
                barWidth,
                info.Height,
                loudness,
                reflectionHeight
            );
        }

        _barPaint.Shader = null;
    }

    private static float CalculateStartX(
        float width,
        int spectrumLength,
        float totalBarWidth,
        float barSpacing) =>
        (width - (spectrumLength * totalBarWidth - barSpacing)) / 2f;

    private void RenderBar(
        SKCanvas canvas,
        int index,
        float magnitude,
        float startX,
        float totalBarWidth,
        float barWidth,
        float canvasHeight,
        float loudness,
        float reflectionHeight)
    {
        float barHeight = magnitude * canvasHeight;
        float x = startX + index * totalBarWidth;
        float y = canvasHeight - barHeight;
        var barRect = new SKRect(x, y, x + barWidth, canvasHeight);

        if (canvas.QuickReject(barRect)) return;

        SKColor barColor = GetBarColor(magnitude);

        if (ShouldDrawGlow(magnitude))
            DrawGlow(canvas, barRect, barColor, magnitude, loudness);

        DrawMainBar(canvas, barRect, barColor, magnitude);

        if (barHeight > CORNER_RADIUS * 2)
        {
            DrawHighlight(canvas, x, y, barWidth, barHeight, magnitude);

            if (ShouldDrawReflection(magnitude))
            {
                DrawReflection(
                    canvas,
                    x,
                    canvasHeight,
                    barWidth,
                    barHeight,
                    barColor,
                    magnitude,
                    reflectionHeight
                );
            }
        }
    }

    private bool ShouldDrawGlow(float magnitude) =>
        UseAdvancedEffects && _glowPaint != null &&
        magnitude > GLOW_MIN_MAGNITUDE && magnitude <= GLOW_MAX_MAGNITUDE;

    private bool ShouldDrawReflection(float magnitude) =>
        UseAdvancedEffects && magnitude > REFLECTION_MIN_MAGNITUDE;

    private void DrawGlow(
        SKCanvas canvas,
        SKRect barRect,
        SKColor barColor,
        float magnitude,
        float loudness)
    {
        if (_glowPaint == null) return;

        float adjustedRadius = GLOW_RADIUS * (1 + loudness * GLOW_LOUDNESS_FACTOR);
        if (MathF.Abs(adjustedRadius - GLOW_RADIUS) > GLOW_RADIUS_THRESHOLD)
        {
            _glowPaint.ImageFilter = SKImageFilter.CreateBlur(
                adjustedRadius,
                adjustedRadius
            );
        }

        byte glowAlpha = (byte)Clamp(
            magnitude * MAX_ALPHA * GLOW_INTENSITY,
            0,
            MAX_ALPHA
        );
        _glowPaint.Color = barColor.WithAlpha(glowAlpha);
        canvas.DrawRoundRect(barRect, CORNER_RADIUS, CORNER_RADIUS, _glowPaint);
    }

    private void DrawMainBar(
        SKCanvas canvas,
        SKRect barRect,
        SKColor barColor,
        float magnitude)
    {
        GradientColors[0] = barColor;
        GradientColors[1] = barColor.WithAlpha(
            (byte)(MAX_ALPHA * GRADIENT_ALPHA_FACTOR)
        );

        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(barRect.Left, barRect.Top),
            new SKPoint(barRect.Right, barRect.Top),
            GradientColors,
            GradientPositions,
            SKShaderTileMode.Clamp
        );

        byte barAlpha = (byte)Clamp(
            magnitude * ALPHA_MULTIPLIER * MAX_ALPHA,
            0,
            MAX_ALPHA
        );
        _barPaint.Color = barColor.WithAlpha(barAlpha);
        _barPaint.Shader = shader;
        canvas.DrawRoundRect(barRect, CORNER_RADIUS, CORNER_RADIUS, _barPaint);
    }

    private void DrawHighlight(
        SKCanvas canvas,
        float x,
        float y,
        float barWidth,
        float barHeight,
        float magnitude)
    {
        float highlightWidth = barWidth * HIGHLIGHT_WIDTH_PROP;
        float highlightHeight = MathF.Min(
            barHeight * HIGHLIGHT_HEIGHT_PROP,
            CORNER_RADIUS
        );
        byte highlightAlpha = (byte)Clamp(
            magnitude * MAX_ALPHA * HIGHLIGHT_ALPHA,
            0,
            MAX_ALPHA
        );

        _highlightPaint.Color = SKColors.White.WithAlpha(highlightAlpha);
        canvas.DrawRect(
            x + (barWidth - highlightWidth) / 2,
            y,
            highlightWidth,
            highlightHeight,
            _highlightPaint
        );
    }

    private void DrawReflection(
        SKCanvas canvas,
        float x,
        float canvasHeight,
        float barWidth,
        float barHeight,
        SKColor barColor,
        float magnitude,
        float reflectionHeight)
    {
        byte reflectionAlpha = (byte)Clamp(
            magnitude * MAX_ALPHA * REFLECTION_OPACITY,
            0,
            MAX_ALPHA
        );
        _reflectionPaint.Color = barColor.WithAlpha(reflectionAlpha);

        float reflectHeight = MathF.Min(
            barHeight * REFLECTION_FACTOR,
            reflectionHeight
        );
        canvas.DrawRect(x, canvasHeight, barWidth, reflectHeight, _reflectionPaint);
    }

    private SKColor GetBarColor(float magnitude) =>
        _colorCache?[GetColorIndex(magnitude)] ?? GetRainbowColor(magnitude);

    private static int GetColorIndex(float magnitude) =>
        (int)Clamp(magnitude * MAX_ALPHA, 0, MAX_ALPHA);

    private static SKColor GetRainbowColor(float normalizedValue)
    {
        normalizedValue = Clamp(normalizedValue, 0f, 1f);
        float hue = HUE_START - HUE_RANGE * normalizedValue;
        if (hue < 0) hue += 360;
        float brightness = BRIGHTNESS_BASE + normalizedValue * BRIGHTNESS_RANGE;
        return SKColor.FromHsv(hue, SATURATION, brightness);
    }

    private static float CalculateLoudness(ReadOnlySpan<float> spectrum)
    {
        if (spectrum.IsEmpty) return 0f;

        float sum = 0f;
        int length = spectrum.Length;
        int subBass = length >> 4, bass = length >> 3, mid = length >> 2;

        for (int i = 0; i < length; i++)
        {
            float weight = i < subBass ? SUB_BASS_WEIGHT :
                          i < bass ? BASS_WEIGHT :
                          i < mid ? MID_WEIGHT : HIGH_WEIGHT;
            sum += MathF.Abs(spectrum[i]) * weight;
        }

        return Clamp(sum / length * LOUDNESS_SCALE, 0f, 1f);
    }

    protected override void CleanupUnusedResources()
    {
        if (_glowPaint?.ImageFilter != null)
        {
            var currentRadius = GLOW_RADIUS;
            if (MathF.Abs(currentRadius - GLOW_RADIUS) < float.Epsilon)
            {
                _glowPaint.ImageFilter?.Dispose();
                _glowPaint.ImageFilter = SKImageFilter.CreateBlur(GLOW_RADIUS, GLOW_RADIUS);
            }
        }
    }

    protected override void OnDispose()
    {
        _path?.Dispose();
        _glowPaint?.Dispose();
        _barPaint?.Dispose();
        _highlightPaint?.Dispose();
        _reflectionPaint?.Dispose();

        _colorCache = null;

        _logger.Log(LogLevel.Debug, LogPrefix, "Disposed");
    }
}