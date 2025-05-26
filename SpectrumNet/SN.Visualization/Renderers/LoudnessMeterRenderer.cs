#nullable enable

using static System.MathF;
using static SpectrumNet.SN.Visualization.Renderers.LoudnessMeterRenderer.Constants;

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class LoudnessMeterRenderer : EffectSpectrumRenderer
{
    private const string LogPrefix = nameof(LoudnessMeterRenderer);

    private static readonly Lazy<LoudnessMeterRenderer> _instance =
        new(() => new LoudnessMeterRenderer());

    private LoudnessMeterRenderer() { }

    public static LoudnessMeterRenderer GetInstance() => _instance.Value;

    public static class Constants
    {
        public const float
            MIN_LOUDNESS_THRESHOLD = 0.001f,
            SMOOTHING_FACTOR_ATTACK_NORMAL = 0.6f,
            SMOOTHING_FACTOR_RELEASE_NORMAL = 0.2f,
            SMOOTHING_FACTOR_ATTACK_OVERLAY = 0.8f,
            SMOOTHING_FACTOR_RELEASE_OVERLAY = 0.3f,
            PEAK_DECAY_RATE = 0.05f,
            HIGH_LOUDNESS_THRESHOLD = 0.7f,
            MEDIUM_LOUDNESS_THRESHOLD = 0.4f,
            BORDER_WIDTH = 1.5f,
            PEAK_RECT_HEIGHT = 4f,
            BLUR_SIGMA = 10f;

        public const int MARKER_COUNT = 10;

        public static readonly Dictionary<RenderQuality, QualitySettings> QualityPresets = new()
        {
            [RenderQuality.Low] = new(
                UseAdvancedEffects: false,
                UseAntialiasing: false,
                GlowIntensity: 0f,
                BlurSigma: 0f,
                GlowHeightFactor: 0f,
                GradientAlphaFactor: 0.8f,
                DynamicGradientFactor: 0f
            ),
            [RenderQuality.Medium] = new(
                UseAdvancedEffects: true,
                UseAntialiasing: true,
                GlowIntensity: 0.3f,
                BlurSigma: 8f,
                GlowHeightFactor: 0.25f,
                GradientAlphaFactor: 0.9f,
                DynamicGradientFactor: 0.6f
            ),
            [RenderQuality.High] = new(
                UseAdvancedEffects: true,
                UseAntialiasing: true,
                GlowIntensity: 0.5f,
                BlurSigma: 12f,
                GlowHeightFactor: 0.5f,
                GradientAlphaFactor: 1.0f,
                DynamicGradientFactor: 1.0f
            )
        };

        public record QualitySettings(
            bool UseAdvancedEffects,
            bool UseAntialiasing,
            float GlowIntensity,
            float BlurSigma,
            float GlowHeightFactor,
            float GradientAlphaFactor,
            float DynamicGradientFactor
        );
    }

    private float _previousLoudness;
    private float? _cachedLoudness;
    private int _currentWidth, _currentHeight;
    private SKPaint? _fillPaint, _glowPaint;
    private QualitySettings _currentSettings = QualityPresets[RenderQuality.Medium];
    private readonly SemaphoreSlim _loudnessSemaphore = new(1, 1);
    private readonly object _loudnessLock = new();
    private static readonly float[] _defaultColorPositions = [0f, 0.5f, 1.0f];

    protected override void OnInitialize()
    {
        ResetState();
        InitializePaints();
        LogDebug("Initialized");
    }

    protected override void OnQualitySettingsApplied()
    {
        _currentSettings = QualityPresets[Quality];
        UpdatePaintsForQuality();
        LogDebug($"Quality changed to {Quality}");
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
        SafeExecute(
            () => RenderLoudnessMeter(canvas, spectrum, info),
            "Error during rendering"
        );
    }

    private void RenderLoudnessMeter(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info)
    {
        UpdateDimensions(info);
        float loudness = ProcessLoudnessData(spectrum);

        if (loudness < MIN_LOUDNESS_THRESHOLD)
            return;

        RenderMeter(canvas, info, loudness);
    }

    private void UpdateDimensions(SKImageInfo info)
    {
        if (info.Width != _currentWidth || info.Height != _currentHeight)
        {
            _currentWidth = info.Width;
            _currentHeight = info.Height;
            RequestRedraw();
        }
    }

    private float ProcessLoudnessData(float[] spectrum)
    {
        bool semaphoreAcquired = false;
        try
        {
            semaphoreAcquired = _loudnessSemaphore.Wait(0);
            if (semaphoreAcquired)
            {
                float loudness = CalculateAndSmoothLoudness(spectrum);
                _cachedLoudness = loudness;
                return loudness;
            }
            else
            {
                lock (_loudnessLock)
                {
                    return _cachedLoudness ?? CalculateAndSmoothLoudness(spectrum);
                }
            }
        }
        finally
        {
            if (semaphoreAcquired)
                _loudnessSemaphore.Release();
        }
    }

    private void RenderMeter(
        SKCanvas canvas,
        SKImageInfo info,
        float loudness)
    {
        canvas.Save();
        try
        {
            float meterHeight = info.Height * loudness;
            DrawLoudnessFill(canvas, info, loudness, meterHeight);

            if (UseAdvancedEffects && _currentSettings.GlowIntensity > 0 &&
                loudness > MEDIUM_LOUDNESS_THRESHOLD)
            {
                DrawGlowEffect(canvas, info, loudness, meterHeight);
            }
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
        if (_fillPaint == null) return;

        _fillPaint.Shader = CreateLoudnessGradientShader(info, loudness);
        canvas.DrawRect(
            0,
            info.Height - meterHeight,
            info.Width,
            meterHeight,
            _fillPaint);
    }

    private SKShader CreateLoudnessGradientShader(
        SKImageInfo info,
        float loudness)
    {
        byte alpha = (byte)(255 * _currentSettings.GradientAlphaFactor);
        var gradientColors = new[]
        {
            SKColors.Green.WithAlpha(alpha),
            SKColors.Yellow.WithAlpha(alpha),
            SKColors.Red.WithAlpha(alpha)
        };

        float[] colorPositions = UseAdvancedEffects
            ? [0f, Clamp(loudness * _currentSettings.DynamicGradientFactor, 0.2f, 0.8f), 1.0f]
            : _defaultColorPositions;

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
        if (_glowPaint == null) return;

        UpdateGlowPaintColor(loudness);
        float glowHeight = meterHeight * _currentSettings.GlowHeightFactor;

        canvas.DrawRect(
            0,
            info.Height - meterHeight,
            info.Width,
            glowHeight,
            _glowPaint);
    }

    private void UpdateGlowPaintColor(float loudness)
    {
        if (_glowPaint == null) return;

        float normalizedLoudness = Clamp(
            (loudness - MEDIUM_LOUDNESS_THRESHOLD) / (1.0f - MEDIUM_LOUDNESS_THRESHOLD),
            0f,
            1f);

        byte interpolatedG = (byte)(255 * (1f - normalizedLoudness));
        SKColor interpolatedColor = new(255, interpolatedG, 0);
        byte finalAlpha = (byte)(255 * _currentSettings.GlowIntensity * normalizedLoudness);

        _glowPaint.Color = interpolatedColor.WithAlpha(finalAlpha);
    }

    private float CalculateAndSmoothLoudness(float[] spectrum)
    {
        float rawLoudness = CalculateLoudness(spectrum.AsSpan());

        float effectiveSmoothingFactor = rawLoudness > _previousLoudness
            ? (IsOverlayActive ? SMOOTHING_FACTOR_ATTACK_OVERLAY : SMOOTHING_FACTOR_ATTACK_NORMAL)
            : (IsOverlayActive ? SMOOTHING_FACTOR_RELEASE_OVERLAY : SMOOTHING_FACTOR_RELEASE_NORMAL);

        float smoothedLoudness = _previousLoudness +
            (rawLoudness - _previousLoudness) * effectiveSmoothingFactor;
        smoothedLoudness = Clamp(smoothedLoudness, MIN_LOUDNESS_THRESHOLD, 1f);
        _previousLoudness = smoothedLoudness;

        return smoothedLoudness;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float CalculateLoudness(ReadOnlySpan<float> spectrum)
    {
        if (spectrum.IsEmpty) return 0f;

        float sum = 0f;

        if (UseAdvancedEffects && spectrum.Length >= Vector<float>.Count)
        {
            sum = CalculateLoudnessVectorized(spectrum);
        }
        else
        {
            for (int i = 0; i < spectrum.Length; i++)
                sum += MathF.Abs(spectrum[i]);
        }

        return Clamp(sum / spectrum.Length, 0f, 1f);
    }

    private static float CalculateLoudnessVectorized(ReadOnlySpan<float> spectrum)
    {
        int vectorSize = Vector<float>.Count;
        int vectorizedLength = spectrum.Length - spectrum.Length % vectorSize;
        Vector<float> sumVector = Vector<float>.Zero;
        float sum = 0f;

        for (int i = 0; i < vectorizedLength; i += vectorSize)
        {
            Vector<float> values = new(spectrum.Slice(i, vectorSize));
            sumVector += System.Numerics.Vector.Abs(values);
        }

        for (int j = 0; j < vectorSize; j++)
            sum += sumVector[j];

        for (int i = vectorizedLength; i < spectrum.Length; i++)
            sum += MathF.Abs(spectrum[i]);

        return sum;
    }

    private void ResetState()
    {
        _previousLoudness = 0f;
        _cachedLoudness = null;
        _currentWidth = 0;
        _currentHeight = 0;
    }

    private void InitializePaints()
    {
        _fillPaint = CreatePaint(SKColors.Black, SKPaintStyle.Fill, 0);
        _glowPaint = CreatePaint(
            SKColors.Red.WithAlpha(0),
            SKPaintStyle.Fill,
            0,
            createBlur: true,
            blurRadius: _currentSettings.BlurSigma);
    }

    private void UpdatePaintsForQuality()
    {
        if (_fillPaint != null)
            _fillPaint.IsAntialias = UseAntiAlias;

        if (_glowPaint != null)
        {
            _glowPaint.IsAntialias = UseAntiAlias;
            _glowPaint.MaskFilter?.Dispose();
            _glowPaint.MaskFilter = _currentSettings.BlurSigma > 0
                ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, _currentSettings.BlurSigma)
                : null;
        }
    }

    private SKPaint CreatePaint(
        SKColor color,
        SKPaintStyle style,
        float strokeWidth,
        SKStrokeCap strokeCap = SKStrokeCap.Butt,
        bool createBlur = false,
        float blurRadius = 0)
    {
        var paint = GetPaint();
        paint.Color = color;
        paint.Style = style;
        paint.IsAntialias = UseAntiAlias;
        paint.StrokeWidth = strokeWidth;
        paint.StrokeCap = strokeCap;

        if (createBlur && blurRadius > 0)
        {
            paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, blurRadius);
        }

        return paint;
    }

    protected override void CleanupUnusedResources()
    {
        if (_fillPaint?.Shader != null)
        {
            _fillPaint.Shader.Dispose();
            _fillPaint.Shader = null;
        }
    }

    protected override void OnDispose()
    {
        _loudnessSemaphore?.Dispose();

        if (_fillPaint != null)
        {
            _fillPaint.Shader?.Dispose();
            ReturnPaint(_fillPaint);
            _fillPaint = null;
        }

        if (_glowPaint != null)
        {
            _glowPaint.MaskFilter?.Dispose();
            ReturnPaint(_glowPaint);
            _glowPaint = null;
        }

        _cachedLoudness = null;
        LogDebug("Disposed");
    }
}