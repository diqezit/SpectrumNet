#nullable enable

using static SpectrumNet.SN.Visualization.Renderers.RippleRenderer.Constants;
using static System.MathF;

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class RippleRenderer : EffectSpectrumRenderer
{
    private const string LogPrefix = nameof(RippleRenderer);

    private static readonly Lazy<RippleRenderer> _instance =
        new(() => new RippleRenderer());

    private RippleRenderer() { }

    public static RippleRenderer GetInstance() => _instance.Value;

    public static class Constants
    {
        public const float
            BASE_RIPPLE_RADIUS = 300f,
            RIPPLE_SPEED = 150f,
            BASE_RIPPLE_SPAWN_RATE = 0.1f,
            BASE_RIPPLE_WIDTH = 3f,
            FADE_DISTANCE = 50f,
            COLOR_ROTATION_SPEED = 0.5f,
            MIN_SPAWN_MAGNITUDE = 0.15f,
            MAGNITUDE_RADIUS_SCALE = 200f,
            BAND_ANGLE_STEP = 45f,
            SPAWN_DISTANCE_BASE = 100f,
            SATURATION_BASE = 80f,
            SATURATION_RANGE = 20f,
            BRIGHTNESS_BASE = 70f,
            BRIGHTNESS_RANGE = 30f,
            HUE_DEGREES = 360f,
            COLOR_WRAP = 1f,
            FADE_START_THRESHOLD = 0.8f,
            FADE_RANGE = 0.2f;

        public const int
            BASE_MAX_RIPPLES_LOW = 20,
            BASE_MAX_RIPPLES_MEDIUM = 40,
            BASE_MAX_RIPPLES_HIGH = 60,
            SPECTRUM_BANDS = 8;

        public static readonly Dictionary<RenderQuality, QualitySettings> QualityPresets = new()
        {
            [RenderQuality.Low] = new(
                BaseMaxRipples: BASE_MAX_RIPPLES_LOW,
                StrokeWidth: BASE_RIPPLE_WIDTH * 0.8f,
                SpawnRate: BASE_RIPPLE_SPAWN_RATE * 2f
            ),
            [RenderQuality.Medium] = new(
                BaseMaxRipples: BASE_MAX_RIPPLES_MEDIUM,
                StrokeWidth: BASE_RIPPLE_WIDTH,
                SpawnRate: BASE_RIPPLE_SPAWN_RATE
            ),
            [RenderQuality.High] = new(
                BaseMaxRipples: BASE_MAX_RIPPLES_HIGH,
                StrokeWidth: BASE_RIPPLE_WIDTH * 1.2f,
                SpawnRate: BASE_RIPPLE_SPAWN_RATE * 0.8f
            )
        };

        public record QualitySettings(
            int BaseMaxRipples,
            float StrokeWidth,
            float SpawnRate
        );
    }

    private readonly List<Ripple> _ripples = new(BASE_MAX_RIPPLES_HIGH);
    private readonly float[] _bandMagnitudes = new float[SPECTRUM_BANDS];
    private float _lastSpawnTime;
    private float _colorRotation;
    private QualitySettings _currentSettings = QualityPresets[RenderQuality.Medium];

    private readonly SKPaint _ripplePaint = new()
    {
        Style = SKPaintStyle.Stroke,
        StrokeWidth = BASE_RIPPLE_WIDTH,
        IsAntialias = true
    };

    protected override void OnInitialize()
    {
        base.OnInitialize();
        _logger.Log(LogLevel.Debug, LogPrefix, "Initialized");
    }

    protected override void OnQualitySettingsApplied()
    {
        _currentSettings = QualityPresets[Quality];
        _ripplePaint.StrokeWidth = _currentSettings.StrokeWidth;
        _ripplePaint.IsAntialias = UseAntiAlias;
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
        float deltaTime = _animationTimer.DeltaTime;
        float currentTime = _animationTimer.Time;

        UpdateBandMagnitudes(spectrum, barCount);
        UpdateRipples(deltaTime);
        TrySpawnNewRipples(currentTime, info, paint.Color);
        RenderRipples(canvas);
        UpdateColorRotation(deltaTime);
    }

    private void UpdateBandMagnitudes(float[] spectrum, int barCount)
    {
        if (spectrum.Length == 0) return;

        int bandSize = CalculateBandSize(spectrum.Length, barCount);

        for (int i = 0; i < SPECTRUM_BANDS; i++)
            _bandMagnitudes[i] = CalculateBandMagnitude(spectrum, i, bandSize);
    }

    private static int CalculateBandSize(int spectrumLength, int barCount) =>
        Max(1, Min(spectrumLength, barCount) / SPECTRUM_BANDS);

    private static float CalculateBandMagnitude(float[] spectrum, int bandIndex, int bandSize)
    {
        int start = bandIndex * bandSize;
        int end = Min((bandIndex + 1) * bandSize, spectrum.Length);

        float sum = 0;
        for (int j = start; j < end; j++)
            sum += spectrum[j];

        return sum / Max(1, end - start);
    }

    private void UpdateRipples(float deltaTime)
    {
        float speed = RIPPLE_SPEED * deltaTime;

        for (int i = _ripples.Count - 1; i >= 0; i--)
        {
            var ripple = _ripples[i];
            ripple.Radius += speed;

            if (IsRippleExpired(ripple))
                _ripples.RemoveAt(i);
        }
    }

    private static bool IsRippleExpired(Ripple ripple) =>
        ripple.Radius > ripple.MaxRadius;

    private void TrySpawnNewRipples(float currentTime, SKImageInfo info, SKColor baseColor)
    {
        if (!CanSpawnRipples(currentTime))
            return;

        float centerX = info.Width * 0.5f;
        float centerY = info.Height * 0.5f;

        SpawnRipplesForBands(centerX, centerY, baseColor);
        _lastSpawnTime = currentTime;
    }

    private bool CanSpawnRipples(float currentTime) =>
        currentTime - _lastSpawnTime >= _currentSettings.SpawnRate &&
        _ripples.Count < _currentSettings.BaseMaxRipples;

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
        _ripples.Count < _currentSettings.BaseMaxRipples;

    private Ripple CreateRippleForBand(int bandIndex, float centerX, float centerY, SKColor baseColor)
    {
        float angle = CalculateBandAngle(bandIndex);
        float distance = CalculateSpawnDistance(bandIndex);
        float maxRadius = CalculateMaxRadius(bandIndex);

        return new Ripple
        {
            X = centerX + Cos(angle) * distance,
            Y = centerY + Sin(angle) * distance,
            Radius = 0,
            MaxRadius = maxRadius,
            Magnitude = _bandMagnitudes[bandIndex],
            ColorHue = CalculateColorHue(bandIndex),
            BaseColor = baseColor
        };
    }

    private static float CalculateBandAngle(int bandIndex) =>
        bandIndex * BAND_ANGLE_STEP * MathF.PI / 180f;

    private float CalculateSpawnDistance(int bandIndex) =>
        SPAWN_DISTANCE_BASE + _bandMagnitudes[bandIndex] * SPAWN_DISTANCE_BASE;

    private float CalculateMaxRadius(int bandIndex) =>
        BASE_RIPPLE_RADIUS + _bandMagnitudes[bandIndex] * MAGNITUDE_RADIUS_SCALE;

    private float CalculateColorHue(int bandIndex) =>
        (_colorRotation + bandIndex / (float)SPECTRUM_BANDS) % COLOR_WRAP;

    private void RenderRipples(SKCanvas canvas)
    {
        foreach (var ripple in _ripples)
        {
            float alpha = CalculateRippleAlpha(ripple);
            if (alpha <= 0) continue;

            SKColor color = CalculateRippleColor(ripple);
            DrawRipple(canvas, ripple, color, alpha);
        }
    }

    private static float CalculateRippleAlpha(Ripple ripple)
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

    private static SKColor CalculateRippleColor(Ripple ripple)
    {
        float saturation = SATURATION_BASE + ripple.Magnitude * SATURATION_RANGE;
        float brightness = BRIGHTNESS_BASE + ripple.Magnitude * BRIGHTNESS_RANGE;
        return SKColor.FromHsv(ripple.ColorHue * HUE_DEGREES, saturation, brightness);
    }

    private void DrawRipple(SKCanvas canvas, Ripple ripple, SKColor color, float alpha)
    {
        _ripplePaint.Color = color.WithAlpha((byte)(alpha * 255));
        canvas.DrawCircle(ripple.X, ripple.Y, ripple.Radius, _ripplePaint);
    }

    private void UpdateColorRotation(float deltaTime)
    {
        _colorRotation += deltaTime * COLOR_ROTATION_SPEED;
        if (_colorRotation > COLOR_WRAP)
            _colorRotation -= COLOR_WRAP;
    }

    protected override void OnDispose()
    {
        _ripplePaint?.Dispose();
        _ripples.Clear();
        base.OnDispose();
    }

    private class Ripple
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Radius { get; set; }
        public float MaxRadius { get; set; }
        public float Magnitude { get; set; }
        public float ColorHue { get; set; }
        public SKColor BaseColor { get; set; }
    }
}