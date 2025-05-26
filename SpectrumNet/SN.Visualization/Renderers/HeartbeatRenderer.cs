#nullable enable

using static SpectrumNet.SN.Visualization.Renderers.HeartbeatRenderer.Constants;
using static System.MathF;

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class HeartbeatRenderer : EffectSpectrumRenderer
{
    private const string LogPrefix = nameof(HeartbeatRenderer);

    private static readonly Lazy<HeartbeatRenderer> _instance =
        new(() => new HeartbeatRenderer());

    private HeartbeatRenderer() { }

    public static HeartbeatRenderer GetInstance() => _instance.Value;

    public static class Constants
    {
        public const float
            MIN_MAGNITUDE_THRESHOLD = 0.05f,
            GLOW_INTENSITY = 0.2f,
            GLOW_ALPHA_DIVISOR = 3f,
            ALPHA_MULTIPLIER = 1.5f,
            PULSE_FREQUENCY = 6f,
            HEART_BASE_SCALE = 0.6f,
            HEART_SIZE_REDUCTION_FACTOR = 0.3f,
            HEART_SPACING_REDUCTION_FACTOR = 0.1f,
            HEART_SIZE_SPACING_FACTOR = 0.5f,
            HEART_SPACING_SPACING_FACTOR = 0.2f,
            MIN_HEART_SIZE = 10f,
            MIN_HEART_SPACING = 5f,
            CANVAS_SIZE_DIVISOR = 4f,
            HEART_RADIUS_FACTOR = 0.5f,
            HEART_PULSE_AMPLITUDE = 0.1f,
            HEART_Y_OFFSET_FACTOR = 0.2f,
            HEART_RADIUS_VARIATION = 0.3f,
            SIMPLIFICATION_RADIUS_FACTOR = 0.5f,
            CENTER_PROPORTION = 0.5f,
            RADIUS_PROPORTION = 1f / 3f;

        public const int
            MIN_HEART_COUNT = 4,
            MAX_HEART_COUNT = 32,
            HEART_COUNT_DIVISOR = 2;

        public static readonly (float Size, float Spacing, int Count)
            DEFAULT_CONFIG = (60f, 15f, 8),
            OVERLAY_CONFIG = (30f, 8f, 12);

        public static readonly Dictionary<RenderQuality, QualitySettings> QualityPresets = new()
        {
            [RenderQuality.Low] = new(
                HeartSides: 8,
                UseGlow: false,
                SimplificationFactor: 0.5f
            ),
            [RenderQuality.Medium] = new(
                HeartSides: 12,
                UseGlow: true,
                SimplificationFactor: 0.2f
            ),
            [RenderQuality.High] = new(
                HeartSides: 0,
                UseGlow: true,
                SimplificationFactor: 0f
            )
        };

        public record QualitySettings(
            int HeartSides,
            bool UseGlow,
            float SimplificationFactor
        );
    }

    private float _heartSize;
    private float _heartSpacing;
    private int _heartCount;
    private float[] _cosValues = [];
    private float[] _sinValues = [];
    private SKPicture? _cachedHeartPicture;
    private QualitySettings _currentSettings = QualityPresets[RenderQuality.Medium];

    protected override void OnInitialize()
    {
        base.OnInitialize();
        UpdateConfiguration(DEFAULT_CONFIG);
        PrecomputeTrigValues();
        LogDebug("Initialized");
    }

    protected override void OnConfigurationChanged()
    {
        base.OnConfigurationChanged();
        SetProcessingSmoothingFactor(IsOverlayActive ? 0.7f : 0.3f);
        UpdateConfiguration(IsOverlayActive ? OVERLAY_CONFIG : DEFAULT_CONFIG);
        InvalidateCachedResources();
    }

    protected override void OnQualitySettingsApplied()
    {
        _currentSettings = QualityPresets[Quality];
        InvalidateCachedResources();
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
        AdjustConfiguration(barCount, barSpacing, info.Width, info.Height);
        int actualHeartCount = Min(spectrum.Length, _heartCount);

        RenderFrame(canvas, spectrum, info, paint, actualHeartCount);
    }

    private void UpdateConfiguration(
        (float Size, float Spacing, int Count) config)
    {
        (_heartSize, _heartSpacing, _heartCount) = config;
        PrecomputeTrigValues();
        InvalidateCachedResources();
    }

    private void AdjustConfiguration(
        int barCount,
        float barSpacing,
        int canvasWidth,
        int canvasHeight)
    {
        _heartSize = MathF.Max(
            MIN_HEART_SIZE,
            DEFAULT_CONFIG.Size - barCount * HEART_SIZE_REDUCTION_FACTOR +
            barSpacing * HEART_SIZE_SPACING_FACTOR);

        _heartSpacing = MathF.Max(
            MIN_HEART_SPACING,
            DEFAULT_CONFIG.Spacing - barCount * HEART_SPACING_REDUCTION_FACTOR +
            barSpacing * HEART_SPACING_SPACING_FACTOR);

        _heartCount = Clamp(
            barCount / HEART_COUNT_DIVISOR,
            MIN_HEART_COUNT,
            MAX_HEART_COUNT);

        float maxSize = Min(canvasWidth, canvasHeight) / CANVAS_SIZE_DIVISOR;
        if (_heartSize > maxSize)
        {
            _heartSize = maxSize;
        }

        if (_cosValues.Length != _heartCount || _sinValues.Length != _heartCount)
        {
            PrecomputeTrigValues();
        }
    }

    private void PrecomputeTrigValues()
    {
        _cosValues = new float[_heartCount];
        _sinValues = new float[_heartCount];
        float angleStep = 2 * MathF.PI / _heartCount;

        for (int i = 0; i < _heartCount; i++)
        {
            float angle = i * angleStep;
            _cosValues[i] = Cos(angle);
            _sinValues[i] = Sin(angle);
        }
    }

    private void RenderFrame(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        SKPaint basePaint,
        int actualHeartCount)
    {
        float centerX = info.Width * CENTER_PROPORTION;
        float centerY = info.Height * CENTER_PROPORTION;
        float radius = Min(info.Width, info.Height) * RADIUS_PROPORTION;

        var heartPath = GetPath();
        try
        {
            EnsureCachedHeartPicture(heartPath, basePaint);

            using var heartPaint = CreateStandardPaint(basePaint.Color);
            using var glowPaint = UseAdvancedEffects && _currentSettings.UseGlow
                ? CreateStandardPaint(basePaint.Color)
                : null;

            DrawHearts(
                canvas,
                spectrum,
                centerX,
                centerY,
                radius,
                heartPath,
                heartPaint,
                glowPaint,
                basePaint,
                actualHeartCount);
        }
        finally
        {
            ReturnPath(heartPath);
        }
    }

    private void EnsureCachedHeartPicture(SKPath heartPath, SKPaint basePaint)
    {
        if (_cachedHeartPicture != null) return;

        var recorder = new SKPictureRecorder();
        var recordCanvas = recorder.BeginRecording(new SKRect(-1, -1, 1, 1));
        CreateHeartPath(heartPath, 0, 0, 1f);
        recordCanvas.DrawPath(heartPath, basePaint);
        _cachedHeartPicture = recorder.EndRecording();
        heartPath.Reset();
    }

    private static void CreateHeartPath(SKPath path, float x, float y, float size)
    {
        path.Reset();
        path.MoveTo(x, y + size / 2);
        path.CubicTo(
            x - size, y,
            x - size, y - size / 2,
            x, y - size);
        path.CubicTo(
            x + size, y - size / 2,
            x + size, y,
            x, y + size / 2);
        path.Close();
    }

    private void DrawHearts(
        SKCanvas canvas,
        float[] spectrum,
        float centerX,
        float centerY,
        float radius,
        SKPath heartPath,
        SKPaint heartPaint,
        SKPaint? glowPaint,
        SKPaint basePaint,
        int actualHeartCount)
    {
        float time = GetAnimationTime();

        for (int i = 0; i < actualHeartCount && i < spectrum.Length; i++)
        {
            float magnitude = spectrum[i];
            if (magnitude < MIN_MAGNITUDE_THRESHOLD) continue;

            DrawSingleHeart(
                canvas,
                magnitude,
                i,
                centerX,
                centerY,
                radius,
                heartPath,
                heartPaint,
                glowPaint,
                basePaint,
                time);
        }
    }

    private void DrawSingleHeart(
        SKCanvas canvas,
        float magnitude,
        int index,
        float centerX,
        float centerY,
        float radius,
        SKPath heartPath,
        SKPaint heartPaint,
        SKPaint? glowPaint,
        SKPaint basePaint,
        float time)
    {
        if (index >= _cosValues.Length || index >= _sinValues.Length) return;

        float x = centerX + _cosValues[index] * radius *
            (1 - magnitude * HEART_RADIUS_FACTOR);
        float y = centerY + _sinValues[index] * radius *
            (1 - magnitude * HEART_RADIUS_FACTOR);
        float heartSize = _heartSize * magnitude * HEART_BASE_SCALE *
            (Sin(time * PULSE_FREQUENCY) * HEART_PULSE_AMPLITUDE + 1f);

        SKRect heartBounds = new(
            x - heartSize,
            y - heartSize,
            x + heartSize,
            y + heartSize);

        if (!IsRenderAreaVisible(canvas, heartBounds.Left, heartBounds.Top,
            heartBounds.Width, heartBounds.Height)) return;

        byte alpha = (byte)MathF.Min(magnitude * ALPHA_MULTIPLIER * 255f, 255f);
        heartPaint.Color = basePaint.Color.WithAlpha(alpha);

        if (_currentSettings.HeartSides > 0)
        {
            DrawSimplifiedHeart(
                canvas,
                x,
                y,
                heartSize,
                heartPath,
                heartPaint,
                glowPaint,
                alpha);
        }
        else
        {
            DrawCachedHeart(
                canvas,
                x,
                y,
                heartSize,
                heartPaint,
                glowPaint,
                alpha);
        }
    }

    private void DrawSimplifiedHeart(
        SKCanvas canvas,
        float x,
        float y,
        float size,
        SKPath path,
        SKPaint heartPaint,
        SKPaint? glowPaint,
        byte alpha)
    {
        CreatePolygonHeartPath(path, x, y, size);

        if (glowPaint != null)
        {
            glowPaint.Color = heartPaint.Color.WithAlpha(
                (byte)(alpha / GLOW_ALPHA_DIVISOR));
            glowPaint.MaskFilter = SKMaskFilter.CreateBlur(
                SKBlurStyle.Normal,
                size * GLOW_INTENSITY * (1 - _currentSettings.SimplificationFactor));
            canvas.DrawPath(path, glowPaint);
        }

        canvas.DrawPath(path, heartPaint);
    }

    private void CreatePolygonHeartPath(
        SKPath path,
        float x,
        float y,
        float size)
    {
        path.Reset();
        float angleStep = 2 * MathF.PI / _currentSettings.HeartSides;
        path.MoveTo(x, y + size / 2);

        for (int i = 0; i < _currentSettings.HeartSides; i++)
        {
            float angle = i * angleStep;
            float radius = size * (1 + HEART_RADIUS_VARIATION * Sin(angle * 2)) *
                (1 - _currentSettings.SimplificationFactor * SIMPLIFICATION_RADIUS_FACTOR);
            float px = x + Cos(angle) * radius;
            float py = y + Sin(angle) * radius - size * HEART_Y_OFFSET_FACTOR;
            path.LineTo(px, py);
        }

        path.Close();
    }

    private void DrawCachedHeart(
        SKCanvas canvas,
        float x,
        float y,
        float size,
        SKPaint heartPaint,
        SKPaint? glowPaint,
        byte alpha)
    {
        if (_cachedHeartPicture == null) return;

        canvas.Save();
        try
        {
            canvas.Translate(x, y);
            canvas.Scale(size, size);

            if (glowPaint != null)
            {
                glowPaint.Color = heartPaint.Color.WithAlpha(
                    (byte)(alpha / GLOW_ALPHA_DIVISOR));
                glowPaint.MaskFilter = SKMaskFilter.CreateBlur(
                    SKBlurStyle.Normal,
                    size * GLOW_INTENSITY);
                canvas.DrawPicture(_cachedHeartPicture, glowPaint);
            }

            canvas.DrawPicture(_cachedHeartPicture, heartPaint);
        }
        finally
        {
            canvas.Restore();
        }
    }

    private void InvalidateCachedResources()
    {
        _cachedHeartPicture?.Dispose();
        _cachedHeartPicture = null;
        RequestRedraw();
    }

    protected override void CleanupUnusedResources()
    {
        if (_cachedHeartPicture != null && !RequiresRedraw())
        {
            _cachedHeartPicture.Dispose();
            _cachedHeartPicture = null;
        }
    }

    protected override void OnDispose()
    {
        _cachedHeartPicture?.Dispose();
        _cachedHeartPicture = null;
        _cosValues = _sinValues = [];
        base.OnDispose();
        LogDebug("Disposed");
    }
}