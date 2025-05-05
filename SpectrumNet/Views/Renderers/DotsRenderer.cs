#nullable enable

using static SpectrumNet.Views.Renderers.DotsRenderer.Constants;
using static System.MathF;

namespace SpectrumNet.Views.Renderers;

public sealed class DotsRenderer : EffectSpectrumRenderer
{
    private static readonly Lazy<DotsRenderer> _instance = new(() => new DotsRenderer());

    private readonly record struct Dot(
        float X, float Y,
        float VelocityX, float VelocityY,
        float Radius,
        float BaseRadius,
        SKColor Color);

    private readonly record struct RenderData(
        Dot[] Dots,
        float MaxSpectrum,
        int BarCount)
    {
        public readonly float AlphaFactor => MathF.Max(0.3f, MathF.Min(1.0f, MaxSpectrum + 0.3f));
    }

    private static readonly Random _random = new();
    private static readonly Vector2 _gravityCenter = new(0.5f, 0.5f);

    private Dot[] _dots = [];
    private SKImageInfo _lastImageInfo;
    private float _maxSpectrum;
    private int _dotCount = DEFAULT_DOT_COUNT;
    private float _globalRadiusMultiplier = 1.0f;

    private readonly object _renderDataLock = new();
    private bool _dataReady;
    private RenderData? _currentRenderData;

    private readonly SKPaint _dotPaint = new();
    private readonly SKPaint _glowPaint = new();

    private DotsRenderer() { }

    public static DotsRenderer GetInstance() => _instance.Value;

    public record Constants
    {
        public const string LOG_PREFIX = "DotsRenderer";

        public const float
            BASE_DOT_RADIUS = 4.0f,
            MIN_DOT_RADIUS = 1.5f,
            MAX_DOT_RADIUS = 8.0f,
            DOT_RADIUS_SCALE_FACTOR = 0.8f;

        public const float
            DOT_SPEED_BASE = 80.0f,
            DOT_SPEED_SCALE = 120.0f,
            DOT_VELOCITY_DAMPING = 0.95f;

        public const float
            SPECTRUM_INFLUENCE_FACTOR = 2.0f,
            SPECTRUM_VELOCITY_FACTOR = 1.5f;

        public const float
            ALPHA_BASE = 0.85f,
            ALPHA_RANGE = 0.15f;

        public const int
            LOW_QUALITY_DOT_COUNT = 75,
            MEDIUM_QUALITY_DOT_COUNT = 150,
            HIGH_QUALITY_DOT_COUNT = 300,
            DEFAULT_DOT_COUNT = MEDIUM_QUALITY_DOT_COUNT;

        public const float
            GLOW_RADIUS_FACTOR = 0.3f,
            BASE_GLOW_ALPHA = 0.6f;

        public const int MIN_BAR_COUNT = 32;
    }

    protected override void OnInitialize()
    {
        ExecuteSafely(
            () =>
            {
                base.OnInitialize();
                InitializeResources();
                Log(LogLevel.Debug, LOG_PREFIX, "Initialized");
            },
            "OnInitialize",
            "Failed during renderer initialization"
        );
    }

    private void InitializeResources()
    {
        _dotPaint.IsAntialias = true;
        _dotPaint.Style = SKPaintStyle.Fill;

        _glowPaint.IsAntialias = true;
        _glowPaint.Style = SKPaintStyle.Fill;
        _glowPaint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, BASE_DOT_RADIUS * GLOW_RADIUS_FACTOR);

        ResetDots(new SKImageInfo(800, 600));
    }

    private void ResetDots(SKImageInfo info)
    {
        _lastImageInfo = info;
        _dots = new Dot[_dotCount];

        for (int i = 0; i < _dotCount; i++)
        {
            float x = _random.NextSingle() * info.Width;
            float y = _random.NextSingle() * info.Height;
            float baseRadius = MinMaxRadius(_random.NextSingle());
            SKColor color = GenerateRandomColor();

            _dots[i] = new Dot(
                x, y,
                (_random.NextSingle() - 0.5f) * DOT_SPEED_BASE,
                (_random.NextSingle() - 0.5f) * DOT_SPEED_BASE,
                baseRadius,
                baseRadius,
                color
            );
        }
    }

    private static float MinMaxRadius(float factor) =>
        MIN_DOT_RADIUS + (MAX_DOT_RADIUS - MIN_DOT_RADIUS) * factor;

    private static SKColor GenerateRandomColor()
    {
        byte r = (byte)_random.Next(180, 255);
        byte g = (byte)_random.Next(100, 180);
        byte b = (byte)_random.Next(50, 100);
        return new SKColor(r, g, b);
    }

    public override void Configure(
        bool isOverlayActive,
        RenderQuality quality = RenderQuality.Medium)
    {
        ExecuteSafely(
            () =>
            {
                bool configChanged = _isOverlayActive != isOverlayActive || Quality != quality;
                base.Configure(isOverlayActive, quality);
                if (configChanged)
                {
                    Log(LogLevel.Debug, LOG_PREFIX, $"Configuration changed. New Quality: {Quality}");
                    OnConfigurationChanged();
                }
            },
            "Configure",
            "Failed to configure renderer"
        );
    }

    protected override void OnQualitySettingsApplied()
    {
        ExecuteSafely(
            () =>
            {
                base.OnQualitySettingsApplied();
                ApplyQualitySpecificSettings();
                Log(LogLevel.Debug, LOG_PREFIX, $"Quality settings applied. New Quality: {Quality}");
            },
            "OnQualitySettingsApplied",
            "Failed to apply specific quality settings"
        );
    }

    private void ApplyQualitySpecificSettings()
    {
        int oldDotCount = _dotCount;
        _dotCount = Quality switch
        {
            RenderQuality.Low => LOW_QUALITY_DOT_COUNT,
            RenderQuality.Medium => MEDIUM_QUALITY_DOT_COUNT,
            RenderQuality.High => HIGH_QUALITY_DOT_COUNT,
            _ => MEDIUM_QUALITY_DOT_COUNT
        };

        _glowPaint.MaskFilter = UseAdvancedEffects
            ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, BASE_DOT_RADIUS * GLOW_RADIUS_FACTOR)
            : null;

        if (oldDotCount != _dotCount)
        {
            lock (_renderDataLock)
            {
                _dataReady = false;
                _currentRenderData = null;
            }

            if (_lastImageInfo.Width > 0 && _lastImageInfo.Height > 0)
            {
                ResetDots(_lastImageInfo);
            }
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
        if (!ValidateRenderParameters(canvas, spectrum, info, paint)) return;

        ExecuteSafely(
            () =>
            {
                UpdateState(spectrum, barCount, info);
                RenderFrame(canvas, info);
            },
            "RenderEffect",
            "Error during rendering"
        );
    }

    private bool ValidateRenderParameters(
        SKCanvas? canvas,
        float[]? spectrum,
        SKImageInfo info,
        SKPaint? paint)
    {
        if (!IsCanvasValid(canvas)) return false;
        if (!IsSpectrumValid(spectrum)) return false;
        if (!IsPaintValid(paint)) return false;
        if (!AreDimensionsValid(info)) return false;
        if (IsDisposed()) return false;
        return true;
    }

    private static bool IsCanvasValid(SKCanvas? canvas)
    {
        if (canvas != null) return true;
        Log(LogLevel.Error, LOG_PREFIX, "Canvas is null");
        return false;
    }

    private static bool IsSpectrumValid(float[]? spectrum)
    {
        if (spectrum != null && spectrum.Length > 0) return true;
        Log(LogLevel.Error, LOG_PREFIX, "Spectrum is null or empty");
        return false;
    }

    private static bool IsPaintValid(SKPaint? paint)
    {
        if (paint != null) return true;
        Log(LogLevel.Error, LOG_PREFIX, "Paint is null");
        return false;
    }

    private static bool AreDimensionsValid(SKImageInfo info)
    {
        if (info.Width > 0 && info.Height > 0) return true;
        Log(LogLevel.Error, LOG_PREFIX, $"Invalid image dimensions: {info.Width}x{info.Height}");
        return false;
    }

    private bool IsDisposed()
    {
        if (!_disposed) return false;
        Log(LogLevel.Error, LOG_PREFIX, "Renderer is disposed");
        return true;
    }

    private void UpdateState(
        float[] spectrum,
        int barCount,
        SKImageInfo info)
    {
        if (_lastImageInfo.Width != info.Width || _lastImageInfo.Height != info.Height)
        {
            ResetDots(info);
        }

        float[] processedSpectrum = ProcessSpectrumForDots(spectrum, barCount);
        _maxSpectrum = processedSpectrum.Length > 0 ? processedSpectrum.Max() : 0f;

        UpdateGlobalRadiusMultiplier();
        UpdateDots(processedSpectrum, info);
        PrepareRenderData(barCount);
    }

    private void UpdateGlobalRadiusMultiplier() =>
        _globalRadiusMultiplier = Clamp(1.0f + _maxSpectrum * DOT_RADIUS_SCALE_FACTOR, 0.5f, 2.0f);

    private static float[] ProcessSpectrumForDots(float[] spectrum, int barCount)
    {
        int targetCount = Max(MIN_BAR_COUNT, Min(spectrum.Length, barCount));
        float[] processedSpectrum = new float[targetCount];

        if (spectrum.Length == 0 || targetCount <= 0)
            return processedSpectrum;

        float blockSize = (float)spectrum.Length / targetCount;

        for (int i = 0; i < targetCount; i++)
        {
            processedSpectrum[i] = CalculateAverageSpectrumBlock(spectrum, i, blockSize);
        }

        return processedSpectrum;
    }

    private static float CalculateAverageSpectrumBlock(float[] spectrum, int blockIndex, float blockSize)
    {
        float sum = 0;
        int start = (int)(blockIndex * blockSize);
        int end = (int)((blockIndex + 1) * blockSize);
        int actualEnd = Min(end, spectrum.Length);
        int count = actualEnd - start;

        if (count <= 0)
            return 0;

        for (int j = start; j < actualEnd; j++)
        {
            sum += spectrum[j];
        }

        return sum / count;
    }

    private void UpdateDots(float[] processedSpectrum, SKImageInfo info)
    {
        Dot[] updatedDots = new Dot[_dots.Length];
        float deltaTime = (float)(DateTime.Now - _lastUpdateTime).TotalSeconds;
        deltaTime = Clamp(deltaTime, 0.001f, 0.1f);

        for (int i = 0; i < _dots.Length; i++)
        {
            var dot = _dots[i];
            int spectrumIndex = GetSpectrumIndexForDot(i, processedSpectrum.Length);
            float spectrumValue = processedSpectrum[spectrumIndex];

            updatedDots[i] = UpdateSingleDot(dot, spectrumValue, deltaTime, info);
        }

        _dots = updatedDots;
    }

    private int GetSpectrumIndexForDot(int dotIndex, int spectrumLength) =>
        Clamp((int)((float)dotIndex / _dotCount * spectrumLength), 0, spectrumLength - 1);

    private Dot UpdateSingleDot(Dot dot, float spectrumValue, float deltaTime, SKImageInfo info)
    {
        float normalizedX = dot.X / info.Width;
        float normalizedY = dot.Y / info.Height;

        (float forceX, float forceY) = CalculateForces(normalizedX, normalizedY, spectrumValue);

        float newVelocityX = (dot.VelocityX + forceX * deltaTime) * DOT_VELOCITY_DAMPING;
        float newVelocityY = (dot.VelocityY + forceY * deltaTime) * DOT_VELOCITY_DAMPING;

        float newX = dot.X + newVelocityX * deltaTime;
        float newY = dot.Y + newVelocityY * deltaTime;

        (newX, newVelocityX) = HandleBoundaryCollision(newX, newVelocityX, 0, info.Width);
        (newY, newVelocityY) = HandleBoundaryCollision(newY, newVelocityY, 0, info.Height);

        float newRadius = dot.BaseRadius * (1.0f + spectrumValue * SPECTRUM_VELOCITY_FACTOR) * _globalRadiusMultiplier;

        return dot with
        {
            X = newX,
            Y = newY,
            VelocityX = newVelocityX,
            VelocityY = newVelocityY,
            Radius = newRadius
        };
    }

    private static (float forceX, float forceY) CalculateForces(float normalizedX, float normalizedY, float spectrumValue)
    {
        float gravityX = (_gravityCenter.X - normalizedX) * DOT_SPEED_BASE;
        float gravityY = (_gravityCenter.Y - normalizedY) * DOT_SPEED_BASE;

        float spectrumForceX = (normalizedX - 0.5f) * spectrumValue * SPECTRUM_INFLUENCE_FACTOR * DOT_SPEED_SCALE;
        float spectrumForceY = (normalizedY - 0.5f) * spectrumValue * SPECTRUM_INFLUENCE_FACTOR * DOT_SPEED_SCALE;

        return (gravityX + spectrumForceX, gravityY + spectrumForceY);
    }

    private static (float position, float velocity) HandleBoundaryCollision(
        float position, float velocity, float minBound, float maxBound)
    {
        if (position < minBound)
        {
            return (minBound, -velocity * 0.5f);
        }
        else if (position > maxBound)
        {
            return (maxBound, -velocity * 0.5f);
        }
        return (position, velocity);
    }

    private void PrepareRenderData(int barCount)
    {
        lock (_renderDataLock)
        {
            _currentRenderData = new RenderData(
                _dots,
                _maxSpectrum,
                barCount);
            _dataReady = true;
        }
    }

    private void RenderFrame(
        SKCanvas canvas,
        SKImageInfo _)
    {
        ExecuteSafely(
            () =>
            {
                RenderData renderData;
                lock (_renderDataLock)
                {
                    if (!_dataReady || _currentRenderData == null)
                        return;
                    renderData = _currentRenderData.Value;
                }

                Dot[] sortedDots = [.. renderData.Dots.OrderBy(d => d.Radius)];
                DrawDots(canvas, sortedDots, renderData.AlphaFactor);
            },
            "RenderFrame",
            "Error rendering dots frame"
        );
    }

    private void DrawDots(
        SKCanvas canvas,
        Dot[] dots,
        float alphaFactor)
    {
        foreach (var dot in dots)
        {
            if (dot.Radius < 0.5f)
                continue;

            byte alpha = (byte)(ALPHA_BASE * 255 * alphaFactor);
            DrawDotWithGlow(canvas, dot, alpha);
        }
    }

    private void DrawDotWithGlow(SKCanvas canvas, Dot dot, byte alpha)
    {
        if (UseAdvancedEffects)
        {
            _glowPaint.Color = dot.Color.WithAlpha((byte)(BASE_GLOW_ALPHA * alpha));
            float glowRadius = dot.Radius * (1.0f + GLOW_RADIUS_FACTOR);
            canvas.DrawCircle(dot.X, dot.Y, glowRadius, _glowPaint);
        }

        _dotPaint.Color = dot.Color.WithAlpha(alpha);
        canvas.DrawCircle(dot.X, dot.Y, dot.Radius, _dotPaint);
    }

    protected override void OnInvalidateCachedResources()
    {
        ExecuteSafely(
            () =>
            {
                _dataReady = false;
                _currentRenderData = null;
            },
            "OnInvalidateCachedResources",
            "Failed to invalidate cached resources"
        );
    }

    public override void Dispose()
    {
        if (_disposed) return;
        ExecuteSafely(
            () =>
            {
                OnDispose();
            },
            "Dispose",
            "Error during disposal"
        );
        _disposed = true;
        GC.SuppressFinalize(this);
        Log(LogLevel.Debug, LOG_PREFIX, "Disposed");
    }

    protected override void OnDispose()
    {
        ExecuteSafely(
            () =>
            {
                DisposeManagedResources();
                base.OnDispose();
            },
            "OnDispose",
            "Error during specific disposal"
        );
    }

    private void DisposeManagedResources()
    {
        _dotPaint?.Dispose();
        _glowPaint?.Dispose();
        _dots = [];
    }
}