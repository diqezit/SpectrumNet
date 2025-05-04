#nullable enable

using static SpectrumNet.Views.Renderers.GradientWaveRenderer.Constants;
using static System.MathF;

namespace SpectrumNet.Views.Renderers;

public sealed class GradientWaveRenderer : EffectSpectrumRenderer
{
    private static readonly Lazy<GradientWaveRenderer> _instance = new(() => new GradientWaveRenderer());

    private GradientWaveRenderer() { }

    public static GradientWaveRenderer GetInstance() => _instance.Value;

    public record Constants
    {
        public const string LOG_PREFIX = "GradientWaveRenderer";

        // Layout constants
        public const float
            EDGE_OFFSET = 10f,
            BASELINE_OFFSET = 2f;

        public const int 
            EXTRA_POINTS_COUNT = 4;

        // Wave smoothing factors
        public const float
            SMOOTHING_FACTOR_NORMAL = 0.3f,
            SMOOTHING_FACTOR_OVERLAY = 0.5f,
            MIN_MAGNITUDE_THRESHOLD = 0.01f,
            MAX_SPECTRUM_VALUE = 1.5f;

        // Rendering properties
        public const float
            DEFAULT_LINE_WIDTH = 3f,
            GLOW_INTENSITY = 0.3f,
            HIGH_MAGNITUDE_THRESHOLD = 0.7f,
            FILL_OPACITY = 0.2f;

        // Color properties
        public const float
            LINE_GRADIENT_SATURATION = 100f,
            LINE_GRADIENT_LIGHTNESS = 50f,
            OVERLAY_GRADIENT_SATURATION = 100f,
            OVERLAY_GRADIENT_LIGHTNESS = 55f,
            MAX_BLUR_RADIUS = 6f;

        // Quality settings (Low)
        public const int 
            POINT_COUNT_LOW = 20,
            SMOOTHING_PASSES_LOW = 1;

        // Quality settings (Medium)
        public const int 
            POINT_COUNT_MEDIUM = 40,
            SMOOTHING_PASSES_MEDIUM = 2;

        // Quality settings (High)
        public const int 
            POINT_COUNT_HIGH = 80,
            SMOOTHING_PASSES_HIGH = 3;

        // Performance optimization
        public const int 
            BATCH_SIZE = 128;
    }

    // Object pools for efficient resource management
    private readonly new ObjectPool<SKPath> _pathPool = new(() => new SKPath(), path => path.Reset(), 2);
    private readonly new ObjectPool<SKPaint> _paintPool = new(() => new SKPaint(), paint => paint.Reset(), 3);

    // Rendering state
    private List<SKPoint>? _cachedPoints;
    private readonly SKPath _wavePath = new();
    private readonly SKPath _fillPath = new();
    private SKPicture? _cachedBackground;

    // Quality-dependent settings
    private int _smoothingPasses = 2;
    private int _pointCount = POINT_COUNT_MEDIUM;

    // Synchronization and state
    private readonly SemaphoreSlim _renderSemaphore = new(1, 1);

    public override void Initialize()
    {
        Safe(
            () =>
            {
                base.Initialize();

                _smoothingFactor = SMOOTHING_FACTOR_NORMAL;
                ApplyQualitySettings();

                Log(LogLevel.Debug, LOG_PREFIX, "Initialized");
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.Initialize",
                ErrorMessage = "Failed to initialize renderer"
            }
        );
    }

    public override void Configure(
        bool isOverlayActive,
        RenderQuality quality = RenderQuality.Medium)
    {
        Safe(
            () =>
            {
                bool configChanged = _isOverlayActive != isOverlayActive || Quality != quality;

                base.Configure(isOverlayActive, quality);

                UpdateConfiguration(isOverlayActive, configChanged);
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.Configure",
                ErrorMessage = "Failed to configure renderer"
            }
        );
    }

    private void UpdateConfiguration(bool isOverlayActive, bool configChanged)
    {
        _isOverlayActive = isOverlayActive;
        _smoothingFactor = isOverlayActive ?
            SMOOTHING_FACTOR_OVERLAY :
            SMOOTHING_FACTOR_NORMAL;

        if (configChanged)
        {
            OnInvalidateCachedResources();
        }
    }

    protected override void ApplyQualitySettings()
    {
        Safe(
            () =>
            {
                base.ApplyQualitySettings();

                ApplyQualityBasedSettings();
                OnInvalidateCachedResources();
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.ApplyQualitySettings",
                ErrorMessage = "Failed to apply quality settings"
            }
        );
    }

    private void ApplyQualityBasedSettings()
    {
        switch (_quality)
        {
            case RenderQuality.Low:
                _useAntiAlias = false;
                _samplingOptions = new(SKFilterMode.Nearest, SKMipmapMode.None);
                _useAdvancedEffects = false;
                _smoothingPasses = SMOOTHING_PASSES_LOW;
                _pointCount = POINT_COUNT_LOW;
                break;

            case RenderQuality.Medium:
                _useAntiAlias = true;
                _samplingOptions = new(SKFilterMode.Linear, SKMipmapMode.Linear);
                _useAdvancedEffects = true;
                _smoothingPasses = SMOOTHING_PASSES_MEDIUM;
                _pointCount = POINT_COUNT_MEDIUM;
                break;

            case RenderQuality.High:
                _useAntiAlias = true;
                _samplingOptions = new(SKFilterMode.Linear, SKMipmapMode.Linear);
                _useAdvancedEffects = true;
                _smoothingPasses = SMOOTHING_PASSES_HIGH;
                _pointCount = POINT_COUNT_HIGH;
                break;
        }
    }

    protected override void OnInvalidateCachedResources()
    {
        Safe(
            () =>
            {
                _cachedBackground?.Dispose();
                _cachedBackground = null;
                _cachedPoints = null;
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.OnInvalidateCachedResources",
                ErrorMessage = "Failed to invalidate cached resources"
            }
        );
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
        if (!ValidateRenderParameters(canvas, spectrum, info, paint))
            return;

        if (canvas.QuickReject(new SKRect(0, 0, info.Width, info.Height)))
            return;

        Safe(
            () =>
            {
                PerformRendering(canvas, spectrum, info, barCount, paint);
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.RenderEffect",
                ErrorMessage = "Error during rendering"
            }
        );
    }

    private void PerformRendering(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        int barCount,
        SKPaint paint)
    {
        bool semaphoreAcquired = false;
        try
        {
            semaphoreAcquired = _renderSemaphore.Wait(0);

            float[] renderSpectrum;
            List<SKPoint> renderPoints;

            if (semaphoreAcquired)
            {
                ProcessSpectrumData(spectrum, barCount);
            }

            (renderSpectrum, renderPoints) = GetRenderingData(spectrum, info, barCount);

            RenderGradientWave(canvas, renderPoints, renderSpectrum, info, paint, barCount);
        }
        finally
        {
            if (semaphoreAcquired)
            {
                _renderSemaphore.Release();
            }
        }
    }

    private (float[] spectrum, List<SKPoint> points) GetRenderingData(
        float[] spectrum,
        SKImageInfo info,
        int barCount)
    {
        lock (_spectrumLock)
        {
            var renderSpectrum = _processedSpectrum ??
                                ProcessSpectrumSynchronously(spectrum, barCount);

            var renderPoints = _cachedPoints ??
                              GenerateOptimizedPoints(renderSpectrum, info);

            return (renderSpectrum, renderPoints);
        }
    }

    private static bool ValidateRenderParameters(
        SKCanvas? canvas,
        float[]? spectrum,
        SKImageInfo info,
        SKPaint? paint)
    {
        if (canvas == null || spectrum == null || paint == null)
        {
            Log(LogLevel.Error,
                LOG_PREFIX,
                "Invalid render parameters: null values");
            return false;
        }

        if (info.Width <= 0 || info.Height <= 0)
        {
            Log(LogLevel.Error,
                LOG_PREFIX,
                $"Invalid image dimensions: {info.Width}x{info.Height}");
            return false;
        }

        if (spectrum.Length == 0)
        {
            Log(LogLevel.Warning, LOG_PREFIX, "Empty spectrum data");
            return false;
        }

        return true;
    }

    private void ProcessSpectrumData(
        float[] spectrum,
        int barCount)
    {
        Safe(
            () =>
            {
                EnsureSpectrumBuffer(spectrum.Length);

                int spectrumLength = spectrum.Length;
                int actualBarCount = Min(spectrumLength, barCount);

                float[] scaledSpectrum = ScaleSpectrum(spectrum, actualBarCount, spectrumLength);
                _processedSpectrum = SmoothSpectrumData(scaledSpectrum, actualBarCount);

                lock (_spectrumLock)
                {
                    _cachedPoints = null;
                }
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.ProcessSpectrumData",
                ErrorMessage = "Error processing spectrum data"
            }
        );
    }

    private float[] ProcessSpectrumSynchronously(
        float[] spectrum,
        int barCount)
    {
        int spectrumLength = spectrum.Length;
        int actualBarCount = Min(spectrumLength, barCount);
        float[] scaledSpectrum = ScaleSpectrum(spectrum, actualBarCount, spectrumLength);
        return SmoothSpectrumData(scaledSpectrum, actualBarCount);
    }

    private void EnsureSpectrumBuffer(int length)
    {
        Safe(
            () =>
            {
                if (_previousSpectrum == null || _previousSpectrum.Length != length)
                {
                    _previousSpectrum = new float[length];
                }
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.EnsureSpectrumBuffer",
                ErrorMessage = "Error ensuring spectrum buffer"
            }
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private float[] SmoothSpectrumData(
        float[] spectrum,
        int targetCount)
    {
        float[] smoothedSpectrum = ApplyTemporalSmoothing(spectrum, targetCount);
        return ApplySpatialSmoothing(smoothedSpectrum, targetCount);
    }

    private float[] ApplyTemporalSmoothing(float[] spectrum, int targetCount)
    {
        float[] smoothedSpectrum = new float[targetCount];

        for (int i = 0; i < targetCount; i++)
        {
            if (_previousSpectrum == null) break;

            float currentValue = spectrum[i];
            float previousValue = _previousSpectrum[i];
            float smoothedValue = previousValue + (currentValue - previousValue) * _smoothingFactor;
            smoothedSpectrum[i] = Clamp(smoothedValue, MIN_MAGNITUDE_THRESHOLD, MAX_SPECTRUM_VALUE);
            _previousSpectrum[i] = smoothedSpectrum[i];
        }

        return smoothedSpectrum;
    }

    private static float[] ApplySpatialSmoothing(float[] spectrum, int targetCount)
    {
        float[] smoothedSpectrum = spectrum;

        for (int pass = 0; pass < targetCount; pass++)
        {
            var extraSmoothedSpectrum = new float[targetCount];

            for (int i = 0; i < targetCount; i++)
            {
                float sum = smoothedSpectrum[i];
                int count = 1;

                if (i > 0) { sum += smoothedSpectrum[i - 1]; count++; }
                if (i < targetCount - 1) { sum += smoothedSpectrum[i + 1]; count++; }

                extraSmoothedSpectrum[i] = sum / count;
            }

            smoothedSpectrum = extraSmoothedSpectrum;
        }

        return smoothedSpectrum;
    }

    private List<SKPoint> GenerateOptimizedPoints(
        float[] spectrum,
        SKImageInfo info)
    {
        float minY = EDGE_OFFSET;
        float maxY = info.Height - EDGE_OFFSET;
        int spectrumLength = spectrum.Length;

        if (spectrumLength < 1)
            return [];

        return BuildPointsList(spectrum, info, minY, maxY, spectrumLength);
    }

    private List<SKPoint> BuildPointsList(
        float[] spectrum,
        SKImageInfo info,
        float minY,
        float maxY,
        int spectrumLength)
    {
        int pointCount = Min(_pointCount, spectrumLength);
        var points = new List<SKPoint>(pointCount + EXTRA_POINTS_COUNT)
        {
            new(-EDGE_OFFSET, maxY),
            new(0, maxY)
        };

        AddInterpolatedPoints(points, spectrum, info, minY, maxY, spectrumLength, pointCount);

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
        int spectrumLength,
        int pointCount)
    {
        float step = (float)spectrumLength / pointCount;

        for (int i = 0; i < pointCount; i++)
        {
            float position = i * step;
            int index = Min((int)position, spectrumLength - 1);
            float remainder = position - index;

            float value = CalculateInterpolatedValue(spectrum, index, remainder, spectrumLength);

            float x = i / (float)(pointCount - 1) * info.Width;
            float y = maxY - value * (maxY - minY);
            points.Add(new SKPoint(x, y));
        }
    }

    private static float CalculateInterpolatedValue(
        float[] spectrum,
        int index,
        float remainder,
        int spectrumLength)
    {
        if (index < spectrumLength - 1 && remainder > 0)
        {
            return spectrum[index] * (1 - remainder) + spectrum[index + 1] * remainder;
        }

        return spectrum[index];
    }

    private void RenderGradientWave(
        SKCanvas canvas,
        List<SKPoint> points,
        float[] spectrum,
        SKImageInfo info,
        SKPaint basePaint,
        int barCount)
    {
        if (points.Count < 2) return;

        float maxMagnitude = CalculateMaxMagnitude(spectrum);
        float yBaseline = info.Height - EDGE_OFFSET + BASELINE_OFFSET;
        bool shouldRenderGlow = maxMagnitude > HIGH_MAGNITUDE_THRESHOLD && _useAdvancedEffects;

        using var wavePath = _pathPool.Get();
        using var fillPath = _pathPool.Get();

        CreateWavePaths(points, info, yBaseline, wavePath, fillPath);

        canvas.Save();

        try
        {
            RenderFillGradient(canvas, basePaint, maxMagnitude, info, yBaseline, fillPath);

            if (shouldRenderGlow)
            {
                RenderGlowEffect(canvas, basePaint, maxMagnitude, barCount, wavePath);
            }

            RenderLineGradient(canvas, points, basePaint, info, wavePath);
        }
        finally
        {
            canvas.Restore();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private float CalculateMaxMagnitude(float[] spectrum)
    {
        if (IsHardwareAccelerated && spectrum.Length >= Vector<float>.Count)
        {
            return CalculateMaxMagnitudeVectorized(spectrum);
        }

        return CalculateMaxMagnitudeStandard(spectrum);
    }

    private static float CalculateMaxMagnitudeVectorized(float[] spectrum)
    {
        float maxMagnitude = 0f;
        int i = 0;

        int vectorCount = spectrum.Length / Vector<float>.Count;
        Vector<float> maxVec = Vector<float>.Zero;

        for (; i < vectorCount * Vector<float>.Count; i += Vector<float>.Count)
        {
            Vector<float> vec = new(spectrum, i);
            maxVec = System.Numerics.Vector.Max(maxVec, vec);
        }

        for (int j = 0; j < Vector<float>.Count; j++)
        {
            maxMagnitude = MathF.Max(maxMagnitude, maxVec[j]);
        }

        for (; i < spectrum.Length; i++)
        {
            maxMagnitude = MathF.Max(maxMagnitude, spectrum[i]);
        }

        return maxMagnitude;
    }

    private static float CalculateMaxMagnitudeStandard(float[] spectrum)
    {
        float maxMagnitude = 0f;

        for (int i = 0; i < spectrum.Length; i++)
        {
            if (spectrum[i] > maxMagnitude)
                maxMagnitude = spectrum[i];
        }

        return maxMagnitude;
    }

    private static void CreateWavePaths(
        List<SKPoint> points,
        SKImageInfo info,
        float yBaseline,
        SKPath wavePath,
        SKPath fillPath)
    {
        wavePath.Reset();
        fillPath.Reset();

        BuildWavePath(points, wavePath);
        BuildFillPath(wavePath, fillPath, info, yBaseline);
    }

    private static void BuildWavePath(List<SKPoint> points, SKPath wavePath)
    {
        wavePath.MoveTo(points[0]);

        for (int i = 1; i < points.Count - 2; i += 1)
        {
            float x1 = points[i].X;
            float y1 = points[i].Y;
            float x2 = points[i + 1].X;
            float y2 = points[i + 1].Y;
            float xMid = (x1 + x2) / 2;
            float yMid = (y1 + y2) / 2;

            wavePath.QuadTo(x1, y1, xMid, yMid);
        }

        if (points.Count >= 2)
        {
            wavePath.LineTo(points[^1]);
        }
    }

    private static void BuildFillPath(
        SKPath wavePath,
        SKPath fillPath,
        SKImageInfo info,
        float yBaseline)
    {
        fillPath.AddPath(wavePath);
        fillPath.LineTo(info.Width, yBaseline);
        fillPath.LineTo(0, yBaseline);
        fillPath.Close();
    }

    private void RenderFillGradient(
        SKCanvas canvas,
        SKPaint basePaint,
        float maxMagnitude,
        SKImageInfo info,
        float yBaseline,
        SKPath fillPath)
    {
        using var fillPaint = _paintPool.Get();
        ConfigureFillPaint(fillPaint, basePaint, maxMagnitude, info, yBaseline);

        canvas.DrawPath(fillPath, fillPaint);
    }

    private void ConfigureFillPaint(
        SKPaint fillPaint,
        SKPaint basePaint,
        float maxMagnitude,
        SKImageInfo _, // info
        float yBaseline)
    {
        fillPaint.Style = SKPaintStyle.Fill;
        fillPaint.IsAntialias = _useAntiAlias;

        SetFillGradient(fillPaint, basePaint, maxMagnitude, yBaseline);
    }

    private static void SetFillGradient(
        SKPaint fillPaint,
        SKPaint basePaint,
        float maxMagnitude,
        float yBaseline)
    {
        SKColor[] colors =
        [
            basePaint.Color.WithAlpha((byte)(255 * FILL_OPACITY * maxMagnitude)),
            basePaint.Color.WithAlpha((byte)(255 * FILL_OPACITY * maxMagnitude * 0.5f)),
            SKColors.Transparent
        ];

        float[] colorPositions = [0.0f, 0.7f, 1.0f];

        fillPaint.Shader = SKShader.CreateLinearGradient(
            new SKPoint(0, EDGE_OFFSET),
            new SKPoint(0, yBaseline),
            colors,
            colorPositions,
            SKShaderTileMode.Clamp);
    }

    private void RenderGlowEffect(
        SKCanvas canvas,
        SKPaint basePaint,
        float maxMagnitude,
        int barCount,
        SKPath wavePath)
    {
        if (!_useAdvancedEffects) return;

        using var glowPaint = _paintPool.Get();
        ConfigureGlowPaint(glowPaint, basePaint, maxMagnitude, barCount);

        canvas.DrawPath(wavePath, glowPaint);
    }

    private void ConfigureGlowPaint(
        SKPaint glowPaint,
        SKPaint basePaint,
        float maxMagnitude,
        int barCount)
    {
        float blurRadius = MathF.Min(MAX_BLUR_RADIUS, 10f / Sqrt((float)barCount));

        glowPaint.Style = SKPaintStyle.Stroke;
        glowPaint.StrokeWidth = DEFAULT_LINE_WIDTH * 2.0f;
        glowPaint.IsAntialias = _useAntiAlias;
        glowPaint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, blurRadius);
        glowPaint.Color = basePaint.Color.WithAlpha((byte)(255 * GLOW_INTENSITY * maxMagnitude));
    }

    private void RenderLineGradient(
        SKCanvas canvas,
        List<SKPoint> points,
        SKPaint _,
        SKImageInfo info,
        SKPath wavePath)
    {
        using var gradientPaint = _paintPool.Get();
        ConfigureGradientPaint(gradientPaint, points, info);

        canvas.DrawPath(wavePath, gradientPaint);
    }

    private void ConfigureGradientPaint(
        SKPaint gradientPaint,
        List<SKPoint> points,
        SKImageInfo info)
    {
        gradientPaint.Style = SKPaintStyle.Stroke;
        gradientPaint.StrokeWidth = DEFAULT_LINE_WIDTH;
        gradientPaint.IsAntialias = _useAntiAlias;
        gradientPaint.StrokeCap = SKStrokeCap.Round;
        gradientPaint.StrokeJoin = SKStrokeJoin.Round;

        SetGradientShader(gradientPaint, points, info);
    }

    private void SetGradientShader(
        SKPaint gradientPaint,
        List<SKPoint> points,
        SKImageInfo info)
    {
        float saturation = _isOverlayActive ?
            OVERLAY_GRADIENT_SATURATION :
            LINE_GRADIENT_SATURATION;

        float lightness = _isOverlayActive ?
            OVERLAY_GRADIENT_LIGHTNESS :
            LINE_GRADIENT_LIGHTNESS;

        int colorSteps = _quality == RenderQuality.Low ?
            Max(2, points.Count / 4) :
            Min(points.Count, _pointCount);

        var (lineColors, positions) = GenerateGradientColors(
            points, info, saturation, lightness, colorSteps);

        gradientPaint.Shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(info.Width, 0),
            lineColors,
            positions,
            SKShaderTileMode.Clamp);
    }

    private static (SKColor[] colors, float[] positions) GenerateGradientColors(
        List<SKPoint> points,
        SKImageInfo info,
        float saturation,
        float lightness,
        int colorSteps)
    {
        var lineColors = new SKColor[colorSteps];
        var positions = new float[colorSteps];

        for (int i = 0; i < colorSteps; i++)
        {
            float normalizedValue = (float)i / (colorSteps - 1);
            int pointIndex = (int)(normalizedValue * (points.Count - 1));

            float segmentMagnitude = 1.0f - (points[pointIndex].Y - EDGE_OFFSET) /
                (info.Height - 2 * EDGE_OFFSET);

            lineColors[i] = SKColor.FromHsl(
                normalizedValue * 360,
                saturation,
                lightness,
                (byte)(255 * MathF.Min(0.6f + segmentMagnitude * 0.4f, 1.0f)));

            positions[i] = normalizedValue;
        }

        return (lineColors, positions);
    }

    protected override void OnDispose()
    {
        Safe(
            () =>
            {
                DisposeManagedResources();
                base.OnDispose();

                Log(LogLevel.Debug, LOG_PREFIX, "Disposed");
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.OnDispose",
                ErrorMessage = "Error during OnDispose"
            }
        );
    }

    private void DisposeManagedResources()
    {
        _cachedBackground?.Dispose();
        _cachedBackground = null;

        _wavePath?.Dispose();
        _fillPath?.Dispose();

        _pathPool?.Dispose();
        _paintPool?.Dispose();

        _renderSemaphore?.Dispose();

        _previousSpectrum = null;
        _processedSpectrum = null;
        _cachedPoints = null;
    }
}