#nullable enable

using static System.MathF;
using static SpectrumNet.SN.Visualization.Renderers.ConstellationRenderer.Constants;

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class ConstellationRenderer : EffectSpectrumRenderer
{
    private const string LogPrefix = nameof(ConstellationRenderer);

    private static readonly Lazy<ConstellationRenderer> _instance =
        new(() => new ConstellationRenderer());

    private ConstellationRenderer() { }

    public static ConstellationRenderer GetInstance() => _instance.Value;

    public static class Constants
    {
        public const float
            BASE_STAR_SIZE = 1.5f,
            MAX_STAR_SIZE = 12f,
            MIN_BRIGHTNESS = 0.2f,
            TWINKLE_SPEED = 2f,
            MOVEMENT_FACTOR = 25f,
            SPAWN_THRESHOLD = 0.05f,
            SPECTRUM_SENSITIVITY = 18f;

        public const int
            DEFAULT_STAR_COUNT = 360,
            OVERLAY_STAR_COUNT = 120,
            SPAWN_RATE = 20;

        public static readonly Dictionary<RenderQuality, QualitySettings> QualityPresets = new()
        {
            [RenderQuality.Low] = new(
                UseGlow: false,
                GlowSize: 1f
            ),
            [RenderQuality.Medium] = new(
                UseGlow: true,
                GlowSize: 1.3f
            ),
            [RenderQuality.High] = new(
                UseGlow: true,
                GlowSize: 1.5f
            )
        };

        public record QualitySettings(
            bool UseGlow,
            float GlowSize
        );
    }

    private QualitySettings _currentSettings = QualityPresets[RenderQuality.Medium];
    private Star[] _stars = Array.Empty<Star>();
    private float _lowSpectrum, _midSpectrum, _highSpectrum, _energy;
    private float _spawnAccumulator;
    private readonly Random _random = new();

    protected override void OnInitialize()
    {
        base.OnInitialize();
        InitializeStars();
        _logger.Log(LogLevel.Debug, LogPrefix, "Initialized");
    }

    protected override void OnQualitySettingsApplied()
    {
        _currentSettings = QualityPresets[Quality];
        _logger.Log(LogLevel.Debug, LogPrefix, $"Quality changed to {Quality}");
    }

    protected override void OnConfigurationChanged()
    {
        int count = _isOverlayActive ? OVERLAY_STAR_COUNT : DEFAULT_STAR_COUNT;
        if (_stars.Length != count)
        {
            InitializeStars();
        }
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
        _logger.Safe(
            () => RenderStarfield(canvas, spectrum, info, paint),
            LogPrefix,
            "Error during rendering"
        );
    }

    private void RenderStarfield(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        SKPaint basePaint)
    {
        UpdateSpectrum(spectrum);
        UpdateStars(info);
        DrawStars(canvas, basePaint);
    }

    private void UpdateSpectrum(float[] spectrum)
    {
        if (spectrum.Length < 3)
        {
            float avg = spectrum.Length > 0 ? spectrum.Average() : 0f;
            _lowSpectrum = _midSpectrum = _highSpectrum = avg;
        }
        else
        {
            int third = spectrum.Length / 3;
            _lowSpectrum = Lerp(_lowSpectrum, GetAverage(spectrum, 0, third), 0.1f);
            _midSpectrum = Lerp(_midSpectrum, GetAverage(spectrum, third, third * 2), 0.1f);
            _highSpectrum = Lerp(_highSpectrum, GetAverage(spectrum, third * 2, spectrum.Length), 0.1f);
        }
        _energy = (_lowSpectrum + _midSpectrum + _highSpectrum) / 3f;
    }

    private static float GetAverage(
        float[] array,
        int start,
        int end)
    {
        if (start >= end) return 0f;
        float sum = 0f;
        for (int i = start; i < end; i++)
        {
            sum += array[i];
        }
        return sum / (end - start);
    }

    private static float Lerp(
        float current,
        float target,
        float amount) =>
        current * (1f - amount) + target * amount;

    private void UpdateStars(SKImageInfo info)
    {
        float deltaTime = 0.016f;
        _spawnAccumulator += _energy * SPAWN_RATE * deltaTime;

        for (int i = 0; i < _stars.Length; i++)
        {
            ref var star = ref _stars[i];
            if (!star.Active) continue;

            star.Lifetime -= deltaTime;
            if (star.Lifetime <= 0)
            {
                star.Active = false;
                continue;
            }

            UpdateStarPosition(ref star, deltaTime, info);
            UpdateStarVisuals(ref star);
        }

        TrySpawnStars(info);
    }

    private void UpdateStarPosition(
        ref Star star,
        float deltaTime,
        SKImageInfo info)
    {
        if (_energy < MIN_MAGNITUDE_THRESHOLD) return;

        float angle = _time * star.Speed;
        float velocityX = (_lowSpectrum * Sin(angle) +
                          _midSpectrum * Cos(angle * 1.3f) +
                          _highSpectrum * Sin(angle * 1.8f)) * SPECTRUM_SENSITIVITY;
        float velocityY = (_lowSpectrum * Cos(angle) +
                          _midSpectrum * Sin(angle * 1.3f) +
                          _highSpectrum * Cos(angle * 1.8f)) * SPECTRUM_SENSITIVITY;

        star.X = Clamp(star.X + velocityX * deltaTime * MOVEMENT_FACTOR, 0, info.Width);
        star.Y = Clamp(star.Y + velocityY * deltaTime * MOVEMENT_FACTOR, 0, info.Height);
    }

    private void UpdateStarVisuals(ref Star star)
    {
        float lifetimeRatio = star.Lifetime / star.MaxLifetime;
        star.Opacity = lifetimeRatio < 0.2f ? lifetimeRatio * 5f : 1f;

        float twinkle = Sin(_time * TWINKLE_SPEED * star.TwinkleSpeed + star.Phase) * 0.3f;
        star.Brightness = Clamp(0.8f + twinkle + _energy * 0.5f, MIN_BRIGHTNESS, 1.5f);
    }

    private void TrySpawnStars(SKImageInfo info)
    {
        if (_spawnAccumulator < 1f || _energy < SPAWN_THRESHOLD) return;

        int toSpawn = Min((int)_spawnAccumulator, 5);
        _spawnAccumulator -= toSpawn;

        for (int i = 0; i < toSpawn; i++)
        {
            for (int j = 0; j < _stars.Length; j++)
            {
                if (!_stars[j].Active)
                {
                    InitializeStar(ref _stars[j], info);
                    break;
                }
            }
        }
    }

    private void InitializeStar(
        ref Star star,
        SKImageInfo info)
    {
        star.Active = true;
        star.X = _random.NextSingle() * info.Width;
        star.Y = _random.NextSingle() * info.Height;
        star.Lifetime = star.MaxLifetime = 3f + _random.NextSingle() * 9f;
        star.Size = BASE_STAR_SIZE + _random.NextSingle() * (MAX_STAR_SIZE - BASE_STAR_SIZE);
        star.Speed = 0.5f + _random.NextSingle() * 2f;
        star.TwinkleSpeed = 0.8f + _random.NextSingle() * 0.4f;
        star.Phase = _random.NextSingle() * MathF.Tau;
        star.Opacity = 0f;
        star.Brightness = 0.8f;
        star.Color = GetStarColor();
    }

    private SKColor GetStarColor()
    {
        if (_lowSpectrum > MathF.Max(_midSpectrum, _highSpectrum))
            return new SKColor((byte)(200 + _random.Next(55)),
                             (byte)(100 + _random.Next(100)),
                             (byte)(100 + _random.Next(100)));

        if (_highSpectrum > MathF.Max(_lowSpectrum, _midSpectrum))
            return new SKColor((byte)(100 + _random.Next(100)),
                             (byte)(100 + _random.Next(100)),
                             (byte)(200 + _random.Next(55)));

        return new SKColor((byte)(150 + _random.Next(105)),
                         (byte)(150 + _random.Next(105)),
                         (byte)(150 + _random.Next(105)));
    }

    private void DrawStars(
        SKCanvas canvas,
        SKPaint basePaint)
    {
        using var paint = _paintPool.Get();
        paint.IsAntialias = _useAntiAlias;
        paint.Style = SKPaintStyle.Fill;

        foreach (ref readonly var star in _stars.AsSpan())
        {
            if (!star.Active) continue;

            byte alpha = (byte)(255 * star.Brightness * star.Opacity);
            if (alpha < 10) continue;

            paint.Color = star.Color.WithAlpha(alpha);
            float size = star.Size * (1f + _energy * 0.5f);

            canvas.DrawCircle(star.X, star.Y, size, paint);

            if (_useAdvancedEffects && _currentSettings.UseGlow && alpha > 180)
            {
                paint.Color = star.Color.WithAlpha((byte)(alpha / 5));
                canvas.DrawCircle(star.X, star.Y, size * _currentSettings.GlowSize, paint);
            }
        }
    }

    private void InitializeStars()
    {
        int count = _isOverlayActive ? OVERLAY_STAR_COUNT : DEFAULT_STAR_COUNT;
        _stars = new Star[count];
    }

    private struct Star
    {
        public float X, Y, Size, Brightness, Speed, TwinkleSpeed,
                     Lifetime, MaxLifetime, Opacity, Phase;
        public SKColor Color;
        public bool Active;
    }

    protected override void OnDispose()
    {
        _stars = [];
        base.OnDispose();
        _logger.Log(LogLevel.Debug, LogPrefix, "Disposed");
    }
}