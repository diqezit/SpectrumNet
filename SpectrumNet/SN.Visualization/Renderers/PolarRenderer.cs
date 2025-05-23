#nullable enable

using static System.MathF;
using static SpectrumNet.SN.Visualization.Renderers.PolarRenderer.Constants;

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class PolarRenderer : EffectSpectrumRenderer
{
    private const string LogPrefix = nameof(PolarRenderer);

    private static readonly Lazy<PolarRenderer> _instance =
        new(() => new PolarRenderer());

    private PolarRenderer() { }

    public static PolarRenderer GetInstance() => _instance.Value;

    public static class Constants
    {
        public const float
            MIN_RADIUS = 30f,
            RADIUS_MULTIPLIER = 200f,
            INNER_RADIUS_RATIO = 0.5f,
            MAX_SPECTRUM_VALUE = 1.0f,
            SPECTRUM_SCALE = 2.0f,
            CHANGE_THRESHOLD = 0.01f,
            DEG_TO_RAD = MathF.PI / 180.0f,
            ROTATION_SPEED = 0.3f,
            TIME_STEP = 0.016f,
            TIME_MODIFIER = 0.01f,
            MODULATION_FACTOR = 0.3f,
            MODULATION_FREQ = 5f,
            PULSE_SPEED = 2.0f,
            PULSE_AMPLITUDE = 0.2f,
            PULSE_SCALE_MULTIPLIER = 0.1f,
            DASH_PHASE_SPEED = 0.5f,
            DEFAULT_STROKE_WIDTH = 1.5f,
            CENTER_CIRCLE_SIZE = 6f,
            CENTER_CIRCLE_GLOW_MULTIPLIER = 0.8f,
            CENTER_CIRCLE_MAIN_MULTIPLIER = 0.7f,
            CENTER_CIRCLE_HIGHLIGHT_OFFSET_MULTIPLIER = 0.25f,
            CENTER_CIRCLE_HIGHLIGHT_SIZE_MULTIPLIER = 0.2f,
            DASH_LENGTH = 6.0f,
            HIGHLIGHT_FACTOR = 1.4f,
            GLOW_RADIUS = 8.0f,
            GLOW_SIGMA = 2.5f,
            GRADIENT_COLOR_MULTIPLIER = 0.7f,
            MIN_BAR_WIDTH = 0.5f,
            MAX_BAR_WIDTH = 4.0f;

        public const byte
            FILL_ALPHA = 120,
            GLOW_ALPHA = 80,
            HIGHLIGHT_ALPHA = 160,
            GRADIENT_END_ALPHA = 20;

        public const int
            MAX_POINT_COUNT = 120,
            POINT_COUNT_OVERLAY = 80,
            MIN_POINT_COUNT = 24;

        public static readonly Dictionary<RenderQuality, QualitySettings> QualityPresets = new()
        {
            [RenderQuality.Low] = new(
                SmoothingFactor: 0.10f,
                MaxPoints: 40,
                StrokeMultiplier: 0.75f,
                PathSimplification: 0.5f,
                UseGlow: false,
                UseHighlight: false,
                UsePulseEffect: false,
                UseDashEffect: false
            ),
            [RenderQuality.Medium] = new(
                SmoothingFactor: 0.15f,
                MaxPoints: 80,
                StrokeMultiplier: 1.0f,
                PathSimplification: 0.25f,
                UseGlow: false,
                UseHighlight: true,
                UsePulseEffect: true,
                UseDashEffect: true
            ),
            [RenderQuality.High] = new(
                SmoothingFactor: 0.20f,
                MaxPoints: 120,
                StrokeMultiplier: 1.25f,
                PathSimplification: 0.0f,
                UseGlow: true,
                UseHighlight: true,
                UsePulseEffect: true,
                UseDashEffect: true
            )
        };

        public record QualitySettings(
            float SmoothingFactor,
            int MaxPoints,
            float StrokeMultiplier,
            float PathSimplification,
            bool UseGlow,
            bool UseHighlight,
            bool UsePulseEffect,
            bool UseDashEffect
        );
    }

    private static readonly float[] DashIntervals = [DASH_LENGTH, DASH_LENGTH * 2];
    private static readonly SKColor[] GradientColors = new SKColor[2];

    private QualitySettings _currentSettings = QualityPresets[RenderQuality.Medium];
    private readonly SemaphoreSlim _pathUpdateSemaphore = new(1, 1);

    private SKPath? _outerPath;
    private SKPath? _innerPath;
    private SKPaint? _fillPaint;
    private SKPaint? _strokePaint;
    private SKPaint? _centerPaint;
    private SKPaint? _glowPaint;
    private SKPaint? _highlightPaint;

    private SKShader? _gradientShader;
    private SKPathEffect? _dashEffect;
    private SKImageFilter? _glowFilter;
    private SKPicture? _cachedCenterCircle;

    private SKPoint[]? _outerPoints;
    private SKPoint[]? _innerPoints;
    private SKColor _lastBaseColor;
    private SKRect _centerCircleBounds;
    private SKRect _clipBounds;
    private float[]? _tempSpectrum;

    private float _rotation;
    private float _pulseEffect = 1.0f;
    private bool _pathsNeedUpdate = true;
    private int _currentPointCount;

    protected override void OnInitialize()
    {
        base.OnInitialize();
        InitializeResources();
        _logger.Log(LogLevel.Debug, LogPrefix, "Initialized");
    }

    private void InitializeResources()
    {
        _outerPoints = new SKPoint[MAX_POINT_COUNT + 1];
        _innerPoints = new SKPoint[MAX_POINT_COUNT + 1];
        _tempSpectrum = new float[MAX_POINT_COUNT];

        _processedSpectrum = new float[MAX_POINT_COUNT];
        _previousSpectrum = new float[MAX_POINT_COUNT];

        _outerPath = _pathPool.Get();
        _innerPath = _pathPool.Get();

        InitializePaints();

        _currentPointCount = MAX_POINT_COUNT;
        _centerCircleBounds = new SKRect(
            -CENTER_CIRCLE_SIZE * 1.5f,
            -CENTER_CIRCLE_SIZE * 1.5f,
            CENTER_CIRCLE_SIZE * 1.5f,
            CENTER_CIRCLE_SIZE * 1.5f
        );

        UpdateCenterCircle(SKColors.White);
    }

    private void InitializePaints()
    {
        _fillPaint = new SKPaint
        {
            IsAntialias = _useAntiAlias,
            Style = SKPaintStyle.Fill
        };

        _strokePaint = new SKPaint
        {
            IsAntialias = _useAntiAlias,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = DEFAULT_STROKE_WIDTH,
            StrokeJoin = SKStrokeJoin.Round,
            StrokeCap = SKStrokeCap.Round
        };

        _centerPaint = new SKPaint
        {
            IsAntialias = _useAntiAlias,
            Style = SKPaintStyle.Fill
        };

        _glowPaint = new SKPaint
        {
            IsAntialias = _useAntiAlias,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = DEFAULT_STROKE_WIDTH * 1.5f,
            BlendMode = SKBlendMode.SrcOver
        };

        _highlightPaint = new SKPaint
        {
            IsAntialias = _useAntiAlias,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = DEFAULT_STROKE_WIDTH * 0.5f,
            StrokeCap = SKStrokeCap.Round
        };
    }

    protected override void OnConfigurationChanged()
    {
        _currentPointCount = _isOverlayActive ?
            POINT_COUNT_OVERLAY :
            (int)MathF.Min(_currentSettings.MaxPoints, MAX_POINT_COUNT);
        _pathsNeedUpdate = true;
    }

    protected override void OnQualitySettingsApplied()
    {
        _currentSettings = QualityPresets[Quality];
        _useAntiAlias = _currentSettings.UseGlow || _currentSettings.UseHighlight;

        UpdatePaintSettings();
        InvalidateCachedResources();

        _currentPointCount = _isOverlayActive ?
            POINT_COUNT_OVERLAY :
            (int)MathF.Min(_currentSettings.MaxPoints, MAX_POINT_COUNT);
        _pathsNeedUpdate = true;

        _logger.Log(LogLevel.Debug, LogPrefix, $"Quality changed to {Quality}");
    }

    private void UpdatePaintSettings()
    {
        var antiAlias = _useAntiAlias;
        var strokeWidth = DEFAULT_STROKE_WIDTH * _currentSettings.StrokeMultiplier;

        if (_fillPaint != null) _fillPaint.IsAntialias = antiAlias;
        if (_strokePaint != null)
        {
            _strokePaint.IsAntialias = antiAlias;
            _strokePaint.StrokeWidth = strokeWidth;
        }
        if (_centerPaint != null) _centerPaint.IsAntialias = antiAlias;
        if (_glowPaint != null)
        {
            _glowPaint.IsAntialias = antiAlias;
            _glowPaint.StrokeWidth = strokeWidth * 1.5f;
        }
        if (_highlightPaint != null)
        {
            _highlightPaint.IsAntialias = antiAlias;
            _highlightPaint.StrokeWidth = strokeWidth * 0.5f;
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
            () => RenderPolarEffect(canvas, spectrum, info, barWidth, barCount, paint),
            LogPrefix,
            "Error during rendering"
        );
    }

    private void RenderPolarEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        int barCount,
        SKPaint paint)
    {
        int safeBarCount = Clamp(barCount, MIN_POINT_COUNT, _currentSettings.MaxPoints);

        PrepareClipBounds();
        ProcessSpectrumData(spectrum, safeBarCount);
        UpdatePaths(safeBarCount);
        UpdateVisualEffects(paint.Color);
        UpdateEffectParameters(barWidth);

        RenderFrame(canvas, info);
    }

    private void PrepareClipBounds()
    {
        float maxRadius = MIN_RADIUS + MAX_SPECTRUM_VALUE *
            RADIUS_MULTIPLIER * (1 + MODULATION_FACTOR);
        _clipBounds = new SKRect(-maxRadius, -maxRadius, maxRadius, maxRadius);
    }

    private void ProcessSpectrumData(float[] spectrum, int barCount)
    {
        if (_tempSpectrum == null || _previousSpectrum == null ||
            _processedSpectrum == null) return;

        int pointCount = (int)MathF.Min(barCount, _currentPointCount);

        for (int i = 0; i < pointCount && i < _tempSpectrum.Length; i++)
        {
            float spectrumIndex = i * spectrum.Length / (2f * pointCount);
            int baseIndex = (int)spectrumIndex;
            float fraction = spectrumIndex - baseIndex;

            float value = baseIndex >= spectrum.Length / 2 - 1 ?
                spectrum[(int)MathF.Min(spectrum.Length / 2 - 1, spectrum.Length - 1)] :
                baseIndex + 1 < spectrum.Length ?
                    spectrum[baseIndex] * (1 - fraction) + spectrum[baseIndex + 1] * fraction :
                    spectrum[baseIndex];

            _tempSpectrum[i] = MathF.Min(value * SPECTRUM_SCALE, MAX_SPECTRUM_VALUE);
        }

        float maxChange = 0f;
        for (int i = 0; i < pointCount; i++)
        {
            float newValue = _previousSpectrum[i] * (1 - _currentSettings.SmoothingFactor) +
                           _tempSpectrum[i] * _currentSettings.SmoothingFactor;

            float change = MathF.Abs(newValue - _previousSpectrum[i]);
            maxChange = MathF.Max(maxChange, change);

            _processedSpectrum[i] = newValue;
            _previousSpectrum[i] = newValue;
        }

        if (maxChange > CHANGE_THRESHOLD)
            _pathsNeedUpdate = true;
    }

    private void UpdatePaths(int barCount)
    {
        if (!_pathsNeedUpdate || !_pathUpdateSemaphore.Wait(0)) return;

        try
        {
            GeneratePaths(barCount);
            _pathsNeedUpdate = false;
        }
        finally
        {
            _pathUpdateSemaphore.Release();
        }
    }

    private void GeneratePaths(int barCount)
    {
        if (_processedSpectrum == null || _outerPath == null ||
            _innerPath == null || _outerPoints == null ||
            _innerPoints == null) return;

        _time += TIME_STEP;
        _rotation += ROTATION_SPEED * _time * TIME_MODIFIER;

        int effectivePointCount = (int)MathF.Min(barCount, _currentPointCount);
        int skipFactor = _currentSettings.PathSimplification > 0 ?
            Max(1, (int)(1.0f / (1.0f - _currentSettings.PathSimplification))) : 1;
        int actualPoints = effectivePointCount / skipFactor;
        float angleStep = 360f / actualPoints;

        for (int i = 0, pointIndex = 0;
             i <= effectivePointCount && pointIndex < _outerPoints.Length;
             i += skipFactor, pointIndex++)
        {
            float angle = pointIndex * angleStep * DEG_TO_RAD;
            float cosAngle = Cos(angle);
            float sinAngle = Sin(angle);

            int spectrumIndex = i % effectivePointCount;
            float spectrumValue = spectrumIndex < _processedSpectrum.Length ?
                _processedSpectrum[spectrumIndex] : 0f;

            float modulation = Quality == RenderQuality.Low ? 1.0f :
                1 + MODULATION_FACTOR * Sin(
                    pointIndex * angleStep * MODULATION_FREQ * DEG_TO_RAD + _time * 2
                );

            if (_currentSettings.UsePulseEffect)
                modulation += PULSE_AMPLITUDE * 0.5f * Sin(_time * 0.5f + pointIndex * 0.1f);

            float outerRadius = MIN_RADIUS + spectrumValue * modulation * RADIUS_MULTIPLIER;
            _outerPoints[pointIndex] = new SKPoint(
                outerRadius * cosAngle,
                outerRadius * sinAngle
            );

            float innerRadius = MIN_RADIUS + spectrumValue * INNER_RADIUS_RATIO * modulation * RADIUS_MULTIPLIER;
            _innerPoints[pointIndex] = new SKPoint(
                innerRadius * cosAngle,
                innerRadius * sinAngle
            );
        }

        _outerPath.Reset();
        _innerPath.Reset();

        int pointsToUse = (int)MathF.Min(actualPoints + 1, _outerPoints.Length);
        var outerSlice = _outerPoints.Take(pointsToUse).ToArray();
        var innerSlice = _innerPoints.Take(pointsToUse).ToArray();

        _outerPath.AddPoly(outerSlice, true);
        _innerPath.AddPoly(innerSlice, true);
    }

    private void UpdateVisualEffects(SKColor baseColor)
    {
        if (baseColor == _lastBaseColor && _gradientShader != null) return;

        _lastBaseColor = baseColor;

        GradientColors[0] = baseColor.WithAlpha(FILL_ALPHA);
        GradientColors[1] = new SKColor(
            (byte)MathF.Min(255, baseColor.Red * GRADIENT_COLOR_MULTIPLIER),
            (byte)MathF.Min(255, baseColor.Green * GRADIENT_COLOR_MULTIPLIER),
            (byte)MathF.Min(255, baseColor.Blue * GRADIENT_COLOR_MULTIPLIER),
            GRADIENT_END_ALPHA
        );

        _gradientShader?.Dispose();
        _gradientShader = SKShader.CreateRadialGradient(
            new SKPoint(0, 0),
            MIN_RADIUS + MAX_SPECTRUM_VALUE * RADIUS_MULTIPLIER,
            GradientColors,
            SKShaderTileMode.Clamp
        );

        if (_fillPaint != null) _fillPaint.Shader = _gradientShader;
        if (_strokePaint != null) _strokePaint.Color = baseColor;
        if (_centerPaint != null) _centerPaint.Color = baseColor;

        if (_currentSettings.UseGlow && _glowPaint != null)
        {
            _glowFilter?.Dispose();
            _glowFilter = SKImageFilter.CreateBlur(GLOW_RADIUS, GLOW_SIGMA);
            _glowPaint.Color = baseColor.WithAlpha(GLOW_ALPHA);
            _glowPaint.ImageFilter = _glowFilter;
        }

        if (_currentSettings.UseHighlight && _highlightPaint != null)
        {
            _highlightPaint.Color = new SKColor(
                (byte)MathF.Min(255, baseColor.Red * HIGHLIGHT_FACTOR),
                (byte)MathF.Min(255, baseColor.Green * HIGHLIGHT_FACTOR),
                (byte)MathF.Min(255, baseColor.Blue * HIGHLIGHT_FACTOR),
                HIGHLIGHT_ALPHA
            );
        }

        if (_currentSettings.UseDashEffect)
        {
            _dashEffect?.Dispose();
            _dashEffect = SKPathEffect.CreateDash(
                DashIntervals,
                _time * DASH_PHASE_SPEED % (DASH_LENGTH * 3)
            );
        }

        UpdateCenterCircle(baseColor);
    }

    private void UpdateEffectParameters(float barWidth)
    {
        float safeBarWidth = Clamp(barWidth, MIN_BAR_WIDTH, MAX_BAR_WIDTH);

        _pulseEffect = _currentSettings.UsePulseEffect ?
            Sin(_time * PULSE_SPEED) * PULSE_AMPLITUDE + 1.0f : 1.0f;

        if (_strokePaint != null)
            _strokePaint.StrokeWidth = safeBarWidth * _pulseEffect * _currentSettings.StrokeMultiplier;

        if (_currentSettings.UseGlow && _glowPaint != null)
            _glowPaint.StrokeWidth = safeBarWidth * 1.5f * _pulseEffect * _currentSettings.StrokeMultiplier;

        if (_currentSettings.UseHighlight && _highlightPaint != null)
            _highlightPaint.StrokeWidth = safeBarWidth * 0.5f * _pulseEffect * _currentSettings.StrokeMultiplier;
    }

    private void RenderFrame(SKCanvas canvas, SKImageInfo info)
    {
        if (_outerPath == null || _innerPath == null ||
            _fillPaint == null || _strokePaint == null) return;

        canvas.Save();
        canvas.Translate(info.Width / 2f, info.Height / 2f);
        canvas.RotateDegrees(_rotation);

        if (!canvas.QuickReject(_clipBounds))
        {
            if (_currentSettings.UseGlow && _glowPaint != null)
                canvas.DrawPath(_outerPath, _glowPaint);

            canvas.DrawPath(_outerPath, _fillPaint);
            canvas.DrawPath(_outerPath, _strokePaint);

            if (_currentSettings.UseDashEffect && _dashEffect != null)
            {
                var originalEffect = _strokePaint.PathEffect;
                _strokePaint.PathEffect = _dashEffect;
                canvas.DrawPath(_innerPath, _strokePaint);
                _strokePaint.PathEffect = originalEffect;
            }
            else
            {
                canvas.DrawPath(_innerPath, _strokePaint);
            }

            if (_currentSettings.UseHighlight && _highlightPaint != null)
                canvas.DrawPath(_innerPath, _highlightPaint);

            RenderCenterCircle(canvas);
        }

        canvas.Restore();
    }

    private void RenderCenterCircle(SKCanvas canvas)
    {
        if (_cachedCenterCircle == null) return;

        float pulseScale = _currentSettings.UsePulseEffect ?
            1.0f + Sin(_time * PULSE_SPEED * 0.5f) * PULSE_SCALE_MULTIPLIER : 1.0f;

        canvas.Save();
        canvas.Scale(pulseScale, pulseScale);
        canvas.DrawPicture(_cachedCenterCircle);
        canvas.Restore();
    }

    private void UpdateCenterCircle(SKColor baseColor)
    {
        _cachedCenterCircle?.Dispose();

        using var recorder = new SKPictureRecorder();
        var pictureCanvas = recorder.BeginRecording(_centerCircleBounds);

        if (_currentSettings.UseGlow)
        {
            using var glowPaint = new SKPaint
            {
                IsAntialias = _useAntiAlias,
                Style = SKPaintStyle.Fill,
                Color = baseColor.WithAlpha(GLOW_ALPHA),
                ImageFilter = SKImageFilter.CreateBlur(
                    GLOW_RADIUS * 0.5f,
                    GLOW_SIGMA * 0.5f
                )
            };

            pictureCanvas.DrawCircle(
                0, 0,
                CENTER_CIRCLE_SIZE * CENTER_CIRCLE_GLOW_MULTIPLIER,
                glowPaint
            );
        }

        if (_centerPaint != null)
        {
            pictureCanvas.DrawCircle(
                0, 0,
                CENTER_CIRCLE_SIZE * CENTER_CIRCLE_MAIN_MULTIPLIER,
                _centerPaint
            );
        }

        if (_currentSettings.UseHighlight)
        {
            using var highlightPaint = new SKPaint
            {
                IsAntialias = _useAntiAlias,
                Style = SKPaintStyle.Fill,
                Color = SKColors.White.WithAlpha(HIGHLIGHT_ALPHA)
            };

            pictureCanvas.DrawCircle(
                -CENTER_CIRCLE_SIZE * CENTER_CIRCLE_HIGHLIGHT_OFFSET_MULTIPLIER,
                -CENTER_CIRCLE_SIZE * CENTER_CIRCLE_HIGHLIGHT_OFFSET_MULTIPLIER,
                CENTER_CIRCLE_SIZE * CENTER_CIRCLE_HIGHLIGHT_SIZE_MULTIPLIER,
                highlightPaint
            );
        }

        _cachedCenterCircle = recorder.EndRecording();
    }

    protected override void OnInvalidateCachedResources()
    {
        base.OnInvalidateCachedResources();

        _cachedCenterCircle?.Dispose();
        _cachedCenterCircle = null;
        _dashEffect?.Dispose();
        _dashEffect = null;
        _gradientShader?.Dispose();
        _gradientShader = null;
        _glowFilter?.Dispose();
        _glowFilter = null;
        _pathsNeedUpdate = true;
    }

    protected override void OnDispose()
    {
        _pathUpdateSemaphore?.Dispose();

        _outerPath?.Dispose();
        _innerPath?.Dispose();
        _fillPaint?.Dispose();
        _strokePaint?.Dispose();
        _centerPaint?.Dispose();
        _cachedCenterCircle?.Dispose();
        _glowPaint?.Dispose();
        _highlightPaint?.Dispose();
        _gradientShader?.Dispose();
        _dashEffect?.Dispose();
        _glowFilter?.Dispose();

        _outerPath = null;
        _innerPath = null;
        _fillPaint = null;
        _strokePaint = null;
        _centerPaint = null;
        _cachedCenterCircle = null;
        _tempSpectrum = null;
        _outerPoints = null;
        _innerPoints = null;
        _glowPaint = null;
        _highlightPaint = null;
        _gradientShader = null;
        _dashEffect = null;
        _glowFilter = null;

        base.OnDispose();
        _logger.Log(LogLevel.Debug, LogPrefix, "Disposed");
    }
}