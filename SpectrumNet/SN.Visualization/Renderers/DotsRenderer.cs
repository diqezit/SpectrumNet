#nullable enable

using static SpectrumNet.SN.Visualization.Renderers.DotsRenderer.Constants;
using static System.MathF;

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class DotsRenderer : EffectSpectrumRenderer
{
    private const string LogPrefix = nameof(DotsRenderer);

    private static readonly Lazy<DotsRenderer> _instance =
        new(() => new DotsRenderer());

    private DotsRenderer() { }

    public static DotsRenderer GetInstance() => _instance.Value;

    public static class Constants
    {
        public const float
            BASE_DOT_RADIUS = 4.0f,
            MIN_DOT_RADIUS = 1.5f,
            MAX_DOT_RADIUS = 8.0f,
            DOT_RADIUS_SCALE_FACTOR = 0.8f,
            DOT_SPEED_BASE = 80.0f,
            DOT_SPEED_SCALE = 120.0f,
            DOT_VELOCITY_DAMPING = 0.95f,
            SPECTRUM_INFLUENCE_FACTOR = 2.0f,
            SPECTRUM_VELOCITY_FACTOR = 1.5f,
            ALPHA_BASE = 0.85f,
            GLOW_RADIUS_FACTOR = 0.3f,
            BASE_GLOW_ALPHA = 0.6f,
            BOUNDARY_DAMPING = 0.5f;

        public const int DOTS_BATCH_SIZE = 64;

        public static readonly Dictionary<RenderQuality, QualitySettings> QualityPresets = new()
        {
            [RenderQuality.Low] = new(
                DotCount: 75,
                UseGlow: false,
                GlowRadius: 0.2f,
                GlowAlpha: 0.4f,
                DotSpeedFactor: 0.7f
            ),
            [RenderQuality.Medium] = new(
                DotCount: 150,
                UseGlow: true,
                GlowRadius: GLOW_RADIUS_FACTOR,
                GlowAlpha: BASE_GLOW_ALPHA,
                DotSpeedFactor: 1.0f
            ),
            [RenderQuality.High] = new(
                DotCount: 300,
                UseGlow: true,
                GlowRadius: 0.5f,
                GlowAlpha: 0.8f,
                DotSpeedFactor: 1.3f
            )
        };

        public record QualitySettings(
            int DotCount,
            bool UseGlow,
            float GlowRadius,
            float GlowAlpha,
            float DotSpeedFactor
        );
    }

    private readonly record struct Dot(
        float X, float Y,
        float VelocityX, float VelocityY,
        float Radius,
        float BaseRadius,
        SKColor Color);

    private static readonly Random _random = new();
    private QualitySettings _currentSettings = QualityPresets[RenderQuality.Medium];
    private Dot[] _dots = [];
    private SKImageInfo _lastImageInfo;
    private float _maxSpectrum;
    private float _globalRadiusMultiplier = 1.0f;

    protected override void OnInitialize()
    {
        base.OnInitialize();
        ResetDots(new SKImageInfo(800, 600));
        _logger.Log(LogLevel.Debug, LogPrefix, "Initialized");
    }

    protected override void OnQualitySettingsApplied()
    {
        _currentSettings = QualityPresets[Quality];
        if (_lastImageInfo.Width > 0 && _lastImageInfo.Height > 0)
        {
            ResetDots(_lastImageInfo);
        }
        _logger.Log(LogLevel.Debug, LogPrefix, $"Quality changed to {Quality}");
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
            () => RenderDots(canvas, spectrum, info, barCount),
            LogPrefix,
            "Error during rendering"
        );
    }

    private void RenderDots(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        int barCount)
    {
        if (_lastImageInfo.Width != info.Width ||
            _lastImageInfo.Height != info.Height)
        {
            ResetDots(info);
        }

        UpdateDots(spectrum, info);
        DrawDots(canvas);
    }

    private void ResetDots(SKImageInfo info)
    {
        _lastImageInfo = info;
        _dots = new Dot[_currentSettings.DotCount];

        for (int i = 0; i < _currentSettings.DotCount; i++)
        {
            _dots[i] = CreateRandomDot(info);
        }
    }

    private Dot CreateRandomDot(SKImageInfo info)
    {
        float x = _random.NextSingle() * info.Width;
        float y = _random.NextSingle() * info.Height;
        float baseRadius = MIN_DOT_RADIUS +
            _random.NextSingle() * (MAX_DOT_RADIUS - MIN_DOT_RADIUS);
        float speedFactor = _currentSettings.DotSpeedFactor;

        return new Dot(
            x, y,
            (_random.NextSingle() - 0.5f) * DOT_SPEED_BASE * speedFactor,
            (_random.NextSingle() - 0.5f) * DOT_SPEED_BASE * speedFactor,
            baseRadius,
            baseRadius,
            GenerateRandomColor()
        );
    }

    private static SKColor GenerateRandomColor()
    {
        byte r = (byte)_random.Next(180, 255);
        byte g = (byte)_random.Next(100, 180);
        byte b = (byte)_random.Next(50, 100);
        return new SKColor(r, g, b);
    }

    private void UpdateDots(float[] spectrum, SKImageInfo info)
    {
        _maxSpectrum = spectrum.Length > 0 ? spectrum.Max() : 0f;
        _globalRadiusMultiplier = Clamp(
            1.0f + _maxSpectrum * DOT_RADIUS_SCALE_FACTOR,
            0.5f,
            2.0f);

        float deltaTime = _animationTimer.DeltaTime;

        for (int i = 0; i < _dots.Length; i++)
        {
            int spectrumIndex = Min(
                i * spectrum.Length / _dots.Length,
                spectrum.Length - 1);
            float spectrumValue = spectrum[spectrumIndex];
            _dots[i] = UpdateDot(_dots[i], spectrumValue, deltaTime, info);
        }
    }

    private Dot UpdateDot(
        Dot dot,
        float spectrumValue,
        float deltaTime,
        SKImageInfo info)
    {
        float normalizedX = dot.X / info.Width;
        float normalizedY = dot.Y / info.Height;

        float gravityX = (0.5f - normalizedX) *
            DOT_SPEED_BASE * _currentSettings.DotSpeedFactor;
        float gravityY = (0.5f - normalizedY) *
            DOT_SPEED_BASE * _currentSettings.DotSpeedFactor;

        float spectrumForceX = (normalizedX - 0.5f) *
            spectrumValue * SPECTRUM_INFLUENCE_FACTOR * DOT_SPEED_SCALE;
        float spectrumForceY = (normalizedY - 0.5f) *
            spectrumValue * SPECTRUM_INFLUENCE_FACTOR * DOT_SPEED_SCALE;

        float newVelocityX = (dot.VelocityX +
            (gravityX + spectrumForceX) * deltaTime) * DOT_VELOCITY_DAMPING;
        float newVelocityY = (dot.VelocityY +
            (gravityY + spectrumForceY) * deltaTime) * DOT_VELOCITY_DAMPING;

        float newX = dot.X + newVelocityX * deltaTime;
        float newY = dot.Y + newVelocityY * deltaTime;

        if (newX < 0)
        {
            newX = 0;
            newVelocityX = -newVelocityX * BOUNDARY_DAMPING;
        }
        else if (newX > info.Width)
        {
            newX = info.Width;
            newVelocityX = -newVelocityX * BOUNDARY_DAMPING;
        }

        if (newY < 0)
        {
            newY = 0;
            newVelocityY = -newVelocityY * BOUNDARY_DAMPING;
        }
        else if (newY > info.Height)
        {
            newY = info.Height;
            newVelocityY = -newVelocityY * BOUNDARY_DAMPING;
        }

        float newRadius = dot.BaseRadius *
            (1.0f + spectrumValue * SPECTRUM_VELOCITY_FACTOR) *
            _globalRadiusMultiplier;

        return dot with
        {
            X = newX,
            Y = newY,
            VelocityX = newVelocityX,
            VelocityY = newVelocityY,
            Radius = newRadius
        };
    }

    private void DrawDots(SKCanvas canvas)
    {
        float alphaFactor = MathF.Max(0.3f, MathF.Min(1.0f, _maxSpectrum + 0.3f));
        byte alpha = (byte)(ALPHA_BASE * 255f * alphaFactor);

        var sortedDots = _dots.OrderBy(d => d.Radius).ToArray();

        for (int i = 0; i < sortedDots.Length; i += DOTS_BATCH_SIZE)
        {
            int batchEnd = Min(i + DOTS_BATCH_SIZE, sortedDots.Length);
            DrawBatch(canvas, sortedDots, i, batchEnd, alpha);
        }
    }

    private void DrawBatch(
        SKCanvas canvas,
        Dot[] dots,
        int start,
        int end,
        byte alpha)
    {
        for (int i = start; i < end; i++)
        {
            var dot = dots[i];
            if (dot.Radius < 0.5f) continue;

            if (UseAdvancedEffects && _currentSettings.UseGlow)
            {
                using var glowPaint = CreateEffectPaint(
                    dot.Color.WithAlpha((byte)(_currentSettings.GlowAlpha * alpha)),
                    SKPaintStyle.Fill,
                    createBlur: true,
                    blurRadius: BASE_DOT_RADIUS * _currentSettings.GlowRadius
                );
                float glowRadius = dot.Radius * (1.0f + _currentSettings.GlowRadius);
                canvas.DrawCircle(dot.X, dot.Y, glowRadius, glowPaint);
            }

            using var dotPaint = CreateEffectPaint(
                dot.Color.WithAlpha(alpha),
                SKPaintStyle.Fill
            );
            canvas.DrawCircle(dot.X, dot.Y, dot.Radius, dotPaint);
        }
    }

    private SKPaint CreateEffectPaint(
        SKColor color,
        SKPaintStyle style,
        float strokeWidth = 0,
        bool createBlur = false,
        float blurRadius = 0)
    {
        var paint = _resourceManager.GetPaint();
        paint.Color = color;
        paint.Style = style;
        paint.IsAntialias = UseAntiAlias;

        if (createBlur && blurRadius > 0)
        {
            paint.ImageFilter = SKImageFilter.CreateBlur(blurRadius, blurRadius);
        }

        return paint;
    }

    protected override void OnDispose()
    {
        base.OnDispose();
        _logger.Log(LogLevel.Debug, LogPrefix, "Disposed");
    }

    public override bool RequiresRedraw() => true;
}