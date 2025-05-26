#nullable enable

using static SpectrumNet.SN.Visualization.Renderers.FireRenderer.Constants;
using static System.MathF;

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class FireRenderer : EffectSpectrumRenderer
{
    private const string LogPrefix = nameof(FireRenderer);

    private static readonly Lazy<FireRenderer> _instance =
        new(() => new FireRenderer());

    private FireRenderer() { }

    public static FireRenderer GetInstance() => _instance.Value;

    public static class Constants
    {
        public const float
            TIME_STEP = 0.016f,
            DECAY_RATE = 0.08f,
            FLAME_BOTTOM_MAX = 6.0f,
            WAVE_SPEED = 2.0f,
            WAVE_AMPLITUDE = 0.2f,
            HORIZONTAL_WAVE_FACTOR = 0.15f,
            CUBIC_CONTROL_POINT1 = 0.33f,
            CUBIC_CONTROL_POINT2 = 0.66f,
            OPACITY_WAVE_SPEED = 3.0f,
            OPACITY_PHASE_SHIFT = 0.2f,
            OPACITY_WAVE_AMPLITUDE = 0.1f,
            OPACITY_BASE = 0.9f,
            POSITION_PHASE_SHIFT = 0.5f,
            GLOW_INTENSITY = 0.3f,
            HIGH_INTENSITY_THRESHOLD = 0.7f,
            RANDOM_OFFSET_PROPORTION = 0.5f,
            RANDOM_OFFSET_CENTER = 0.25f;

        public const int FIRE_BATCH_SIZE = 64;

        public static readonly Dictionary<RenderQuality, QualitySettings> QualityPresets = new()
        {
            [RenderQuality.Low] = new(
                MaxDetailLevel: 2,
                GlowRadius: 1.5f,
                UseGlow: false
            ),
            [RenderQuality.Medium] = new(
                MaxDetailLevel: 4,
                GlowRadius: 3.0f,
                UseGlow: true
            ),
            [RenderQuality.High] = new(
                MaxDetailLevel: 8,
                GlowRadius: 5.0f,
                UseGlow: true
            )
        };

        public record QualitySettings(
            int MaxDetailLevel,
            float GlowRadius,
            bool UseGlow
        );
    }

    private readonly Random _random = new();
    private QualitySettings _currentSettings = QualityPresets[RenderQuality.Medium];
    private float[] _flameHeights = [];

    protected override void OnInitialize()
    {
        base.OnInitialize();
        LogDebug("Initialized");
    }

    protected override void OnQualitySettingsApplied()
    {
        _currentSettings = QualityPresets[Quality];
        LogDebug($"Quality changed to {Quality}");
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
        SafeExecute(
            () => RenderFire(
                canvas,
                spectrum,
                info,
                barWidth,
                barSpacing,
                paint),
            "Error during rendering"
        );
    }

    private void RenderFire(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        SKPaint paint)
    {
        UpdateFlameHeights(spectrum);

        float totalBarWidth = barWidth + barSpacing;

        for (int i = 0; i < spectrum.Length; i += FIRE_BATCH_SIZE)
        {
            int batchEnd = Min(i + FIRE_BATCH_SIZE, spectrum.Length);
            RenderBatch(
                canvas,
                spectrum,
                i,
                batchEnd,
                info,
                barWidth,
                totalBarWidth,
                paint);
        }
    }

    private void UpdateFlameHeights(float[] spectrum)
    {
        if (_flameHeights.Length != spectrum.Length)
        {
            _flameHeights = new float[spectrum.Length];
        }

        for (int i = 0; i < spectrum.Length; i++)
        {
            _flameHeights[i] = MathF.Max(
                spectrum[i],
                _flameHeights[i] - DECAY_RATE);
        }
    }

    private void RenderBatch(
        SKCanvas canvas,
        float[] spectrum,
        int start,
        int end,
        SKImageInfo info,
        float barWidth,
        float totalBarWidth,
        SKPaint basePaint)
    {
        for (int i = start; i < end; i++)
        {
            float spectrumValue = spectrum[i];
            if (spectrumValue < MIN_MAGNITUDE_THRESHOLD) continue;

            RenderSingleFlame(
                canvas,
                i,
                spectrumValue,
                info,
                barWidth,
                totalBarWidth,
                basePaint);
        }
    }

    private void RenderSingleFlame(
        SKCanvas canvas,
        int index,
        float spectrumValue,
        SKImageInfo info,
        float barWidth,
        float totalBarWidth,
        SKPaint basePaint)
    {
        float x = index * totalBarWidth;
        float waveOffset = Sin(
            GetAnimationTime() * WAVE_SPEED +
            index * POSITION_PHASE_SHIFT);

        float currentHeight = spectrumValue *
            info.Height *
            (1 + waveOffset * WAVE_AMPLITUDE);
        float previousHeight = _flameHeights[index] * info.Height;
        float flameHeight = MathF.Max(currentHeight, previousHeight);

        float flameTop = info.Height - flameHeight;
        float flameBottom = info.Height - FLAME_BOTTOM_MAX;

        if (flameBottom - flameTop < 1) return;

        x += waveOffset * barWidth * HORIZONTAL_WAVE_FACTOR;

        var flamePath = GetPath();
        try
        {
            CreateFlamePath(
                flamePath,
                x,
                flameTop,
                flameBottom,
                barWidth);

            if (UseAdvancedEffects &&
                _currentSettings.UseGlow &&
                flameHeight / info.Height > HIGH_INTENSITY_THRESHOLD)
            {
                RenderFlameGlow(
                    canvas,
                    flamePath,
                    basePaint,
                    flameHeight / info.Height);
            }

            RenderFlameBody(
                canvas,
                flamePath,
                basePaint,
                index,
                flameHeight / info.Height);
        }
        finally
        {
            ReturnPath(flamePath);
        }
    }

    private void CreateFlamePath(
        SKPath path,
        float x,
        float flameTop,
        float flameBottom,
        float barWidth)
    {
        path.MoveTo(x, flameBottom);

        float height = flameBottom - flameTop;
        float detailFactor = (float)_currentSettings.MaxDetailLevel / 8.0f;

        float cp1X = x +
            barWidth * CUBIC_CONTROL_POINT1 +
            GetRandomOffset(barWidth, detailFactor);
        float cp1Y = flameBottom - height * CUBIC_CONTROL_POINT1;

        float cp2X = x +
            barWidth * CUBIC_CONTROL_POINT2 +
            GetRandomOffset(barWidth, detailFactor);
        float cp2Y = flameBottom - height * CUBIC_CONTROL_POINT2;

        path.CubicTo(
            cp1X, cp1Y,
            cp2X, cp2Y,
            x + barWidth, flameBottom);
    }

    private float GetRandomOffset(float barWidth, float detailFactor)
    {
        float randomnessFactor = detailFactor * RANDOM_OFFSET_PROPORTION;
        return (float)(_random.NextDouble() *
            barWidth * randomnessFactor -
            barWidth * RANDOM_OFFSET_CENTER);
    }

    private void RenderFlameGlow(
        SKCanvas canvas,
        SKPath path,
        SKPaint basePaint,
        float intensity)
    {
        byte glowAlpha = (byte)(255 * intensity * GLOW_INTENSITY);

        var glowPaint = CreateEffectPaint(
            basePaint.Color.WithAlpha(glowAlpha),
            SKPaintStyle.Fill,
            createBlur: true,
            blurRadius: _currentSettings.GlowRadius
        );

        try
        {
            canvas.DrawPath(path, glowPaint);
        }
        finally
        {
            ReturnPaint(glowPaint);
        }
    }

    private void RenderFlameBody(
        SKCanvas canvas,
        SKPath path,
        SKPaint basePaint,
        int index,
        float intensity)
    {
        float opacityWave = Sin(
            GetAnimationTime() * OPACITY_WAVE_SPEED +
            index * OPACITY_PHASE_SHIFT) *
            OPACITY_WAVE_AMPLITUDE +
            OPACITY_BASE;

        byte alpha = (byte)(255 *
            MathF.Min(intensity * opacityWave, 1.0f));

        var flamePaint = CreateEffectPaint(
            basePaint.Color.WithAlpha(alpha),
            SKPaintStyle.Fill
        );

        try
        {
            canvas.DrawPath(path, flamePaint);
        }
        finally
        {
            ReturnPaint(flamePaint);
        }
    }

    private SKPaint CreateEffectPaint(
        SKColor color,
        SKPaintStyle style,
        float strokeWidth = 0,
        bool createBlur = false,
        float blurRadius = 0)
    {
        var paint = GetPaint();
        paint.Color = color;
        paint.Style = style;
        paint.IsAntialias = UseAntiAlias;

        if (createBlur && blurRadius > 0)
        {
            paint.ImageFilter = SKImageFilter.CreateBlur(
                blurRadius,
                blurRadius);
        }

        return paint;
    }

    protected override void OnDispose()
    {
        base.OnDispose();
        LogDebug("Disposed");
    }
}