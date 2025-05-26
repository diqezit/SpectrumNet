#nullable enable

using static SpectrumNet.SN.Visualization.Renderers.KenwoodBarsRenderer.Constants;
using static System.MathF;

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class KenwoodBarsRenderer : EffectSpectrumRenderer
{
    private const string LogPrefix = nameof(KenwoodBarsRenderer);

    private static readonly Lazy<KenwoodBarsRenderer> _instance =
        new(() => new KenwoodBarsRenderer());

    private KenwoodBarsRenderer() { }

    public static KenwoodBarsRenderer GetInstance() => _instance.Value;

    public static class Constants
    {
        public const float
            ANIMATION_SPEED = 0.85f,
            PEAK_FALL_SPEED = 0.007f,
            PEAK_HEIGHT = 3f,
            PEAK_HOLD_TIME_MS = 500f,
            MIN_BAR_HEIGHT = 1f,
            GLOW_EFFECT_ALPHA = 0.3f,
            ALPHA_MULTIPLIER = 1.5f,
            HIGH_INTENSITY_THRESHOLD = 0.4f,
            CORNER_RADIUS_FACTOR = 0.15f,
            MAX_CORNER_RADIUS = 5f;

        public const int
            RENDER_BATCH_SIZE = 32,
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
                IntensityThreshold: HIGH_INTENSITY_THRESHOLD * 1.5f,
                AlphaMultiplier: ALPHA_MULTIPLIER * 0.8f,
                EdgeStrokeWidth: 0f,
                EdgeBlurRadius: 0f,
                SmoothingFactor: 0.3f
            ),
            [RenderQuality.Medium] = new(
                UseGlow: true,
                UseEdge: true,
                GlowRadius: 2.0f,
                GlowAlpha: GLOW_EFFECT_ALPHA * 0.8f,
                IntensityThreshold: HIGH_INTENSITY_THRESHOLD * 1.2f,
                AlphaMultiplier: ALPHA_MULTIPLIER,
                EdgeStrokeWidth: 1.5f,
                EdgeBlurRadius: 1f,
                SmoothingFactor: 0.8f
            ),
            [RenderQuality.High] = new(
                UseGlow: true,
                UseEdge: true,
                GlowRadius: 3.0f,
                GlowAlpha: GLOW_EFFECT_ALPHA,
                IntensityThreshold: HIGH_INTENSITY_THRESHOLD,
                AlphaMultiplier: ALPHA_MULTIPLIER * 1.2f,
                EdgeStrokeWidth: 2.5f,
                EdgeBlurRadius: 2f,
                SmoothingFactor: 1.0f
            )
        };

        public record QualitySettings(
            bool UseGlow,
            bool UseEdge,
            float GlowRadius,
            float GlowAlpha,
            float IntensityThreshold,
            float AlphaMultiplier,
            float EdgeStrokeWidth,
            float EdgeBlurRadius,
            float SmoothingFactor
        );
    }

    private QualitySettings _currentSettings = QualityPresets[RenderQuality.Medium];
    private float _lastCanvasHeight;

    private float[]? _previousValues;
    private float[]? _peaks;
    private DateTime[]? _peakHoldTimes;
    private float[]? _scaledSpectrum;

    private SKShader? _barGradient;
    private SKPath? _glowPath;

    private readonly SKPaint _peakPaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Fill,
        Color = SKColors.White
    };

    protected override int GetMaxBarsForQuality() => Quality switch
    {
        RenderQuality.Low => MAX_BARS_LOW,
        RenderQuality.Medium => MAX_BARS_MEDIUM,
        RenderQuality.High => MAX_BARS_HIGH,
        _ => MAX_BARS_MEDIUM
    };

    protected override void OnInitialize()
    {
        base.OnInitialize();
        _glowPath = new SKPath();
        ApplyQualitySettings();
        LogDebug("Initialized");
    }

    protected override void OnQualitySettingsApplied()
    {
        _currentSettings = QualityPresets[Quality];
        ApplyQualitySettings();
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
        var renderParams = base.CalculateRenderParameters(info, barCount);

        UpdateGradientIfNeeded(info.Height);
        EnsureBuffers(renderParams.EffectiveBarCount);

        ScaleSpectrumToFixedSize(spectrum, renderParams.EffectiveBarCount);
        AnimateSpectrum(renderParams.EffectiveBarCount);

        RenderAllBars(canvas, info, renderParams);
        RenderAllPeaks(canvas, info, renderParams);
    }

    private void ApplyQualitySettings()
    {
        SetProcessingSmoothingFactor(_currentSettings.SmoothingFactor);
        UpdatePeakPaint();
    }

    private void UpdatePeakPaint() =>
        _peakPaint.IsAntialias = UseAntiAlias;

    private void UpdateGradientIfNeeded(float height)
    {
        if (MathF.Abs(_lastCanvasHeight - height) < 0.5f) return;

        _lastCanvasHeight = height;
        CreateGradient(height);
    }

    private void CreateGradient(float height)
    {
        _barGradient?.Dispose();
        _barGradient = SKShader.CreateLinearGradient(
            new SKPoint(0, height),
            new SKPoint(0, 0),
            BarColors,
            BarColorPositions,
            SKShaderTileMode.Clamp);
    }

    private void EnsureBuffers(int size)
    {
        if (_previousValues?.Length >= size) return;

        AllocateBuffers(size);
        InitializeBuffers();
    }

    private void AllocateBuffers(int size)
    {
        _previousValues = new float[size];
        _peaks = new float[size];
        _peakHoldTimes = new DateTime[size];
        _scaledSpectrum = new float[size];
    }

    private void InitializeBuffers()
    {
        if (_peakHoldTimes != null)
            Array.Fill(_peakHoldTimes, DateTime.MinValue);
    }

    private void ScaleSpectrumToFixedSize(float[] spectrum, int targetSize)
    {
        if (_scaledSpectrum == null || _scaledSpectrum.Length < targetSize) return;

        float scale = (float)spectrum.Length / targetSize;

        for (int i = 0; i < targetSize; i++)
        {
            int startIdx = (int)(i * scale);
            int endIdx = Min((int)((i + 1) * scale), spectrum.Length);

            float sum = 0f;
            int count = 0;

            for (int j = startIdx; j < endIdx; j++)
            {
                sum += spectrum[j];
                count++;
            }

            _scaledSpectrum[i] = count > 0 ? sum / count : 0f;
        }
    }

    private void AnimateSpectrum(int count)
    {
        if (!BuffersValid() || _scaledSpectrum == null) return;

        DateTime currentTime = DateTime.Now;

        for (int i = 0; i < count; i++)
            AnimateSingleBar(i, _scaledSpectrum[i], currentTime);
    }

    private bool BuffersValid() =>
        _previousValues != null &&
        _peaks != null &&
        _peakHoldTimes != null;

    private void AnimateSingleBar(int index, float targetValue, DateTime currentTime)
    {
        UpdateBarValue(index, targetValue);
        UpdatePeakValue(index, currentTime);
    }

    private void UpdateBarValue(int index, float targetValue)
    {
        float current = _previousValues![index];
        _previousValues[index] = Lerp(current, targetValue, ANIMATION_SPEED);
    }

    private void UpdatePeakValue(int index, DateTime currentTime)
    {
        float currentValue = _previousValues![index];
        float currentPeak = _peaks![index];

        if (currentValue > currentPeak)
            SetNewPeak(index, currentValue, currentTime);
        else if (ShouldFallPeak(index, currentTime))
            FallPeak(index);
    }

    private void SetNewPeak(int index, float value, DateTime time)
    {
        _peaks![index] = value;
        _peakHoldTimes![index] = time;
    }

    private bool ShouldFallPeak(int index, DateTime currentTime) =>
        (currentTime - _peakHoldTimes![index]).TotalMilliseconds > PEAK_HOLD_TIME_MS;

    private void FallPeak(int index)
    {
        float peakFallRate = PEAK_FALL_SPEED * ANIMATION_SPEED;
        _peaks![index] = MathF.Max(0, _peaks[index] - peakFallRate);
    }

    private void RenderAllBars(
        SKCanvas canvas,
        SKImageInfo info,
        RenderParameters renderParams)
    {
        if (!BuffersValid()) return;

        float cornerRadius = CalculateCornerRadius(renderParams.BarWidth);

        if (ShouldRenderGlow())
        {
            PrepareGlowPath(info, renderParams, cornerRadius);
            RenderGlow(canvas);
        }

        ProcessBatches(canvas, info, renderParams, cornerRadius);
    }

    private void PrepareGlowPath(
        SKImageInfo info,
        RenderParameters renderParams,
        float cornerRadius)
    {
        if (_glowPath == null) return;

        _glowPath.Reset();

        for (int i = 0; i < renderParams.EffectiveBarCount; i++)
        {
            float magnitude = _previousValues![i];
            if (magnitude < MIN_MAGNITUDE_THRESHOLD ||
                magnitude <= _currentSettings.IntensityThreshold) continue;

            float x = CalculateBarX(i, renderParams);
            var rect = CalculateBarRect(x, magnitude, renderParams.BarWidth, info.Height);

            _glowPath.AddRoundRect(rect, cornerRadius, cornerRadius);
        }
    }

    private void RenderGlow(SKCanvas canvas)
    {
        if (_glowPath == null || _glowPath.IsEmpty) return;

        using var glowPaint = GetPaint();
        glowPaint.Color = SKColors.White.WithAlpha(
            (byte)(_currentSettings.GlowAlpha * 255));
        glowPaint.Style = SKPaintStyle.Fill;
        glowPaint.IsAntialias = UseAntiAlias;
        glowPaint.Shader = _barGradient;
        glowPaint.MaskFilter = SKMaskFilter.CreateBlur(
            SKBlurStyle.Normal,
            _currentSettings.GlowRadius);

        canvas.DrawPath(_glowPath, glowPaint);
    }

    private static float CalculateCornerRadius(float barWidth) =>
        MathF.Min(barWidth * CORNER_RADIUS_FACTOR, MAX_CORNER_RADIUS);

    private void ProcessBatches(
        SKCanvas canvas,
        SKImageInfo info,
        RenderParameters renderParams,
        float cornerRadius)
    {
        for (int i = 0; i < renderParams.EffectiveBarCount; i += RENDER_BATCH_SIZE)
        {
            int batchEnd = Min(i + RENDER_BATCH_SIZE, renderParams.EffectiveBarCount);
            RenderBatch(canvas, info, i, batchEnd, renderParams, cornerRadius);
        }
    }

    private void RenderBatch(
        SKCanvas canvas,
        SKImageInfo info,
        int start,
        int end,
        RenderParameters renderParams,
        float cornerRadius)
    {
        for (int i = start; i < end; i++)
            RenderBarIfVisible(canvas, info, i, renderParams, cornerRadius);
    }

    private void RenderBarIfVisible(
        SKCanvas canvas,
        SKImageInfo info,
        int index,
        RenderParameters renderParams,
        float cornerRadius)
    {
        float magnitude = _previousValues![index];
        if (magnitude < MIN_MAGNITUDE_THRESHOLD) return;

        float x = CalculateBarX(index, renderParams);
        if (!IsRenderAreaVisible(canvas, x, 0, renderParams.BarWidth, info.Height)) return;

        RenderSingleBar(canvas, x, magnitude, renderParams.BarWidth, info.Height, cornerRadius);
    }

    private static float CalculateBarX(int index, RenderParameters renderParams) =>
        renderParams.StartOffset + index * (renderParams.BarWidth + renderParams.BarSpacing);

    private void RenderSingleBar(
        SKCanvas canvas,
        float x,
        float magnitude,
        float barWidth,
        float canvasHeight,
        float cornerRadius)
    {
        var rect = CalculateBarRect(x, magnitude, barWidth, canvasHeight);

        RenderBarFill(canvas, rect, cornerRadius);

        if (ShouldRenderEdge())
            RenderBarEdge(canvas, rect, cornerRadius, magnitude);
    }

    private static SKRect CalculateBarRect(float x, float magnitude, float barWidth, float canvasHeight)
    {
        float barHeight = MathF.Max(magnitude * canvasHeight, MIN_BAR_HEIGHT);
        float y = canvasHeight - barHeight;
        return new SKRect(x, y, x + barWidth, y + barHeight);
    }

    private bool ShouldRenderGlow() =>
        UseAdvancedEffects && _currentSettings.UseGlow;

    private bool ShouldRenderEdge() =>
        UseAdvancedEffects &&
        _currentSettings.UseEdge &&
        _currentSettings.EdgeStrokeWidth > 0;

    private void RenderBarFill(SKCanvas canvas, SKRect rect, float cornerRadius)
    {
        using var barPaint = CreateBarPaint();
        canvas.DrawRoundRect(rect, cornerRadius, cornerRadius, barPaint);
    }

    private void RenderBarEdge(SKCanvas canvas, SKRect rect, float cornerRadius, float magnitude)
    {
        using var edgePaint = CreateEdgePaint(magnitude);
        canvas.DrawRoundRect(rect, cornerRadius, cornerRadius, edgePaint);
    }

    private void RenderAllPeaks(
        SKCanvas canvas,
        SKImageInfo info,
        RenderParameters renderParams)
    {
        if (!BuffersValid()) return;

        float cornerRadius = CalculateCornerRadius(renderParams.BarWidth);

        for (int i = 0; i < renderParams.EffectiveBarCount && i < _peaks!.Length; i++)
            RenderSinglePeak(canvas, info, i, renderParams, cornerRadius);
    }

    private void RenderSinglePeak(
        SKCanvas canvas,
        SKImageInfo info,
        int index,
        RenderParameters renderParams,
        float cornerRadius)
    {
        float peakValue = _peaks![index];
        if (peakValue < MIN_MAGNITUDE_THRESHOLD) return;

        var peakRect = CalculatePeakRect(index, peakValue, renderParams, info.Height);
        canvas.DrawRoundRect(peakRect, cornerRadius, cornerRadius, _peakPaint);
    }

    private static SKRect CalculatePeakRect(
        int index,
        float peakValue,
        RenderParameters renderParams,
        float canvasHeight)
    {
        float x = CalculateBarX(index, renderParams);
        float peakY = canvasHeight - (peakValue * canvasHeight);

        return new SKRect(
            x,
            peakY - PEAK_HEIGHT,
            x + renderParams.BarWidth,
            peakY);
    }

    private SKPaint CreateBarPaint()
    {
        var paint = GetPaint();
        ConfigureBarPaint(paint);
        return paint;
    }

    private void ConfigureBarPaint(SKPaint paint)
    {
        paint.Color = SKColors.White;
        paint.Style = SKPaintStyle.Fill;
        paint.IsAntialias = UseAntiAlias;
        paint.Shader = _barGradient;
    }

    private SKPaint CreateEdgePaint(float magnitude)
    {
        var paint = GetPaint();
        ConfigureEdgePaint(paint, magnitude);
        return paint;
    }

    private void ConfigureEdgePaint(SKPaint paint, float magnitude)
    {
        paint.Color = SKColors.White.WithAlpha(
            (byte)(magnitude * _currentSettings.AlphaMultiplier * 255f));
        paint.Style = SKPaintStyle.Stroke;
        paint.IsAntialias = UseAntiAlias;
        ConfigureStroke(paint);
        ApplyEdgeBlur(paint);
    }

    private void ConfigureStroke(SKPaint paint)
    {
        paint.StrokeWidth = _currentSettings.EdgeStrokeWidth;
        paint.StrokeCap = SKStrokeCap.Round;
        paint.StrokeJoin = SKStrokeJoin.Round;
    }

    private void ApplyEdgeBlur(SKPaint paint)
    {
        if (_currentSettings.EdgeBlurRadius > 0)
        {
            paint.MaskFilter = SKMaskFilter.CreateBlur(
                SKBlurStyle.Normal,
                _currentSettings.EdgeBlurRadius);
        }
    }

    protected override void OnDispose()
    {
        DisposeResources();
        ClearBuffers();
        base.OnDispose();
        LogDebug("Disposed");
    }

    private void DisposeResources()
    {
        _peakPaint.Dispose();
        _barGradient?.Dispose();
        _glowPath?.Dispose();
    }

    private void ClearBuffers()
    {
        _previousValues = null;
        _peaks = null;
        _peakHoldTimes = null;
        _scaledSpectrum = null;
    }
}