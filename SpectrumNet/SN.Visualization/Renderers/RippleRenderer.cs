#nullable enable

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class RippleRenderer : EffectSpectrumRenderer<RippleRenderer.QualitySettings>
{
    private static readonly Lazy<RippleRenderer> _instance =
        new(() => new RippleRenderer());

    public static RippleRenderer GetInstance() => _instance.Value;

    private const float BASE_RIPPLE_RADIUS = 300f,
        RIPPLE_SPEED = 150f,
        BASE_RIPPLE_SPAWN_RATE = 0.1f,
        BASE_RIPPLE_WIDTH = 3f,
        SPAWN_DISTANCE_BASE = 100f,
        MAGNITUDE_RADIUS_SCALE = 200f,
        FADE_START_THRESHOLD = 0.8f,
        FADE_RANGE = 0.2f;

    private const float COLOR_ROTATION_SPEED = 0.5f,
        SATURATION_BASE = 80f,
        SATURATION_RANGE = 20f,
        BRIGHTNESS_BASE = 70f,
        BRIGHTNESS_RANGE = 30f,
        HUE_DEGREES = 360f,
        COLOR_WRAP = 1f;

    private const float MIN_SPAWN_MAGNITUDE = 0.15f,
        BAND_ANGLE_STEP = 45f;

    private const int SPECTRUM_BANDS = 8,
        BASE_MAX_RIPPLES_LOW = 20,
        BASE_MAX_RIPPLES_MEDIUM = 40,
        BASE_MAX_RIPPLES_HIGH = 60;

    private readonly List<RippleData> _ripples = new(BASE_MAX_RIPPLES_HIGH);
    private readonly float[] _bandMagnitudes = new float[SPECTRUM_BANDS];
    private float _lastSpawnTime;
    private float _colorRotation;
    private float _animationTime;

    public sealed class QualitySettings
    {
        public int MaxRipples { get; init; }
        public float StrokeWidth { get; init; }
        public float SpawnRate { get; init; }
        public bool UseColorRotation { get; init; }
        public bool UseAdaptiveMaxRadius { get; init; }
    }

    protected override IReadOnlyDictionary<RenderQuality, QualitySettings>
        QualitySettingsPresets
    { get; } = new Dictionary<RenderQuality, QualitySettings>
    {
        [RenderQuality.Low] = new()
        {
            MaxRipples = BASE_MAX_RIPPLES_LOW,
            StrokeWidth = BASE_RIPPLE_WIDTH * 0.8f,
            SpawnRate = BASE_RIPPLE_SPAWN_RATE * 2f,
            UseColorRotation = false,
            UseAdaptiveMaxRadius = false
        },
        [RenderQuality.Medium] = new()
        {
            MaxRipples = BASE_MAX_RIPPLES_MEDIUM,
            StrokeWidth = BASE_RIPPLE_WIDTH,
            SpawnRate = BASE_RIPPLE_SPAWN_RATE,
            UseColorRotation = true,
            UseAdaptiveMaxRadius = true
        },
        [RenderQuality.High] = new()
        {
            MaxRipples = BASE_MAX_RIPPLES_HIGH,
            StrokeWidth = BASE_RIPPLE_WIDTH * 1.2f,
            SpawnRate = BASE_RIPPLE_SPAWN_RATE * 0.8f,
            UseColorRotation = true,
            UseAdaptiveMaxRadius = true
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

        var renderData = CalculateRenderData(
            processedSpectrum,
            info,
            passedInPaint.Color);

        if (!ValidateRenderData(renderData))
            return;

        RenderVisualization(canvas, renderData);
    }

    private RenderData CalculateRenderData(
        float[] spectrum,
        SKImageInfo info,
        SKColor baseColor)
    {
        UpdateAnimationTime();
        UpdateBandMagnitudes(spectrum);
        UpdateRipples(1f / 60f);
        TrySpawnNewRipples(info, baseColor);
        UpdateColorRotation(1f / 60f);

        var boundingBox = CalculateBoundingBox(info);

        return new RenderData(
            Ripples: _ripples,
            BoundingBox: boundingBox,
            Width: info.Width,
            Height: info.Height,
            ColorRotation: _colorRotation,
            AverageIntensity: CalculateAverageIntensity(spectrum));
    }

    private static bool ValidateRenderData(RenderData data) =>
        data.Width > 0 &&
        data.Height > 0 &&
        data.BoundingBox.Width > 0 &&
        data.BoundingBox.Height > 0;

    private void RenderVisualization(
        SKCanvas canvas,
        RenderData data)
    {
        var settings = CurrentQualitySettings!;

        if (!IsAreaVisible(canvas, data.BoundingBox))
            return;

        RenderWithOverlay(canvas, () => RenderRipplesLayer(canvas, data, settings));
    }

    private void UpdateAnimationTime() => _animationTime += 1f / 60f;

    private void UpdateBandMagnitudes(float[] spectrum)
    {
        if (spectrum.Length == 0) return;

        int bandSize = CalculateBandSize(spectrum.Length);

        for (int i = 0; i < SPECTRUM_BANDS; i++)
            _bandMagnitudes[i] = CalculateBandMagnitude(spectrum, i, bandSize);
    }

    private static int CalculateBandSize(int spectrumLength) =>
        (int)MathF.Max(1, spectrumLength / SPECTRUM_BANDS);

    private static float CalculateBandMagnitude(float[] spectrum, int bandIndex, int bandSize)
    {
        int start = bandIndex * bandSize;
        int end = (int)MathF.Min((bandIndex + 1) * bandSize, spectrum.Length);

        float sum = 0;
        for (int j = start; j < end; j++)
            sum += spectrum[j];

        return sum / MathF.Max(1, end - start);
    }

    private static float CalculateAverageIntensity(float[] spectrum)
    {
        if (spectrum.Length == 0)
            return 0f;

        float sum = 0f;
        for (int i = 0; i < spectrum.Length; i++)
            sum += spectrum[i];

        return sum / spectrum.Length;
    }

    private void UpdateRipples(float deltaTime)
    {
        float speed = RIPPLE_SPEED * deltaTime;

        for (int i = _ripples.Count - 1; i >= 0; i--)
        {
            var ripple = _ripples[i];
            float newRadius = ripple.Radius + speed;

            if (newRadius > ripple.MaxRadius)
                _ripples.RemoveAt(i);
            else
                _ripples[i] = ripple with { Radius = newRadius };
        }
    }

    private void TrySpawnNewRipples(SKImageInfo info, SKColor baseColor)
    {
        if (!CanSpawnRipples())
            return;

        float centerX = info.Width * 0.5f;
        float centerY = info.Height * 0.5f;

        SpawnRipplesForBands(centerX, centerY, baseColor);
        _lastSpawnTime = _animationTime;
    }

    private bool CanSpawnRipples() =>
        _animationTime - _lastSpawnTime >= CurrentQualitySettings!.SpawnRate &&
        _ripples.Count < CurrentQualitySettings.MaxRipples;

    private void SpawnRipplesForBands(float centerX, float centerY, SKColor baseColor)
    {
        for (int i = 0; i < SPECTRUM_BANDS; i++)
        {
            if (!ShouldSpawnRippleForBand(i))
                continue;

            var ripple = CreateRippleForBand(i, centerX, centerY, baseColor);
            _ripples.Add(ripple);
        }
    }

    private bool ShouldSpawnRippleForBand(int bandIndex) =>
        _bandMagnitudes[bandIndex] >= MIN_SPAWN_MAGNITUDE &&
        _ripples.Count < CurrentQualitySettings!.MaxRipples;

    private RippleData CreateRippleForBand(int bandIndex, float centerX, float centerY, SKColor baseColor)
    {
        float angle = CalculateBandAngle(bandIndex);
        float distance = CalculateSpawnDistance(bandIndex);
        float maxRadius = CalculateMaxRadius(bandIndex);

        return new RippleData(
            X: centerX + MathF.Cos(angle) * distance,
            Y: centerY + MathF.Sin(angle) * distance,
            Radius: 0,
            MaxRadius: maxRadius,
            Magnitude: _bandMagnitudes[bandIndex],
            ColorHue: CalculateColorHue(bandIndex),
            BaseColor: baseColor);
    }

    private static SKRect CalculateBoundingBox(SKImageInfo info) =>
        new(0, 0, info.Width, info.Height);

    private static float CalculateBandAngle(int bandIndex) =>
        bandIndex * BAND_ANGLE_STEP * MathF.PI / 180f;

    private float CalculateSpawnDistance(int bandIndex) =>
        SPAWN_DISTANCE_BASE + _bandMagnitudes[bandIndex] * SPAWN_DISTANCE_BASE;

    private float CalculateMaxRadius(int bandIndex)
    {
        if (CurrentQualitySettings!.UseAdaptiveMaxRadius)
            return BASE_RIPPLE_RADIUS + _bandMagnitudes[bandIndex] * MAGNITUDE_RADIUS_SCALE;
        else
            return BASE_RIPPLE_RADIUS;
    }

    private float CalculateColorHue(int bandIndex)
    {
        if (CurrentQualitySettings!.UseColorRotation)
            return (_colorRotation + bandIndex / (float)SPECTRUM_BANDS) % COLOR_WRAP;
        else
            return (bandIndex / (float)SPECTRUM_BANDS) % COLOR_WRAP;
    }

    private void UpdateColorRotation(float deltaTime)
    {
        if (!CurrentQualitySettings!.UseColorRotation)
            return;

        _colorRotation += deltaTime * COLOR_ROTATION_SPEED;
        if (_colorRotation > COLOR_WRAP)
            _colorRotation -= COLOR_WRAP;
    }

    private static float CalculateRippleAlpha(RippleData ripple)
    {
        float progress = ripple.Radius / ripple.MaxRadius;
        float alpha = ripple.Magnitude;

        if (progress > FADE_START_THRESHOLD)
        {
            float fadeProgress = (progress - FADE_START_THRESHOLD) / FADE_RANGE;
            alpha *= 1f - fadeProgress;
        }

        return alpha;
    }

    private static SKColor CalculateRippleColor(RippleData ripple)
    {
        float saturation = SATURATION_BASE + ripple.Magnitude * SATURATION_RANGE;
        float brightness = BRIGHTNESS_BASE + ripple.Magnitude * BRIGHTNESS_RANGE;
        return SKColor.FromHsv(ripple.ColorHue * HUE_DEGREES, saturation, brightness);
    }

    private void RenderRipplesLayer(SKCanvas canvas, RenderData data, QualitySettings settings)
    {
        foreach (var ripple in data.Ripples)
        {
            float alpha = CalculateRippleAlpha(ripple);
            if (alpha <= 0) continue;

            SKColor color = CalculateRippleColor(ripple);
            DrawRipple(canvas, ripple, color, alpha, settings.StrokeWidth);
        }
    }

    private void DrawRipple(SKCanvas canvas, RippleData ripple, SKColor color, float alpha, float strokeWidth)
    {
        var paint = CreatePaint(color.WithAlpha(CalculateAlpha(alpha)), SKPaintStyle.Stroke);
        paint.StrokeWidth = strokeWidth;
        paint.IsAntialias = UseAntiAlias;

        try
        {
            canvas.DrawCircle(ripple.X, ripple.Y, ripple.Radius, paint);
        }
        finally
        {
            ReturnPaint(paint);
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
        RequestRedraw();
    }

    protected override void OnDispose()
    {
        _ripples.Clear();
        _animationTime = 0f;
        _lastSpawnTime = 0f;
        _colorRotation = 0f;
        base.OnDispose();
    }

    private record RippleData(
        float X,
        float Y,
        float Radius,
        float MaxRadius,
        float Magnitude,
        float ColorHue,
        SKColor BaseColor);

    private record RenderData(
        List<RippleData> Ripples,
        SKRect BoundingBox,
        int Width,
        int Height,
        float ColorRotation,
        float AverageIntensity);
}