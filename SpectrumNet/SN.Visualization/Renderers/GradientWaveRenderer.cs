#nullable enable

using static SpectrumNet.SN.Visualization.Renderers.GradientWaveRenderer.Constants;
using static System.MathF;

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class GradientWaveRenderer : EffectSpectrumRenderer
{
    private const string LogPrefix = nameof(GradientWaveRenderer);

    private static readonly Lazy<GradientWaveRenderer> _instance =
        new(() => new GradientWaveRenderer());

    private GradientWaveRenderer() { }

    public static GradientWaveRenderer GetInstance() => _instance.Value;

    public static class Constants
    {
        public const float
            EDGE_OFFSET = 10f,
            BASELINE_OFFSET = 2f,
            SMOOTHING_FACTOR_NORMAL = 0.3f,
            SMOOTHING_FACTOR_OVERLAY = 0.5f,
            SMOOTHING_FACTOR_TRANSITION = 0.7f,
            MIN_MAGNITUDE_THRESHOLD = 0.01f,
            MAX_SPECTRUM_VALUE = 1.5f,
            DEFAULT_LINE_WIDTH = 3f,
            GLOW_INTENSITY = 0.3f,
            HIGH_MAGNITUDE_THRESHOLD = 0.7f,
            FILL_OPACITY = 0.2f,
            LINE_GRADIENT_SATURATION = 100f,
            LINE_GRADIENT_LIGHTNESS = 50f,
            OVERLAY_GRADIENT_SATURATION = 100f,
            OVERLAY_GRADIENT_LIGHTNESS = 55f,
            MAX_BLUR_RADIUS = 6f,
            BAR_COUNT_TRANSITION_FRAMES = 10;

        public const int
            EXTRA_POINTS_COUNT = 4,
            BATCH_SIZE = 128;

        public static readonly Dictionary<RenderQuality, QualitySettings> QualityPresets = new()
        {
            [RenderQuality.Low] = new(20, 1),
            [RenderQuality.Medium] = new(40, 2),
            [RenderQuality.High] = new(80, 3)
        };

        public record QualitySettings(
            int PointCount,
            int SmoothingPasses);
    }

    private List<SKPoint>? _cachedPoints;
    private SKPicture? _cachedBackground;
    private QualitySettings _currentSettings = QualityPresets[RenderQuality.Medium];
    private readonly SemaphoreSlim _renderSemaphore = new(1, 1);

    private float[]? _previousScaledSpectrum;
    private float[] _processedSpectrum = [];
    private float _smoothingFactor = SMOOTHING_FACTOR_NORMAL;
    private int _previousBarCount = -1;
    private int _barCountTransitionFrames = 0;

    private readonly object _spectrumLock = new();

    protected override void OnInitialize()
    {
        base.OnInitialize();
        _smoothingFactor = SMOOTHING_FACTOR_NORMAL;
        LogDebug("Initialized");
    }

    protected override void OnConfigurationChanged()
    {
        _smoothingFactor = IsOverlayActive
            ? SMOOTHING_FACTOR_OVERLAY
            : SMOOTHING_FACTOR_NORMAL;

        SetProcessingSmoothingFactor(_smoothingFactor);
    }

    protected override void OnQualitySettingsApplied()
    {
        _currentSettings = QualityPresets[Quality];
        CleanupUnusedResources();
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
        if (canvas.QuickReject(new SKRect(0, 0, info.Width, info.Height)))
            return;

        UpdateState(spectrum, barCount);
        RenderFrame(canvas, spectrum, info, barCount, paint);
    }

    private void UpdateState(
        float[] spectrum,
        int barCount)
    {
        if (!_renderSemaphore.Wait(0))
            return;

        try
        {
            ProcessSpectrumData(spectrum, barCount);
        }
        finally
        {
            _renderSemaphore.Release();
        }
    }

    private void ProcessSpectrumData(
        float[] spectrum,
        int barCount)
    {
        int actualBarCount = Min(spectrum.Length, barCount);

        if (_previousBarCount != actualBarCount && _previousBarCount != -1)
            _barCountTransitionFrames = (int)BAR_COUNT_TRANSITION_FRAMES;

        var (isValid, scaledSpectrum) = PrepareSpectrum(
            spectrum,
            actualBarCount,
            spectrum.Length);

        if (!isValid || scaledSpectrum == null)
            return;

        EnsurePreviousSpectrumInitialized(scaledSpectrum, actualBarCount);
        _processedSpectrum = SmoothSpectrumData(scaledSpectrum, actualBarCount);

        lock (_spectrumLock)
        {
            _cachedPoints = null;
        }

        _previousBarCount = actualBarCount;
        if (_barCountTransitionFrames > 0)
            _barCountTransitionFrames--;
    }

    private void EnsurePreviousSpectrumInitialized(
        float[] scaledSpectrum,
        int actualBarCount)
    {
        if (_previousScaledSpectrum == null || _previousScaledSpectrum.Length != actualBarCount)
            _previousScaledSpectrum = _previousScaledSpectrum != null && _previousScaledSpectrum.Length > 0
                ? InterpolateSpectrum(_previousScaledSpectrum, actualBarCount)
                : (float[])scaledSpectrum.Clone();
    }

    private static float[] InterpolateSpectrum(
        float[] source,
        int targetLength)
    {
        var result = new float[targetLength];
        float ratio = (float)(source.Length - 1) / (targetLength - 1);

        for (int i = 0; i < targetLength; i++)
        {
            float sourceIndex = i * ratio;
            int index = (int)sourceIndex;
            float fraction = sourceIndex - index;

            result[i] = index < source.Length - 1
                ? source[index] * (1 - fraction) + source[index + 1] * fraction
                : source[^1];
        }

        return result;
    }

    private float[] SmoothSpectrumData(
        float[] spectrum,
        int targetCount) =>
        ApplySpatialSmoothing(
            ApplyTemporalSmoothing(spectrum, targetCount),
            targetCount);

    private float[] ApplyTemporalSmoothing(
        float[] spectrum,
        int targetCount)
    {
        if (_previousScaledSpectrum == null || _previousScaledSpectrum.Length != targetCount)
            _previousScaledSpectrum = (float[])spectrum.Clone();

        var smoothed = new float[targetCount];
        float effectiveSmoothingFactor = GetEffectiveSmoothingFactor();

        for (int i = 0; i < targetCount; i++)
        {
            float value = Lerp(
                _previousScaledSpectrum[i],
                spectrum[i],
                effectiveSmoothingFactor);

            smoothed[i] = Clamp(
                value,
                MIN_MAGNITUDE_THRESHOLD,
                MAX_SPECTRUM_VALUE);

            _previousScaledSpectrum[i] = smoothed[i];
        }

        return smoothed;
    }

    private float GetEffectiveSmoothingFactor() =>
        _barCountTransitionFrames > 0
            ? Lerp(
                SMOOTHING_FACTOR_TRANSITION,
                _smoothingFactor,
                1f - (_barCountTransitionFrames / BAR_COUNT_TRANSITION_FRAMES))
            : _smoothingFactor;

    private float[] ApplySpatialSmoothing(
        float[] spectrum,
        int targetCount)
    {
        var smoothed = spectrum;
        int passes = _barCountTransitionFrames > 0
            ? Min(_currentSettings.SmoothingPasses + 1, 3)
            : _currentSettings.SmoothingPasses;

        for (int pass = 0; pass < passes; pass++)
            smoothed = ApplySingleSpatialPass(smoothed, targetCount);

        return smoothed;
    }

    private static float[] ApplySingleSpatialPass(
        float[] spectrum,
        int targetCount)
    {
        var temp = new float[targetCount];

        for (int i = 0; i < targetCount; i++)
        {
            float sum = spectrum[i] * 2f;
            int count = 2;

            if (i > 0)
            {
                sum += spectrum[i - 1];
                count++;
            }

            if (i < targetCount - 1)
            {
                sum += spectrum[i + 1];
                count++;
            }

            temp[i] = sum / count;
        }

        return temp;
    }

    private void RenderFrame(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        int barCount,
        SKPaint paint)
    {
        var (renderSpectrum, renderPoints) = GetRenderingData(
            spectrum,
            info,
            barCount);

        RenderGradientWave(
            canvas,
            renderPoints,
            renderSpectrum,
            info,
            paint);
    }

    private (float[] spectrum, List<SKPoint> points) GetRenderingData(
        float[] spectrum,
        SKImageInfo info,
        int barCount)
    {
        lock (_spectrumLock)
        {
            var renderSpectrum = _processedSpectrum.Length > 0
                ? _processedSpectrum
                : ProcessSpectrumSynchronously(spectrum, barCount);

            var renderPoints = _cachedPoints ?? GenerateOptimizedPoints(renderSpectrum, info);

            return (renderSpectrum, renderPoints);
        }
    }

    private float[] ProcessSpectrumSynchronously(
        float[] spectrum,
        int barCount)
    {
        int actualBarCount = Min(spectrum.Length, barCount);

        var (isValid, scaledSpectrum) = PrepareSpectrum(
            spectrum,
            actualBarCount,
            spectrum.Length);

        return !isValid || scaledSpectrum == null
            ? new float[actualBarCount]
            : SmoothSpectrumData(scaledSpectrum, actualBarCount);
    }

    private List<SKPoint> GenerateOptimizedPoints(
        float[] spectrum,
        SKImageInfo info)
    {
        if (spectrum.Length < 1)
            return [];

        float minY = EDGE_OFFSET;
        float maxY = info.Height - EDGE_OFFSET;
        int pointCount = Min(_currentSettings.PointCount, spectrum.Length);

        var points = new List<SKPoint>(pointCount + EXTRA_POINTS_COUNT)
        {
            new(-EDGE_OFFSET, maxY),
            new(0, maxY)
        };

        AddInterpolatedPoints(
            points,
            spectrum,
            info,
            minY,
            maxY,
            pointCount);

        points.Add(new SKPoint(info.Width, maxY));
        points.Add(new SKPoint(info.Width + EDGE_OFFSET, maxY));

        return points;
    }

    private static void AddInterpolatedPoints(
        List<SKPoint> points,
        float[] spectrum,
        SKImageInfo info,
        float minY,
        float maxY,
        int pointCount)
    {
        float step = (float)spectrum.Length / pointCount;

        for (int i = 0; i < pointCount; i++)
        {
            float position = i * step;
            int index = Min((int)position, spectrum.Length - 1);
            float remainder = position - index;

            float value = index < spectrum.Length - 1 && remainder > 0
                ? spectrum[index] * (1 - remainder) + spectrum[index + 1] * remainder
                : spectrum[index];

            float x = i / (float)(pointCount - 1) * info.Width;
            float y = maxY - value * (maxY - minY);
            points.Add(new SKPoint(x, y));
        }
    }

    private void RenderGradientWave(
        SKCanvas canvas,
        List<SKPoint> points,
        float[] spectrum,
        SKImageInfo info,
        SKPaint basePaint)
    {
        if (points.Count < 2)
            return;

        float maxMagnitude = CalculateMaxMagnitude(spectrum);
        float yBaseline = info.Height - EDGE_OFFSET + BASELINE_OFFSET;
        bool shouldRenderGlow = maxMagnitude > HIGH_MAGNITUDE_THRESHOLD && UseAdvancedEffects;

        var wavePath = GetPath();
        var fillPath = GetPath();

        CreateWavePaths(
            points,
            info,
            yBaseline,
            wavePath,
            fillPath);

        canvas.Save();
        try
        {
            RenderFillGradient(
                canvas,
                basePaint,
                maxMagnitude,
                yBaseline,
                fillPath);

            if (shouldRenderGlow)
                RenderGlowEffect(
                    canvas,
                    basePaint,
                    maxMagnitude,
                    spectrum.Length,
                    wavePath);

            RenderLineGradient(
                canvas,
                points,
                info,
                wavePath);
        }
        finally
        {
            canvas.Restore();
        }

        ReturnPath(wavePath);
        ReturnPath(fillPath);
    }

    private static float CalculateMaxMagnitude(float[] spectrum) =>
        spectrum.Length > 0 ? spectrum.Max() : 0f;

    private static void CreateWavePaths(
        List<SKPoint> points,
        SKImageInfo info,
        float yBaseline,
        SKPath wavePath,
        SKPath fillPath)
    {
        wavePath.Reset();
        fillPath.Reset();

        wavePath.MoveTo(points[0]);

        for (int i = 1; i < points.Count - 2; i++)
        {
            float xMid = (points[i].X + points[i + 1].X) / 2;
            float yMid = (points[i].Y + points[i + 1].Y) / 2;
            wavePath.QuadTo(points[i].X, points[i].Y, xMid, yMid);
        }

        if (points.Count >= 2)
            wavePath.LineTo(points[^1]);

        fillPath.AddPath(wavePath);
        fillPath.LineTo(info.Width, yBaseline);
        fillPath.LineTo(0, yBaseline);
        fillPath.Close();
    }

    private void RenderFillGradient(
        SKCanvas canvas,
        SKPaint basePaint,
        float maxMagnitude,
        float yBaseline,
        SKPath fillPath)
    {
        var fillPaint = CreateFillPaint(
            basePaint,
            maxMagnitude,
            yBaseline);

        canvas.DrawPath(fillPath, fillPaint);
        ReturnPaint(fillPaint);
    }

    private SKPaint CreateFillPaint(
        SKPaint basePaint,
        float maxMagnitude,
        float yBaseline)
    {
        var paint = GetPaint();
        paint.Style = SKPaintStyle.Fill;
        paint.IsAntialias = UseAntiAlias;

        SKColor[] colors =
        [
            basePaint.Color.WithAlpha((byte)(255 * FILL_OPACITY * maxMagnitude)),
            basePaint.Color.WithAlpha((byte)(255 * FILL_OPACITY * maxMagnitude * 0.5f)),
            SKColors.Transparent
        ];

        paint.Shader = SKShader.CreateLinearGradient(
            new SKPoint(0, EDGE_OFFSET),
            new SKPoint(0, yBaseline),
            colors,
            [0.0f, 0.7f, 1.0f],
            SKShaderTileMode.Clamp);

        return paint;
    }

    private void RenderGlowEffect(
        SKCanvas canvas,
        SKPaint basePaint,
        float maxMagnitude,
        int barCount,
        SKPath wavePath)
    {
        float blurRadius = MathF.Min(
            MAX_BLUR_RADIUS,
            10f / Sqrt((float)barCount));

        var glowPaint = CreatePaint(
            basePaint.Color.WithAlpha((byte)(255 * GLOW_INTENSITY * maxMagnitude)),
            SKPaintStyle.Stroke,
            DEFAULT_LINE_WIDTH * 2.0f,
            createBlur: true,
            blurRadius: blurRadius);

        canvas.DrawPath(wavePath, glowPaint);
        ReturnPaint(glowPaint);
    }

    private void RenderLineGradient(
        SKCanvas canvas,
        List<SKPoint> points,
        SKImageInfo info,
        SKPath wavePath)
    {
        var gradientPaint = CreateGradientPaint(points, info);
        canvas.DrawPath(wavePath, gradientPaint);
        ReturnPaint(gradientPaint);
    }

    private SKPaint CreateGradientPaint(
        List<SKPoint> points,
        SKImageInfo info)
    {
        var paint = GetPaint();
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = DEFAULT_LINE_WIDTH;
        paint.IsAntialias = UseAntiAlias;
        paint.StrokeCap = SKStrokeCap.Round;
        paint.StrokeJoin = SKStrokeJoin.Round;

        float saturation = IsOverlayActive
            ? OVERLAY_GRADIENT_SATURATION
            : LINE_GRADIENT_SATURATION;

        float lightness = IsOverlayActive
            ? OVERLAY_GRADIENT_LIGHTNESS
            : LINE_GRADIENT_LIGHTNESS;

        int colorSteps = Quality == RenderQuality.Low
            ? Max(2, points.Count / 4)
            : Min(points.Count, _currentSettings.PointCount);

        var (colors, positions) = GenerateGradientColors(
            points,
            info,
            saturation,
            lightness,
            colorSteps);

        paint.Shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(info.Width, 0),
            colors,
            positions,
            SKShaderTileMode.Clamp);

        return paint;
    }

    private static (SKColor[] colors, float[] positions) GenerateGradientColors(
        List<SKPoint> points,
        SKImageInfo info,
        float saturation,
        float lightness,
        int colorSteps)
    {
        var colors = new SKColor[colorSteps];
        var positions = new float[colorSteps];

        for (int i = 0; i < colorSteps; i++)
        {
            float normalizedValue = (float)i / (colorSteps - 1);
            int pointIndex = (int)(normalizedValue * (points.Count - 1));

            float segmentMagnitude = 1.0f - (points[pointIndex].Y - EDGE_OFFSET) /
                (info.Height - 2 * EDGE_OFFSET);

            colors[i] = SKColor.FromHsl(
                normalizedValue * 360,
                saturation,
                lightness,
                (byte)(255 * MathF.Min(0.6f + segmentMagnitude * 0.4f, 1.0f)));

            positions[i] = normalizedValue;
        }

        return (colors, positions);
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
            paint.MaskFilter = SKMaskFilter.CreateBlur(
                SKBlurStyle.Normal,
                blurRadius);

        return paint;
    }

    protected override void CleanupUnusedResources()
    {
        _cachedBackground?.Dispose();
        _cachedBackground = null;
        _cachedPoints = null;
    }

    protected override void OnDispose()
    {
        _cachedBackground?.Dispose();
        _cachedBackground = null;
        _renderSemaphore?.Dispose();
        _previousScaledSpectrum = null;
        _processedSpectrum = [];
        _cachedPoints = null;
        base.OnDispose();
        LogDebug("Disposed");
    }
}