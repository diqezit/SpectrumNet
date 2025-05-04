#nullable enable

using static System.MathF;
using static SpectrumNet.Views.Renderers.RainbowRenderer.Constants;

namespace SpectrumNet.Views.Renderers;

public sealed class RainbowRenderer : EffectSpectrumRenderer
{
    private static readonly Lazy<RainbowRenderer> _instance = new(() => new RainbowRenderer());

    private SKColor[]? _colorCache;
    private SKPaint? _glowPaint;
    private readonly SKPath _path = new();
    private readonly SKPaint _barPaint = new() { Style = SKPaintStyle.Fill };
    private readonly SKPaint _highlightPaint = new() { Style = SKPaintStyle.Fill, Color = SKColors.White };
    private readonly SKPaint _reflectionPaint = new() { Style = SKPaintStyle.Fill, BlendMode = SKBlendMode.SrcOver };

    private new bool _useAntiAlias = true;
    private new bool _useAdvancedEffects = true;
    private new SKSamplingOptions _samplingOptions = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    private RainbowRenderer() { }

    public static RainbowRenderer GetInstance() => _instance.Value;

    public record Constants
    {
        public const string LOG_PREFIX = "RainbowRenderer";

        public const float
            MIN_MAGNITUDE_THRESHOLD = 0.008f,
            ALPHA_MULTIPLIER = 1.7f,
            SMOOTHING_BASE = 0.3f,
            SMOOTHING_OVERLAY = 0.5f;

        public const int
            MAX_ALPHA = 255;

        public const float
            CORNER_RADIUS = 8f,
            GRADIENT_ALPHA_FACTOR = 0.7f;

        public const float
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
            REFLECTION_MIN_MAGNITUDE = 0.2f;

        public const float
            SUB_BASS_WEIGHT = 1.7f,
            BASS_WEIGHT = 1.4f,
            MID_WEIGHT = 1.1f,
            HIGH_WEIGHT = 0.6f,
            LOUDNESS_SCALE = 4.0f,
            LOUDNESS_SMOOTH_FACTOR = 0.5f;

        public const float
            HUE_START = 240f,
            HUE_RANGE = 240f,
            SATURATION = 100f,
            BRIGHTNESS_BASE = 90f,
            BRIGHTNESS_RANGE = 10f;

        public static class Quality
        {
            public const bool
                LOW_USE_ADVANCED_EFFECTS = false,
                MEDIUM_USE_ADVANCED_EFFECTS = true,
                HIGH_USE_ADVANCED_EFFECTS = true;
        }
    }

    protected override void OnInitialize()
    {
        ExecuteSafely(
            () =>
            {
                base.OnInitialize();
                InitializeResources();
                Log(LogLevel.Debug, LOG_PREFIX, "Initialized");
            },
            "OnInitialize",
            "Failed during renderer initialization"
        );
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
            IsAntialias = _useAntiAlias,
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

    public override void Configure(
        bool isOverlayActive,
        RenderQuality quality = RenderQuality.Medium)
    {
        ExecuteSafely(
            () =>
            {
                bool configChanged = _isOverlayActive != isOverlayActive || Quality != quality;
                base.Configure(isOverlayActive, quality);

                _smoothingFactor = isOverlayActive ? SMOOTHING_OVERLAY : SMOOTHING_BASE;

                if (configChanged)
                {
                    Log(LogLevel.Debug, LOG_PREFIX, $"Configuration changed. New Quality: {Quality}");
                    OnConfigurationChanged();
                }
            },
            "Configure",
            "Failed to configure renderer"
        );
    }

    protected override void OnQualitySettingsApplied()
    {
        ExecuteSafely(
            () =>
            {
                base.OnQualitySettingsApplied();
                ApplyQualitySpecificSettings();
                Log(LogLevel.Debug, LOG_PREFIX, $"Quality settings applied. New Quality: {Quality}");
            },
            "OnQualitySettingsApplied",
            "Failed to apply specific quality settings"
        );
    }

    private void ApplyQualitySpecificSettings()
    {
        SetQualityBasedOptions();
        UpdatePaintProperties();
    }

    private void SetQualityBasedOptions()
    {
        switch (Quality)
        {
            case RenderQuality.Low:
                _useAntiAlias = false;
                _samplingOptions = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);
                _useAdvancedEffects = Constants.Quality.LOW_USE_ADVANCED_EFFECTS;
                break;

            case RenderQuality.Medium:
                _useAntiAlias = true;
                _samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                _useAdvancedEffects = Constants.Quality.MEDIUM_USE_ADVANCED_EFFECTS;
                break;

            case RenderQuality.High:
                _useAntiAlias = true;
                _samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                _useAdvancedEffects = Constants.Quality.HIGH_USE_ADVANCED_EFFECTS;
                break;
        }
    }

    private void UpdatePaintProperties()
    {
        _barPaint.IsAntialias = _useAntiAlias;
        _highlightPaint.IsAntialias = _useAntiAlias;
        _reflectionPaint.IsAntialias = _useAntiAlias;

        if (_glowPaint != null)
        {
            _glowPaint.IsAntialias = _useAntiAlias;
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
        if (!ValidateRenderParameters(canvas, spectrum, info, paint)) return;

        ExecuteSafely(
            () =>
            {
                RenderBars(canvas, spectrum, info, barWidth, barSpacing, paint);
            },
            "RenderEffect",
            "Error during rendering"
        );
    }

    private bool ValidateRenderParameters(
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
        if (spectrum != null && spectrum.Length > 1) return true;
        Log(LogLevel.Error, LOG_PREFIX, "Spectrum is null or too small");
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

    private void RenderBars(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        SKPaint basePaint)
    {
        float totalBarWidth = barWidth + barSpacing;
        float canvasHeight = info.Height;
        float startX = CalculateStartingX(info.Width, spectrum.Length, totalBarWidth, barSpacing);
        float loudness = CalculateLoudness(spectrum);
        float reflectionHeight = canvasHeight * REFLECTION_HEIGHT;

        RenderBarElements(canvas, spectrum, canvasHeight, startX, barWidth, totalBarWidth, loudness, reflectionHeight);

        _barPaint.Shader = null;
    }

    private static float CalculateStartingX(float width, int spectrumLength, float totalBarWidth, float barSpacing) =>
        (width - (spectrumLength * totalBarWidth - barSpacing)) / 2f;

    private void RenderBarElements(
        SKCanvas canvas,
        float[] spectrum,
        float canvasHeight,
        float startX,
        float barWidth,
        float totalBarWidth,
        float loudness,
        float reflectionHeight)
    {
        for (int i = 0; i < spectrum.Length; i++)
        {
            float magnitude = Clamp(spectrum[i], 0f, 1f);
            if (magnitude < MIN_MAGNITUDE_THRESHOLD)
                continue;

            RenderSingleBar(canvas, canvasHeight, startX, i, totalBarWidth, barWidth, magnitude, loudness, reflectionHeight);
        }
    }

    private void RenderSingleBar(
        SKCanvas canvas,
        float canvasHeight,
        float startX,
        int index,
        float totalBarWidth,
        float barWidth,
        float magnitude,
        float loudness,
        float reflectionHeight)
    {
        float barHeight = magnitude * canvasHeight;
        float x = startX + index * totalBarWidth;
        float y = canvasHeight - barHeight;
        var barRect = new SKRect(x, y, x + barWidth, canvasHeight);

        if (canvas.QuickReject(barRect))
            return;

        SKColor barColor = GetBarColor(magnitude);
        DrawBar(canvas, barRect, barColor, magnitude, loudness, x, y, barWidth, barHeight, canvasHeight, reflectionHeight);
    }

    private void DrawBar(
        SKCanvas canvas,
        SKRect barRect,
        SKColor barColor,
        float magnitude,
        float loudness,
        float x,
        float y,
        float barWidth,
        float barHeight,
        float canvasHeight,
        float reflectionHeight)
    {
        DrawGlowIfNeeded(canvas, barRect, barColor, magnitude, loudness);
        DrawMainBar(canvas, barRect, barColor, magnitude, x, y, barWidth);

        if (barHeight <= CORNER_RADIUS * 2)
            return;

        DrawHighlight(canvas, x, y, barWidth, barHeight, magnitude);
        DrawReflectionIfNeeded(canvas, x, canvasHeight, barWidth, barHeight, barColor, magnitude, reflectionHeight);
    }

    private void DrawGlowIfNeeded(
        SKCanvas canvas,
        SKRect barRect,
        SKColor barColor,
        float magnitude,
        float loudness)
    {
        if (!ShouldDrawGlow(magnitude))
            return;

        DrawGlowEffect(canvas, barRect, barColor, magnitude, loudness);
    }

    private bool ShouldDrawGlow(float magnitude) =>
        _useAdvancedEffects && _glowPaint != null &&
        magnitude > GLOW_MIN_MAGNITUDE && magnitude <= GLOW_MAX_MAGNITUDE;

    private void DrawMainBar(
        SKCanvas canvas,
        SKRect barRect,
        SKColor barColor,
        float magnitude,
        float x,
        float y,
        float barWidth)
    {
        using var shader = CreateBarShader(barColor, x, y, barWidth);
        byte barAlpha = CalculateBarAlpha(magnitude);

        _barPaint.Color = barColor.WithAlpha(barAlpha);
        _barPaint.Shader = shader;
        canvas.DrawRoundRect(barRect, CORNER_RADIUS, CORNER_RADIUS, _barPaint);
    }

    private static SKShader CreateBarShader(SKColor barColor, float x, float y, float barWidth)
    {
        SKColor[] colors = [barColor, barColor.WithAlpha((byte)(MAX_ALPHA * GRADIENT_ALPHA_FACTOR))];
        float[] positions = [0f, 1f];

        return SKShader.CreateLinearGradient(
            new SKPoint(x, y),
            new SKPoint(x + barWidth, y),
            colors,
            positions,
            SKShaderTileMode.Clamp);
    }

    private static byte CalculateBarAlpha(float magnitude) =>
        (byte)Clamp(magnitude * ALPHA_MULTIPLIER * MAX_ALPHA, 0, MAX_ALPHA);

    private void DrawReflectionIfNeeded(
        SKCanvas canvas,
        float x,
        float canvasHeight,
        float barWidth,
        float barHeight,
        SKColor barColor,
        float magnitude,
        float reflectionHeight)
    {
        if (!ShouldDrawReflection(magnitude))
            return;

        DrawReflection(canvas, x, canvasHeight, barWidth, barHeight, barColor, magnitude, reflectionHeight);
    }

    private bool ShouldDrawReflection(float magnitude) =>
        _useAdvancedEffects && magnitude > REFLECTION_MIN_MAGNITUDE;

    private void DrawGlowEffect(
        SKCanvas canvas,
        SKRect barRect,
        SKColor barColor,
        float magnitude,
        float loudness)
    {
        UpdateGlowRadiusIfNeeded(loudness);

        byte glowAlpha = (byte)Clamp(magnitude * MAX_ALPHA * GLOW_INTENSITY, 0, MAX_ALPHA);
        _glowPaint!.Color = barColor.WithAlpha(glowAlpha);
        canvas.DrawRoundRect(barRect, CORNER_RADIUS, CORNER_RADIUS, _glowPaint);
    }

    private void UpdateGlowRadiusIfNeeded(float loudness)
    {
        float adjustedGlowRadius = GLOW_RADIUS * (1 + loudness * GLOW_LOUDNESS_FACTOR);
        if (MathF.Abs(adjustedGlowRadius - GLOW_RADIUS) > GLOW_RADIUS_THRESHOLD)
        {
            _glowPaint!.ImageFilter = SKImageFilter.CreateBlur(adjustedGlowRadius, adjustedGlowRadius);
        }
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
        float highlightHeight = MathF.Min(barHeight * HIGHLIGHT_HEIGHT_PROP, CORNER_RADIUS);
        byte highlightAlpha = (byte)Clamp(magnitude * MAX_ALPHA * HIGHLIGHT_ALPHA, 0, MAX_ALPHA);

        _highlightPaint.Color = SKColors.White.WithAlpha(highlightAlpha);

        float highlightX = x + (barWidth - highlightWidth) / 2;
        canvas.DrawRect(highlightX, y, highlightWidth, highlightHeight, _highlightPaint);
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
        byte reflectionAlpha = (byte)Clamp(magnitude * MAX_ALPHA * REFLECTION_OPACITY, 0, MAX_ALPHA);
        _reflectionPaint.Color = barColor.WithAlpha(reflectionAlpha);

        float reflectHeight = MathF.Min(barHeight * REFLECTION_FACTOR, reflectionHeight);
        canvas.DrawRect(x, canvasHeight, barWidth, reflectHeight, _reflectionPaint);
    }

    private SKColor GetBarColor(float magnitude) =>
        _colorCache != null ? _colorCache[GetColorIndex(magnitude)] : GetRainbowColor(magnitude);

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
            float weight = GetFrequencyWeight(i, subBass, bass, mid);
            sum += MathF.Abs(spectrum[i]) * weight;
        }

        return Clamp(sum / length * LOUDNESS_SCALE, 0f, 1f);
    }

    private static float GetFrequencyWeight(int index, int subBass, int bass, int mid)
    {
        if (index < subBass) return SUB_BASS_WEIGHT;
        if (index < bass) return BASS_WEIGHT;
        if (index < mid) return MID_WEIGHT;
        return HIGH_WEIGHT;
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
                DisposeManagedResources();
                base.OnDispose();
            },
            "OnDispose",
            "Error during specific disposal"
        );
    }

    private void DisposeManagedResources()
    {
        _path?.Dispose();
        _glowPaint?.Dispose();
        _barPaint?.Dispose();
        _highlightPaint?.Dispose();
        _reflectionPaint?.Dispose();

        _previousSpectrum = null;
        _colorCache = null;
    }

    private static void ExecuteSafely(Action action, string source, string errorMessage) =>
        Safe(action, new ErrorHandlingOptions
        {
            Source = $"{LOG_PREFIX}.{source}",
            ErrorMessage = errorMessage
        });
}