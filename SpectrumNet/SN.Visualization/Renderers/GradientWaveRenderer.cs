#nullable enable

namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class GradientWaveRenderer : EffectSpectrumRenderer<GradientWaveRenderer.QualitySettings>
{
    private static readonly Lazy<GradientWaveRenderer> _instance =
        new(() => new GradientWaveRenderer());

    public static GradientWaveRenderer GetInstance() => _instance.Value;

    private const float WAVE_HEIGHT = 0.85f,
        WAVE_HEIGHT_OVERLAY = 0.7f,
        WAVE_EDGE_PADDING = 15f,
        WAVE_SMOOTHNESS = 0.25f,
        LINE_WIDTH = 3f,
        LINE_WIDTH_OVERLAY = 2f,
        LINE_OUTLINE_WIDTH = 1f,
        LINE_OUTLINE_ALPHA = 80,
        GLOW_RADIUS = 12f,
        GLOW_RADIUS_OVERLAY = 8f,
        GLOW_ALPHA = 40,
        GLOW_STROKE_MULTIPLIER = 3f,
        FILL_ALPHA = 0.3f,
        FILL_ALPHA_OVERLAY = 0.2f,
        FILL_MID_ALPHA_MULTIPLIER = 0.5f,
        ANIMATION_SPEED = 0.5f,
        ANIMATION_DELTA_TIME = 0.016f,
        ANIMATION_COLOR_CYCLE_SPEED = 0.3f,
        ANIMATION_DASH_SPEED = 10f,
        ANIMATION_DASH_ON = 2f,
        ANIMATION_DASH_OFF = 4f,
        HIGHLIGHT_THRESHOLD = 0.8f,
        HIGHLIGHT_INTENSITY = 1.5f,
        HIGHLIGHT_STROKE_MULTIPLIER = 0.5f,
        RENDER_MIN_MAGNITUDE = 0.01f,
        MATH_RADIANS_TO_DEGREES = 57.2958f,
        MATH_COLOR_PHASE_OFFSET = 0.667f;

    private const int MIN_WAVE_POINTS = 20,
        MAX_WAVE_POINTS_LOW = 40,
        MAX_WAVE_POINTS_MEDIUM = 80,
        MAX_WAVE_POINTS_HIGH = 120,
        GRADIENT_COLOR_COUNT = 3,
        SMOOTHING_WINDOW_SIZE = 4;

    private static readonly SKColor[] _gradientColors =
        [new(100, 200, 255), new(255, 100, 200), new(200, 255, 100)];
    private static readonly float[] _gradientPositions =
        [0f, 0.5f, 1f];

    private float _animationPhase;
    private float _colorPhase;
    private float[] _smoothingBuffer = Array.Empty<float>();

    public sealed class QualitySettings
    {
        public bool UseGlow { get; init; }
        public bool UseOutline { get; init; }
        public bool UseAnimatedColors { get; init; }
        public bool UseHighlight { get; init; }
        public int MaxWavePoints { get; init; }
        public int SmoothingPasses { get; init; }
        public float AnimationSpeed { get; init; }
        public float ResponseFactor { get; init; }
    }

    protected override IReadOnlyDictionary<RenderQuality, QualitySettings>
        QualitySettingsPresets
    { get; } = new Dictionary<RenderQuality, QualitySettings>
    {
        [RenderQuality.Low] = new()
        {
            UseGlow = false,
            UseOutline = false,
            UseAnimatedColors = false,
            UseHighlight = false,
            MaxWavePoints = MAX_WAVE_POINTS_LOW,
            SmoothingPasses = 1,
            AnimationSpeed = 0.8f,
            ResponseFactor = 0.2f
        },
        [RenderQuality.Medium] = new()
        {
            UseGlow = true,
            UseOutline = true,
            UseAnimatedColors = true,
            UseHighlight = true,
            MaxWavePoints = MAX_WAVE_POINTS_MEDIUM,
            SmoothingPasses = 2,
            AnimationSpeed = 1f,
            ResponseFactor = 0.3f
        },
        [RenderQuality.High] = new()
        {
            UseGlow = true,
            UseOutline = true,
            UseAnimatedColors = true,
            UseHighlight = true,
            MaxWavePoints = MAX_WAVE_POINTS_HIGH,
            SmoothingPasses = 2,
            AnimationSpeed = 1.2f,
            ResponseFactor = 0.4f
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
            info);

        if (!ValidateRenderData(renderData))
            return;

        RenderVisualization(
            canvas,
            renderData,
            passedInPaint);
    }

    private RenderData CalculateRenderData(
        float[] spectrum,
        SKImageInfo info)
    {
        UpdateAnimationState();

        var smoothedSpectrum = ApplyAdditionalSmoothing(spectrum);
        var wavePoints = GenerateWavePoints(smoothedSpectrum, info);
        var metrics = CalculateWaveMetrics(smoothedSpectrum);
        var bounds = CalculateWaveBounds(info);

        return new RenderData(
            WavePoints: wavePoints,
            Metrics: metrics,
            Bounds: bounds,
            AnimationPhase: _animationPhase,
            ColorPhase: _colorPhase);
    }

    private static bool ValidateRenderData(RenderData data)
    {
        return data.WavePoints.Count >= MIN_WAVE_POINTS &&
               data.Bounds.Width > 0 &&
               data.Bounds.Height > 0 &&
               data.Metrics.IsValid();
    }

    private void RenderVisualization(
        SKCanvas canvas,
        RenderData data,
        SKPaint basePaint)
    {
        var settings = CurrentQualitySettings!;

        var visibleBounds = data.Bounds;
        if (UseAdvancedEffects && settings.UseGlow)
        {
            float glowRadius = GetAdaptiveParameter(GLOW_RADIUS, GLOW_RADIUS_OVERLAY);
            visibleBounds = ExpandRect(visibleBounds, glowRadius);
        }

        if (!IsAreaVisible(canvas, visibleBounds))
            return;

        RenderWithOverlay(canvas, () =>
        {
            using var wavePath = CreateSmoothWavePath(data.WavePoints);

            RenderFillLayer(canvas, wavePath, data, basePaint, settings);

            if (UseAdvancedEffects && settings.UseGlow)
                RenderGlowLayer(canvas, wavePath, data, basePaint, settings);

            RenderMainWaveLayer(canvas, wavePath, data, basePaint, settings);

            if (settings.UseOutline)
                RenderOutlineLayer(canvas, wavePath, data);

            if (UseAdvancedEffects && settings.UseHighlight && data.Metrics.Peak > HIGHLIGHT_THRESHOLD)
                RenderHighlightLayer(canvas, wavePath, data);
        });
    }

    private void UpdateAnimationState()
    {
        var settings = CurrentQualitySettings!;
        float deltaTime = ANIMATION_DELTA_TIME * settings.AnimationSpeed;

        _animationPhase += ANIMATION_SPEED * deltaTime;
        _colorPhase += ANIMATION_COLOR_CYCLE_SPEED * deltaTime;

        if (_animationPhase > MathF.Tau) _animationPhase -= MathF.Tau;
        if (_colorPhase > MathF.Tau) _colorPhase -= MathF.Tau;
    }

    private float[] ApplyAdditionalSmoothing(float[] spectrum)
    {
        var settings = CurrentQualitySettings!;

        EnsureSmoothingBuffer(spectrum.Length);
        Array.Copy(spectrum, _smoothingBuffer, spectrum.Length);

        for (int pass = 0; pass < settings.SmoothingPasses; pass++)
        {
            ApplySmoothingPass(_smoothingBuffer);
        }

        return _smoothingBuffer;
    }

    private void EnsureSmoothingBuffer(int size)
    {
        if (_smoothingBuffer.Length < size)
            _smoothingBuffer = new float[size];
    }

    private static void ApplySmoothingPass(float[] data)
    {
        if (data.Length < 3) return;

        float prev = data[0];
        data[0] = (data[0] * 3f + data[1]) / SMOOTHING_WINDOW_SIZE;

        for (int i = 1; i < data.Length - 1; i++)
        {
            float current = data[i];
            data[i] = (prev + data[i] * 2f + data[i + 1]) / SMOOTHING_WINDOW_SIZE;
            prev = current;
        }

        data[^1] = (prev + data[^1] * 3f) / SMOOTHING_WINDOW_SIZE;
    }

    private List<SKPoint> GenerateWavePoints(float[] spectrum, SKImageInfo info)
    {
        var settings = CurrentQualitySettings!;
        int pointCount = CalculatePointCount(spectrum.Length, settings.MaxWavePoints);

        var points = new List<SKPoint>(pointCount);
        var bounds = CalculateWaveBounds(info);

        float waveHeight = GetAdaptiveParameter(WAVE_HEIGHT, WAVE_HEIGHT_OVERLAY) * bounds.Height;

        for (int i = 0; i < pointCount; i++)
        {
            float t = i / (float)(pointCount - 1);
            float value = SampleSpectrum(spectrum, t);

            if (value < RENDER_MIN_MAGNITUDE)
                value = 0f;

            float x = bounds.Left + t * bounds.Width;
            float y = bounds.Bottom - value * waveHeight;

            points.Add(new SKPoint(x, y));
        }

        return points;
    }

    private static int CalculatePointCount(int spectrumLength, int maxPoints) =>
        Clamp(spectrumLength, MIN_WAVE_POINTS, maxPoints);

    private static float SampleSpectrum(float[] spectrum, float position)
    {
        float index = position * (spectrum.Length - 1);
        int i0 = (int)index;
        int i1 = Math.Min(i0 + 1, spectrum.Length - 1);
        float t = index - i0;

        return Lerp(spectrum[i0], spectrum[i1], t);
    }

    private static SKRect CalculateWaveBounds(SKImageInfo info) =>
        new(
            WAVE_EDGE_PADDING,
            WAVE_EDGE_PADDING,
            info.Width - WAVE_EDGE_PADDING,
            info.Height - WAVE_EDGE_PADDING);

    private static WaveMetrics CalculateWaveMetrics(float[] spectrum)
    {
        float sum = 0f;
        float max = 0f;
        int validCount = 0;

        foreach (float value in spectrum)
        {
            if (value > RENDER_MIN_MAGNITUDE)
            {
                sum += value;
                max = Max(max, value);
                validCount++;
            }
        }

        float average = validCount > 0 ? sum / validCount : 0f;
        float energy = (average + max) * 0.5f;

        return new WaveMetrics(
            Average: average,
            Peak: max,
            Energy: energy);
    }

    private static SKPath CreateSmoothWavePath(List<SKPoint> points)
    {
        var path = new SKPath();
        if (points.Count < 2) return path;

        path.MoveTo(points[0]);

        for (int i = 0; i < points.Count - 1; i++)
        {
            var p0 = i > 0 ? points[i - 1] : points[i];
            var p1 = points[i];
            var p2 = points[i + 1];
            var p3 = i < points.Count - 2 ? points[i + 2] : p2;

            for (float t = 0; t <= 1; t += WAVE_SMOOTHNESS)
            {
                var point = CatmullRomInterpolate(p0, p1, p2, p3, t);
                path.LineTo(point);
            }
        }

        return path;
    }

    private static SKPoint CatmullRomInterpolate(SKPoint p0, SKPoint p1, SKPoint p2, SKPoint p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;

        float x = 0.5f * (
            2f * p1.X +
            (-p0.X + p2.X) * t +
            (2f * p0.X - 5f * p1.X + 4f * p2.X - p3.X) * t2 +
            (-p0.X + 3f * p1.X - 3f * p2.X + p3.X) * t3);

        float y = 0.5f * (
            2f * p1.Y +
            (-p0.Y + p2.Y) * t +
            (2f * p0.Y - 5f * p1.Y + 4f * p2.Y - p3.Y) * t2 +
            (-p0.Y + 3f * p1.Y - 3f * p2.Y + p3.Y) * t3);

        return new SKPoint(x, y);
    }

    private void RenderFillLayer(
        SKCanvas canvas,
        SKPath wavePath,
        RenderData data,
        SKPaint basePaint,
        QualitySettings settings)
    {
        using var fillPath = new SKPath();
        fillPath.AddPath(wavePath);
        fillPath.LineTo(data.Bounds.Right, data.Bounds.Bottom);
        fillPath.LineTo(data.Bounds.Left, data.Bounds.Bottom);
        fillPath.Close();

        float fillAlpha = GetAdaptiveParameter(FILL_ALPHA, FILL_ALPHA_OVERLAY);
        var fillColor = GetWaveColor(data, basePaint.Color, settings);

        using var gradient = CreateFillGradient(fillColor, data.Bounds, fillAlpha);
        var fillPaint = CreatePaint(SKColors.White, SKPaintStyle.Fill, gradient);

        try
        {
            canvas.DrawPath(fillPath, fillPaint);
        }
        finally
        {
            ReturnPaint(fillPaint);
        }
    }

    private void RenderGlowLayer(
        SKCanvas canvas,
        SKPath wavePath,
        RenderData data,
        SKPaint basePaint,
        QualitySettings settings)
    {
        float glowRadius = GetAdaptiveParameter(GLOW_RADIUS, GLOW_RADIUS_OVERLAY);
        var glowColor = GetWaveColor(data, basePaint.Color, settings);
        byte glowAlpha = (byte)GLOW_ALPHA;

        var glowPaint = CreatePaint(
            glowColor.WithAlpha(glowAlpha),
            SKPaintStyle.Stroke);

        glowPaint.StrokeWidth = GetAdaptiveParameter(LINE_WIDTH, LINE_WIDTH_OVERLAY) * GLOW_STROKE_MULTIPLIER;

        using var blurFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, glowRadius);
        glowPaint.MaskFilter = blurFilter;

        try
        {
            canvas.DrawPath(wavePath, glowPaint);
        }
        finally
        {
            ReturnPaint(glowPaint);
        }
    }

    private void RenderMainWaveLayer(
        SKCanvas canvas,
        SKPath wavePath,
        RenderData data,
        SKPaint basePaint,
        QualitySettings settings)
    {
        var lineColor = GetWaveColor(data, basePaint.Color, settings);

        using var gradient = CreateLineGradient(data, lineColor, settings);
        var linePaint = CreatePaint(SKColors.White, SKPaintStyle.Stroke, gradient);

        linePaint.StrokeWidth = GetAdaptiveParameter(LINE_WIDTH, LINE_WIDTH_OVERLAY);
        linePaint.StrokeCap = SKStrokeCap.Round;
        linePaint.StrokeJoin = SKStrokeJoin.Round;

        try
        {
            canvas.DrawPath(wavePath, linePaint);
        }
        finally
        {
            ReturnPaint(linePaint);
        }
    }

    private void RenderOutlineLayer(
        SKCanvas canvas,
        SKPath wavePath,
        RenderData data)
    {
        var outlinePaint = CreatePaint(
            SKColors.White.WithAlpha((byte)LINE_OUTLINE_ALPHA),
            SKPaintStyle.Stroke);

        outlinePaint.StrokeWidth = LINE_OUTLINE_WIDTH;
        outlinePaint.PathEffect = SKPathEffect.CreateDash(
            [ANIMATION_DASH_ON, ANIMATION_DASH_OFF],
            data.AnimationPhase * ANIMATION_DASH_SPEED);

        try
        {
            canvas.DrawPath(wavePath, outlinePaint);
        }
        finally
        {
            ReturnPaint(outlinePaint);
        }
    }

    private void RenderHighlightLayer(
        SKCanvas canvas,
        SKPath wavePath,
        RenderData data)
    {
        float intensity = (data.Metrics.Peak - HIGHLIGHT_THRESHOLD) * HIGHLIGHT_INTENSITY;
        byte alpha = CalculateAlpha(Clamp(intensity, 0f, 1f));

        var highlightPaint = CreatePaint(
            SKColors.White.WithAlpha(alpha),
            SKPaintStyle.Stroke);

        highlightPaint.StrokeWidth = GetAdaptiveParameter(LINE_WIDTH, LINE_WIDTH_OVERLAY) * HIGHLIGHT_STROKE_MULTIPLIER;
        highlightPaint.BlendMode = SKBlendMode.Screen;

        try
        {
            canvas.DrawPath(wavePath, highlightPaint);
        }
        finally
        {
            ReturnPaint(highlightPaint);
        }
    }

    private static SKShader CreateFillGradient(SKColor baseColor, SKRect bounds, float alpha)
    {
        byte topAlpha = CalculateAlpha(alpha);
        byte midAlpha = CalculateAlpha(alpha * FILL_MID_ALPHA_MULTIPLIER);

        var colors = new[]
        {
        baseColor.WithAlpha(topAlpha),
        baseColor.WithAlpha(midAlpha),
        SKColors.Transparent
        };

        return SKShader.CreateLinearGradient(
            new SKPoint(0, bounds.Top),
            new SKPoint(0, bounds.Bottom),
            colors,
            _gradientPositions,
            SKShaderTileMode.Clamp);
    }

    private static SKShader CreateLineGradient(RenderData data, SKColor baseColor, QualitySettings settings)
    {
        if (!settings.UseAnimatedColors)
            return SKShader.CreateColor(baseColor);

        var colors = new SKColor[GRADIENT_COLOR_COUNT];
        for (int i = 0; i < GRADIENT_COLOR_COUNT; i++)
        {
            float phase = data.ColorPhase + i * MathF.PI * MATH_COLOR_PHASE_OFFSET;
            float hue = (phase * MATH_RADIANS_TO_DEGREES) % 360f;
            colors[i] = SKColor.FromHsl(hue, 90f, 60f);
        }

        return SKShader.CreateLinearGradient(
            new SKPoint(data.Bounds.Left, 0),
            new SKPoint(data.Bounds.Right, 0),
            colors,
            _gradientPositions,
            SKShaderTileMode.Clamp);
    }

    private static SKColor GetWaveColor(RenderData data, SKColor baseColor, QualitySettings settings)
    {
        if (!settings.UseAnimatedColors)
            return baseColor;

        float t = (data.ColorPhase / MathF.Tau + data.Metrics.Energy) % 1f;
        return InterpolateGradientColor(t);
    }

    private static SKColor InterpolateGradientColor(float t)
    {
        float scaledT = t * (_gradientColors.Length - 1);
        int index = (int)scaledT;
        float localT = scaledT - index;

        if (index >= _gradientColors.Length - 1)
            return _gradientColors[^1];

        var from = _gradientColors[index];
        var to = _gradientColors[index + 1];

        return new SKColor(
            (byte)Lerp(from.Red, to.Red, localT),
            (byte)Lerp(from.Green, to.Green, localT),
            (byte)Lerp(from.Blue, to.Blue, localT));
    }

    private float GetAdaptiveParameter(float normalValue, float overlayValue) =>
        IsOverlayActive ? overlayValue : normalValue;

    private static SKRect ExpandRect(SKRect rect, float amount) =>
        new(rect.Left - amount,
            rect.Top - amount,
            rect.Right + amount,
            rect.Bottom + amount);

    protected override int GetMaxBarsForQuality() => Quality switch
    {
        RenderQuality.Low => 64,
        RenderQuality.Medium => 128,
        RenderQuality.High => 256,
        _ => 128
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

        _smoothingBuffer = [];

        RequestRedraw();
    }

    protected override void CleanupUnusedResources()
    {
        base.CleanupUnusedResources();

        if (_smoothingBuffer.Length > GetMaxBarsForQuality() * 2)
            _smoothingBuffer = [];
    }

    protected override void OnDispose()
    {
        _animationPhase = 0f;
        _colorPhase = 0f;
        _smoothingBuffer = [];
        base.OnDispose();
    }

    private record RenderData(
        List<SKPoint> WavePoints,
        WaveMetrics Metrics,
        SKRect Bounds,
        float AnimationPhase,
        float ColorPhase);

    private record WaveMetrics(
        float Average,
        float Peak,
        float Energy)
    {
        public bool IsValid() => Average >= 0f && Peak >= 0f && Energy >= 0f;
    }
}