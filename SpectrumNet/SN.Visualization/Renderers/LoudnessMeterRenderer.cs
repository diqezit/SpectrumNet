#nullable enable

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class LoudnessMeterRenderer : EffectSpectrumRenderer<LoudnessMeterRenderer.QualitySettings>
{
    private static readonly Lazy<LoudnessMeterRenderer> _instance =
        new(() => new LoudnessMeterRenderer());

    public static LoudnessMeterRenderer GetInstance() => _instance.Value;

    private const float MIN_LOUDNESS_THRESHOLD = 0.001f,
        MEDIUM_LOUDNESS_THRESHOLD = 0.4f;

    private const float SMOOTHING_FACTOR_ATTACK_NORMAL = 0.6f,
        SMOOTHING_FACTOR_RELEASE_NORMAL = 0.2f,
        SMOOTHING_FACTOR_ATTACK_OVERLAY = 0.8f,
        SMOOTHING_FACTOR_RELEASE_OVERLAY = 0.3f;

    private const float PEAK_GRAVITY = 0.015f,
        PEAK_VELOCITY_DAMPING = 0.95f,
        PEAK_CAPTURE_THRESHOLD = 0.02f,
        PEAK_HOLD_TIME_MS = 300f,
        PEAK_INDICATOR_HEIGHT = 3f,
        PEAK_INDICATOR_BORDER_WIDTH = 0.8f,
        EDGE_INDICATOR_WIDTH = 4f;

    private const float METER_PADDING = 20f,
        METER_CORNER_RADIUS = 8f,
        OUTER_BORDER_WIDTH = 2.5f,
        INNER_BORDER_WIDTH = 1.8f,
        FILL_BORDER_WIDTH = 1.2f;

    private const float MARKER_WIDTH = 12f,
        MARKER_HEIGHT = 2.5f,
        MARKER_BORDER_WIDTH = 0.8f;

    private const float GLOW_HEIGHT_FACTOR_LOW = 0f,
        GLOW_HEIGHT_FACTOR_MEDIUM = 0.35f,
        GLOW_HEIGHT_FACTOR_HIGH = 0.6f,
        GRADIENT_ALPHA_FACTOR_LOW = 0.85f,
        GRADIENT_ALPHA_FACTOR_MEDIUM = 0.95f,
        GRADIENT_ALPHA_FACTOR_HIGH = 1.0f,
        DYNAMIC_GRADIENT_FACTOR_LOW = 0f,
        DYNAMIC_GRADIENT_FACTOR_MEDIUM = 0.7f,
        DYNAMIC_GRADIENT_FACTOR_HIGH = 1.0f,
        GLOW_INTENSITY_LOW = 0f,
        GLOW_INTENSITY_MEDIUM = 0.5f,
        GLOW_INTENSITY_HIGH = 0.7f,
        BLUR_SIGMA_LOW = 0f,
        BLUR_SIGMA_MEDIUM = 8f,
        BLUR_SIGMA_HIGH = 12f;

    private const byte BACKGROUND_ALPHA = 45,
        OUTER_BORDER_ALPHA = 140,
        INNER_BORDER_ALPHA = 80,
        FILL_BORDER_ALPHA = 100,
        MARKER_ALPHA = 85,
        MARKER_BORDER_ALPHA = 120,
        PEAK_ALPHA = 230,
        PEAK_BORDER_ALPHA = 255,
        EDGE_ALPHA = 255;

    private static readonly float[] _defaultColorPositions = [0f, 0.5f, 1.0f];
    private static readonly float[] _markerPositions =
        [0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1.0f];

    private float _currentLoudness;
    private float _peakValue;
    private float _peakTimer;
    private float _peakVelocity;
    private int _currentWidth;
    private int _currentHeight;
    private readonly float _peakHoldTime = PEAK_HOLD_TIME_MS / 1000f;

    public sealed class QualitySettings
    {
        public bool UseGlow { get; init; }
        public bool UseMarkers { get; init; }
        public bool UseInnerBorder { get; init; }
        public bool UseFillBorder { get; init; }
        public bool UseMarkerBorders { get; init; }
        public float GlowIntensity { get; init; }
        public float BlurSigma { get; init; }
        public float GlowHeightFactor { get; init; }
        public float GradientAlphaFactor { get; init; }
        public float DynamicGradientFactor { get; init; }
    }

    protected override IReadOnlyDictionary<RenderQuality, QualitySettings>
        QualitySettingsPresets
    { get; } = new Dictionary<RenderQuality, QualitySettings>
    {
        [RenderQuality.Low] = new()
        {
            UseGlow = false,
            UseMarkers = false,
            UseInnerBorder = false,
            UseFillBorder = false,
            UseMarkerBorders = false,
            GlowIntensity = GLOW_INTENSITY_LOW,
            BlurSigma = BLUR_SIGMA_LOW,
            GlowHeightFactor = GLOW_HEIGHT_FACTOR_LOW,
            GradientAlphaFactor = GRADIENT_ALPHA_FACTOR_LOW,
            DynamicGradientFactor = DYNAMIC_GRADIENT_FACTOR_LOW
        },
        [RenderQuality.Medium] = new()
        {
            UseGlow = true,
            UseMarkers = true,
            UseInnerBorder = true,
            UseFillBorder = true,
            UseMarkerBorders = false,
            GlowIntensity = GLOW_INTENSITY_MEDIUM,
            BlurSigma = BLUR_SIGMA_MEDIUM,
            GlowHeightFactor = GLOW_HEIGHT_FACTOR_MEDIUM,
            GradientAlphaFactor = GRADIENT_ALPHA_FACTOR_MEDIUM,
            DynamicGradientFactor = DYNAMIC_GRADIENT_FACTOR_MEDIUM
        },
        [RenderQuality.High] = new()
        {
            UseGlow = true,
            UseMarkers = true,
            UseInnerBorder = true,
            UseFillBorder = true,
            UseMarkerBorders = true,
            GlowIntensity = GLOW_INTENSITY_HIGH,
            BlurSigma = BLUR_SIGMA_HIGH,
            GlowHeightFactor = GLOW_HEIGHT_FACTOR_HIGH,
            GradientAlphaFactor = GRADIENT_ALPHA_FACTOR_HIGH,
            DynamicGradientFactor = DYNAMIC_GRADIENT_FACTOR_HIGH
        }
    };

    protected override void RenderEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        RenderParameters renderParams,
        SKPaint passedInPaint)
    {
        if (CurrentQualitySettings == null || renderParams.EffectiveBarCount <= 0)
            return;

        var (isValid, processedSpectrum) = ProcessSpectrum(
            spectrum,
            renderParams.EffectiveBarCount,
            applyTemporalSmoothing: true);

        if (!isValid || processedSpectrum == null)
            return;

        var meterData = CalculateMeterData(processedSpectrum, info);

        if (!ValidateMeterData(meterData))
            return;

        RenderMeterVisualization(
            canvas,
            meterData,
            renderParams,
            passedInPaint);
    }

    private MeterData CalculateMeterData(float[] spectrum, SKImageInfo info)
    {
        UpdateDimensions(info);
        float loudness = CalculateAndSmoothLoudness(spectrum);
        UpdatePeakPhysics(loudness, 1f / 60f);

        var meterRect = CalculateMeterRect(info);

        return new MeterData(
            Loudness: loudness,
            PeakPosition: _peakValue,
            MeterRect: meterRect,
            Width: info.Width,
            Height: info.Height);
    }

    private static SKRect CalculateMeterRect(SKImageInfo info) =>
        new(
            METER_PADDING,
            METER_PADDING,
            info.Width - METER_PADDING,
            info.Height - METER_PADDING);

    private static bool ValidateMeterData(MeterData data) =>
        data.MeterRect.Width > 0 &&
        data.MeterRect.Height > 0;

    private void RenderMeterVisualization(
        SKCanvas canvas,
        MeterData data,
        RenderParameters renderParams,
        SKPaint basePaint)
    {
        var settings = CurrentQualitySettings!;

        DrawMeterBackground(canvas, data.MeterRect);
        DrawOuterBorder(canvas, data.MeterRect);

        RenderWithOverlay(canvas, () =>
        {
            canvas.Save();
            canvas.ClipRoundRect(
                new SKRoundRect(data.MeterRect, METER_CORNER_RADIUS),
                SKClipOperation.Intersect);

            try
            {
                if (UseAdvancedEffects && settings.UseMarkers)
                    RenderMarkersLayer(canvas, data.MeterRect, settings);

                if (data.Loudness >= MIN_LOUDNESS_THRESHOLD)
                    RenderFillLayer(canvas, data, settings);

                if (settings.UseInnerBorder)
                    RenderInnerBorderLayer(canvas, data.MeterRect);

                if (data.PeakPosition > MIN_LOUDNESS_THRESHOLD)
                    RenderPeakLayer(canvas, data, settings);
            }
            finally
            {
                canvas.Restore();
            }
        });
    }

    private void DrawMeterBackground(SKCanvas canvas, SKRect rect)
    {
        var backgroundPaint = CreatePaint(
            new SKColor(20, 20, 20, BACKGROUND_ALPHA),
            SKPaintStyle.Fill);

        try
        {
            canvas.DrawRoundRect(
                rect,
                METER_CORNER_RADIUS,
                METER_CORNER_RADIUS,
                backgroundPaint);
        }
        finally
        {
            ReturnPaint(backgroundPaint);
        }
    }

    private void DrawOuterBorder(SKCanvas canvas, SKRect rect)
    {
        var borderPaint = CreatePaint(
            new SKColor(120, 120, 120, OUTER_BORDER_ALPHA),
            SKPaintStyle.Stroke);
        borderPaint.StrokeWidth = OUTER_BORDER_WIDTH;
        borderPaint.IsAntialias = true;

        try
        {
            canvas.DrawRoundRect(
                rect,
                METER_CORNER_RADIUS,
                METER_CORNER_RADIUS,
                borderPaint);
        }
        finally
        {
            ReturnPaint(borderPaint);
        }
    }

    private void RenderMarkersLayer(SKCanvas canvas, SKRect meterRect, QualitySettings settings)
    {
        var markerPaint = CreatePaint(
            new SKColor(120, 120, 120, MARKER_ALPHA),
            SKPaintStyle.Fill);
        markerPaint.IsAntialias = true;

        var borderPaint = settings.UseMarkerBorders
            ? CreatePaint(new SKColor(160, 160, 160, MARKER_BORDER_ALPHA), SKPaintStyle.Stroke)
            : null;

        if (borderPaint != null)
        {
            borderPaint.StrokeWidth = MARKER_BORDER_WIDTH;
            borderPaint.IsAntialias = true;
        }

        try
        {
            foreach (float position in _markerPositions)
            {
                var (leftRect, rightRect) = CalculateMarkerRects(meterRect, position);

                canvas.DrawRect(leftRect, markerPaint);
                canvas.DrawRect(rightRect, markerPaint);

                if (borderPaint != null)
                {
                    canvas.DrawRect(leftRect, borderPaint);
                    canvas.DrawRect(rightRect, borderPaint);
                }
            }
        }
        finally
        {
            ReturnPaint(markerPaint);
            if (borderPaint != null)
                ReturnPaint(borderPaint);
        }
    }

    private static (SKRect Left, SKRect Right) CalculateMarkerRects(SKRect meterRect, float position)
    {
        float y = meterRect.Bottom - (meterRect.Height * position);

        var leftRect = new SKRect(
            meterRect.Left,
            y - MARKER_HEIGHT / 2,
            meterRect.Left + MARKER_WIDTH,
            y + MARKER_HEIGHT / 2);

        var rightRect = new SKRect(
            meterRect.Right - MARKER_WIDTH,
            y - MARKER_HEIGHT / 2,
            meterRect.Right,
            y + MARKER_HEIGHT / 2);

        return (leftRect, rightRect);
    }

    private void RenderFillLayer(SKCanvas canvas, MeterData data, QualitySettings settings)
    {
        float meterHeight = data.MeterRect.Height * data.Loudness;

        RenderLoudnessFill(canvas, data, meterHeight, settings);

        if (UseAdvancedEffects && settings.UseGlow && data.Loudness > MEDIUM_LOUDNESS_THRESHOLD)
            RenderGlowEffect(canvas, data, meterHeight, settings);

        if (settings.UseFillBorder)
            RenderFillBorder(canvas, data, meterHeight);
    }

    private void RenderLoudnessFill(
        SKCanvas canvas,
        MeterData data,
        float meterHeight,
        QualitySettings settings)
    {
        using var shader = CreateLoudnessGradientShader(data, settings);
        var fillPaint = CreatePaint(SKColors.Black, SKPaintStyle.Fill, shader);
        fillPaint.IsAntialias = true;

        try
        {
            canvas.DrawRect(
                data.MeterRect.Left,
                data.MeterRect.Bottom - meterHeight,
                data.MeterRect.Width,
                meterHeight,
                fillPaint);
        }
        finally
        {
            ReturnPaint(fillPaint);
        }
    }

    private void RenderGlowEffect(
        SKCanvas canvas,
        MeterData data,
        float meterHeight,
        QualitySettings settings)
    {
        float glowHeight = meterHeight * settings.GlowHeightFactor;
        var glowColor = CalculateGlowColor(data.Loudness, settings);

        using var blurFilter = SKMaskFilter.CreateBlur(
            SKBlurStyle.Normal,
            settings.BlurSigma);

        var glowPaint = CreatePaint(glowColor, SKPaintStyle.Fill);
        glowPaint.MaskFilter = blurFilter;
        glowPaint.IsAntialias = true;

        try
        {
            canvas.DrawRect(
                data.MeterRect.Left,
                data.MeterRect.Bottom - meterHeight,
                data.MeterRect.Width,
                glowHeight,
                glowPaint);
        }
        finally
        {
            ReturnPaint(glowPaint);
        }
    }

    private void RenderFillBorder(SKCanvas canvas, MeterData data, float meterHeight)
    {
        float fillTop = data.MeterRect.Bottom - meterHeight;

        var borderPaint = CreatePaint(
            new SKColor(220, 220, 220, FILL_BORDER_ALPHA),
            SKPaintStyle.Stroke);
        borderPaint.StrokeWidth = FILL_BORDER_WIDTH;
        borderPaint.IsAntialias = true;

        try
        {
            RenderPath(canvas, path =>
            {
                path.MoveTo(data.MeterRect.Left, fillTop);
                path.LineTo(data.MeterRect.Right, fillTop);
                path.LineTo(data.MeterRect.Right, data.MeterRect.Bottom);
                path.LineTo(data.MeterRect.Left, data.MeterRect.Bottom);
                path.Close();
            }, borderPaint);
        }
        finally
        {
            ReturnPaint(borderPaint);
        }
    }

    private void RenderInnerBorderLayer(SKCanvas canvas, SKRect rect)
    {
        var innerRect = rect;
        innerRect.Inflate(-INNER_BORDER_WIDTH, -INNER_BORDER_WIDTH);

        var innerBorderPaint = CreatePaint(
            new SKColor(170, 170, 170, INNER_BORDER_ALPHA),
            SKPaintStyle.Stroke);
        innerBorderPaint.StrokeWidth = INNER_BORDER_WIDTH;
        innerBorderPaint.IsAntialias = true;

        try
        {
            canvas.DrawRoundRect(
                innerRect,
                METER_CORNER_RADIUS - INNER_BORDER_WIDTH,
                METER_CORNER_RADIUS - INNER_BORDER_WIDTH,
                innerBorderPaint);
        }
        finally
        {
            ReturnPaint(innerBorderPaint);
        }
    }

    private void RenderPeakLayer(SKCanvas canvas, MeterData data, QualitySettings settings)
    {
        float peakY = data.MeterRect.Bottom - (data.MeterRect.Height * data.PeakPosition);

        RenderPeakIndicator(canvas, data.MeterRect, peakY);

        if (UseAdvancedEffects && settings.UseMarkers)
            RenderPeakEdgeIndicators(canvas, data.MeterRect, peakY);
    }

    private void RenderPeakIndicator(SKCanvas canvas, SKRect meterRect, float peakY)
    {
        var peakPaint = CreatePaint(
            SKColors.White.WithAlpha(PEAK_ALPHA),
            SKPaintStyle.Fill);
        peakPaint.IsAntialias = true;

        try
        {
            canvas.DrawRect(
                meterRect.Left,
                peakY - PEAK_INDICATOR_HEIGHT / 2,
                meterRect.Width,
                PEAK_INDICATOR_HEIGHT,
                peakPaint);
        }
        finally
        {
            ReturnPaint(peakPaint);
        }

        var borderPaint = CreatePaint(
            SKColors.White.WithAlpha(PEAK_BORDER_ALPHA),
            SKPaintStyle.Stroke);
        borderPaint.StrokeWidth = PEAK_INDICATOR_BORDER_WIDTH;
        borderPaint.IsAntialias = true;

        try
        {
            canvas.DrawLine(
                meterRect.Left,
                peakY,
                meterRect.Right,
                peakY,
                borderPaint);
        }
        finally
        {
            ReturnPaint(borderPaint);
        }
    }

    private void RenderPeakEdgeIndicators(SKCanvas canvas, SKRect meterRect, float peakY)
    {
        var edgePaint = CreatePaint(
            SKColors.White.WithAlpha(EDGE_ALPHA),
            SKPaintStyle.Fill);
        edgePaint.IsAntialias = true;

        try
        {
            canvas.DrawRect(
                meterRect.Left,
                peakY - PEAK_INDICATOR_HEIGHT,
                EDGE_INDICATOR_WIDTH,
                PEAK_INDICATOR_HEIGHT * 2,
                edgePaint);

            canvas.DrawRect(
                meterRect.Right - EDGE_INDICATOR_WIDTH,
                peakY - PEAK_INDICATOR_HEIGHT,
                EDGE_INDICATOR_WIDTH,
                PEAK_INDICATOR_HEIGHT * 2,
                edgePaint);
        }
        finally
        {
            ReturnPaint(edgePaint);
        }
    }

    private void UpdateDimensions(SKImageInfo info)
    {
        if (_currentWidth != info.Width || _currentHeight != info.Height)
        {
            _currentWidth = info.Width;
            _currentHeight = info.Height;
            RequestRedraw();
        }
    }

    private void UpdatePeakPhysics(float loudness, float deltaTime)
    {
        if (loudness > _peakValue + PEAK_CAPTURE_THRESHOLD)
        {
            _peakValue = loudness;
            _peakTimer = _peakHoldTime;
            _peakVelocity = 0f;
        }
        else if (_peakTimer > 0)
        {
            _peakTimer -= deltaTime;
        }
        else
        {
            _peakVelocity += PEAK_GRAVITY;
            _peakVelocity *= PEAK_VELOCITY_DAMPING;

            float newPeakValue = MathF.Max(0, _peakValue - _peakVelocity);
            _peakValue = MathF.Max(newPeakValue, _currentLoudness);
        }
    }

    private float CalculateAndSmoothLoudness(float[] spectrum)
    {
        float rawLoudness = CalculateLoudness(spectrum);

        float effectiveSmoothingFactor = rawLoudness > _currentLoudness
            ? (IsOverlayActive ? SMOOTHING_FACTOR_ATTACK_OVERLAY : SMOOTHING_FACTOR_ATTACK_NORMAL)
            : (IsOverlayActive ? SMOOTHING_FACTOR_RELEASE_OVERLAY : SMOOTHING_FACTOR_RELEASE_NORMAL);

        float smoothedLoudness = Lerp(_currentLoudness, rawLoudness, effectiveSmoothingFactor);
        _currentLoudness = Clamp(smoothedLoudness, 0f, 1f);

        return _currentLoudness;
    }

    private static float CalculateLoudness(float[] spectrum)
    {
        if (spectrum.Length == 0) return 0f;

        float sum = 0f;
        for (int i = 0; i < spectrum.Length; i++)
            sum += MathF.Abs(spectrum[i]);

        return Clamp(sum / spectrum.Length, 0f, 1f);
    }

    private SKShader CreateLoudnessGradientShader(MeterData data, QualitySettings settings)
    {
        byte alpha = CalculateAlpha(settings.GradientAlphaFactor);

        var gradientColors = new[]
        {
            SKColors.Green.WithAlpha(alpha),
            SKColors.Yellow.WithAlpha(alpha),
            SKColors.Red.WithAlpha(alpha)
        };

        float[] colorPositions = UseAdvancedEffects && settings.DynamicGradientFactor > 0
            ? [0f, Clamp(data.Loudness * settings.DynamicGradientFactor, 0.2f, 0.8f), 1.0f]
            : _defaultColorPositions;

        return SKShader.CreateLinearGradient(
            new SKPoint(data.MeterRect.Left, data.MeterRect.Bottom),
            new SKPoint(data.MeterRect.Left, data.MeterRect.Top),
            gradientColors,
            colorPositions,
            SKShaderTileMode.Clamp);
    }

    private static SKColor CalculateGlowColor(float loudness, QualitySettings settings)
    {
        float normalizedLoudness = Clamp(
            (loudness - MEDIUM_LOUDNESS_THRESHOLD) / (1.0f - MEDIUM_LOUDNESS_THRESHOLD),
            0f,
            1f);

        byte interpolatedG = (byte)(255 * (1f - normalizedLoudness));
        var interpolatedColor = new SKColor(255, interpolatedG, 0);
        byte finalAlpha = (byte)(255 * settings.GlowIntensity * normalizedLoudness);

        return interpolatedColor.WithAlpha(finalAlpha);
    }

    protected override int GetMaxBarsForQuality() => Quality switch
    {
        RenderQuality.Low => 32,
        RenderQuality.Medium => 64,
        RenderQuality.High => 128,
        _ => 64
    };

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();

        float smoothingFactor = Quality switch
        {
            RenderQuality.Low => 0.4f,
            RenderQuality.Medium => 0.3f,
            RenderQuality.High => 0.25f,
            _ => 0.3f
        };

        if (IsOverlayActive)
            smoothingFactor *= 1.2f;

        SetProcessingSmoothingFactor(smoothingFactor);

        ResetPeakState();
        RequestRedraw();
    }

    private void ResetPeakState()
    {
        _peakValue = 0f;
        _peakVelocity = 0f;
        _peakTimer = 0f;
    }

    protected override void OnDispose()
    {
        _currentLoudness = 0f;
        _peakValue = 0f;
        _peakVelocity = 0f;
        _peakTimer = 0f;
        _currentWidth = 0;
        _currentHeight = 0;

        base.OnDispose();
    }

    private record MeterData(
        float Loudness,
        float PeakPosition,
        SKRect MeterRect,
        int Width,
        int Height);
}