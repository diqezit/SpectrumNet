#nullable enable

using static SpectrumNet.Views.Renderers.SphereRenderer.Constants;
using static SpectrumNet.Views.Renderers.SphereRenderer.Constants.Quality;

namespace SpectrumNet.Views.Renderers;

public sealed class SphereRenderer : EffectSpectrumRenderer
{
    private static readonly Lazy<SphereRenderer> _instance = new(() => new SphereRenderer());
    private const string LOG_PREFIX = nameof(SphereRenderer);

    private SphereRenderer() { }

    public static SphereRenderer GetInstance() => _instance.Value;

    public record Constants
    {
        public const float
            MIN_MAGNITUDE = 0.01f,
            MAX_INTENSITY_MULTIPLIER = 3f,
            MIN_ALPHA = 0.1f,
            PI_OVER_180 = (MathF.PI / 180);

        public const float
            DEFAULT_RADIUS = 40f,
            MIN_RADIUS = 1.0f,
            DEFAULT_SPACING = 10f;

        public const int
            DEFAULT_COUNT = 8,
            BATCH_SIZE = 128;

        public static readonly (float Radius, float Spacing, int Count)
            DEFAULT_CONFIG = (DEFAULT_RADIUS, DEFAULT_SPACING, DEFAULT_COUNT),
            OVERLAY_CONFIG = (20f, 5f, 16);

        public static class Quality
        {
            public const float
                LOW_SMOOTHING_FACTOR = 0.1f,
                MEDIUM_SMOOTHING_FACTOR = 0.2f,
                HIGH_SMOOTHING_FACTOR = 0.3f;

            public const bool
                LOW_USE_ANTIALIASING = false,
                MEDIUM_USE_ANTIALIASING = true,
                HIGH_USE_ANTIALIASING = true;

            public const bool
                LOW_USE_ADVANCED_EFFECTS = false,
                MEDIUM_USE_ADVANCED_EFFECTS = true,
                HIGH_USE_ADVANCED_EFFECTS = true;

            public const int
                LOW_SPHERE_SEGMENTS = 0,
                MEDIUM_SPHERE_SEGMENTS = 0,
                HIGH_SPHERE_SEGMENTS = 8;
        }
    }

    // Rendering settings
    private float _sphereRadius = DEFAULT_RADIUS;
    private float _sphereSpacing = DEFAULT_SPACING;
    private int _sphereCount = DEFAULT_COUNT;

    // Quality settings
    private float _alphaSmoothingFactor;
    private int _sphereSegments;

    // Buffers and cached data
    private float[]? _cosValues;
    private float[]? _sinValues;
    private float[]? _currentAlphas;

    protected override void OnInitialize() =>
        _logger.Safe(
            () =>
            {
                base.OnInitialize();
                UpdateConfiguration(DEFAULT_CONFIG);
                _logger.Debug(LOG_PREFIX, "Initialized");
            },
            LOG_PREFIX,
            "Failed to initialize renderer"
        );

    protected override void OnConfigurationChanged() =>
        _logger.Safe(
            () =>
            {
                UpdateConfiguration(_isOverlayActive ? OVERLAY_CONFIG : DEFAULT_CONFIG);
                _logger.Info(
                    LOG_PREFIX,
                    $"Configuration changed. New Quality: {Quality}, Overlay: {_isOverlayActive}");
            },
            LOG_PREFIX,
            "Failed to handle configuration change"
        );

    protected override void OnQualitySettingsApplied() =>
        _logger.Safe(
            () =>
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

                _logger.Debug(LOG_PREFIX,
                    $"Quality settings applied. Quality: {Quality}, " +
                    $"AntiAlias: {UseAntiAlias}, AdvancedEffects: {UseAdvancedEffects}, " +
                    $"SphereSegments: {_sphereSegments}, AlphaSmoothingFactor: {_alphaSmoothingFactor}");
            },
            LOG_PREFIX,
            "Failed to apply quality settings"
        );

    private void LowQualitySettings()
    {
        _alphaSmoothingFactor = LOW_SMOOTHING_FACTOR;
        _useAntiAlias = LOW_USE_ANTIALIASING;
        _useAdvancedEffects = LOW_USE_ADVANCED_EFFECTS;
        _sphereSegments = LOW_SPHERE_SEGMENTS;
    }

    private void MediumQualitySettings()
    {
        _alphaSmoothingFactor = MEDIUM_SMOOTHING_FACTOR;
        _useAntiAlias = MEDIUM_USE_ANTIALIASING;
        _useAdvancedEffects = MEDIUM_USE_ADVANCED_EFFECTS;
        _sphereSegments = MEDIUM_SPHERE_SEGMENTS;
    }

    private void HighQualitySettings()
    {
        _alphaSmoothingFactor = HIGH_SMOOTHING_FACTOR;
        _useAntiAlias = HIGH_USE_ANTIALIASING;
        _useAdvancedEffects = HIGH_USE_ADVANCED_EFFECTS;
        _sphereSegments = HIGH_SPHERE_SEGMENTS;
    }

    private void UpdateConfiguration((float Radius, float Spacing, int Count) config) =>
        _logger.Safe(
            () => HandleUpdateConfiguration(config),
            LOG_PREFIX,
            "Failed to update configuration"
        );

    private void HandleUpdateConfiguration((float Radius, float Spacing, int Count) config)
    {
        (_sphereRadius, _sphereSpacing, _sphereCount) = config;
        _sphereRadius = Max(MIN_RADIUS, _sphereRadius);

        EnsureArrayCapacity(ref _currentAlphas, _sphereCount);
        PrecomputeTrigValues();
    }

    private void AdjustConfiguration(
        int barCount,
        float barSpacing,
        int canvasWidth,
        int canvasHeight) =>
        _logger.Safe(
            () => HandleAdjustConfiguration(barCount, barSpacing, canvasWidth, canvasHeight),
            LOG_PREFIX,
            "Failed to adjust configuration"
        );

    private void HandleAdjustConfiguration(
        int barCount,
        float barSpacing,
        int canvasWidth,
        int canvasHeight)
    {
        _sphereRadius = Max(5f, DEFAULT_RADIUS - barCount * 0.2f + barSpacing * 0.5f);
        _sphereSpacing = Max(2f, DEFAULT_SPACING - barCount * 0.1f + barSpacing * 0.3f);
        _sphereCount = Clamp(barCount / 2, 4, 64);

        float maxRadius = Min(canvasWidth,
                              canvasHeight) / 2f - (_sphereRadius + _sphereSpacing);
        if (_sphereRadius > maxRadius)
            _sphereRadius = maxRadius;

        _sphereRadius = Max(MIN_RADIUS, _sphereRadius);

        EnsureArrayCapacity(ref _currentAlphas, _sphereCount);
        PrecomputeTrigValues();
    }

    protected override void RenderEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint paint) =>
        _logger.Safe(
            () =>
            {
                UpdateState(spectrum, info, barWidth, barSpacing, barCount);
                RenderFrame(canvas, spectrum, info, paint);
            },
            LOG_PREFIX,
            "Error during rendering"
        );

    private void UpdateState(
        float[] spectrum,
        SKImageInfo info,
        float _,
        float barSpacing,
        int barCount)
    {
        bool semaphoreAcquired = false;
        var spectrumSemaphore = _spectrumSemaphore;
        if (spectrumSemaphore == null) return;

        try
        {
            semaphoreAcquired = spectrumSemaphore.Wait(0);
            if (semaphoreAcquired)
            {
                AdjustConfiguration(barCount, barSpacing, info.Width, info.Height);
                ProcessSpectrum(spectrum);
            }
        }
        finally
        {
            if (semaphoreAcquired)
            {
                spectrumSemaphore.Release();
            }
        }
    }

    private void RenderFrame(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        SKPaint paint)
    {
        if (_processedSpectrum != null)
        {
            int sphereCount = Min(spectrum.Length, _sphereCount);
            float centerRadius = info.Height / 2f - (_sphereRadius + _sphereSpacing);

            RenderSpheres(
                canvas,
                _processedSpectrum,
                sphereCount,
                info.Width / 2f,
                info.Height / 2f,
                centerRadius,
                paint);
        }
    }

    private void RenderSpheres(
        SKCanvas canvas,
        float[] spectrum,
        int sphereCount,
        float centerX,
        float centerY,
        float maxRadius,
        SKPaint paint) =>
        _logger.Safe(
            () => HandleRenderSpheres(canvas, spectrum, sphereCount, centerX, centerY, maxRadius, paint),
            LOG_PREFIX,
            "Error during sphere rendering"
        );

    private void HandleRenderSpheres(
        SKCanvas canvas,
        float[] spectrum,
        int sphereCount,
        float centerX,
        float centerY,
        float maxRadius,
        SKPaint paint)
    {
        if (!AreArraysValid(sphereCount))
            return;

        var alphaGroups = GetAlphaGroups(sphereCount, 5);

        if (_sphereSegments > 0)
        {
            RenderHighQualitySpheres(
                canvas,
                spectrum,
                alphaGroups,
                paint,
                centerX,
                centerY,
                maxRadius);
        }
        else
        {
            RenderSimpleSpheres(
                canvas,
                spectrum,
                alphaGroups,
                paint,
                centerX,
                centerY,
                maxRadius);
        }
    }

    private void RenderHighQualitySpheres(
        SKCanvas canvas,
        float[] spectrum,
        (int start, int end, float alpha)[] alphaGroups,
        SKPaint paint,
        float centerX,
        float centerY,
        float maxRadius)
    {
        var paintPool = _paintPool;
        if (paintPool == null) return;

        foreach (var group in alphaGroups)
        {
            if (group.end <= group.start)
                continue;

            using var groupPaint = PrepareHighQualityPaint(paint, group.alpha);
            if (groupPaint == null) continue;

            DrawHighQualitySpheres(
                canvas,
                spectrum,
                group,
                groupPaint,
                centerX,
                centerY,
                maxRadius);
        }
    }

    private SKPaint? PrepareHighQualityPaint(SKPaint paint, float alpha)
    {
        var paintPool = _paintPool;
        if (paintPool == null) return null;

        var centerColor = paint.Color.WithAlpha((byte)(255 * alpha));
        var edgeColor = paint.Color.WithAlpha(0);

        using var shader = SKShader.CreateRadialGradient(
            new SKPoint(0, 0),
            1.0f,
            [centerColor, edgeColor],
            [0.0f, 1.0f],
            SKShaderTileMode.Clamp);

        var groupPaint = paintPool.Get();
        if (groupPaint == null) return null;

        groupPaint.Reset();
        groupPaint.Shader = shader;
        groupPaint.IsAntialias = UseAntiAlias;

        return groupPaint;
    }

    private void DrawHighQualitySpheres(
        SKCanvas canvas,
        float[] spectrum,
        (int start, int end, float _) group,
        SKPaint groupPaint,
        float centerX,
        float centerY,
        float maxRadius)
    {
        if (_cosValues == null || _sinValues == null) return;

        for (int i = group.start; i < group.end; i++)
        {
            float magnitude = spectrum[i];

            if (magnitude < MIN_MAGNITUDE)
                continue;

            float x = centerX + _cosValues[i] * maxRadius;
            float y = centerY + _sinValues[i] * maxRadius;
            float circleSize = Max(magnitude * _sphereRadius, 2f) + _sphereSpacing * 0.2f;

            SKRect bounds = new(x - circleSize, y - circleSize, x + circleSize, y + circleSize);
            if (canvas.QuickReject(bounds))
                continue;

            DrawSingleHighQualitySphere(canvas, x, y, circleSize, groupPaint);
        }
    }

    private static void DrawSingleHighQualitySphere(
        SKCanvas canvas,
        float x,
        float y,
        float circleSize,
        SKPaint paint)
    {
        canvas.Save();
        canvas.Translate(x, y);
        canvas.Scale(circleSize);
        canvas.DrawCircle(0, 0, 1.0f, paint);
        canvas.Restore();
    }

    private void RenderSimpleSpheres(
        SKCanvas canvas,
        float[] spectrum,
        (int start, int end, float alpha)[] alphaGroups,
        SKPaint paint,
        float centerX,
        float centerY,
        float maxRadius)
    {
        var spherePaint = PrepareSimplePaint();
        if (spherePaint == null) return;

        using (spherePaint)
        {
            foreach (var group in alphaGroups)
            {
                if (group.end <= group.start)
                    continue;

                spherePaint.Color = paint.Color.WithAlpha((byte)(255 * group.alpha));

                DrawSimpleSpheres(
                    canvas,
                    spectrum,
                    group,
                    spherePaint,
                    centerX,
                    centerY,
                    maxRadius);
            }
        }
    }

    private SKPaint? PrepareSimplePaint()
    {
        var paintPool = _paintPool;
        if (paintPool == null) return null;

        var spherePaint = paintPool.Get();
        if (spherePaint == null) return null;

        spherePaint.Reset();
        spherePaint.IsAntialias = UseAntiAlias;
        return spherePaint;
    }

    private void DrawSimpleSpheres(
        SKCanvas canvas,
        float[] spectrum,
        (int start, int end, float _) group,
        SKPaint spherePaint,
        float centerX,
        float centerY,
        float maxRadius)
    {
        if (_cosValues == null || _sinValues == null) return;

        for (int i = group.start; i < group.end; i++)
        {
            float magnitude = spectrum[i];

            if (magnitude < MIN_MAGNITUDE)
                continue;

            float x = centerX + _cosValues[i] * maxRadius;
            float y = centerY + _sinValues[i] * maxRadius;
            float circleSize = Max(magnitude * _sphereRadius, 2f) + _sphereSpacing * 0.2f;

            SKRect bounds = new(x - circleSize, y - circleSize, x + circleSize, y + circleSize);
            if (canvas.QuickReject(bounds))
                continue;

            canvas.DrawCircle(x, y, circleSize, spherePaint);
        }
    }

    private (int start, int end, float alpha)[] GetAlphaGroups(int length, int maxGroups)
    {
        if (_currentAlphas == null || length == 0)
            return [];

        var groups = new List<(int start, int end, float alpha)>(maxGroups);

        CollectAlphaGroups(groups, length, maxGroups);

        return [.. groups];
    }

    private void CollectAlphaGroups(
        List<(int start, int end, float alpha)> groups,
        int length,
        int maxGroups)
    {
        if (_currentAlphas == null || length == 0)
            return;

        int currentStart = 0;
        float currentAlpha = _currentAlphas[0];

        for (int i = 1; i < length; i++)
        {
            if (Abs(_currentAlphas[i] - currentAlpha) > 0.1f ||
                groups.Count >= maxGroups - 1)
            {
                groups.Add((currentStart, i, currentAlpha));
                currentStart = i;
                currentAlpha = _currentAlphas[i];
            }
        }

        groups.Add((currentStart, length, currentAlpha));
    }

    private void ProcessSpectrum(float[] spectrum) =>
        _logger.Safe(
            () => HandleProcessSpectrum(spectrum),
            LOG_PREFIX,
            "Error processing spectrum data"
        );

    private void HandleProcessSpectrum(float[] spectrum)
    {
        int sphereCount = Min(spectrum.Length, _sphereCount);
        EnsureProcessedSpectrumCapacity(sphereCount);

        ScaleSpectrumData(spectrum, sphereCount);
        UpdateAlphas(sphereCount);
    }

    private void ScaleSpectrumData(float[] spectrum, int sphereCount)
    {
        if (_processedSpectrum == null) return;

        if (IsHardwareAccelerated && spectrum.Length >= Vector<float>.Count)
        {
            ProcessSpectrumSIMD(spectrum, _processedSpectrum, sphereCount);
        }
        else
        {
            ScaleSpectrum(spectrum, _processedSpectrum, sphereCount);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void ProcessSpectrumSIMD(
        float[] source,
        float[] target,
        int targetCount)
    {
        float blockSize = source.Length / (float)targetCount;

        Parallel.For(0, targetCount, i =>
        {
            ProcessSpectrumBlock(source, target, i, blockSize);
        });
    }

    private static void ProcessSpectrumBlock(
        float[] source,
        float[] target,
        int blockIndex,
        float blockSize)
    {
        int start = (int)(blockIndex * blockSize);
        int end = (int)((blockIndex + 1) * blockSize);
        end = Min(end, source.Length);

        if (start >= end)
        {
            target[blockIndex] = 0;
            return;
        }

        target[blockIndex] = CalculateVectorizedAverage(source, start, end);
    }

    private static float CalculateVectorizedAverage(
        float[] source,
        int start,
        int end)
    {
        float sum = 0;
        int count = end - start;

        int vectorSize = Vector<float>.Count;
        int vectorizableLength = (end - start) / vectorSize * vectorSize;

        CalculateVectorizedSum(source, start, vectorizableLength, ref sum);
        CalculateRemainingSum(source, start + vectorizableLength, end, ref sum);

        return sum / count;
    }

    private static void CalculateVectorizedSum(
        float[] source,
        int start,
        int vectorizableLength,
        ref float sum)
    {
        int vectorSize = Vector<float>.Count;

        for (int j = 0; j < vectorizableLength; j += vectorSize)
        {
            var vec = new Vector<float>(source, start + j);
            sum += System.Numerics.Vector.Sum(vec);
        }
    }

    private static void CalculateRemainingSum(
        float[] source,
        int start,
        int end,
        ref float sum)
    {
        for (int j = start; j < end; j++)
        {
            sum += source[j];
        }
    }

    private void EnsureProcessedSpectrumCapacity(int requiredSize) =>
        _logger.Safe(
            () => HandleEnsureProcessedSpectrumCapacity(requiredSize),
            LOG_PREFIX,
            "Failed to ensure spectrum capacity"
        );

    private void HandleEnsureProcessedSpectrumCapacity(int requiredSize)
    {
        if (_processedSpectrum != null && _processedSpectrum.Length >= requiredSize)
            return;

        if (_processedSpectrum != null)
            ArrayPool<float>.Shared.Return(_processedSpectrum);

        _processedSpectrum = ArrayPool<float>.Shared.Rent(requiredSize);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ScaleSpectrum(float[] source, float[] target, int targetCount)
    {
        float blockSize = source.Length / (float)targetCount;

        for (int i = 0; i < targetCount; i++)
        {
            int start = (int)(blockSize * i);
            int end = (int)(blockSize * (i + 1));
            end = Min(end, source.Length);

            if (start >= end)
            {
                target[i] = 0;
                continue;
            }

            target[i] = CalculateBlockAverage(source, start, end);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float CalculateBlockAverage(float[] source, int start, int end)
    {
        float sum = 0;
        for (int j = start; j < end; j++)
            sum += source[j];

        return sum / (end - start);
    }

    private void UpdateAlphas(int length) =>
        _logger.Safe(
            () => HandleUpdateAlphas(length),
            LOG_PREFIX,
            "Failed to update alpha values"
        );

    private void HandleUpdateAlphas(int length)
    {
        if (_processedSpectrum == null
            || _currentAlphas == null
            || _currentAlphas.Length < length)
            return;

        for (int i = 0; i < length; i++)
        {
            UpdateSingleAlpha(i);
        }
    }

    private void UpdateSingleAlpha(int index)
    {
        if (_processedSpectrum == null || _currentAlphas == null)
            return;

        float targetAlpha = Max(MIN_ALPHA, _processedSpectrum[index] * MAX_INTENSITY_MULTIPLIER);
        _currentAlphas[index] = _currentAlphas[index] +
                            (targetAlpha - _currentAlphas[index]) * _alphaSmoothingFactor;
    }

    private void PrecomputeTrigValues() =>
        _logger.Safe(
            () => HandlePrecomputeTrigValues(),
            LOG_PREFIX,
            "Failed to precompute trigonometric values"
        );

    private void HandlePrecomputeTrigValues()
    {
        EnsureArrayCapacity(ref _cosValues, _sphereCount);
        EnsureArrayCapacity(ref _sinValues, _sphereCount);

        float angleStepRad = 360f / _sphereCount * PI_OVER_180;

        for (int i = 0; i < _sphereCount; i++)
        {
            float angle = i * angleStepRad;
            _cosValues![i] = (float)Cos(angle);
            _sinValues![i] = (float)Sin(angle);
        }
    }

    private bool AreArraysValid(int requiredLength) =>
        _cosValues != null && _sinValues != null && _currentAlphas != null &&
        _cosValues.Length >= requiredLength &&
        _sinValues.Length >= requiredLength &&
        _currentAlphas.Length >= requiredLength;

    private static void EnsureArrayCapacity<T>(ref T[]? array, int requiredSize) where T : struct
    {
        if (array == null || array.Length < requiredSize)
            array = new T[requiredSize];
    }

    protected override void OnInvalidateCachedResources() =>
        _logger.Safe(
            () =>
            {
                base.OnInvalidateCachedResources();
                _logger.Debug(LOG_PREFIX, "Cached resources invalidated");
            },
            LOG_PREFIX,
            "Error invalidating cached resources"
        );

    protected override void OnDispose() =>
        _logger.Safe(
            () =>
            {
                if (_processedSpectrum != null)
                    ArrayPool<float>.Shared.Return(_processedSpectrum);

                _cosValues = _sinValues = _currentAlphas = null;
                _processedSpectrum = null;

                base.OnDispose();
                _logger.Debug(LOG_PREFIX, "Disposed");
            },
            LOG_PREFIX,
            "Error during specific disposal"
        );
}