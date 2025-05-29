//#nullable enable

//using static System.MathF;
//using static SpectrumNet.SN.Visualization.Renderers.ConstellationRenderer.Constants;

//namespace SpectrumNet.SN.Visualization.Renderers;

//public sealed class ConstellationRenderer() : EffectSpectrumRenderer
//{
//    private const string LogPrefix = nameof(ConstellationRenderer);

//    private static readonly Lazy<ConstellationRenderer> _instance =
//        new(() => new ConstellationRenderer());

//    public static ConstellationRenderer GetInstance() => _instance.Value;

//    public static class Constants
//    {
//        public const float
//            BASE_STAR_SIZE = 1.5f,
//            MAX_STAR_SIZE = 12f,
//            MIN_BRIGHTNESS = 0.2f,
//            TWINKLE_SPEED = 2f,
//            MOVEMENT_FACTOR = 25f,
//            SPAWN_THRESHOLD = 0.05f,
//            SPECTRUM_SENSITIVITY = 18f;

//        public const int
//            DEFAULT_STAR_COUNT = 360,
//            OVERLAY_STAR_COUNT = 120,
//            SPAWN_RATE = 20;

//        public static readonly Dictionary<RenderQuality, QualitySettings> QualityPresets = new()
//        {
//            [RenderQuality.Low] = new(
//                UseGlow: false,
//                GlowSize: 1f
//            ),
//            [RenderQuality.Medium] = new(
//                UseGlow: true,
//                GlowSize: 1.3f
//            ),
//            [RenderQuality.High] = new(
//                UseGlow: true,
//                GlowSize: 1.5f
//            )
//        };

//        public record QualitySettings(
//            bool UseGlow,
//            float GlowSize
//        );
//    }

//    private QualitySettings _currentSettings = QualityPresets[RenderQuality.Medium];
//    private Star[] _stars = [];
//    private float _spawnAccumulator;
//    private readonly Random _random = new();

//    protected override void OnInitialize()
//    {
//        base.OnInitialize();
//        InitializeStars();
//    }

//    protected override void OnQualitySettingsApplied() =>
//        _currentSettings = QualityPresets[Quality];

//    protected override void OnConfigurationChanged()
//    {
//        int count = IsOverlayActive ? OVERLAY_STAR_COUNT : DEFAULT_STAR_COUNT;
//        if (_stars.Length != count)
//            InitializeStars();
//    }

//    protected override void RenderEffect(
//        SKCanvas canvas,
//        float[] spectrum,
//        SKImageInfo info,
//        float barWidth,
//        float barSpacing,
//        int barCount,
//        SKPaint paint)
//    {
//        var bands = ProcessSpectrumBands(spectrum, 3);
//        float lowSpectrum = bands.Length > 0 ? bands[0] : 0f;
//        float midSpectrum = bands.Length > 1 ? bands[1] : 0f;
//        float highSpectrum = bands.Length > 2 ? bands[2] : 0f;
//        float energy = (lowSpectrum + midSpectrum + highSpectrum) / 3f;

//        AnimateValues(
//            [lowSpectrum, midSpectrum, highSpectrum, energy],
//            0.1f);
//        var animated = GetAnimatedValues();

//        UpdateStars(
//            info,
//            animated[0],
//            animated[1],
//            animated[2],
//            animated[3]);
//        DrawStars(canvas, paint);
//    }

//    private void UpdateStars(
//        SKImageInfo info,
//        float lowSpectrum,
//        float midSpectrum,
//        float highSpectrum,
//        float energy)
//    {
//        float deltaTime = GetAnimationDeltaTime();
//        _spawnAccumulator += energy * SPAWN_RATE * deltaTime;

//        for (int i = 0; i < _stars.Length; i++)
//        {
//            ref var star = ref _stars[i];
//            if (!star.Active) continue;

//            star.Lifetime -= deltaTime;
//            if (star.Lifetime <= 0)
//            {
//                star.Active = false;
//                continue;
//            }

//            UpdateStarPosition(
//                ref star,
//                deltaTime,
//                info,
//                lowSpectrum,
//                midSpectrum,
//                highSpectrum);
//            UpdateStarVisuals(ref star, energy);
//        }

//        TrySpawnStars(info, energy);
//    }

//    private void UpdateStarPosition(
//        ref Star star,
//        float deltaTime,
//        SKImageInfo info,
//        float lowSpectrum,
//        float midSpectrum,
//        float highSpectrum)
//    {
//        float angle = GetAnimationTime() * star.Speed;
//        float velocityX = (lowSpectrum * Sin(angle) +
//                          midSpectrum * Cos(angle * 1.3f) +
//                          highSpectrum * Sin(angle * 1.8f)) * SPECTRUM_SENSITIVITY;
//        float velocityY = (lowSpectrum * Cos(angle) +
//                          midSpectrum * Sin(angle * 1.3f) +
//                          highSpectrum * Cos(angle * 1.8f)) * SPECTRUM_SENSITIVITY;

//        star.X = Clamp(
//            star.X + velocityX * deltaTime * MOVEMENT_FACTOR,
//            0,
//            info.Width);
//        star.Y = Clamp(
//            star.Y + velocityY * deltaTime * MOVEMENT_FACTOR,
//            0,
//            info.Height);
//    }

//    private void UpdateStarVisuals(ref Star star, float energy)
//    {
//        float lifetimeRatio = star.Lifetime / star.MaxLifetime;
//        star.Opacity = lifetimeRatio < 0.2f ? lifetimeRatio * 5f : 1f;

//        float twinkle = Sin(
//            GetAnimationTime() * TWINKLE_SPEED * star.TwinkleSpeed +
//            star.Phase) * 0.3f;
//        star.Brightness = Clamp(
//            0.8f + twinkle + energy * 0.5f,
//            MIN_BRIGHTNESS,
//            1.5f);
//    }

//    private void TrySpawnStars(SKImageInfo info, float energy)
//    {
//        if (_spawnAccumulator < 1f || energy < SPAWN_THRESHOLD) return;

//        int toSpawn = Min((int)_spawnAccumulator, 5);
//        _spawnAccumulator -= toSpawn;

//        for (int i = 0; i < toSpawn; i++)
//        {
//            for (int j = 0; j < _stars.Length; j++)
//            {
//                if (!_stars[j].Active)
//                {
//                    InitializeStar(ref _stars[j], info);
//                    break;
//                }
//            }
//        }
//    }

//    private void InitializeStar(ref Star star, SKImageInfo info)
//    {
//        star.Active = true;
//        star.X = _random.NextSingle() * info.Width;
//        star.Y = _random.NextSingle() * info.Height;
//        star.Lifetime = star.MaxLifetime = 3f + _random.NextSingle() * 9f;
//        star.Size = BASE_STAR_SIZE +
//            _random.NextSingle() * (MAX_STAR_SIZE - BASE_STAR_SIZE);
//        star.Speed = 0.5f + _random.NextSingle() * 2f;
//        star.TwinkleSpeed = 0.8f + _random.NextSingle() * 0.4f;
//        star.Phase = _random.NextSingle() * MathF.Tau;
//        star.Opacity = 0f;
//        star.Brightness = 0.8f;
//        star.Color = GetStarColor();
//    }

//    private SKColor GetStarColor()
//    {
//        var animated = GetAnimatedValues();
//        float lowSpectrum = animated.Length > 0 ? animated[0] : 0f;
//        float midSpectrum = animated.Length > 1 ? animated[1] : 0f;
//        float highSpectrum = animated.Length > 2 ? animated[2] : 0f;

//        if (lowSpectrum > MathF.Max(midSpectrum, highSpectrum))
//            return new SKColor(
//                (byte)(200 + _random.Next(55)),
//                (byte)(100 + _random.Next(100)),
//                (byte)(100 + _random.Next(100)));

//        if (highSpectrum > MathF.Max(lowSpectrum, midSpectrum))
//            return new SKColor(
//                (byte)(100 + _random.Next(100)),
//                (byte)(100 + _random.Next(100)),
//                (byte)(200 + _random.Next(55)));

//        return new SKColor(
//            (byte)(150 + _random.Next(105)),
//            (byte)(150 + _random.Next(105)),
//            (byte)(150 + _random.Next(105)));
//    }

//    private void DrawStars(SKCanvas canvas, SKPaint basePaint)
//    {
//        var animated = GetAnimatedValues();
//        float energy = animated.Length > 3 ? animated[3] : 0f;

//        var circles = new List<(SKPoint center, float radius)>();
//        var glowCircles = new List<(SKPoint center, float radius, byte alpha)>();

//        foreach (ref readonly var star in _stars.AsSpan())
//        {
//            if (!star.Active) continue;

//            byte alpha = CalculateAlpha(star.Brightness * star.Opacity);
//            if (alpha < 10) continue;

//            float size = star.Size * (1f + energy * 0.5f);
//            var center = new SKPoint(star.X, star.Y);

//            circles.Add((center, size));

//            if (UseAdvancedEffects &&
//                _currentSettings.UseGlow &&
//                alpha > 180)
//            {
//                glowCircles.Add((
//                    center,
//                    size * _currentSettings.GlowSize,
//                    (byte)(alpha / 5)));
//            }
//        }

//        var paint = CreateStandardPaint(basePaint.Color);
//        RenderCircles(canvas, circles, paint);
//        ReturnPaint(paint);

//        if (glowCircles.Count > 0)
//        {
//            var glowPaint = CreateStandardPaint(basePaint.Color);
//            foreach (var (center, radius, alpha) in glowCircles)
//            {
//                glowPaint.Color = basePaint.Color.WithAlpha(alpha);
//                canvas.DrawCircle(center.X, center.Y, radius, glowPaint);
//            }
//            ReturnPaint(glowPaint);
//        }
//    }

//    private void InitializeStars()
//    {
//        int count = IsOverlayActive ? OVERLAY_STAR_COUNT : DEFAULT_STAR_COUNT;
//        _stars = new Star[count];
//    }

//    private struct Star
//    {
//        public float X, Y, Size, Brightness, Speed, TwinkleSpeed,
//                     Lifetime, MaxLifetime, Opacity, Phase;
//        public SKColor Color;
//        public bool Active;
//    }

//    protected override void OnDispose()
//    {
//        _stars = [];
//        base.OnDispose();
//    }
//}