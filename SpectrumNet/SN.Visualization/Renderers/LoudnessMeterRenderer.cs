#nullable enable

using static SpectrumNet.SN.Visualization.Renderers.LoudnessMeterRenderer.Constants;
using static SpectrumNet.SN.Visualization.Renderers.LoudnessMeterRenderer.Constants.Quality;

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class LoudnessMeterRenderer : EffectSpectrumRenderer
{
    private static readonly Lazy<LoudnessMeterRenderer> _instance = new(() => new LoudnessMeterRenderer());

    public const string LOG_PREFIX = nameof(LoudnessMeterRenderer);

    private LoudnessMeterRenderer() { }

    public static LoudnessMeterRenderer GetInstance() => _instance.Value;

    public record Constants
    {
        public const float
            MIN_LOUDNESS_THRESHOLD = 0.001f,
            SMOOTHING_FACTOR_ATTACK_NORMAL = 0.6f,
            SMOOTHING_FACTOR_RELEASE_NORMAL = 0.2f,
            SMOOTHING_FACTOR_ATTACK_OVERLAY = 0.8f,
            SMOOTHING_FACTOR_RELEASE_OVERLAY = 0.3f,
            PEAK_DECAY_RATE = 0.05f;

        public const float
            HIGH_LOUDNESS_THRESHOLD = 0.7f,
            MEDIUM_LOUDNESS_THRESHOLD = 0.4f,
            BORDER_WIDTH = 1.5f,
            PEAK_RECT_HEIGHT = 4f,
            BLUR_SIGMA = 10f;

        public const int MARKER_COUNT = 10;

        public const float
            GLOW_INTENSITY_LOW = 0f,
            GLOW_INTENSITY_MEDIUM = 0.3f,
            GLOW_INTENSITY_HIGH = 0.5f;

        public const float
            BLUR_SIGMA_LOW = 0f,
            BLUR_SIGMA_MEDIUM = 8f,
            BLUR_SIGMA_HIGH = 12f;

        public const float
            GLOW_HEIGHT_FACTOR_LOW = 0f,
            GLOW_HEIGHT_FACTOR_MEDIUM = 1f / 4f,
            GLOW_HEIGHT_FACTOR_HIGH = 1f / 2f;

        public const float
            GRADIENT_ALPHA_FACTOR_LOW = 0.8f,
            GRADIENT_ALPHA_FACTOR_MEDIUM = 0.9f,
            GRADIENT_ALPHA_FACTOR_HIGH = 1.0f;

        public const float
            DYNAMIC_GRADIENT_FACTOR_LOW = 0f,
            DYNAMIC_GRADIENT_FACTOR_MEDIUM = 0.6f,
            DYNAMIC_GRADIENT_FACTOR_HIGH = 1.0f;

        public static class Quality
        {
            public const bool
                LOW_USE_ADVANCED_EFFECTS = false,
                MEDIUM_USE_ADVANCED_EFFECTS = true,
                HIGH_USE_ADVANCED_EFFECTS = true;

            public const bool
                LOW_USE_ANTIALIASING = false,
                MEDIUM_USE_ANTIALIASING = true,
                HIGH_USE_ANTIALIASING = true;
        }
    }

    private float
        _previousLoudness;

    private float? _cachedLoudness;

    private int
        _currentWidth,
        _currentHeight;

    private SKPaint? _fillPaint;
    private SKPaint? _glowPaint;

    private float
        _currentGlowIntensity,
        _currentBlurSigma,
        _currentGlowHeightFactor,
        _currentGradientAlphaFactor,
        _currentDynamicGradientFactor;

    private readonly SemaphoreSlim _loudnessSemaphore = new(1, 1);
    private readonly object _loudnessLock = new();
    private static readonly float[] action = [0f, 0.5f, 1.0f];

    protected override void OnInitialize()
    {
        _logger.Safe(() =>
        {
            base.OnInitialize();

            _previousLoudness = 0f;
            _cachedLoudness = null;
            _currentWidth = 0;
            _currentHeight = 0;

            _currentGlowIntensity = GLOW_INTENSITY_LOW;
            _currentBlurSigma = BLUR_SIGMA_LOW;
            _currentGlowHeightFactor = GLOW_HEIGHT_FACTOR_LOW;
            _currentGradientAlphaFactor = GRADIENT_ALPHA_FACTOR_LOW;
            _currentDynamicGradientFactor = DYNAMIC_GRADIENT_FACTOR_LOW;

            ApplyQualitySettingsInternal();
            InitializePaints();

        }, LOG_PREFIX, "Failed to initialize renderer");
    }

    private void InitializePaints()
    {
        _fillPaint = InitPaint(SKColors.Black, SKPaintStyle.Fill, null);
        _glowPaint = InitPaint(SKColors.Red.WithAlpha(0), SKPaintStyle.Fill, null);
    }

    protected override void OnConfigurationChanged() { }

    protected override void OnQualitySettingsApplied()
    {
        ApplyQualitySettingsInternal();
        UpdatePaintsForQuality();
    }

    private void UpdatePaintsForQuality()
    {
        if (_fillPaint != null) _fillPaint.IsAntialias = _useAntiAlias;
        if (_glowPaint != null) _glowPaint.IsAntialias = _useAntiAlias;

        if (_glowPaint != null)
        {
            _glowPaint.MaskFilter?.Dispose();
            _glowPaint.MaskFilter = SKMaskFilter.CreateBlur(
                SKBlurStyle.Normal,
                _currentBlurSigma);
        }
    }


    private void ApplyQualitySettingsInternal()
    {
        switch (Quality)
        {
            case RenderQuality.Low:
                LowQualitySettings();
                break;
            case RenderQuality.Medium:
                MediumQualitySettings();
                break;
            case RenderQuality.High:
                HighQualitySettings();
                break;
        }
    }

    private void LowQualitySettings()
    {
        _useAdvancedEffects = LOW_USE_ADVANCED_EFFECTS;
        _useAntiAlias = LOW_USE_ANTIALIASING;
        _currentGlowIntensity = GLOW_INTENSITY_LOW;
        _currentBlurSigma = BLUR_SIGMA_LOW;
        _currentGlowHeightFactor = GLOW_HEIGHT_FACTOR_LOW;
        _currentGradientAlphaFactor = GRADIENT_ALPHA_FACTOR_LOW;
        _currentDynamicGradientFactor = DYNAMIC_GRADIENT_FACTOR_LOW;
    }

    private void MediumQualitySettings()
    {
        _useAdvancedEffects = MEDIUM_USE_ADVANCED_EFFECTS;
        _useAntiAlias = MEDIUM_USE_ANTIALIASING;
        _currentGlowIntensity = GLOW_INTENSITY_MEDIUM;
        _currentBlurSigma = BLUR_SIGMA_MEDIUM;
        _currentGlowHeightFactor = GLOW_HEIGHT_FACTOR_MEDIUM;
        _currentGradientAlphaFactor = GRADIENT_ALPHA_FACTOR_MEDIUM;
        _currentDynamicGradientFactor = DYNAMIC_GRADIENT_FACTOR_MEDIUM;
    }

    private void HighQualitySettings()
    {
        _useAdvancedEffects = HIGH_USE_ADVANCED_EFFECTS;
        _useAntiAlias = HIGH_USE_ANTIALIASING;
        _currentGlowIntensity = GLOW_INTENSITY_HIGH;
        _currentBlurSigma = BLUR_SIGMA_HIGH;
        _currentGlowHeightFactor = GLOW_HEIGHT_FACTOR_HIGH;
        _currentGradientAlphaFactor = GRADIENT_ALPHA_FACTOR_HIGH;
        _currentDynamicGradientFactor = DYNAMIC_GRADIENT_FACTOR_HIGH;
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
        if (canvas == null || spectrum == null || spectrum.Length == 0 ||
            paint == null || info.Width <= 0 || info.Height <= 0 || _disposed)
            return;

        _logger.Safe(() =>
        {
            CheckAndUpdateDimensions(info);

            float loudness = ProcessLoudnessData(spectrum);

            RenderMeter(
                canvas,
                info,
                loudness);

        }, LOG_PREFIX, "Error during rendering");
    }

    private void CheckAndUpdateDimensions(SKImageInfo info)
    {
        if (info.Width != _currentWidth || info.Height != _currentHeight)
        {
            _currentWidth = info.Width;
            _currentHeight = info.Height;
        }
    }

    private float ProcessLoudnessData(float[] spectrum)
    {
        float loudness = 0f;
        bool semaphoreAcquired = false;

        try
        {
            semaphoreAcquired = _loudnessSemaphore.Wait(0);

            if (semaphoreAcquired)
            {
                loudness = CalculateAndSmoothLoudness(spectrum);
                _cachedLoudness = loudness;
            }
            else
            {
                lock (_loudnessLock)
                {
                    loudness = _cachedLoudness ?? CalculateAndSmoothLoudness(spectrum);
                }
            }
        }
        finally
        {
            if (semaphoreAcquired)
                _loudnessSemaphore.Release();
        }

        return loudness;
    }

    private void RenderMeter(
        SKCanvas canvas,
        SKImageInfo info,
        float loudness)
    {
        if (loudness < MIN_LOUDNESS_THRESHOLD)
            return;

        canvas.Save();

        try
        {
            float meterHeight = info.Height * loudness;

            DrawLoudnessFill(
                canvas,
                info,
                loudness,
                meterHeight);

            DrawGlowEffect(
                canvas,
                info,
                loudness,
                meterHeight);
        }
        finally
        {
            canvas.Restore();
        }
    }

    private void DrawLoudnessFill(
        SKCanvas canvas,
        SKImageInfo info,
        float loudness,
        float meterHeight)
    {
        if (_fillPaint != null)
        {
            _fillPaint.Shader = CreateLoudnessGradientShader(info, loudness);

            canvas.DrawRect(
                0,
                info.Height - meterHeight,
                info.Width,
                meterHeight,
                _fillPaint);
        }
    }

    private SKShader CreateLoudnessGradientShader(
        SKImageInfo info,
        float loudness)
    {
        byte alpha = (byte)(255 * _currentGradientAlphaFactor);
        var gradientColors = new[]
       {
            SKColors.Green.WithAlpha(alpha),
            SKColors.Yellow.WithAlpha(alpha),
            SKColors.Red.WithAlpha(alpha)
        };
        float[] colorPositions;

        if (_useAdvancedEffects)
        {
            float dynamicPos = Clamp(
                loudness * _currentDynamicGradientFactor,
                0.2f,
                0.8f);
            colorPositions = [0f, dynamicPos, 1.0f];
        }
        else
        {
            colorPositions = action;
        }

        return SKShader.CreateLinearGradient(
            new SKPoint(0, info.Height),
            new SKPoint(0, 0),
            gradientColors,
            colorPositions,
            SKShaderTileMode.Clamp);
    }


    private void DrawGlowEffect(
        SKCanvas canvas,
        SKImageInfo info,
        float loudness,
        float meterHeight)
    {
        if (_useAdvancedEffects && _currentGlowIntensity > 0 &&
            loudness > MEDIUM_LOUDNESS_THRESHOLD && _glowPaint != null)
        {
            UpdateGlowPaintColor(loudness);

            float glowHeight = CalculateGlowHeight(meterHeight);

            canvas.DrawRect(
                0,
                info.Height - meterHeight,
                info.Width,
                glowHeight,
                _glowPaint);
        }
    }

    private void UpdateGlowPaintColor(float loudness)
    {
        if (_glowPaint == null) return;

        float normalizedLoudness = Clamp(
            (loudness - MEDIUM_LOUDNESS_THRESHOLD) /
            (1.0f - MEDIUM_LOUDNESS_THRESHOLD),
            0f,
            1f);

        byte interpolatedR = 255;
        byte interpolatedG = (byte)(255 * (1f - normalizedLoudness));
        byte interpolatedB = 0;

        SKColor interpolatedColor = new(interpolatedR, interpolatedG, interpolatedB);

        byte finalAlpha = (byte)(255 * _currentGlowIntensity * normalizedLoudness);

        _glowPaint.Color = interpolatedColor.WithAlpha(finalAlpha);
    }

    private float CalculateGlowHeight(float meterHeight)
    {
        return meterHeight * _currentGlowHeightFactor;
    }


    private float CalculateAndSmoothLoudness(float[] spectrum)
    {
        float rawLoudness = CalculateLoudness(spectrum.AsSpan());
        float effectiveSmoothingFactor;

        if (rawLoudness > _previousLoudness)
        {
            effectiveSmoothingFactor = _isOverlayActive
                ? SMOOTHING_FACTOR_ATTACK_OVERLAY
                : SMOOTHING_FACTOR_ATTACK_NORMAL;
        }
        else
        {
            effectiveSmoothingFactor = _isOverlayActive
                ? SMOOTHING_FACTOR_RELEASE_OVERLAY
                : SMOOTHING_FACTOR_RELEASE_NORMAL;
        }

        float smoothedLoudness = _previousLoudness +
                                 (rawLoudness - _previousLoudness) * effectiveSmoothingFactor;
        smoothedLoudness = Clamp(smoothedLoudness, MIN_LOUDNESS_THRESHOLD, 1f);
        _previousLoudness = smoothedLoudness;
        return smoothedLoudness;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float CalculateLoudness(ReadOnlySpan<float> spectrum)
    {
        if (spectrum.IsEmpty)
            return 0f;

        float sum = 0f;

        if (_useAdvancedEffects && spectrum.Length >= Vector<float>.Count)
        {
            int vectorSize = Vector<float>.Count;
            int vectorizedLength = spectrum.Length - spectrum.Length % vectorSize;
            Vector<float> sumVector = Vector<float>.Zero;
            int i = 0;

            for (; i < vectorizedLength; i += vectorSize)
            {
                Vector<float> values = new(spectrum.Slice(i, vectorSize));
                sumVector += System.Numerics.Vector.Abs(values);
            }

            for (int j = 0; j < vectorSize; j++)
                sum += sumVector[j];

            for (; i < spectrum.Length; i++)
                sum += Abs(spectrum[i]);
        }
        else
        {
            for (int i = 0; i < spectrum.Length; i++)
                sum += Abs(spectrum[i]);
        }

        return Clamp(sum / spectrum.Length, 0f, 1f);
    }

    protected override void OnDispose()
    {
        _logger.Safe(() =>
        {
            _loudnessSemaphore?.Dispose();

            if (_fillPaint != null) _paintPool.Return(_fillPaint);
            _fillPaint = null;

            if (_glowPaint != null)
            {
                _glowPaint.MaskFilter?.Dispose();
                _paintPool.Return(_glowPaint);
            }
            _glowPaint = null;

            _cachedLoudness = null;
            _previousLoudness = 0;

            base.OnDispose();
        }, LOG_PREFIX, "Error during disposal");
    }
}