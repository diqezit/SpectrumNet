#nullable enable

using static SpectrumNet.Views.Renderers.LedMeterRenderer.Constants;
using static SpectrumNet.Views.Renderers.LedMeterRenderer.Constants.Quality;

namespace SpectrumNet.Views.Renderers;

public sealed class LedMeterRenderer : EffectSpectrumRenderer
{
    private static readonly Lazy<LedMeterRenderer> _instance = new(() => new LedMeterRenderer());

    private LedMeterRenderer() { }

    public static LedMeterRenderer GetInstance() => _instance.Value;

    public record Constants
    {
        public const string LOG_PREFIX = "LedMeterRenderer";

        public const float
            ANIMATION_SPEED = 0.015f,
            SMOOTHING_FACTOR_NORMAL = 0.3f,
            SMOOTHING_FACTOR_OVERLAY = 0.5f,
            PEAK_DECAY_RATE = 0.04f,
            GLOW_INTENSITY = 0.3f;

        public const float
            MIN_LOUDNESS_THRESHOLD = 0.001f,
            HIGH_LOUDNESS_THRESHOLD = 0.7f,
            MEDIUM_LOUDNESS_THRESHOLD = 0.4f;

        public const int DEFAULT_LED_COUNT = 22;
        public const float LED_SPACING = 0.1f;

        public const float
            LED_ROUNDING_RADIUS = 2.5f,
            PANEL_PADDING = 12f,
            TICK_MARK_WIDTH = 22f,
            BEVEL_SIZE = 3f,
            CORNER_RADIUS = 14f;

        public const int PERFORMANCE_INFO_BOTTOM_MARGIN = 30;

        public const int
            SCREW_TEXTURE_SIZE = 24,
            BRUSHED_METAL_TEXTURE_SIZE = 100;

        public static class Quality
        {
            public const bool
                LOW_USE_ADVANCED_EFFECTS = false,
                MEDIUM_USE_ADVANCED_EFFECTS = true,
                HIGH_USE_ADVANCED_EFFECTS = true;

            public const bool
                LOW_USE_ANTIALIASING = false,
                MEDIUM_USE_ANTIALIASING = true,
                HIGH_USE_ANTIALIASING = true;

            public const SKFilterMode
                LOW_FILTER_MODE = SKFilterMode.Nearest,
                MEDIUM_FILTER_MODE = SKFilterMode.Linear,
                HIGH_FILTER_MODE = SKFilterMode.Linear;

            public const SKMipmapMode
                LOW_MIPMAP_MODE = SKMipmapMode.None,
                MEDIUM_MIPMAP_MODE = SKMipmapMode.Linear,
                HIGH_MIPMAP_MODE = SKMipmapMode.Linear;
        }
    }

    private float
        _animationPhase,
        _vibrationOffset,
        _previousLoudness,
        _peakLoudness;

    private float? _cachedLoudness;

    private float[] _ledAnimationPhases = [];

    private int
        _currentWidth,
        _currentHeight,
        _ledCount = DEFAULT_LED_COUNT;

    private SKImageInfo _lastImageInfo;

    private readonly SKPath _ledPath = new();
    private readonly SKPath _highlightPath = new();
    private readonly float[] _screwAngles = [45f, 120f, 10f, 80f];
    private SKBitmap? _screwBitmap;
    private SKBitmap? _brushedMetalBitmap;
    private readonly List<float> _ledVariations = new(30);
    private readonly List<SKColor> _ledColorVariations = new(30);

    private readonly object _loudnessLock = new();
    private volatile bool _isConfiguring;

    protected override void OnInitialize()
    {
        ExecuteSafely(
            () =>
            {
                base.OnInitialize();
                InitializeResources();
                InitializeQualityParams();
                Log(LogLevel.Debug, LOG_PREFIX, "Initialized");
            },
            nameof(OnInitialize),
            "Failed to initialize renderer"
        );
    }

    private void InitializeResources()
    {
        ExecuteSafely(
            () =>
            {
                InitializeVariationsAndTextures();
                CreateCachedResources();
                ResetState();
            },
            nameof(InitializeResources),
            "Failed to initialize renderer resources"
        );
    }

    private void InitializeQualityParams()
    {
        ExecuteSafely(
            () =>
            {
                ApplyQualitySettingsInternal();
            },
            nameof(InitializeQualityParams),
            "Failed to initialize quality parameters"
        );
    }

    private void ResetState()
    {
        _animationPhase = 0f;
        _vibrationOffset = 0f;
        _previousLoudness = 0f;
        _peakLoudness = 0f;
        _cachedLoudness = null;
        _currentWidth = 0;
        _currentHeight = 0;
    }

    private void InitializeVariationsAndTextures()
    {
        Random fixedRandom = new(42);
        InitializeLedVariations(fixedRandom);
        InitializeColorVariations(fixedRandom);
    }

    private void InitializeLedVariations(Random random)
    {
        _ledVariations.Clear();
        for (int i = 0; i < 30; i++)
        {
            _ledVariations.Add(0.85f + (float)random.NextDouble() * 0.3f);
        }
    }

    private void InitializeColorVariations(Random random)
    {
        _ledColorVariations.Clear();

        SKColor greenBase = new(30, 200, 30);
        SKColor yellowBase = new(220, 200, 0);
        SKColor redBase = new(230, 30, 30);

        GenerateColorVariations(random, greenBase, 10);
        GenerateColorVariations(random, yellowBase, 10);
        GenerateColorVariations(random, redBase, 10);
    }

    private void GenerateColorVariations(Random random, SKColor baseColor, int count)
    {
        for (int j = 0; j < count; j++)
        {
            _ledColorVariations.Add(new SKColor(
                (byte)Clamp(baseColor.Red + random.Next(-10, 10), 0, 255),
                (byte)Clamp(baseColor.Green + random.Next(-10, 10), 0, 255),
                (byte)Clamp(baseColor.Blue + random.Next(-10, 10), 0, 255)
            ));
        }
    }

    private void CreateCachedResources()
    {
        _screwBitmap = CreateScrewTexture();
        _brushedMetalBitmap = CreateBrushedMetalTexture();
    }

    private static SKBitmap CreateScrewTexture()
    {
        var bitmap = new SKBitmap(SCREW_TEXTURE_SIZE, SCREW_TEXTURE_SIZE);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        DrawScrewBase(canvas);
        DrawScrewSlot(canvas);
        DrawScrewHighlight(canvas);
        DrawScrewOutline(canvas);

        return bitmap;
    }

    private static void DrawScrewBase(SKCanvas canvas)
    {
        using var circlePaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        SKColor[] gradientColors = [new SKColor(220, 220, 220), new SKColor(140, 140, 140)];
        float[] positions = null!;

        circlePaint.Shader = SKShader.CreateLinearGradient(
            new SKPoint(4, 4),
            new SKPoint(20, 20),
            gradientColors,
            positions,
            SKShaderTileMode.Clamp
        );

        canvas.DrawCircle(12, 12, 10, circlePaint);
    }

    private static void DrawScrewSlot(SKCanvas canvas)
    {
        using var slotPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.5f,
            Color = new SKColor(50, 50, 50, 180)
        };

        canvas.DrawLine(7, 12, 17, 12, slotPaint);
    }

    private static void DrawScrewHighlight(SKCanvas canvas)
    {
        using var highlightPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            Color = new SKColor(255, 255, 255, 100)
        };

        canvas.DrawArc(new SKRect(4, 4, 20, 20), 200, 160, false, highlightPaint);
    }

    private static void DrawScrewOutline(SKCanvas canvas)
    {
        using var shadowPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f,
            Color = new SKColor(0, 0, 0, 100)
        };

        canvas.DrawCircle(12, 12, 9, shadowPaint);
    }

    private SKBitmap CreateBrushedMetalTexture()
    {
        var bitmap = new SKBitmap(BRUSHED_METAL_TEXTURE_SIZE, BRUSHED_METAL_TEXTURE_SIZE);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(190, 190, 190));

        DrawBrushedMetalLines(canvas);
        ApplyBrushedMetalGradient(canvas);

        return bitmap;
    }

    private void DrawBrushedMetalLines(SKCanvas canvas)
    {
        Random texRandom = new(42);
        var paintPool = _paintPool;
        if (paintPool == null) return;

        using var linePaint = paintPool.Get();
        if (linePaint == null) return;

        linePaint.IsAntialias = false;
        linePaint.StrokeWidth = 1;

        DrawMetalLineSet(canvas, texRandom, linePaint, 150, new SKColor(210, 210, 210), 10, 20);
        DrawMetalLineSet(canvas, texRandom, linePaint, 30, new SKColor(100, 100, 100), 5, 10);
    }

    private static void DrawMetalLineSet(
        SKCanvas canvas,
        Random random,
        SKPaint paint,
        int count,
        SKColor baseColor,
        int minAlpha,
        int maxAlpha)
    {
        for (int i = 0; i < count; i++)
        {
            float y = (float)random.NextDouble() * BRUSHED_METAL_TEXTURE_SIZE;
            paint.Color = baseColor.WithAlpha((byte)random.Next(minAlpha, maxAlpha));
            canvas.DrawLine(0, y, BRUSHED_METAL_TEXTURE_SIZE, y, paint);
        }
    }

    private static void ApplyBrushedMetalGradient(SKCanvas canvas)
    {
        using var gradientPaint = new SKPaint();

        SKColor[] colors = [new SKColor(255, 255, 255, 20), new SKColor(0, 0, 0, 20)];
        float[] positions = null!;

        gradientPaint.Shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(BRUSHED_METAL_TEXTURE_SIZE, BRUSHED_METAL_TEXTURE_SIZE),
            colors,
            positions,
            SKShaderTileMode.Clamp
        );

        canvas.DrawRect(0, 0, BRUSHED_METAL_TEXTURE_SIZE, BRUSHED_METAL_TEXTURE_SIZE, gradientPaint);
    }

    public override void Configure(
        bool isOverlayActive,
        RenderQuality quality)
    {
        ExecuteSafely(
            () =>
            {
                if (_isConfiguring) return;

                try
                {
                    _isConfiguring = true;
                    bool configChanged = _isOverlayActive != isOverlayActive
                                         || Quality != quality;

                    _isOverlayActive = isOverlayActive;
                    Quality = quality;
                    _smoothingFactor = isOverlayActive ?
                        SMOOTHING_FACTOR_OVERLAY :
                        SMOOTHING_FACTOR_NORMAL;

                    if (configChanged)
                    {
                        ApplyQualitySettingsInternal();
                        OnConfigurationChanged();
                    }
                }
                finally
                {
                    _isConfiguring = false;
                }
            },
            nameof(Configure),
            "Failed to configure renderer"
        );
    }

    protected override void OnConfigurationChanged()
    {
        ExecuteSafely(
            () =>
            {
                Log(LogLevel.Information,
                    LOG_PREFIX,
                    $"Configuration changed. New Quality: {Quality}, Overlay: {_isOverlayActive}");
            },
            nameof(OnConfigurationChanged),
            "Failed to apply configuration changes"
        );
    }

    protected override void ApplyQualitySettings()
    {
        ExecuteSafely(
            () =>
            {
                if (_isConfiguring) return;

                try
                {
                    _isConfiguring = true;
                    base.ApplyQualitySettings();
                    ApplyQualitySettingsInternal();
                }
                finally
                {
                    _isConfiguring = false;
                }
            },
            nameof(ApplyQualitySettings),
            "Failed to apply quality settings"
        );
    }

    private void ApplyQualitySettingsInternal()
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

        Log(LogLevel.Debug, LOG_PREFIX,
            $"Quality settings applied. Quality: {Quality}, " +
            $"AntiAlias: {UseAntiAlias}, AdvancedEffects: {UseAdvancedEffects}");
    }

    private void LowQualitySettings()
    {
        base._useAntiAlias = LOW_USE_ANTIALIASING;
        base._samplingOptions = new SKSamplingOptions(
            LOW_FILTER_MODE,
            LOW_MIPMAP_MODE);
        base._useAdvancedEffects = LOW_USE_ADVANCED_EFFECTS;
    }

    private void MediumQualitySettings()
    {
        base._useAntiAlias = MEDIUM_USE_ANTIALIASING;
        base._samplingOptions = new SKSamplingOptions(
            MEDIUM_FILTER_MODE,
            MEDIUM_MIPMAP_MODE);
        base._useAdvancedEffects = MEDIUM_USE_ADVANCED_EFFECTS;
    }

    private void HighQualitySettings()
    {
        base._useAntiAlias = HIGH_USE_ANTIALIASING;
        base._samplingOptions = new SKSamplingOptions(
            HIGH_FILTER_MODE,
            HIGH_MIPMAP_MODE);
        base._useAdvancedEffects = HIGH_USE_ADVANCED_EFFECTS;
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
        if (!ValidateRenderParameters(canvas, spectrum, info, paint))
            return;

        if (canvas.QuickReject(new SKRect(0, 0, info.Width, info.Height)))
            return;

        ExecuteSafely(
            () =>
            {
                UpdateState(info, spectrum);
                RenderFrame(canvas, info);
            },
            nameof(RenderEffect),
            "Error in RenderEffect method"
        );
    }

    private void UpdateState(SKImageInfo info, float[] spectrum)
    {
        ExecuteSafely(
            () =>
            {
                _lastImageInfo = info;

                if (_currentWidth != info.Width || _currentHeight != info.Height)
                {
                    UpdateDimensions(info);
                }

                UpdateAnimationState(spectrum);
            },
            nameof(UpdateState),
            "Error updating state"
        );
    }

    private void RenderFrame(SKCanvas canvas, SKImageInfo info)
    {
        ExecuteSafely(
            () =>
            {
                float loudness = GetCurrentLoudness();
                RenderMeter(canvas, info, loudness, _peakLoudness);
            },
            nameof(RenderFrame),
            "Error rendering frame"
        );
    }

    private float GetCurrentLoudness()
    {
        lock (_loudnessLock)
        {
            return _cachedLoudness ?? 0f;
        }
    }

    private void UpdateAnimationState(float[] spectrum)
    {
        float loudness = CalculateAndSmoothLoudness(spectrum);
        _cachedLoudness = loudness;

        UpdateAnimationPhase();
        UpdatePeakLoudness(loudness);
        UpdateVibrationEffect(loudness);
    }

    private void UpdateDimensions(SKImageInfo info)
    {
        _currentWidth = info.Width;
        _currentHeight = info.Height;

        float panelHeight = info.Height - PANEL_PADDING * 2;
        _ledCount = Max(10, Min(DEFAULT_LED_COUNT, (int)(panelHeight / 12)));

        InitializeLedAnimationPhases();
    }

    private void InitializeLedAnimationPhases()
    {
        _ledAnimationPhases = new float[_ledCount];
        Random phaseRandom = new(42);

        for (int i = 0; i < _ledCount; i++)
        {
            _ledAnimationPhases[i] = (float)phaseRandom.NextDouble();
        }
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
        Log(LogLevel.Warning, LOG_PREFIX, "Empty spectrum data");
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

    private void UpdateAnimationPhase()
    {
        _animationPhase = (_animationPhase + ANIMATION_SPEED) % 1.0f;
    }

    private void UpdatePeakLoudness(float loudness)
    {
        if (loudness > _peakLoudness)
        {
            _peakLoudness = loudness;
        }
        else
        {
            _peakLoudness = Max(0, _peakLoudness - PEAK_DECAY_RATE);
        }
    }

    private void UpdateVibrationEffect(float loudness)
    {
        if (loudness > HIGH_LOUDNESS_THRESHOLD)
        {
            float vibrationIntensity = (loudness - HIGH_LOUDNESS_THRESHOLD) / (1 - HIGH_LOUDNESS_THRESHOLD);
            _vibrationOffset = (float)Sin(_animationPhase * PI * 10) * 0.8f * vibrationIntensity;
        }
        else
        {
            _vibrationOffset = 0;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float CalculateAndSmoothLoudness(float[] spectrum)
    {
        float rawLoudness = CalculateLoudness(spectrum.AsSpan());
        float smoothedLoudness = _previousLoudness + (rawLoudness - _previousLoudness) * _smoothingFactor;
        smoothedLoudness = Clamp(smoothedLoudness, MIN_LOUDNESS_THRESHOLD, 1f);
        _previousLoudness = smoothedLoudness;
        return smoothedLoudness;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static float CalculateLoudness(ReadOnlySpan<float> spectrum)
    {
        if (spectrum.IsEmpty)
            return 0f;

        float sum = 0f;
        int vectorSize = Vector<float>.Count;
        int i = 0;

        if (System.Numerics.Vector.IsHardwareAccelerated && spectrum.Length >= vectorSize)
        {
            sum = CalculateLoudnessVectorized(spectrum, vectorSize, ref i);
        }

        for (; i < spectrum.Length; i++)
        {
            sum += Abs(spectrum[i]);
        }

        return Clamp(sum / spectrum.Length, 0f, 1f);
    }

    private static float CalculateLoudnessVectorized(ReadOnlySpan<float> spectrum, int vectorSize, ref int i)
    {
        Vector<float> sumVector = Vector<float>.Zero;
        int vectorizedLength = spectrum.Length - spectrum.Length % vectorSize;

        for (; i < vectorizedLength; i += vectorSize)
        {
            var values = new Vector<float>(spectrum.Slice(i, vectorSize));
            values = System.Numerics.Vector.Abs(values);
            sumVector += values;
        }

        float sum = 0f;
        for (int j = 0; j < vectorSize; j++)
        {
            sum += sumVector[j];
        }

        return sum;
    }

    private void RenderMeter(
        SKCanvas canvas,
        SKImageInfo info,
        float loudness,
        float peakLoudness)
    {
        if (loudness < MIN_LOUDNESS_THRESHOLD)
            return;

        canvas.Save();
        canvas.Translate(_vibrationOffset, 0);

        try
        {
            var (outerRect, panelRect, meterRect, ledPanelRect) = CalculateDimensions(info);

            RenderOuterCase(canvas, outerRect);
            RenderPanel(canvas, panelRect);
            RenderLabels(canvas, panelRect);
            RenderRecessedLedPanel(canvas, ledPanelRect);

            int activeLedCount = (int)(loudness * _ledCount);
            int peakLedIndex = (int)(peakLoudness * _ledCount);

            var (ledHeight, ledSpacing, ledWidth) = CalculateLedDimensions(meterRect);

            RenderTickMarks(canvas, panelRect, meterRect);
            RenderLedArray(canvas, meterRect, (ledHeight, ledSpacing, ledWidth), activeLedCount, peakLedIndex);
            RenderScrews(canvas, panelRect);
        }
        finally
        {
            canvas.Restore();
        }
    }

    private static (SKRect outerRect, SKRect panelRect, SKRect meterRect, SKRect ledPanelRect) CalculateDimensions(SKImageInfo info)
    {
        float outerPadding = 5f;
        float panelLeft = PANEL_PADDING;
        float panelTop = PANEL_PADDING;
        float panelWidth = info.Width - PANEL_PADDING * 2;
        float panelHeight = info.Height - PANEL_PADDING * 2;

        SKRect outerRect = new(
            outerPadding,
            outerPadding,
            info.Width - outerPadding,
            info.Height - outerPadding
        );

        SKRect panelRect = new(
            panelLeft,
            panelTop,
            panelLeft + panelWidth,
            panelTop + panelHeight
        );

        float meterLeft = panelLeft + TICK_MARK_WIDTH + 5;
        float meterTop = panelTop + 20;
        float meterWidth = panelWidth - (TICK_MARK_WIDTH + 15);
        float meterHeight = panelHeight - 25;

        SKRect meterRect = new(
            meterLeft,
            meterTop,
            meterLeft + meterWidth,
            meterTop + meterHeight
        );

        SKRect ledPanelRect = new(
            meterLeft - 3,
            meterTop - 3,
            meterLeft + meterWidth + 6,
            meterTop + meterHeight + 6
        );

        return (outerRect, panelRect, meterRect, ledPanelRect);
    }

    private (float height, float spacing, float width) CalculateLedDimensions(SKRect meterRect)
    {
        float totalLedSpace = meterRect.Height * 0.95f;
        float totalSpacingSpace = meterRect.Height * 0.05f;
        float ledHeight = (totalLedSpace - totalSpacingSpace) / _ledCount;
        float spacing = _ledCount > 1 ? totalSpacingSpace / (_ledCount - 1) : 0;
        float ledWidth = meterRect.Width;

        return (ledHeight, spacing, ledWidth);
    }

    private void RenderOuterCase(SKCanvas canvas, SKRect rect)
    {
        var paintPool = _paintPool;
        if (paintPool == null) return;

        using var outerCasePaint = paintPool.Get();
        if (outerCasePaint == null) return;

        SKColor[] colors = [
            new SKColor(70, 70, 70),
            new SKColor(40, 40, 40),
            new SKColor(55, 55, 55)
            ];

        float[] positions = [0.0f, 0.7f, 1.0f];

        outerCasePaint.Shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(0, rect.Height),
            colors,
            positions,
            SKShaderTileMode.Clamp
        );
        outerCasePaint.IsAntialias = UseAntiAlias;

        canvas.DrawRoundRect(rect, CORNER_RADIUS, CORNER_RADIUS, outerCasePaint);

        RenderCaseHighlight(canvas, rect);
    }

    private void RenderCaseHighlight(SKCanvas canvas, SKRect rect)
    {
        var paintPool = _paintPool;
        if (paintPool == null) return;

        using var highlightPaint = paintPool.Get();
        if (highlightPaint == null) return;

        highlightPaint.IsAntialias = UseAntiAlias;
        highlightPaint.Style = SKPaintStyle.Stroke;
        highlightPaint.StrokeWidth = 1.2f;
        highlightPaint.Color = new SKColor(255, 255, 255, 40);

        canvas.DrawLine(
            rect.Left + CORNER_RADIUS, rect.Top + 1.5f,
            rect.Right - CORNER_RADIUS, rect.Top + 1.5f,
            highlightPaint
        );
    }

    private void RenderPanel(SKCanvas canvas, SKRect rect)
    {
        using var roundRect = new SKRoundRect(rect, CORNER_RADIUS - 4, CORNER_RADIUS - 4);

        RenderPanelBackground(canvas, roundRect);
        RenderPanelBevel(canvas, roundRect);

        if (UseAdvancedEffects)
        {
            RenderPanelVignette(canvas, roundRect, rect);
        }
    }

    private void RenderPanelBackground(SKCanvas canvas, SKRoundRect roundRect)
    {
        if (_brushedMetalBitmap == null)
            return;

        var paintPool = _paintPool;
        if (paintPool == null) return;

        using var panelPaint = paintPool.Get();
        if (panelPaint == null) return;

        panelPaint.Shader = SKShader.CreateBitmap(
            _brushedMetalBitmap,
            SKShaderTileMode.Repeat,
            SKShaderTileMode.Repeat,
            SKMatrix.CreateScale(1.5f, 1.5f)
        );
        panelPaint.IsAntialias = UseAntiAlias;
        canvas.DrawRoundRect(roundRect, panelPaint);
    }

    private void RenderPanelVignette(SKCanvas canvas, SKRoundRect roundRect, SKRect rect)
    {
        var paintPool = _paintPool;
        if (paintPool == null) return;

        using var vignettePaint = paintPool.Get();
        if (vignettePaint == null) return;

        vignettePaint.IsAntialias = UseAntiAlias;

        SKColor[] colors = [new SKColor(0, 0, 0, 0), new SKColor(0, 0, 0, 30)];
        float[] positions = null!;

        vignettePaint.Shader = SKShader.CreateRadialGradient(
            new SKPoint(rect.MidX, rect.MidY),
            Max(rect.Width, rect.Height) * 0.75f,
            colors,
            positions,
            SKShaderTileMode.Clamp
        );
        canvas.DrawRoundRect(roundRect, vignettePaint);
    }

    private void RenderPanelBevel(SKCanvas canvas, SKRoundRect roundRect)
    {
        RenderBevelHighlight(canvas, roundRect);
        RenderBevelShadow(canvas, roundRect);
    }

    private void RenderBevelHighlight(SKCanvas canvas, SKRoundRect roundRect)
    {
        var paintPool = _paintPool;
        if (paintPool == null) return;

        using var outerHighlightPaint = paintPool.Get();
        if (outerHighlightPaint == null) return;

        outerHighlightPaint.IsAntialias = UseAntiAlias;
        outerHighlightPaint.Style = SKPaintStyle.Stroke;
        outerHighlightPaint.StrokeWidth = BEVEL_SIZE;
        outerHighlightPaint.Color = new SKColor(255, 255, 255, 120);

        using var highlightPath = new SKPath();
        float radOffset = BEVEL_SIZE / 2;
        highlightPath.MoveTo(roundRect.Rect.Left + CORNER_RADIUS, roundRect.Rect.Bottom - radOffset);
        highlightPath.ArcTo(new SKRect(
            roundRect.Rect.Left + radOffset,
            roundRect.Rect.Bottom - CORNER_RADIUS * 2 + radOffset,
            roundRect.Rect.Left + CORNER_RADIUS * 2 - radOffset,
            roundRect.Rect.Bottom),
            90, 90, false);
        highlightPath.LineTo(roundRect.Rect.Left + radOffset, roundRect.Rect.Top + CORNER_RADIUS);
        highlightPath.ArcTo(new SKRect(
            roundRect.Rect.Left + radOffset,
            roundRect.Rect.Top + radOffset,
            roundRect.Rect.Left + CORNER_RADIUS * 2 - radOffset,
            roundRect.Rect.Top + CORNER_RADIUS * 2 - radOffset),
            180, 90, false);
        highlightPath.LineTo(roundRect.Rect.Right - CORNER_RADIUS, roundRect.Rect.Top + radOffset);
        canvas.DrawPath(highlightPath, outerHighlightPaint);
    }

    private void RenderBevelShadow(SKCanvas canvas, SKRoundRect roundRect)
    {
        var paintPool = _paintPool;
        if (paintPool == null) return;

        using var outerShadowPaint = paintPool.Get();
        if (outerShadowPaint == null) return;

        outerShadowPaint.IsAntialias = UseAntiAlias;
        outerShadowPaint.Style = SKPaintStyle.Stroke;
        outerShadowPaint.StrokeWidth = BEVEL_SIZE;
        outerShadowPaint.Color = new SKColor(0, 0, 0, 90);

        using var shadowPath = new SKPath();
        float radOffset = BEVEL_SIZE / 2;
        shadowPath.MoveTo(roundRect.Rect.Right - CORNER_RADIUS, roundRect.Rect.Top + radOffset);
        shadowPath.ArcTo(new SKRect(
            roundRect.Rect.Right - CORNER_RADIUS * 2 + radOffset,
            roundRect.Rect.Top + radOffset,
            roundRect.Rect.Right - radOffset,
            roundRect.Rect.Top + CORNER_RADIUS * 2 - radOffset),
            270, 90, false);
        shadowPath.LineTo(roundRect.Rect.Right - radOffset, roundRect.Rect.Bottom - CORNER_RADIUS);
        shadowPath.ArcTo(new SKRect(
            roundRect.Rect.Right - CORNER_RADIUS * 2 + radOffset,
            roundRect.Rect.Bottom - CORNER_RADIUS * 2 + radOffset,
            roundRect.Rect.Right - radOffset,
            roundRect.Rect.Bottom - radOffset),
            0, 90, false);
        shadowPath.LineTo(roundRect.Rect.Left + CORNER_RADIUS, roundRect.Rect.Bottom - radOffset);
        canvas.DrawPath(shadowPath, outerShadowPaint);
    }

    private void RenderRecessedLedPanel(SKCanvas canvas, SKRect rect)
    {
        float recessRadius = 6f;
        using var recessRoundRect = new SKRoundRect(rect, recessRadius, recessRadius);

        var paintPool = _paintPool;
        if (paintPool == null) return;

        using var backgroundPaint = paintPool.Get();
        if (backgroundPaint == null) return;

        backgroundPaint.IsAntialias = UseAntiAlias;
        backgroundPaint.Color = new SKColor(12, 12, 12);
        canvas.DrawRoundRect(recessRoundRect, backgroundPaint);

        if (UseAdvancedEffects)
        {
            RenderLedPanelShadow(canvas, recessRoundRect, rect);
        }

        using var borderPaint = paintPool.Get();
        if (borderPaint == null) return;

        borderPaint.IsAntialias = UseAntiAlias;
        borderPaint.Style = SKPaintStyle.Stroke;
        borderPaint.StrokeWidth = 1;
        borderPaint.Color = new SKColor(0, 0, 0, 180);
        canvas.DrawRoundRect(recessRoundRect, borderPaint);
    }

    private void RenderLedPanelShadow(
        SKCanvas canvas,
        SKRoundRect recessRoundRect,
        SKRect rect)
    {
        var paintPool = _paintPool;
        if (paintPool == null) return;

        using var innerShadowPaint = paintPool.Get();
        if (innerShadowPaint == null) return;

        innerShadowPaint.IsAntialias = UseAntiAlias;

        SKColor[] colors = [new SKColor(0, 0, 0, 120), new SKColor(0, 0, 0, 0)];
        float[] positions = null!;

        innerShadowPaint.Shader = SKShader.CreateLinearGradient(
            new SKPoint(rect.Left, rect.Top),
            new SKPoint(rect.Left, rect.Top + rect.Height * 0.2f),
            colors,
            positions,
            SKShaderTileMode.Clamp
        );
        canvas.DrawRoundRect(recessRoundRect, innerShadowPaint);
    }

    private void RenderScrews(SKCanvas canvas, SKRect panelRect)
    {
        if (_screwBitmap == null)
            return;

        float cornerOffset = CORNER_RADIUS - 4;

        DrawScrew(canvas, panelRect.Left + cornerOffset, panelRect.Top + cornerOffset, _screwAngles[0]);
        DrawScrew(canvas, panelRect.Right - cornerOffset, panelRect.Top + cornerOffset, _screwAngles[1]);
        DrawScrew(canvas, panelRect.Left + cornerOffset, panelRect.Bottom - cornerOffset, _screwAngles[2]);
        DrawScrew(canvas, panelRect.Right - cornerOffset, panelRect.Bottom - cornerOffset, _screwAngles[3]);

        RenderBrandingText(canvas, panelRect);
    }

    private void RenderBrandingText(SKCanvas canvas, SKRect panelRect)
    {
        float labelX = panelRect.Right - 65;
        float labelY = panelRect.Bottom - 8;

        var paintPool = _paintPool;
        if (paintPool == null) return;

        using var labelPaint = paintPool.Get();
        if (labelPaint == null) return;

        labelPaint.IsAntialias = UseAntiAlias;
        labelPaint.Color = new SKColor(230, 230, 230, 120);

        using var font = new SKFont(
            SKTypeface.FromFamilyName(
                "Arial",
                SKFontStyleWeight.Normal,
                SKFontStyleWidth.Normal,
                SKFontStyleSlant.Upright),
            8
        );

        canvas.DrawText(
            "SpectrumNet™ Audio",
            labelX,
            labelY,
            SKTextAlign.Right,
            font,
            labelPaint);
    }

    private void DrawScrew(SKCanvas canvas, float x, float y, float angle)
    {
        if (_screwBitmap == null)
            return;

        canvas.Save();
        try
        {
            canvas.Translate(x, y);
            canvas.RotateDegrees(angle);
            canvas.Translate(-12, -12);
            canvas.DrawBitmap(_screwBitmap, 0, 0);
        }
        finally
        {
            canvas.Restore();
        }
    }

    private void RenderLabels(SKCanvas canvas, SKRect panelRect)
    {
        var paintPool = _paintPool;
        if (paintPool == null) return;

        using var labelPaint = paintPool.Get();
        if (labelPaint == null) return;

        labelPaint.IsAntialias = UseAntiAlias;

        using var boldTypeface = SKTypeface.FromFamilyName(
            "Arial",
            SKFontStyleWeight.Bold,
            SKFontStyleWidth.Normal,
            SKFontStyleSlant.Upright);

        using var font14 = new SKFont(boldTypeface, 14);
        using var font10 = new SKFont(boldTypeface, 10);
        using var font8 = new SKFont(boldTypeface, 8);

        DrawMainLabels(canvas, panelRect, labelPaint, font14, font10);
        DrawSubLabels(canvas, panelRect, labelPaint, font8);
    }

    private static void DrawMainLabels(
        SKCanvas canvas,
        SKRect panelRect,
        SKPaint paint,
        SKFont titleFont,
        SKFont subtitleFont)
    {
        float labelX = panelRect.Left + 10;
        float labelY = panelRect.Top + 14;

        paint.Color = new SKColor(30, 30, 30, 180);
        canvas.DrawText(
            "VU",
            labelX + 1,
            labelY + 1,
            SKTextAlign.Left,
            titleFont,
            paint);

        paint.Color = new SKColor(230, 230, 230, 200);
        canvas.DrawText(
            "VU",
            labelX,
            labelY,
            SKTextAlign.Left,
            titleFont,
            paint);

        paint.Color = new SKColor(200, 200, 200, 150);
        canvas.DrawText(
            "dB METER",
            labelX + 30,
            labelY,
            SKTextAlign.Left,
            subtitleFont,
            paint);
    }

    private static void DrawSubLabels(SKCanvas canvas, SKRect panelRect, SKPaint paint, SKFont font)
    {
        paint.Color = new SKColor(200, 200, 200, 120);
        canvas.DrawText(
            "PRO SERIES",
            panelRect.Right - 10,
            panelRect.Top + 14,
            SKTextAlign.Right,
            font,
            paint);

        paint.Color = new SKColor(200, 200, 200, 120);
        canvas.DrawText(
            "dB",
            panelRect.Left + 10,
            panelRect.Bottom - 10,
            SKTextAlign.Left,
            font,
            paint);
    }

    private void RenderTickMarks(SKCanvas canvas, SKRect panelRect, SKRect meterRect)
    {
        DrawTickBackground(canvas, panelRect, meterRect);
        DrawTickLabels(canvas, meterRect);

        if (UseAdvancedEffects)
        {
            DrawMinorTicks(canvas, meterRect);
        }
    }

    private void DrawTickBackground(SKCanvas canvas, SKRect panelRect, SKRect meterRect)
    {
        var paintPool = _paintPool;
        if (paintPool == null) return;

        using var tickAreaPaint = paintPool.Get();
        if (tickAreaPaint == null) return;

        tickAreaPaint.IsAntialias = UseAntiAlias;
        tickAreaPaint.Color = new SKColor(30, 30, 30, 70);

        SKRect tickAreaRect = new(
            panelRect.Left,
            meterRect.Top,
            panelRect.Left + TICK_MARK_WIDTH - 2,
            meterRect.Bottom
        );

        canvas.DrawRect(tickAreaRect, tickAreaPaint);
    }

    private void DrawTickLabels(SKCanvas canvas, SKRect meterRect)
    {
        var paintPool = _paintPool;
        if (paintPool == null) return;

        using var tickPaint = paintPool.Get();
        if (tickPaint == null) return;

        tickPaint.Style = SKPaintStyle.Stroke;
        tickPaint.StrokeWidth = 1;
        tickPaint.Color = SKColors.LightGray.WithAlpha(150);
        tickPaint.IsAntialias = UseAntiAlias;

        using var textPaint = paintPool.Get();
        if (textPaint == null) return;

        textPaint.Color = SKColors.LightGray.WithAlpha(180);
        textPaint.IsAntialias = UseAntiAlias;

        using var font = new SKFont(
            SKTypeface.FromFamilyName(
                "Arial",
                SKFontStyleWeight.Normal,
                SKFontStyleWidth.Normal,
                SKFontStyleSlant.Upright),
            9
        );

        string[] dbValues = ["0", "-3", "-6", "-10", "-20", "-40"];
        float[] dbPositions = [1.0f, 0.85f, 0.7f, 0.55f, 0.3f, 0.0f];

        float x = meterRect.Left - TICK_MARK_WIDTH;
        float width = TICK_MARK_WIDTH;
        float height = meterRect.Height;
        float y = meterRect.Top;

        for (int i = 0; i < dbValues.Length; i++)
        {
            float yPos = y + height - dbPositions[i] * height;
            canvas.DrawLine(x, yPos, x + width - 5, yPos, tickPaint);

            if (UseAdvancedEffects)
            {
                using var shadowPaint = paintPool.Get();
                if (shadowPaint != null)
                {
                    shadowPaint.Color = SKColors.Black.WithAlpha(80);
                    shadowPaint.IsAntialias = UseAntiAlias;
                    canvas.DrawText(dbValues[i], x + width - 7, yPos + 3.5f, SKTextAlign.Right, font, shadowPaint);
                }
            }

            canvas.DrawText(dbValues[i], x + width - 8, yPos + 3, SKTextAlign.Right, font, textPaint);
        }
    }

    private void DrawMinorTicks(SKCanvas canvas, SKRect meterRect)
    {
        var paintPool = _paintPool;
        if (paintPool == null) return;

        using var tickPaint = paintPool.Get();
        if (tickPaint == null) return;

        tickPaint.Style = SKPaintStyle.Stroke;
        tickPaint.StrokeWidth = 1;
        tickPaint.Color = SKColors.LightGray.WithAlpha(80);
        tickPaint.IsAntialias = UseAntiAlias;

        float x = meterRect.Left - TICK_MARK_WIDTH;
        float width = TICK_MARK_WIDTH;
        float height = meterRect.Height;
        float y = meterRect.Top;

        for (int i = 0; i < 10; i++)
        {
            float ratio = i / 10f;
            float yPos = y + ratio * height;
            canvas.DrawLine(x, yPos, x + width * 0.6f, yPos, tickPaint);
        }
    }

    private void RenderLedArray(
        SKCanvas canvas,
        SKRect meterRect,
        (float height, float spacing, float width) ledDimensions,
        int activeLedCount,
        int peakLedIndex)
    {
        float x = meterRect.Left;
        float y = meterRect.Top;
        float height = ledDimensions.height;
        float spacing = ledDimensions.spacing;
        float width = ledDimensions.width;

        RenderInactiveLeds(canvas, x, y, width, height, spacing, activeLedCount, peakLedIndex);
        RenderActiveLeds(canvas, x, y, width, height, spacing, activeLedCount, peakLedIndex);
    }

    private void RenderInactiveLeds(
        SKCanvas canvas,
        float x,
        float y,
        float width,
        float height,
        float spacing,
        int activeLedCount,
        int peakLedIndex)
    {
        for (int i = 0; i < _ledCount; i++)
        {
            if (i < activeLedCount || i == peakLedIndex)
                continue;

            float normalizedPosition = (float)i / _ledCount;
            float ledY = y + (_ledCount - i - 1) * (height + spacing);
            SKColor color = GetLedColorForPosition(normalizedPosition, i);

            RenderInactiveLed(canvas, x, ledY, width, height, color);
        }
    }

    private void RenderActiveLeds(
        SKCanvas canvas,
        float x,
        float y,
        float width,
        float height,
        float spacing,
        int activeLedCount,
        int peakLedIndex)
    {
        for (int i = 0; i < _ledCount; i++)
        {
            if (i >= activeLedCount && i != peakLedIndex)
                continue;

            float normalizedPosition = (float)i / _ledCount;
            float ledY = y + (_ledCount - i - 1) * (height + spacing);
            SKColor color = GetLedColorForPosition(normalizedPosition, i);
            bool isActive = i < activeLedCount;
            bool isPeak = i == peakLedIndex;

            _ledAnimationPhases[i] = (_ledAnimationPhases[i] + ANIMATION_SPEED * (0.5f + normalizedPosition))
                % 1.0f;
            RenderActiveLed(canvas, x, ledY, width, height, color, isActive, isPeak, i);
        }
    }

    private void RenderInactiveLed(
        SKCanvas canvas,
        float x,
        float y,
        float width,
        float height,
        SKColor color)
    {
        _ledPath.Reset();
        using var ledRect = new SKRoundRect(
            new SKRect(x, y, x + width, y + height),
            LED_ROUNDING_RADIUS,
            LED_ROUNDING_RADIUS
        );
        _ledPath.AddRoundRect(ledRect);

        var paintPool = _paintPool;
        if (paintPool == null) return;

        using var ledBasePaint = paintPool.Get();
        if (ledBasePaint == null) return;

        ledBasePaint.Style = SKPaintStyle.Fill;
        ledBasePaint.Color = new SKColor(8, 8, 8);
        ledBasePaint.IsAntialias = UseAntiAlias;
        canvas.DrawPath(_ledPath, ledBasePaint);

        float inset = 1f;
        using var ledSurfaceRect = new SKRoundRect(
            new SKRect(x + inset, y + inset, x + width - inset, y + height - inset),
            Max(1, LED_ROUNDING_RADIUS - inset * 0.5f),
            Max(1, LED_ROUNDING_RADIUS - inset * 0.5f)
        );

        using var inactiveLedPaint = paintPool.Get();
        if (inactiveLedPaint == null) return;

        inactiveLedPaint.Style = SKPaintStyle.Fill;
        inactiveLedPaint.Color = MultiplyColor(color, 0.10f);
        inactiveLedPaint.IsAntialias = UseAntiAlias;
        canvas.DrawRoundRect(ledSurfaceRect, inactiveLedPaint);
    }

    private void RenderActiveLed(
        SKCanvas canvas,
        float x,
        float y,
        float width,
        float height,
        SKColor color,
        bool isActive,
        bool isPeak,
        int index)
    {
        float brightnessVariation = _ledVariations[index % _ledVariations.Count];
        float animPhase = _ledAnimationPhases[index % _ledAnimationPhases.Length];

        _ledPath.Reset();
        using var ledRect = new SKRoundRect(
            new SKRect(x, y, x + width, y + height),
            LED_ROUNDING_RADIUS,
            LED_ROUNDING_RADIUS
        );
        _ledPath.AddRoundRect(ledRect);

        var paintPool = _paintPool;
        if (paintPool == null) return;

        using var ledBasePaint = paintPool.Get();
        if (ledBasePaint == null) return;

        ledBasePaint.Style = SKPaintStyle.Fill;
        ledBasePaint.Color = new SKColor(8, 8, 8);
        ledBasePaint.IsAntialias = UseAntiAlias;
        canvas.DrawPath(_ledPath, ledBasePaint);

        float inset = 1f;
        using var ledSurfaceRect = new SKRoundRect(
            new SKRect(x + inset, y + inset, x + width - inset, y + height - inset),
            Max(1, LED_ROUNDING_RADIUS - inset * 0.5f),
            Max(1, LED_ROUNDING_RADIUS - inset * 0.5f)
        );

        SKColor ledOnColor = color;
        SKColor ledOffColor = new(10, 10, 10, 220);

        float pulse = isPeak ?
            0.7f + (float)Sin(animPhase * PI * 2) * 0.3f :
            brightnessVariation;

        ledOnColor = MultiplyColor(ledOnColor, pulse);

        RenderLedGlow(canvas, ledSurfaceRect, ledOnColor, index, animPhase, brightnessVariation);
        RenderLedSurface(canvas, x, y, height, ledSurfaceRect, ledOnColor, ledOffColor);
        RenderLedHighlight(canvas, x, y, width, height);
    }

    private void RenderLedGlow(
        SKCanvas canvas,
        SKRoundRect ledSurfaceRect,
        SKColor ledOnColor,
        int index,
        float animPhase,
        float brightnessVariation)
    {
        if (!UseAdvancedEffects || index <= ledSurfaceRect.Rect.Height * 0.7f)
            return;

        var paintPool = _paintPool;
        if (paintPool == null) return;

        float glowIntensity = GLOW_INTENSITY
            * (0.8f + MathF.Sin(animPhase * MathF.PI * 2) * 0.2f * brightnessVariation);

        using var glowPaint = paintPool.Get();
        if (glowPaint == null) return;

        glowPaint.Style = SKPaintStyle.Fill;
        glowPaint.Color = ledOnColor.WithAlpha((byte)(glowIntensity * 160 * brightnessVariation));
        glowPaint.IsAntialias = UseAntiAlias;
        glowPaint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2);
        canvas.DrawRoundRect(ledSurfaceRect, glowPaint);
    }

    private void RenderLedSurface(
        SKCanvas canvas,
        float x,
        float y,
        float height,
        SKRoundRect ledSurfaceRect,
        SKColor ledOnColor,
        SKColor ledOffColor)
    {
        var paintPool = _paintPool;
        if (paintPool == null) return;

        using var ledPaint = paintPool.Get();
        if (ledPaint == null) return;

        ledPaint.Style = SKPaintStyle.Fill;
        ledPaint.IsAntialias = UseAntiAlias;

        SKColor[] colors = [ledOnColor, MultiplyColor(ledOnColor, 0.9f), ledOffColor];
        float[] positions = [0.0f, 0.7f, 1.0f];

        ledPaint.Shader = SKShader.CreateLinearGradient(
            new SKPoint(x, y),
            new SKPoint(x, y + height),
            colors,
            positions,
            SKShaderTileMode.Clamp
        );

        canvas.DrawRoundRect(ledSurfaceRect, ledPaint);
    }

    private void RenderLedHighlight(SKCanvas canvas, float x, float y, float width, float height)
    {
        if (!UseAdvancedEffects)
            return;

        _highlightPath.Reset();
        float arcWidth = width * 0.9f;
        float arcHeight = height * 0.4f;
        float arcX = x + (width - arcWidth) / 2;
        float arcY = y + height * 0.05f;

        _highlightPath.AddRoundRect(new SKRoundRect(
            new SKRect(arcX, arcY, arcX + arcWidth, arcY + arcHeight),
            LED_ROUNDING_RADIUS,
            LED_ROUNDING_RADIUS
        ));

        var paintPool = _paintPool;
        if (paintPool == null) return;

        using var highlightFillPaint = paintPool.Get();
        if (highlightFillPaint == null) return;

        highlightFillPaint.Color = new SKColor(255, 255, 255, 50);
        highlightFillPaint.IsAntialias = UseAntiAlias;
        highlightFillPaint.Style = SKPaintStyle.Fill;
        canvas.DrawPath(_highlightPath, highlightFillPaint);

        using var highlightPaint = paintPool.Get();
        if (highlightPaint == null) return;

        highlightPaint.Style = SKPaintStyle.Stroke;
        highlightPaint.StrokeWidth = 0.7f;
        highlightPaint.Color = new SKColor(255, 255, 255, 180);
        highlightPaint.IsAntialias = UseAntiAlias;
        canvas.DrawPath(_highlightPath, highlightPaint);
    }

    private SKColor GetLedColorForPosition(float normalizedPosition, int index)
    {
        int colorGroup;
        if (normalizedPosition >= HIGH_LOUDNESS_THRESHOLD)
            colorGroup = 2; // Red
        else if (normalizedPosition >= MEDIUM_LOUDNESS_THRESHOLD)
            colorGroup = 1; // Yellow
        else
            colorGroup = 0; // Green

        int variationIndex = index % 10;
        int colorIndex = colorGroup * 10 + variationIndex;

        if (colorIndex < _ledColorVariations.Count)
            return _ledColorVariations[colorIndex];

        return colorGroup switch
        {
            2 => new SKColor(220, 30, 30),
            1 => new SKColor(230, 200, 0),
            _ => new SKColor(40, 200, 40)
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static SKColor MultiplyColor(SKColor color, float factor)
    {
        return new SKColor(
            (byte)Clamp(color.Red * factor, 0, 255),
            (byte)Clamp(color.Green * factor, 0, 255),
            (byte)Clamp(color.Blue * factor, 0, 255),
            color.Alpha
        );
    }

    protected override void OnInvalidateCachedResources()
    {
        ExecuteSafely(
            () =>
            {
                base.OnInvalidateCachedResources();
                _cachedLoudness = null;

                Log(LogLevel.Debug, LOG_PREFIX, "Cached resources invalidated");
            },
            nameof(OnInvalidateCachedResources),
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
            nameof(Dispose),
            "Error during renderer disposal"
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
            nameof(OnDispose),
            "Error during disposal"
        );
    }

    private void DisposeManagedResources()
    {
        _ledPath?.Dispose();
        _highlightPath?.Dispose();
        _screwBitmap?.Dispose();
        _brushedMetalBitmap?.Dispose();
        // _paintPool управляется базовым классом
        _ledColorVariations.Clear();
        _ledVariations.Clear();
    }
}