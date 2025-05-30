#nullable enable

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class KenwoodBarsRenderer : EffectSpectrumRenderer<KenwoodBarsRenderer.QualitySettings>
{
    private static readonly Lazy<KenwoodBarsRenderer> _instance =
        new(() => new KenwoodBarsRenderer());

    public static KenwoodBarsRenderer GetInstance() => _instance.Value;

    private const float 
        PEAK_FALL_SPEED = 0.25f,
        PEAK_HEIGHT = 3f,
        PEAK_HEIGHT_OVERLAY = 2f,
        PEAK_HOLD_TIME_MS = 300f,
        PEAK_FALL_FIXED_DELTA_TIME = 1f / 60f;

    private const float 
        MIN_BAR_HEIGHT = 2f,
        MIN_MAGNITUDE_FOR_RENDER = 0.01f;

    private const float 
        CORNER_RADIUS_RATIO = 0.25f,
        CORNER_RADIUS_RATIO_OVERLAY = 0.2f;

    private const float 
        OUTLINE_WIDTH = 1.5f,
        OUTLINE_WIDTH_OVERLAY = 1f,
        OUTLINE_ALPHA = 0.5f,
        OUTLINE_ALPHA_OVERLAY = 0.35f,
        PEAK_OUTLINE_ALPHA = 0.7f,
        PEAK_OUTLINE_ALPHA_OVERLAY = 0.5f;

    private const float 
        GRADIENT_INTENSITY_BOOST = 1.1f,
        GRADIENT_INTENSITY_BOOST_OVERLAY = 0.95f;

    private static readonly SKColor[] _barColors =
    [
        new(0, 240, 120),
        new(0, 255, 0),
        new(255, 235, 0),
        new(255, 185, 0),
        new(255, 85, 0),
        new(255, 35, 0)
    ];

    private static readonly float[] _barColorPositions =
        [0f, 0.55f, 0.55f, 0.8f, 0.8f, 1f];

    private static readonly SKColor _peakColor = SKColors.White;
    private static readonly SKColor _peakOutlineColor = new(255, 255, 255, 200);

    private float[] _peaks = [];
    private float[] _peakTimers = [];
    private readonly float _peakHoldTime = PEAK_HOLD_TIME_MS / 1000f;
    private SKShader? _barGradient;
    private float _cachedGradientHeight;

    public sealed class QualitySettings
    {
        public bool UseGradient { get; init; }
        public bool UseRoundCorners { get; init; }
        public bool UseOutline { get; init; }
        public bool UseEnhancedPeaks { get; init; }
        public float SmoothingFactorOverride { get; init; }
    }

    protected override IReadOnlyDictionary<RenderQuality, QualitySettings>
        QualitySettingsPresets
    { get; } = new Dictionary<RenderQuality, QualitySettings>
    {
        [RenderQuality.Low] = new()
        {
            UseGradient = true,
            UseRoundCorners = false,
            UseOutline = false,
            UseEnhancedPeaks = false,
            SmoothingFactorOverride = 0.3f
        },
        [RenderQuality.Medium] = new()
        {
            UseGradient = true,
            UseRoundCorners = true,
            UseOutline = true,
            UseEnhancedPeaks = true,
            SmoothingFactorOverride = 0.25f
        },
        [RenderQuality.High] = new()
        {
            UseGradient = true,
            UseRoundCorners = true,
            UseOutline = true,
            UseEnhancedPeaks = true,
            SmoothingFactorOverride = 0.2f
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

        var renderData = CalculateRenderData(
            processedSpectrum,
            info,
            renderParams);

        if (!ValidateRenderData(renderData))
            return;

        RenderVisualization(
            canvas,
            renderData,
            renderParams,
            passedInPaint);
    }

    private RenderData CalculateRenderData(
        float[] spectrum,
        SKImageInfo info,
        RenderParameters renderParams)
    {
        InvalidateGradientIfNeeded(info.Height);
        EnsurePeakArraySize(renderParams.EffectiveBarCount);

        var bars = new List<BarData>(renderParams.EffectiveBarCount);
        var peaks = new List<PeakData>(renderParams.EffectiveBarCount);
        float xPosition = renderParams.StartOffset;
        float peakHeight = GetAdaptivePeakHeight();

        for (int i = 0; i < renderParams.EffectiveBarCount && i < spectrum.Length; i++)
        {
            float magnitude = Max(spectrum[i], 0f);
            UpdatePeak(i, magnitude, PEAK_FALL_FIXED_DELTA_TIME);

            if (magnitude > MIN_MAGNITUDE_FOR_RENDER)
            {
                var rect = GetBarRect(
                    xPosition,
                    magnitude,
                    renderParams.BarWidth,
                    info.Height,
                    MIN_BAR_HEIGHT);

                bars.Add(new BarData(
                    Rect: rect,
                    Magnitude: magnitude,
                    Index: i));
            }

            float peakValue = GetPeakValue(i);
            if (peakValue > MIN_MAGNITUDE_FOR_RENDER)
            {
                float peakY = info.Height - (peakValue * info.Height);

                var peakRect = new SKRect(
                    xPosition,
                    Max(0, peakY - peakHeight),
                    xPosition + renderParams.BarWidth,
                    Max(0, peakY));

                if (peakRect.Height > 0)
                    peaks.Add(new PeakData(
                        Rect: peakRect,
                        Value: peakValue,
                        Index: i));
            }

            xPosition += renderParams.BarWidth + renderParams.BarSpacing;
        }

        return new RenderData(
            Bars: bars,
            Peaks: peaks,
            BoundingBox: CalculateBoundingBox(bars, peaks),
            AverageIntensity: CalculateAverageIntensity(spectrum));
    }

    private static bool ValidateRenderData(RenderData data) =>
        data.BoundingBox.Width > 0 && data.BoundingBox.Height > 0;

    private void RenderVisualization(
        SKCanvas canvas,
        RenderData data,
        RenderParameters renderParams,
        SKPaint basePaint)
    {
        var settings = CurrentQualitySettings!;

        if (!IsAreaVisible(canvas, data.BoundingBox))
            return;

        RenderWithOverlay(canvas, () =>
        {
            RenderMainLayer(canvas, data, renderParams, settings);

            if (UseAdvancedEffects && settings.UseOutline)
                RenderOutlineLayer(canvas, data, renderParams, settings);

            RenderPeakLayer(canvas, data, renderParams, settings);

            if (UseAdvancedEffects && settings.UseEnhancedPeaks)
                RenderPeakEnhancementLayer(canvas, data, renderParams, settings);
        });
    }

    private void RenderMainLayer(
        SKCanvas canvas,
        RenderData data,
        RenderParameters renderParams,
        QualitySettings settings)
    {
        if (data.Bars.Count == 0)
            return;

        var barPaint = CreatePaint(SKColors.White, SKPaintStyle.Fill);

        if (settings.UseGradient)
        {
            float intensityBoost = GetAdaptiveParameter(
                GRADIENT_INTENSITY_BOOST,
                GRADIENT_INTENSITY_BOOST_OVERLAY);

            barPaint.Shader = GetOrCreateBarGradient(
                data.BoundingBox.Height,
                intensityBoost);
        }
        else
        {
            barPaint.Color = _barColors[0];
        }

        try
        {
            float cornerRadius = settings.UseRoundCorners
                ? GetAdaptiveCornerRadius(renderParams)
                : 0f;

            if (cornerRadius > 0)
            {
                RenderPath(canvas, path =>
                {
                    foreach (var bar in data.Bars)
                        path.AddRoundRect(bar.Rect, cornerRadius, cornerRadius);
                }, barPaint);
            }
            else
            {
                var rects = data.Bars.Select(b => b.Rect).ToList();
                RenderRects(canvas, rects, barPaint, 0);
            }
        }
        finally
        {
            ReturnPaint(barPaint);
        }
    }

    private void RenderOutlineLayer(
        SKCanvas canvas,
        RenderData data,
        RenderParameters renderParams,
        QualitySettings settings)
    {
        float outlineWidth = GetAdaptiveParameter(
            OUTLINE_WIDTH,
            OUTLINE_WIDTH_OVERLAY);

        float baseAlpha = GetAdaptiveParameter(
            OUTLINE_ALPHA,
            OUTLINE_ALPHA_OVERLAY);

        var outlinePaint = CreatePaint(
            SKColors.White,
            SKPaintStyle.Stroke);

        outlinePaint.StrokeWidth = outlineWidth;

        try
        {
            float cornerRadius = settings.UseRoundCorners
                ? GetAdaptiveCornerRadius(renderParams)
                : 0f;

            foreach (var bar in data.Bars)
            {
                byte alpha = (byte)(CalculateAlpha(bar.Magnitude) * baseAlpha);
                outlinePaint.Color = SKColors.White.WithAlpha(alpha);

                if (cornerRadius > 0)
                    canvas.DrawRoundRect(
                        bar.Rect,
                        cornerRadius,
                        cornerRadius,
                        outlinePaint);
                else
                    canvas.DrawRect(bar.Rect, outlinePaint);
            }
        }
        finally
        {
            ReturnPaint(outlinePaint);
        }
    }

    private void RenderPeakLayer(
        SKCanvas canvas,
        RenderData data,
        RenderParameters renderParams,
        QualitySettings settings)
    {
        if (data.Peaks.Count == 0)
            return;

        var peakPaint = CreatePaint(_peakColor, SKPaintStyle.Fill);

        try
        {
            if (settings.UseRoundCorners)
            {
                float cornerRadius = GetAdaptiveCornerRadius(renderParams) * 0.5f;
                RenderPath(canvas, path =>
                {
                    foreach (var peak in data.Peaks)
                        path.AddRoundRect(peak.Rect, cornerRadius, cornerRadius);
                }, peakPaint);
            }
            else
            {
                var peakRects = data.Peaks.Select(p => p.Rect).ToList();
                RenderRects(canvas, peakRects, peakPaint, 0);
            }
        }
        finally
        {
            ReturnPaint(peakPaint);
        }
    }

    private void RenderPeakEnhancementLayer(
        SKCanvas canvas,
        RenderData data,
        RenderParameters renderParams,
        QualitySettings settings)
    {
        float outlineWidth = GetAdaptiveParameter(
            OUTLINE_WIDTH,
            OUTLINE_WIDTH_OVERLAY) * 0.75f;

        float baseAlpha = GetAdaptiveParameter(
            PEAK_OUTLINE_ALPHA,
            PEAK_OUTLINE_ALPHA_OVERLAY);

        var outlinePaint = CreatePaint(
            _peakOutlineColor.WithAlpha((byte)(baseAlpha * 255)),
            SKPaintStyle.Stroke);

        outlinePaint.StrokeWidth = outlineWidth;

        try
        {
            foreach (var peak in data.Peaks)
            {
                canvas.DrawLine(
                    peak.Rect.Left,
                    peak.Rect.Top,
                    peak.Rect.Right,
                    peak.Rect.Top,
                    outlinePaint);

                canvas.DrawLine(
                    peak.Rect.Left,
                    peak.Rect.Bottom,
                    peak.Rect.Right,
                    peak.Rect.Bottom,
                    outlinePaint);
            }
        }
        finally
        {
            ReturnPaint(outlinePaint);
        }
    }

    private float GetAdaptiveParameter(float normalValue, float overlayValue) =>
        IsOverlayActive ? overlayValue : normalValue;

    private float GetAdaptiveCornerRadius(RenderParameters renderParams) =>
        renderParams.BarWidth * GetAdaptiveParameter(
            CORNER_RADIUS_RATIO,
            CORNER_RADIUS_RATIO_OVERLAY);

    private float GetAdaptivePeakHeight() =>
        GetAdaptiveParameter(PEAK_HEIGHT, PEAK_HEIGHT_OVERLAY);

    private static SKRect CalculateBoundingBox(
        List<BarData> bars,
        List<PeakData> peaks)
    {
        if (bars.Count == 0 && peaks.Count == 0)
            return SKRect.Empty;

        var allRects = bars.Select(b => b.Rect)
            .Concat(peaks.Select(p => p.Rect))
            .ToList();

        return allRects.Aggregate((acc, r) => SKRect.Union(acc, r));
    }

    private static float CalculateAverageIntensity(float[] spectrum)
    {
        if (spectrum.Length == 0) 
            return 0f;

        float sum = 0f;
        for (int i = 0; i < spectrum.Length; i++)
            sum += spectrum[i];

        return sum / spectrum.Length;
    }

    private void UpdatePeak(int index, float value, float deltaTime)
    {
        if (index < 0 || index >= _peaks.Length)
            return;

        if (value > _peaks[index])
        {
            _peaks[index] = value;
            _peakTimers[index] = _peakHoldTime;
        }
        else if (_peakTimers[index] > 0)
        {
            _peakTimers[index] -= deltaTime;
        }
        else
        {
            _peaks[index] = Max(0,
                _peaks[index] - PEAK_FALL_SPEED * deltaTime);
        }
    }

    private float GetPeakValue(int index) =>
        index >= 0 && index < _peaks.Length ? _peaks[index] : 0f;

    private void EnsurePeakArraySize(int size)
    {
        if (size <= 0)
            size = 1;

        if (_peaks.Length != size)
        {
            Array.Resize(ref _peaks, size);
            Array.Resize(ref _peakTimers, size);
            Array.Fill(_peaks, 0f);
            Array.Fill(_peakTimers, 0f);
        }
    }

    private void InvalidateGradientIfNeeded(float newHeight)
    {
        if (Abs(_cachedGradientHeight - newHeight) > 0.01f)
        {
            _barGradient?.Dispose();
            _barGradient = null;
            _cachedGradientHeight = newHeight;
        }
    }

    private SKShader GetOrCreateBarGradient(float height, float intensityBoost)
    {
        if (height <= 0)
            height = 1;

        if (_barGradient == null || Abs(_cachedGradientHeight - height) > 0.01f)
        {
            _barGradient?.Dispose();
            _cachedGradientHeight = height;

            var adjustedColors = _barColors
                .Select(c => new SKColor(
                    (byte)Min(255, c.Red * intensityBoost),
                    (byte)Min(255, c.Green * intensityBoost),
                    (byte)Min(255, c.Blue * intensityBoost),
                    c.Alpha))
                .ToArray();

            _barGradient = SKShader.CreateLinearGradient(
                new SKPoint(0, height),
                new SKPoint(0, 0),
                adjustedColors,
                _barColorPositions,
                SKShaderTileMode.Clamp);
        }

        return _barGradient!;
    }

    protected override int GetMaxBarsForQuality() => Quality switch
    {
        RenderQuality.Low => 50,
        RenderQuality.Medium => 75,
        RenderQuality.High => 100,
        _ => 75
    };

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();

        float smoothingFactor = Quality switch
        {
            RenderQuality.Low => 0.3f,
            RenderQuality.Medium => 0.25f,
            RenderQuality.High => 0.2f,
            _ => 0.25f
        };

        if (IsOverlayActive)
            smoothingFactor *= 1.2f;
        
        SetProcessingSmoothingFactor(smoothingFactor);

        var tempRenderParams = CalculateRenderParameters(
            new SKImageInfo(100, 100),
            GetMaxBarsForQuality(),0f,0f);

        EnsurePeakArraySize(tempRenderParams.EffectiveBarCount);

        InvalidateGradientIfNeeded(0);

        RequestRedraw();
    }

    protected override void OnDispose()
    {
        _peaks = [];
        _peakTimers = [];
        _barGradient?.Dispose();
        _barGradient = null;
        _cachedGradientHeight = 0;
        base.OnDispose();
    }

    private record RenderData(
        List<BarData> Bars,
        List<PeakData> Peaks,
        SKRect BoundingBox,
        float AverageIntensity);

    private record BarData(
        SKRect Rect,
        float Magnitude,
        int Index);

    private record PeakData(
        SKRect Rect,
        float Value,
        int Index);
}