#nullable enable

using static SpectrumNet.SN.Visualization.Renderers.DotsRenderer.Constants;
using static SpectrumNet.SN.Visualization.Renderers.DotsRenderer.Constants.Quality;
using static System.MathF;

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class DotsRenderer : EffectSpectrumRenderer
{
    private static readonly Lazy<DotsRenderer> _instance = new(() => new DotsRenderer());
    private const string LogPrefix = nameof(DotsRenderer);

    private DotsRenderer()
    {
        _dotPaint = new SKPaint();
        _glowPaint = new SKPaint();
    }

    public static DotsRenderer GetInstance() => _instance.Value;

    public record Constants
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
            ALPHA_RANGE = 0.15f,
            GLOW_RADIUS_FACTOR = 0.3f,
            BASE_GLOW_ALPHA = 0.6f;

        public const int
            LOW_QUALITY_DOT_COUNT = 75,
            MEDIUM_QUALITY_DOT_COUNT = 150,
            HIGH_QUALITY_DOT_COUNT = 300,
            DEFAULT_DOT_COUNT = MEDIUM_QUALITY_DOT_COUNT,
            MIN_BAR_COUNT = 32;

        public const byte MAX_ALPHA_BYTE = 255;

        public static class Quality
        {
            public const int
                LOW_DOT_COUNT = LOW_QUALITY_DOT_COUNT,
                MEDIUM_DOT_COUNT = MEDIUM_QUALITY_DOT_COUNT,
                HIGH_DOT_COUNT = HIGH_QUALITY_DOT_COUNT;

            public const bool
                LOW_USE_ADVANCED_EFFECTS = false,
                MEDIUM_USE_ADVANCED_EFFECTS = true,
                HIGH_USE_ADVANCED_EFFECTS = true;

            public const bool
                LOW_USE_ANTIALIASING = false,
                MEDIUM_USE_ANTIALIASING = true,
                HIGH_USE_ANTIALIASING = true;

            public const bool
                LOW_USE_GLOW_EFFECTS = false,
                MEDIUM_USE_GLOW_EFFECTS = true,
                HIGH_USE_GLOW_EFFECTS = true;

            public const float
                LOW_GLOW_RADIUS_FACTOR = 0.2f,
                MEDIUM_GLOW_RADIUS_FACTOR = 0.3f,
                HIGH_GLOW_RADIUS_FACTOR = 0.5f;

            public const float
                LOW_GLOW_ALPHA = 0.4f,
                MEDIUM_GLOW_ALPHA = 0.6f,
                HIGH_GLOW_ALPHA = 0.8f;

            public const float
                LOW_DOT_SPEED_FACTOR = 0.7f,
                MEDIUM_DOT_SPEED_FACTOR = 1.0f,
                HIGH_DOT_SPEED_FACTOR = 1.3f;
        }
    }

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
        public readonly float AlphaFactor =>
            MathF.Max(0.3f, MathF.Min(1.0f, MaxSpectrum + 0.3f));
    }

    private static readonly Random _random = new();
    private static readonly Vector2 _gravityCenter = new(0.5f, 0.5f);

    private Dot[] _dots = [];
    private SKImageInfo _lastImageInfo;
    private float _maxSpectrum;
    private int _dotCount = DEFAULT_DOT_COUNT;
    private float _globalRadiusMultiplier = 1.0f;
    private bool _useGlowEffects;
    private float _glowRadiusFactor = GLOW_RADIUS_FACTOR;
    private float _glowAlpha = BASE_GLOW_ALPHA;
    private float _dotSpeedFactor = 1.0f;

    private readonly object _renderDataLock = new();
    private bool _dataReady;
    private RenderData? _currentRenderData;

    private readonly SKPaint _dotPaint;
    private readonly SKPaint _glowPaint;

    protected override void OnInitialize()
    {
        base.OnInitialize();
        InitializeResources();
        _logger.Debug(LogPrefix, "Initialized");
    }

    private void InitializeResources() =>
        _logger.Safe(() => HandleInitializeResources(), LogPrefix, "Failed to initialize renderer resources");

    private void HandleInitializeResources()
    {
        _dotPaint.IsAntialias = _useAntiAlias;
        _dotPaint.Style = SKPaintStyle.Fill;

        _glowPaint.IsAntialias = _useAntiAlias;
        _glowPaint.Style = SKPaintStyle.Fill;

        UpdateGlowMaskFilter();

        ResetDots(new SKImageInfo(800, 600));
        _logger.Debug(LogPrefix, "Resources initialized");
    }

    private void UpdateGlowMaskFilter()
    {
        _glowPaint.MaskFilter = _useGlowEffects && _useAdvancedEffects
            ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, BASE_DOT_RADIUS * _glowRadiusFactor)
            : null;
    }

    private void ResetDots(SKImageInfo info) =>
        _logger.Safe(() => HandleResetDots(info), LogPrefix, "Failed to reset dots");

    private void HandleResetDots(SKImageInfo info)
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
                (_random.NextSingle() - 0.5f) * DOT_SPEED_BASE * _dotSpeedFactor,
                (_random.NextSingle() - 0.5f) * DOT_SPEED_BASE * _dotSpeedFactor,
                baseRadius,
                baseRadius,
                color
            );
        }

        _logger.Debug(LogPrefix, $"Dots reset - count: {_dots.Length}");
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

    protected override void OnConfigurationChanged()
    {
        _logger.Debug(LogPrefix, $"Configuration changed. New Quality: {Quality}");
    }

    protected override void OnQualitySettingsApplied()
    {
        switch (Quality)
        {
            case RenderQuality.Low:
                LowQualitySettings();
                break;
            case RenderQuality.Medium:
                MediumQualitySettings();
                break;
            case RenderQuality.High:
                HighQualitySettings();
                break;
        }

        UpdateQualityDependentResources();

        _logger.Debug(LogPrefix,
            $"Quality settings applied. Quality: {Quality}, " +
            $"DotCount: {_dotCount}, AntiAlias: {_useAntiAlias}, " +
            $"AdvancedEffects: {_useAdvancedEffects}, GlowEffects: {_useGlowEffects}, " +
            $"GlowRadius: {_glowRadiusFactor}, SpeedFactor: {_dotSpeedFactor}");
    }

    private void LowQualitySettings()
    {
        _dotCount = LOW_DOT_COUNT;
        _useAdvancedEffects = LOW_USE_ADVANCED_EFFECTS;
        _useAntiAlias = LOW_USE_ANTIALIASING;
        _useGlowEffects = LOW_USE_GLOW_EFFECTS;
        _glowRadiusFactor = LOW_GLOW_RADIUS_FACTOR;
        _glowAlpha = LOW_GLOW_ALPHA;
        _dotSpeedFactor = LOW_DOT_SPEED_FACTOR;
    }

    private void MediumQualitySettings()
    {
        _dotCount = MEDIUM_DOT_COUNT;
        _useAdvancedEffects = MEDIUM_USE_ADVANCED_EFFECTS;
        _useAntiAlias = MEDIUM_USE_ANTIALIASING;
        _useGlowEffects = MEDIUM_USE_GLOW_EFFECTS;
        _glowRadiusFactor = MEDIUM_GLOW_RADIUS_FACTOR;
        _glowAlpha = MEDIUM_GLOW_ALPHA;
        _dotSpeedFactor = MEDIUM_DOT_SPEED_FACTOR;
    }

    private void HighQualitySettings()
    {
        _dotCount = HIGH_DOT_COUNT;
        _useAdvancedEffects = HIGH_USE_ADVANCED_EFFECTS;
        _useAntiAlias = HIGH_USE_ANTIALIASING;
        _useGlowEffects = HIGH_USE_GLOW_EFFECTS;
        _glowRadiusFactor = HIGH_GLOW_RADIUS_FACTOR;
        _glowAlpha = HIGH_GLOW_ALPHA;
        _dotSpeedFactor = HIGH_DOT_SPEED_FACTOR;
    }

    private void UpdateQualityDependentResources() =>
        _logger.Safe(() => HandleUpdateQualityDependentResources(),
                    LogPrefix,
                    "Failed to update quality-dependent resources");

    private void HandleUpdateQualityDependentResources()
    {
        _dotPaint.IsAntialias = _useAntiAlias;
        _glowPaint.IsAntialias = _useAntiAlias;

        UpdateGlowMaskFilter();

        if (_lastImageInfo.Width > 0 && _lastImageInfo.Height > 0)
        {
            lock (_renderDataLock)
            {
                _dataReady = false;
                _currentRenderData = null;
            }
            ResetDots(_lastImageInfo);
        }
    }

    protected override void RenderEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint paint) =>
        _logger.Safe(() => HandleRenderEffect(canvas, spectrum, info, barWidth, barSpacing, barCount, paint),
                  LogPrefix,
                  "Error during rendering");

    private void HandleRenderEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint paint)
    {
        UpdateState(spectrum, barCount, info);
        RenderFrame(canvas, info);
    }

    private void UpdateState(
        float[] spectrum,
        int barCount,
        SKImageInfo info) =>
        _logger.Safe(() => HandleUpdateState(spectrum, barCount, info),
                    LogPrefix,
                    "Error updating renderer state");

    private void HandleUpdateState(
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

        _lastUpdateTime = DateTime.Now;
    }

    private void UpdateGlobalRadiusMultiplier() =>
        _globalRadiusMultiplier = Clamp(1.0f + _maxSpectrum * DOT_RADIUS_SCALE_FACTOR, 0.5f, 2.0f);

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

    private void UpdateDots(float[] processedSpectrum, SKImageInfo info) =>
        _logger.Safe(() => HandleUpdateDots(processedSpectrum, info),
                    LogPrefix,
                    "Error updating dots");

    private void HandleUpdateDots(float[] processedSpectrum, SKImageInfo info)
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetSpectrumIndexForDot(int dotIndex, int spectrumLength) =>
        Clamp((int)((float)dotIndex / _dotCount * spectrumLength), 0, spectrumLength - 1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

        float newRadius = dot.BaseRadius
                          * (1.0f + spectrumValue * SPECTRUM_VELOCITY_FACTOR)
                          * _globalRadiusMultiplier;

        return dot with
        {
            X = newX,
            Y = newY,
            VelocityX = newVelocityX,
            VelocityY = newVelocityY,
            Radius = newRadius
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (float forceX, float forceY) CalculateForces(
        float normalizedX,
        float normalizedY,
        float spectrumValue)
    {
        float gravityX = (_gravityCenter.X - normalizedX) * DOT_SPEED_BASE * _dotSpeedFactor;
        float gravityY = (_gravityCenter.Y - normalizedY) * DOT_SPEED_BASE * _dotSpeedFactor;

        float spectrumForceX = (normalizedX - 0.5f)
                               * spectrumValue
                               * SPECTRUM_INFLUENCE_FACTOR
                               * DOT_SPEED_SCALE;

        float spectrumForceY = (normalizedY - 0.5f)
                               * spectrumValue
                               * SPECTRUM_INFLUENCE_FACTOR
                               * DOT_SPEED_SCALE;

        return (gravityX + spectrumForceX, gravityY + spectrumForceY);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

    private void PrepareRenderData(int barCount) =>
        _logger.Safe(() => HandlePrepareRenderData(barCount),
                    LogPrefix,
                    "Error preparing render data");

    private void HandlePrepareRenderData(int barCount)
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
        SKImageInfo info) =>
        _logger.Safe(() => HandleRenderFrame(canvas, info),
                    LogPrefix,
                    "Error rendering dots frame");

    private void HandleRenderFrame(
        SKCanvas canvas,
        SKImageInfo info)
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
    }

    private void DrawDots(
        SKCanvas canvas,
        Dot[] dots,
        float alphaFactor) =>
        _logger.Safe(() => HandleDrawDots(canvas, dots, alphaFactor),
                    LogPrefix,
                    "Error drawing dots");

    private void HandleDrawDots(
        SKCanvas canvas,
        Dot[] dots,
        float alphaFactor)
    {
        foreach (var dot in dots)
        {
            if (dot.Radius < 0.5f)
                continue;

            byte alpha = (byte)(ALPHA_BASE * MAX_ALPHA_BYTE * alphaFactor);
            DrawDotWithGlow(canvas, dot, alpha);
        }
    }

    private void DrawDotWithGlow(SKCanvas canvas, Dot dot, byte alpha)
    {
        if (_useAdvancedEffects && _useGlowEffects)
        {
            _glowPaint.Color = dot.Color.WithAlpha((byte)(_glowAlpha * alpha));
            float glowRadius = dot.Radius * (1.0f + _glowRadiusFactor);
            canvas.DrawCircle(dot.X, dot.Y, glowRadius, _glowPaint);
        }

        _dotPaint.Color = dot.Color.WithAlpha(alpha);
        canvas.DrawCircle(dot.X, dot.Y, dot.Radius, _dotPaint);
    }

    protected override void OnInvalidateCachedResources()
    {
        base.OnInvalidateCachedResources();
        _dataReady = false;
        _currentRenderData = null;
        _logger.Debug(LogPrefix, "Cached resources invalidated");
    }

    protected override void OnDispose()
    {
        _dotPaint?.Dispose();
        _glowPaint?.Dispose();
        _dots = [];
        base.OnDispose();
        _logger.Debug(LogPrefix, "Disposed");
    }

    public override bool RequiresRedraw() => true;
}