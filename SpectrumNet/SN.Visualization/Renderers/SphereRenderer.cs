#nullable enable

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class SphereRenderer : EffectSpectrumRenderer<SphereRenderer.QualitySettings>
{
    private static readonly Lazy<SphereRenderer> _instance =
        new(() => new SphereRenderer());

    public static SphereRenderer GetInstance() => _instance.Value;

    private const float MIN_MAGNITUDE = 0.01f,
        MAX_INTENSITY_MULTIPLIER = 3f,
        MIN_ALPHA = 0.1f,
        PI_OVER_180 = MathF.PI / 180f,
        DEFAULT_RADIUS = 40f,
        DEFAULT_RADIUS_OVERLAY = 20f,
        MIN_RADIUS = 1.0f,
        DEFAULT_SPACING = 10f,
        DEFAULT_SPACING_OVERLAY = 5f,
        ALPHA_THRESHOLD = 0.1f,
        MIN_CIRCLE_SIZE = 2f,
        SPACING_FACTOR = 0.2f,
        RADIUS_REDUCTION_FACTOR = 0.2f,
        SPACING_REDUCTION_FACTOR = 0.1f,
        RADIUS_SPACING_FACTOR = 0.5f,
        SPACING_SPACING_FACTOR = 0.3f;

    private const int DEFAULT_COUNT = 8,
        DEFAULT_COUNT_OVERLAY = 16,
        MAX_ALPHA_GROUPS = 5,
        MIN_SPHERE_COUNT = 4,
        MAX_SPHERE_COUNT = 64,
        SPHERE_COUNT_DIVISOR = 2;

    private static readonly float[] _gradientPositions = [0.0f, 1.0f];

    private float _currentRadius = DEFAULT_RADIUS;
    private float _currentSpacing = DEFAULT_SPACING;
    private int _currentCount = DEFAULT_COUNT;
    private float[]? _cosValues;
    private float[]? _sinValues;
    private float[]? _currentAlphas;

    public sealed class QualitySettings
    {
        public bool UseGradient { get; init; }
        public bool UseHighQualityBlending { get; init; }
        public float SmoothingFactor { get; init; }
        public float ResponseSpeed { get; init; }
    }

    protected override IReadOnlyDictionary<RenderQuality, QualitySettings>
        QualitySettingsPresets
    { get; } = new Dictionary<RenderQuality, QualitySettings>
    {
        [RenderQuality.Low] = new()
        {
            UseGradient = false,
            UseHighQualityBlending = false,
            SmoothingFactor = 0.1f,
            ResponseSpeed = 0.15f
        },
        [RenderQuality.Medium] = new()
        {
            UseGradient = true,
            UseHighQualityBlending = false,
            SmoothingFactor = 0.2f,
            ResponseSpeed = 0.2f
        },
        [RenderQuality.High] = new()
        {
            UseGradient = true,
            UseHighQualityBlending = true,
            SmoothingFactor = 0.3f,
            ResponseSpeed = 0.25f
        }
    };

    protected override void RenderEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        RenderParameters renderParams,
        SKPaint passedInPaint)
    {
        if (CurrentQualitySettings == null || renderParams.EffectiveBarCount <= 0)
            return;

        var (isValid, processedSpectrum) = ProcessSpectrum(
            spectrum,
            renderParams.EffectiveBarCount,
            applyTemporalSmoothing: true);

        if (!isValid || processedSpectrum == null)
            return;

        var sphereData = CalculateSphereData(
            processedSpectrum,
            info,
            renderParams);

        if (!ValidateSphereData(sphereData))
            return;

        RenderSphereVisualization(
            canvas,
            sphereData,
            passedInPaint);
    }

    private SphereRenderData CalculateSphereData(
        float[] spectrum,
        SKImageInfo info,
        RenderParameters renderParams)
    {
        UpdateConfiguration(renderParams.EffectiveBarCount, renderParams.BarSpacing, info);
        UpdateAlphas(spectrum);

        int sphereCount = Min(spectrum.Length, _currentCount);
        float maxRadius = CalculateMaxRadius(info.Width, info.Height);

        var alphaGroups = CalculateAlphaGroups(sphereCount);

        return new SphereRenderData(
            Spectrum: spectrum,
            CenterX: info.Width / 2f,
            CenterY: info.Height / 2f,
            MaxRadius: maxRadius,
            SphereCount: sphereCount,
            AlphaGroups: alphaGroups);
    }

    private bool ValidateSphereData(SphereRenderData data)
    {
        return data.SphereCount > 0 &&
               data.MaxRadius > 0 &&
               data.AlphaGroups.Length > 0 &&
               _cosValues != null &&
               _sinValues != null &&
               _currentAlphas != null;
    }

    private void RenderSphereVisualization(
        SKCanvas canvas,
        SphereRenderData data,
        SKPaint basePaint)
    {
        var settings = CurrentQualitySettings!;

        RenderWithOverlay(canvas, () =>
        {
            if (UseAdvancedEffects && settings.UseGradient)
                RenderGradientSpheres(canvas, data, basePaint, settings);
            else
                RenderSolidSpheres(canvas, data, basePaint);
        });
    }

    private void RenderGradientSpheres(
        SKCanvas canvas,
        SphereRenderData data,
        SKPaint basePaint,
        QualitySettings settings)
    {
        foreach (var group in data.AlphaGroups)
        {
            if (group.Alpha < MIN_ALPHA) continue;

            using var shader = CreateRadialGradientShader(basePaint.Color, group.Alpha);
            var spherePaint = CreatePaint(basePaint.Color, SKPaintStyle.Fill, shader);

            try
            {
                RenderSphereGroup(canvas, data, group, spherePaint, useTransform: true);
            }
            finally
            {
                ReturnPaint(spherePaint);
            }
        }
    }

    private void RenderSolidSpheres(
        SKCanvas canvas,
        SphereRenderData data,
        SKPaint basePaint)
    {
        var spherePaint = CreatePaint(basePaint.Color, SKPaintStyle.Fill);

        try
        {
            foreach (var group in data.AlphaGroups)
            {
                if (group.Alpha < MIN_ALPHA) continue;

                spherePaint.Color = basePaint.Color.WithAlpha(CalculateAlpha(group.Alpha));
                RenderSphereGroup(canvas, data, group, spherePaint, useTransform: false);
            }
        }
        finally
        {
            ReturnPaint(spherePaint);
        }
    }

    private void RenderSphereGroup(
        SKCanvas canvas,
        SphereRenderData data,
        AlphaGroup group,
        SKPaint paint,
        bool useTransform)
    {
        for (int i = group.Start; i < group.End && i < data.SphereCount; i++)
        {
            if (data.Spectrum[i] < MIN_MAGNITUDE) continue;

            var (x, y) = CalculateSpherePosition(i, data.CenterX, data.CenterY, data.MaxRadius);
            float size = CalculateSphereSize(data.Spectrum[i]);

            var bounds = new SKRect(x - size, y - size, x + size, y + size);

            if (!IsAreaVisible(canvas, bounds)) continue;

            if (useTransform)
            {
                canvas.Save();
                canvas.Translate(x, y);
                canvas.Scale(size);
                canvas.DrawCircle(0, 0, 1.0f, paint);
                canvas.Restore();
            }
            else
            {
                canvas.DrawCircle(x, y, size, paint);
            }
        }
    }

    private (float x, float y) CalculateSpherePosition(
        int index,
        float centerX,
        float centerY,
        float radius)
    {
        if (_cosValues == null || _sinValues == null || index >= _cosValues.Length)
            return (centerX, centerY);

        return (
            centerX + _cosValues[index] * radius,
            centerY + _sinValues[index] * radius);
    }

    private float CalculateSphereSize(float magnitude) =>
        Max(magnitude * _currentRadius, MIN_CIRCLE_SIZE) + _currentSpacing * SPACING_FACTOR;

    private float CalculateMaxRadius(float width, float height) =>
        Min(width, height) / 2f - _currentRadius - _currentSpacing;

    private static SKShader CreateRadialGradientShader(SKColor baseColor, float alpha) =>
        SKShader.CreateRadialGradient(
            new SKPoint(0, 0),
            1.0f,
            [
                baseColor.WithAlpha((byte)(255 * alpha)),
                baseColor.WithAlpha(0)
            ],
            _gradientPositions,
            SKShaderTileMode.Clamp);

    private AlphaGroup[] CalculateAlphaGroups(int count)
    {
        if (_currentAlphas == null || count == 0)
            return [];

        var groups = new List<AlphaGroup>(MAX_ALPHA_GROUPS);
        int currentStart = 0;
        float currentAlpha = _currentAlphas[0];

        for (int i = 1; i < count && i < _currentAlphas.Length; i++)
        {
            if (Abs(_currentAlphas[i] - currentAlpha) > ALPHA_THRESHOLD ||
                groups.Count >= MAX_ALPHA_GROUPS - 1)
            {
                groups.Add(new AlphaGroup(currentStart, i, currentAlpha));
                currentStart = i;
                currentAlpha = _currentAlphas[i];
            }
        }

        groups.Add(new AlphaGroup(currentStart, count, currentAlpha));
        return [.. groups];
    }

    private void UpdateAlphas(float[] spectrum)
    {
        if (_currentAlphas == null) return;

        var settings = CurrentQualitySettings!;
        int length = Min(spectrum.Length, _currentAlphas.Length);

        for (int i = 0; i < length; i++)
        {
            float targetAlpha = Max(MIN_ALPHA, spectrum[i] * MAX_INTENSITY_MULTIPLIER);
            _currentAlphas[i] = Lerp(_currentAlphas[i], targetAlpha, settings.ResponseSpeed);
        }
    }

    private void UpdateConfiguration(int barCount, float barSpacing, SKImageInfo info)
    {
        float radius = CalculateAdaptiveRadius(barCount, barSpacing);
        float spacing = CalculateAdaptiveSpacing(barCount, barSpacing);
        int count = CalculateAdaptiveCount(barCount);

        float maxPossibleRadius = Min(info.Width, info.Height) / 2f - (radius + spacing);
        radius = Max(MIN_RADIUS, Min(radius, maxPossibleRadius));

        if (_currentRadius != radius || _currentSpacing != spacing || _currentCount != count)
        {
            _currentRadius = radius;
            _currentSpacing = spacing;
            _currentCount = Clamp(count, MIN_SPHERE_COUNT, MAX_SPHERE_COUNT);

            EnsureArraysInitialized();
            PrecomputeTrigValues();
        }
    }

    private float CalculateAdaptiveRadius(int barCount, float barSpacing)
    {
        float baseRadius = IsOverlayActive ? DEFAULT_RADIUS_OVERLAY : DEFAULT_RADIUS;
        return Max(5f, baseRadius - barCount * RADIUS_REDUCTION_FACTOR + barSpacing * RADIUS_SPACING_FACTOR);
    }

    private float CalculateAdaptiveSpacing(int barCount, float barSpacing)
    {
        float baseSpacing = IsOverlayActive ? DEFAULT_SPACING_OVERLAY : DEFAULT_SPACING;
        return Max(2f, baseSpacing - barCount * SPACING_REDUCTION_FACTOR + barSpacing * SPACING_SPACING_FACTOR);
    }

    private int CalculateAdaptiveCount(int barCount)
    {
        int baseCount = IsOverlayActive ? DEFAULT_COUNT_OVERLAY : DEFAULT_COUNT;
        return Max(baseCount, barCount / SPHERE_COUNT_DIVISOR);
    }

    private void EnsureArraysInitialized()
    {
        if (_cosValues == null || _cosValues.Length < _currentCount)
            _cosValues = new float[_currentCount];

        if (_sinValues == null || _sinValues.Length < _currentCount)
            _sinValues = new float[_currentCount];

        if (_currentAlphas == null || _currentAlphas.Length < _currentCount)
        {
            _currentAlphas = new float[_currentCount];
            Array.Fill(_currentAlphas, MIN_ALPHA);
        }
    }

    private void PrecomputeTrigValues()
    {
        if (_cosValues == null || _sinValues == null) return;

        float angleStep = 360f / _currentCount * PI_OVER_180;

        for (int i = 0; i < _currentCount && i < _cosValues.Length; i++)
        {
            float angle = i * angleStep;
            _cosValues[i] = MathF.Cos(angle);
            _sinValues[i] = MathF.Sin(angle);
        }
    }

    protected override int GetMaxBarsForQuality() => Quality switch
    {
        RenderQuality.Low => 32,
        RenderQuality.Medium => 64,
        RenderQuality.High => 128,
        _ => 64
    };

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();

        float smoothingFactor = Quality switch
        {
            RenderQuality.Low => 0.4f,
            RenderQuality.Medium => 0.3f,
            RenderQuality.High => 0.25f,
            _ => 0.3f
        };

        if (IsOverlayActive)
            smoothingFactor *= 1.2f;

        SetProcessingSmoothingFactor(smoothingFactor);

        EnsureArraysInitialized();
        PrecomputeTrigValues();

        RequestRedraw();
    }

    protected override void OnDispose()
    {
        _cosValues = null;
        _sinValues = null;
        _currentAlphas = null;
        _currentRadius = DEFAULT_RADIUS;
        _currentSpacing = DEFAULT_SPACING;
        _currentCount = DEFAULT_COUNT;

        base.OnDispose();
    }

    private record SphereRenderData(
        float[] Spectrum,
        float CenterX,
        float CenterY,
        float MaxRadius,
        int SphereCount,
        AlphaGroup[] AlphaGroups);

    private readonly record struct AlphaGroup(int Start, int End, float Alpha);
}