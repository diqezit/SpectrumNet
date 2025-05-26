#nullable enable

using static System.MathF;
using static SpectrumNet.SN.Visualization.Renderers.SphereRenderer.Constants;

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class SphereRenderer : EffectSpectrumRenderer
{
    private const string LogPrefix = nameof(SphereRenderer);

    private static readonly Lazy<SphereRenderer> _instance =
        new(() => new SphereRenderer());

    private SphereRenderer() { }

    public static SphereRenderer GetInstance() => _instance.Value;

    public static class Constants
    {
        public const float
            MIN_MAGNITUDE = 0.01f,
            MAX_INTENSITY_MULTIPLIER = 3f,
            MIN_ALPHA = 0.1f,
            PI_OVER_180 = MathF.PI / 180f,
            DEFAULT_RADIUS = 40f,
            MIN_RADIUS = 1.0f,
            DEFAULT_SPACING = 10f,
            ALPHA_THRESHOLD = 0.1f,
            MIN_CIRCLE_SIZE = 2f,
            SPACING_FACTOR = 0.2f;

        public const int
            DEFAULT_COUNT = 8,
            BATCH_SIZE = 128,
            MAX_ALPHA_GROUPS = 5,
            MIN_SPHERE_COUNT = 4,
            MAX_SPHERE_COUNT = 64;

        public static readonly Dictionary<RenderQuality, QualitySettings> QualityPresets = new()
        {
            [RenderQuality.Low] = new(
                SmoothingFactor: 0.1f,
                UseAntialiasing: false,
                UseAdvancedEffects: false,
                SphereSegments: 0
            ),
            [RenderQuality.Medium] = new(
                SmoothingFactor: 0.2f,
                UseAntialiasing: true,
                UseAdvancedEffects: true,
                SphereSegments: 0
            ),
            [RenderQuality.High] = new(
                SmoothingFactor: 0.3f,
                UseAntialiasing: true,
                UseAdvancedEffects: true,
                SphereSegments: 8
            )
        };

        public static readonly Dictionary<bool, SphereConfig> ConfigPresets = new()
        {
            [false] = new(DEFAULT_RADIUS, DEFAULT_SPACING, DEFAULT_COUNT),
            [true] = new(20f, 5f, 16)
        };

        public record QualitySettings(
            float SmoothingFactor,
            bool UseAntialiasing,
            bool UseAdvancedEffects,
            int SphereSegments
        );

        public record SphereConfig(
            float Radius,
            float Spacing,
            int Count
        );
    }

    private static readonly SKColor[] GradientColors = [SKColors.White, SKColors.Transparent];
    private static readonly float[] GradientPositions = [0.0f, 1.0f];

    private QualitySettings _currentSettings = QualityPresets[RenderQuality.Medium];
    private SphereConfig _currentConfig = ConfigPresets[false];

    private float[]? _cosValues;
    private float[]? _sinValues;
    private float[]? _currentAlphas;
    private float[]? _processedSpectrum;

    protected override void OnInitialize()
    {
        UpdateConfiguration(ConfigPresets[false]);
        LogDebug("Initialized");
    }

    protected override void OnConfigurationChanged()
    {
        UpdateConfiguration(ConfigPresets[IsOverlayActive]);
        RequestRedraw();
    }

    protected override void OnQualitySettingsApplied()
    {
        _currentSettings = QualityPresets[Quality];
        SetProcessingSmoothingFactor(_currentSettings.SmoothingFactor);
        LogDebug($"Quality changed to {Quality}");
    }

    private void UpdateConfiguration(SphereConfig config)
    {
        _currentConfig = config with { Radius = MathF.Max(MIN_RADIUS, config.Radius) };
        EnsureArrayCapacity(ref _currentAlphas, _currentConfig.Count);
        PrecomputeTrigValues();
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
        RenderSphereEffect(canvas, spectrum, info, barSpacing, barCount, paint);
    }

    private void RenderSphereEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barSpacing,
        int barCount,
        SKPaint paint)
    {
        AdjustConfigurationForCanvas(barCount, barSpacing, info.Width, info.Height);
        ProcessSpectrumData(spectrum);
        RenderSpheres(canvas, spectrum, info, paint);
    }

    private void AdjustConfigurationForCanvas(
        int barCount,
        float barSpacing,
        int canvasWidth,
        int canvasHeight)
    {
        float radius = MathF.Max(5f, DEFAULT_RADIUS - barCount * 0.2f + barSpacing * 0.5f);
        float spacing = MathF.Max(2f, DEFAULT_SPACING - barCount * 0.1f + barSpacing * 0.3f);
        int count = Clamp(barCount / 2, MIN_SPHERE_COUNT, MAX_SPHERE_COUNT);

        float maxRadius = MathF.Min(canvasWidth, canvasHeight) / 2f - (radius + spacing);
        radius = MathF.Max(MIN_RADIUS, MathF.Min(radius, maxRadius));

        _currentConfig = new SphereConfig(radius, spacing, count);
        EnsureArrayCapacity(ref _currentAlphas, count);
        PrecomputeTrigValues();
    }

    private void ProcessSpectrumData(float[] spectrum)
    {
        int sphereCount = (int)MathF.Min(spectrum.Length, _currentConfig.Count);
        EnsureProcessedSpectrumCapacity(sphereCount);

        if (_processedSpectrum == null) return;

        ScaleSpectrum(spectrum, _processedSpectrum, sphereCount);
        UpdateAlphas(sphereCount);
    }

    private void RenderSpheres(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        SKPaint paint)
    {
        if (_processedSpectrum == null || !AreArraysValid()) return;

        int sphereCount = (int)MathF.Min(spectrum.Length, _currentConfig.Count);
        float centerX = info.Width / 2f;
        float centerY = info.Height / 2f;
        float maxRadius = info.Height / 2f - (_currentConfig.Radius + _currentConfig.Spacing);

        var alphaGroups = GetAlphaGroups(sphereCount);

        if (_currentSettings.SphereSegments > 0 && UseAdvancedEffects)
            RenderHighQualitySpheres(canvas, _processedSpectrum, alphaGroups, paint, centerX, centerY, maxRadius);
        else
            RenderSimpleSpheres(canvas, _processedSpectrum, alphaGroups, paint, centerX, centerY, maxRadius);
    }

    private void RenderHighQualitySpheres(
        SKCanvas canvas,
        float[] spectrum,
        AlphaGroup[] alphaGroups,
        SKPaint paint,
        float centerX,
        float centerY,
        float maxRadius)
    {
        foreach (var group in alphaGroups)
        {
            if (group.End <= group.Start) continue;

            using var groupPaint = CreateHighQualityPaint(paint, group.Alpha);
            RenderSphereGroup(canvas, spectrum, group, groupPaint, centerX, centerY, maxRadius);
        }
    }

    private void RenderSimpleSpheres(
        SKCanvas canvas,
        float[] spectrum,
        AlphaGroup[] alphaGroups,
        SKPaint paint,
        float centerX,
        float centerY,
        float maxRadius)
    {
        using var spherePaint = CreateStandardPaint(paint.Color);

        foreach (var group in alphaGroups)
        {
            if (group.End <= group.Start) continue;

            spherePaint.Color = paint.Color.WithAlpha((byte)(255 * group.Alpha));
            RenderSphereGroup(canvas, spectrum, group, spherePaint, centerX, centerY, maxRadius);
        }
    }

    private void RenderSphereGroup(
        SKCanvas canvas,
        float[] spectrum,
        AlphaGroup group,
        SKPaint paint,
        float centerX,
        float centerY,
        float maxRadius)
    {
        for (int i = group.Start; i < group.End; i++)
        {
            if (spectrum[i] < MIN_MAGNITUDE) continue;

            var position = CalculateSpherePosition(i, centerX, centerY, maxRadius);
            float size = CalculateSphereSize(spectrum[i]);

            if (IsRenderAreaVisible(canvas, position.x, position.y, size * 2, size * 2))
                DrawSphere(canvas, position, size, paint);
        }
    }

    private (float x, float y) CalculateSpherePosition(
        int index,
        float centerX,
        float centerY,
        float radius)
    {
        if (_cosValues == null || _sinValues == null) return (centerX, centerY);
        return (centerX + _cosValues[index] * radius, centerY + _sinValues[index] * radius);
    }

    private float CalculateSphereSize(float magnitude) =>
        MathF.Max(magnitude * _currentConfig.Radius, MIN_CIRCLE_SIZE) + _currentConfig.Spacing * SPACING_FACTOR;

    private static void DrawSphere(
        SKCanvas canvas,
        (float x, float y) position,
        float size,
        SKPaint paint)
    {
        if (paint.Shader != null)
        {
            canvas.Save();
            canvas.Translate(position.x, position.y);
            canvas.Scale(size);
            canvas.DrawCircle(0, 0, 1.0f, paint);
            canvas.Restore();
        }
        else
        {
            canvas.DrawCircle(position.x, position.y, size, paint);
        }
    }

    private SKPaint CreateHighQualityPaint(SKPaint basePaint, float alpha)
    {
        var centerColor = basePaint.Color.WithAlpha((byte)(255 * alpha));
        var edgeColor = basePaint.Color.WithAlpha(0);

        var colors = new[] { centerColor, edgeColor };

        var shader = SKShader.CreateRadialGradient(
            new SKPoint(0, 0),
            1.0f,
            colors,
            GradientPositions,
            SKShaderTileMode.Clamp
        );

        var paint = GetPaint();
        paint.Shader = shader;
        paint.IsAntialias = UseAntiAlias;
        return paint;
    }

    private AlphaGroup[] GetAlphaGroups(int length)
    {
        if (_currentAlphas == null || length == 0) return [];

        var groups = new List<AlphaGroup>(MAX_ALPHA_GROUPS);
        int currentStart = 0;
        float currentAlpha = _currentAlphas[0];

        for (int i = 1; i < length; i++)
        {
            if (MathF.Abs(_currentAlphas[i] - currentAlpha) > ALPHA_THRESHOLD ||
                groups.Count >= MAX_ALPHA_GROUPS - 1)
            {
                groups.Add(new AlphaGroup(currentStart, i, currentAlpha));
                currentStart = i;
                currentAlpha = _currentAlphas[i];
            }
        }

        groups.Add(new AlphaGroup(currentStart, length, currentAlpha));
        return [.. groups];
    }

    private void UpdateAlphas(int length)
    {
        if (_processedSpectrum == null || _currentAlphas == null) return;

        for (int i = 0; i < length; i++)
        {
            float targetAlpha = MathF.Max(MIN_ALPHA, _processedSpectrum[i] * MAX_INTENSITY_MULTIPLIER);
            _currentAlphas[i] += (targetAlpha - _currentAlphas[i]) * _currentSettings.SmoothingFactor;
        }
    }

    private void PrecomputeTrigValues()
    {
        int count = _currentConfig.Count;
        EnsureArrayCapacity(ref _cosValues, count);
        EnsureArrayCapacity(ref _sinValues, count);

        float angleStep = 360f / count * PI_OVER_180;

        for (int i = 0; i < count; i++)
        {
            float angle = i * angleStep;
            _cosValues![i] = Cos(angle);
            _sinValues![i] = Sin(angle);
        }
    }

    private bool AreArraysValid()
    {
        int required = _currentConfig.Count;
        return _cosValues?.Length >= required &&
               _sinValues?.Length >= required &&
               _currentAlphas?.Length >= required;
    }

    private static void EnsureArrayCapacity<T>(ref T[]? array, int requiredSize) where T : struct
    {
        if (array == null || array.Length < requiredSize)
            array = new T[requiredSize];
    }

    private void EnsureProcessedSpectrumCapacity(int requiredSize)
    {
        if (_processedSpectrum?.Length >= requiredSize) return;

        if (_processedSpectrum != null)
            ArrayPool<float>.Shared.Return(_processedSpectrum);

        _processedSpectrum = ArrayPool<float>.Shared.Rent(requiredSize);
    }

    private static void ScaleSpectrum(float[] source, float[] target, int targetCount)
    {
        float blockSize = source.Length / (float)targetCount;

        for (int i = 0; i < targetCount; i++)
        {
            int start = (int)(blockSize * i);
            int end = (int)MathF.Min((int)(blockSize * (i + 1)), source.Length);

            target[i] = end > start ? CalculateAverage(source, start, end) : 0;
        }
    }

    private static float CalculateAverage(float[] source, int start, int end)
    {
        float sum = 0;
        for (int i = start; i < end; i++)
            sum += source[i];
        return sum / (end - start);
    }

    protected override void CleanupUnusedResources()
    {
        if (_processedSpectrum != null && _processedSpectrum.All(v => v < MIN_MAGNITUDE))
        {
            ArrayPool<float>.Shared.Return(_processedSpectrum);
            _processedSpectrum = null;
        }
    }

    protected override void OnDispose()
    {
        if (_processedSpectrum != null)
            ArrayPool<float>.Shared.Return(_processedSpectrum);

        _cosValues = null;
        _sinValues = null;
        _currentAlphas = null;
        _processedSpectrum = null;

        LogDebug("Disposed");
    }

    private readonly record struct AlphaGroup(int Start, int End, float Alpha);
}