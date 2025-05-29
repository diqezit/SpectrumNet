//#nullable enable

//using static SpectrumNet.SN.Visualization.Renderers.FireRenderer.Constants;
//using static System.MathF;

//namespace SpectrumNet.SN.Visualization.Renderers;

//public sealed class FireRenderer() : EffectSpectrumRenderer
//{
//    private const string LogPrefix = nameof(FireRenderer);

//    private static readonly Lazy<FireRenderer> _instance =
//        new(() => new FireRenderer());

//    public static FireRenderer GetInstance() => _instance.Value;

//    public static class Constants
//    {
//        public const float
//            DECAY_RATE = 0.08f,
//            FLAME_BOTTOM_MAX = 6.0f,
//            WAVE_SPEED = 2.0f,
//            WAVE_AMPLITUDE = 0.2f,
//            HORIZONTAL_WAVE_FACTOR = 0.15f,
//            CUBIC_CONTROL_POINT1 = 0.33f,
//            CUBIC_CONTROL_POINT2 = 0.66f,
//            OPACITY_WAVE_SPEED = 3.0f,
//            OPACITY_PHASE_SHIFT = 0.2f,
//            OPACITY_WAVE_AMPLITUDE = 0.1f,
//            OPACITY_BASE = 0.9f,
//            POSITION_PHASE_SHIFT = 0.5f,
//            GLOW_INTENSITY = 0.3f,
//            HIGH_INTENSITY_THRESHOLD = 0.7f,
//            RANDOM_OFFSET_PROPORTION = 0.5f,
//            RANDOM_OFFSET_CENTER = 0.25f;

//        public const int FIRE_BATCH_SIZE = 64;

//        public static readonly Dictionary<RenderQuality, QualitySettings> QualityPresets = new()
//        {
//            [RenderQuality.Low] = new(
//                MaxDetailLevel: 2,
//                GlowRadius: 1.5f,
//                UseGlow: false
//            ),
//            [RenderQuality.Medium] = new(
//                MaxDetailLevel: 4,
//                GlowRadius: 3.0f,
//                UseGlow: true
//            ),
//            [RenderQuality.High] = new(
//                MaxDetailLevel: 8,
//                GlowRadius: 5.0f,
//                UseGlow: true
//            )
//        };

//        public record QualitySettings(
//            int MaxDetailLevel,
//            float GlowRadius,
//            bool UseGlow
//        );
//    }

//    private readonly Random _random = new();
//    private QualitySettings _currentSettings = QualityPresets[RenderQuality.Medium];
//    private float[] _peaks = [];
//    private float[] _timers = [];
//    private readonly float _holdTime = 0f;
//    private readonly float _fallSpeed = DECAY_RATE;

//    protected override void OnInitialize()
//    {
//        base.OnInitialize();
//    }

//    protected override void OnQualitySettingsApplied() =>
//        _currentSettings = QualityPresets[Quality];

//    protected override void RenderEffect(
//        SKCanvas canvas,
//        float[] spectrum,
//        SKImageInfo info,
//        float barWidth,
//        float barSpacing,
//        int barCount,
//        SKPaint paint)
//    {
//        EnsurePeakArraySize(spectrum.Length);
//        for (int i = 0; i < spectrum.Length; i++)
//            UpdatePeak(i, spectrum[i], DeltaTime);
        
//        float totalBarWidth = barWidth + barSpacing;
//        var flames = new List<(SKPath path, float intensity, int index)>();

//        for (int i = 0; i < spectrum.Length; i++)
//        {
//            if (spectrum[i] < MIN_MAGNITUDE_THRESHOLD) continue;

//            var flamePath = CreateFlamePath(
//                i,
//                spectrum[i],
//                info,
//                barWidth,
//                totalBarWidth);

//            if (flamePath != null)
//                flames.Add((flamePath, spectrum[i], i));
//        }

//        RenderFlames(canvas, flames, paint);

//        foreach (var (path, _, _) in flames)
//            ReturnPath(path);
//    }

//    private SKPath? CreateFlamePath(
//        int index,
//        float spectrumValue,
//        SKImageInfo info,
//        float barWidth,
//        float totalBarWidth)
//    {
//        float x = index * totalBarWidth;
//        float waveOffset = Sin(
//            GetAnimationTime() * WAVE_SPEED +
//            index * POSITION_PHASE_SHIFT);

//        float currentHeight = spectrumValue *
//            info.Height *
//            (1 + waveOffset * WAVE_AMPLITUDE);
//        float previousHeight = GetPeakValue(index) * info.Height;
//        float flameHeight = MathF.Max(currentHeight, previousHeight);

//        float flameTop = info.Height - flameHeight;
//        float flameBottom = info.Height - FLAME_BOTTOM_MAX;

//        if (flameBottom - flameTop < 1) return null;

//        x += waveOffset * barWidth * HORIZONTAL_WAVE_FACTOR;

//        var path = GetPath();
//        CreateFlameShape(
//            path,
//            x,
//            flameTop,
//            flameBottom,
//            barWidth);

//        return path;
//    }

//    private void CreateFlameShape(
//        SKPath path,
//        float x,
//        float flameTop,
//        float flameBottom,
//        float barWidth)
//    {
//        path.MoveTo(x, flameBottom);

//        float height = flameBottom - flameTop;
//        float detailFactor = (float)_currentSettings.MaxDetailLevel / 8.0f;

//        float cp1X = x +
//            barWidth * CUBIC_CONTROL_POINT1 +
//            GetRandomOffset(barWidth, detailFactor);
//        float cp1Y = flameBottom - height * CUBIC_CONTROL_POINT1;

//        float cp2X = x +
//            barWidth * CUBIC_CONTROL_POINT2 +
//            GetRandomOffset(barWidth, detailFactor);
//        float cp2Y = flameBottom - height * CUBIC_CONTROL_POINT2;

//        path.CubicTo(
//            cp1X, cp1Y,
//            cp2X, cp2Y,
//            x + barWidth, flameBottom);
//    }

//    private float GetRandomOffset(float barWidth, float detailFactor)
//    {
//        float randomnessFactor = detailFactor * RANDOM_OFFSET_PROPORTION;
//        return (float)(_random.NextDouble() *
//            barWidth * randomnessFactor -
//            barWidth * RANDOM_OFFSET_CENTER);
//    }

//    private void RenderFlames(
//        SKCanvas canvas,
//        List<(SKPath path, float intensity, int index)> flames,
//        SKPaint basePaint)
//    {
//        if (UseAdvancedEffects && _currentSettings.UseGlow)
//        {
//            var glowPaint = CreateGlowPaint(
//                basePaint.Color,
//                _currentSettings.GlowRadius);

//            foreach (var (path, intensity, _) in flames)
//            {
//                if (intensity > HIGH_INTENSITY_THRESHOLD)
//                {
//                    byte glowAlpha = CalculateAlpha(intensity * GLOW_INTENSITY);
//                    glowPaint.Color = basePaint.Color.WithAlpha(glowAlpha);
//                    canvas.DrawPath(path, glowPaint);
//                }
//            }

//            ReturnPaint(glowPaint);
//        }

//        var flamePaint = CreateStandardPaint(basePaint.Color);

//        foreach (var (path, intensity, index) in flames)
//        {
//            float opacityWave = Sin(
//                GetAnimationTime() * OPACITY_WAVE_SPEED +
//                index * OPACITY_PHASE_SHIFT) *
//                OPACITY_WAVE_AMPLITUDE +
//                OPACITY_BASE;

//            byte alpha = CalculateAlpha(
//                MathF.Min(intensity * opacityWave, 1.0f));
//            flamePaint.Color = basePaint.Color.WithAlpha(alpha);
//            canvas.DrawPath(path, flamePaint);
//        }

//        ReturnPaint(flamePaint);
//    }

//    private void UpdatePeak(int index, float value, float deltaTime)
//    {
//        if (index < 0 || index >= _peaks.Length) return;

//        if (value > _peaks[index])
//        {
//            _peaks[index] = value;
//            _timers[index] = _holdTime;
//        }
//        else if (_timers[index] > 0)
//        {
//            _timers[index] -= deltaTime;
//        }
//        else
//        {
//            _peaks[index] = MathF.Max(0, _peaks[index] - _fallSpeed * deltaTime);
//        }
//    }

//    private float GetPeakValue(int index) =>
//        index >= 0 && index < _peaks.Length ? _peaks[index] : 0f;

//    private void EnsurePeakArraySize(int size)
//    {
//        if (_peaks.Length < size)
//        {
//            Array.Resize(ref _peaks, size);
//            Array.Resize(ref _timers, size);
//        }
//    }
//}