#nullable enable

using static SpectrumNet.Views.Renderers.LedMeterRenderer.Constants;
using static SpectrumNet.Views.Renderers.LedMeterRenderer.Constants.Quality;

namespace SpectrumNet.Views.Renderers;
 
public sealed class LedMeterRenderer : EffectSpectrumRenderer
{
    private static readonly Lazy<LedMeterRenderer> _instance = 
        new(() => new LedMeterRenderer());

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
            GLOW_INTENSITY = 0.3f,
            OVERLAY_ALPHA_FACTOR = 0.7f;

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
        _peakLoudness,
        _overlayAlphaFactor = 1.0f;

    private float? _cachedLoudness;
    private float[] _ledAnimationPhases = [];

    private int
        _currentWidth,
        _currentHeight,
        _ledCount = DEFAULT_LED_COUNT;

    private readonly SKPath _ledPath = new();
    private readonly SKPath _highlightPath = new();
    private readonly float[] _screwAngles = [45f, 120f, 10f, 80f];
    private SKBitmap? _screwBitmap;
    private SKBitmap? _staticBitmap;
    private SKBitmap? _brushedMetalBitmap;
    private readonly List<float> _ledVariations = new(30);
    private readonly List<SKColor> _ledColorVariations = new(30);

    private readonly object _loudnessLock = new();
    private readonly object _configLock = new();

    private volatile bool 
        _isConfiguring,
        _overlayStateChanged,
        _overlayStateChangeRequested;

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
        InitializeVariations();
        CreateCachedTextures();
        ResetState();
    }

    private void InitializeQualityParams() => 
        ApplyQualitySettingsInternal();

    private void InitializeVariations()
    {
        Random fixedRandom = new(42);
        InitializeLedVariations(fixedRandom);
        InitializeColorVariations(fixedRandom);
    }

    private void CreateCachedTextures()
    {
        _screwBitmap = CreateScrewTexture();
        _brushedMetalBitmap = CreateBrushedMetalTexture();
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
        _overlayStateChanged = true;
        _overlayStateChangeRequested = false;
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

        SKColor[] baseColors = [
            new(30, 200, 30),   // green
            new(220, 200, 0),   // yellow
            new(230, 30, 30)    // red
        ];

        foreach (var baseColor in baseColors)
        {
            GenerateColorVariations(random, baseColor, 10);
        }
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

    public override void Configure(
        bool isOverlayActive,
        RenderQuality quality)
    {
        lock (_configLock)
        {
            ExecuteSafely(
                () =>
                {
                    bool overlayChanged = _isOverlayActive != isOverlayActive;
                    bool qualityChanged = Quality != quality;

                    _isOverlayActive = isOverlayActive;
                    Quality = quality;
                    _smoothingFactor = isOverlayActive ?
                        SMOOTHING_FACTOR_OVERLAY :
                        SMOOTHING_FACTOR_NORMAL;

                    _overlayAlphaFactor = isOverlayActive ?
                        OVERLAY_ALPHA_FACTOR :
                        1.0f;

                    if (overlayChanged)
                    {
                        _overlayStateChangeRequested = true;
                        _overlayStateChanged = true;

                        Log(LogLevel.Information, LOG_PREFIX,
                            $"Overlay state changed from {!isOverlayActive} to {isOverlayActive}");
                    }

                    if (qualityChanged || overlayChanged)
                    {
                        if (!_isConfiguring)
                        {
                            _isConfiguring = true;
                            try
                            {
                                ApplyQualitySettingsInternal();
                                OnConfigurationChanged();
                            }
                            finally
                            {
                                _isConfiguring = false;
                            }
                        }
                    }
                },
                nameof(Configure),
                "Failed to configure renderer"
            );
        }
    }

    protected override void OnConfigurationChanged()
    {
        ExecuteSafely(
            () =>
            {
                Log(LogLevel.Information,
                    LOG_PREFIX,
                    $"Configuration changed. Quality: {Quality}, " +
                    $"Overlay: {_isOverlayActive}, Alpha: {_overlayAlphaFactor}");
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
                    _overlayStateChanged = true;
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
                ApplyLowQualitySettings();
                break;
            case RenderQuality.Medium:
                ApplyMediumQualitySettings();
                break;
            case RenderQuality.High:
                ApplyHighQualitySettings();
                break;
        }

        if (_currentWidth > 0 && _currentHeight > 0)
        {
            var info = new SKImageInfo(_currentWidth, _currentHeight);
            CreateStaticBitmap(info);
        }

        LogQualitySettings();
    }

    private void ApplyLowQualitySettings()
    {
        base._useAntiAlias = LOW_USE_ANTIALIASING;
        base._samplingOptions = new SKSamplingOptions(
            LOW_FILTER_MODE,
            LOW_MIPMAP_MODE);
        base._useAdvancedEffects = LOW_USE_ADVANCED_EFFECTS;
    }

    private void ApplyMediumQualitySettings()
    {
        base._useAntiAlias = MEDIUM_USE_ANTIALIASING;
        base._samplingOptions = new SKSamplingOptions(
            MEDIUM_FILTER_MODE,
            MEDIUM_MIPMAP_MODE);
        base._useAdvancedEffects = MEDIUM_USE_ADVANCED_EFFECTS;
    }

    private void ApplyHighQualitySettings()
    {
        base._useAntiAlias = HIGH_USE_ANTIALIASING;
        base._samplingOptions = new SKSamplingOptions(
            HIGH_FILTER_MODE,
            HIGH_MIPMAP_MODE);
        base._useAdvancedEffects = HIGH_USE_ADVANCED_EFFECTS;
    }

    private void LogQualitySettings()
    {
        Log(LogLevel.Debug, LOG_PREFIX,
            $"Quality settings applied. Quality: {Quality}, " +
            $"AntiAlias: {UseAntiAlias}, AdvancedEffects: {UseAdvancedEffects}");
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

        ExecuteSafely(
            () =>
            {
                if (_overlayStateChangeRequested)
                {
                    _overlayStateChangeRequested = false;
                    _overlayStateChanged = true;
                }

                UpdateRenderState(info, spectrum);
                ExecuteRendering(canvas, info);

                if (_overlayStateChanged)
                {
                    _overlayStateChanged = false;
                }
            },
            nameof(RenderEffect),
            "Error in RenderEffect method"
        );
    }

    private void UpdateRenderState(SKImageInfo info, float[] spectrum)
    {
        CheckAndUpdateDimensions(info);
        UpdateAnimationState(spectrum);
    }

    private void CheckAndUpdateDimensions(SKImageInfo info)
    {
        if (_currentWidth != info.Width || _currentHeight != info.Height)
        {
            _currentWidth = info.Width;
            _currentHeight = info.Height;
            float panelHeight = info.Height - PANEL_PADDING * 2;
            _ledCount = Math.Max(10, Math.Min(DEFAULT_LED_COUNT, (int)(panelHeight / 12)));
            InitializeLedAnimationPhases();
            CreateStaticBitmap(info);
        }
    }

    private void CreateStaticBitmap(SKImageInfo info)
    {
        _staticBitmap?.Dispose();
        _staticBitmap = new SKBitmap(info.Width, info.Height);
        using var canvas = new SKCanvas(_staticBitmap);
        var dimensions = CalculateDimensions(info);
        RenderOuterCase(canvas, dimensions.OuterRect);
        RenderPanel(canvas, dimensions.PanelRect);
        RenderLabels(canvas, dimensions.PanelRect);
        RenderRecessedLedPanel(canvas, dimensions.LedPanelRect);
        RenderTickMarks(canvas, dimensions.PanelRect, dimensions.MeterRect);
        RenderScrews(canvas, dimensions.PanelRect);
    }

    private void ExecuteRendering(SKCanvas canvas, SKImageInfo info)
    {
        float loudness = GetCurrentLoudness();
        RenderMeter(canvas, info, loudness, _peakLoudness);
    }

    private float GetCurrentLoudness()
    {
        lock (_loudnessLock)
        {
            return _cachedLoudness ?? 0f;
        }
    }

    private bool ValidateRenderParameters(
        SKCanvas? canvas,
        float[]? spectrum,
        SKImageInfo info,
        SKPaint? paint)
    {
        return canvas != null
            && spectrum != null
            && spectrum.Length > 0
            && paint != null
            && info.Width > 0
            && info.Height > 0
            && !_disposed;
    }

    private void UpdateAnimationState(float[] spectrum)
    {
        float loudness = CalculateAndSmoothLoudness(spectrum);
        _cachedLoudness = loudness;

        UpdateAnimationPhase();
        UpdatePeakLoudness(loudness);
        UpdateVibrationEffect(loudness);
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

    private void UpdateAnimationPhase() => 
        _animationPhase = (_animationPhase + ANIMATION_SPEED) % 1.0f;

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
            float vibrationIntensity =
                (loudness - HIGH_LOUDNESS_THRESHOLD) /
                (1 - HIGH_LOUDNESS_THRESHOLD);

            _vibrationOffset = (float)Sin(_animationPhase * PI * 10)
                * 0.8f * vibrationIntensity;
        }
        else
        {
            _vibrationOffset = 0;
        }
    }

    private float CalculateAndSmoothLoudness(float[] spectrum)
    {
        float rawLoudness = CalculateLoudness(spectrum.AsSpan());
        float smoothedLoudness = _previousLoudness +
            (rawLoudness - _previousLoudness) * _smoothingFactor;
        smoothedLoudness = Clamp(
            smoothedLoudness,
            MIN_LOUDNESS_THRESHOLD,
            1f);
        _previousLoudness = smoothedLoudness;
        return smoothedLoudness;
    }

    private static float CalculateLoudness(ReadOnlySpan<float> spectrum)
    {
        if (spectrum.IsEmpty)
            return 0f;

        float sum = 0f;
        for (int i = 0; i < spectrum.Length; i++)
        {
            sum += Abs(spectrum[i]);
        }

        return Clamp(sum / spectrum.Length, 0f, 1f);
    }

    private void RenderMeter(
        SKCanvas canvas,
        SKImageInfo info,
        float loudness,
        float peakLoudness)
    {
        if (loudness < MIN_LOUDNESS_THRESHOLD && !_isOverlayActive)
            return;

        canvas.Save();

        if (!_isOverlayActive)
        {
            canvas.Translate(_vibrationOffset, 0);
        }

        try
        {
            var dimensions = CalculateDimensions(info);

            if (_isOverlayActive)
            {
                RenderOverlayMode(canvas, dimensions, loudness, peakLoudness);
            }
            else
            {
                RenderNormalMode(canvas, dimensions, loudness, peakLoudness);
            }
        }
        finally
        {
            canvas.Restore();
        }
    }

    private void RenderOverlayMode(
        SKCanvas canvas,
        MeterDimensions dimensions,
        float loudness,
        float peakLoudness)
    {
        using var overlayPaint = new SKPaint
        {
            Color = new SKColor(255, 255, 255, (byte)(255 * _overlayAlphaFactor))
        };

        canvas.SaveLayer(overlayPaint);
        try
        {
            RenderMeterContent(canvas, dimensions, loudness, peakLoudness);
        }
        finally
        {
            canvas.Restore();
        }
    }

    private void RenderNormalMode(
        SKCanvas canvas,
        MeterDimensions dimensions,
        float loudness,
        float peakLoudness)
    {
        RenderMeterContent(canvas, dimensions, loudness, peakLoudness);
    }

    private void RenderMeterContent(
        SKCanvas canvas,
        MeterDimensions dimensions,
        float loudness,
        float peakLoudness)
    {
        if (_staticBitmap != null)
        {
            canvas.DrawBitmap(_staticBitmap, 0, 0);
        }
        RenderLedSystem(canvas, dimensions, loudness, peakLoudness);
    }

    private void RenderLedSystem(
        SKCanvas canvas,
        MeterDimensions dimensions,
        float loudness,
        float peakLoudness)
    {
        int activeLedCount = (int)(loudness * _ledCount);
        int peakLedIndex = (int)(peakLoudness * _ledCount);

        var ledDimensions = CalculateLedDimensions(dimensions.MeterRect);

        RenderLedArray(
            canvas,
            dimensions.MeterRect,
            ledDimensions,
            activeLedCount,
            peakLedIndex);
    }

    public bool RequiresRedraw()
    {
        return _overlayStateChanged ||
               _overlayStateChangeRequested ||
               _animationPhase != 0;
    }

    private readonly record struct MeterDimensions(
        SKRect OuterRect,
        SKRect PanelRect,
        SKRect MeterRect,
        SKRect LedPanelRect);

    private static MeterDimensions CalculateDimensions(SKImageInfo info)
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

        return new MeterDimensions(
            outerRect,
            panelRect,
            meterRect,
            ledPanelRect);
    }

    private readonly record struct LedDimensions(
        float Height,
        float Spacing,
        float Width);

    private LedDimensions CalculateLedDimensions(SKRect meterRect)
    {
        float totalLedSpace = meterRect.Height * 0.95f;
        float totalSpacingSpace = meterRect.Height * 0.05f;
        float ledHeight = (totalLedSpace - totalSpacingSpace) / _ledCount;
        float spacing = _ledCount > 1 ?
            totalSpacingSpace / (_ledCount - 1) :
            0;
        float ledWidth = meterRect.Width;

        return new LedDimensions(ledHeight, spacing, ledWidth);
    }

    private void RenderOuterCase(SKCanvas canvas, SKRect rect)
    {
        var paintPool = _paintPool;
        if (paintPool == null) return;

        using var outerCasePaint = paintPool.Get();
        if (outerCasePaint == null) return;

        ApplyOuterCaseGradient(outerCasePaint, rect);
        canvas.DrawRoundRect(rect, CORNER_RADIUS, CORNER_RADIUS, outerCasePaint);
        RenderCaseHighlight(canvas, rect);
    }

    private void ApplyOuterCaseGradient(SKPaint paint, SKRect rect)
    {
        SKColor[] colors = [
            new SKColor(70, 70, 70),
            new SKColor(40, 40, 40),
            new SKColor(55, 55, 55)
        ];

        float[] positions = [0.0f, 0.7f, 1.0f];

        paint.Shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(0, rect.Height),
            colors,
            positions,
            SKShaderTileMode.Clamp
        );
        paint.IsAntialias = UseAntiAlias;
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
        using var roundRect = new SKRoundRect(
            rect,
            CORNER_RADIUS - 4,
            CORNER_RADIUS - 4);

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

        SKColor[] colors = [
            new SKColor(0, 0, 0, 0),
            new SKColor(0, 0, 0, 30)
        ];
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

        using var highlightPath = CreateBevelHighlightPath(roundRect);
        canvas.DrawPath(highlightPath, outerHighlightPaint);
    }

    private static SKPath CreateBevelHighlightPath(SKRoundRect roundRect)
    {
        var path = new SKPath();
        float radOffset = BEVEL_SIZE / 2;

        path.MoveTo(
            roundRect.Rect.Left + CORNER_RADIUS,
            roundRect.Rect.Bottom - radOffset);

        path.ArcTo(new SKRect(
            roundRect.Rect.Left + radOffset,
            roundRect.Rect.Bottom - CORNER_RADIUS * 2 + radOffset,
            roundRect.Rect.Left + CORNER_RADIUS * 2 - radOffset,
            roundRect.Rect.Bottom),
            90, 90, false);

        path.LineTo(
            roundRect.Rect.Left + radOffset,
            roundRect.Rect.Top + CORNER_RADIUS);

        path.ArcTo(new SKRect(
            roundRect.Rect.Left + radOffset,
            roundRect.Rect.Top + radOffset,
            roundRect.Rect.Left + CORNER_RADIUS * 2 - radOffset,
            roundRect.Rect.Top + CORNER_RADIUS * 2 - radOffset),
            180, 90, false);

        path.LineTo(
            roundRect.Rect.Right - CORNER_RADIUS,
            roundRect.Rect.Top + radOffset);

        return path;
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

        using var shadowPath = CreateBevelShadowPath(roundRect);
        canvas.DrawPath(shadowPath, outerShadowPaint);
    }

    private static SKPath CreateBevelShadowPath(SKRoundRect roundRect)
    {
        var path = new SKPath();
        float radOffset = BEVEL_SIZE / 2;

        path.MoveTo(
            roundRect.Rect.Right - CORNER_RADIUS,
            roundRect.Rect.Top + radOffset);

        path.ArcTo(new SKRect(
            roundRect.Rect.Right - CORNER_RADIUS * 2 + radOffset,
            roundRect.Rect.Top + radOffset,
            roundRect.Rect.Right - radOffset,
            roundRect.Rect.Top + CORNER_RADIUS * 2 - radOffset),
            270, 90, false);

        path.LineTo(
            roundRect.Rect.Right - radOffset,
            roundRect.Rect.Bottom - CORNER_RADIUS);

        path.ArcTo(new SKRect(
            roundRect.Rect.Right - CORNER_RADIUS * 2 + radOffset,
            roundRect.Rect.Bottom - CORNER_RADIUS * 2 + radOffset,
            roundRect.Rect.Right - radOffset,
            roundRect.Rect.Bottom - radOffset),
            0, 90, false);

        path.LineTo(
            roundRect.Rect.Left + CORNER_RADIUS,
            roundRect.Rect.Bottom - radOffset);

        return path;
    }

    private void RenderRecessedLedPanel(SKCanvas canvas, SKRect rect)
    {
        float recessRadius = 6f;
        using var recessRoundRect = new SKRoundRect(
            rect,
            recessRadius,
            recessRadius);

        RenderLedPanelBackground(canvas, recessRoundRect);
        RenderLedPanelEffects(canvas, recessRoundRect, rect);
        RenderLedPanelBorder(canvas, recessRoundRect);
    }

    private void RenderLedPanelBackground(SKCanvas canvas, SKRoundRect roundRect)
    {
        var paintPool = _paintPool;
        if (paintPool == null) return;

        using var backgroundPaint = paintPool.Get();
        if (backgroundPaint == null) return;

        backgroundPaint.IsAntialias = UseAntiAlias;
        backgroundPaint.Color = new SKColor(12, 12, 12);
        canvas.DrawRoundRect(roundRect, backgroundPaint);
    }

    private void RenderLedPanelEffects(
        SKCanvas canvas,
        SKRoundRect roundRect,
        SKRect rect)
    {
        if (!UseAdvancedEffects)
            return;

        var paintPool = _paintPool;
        if (paintPool == null) return;

        using var innerShadowPaint = paintPool.Get();
        if (innerShadowPaint == null) return;

        innerShadowPaint.IsAntialias = UseAntiAlias;

        SKColor[] colors = [
            new SKColor(0, 0, 0, 120),
            new SKColor(0, 0, 0, 0)
        ];
        float[] positions = null!;

        innerShadowPaint.Shader = SKShader.CreateLinearGradient(
            new SKPoint(rect.Left, rect.Top),
            new SKPoint(rect.Left, rect.Top + rect.Height * 0.2f),
            colors,
            positions,
            SKShaderTileMode.Clamp
        );
        canvas.DrawRoundRect(roundRect, innerShadowPaint);
    }

    private void RenderLedPanelBorder(SKCanvas canvas, SKRoundRect roundRect)
    {
        var paintPool = _paintPool;
        if (paintPool == null) return;

        using var borderPaint = paintPool.Get();
        if (borderPaint == null) return;

        borderPaint.IsAntialias = UseAntiAlias;
        borderPaint.Style = SKPaintStyle.Stroke;
        borderPaint.StrokeWidth = 1;
        borderPaint.Color = new SKColor(0, 0, 0, 180);
        canvas.DrawRoundRect(roundRect, borderPaint);
    }

    private void RenderScrews(SKCanvas canvas, SKRect panelRect)
    {
        if (_screwBitmap == null)
            return;

        float cornerOffset = CORNER_RADIUS - 4;

        DrawScrew(canvas,
            panelRect.Left + cornerOffset,
            panelRect.Top + cornerOffset,
            _screwAngles[0]);

        DrawScrew(canvas,
            panelRect.Right - cornerOffset,
            panelRect.Top + cornerOffset,
            _screwAngles[1]);

        DrawScrew(canvas,
            panelRect.Left + cornerOffset,
            panelRect.Bottom - cornerOffset,
            _screwAngles[2]);

        DrawScrew(canvas,
            panelRect.Right - cornerOffset,
            panelRect.Bottom - cornerOffset,
            _screwAngles[3]);

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

    private static void DrawSubLabels(
        SKCanvas canvas,
        SKRect panelRect,
        SKPaint paint,
        SKFont font)
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
                DrawTickShadow(canvas, dbValues[i], paintPool, font, x, width, yPos);
            }

            canvas.DrawText(
                dbValues[i],
                x + width - 8,
                yPos + 3,
                SKTextAlign.Right,
                font,
                textPaint);
        }
    }

    private void DrawTickShadow(
        SKCanvas canvas,
        string text,
        ObjectPool<SKPaint> paintPool,
        SKFont font,
        float x,
        float width,
        float yPos)
    {
        using var shadowPaint = paintPool.Get();
        if (shadowPaint != null)
        {
            shadowPaint.Color = SKColors.Black.WithAlpha(80);
            shadowPaint.IsAntialias = UseAntiAlias;
            canvas.DrawText(
                text,
                x + width - 7,
                yPos + 3.5f,
                SKTextAlign.Right,
                font,
                shadowPaint);
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
        LedDimensions ledDimensions,
        int activeLedCount,
        int peakLedIndex)
    {
        RenderInactiveLeds(canvas, meterRect, ledDimensions, activeLedCount, peakLedIndex);
        RenderActiveLeds(canvas, meterRect, ledDimensions, activeLedCount, peakLedIndex);
    }

    private void RenderInactiveLeds(
        SKCanvas canvas,
        SKRect meterRect,
        LedDimensions ledDimensions,
        int activeLedCount,
        int peakLedIndex)
    {
        for (int i = 0; i < _ledCount; i++)
        {
            if (i < activeLedCount || i == peakLedIndex)
                continue;

            var ledInfo = CalculateLedInfo(i, meterRect, ledDimensions);
            SKColor color = GetLedColorForPosition(ledInfo.NormalizedPosition, i);

            RenderInactiveLed(canvas, ledInfo, color);
        }
    }

    private void RenderActiveLeds(
        SKCanvas canvas,
        SKRect meterRect,
        LedDimensions ledDimensions,
        int activeLedCount,
        int peakLedIndex)
    {
        for (int i = 0; i < _ledCount; i++)
        {
            if (i >= activeLedCount && i != peakLedIndex)
                continue;

            var ledInfo = CalculateLedInfo(i, meterRect, ledDimensions);
            SKColor color = GetLedColorForPosition(ledInfo.NormalizedPosition, i);
            bool isActive = i < activeLedCount;
            bool isPeak = i == peakLedIndex;

            UpdateLedAnimation(i, ledInfo.NormalizedPosition);
            RenderActiveLed(canvas, ledInfo, color, isPeak, i);
        }
    }

    private readonly record struct LedInfo(
        float X,
        float Y,
        float Width,
        float Height,
        float NormalizedPosition);

    private LedInfo CalculateLedInfo(
        int index,
        SKRect meterRect,
        LedDimensions ledDimensions)
    {
        float normalizedPosition = (float)index / _ledCount;
        float ledY = meterRect.Top + (_ledCount - index - 1) *
            (ledDimensions.Height + ledDimensions.Spacing);

        return new LedInfo(
            meterRect.Left,
            ledY,
            ledDimensions.Width,
            ledDimensions.Height,
            normalizedPosition
        );
    }

    private void UpdateLedAnimation(int index, float normalizedPosition)
    {
        _ledAnimationPhases[index] = (_ledAnimationPhases[index] +
            ANIMATION_SPEED * (0.5f + normalizedPosition)) % 1.0f;
    }

    private void RenderInactiveLed(
        SKCanvas canvas,
        LedInfo ledInfo,
        SKColor color)
    {
        _ledPath.Reset();
        using var ledRect = new SKRoundRect(
            new SKRect(
                ledInfo.X,
                ledInfo.Y,
                ledInfo.X + ledInfo.Width,
                ledInfo.Y + ledInfo.Height),
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

        RenderLedSurface(canvas, ledInfo, ledRect, color, false);
    }

    private void RenderActiveLed(
        SKCanvas canvas,
        LedInfo ledInfo,
        SKColor color,
        bool isPeak,
        int index)
    {
        _ledPath.Reset();
        using var ledRect = new SKRoundRect(
            new SKRect(
                ledInfo.X,
                ledInfo.Y,
                ledInfo.X + ledInfo.Width,
                ledInfo.Y + ledInfo.Height),
            LED_ROUNDING_RADIUS,
            LED_ROUNDING_RADIUS
        );
        _ledPath.AddRoundRect(ledRect);

        RenderLedBase(canvas, ledRect);
        RenderActiveLedContent(canvas, ledInfo, ledRect, color, isPeak, index);
    }

    private void RenderLedBase(SKCanvas canvas, SKRoundRect ledRect)
    {
        var paintPool = _paintPool;
        if (paintPool == null) return;

        using var ledBasePaint = paintPool.Get();
        if (ledBasePaint == null) return;

        ledBasePaint.Style = SKPaintStyle.Fill;
        ledBasePaint.Color = new SKColor(8, 8, 8);
        ledBasePaint.IsAntialias = UseAntiAlias;
        canvas.DrawPath(_ledPath, ledBasePaint);
    }

    private void RenderActiveLedContent(
        SKCanvas canvas,
        LedInfo ledInfo,
        SKRoundRect ledRect,
        SKColor color,
        bool isPeak,
        int index)
    {
        float brightnessVariation = _ledVariations[index % _ledVariations.Count];
        float animPhase = _ledAnimationPhases[index % _ledAnimationPhases.Length];

        float pulse = isPeak ?
            0.7f + (float)Sin(animPhase * PI * 2) * 0.3f :
            brightnessVariation;

        SKColor ledOnColor = MultiplyColor(color, pulse);

        RenderLedGlow(canvas, ledRect, ledOnColor, index, animPhase, brightnessVariation);
        RenderLedSurface(canvas, ledInfo, ledRect, ledOnColor, true);
        RenderLedHighlight(canvas, ledInfo);
    }

    private void RenderLedGlow(
        SKCanvas canvas,
        SKRoundRect ledRect,
        SKColor ledOnColor,
        int index,
        float animPhase,
        float brightnessVariation)
    {
        if (!UseAdvancedEffects || index <= ledRect.Rect.Height * 0.7f)
            return;

        var paintPool = _paintPool;
        if (paintPool == null) return;

        float glowIntensity = GLOW_INTENSITY
            * (0.8f + MathF.Sin(animPhase * MathF.PI * 2) * 0.2f * brightnessVariation);

        using var glowPaint = paintPool.Get();
        if (glowPaint == null) return;

        glowPaint.Style = SKPaintStyle.Fill;
        glowPaint.Color = ledOnColor.WithAlpha(
            (byte)(glowIntensity * 160 * brightnessVariation));
        glowPaint.IsAntialias = UseAntiAlias;
        glowPaint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2);
        canvas.DrawRoundRect(ledRect, glowPaint);
    }

    private void RenderLedSurface(
        SKCanvas canvas,
        LedInfo ledInfo,
        SKRoundRect ledRect,
        SKColor color,
        bool isActive)
    {
        float inset = 1f;
        using var ledSurfaceRect = new SKRoundRect(
            new SKRect(
                ledInfo.X + inset,
                ledInfo.Y + inset,
                ledInfo.X + ledInfo.Width - inset,
                ledInfo.Y + ledInfo.Height - inset),
            Max(1, LED_ROUNDING_RADIUS - inset * 0.5f),
            Max(1, LED_ROUNDING_RADIUS - inset * 0.5f)
        );

        var paintPool = _paintPool;
        if (paintPool == null) return;

        using var ledPaint = paintPool.Get();
        if (ledPaint == null) return;

        ledPaint.Style = SKPaintStyle.Fill;
        ledPaint.IsAntialias = UseAntiAlias;

        if (isActive)
        {
            ApplyLedGradient(ledPaint, ledInfo, color);
        }
        else
        {
            ledPaint.Color = MultiplyColor(color, 0.10f);
        }

        canvas.DrawRoundRect(ledSurfaceRect, ledPaint);
    }

    private static void ApplyLedGradient(
        SKPaint paint,
        LedInfo ledInfo,
        SKColor color)
    {
        SKColor[] colors = [
            color,
            MultiplyColor(color, 0.9f),
            new(10, 10, 10, 220)
        ];
        float[] positions = [0.0f, 0.7f, 1.0f];

        paint.Shader = SKShader.CreateLinearGradient(
            new SKPoint(ledInfo.X, ledInfo.Y),
            new SKPoint(ledInfo.X, ledInfo.Y + ledInfo.Height),
            colors,
            positions,
            SKShaderTileMode.Clamp
        );
    }

    private void RenderLedHighlight(SKCanvas canvas, LedInfo ledInfo)
    {
        if (!UseAdvancedEffects)
            return;

        _highlightPath.Reset();

        float arcWidth = ledInfo.Width * 0.9f;
        float arcHeight = ledInfo.Height * 0.4f;
        float arcX = ledInfo.X + (ledInfo.Width - arcWidth) / 2;
        float arcY = ledInfo.Y + ledInfo.Height * 0.05f;

        _highlightPath.AddRoundRect(new SKRoundRect(
            new SKRect(
                arcX,
                arcY,
                arcX + arcWidth,
                arcY + arcHeight),
            LED_ROUNDING_RADIUS,
            LED_ROUNDING_RADIUS
        ));

        RenderHighlightFill(canvas);
        RenderHighlightStroke(canvas);
    }

    private void RenderHighlightFill(SKCanvas canvas)
    {
        var paintPool = _paintPool;
        if (paintPool == null) return;

        using var highlightFillPaint = paintPool.Get();
        if (highlightFillPaint == null) return;

        highlightFillPaint.Color = new SKColor(255, 255, 255, 50);
        highlightFillPaint.IsAntialias = UseAntiAlias;
        highlightFillPaint.Style = SKPaintStyle.Fill;
        canvas.DrawPath(_highlightPath, highlightFillPaint);
    }

    private void RenderHighlightStroke(SKCanvas canvas)
    {
        var paintPool = _paintPool;
        if (paintPool == null) return;

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
        int colorGroup = GetColorGroup(normalizedPosition);
        int variationIndex = index % 10;
        int colorIndex = colorGroup * 10 + variationIndex;

        if (colorIndex < _ledColorVariations.Count)
            return _ledColorVariations[colorIndex];

        return GetDefaultColor(colorGroup);
    }

    private static int GetColorGroup(float normalizedPosition)
    {
        if (normalizedPosition >= HIGH_LOUDNESS_THRESHOLD)
            return 2; // Red
        if (normalizedPosition >= MEDIUM_LOUDNESS_THRESHOLD)
            return 1; // Yellow
        return 0; // Green
    }

    private static SKColor GetDefaultColor(int colorGroup)
    {
        return colorGroup switch
        {
            2 => new SKColor(220, 30, 30),
            1 => new SKColor(230, 200, 0),
            _ => new SKColor(40, 200, 40)
        };
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

        SKColor[] gradientColors = [
            new SKColor(220, 220, 220),
            new SKColor(140, 140, 140)
        ];
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

        DrawMetalLineSet(
            canvas,
            texRandom,
            linePaint,
            150,
            new SKColor(210, 210, 210),
            10,
            20);

        DrawMetalLineSet(
            canvas,
            texRandom,
            linePaint,
            30,
            new SKColor(100, 100, 100),
            5,
            10);
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

        SKColor[] colors = [
            new SKColor(255, 255, 255, 20),
            new SKColor(0, 0, 0, 20)
        ];
        float[] positions = null!;

        gradientPaint.Shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(BRUSHED_METAL_TEXTURE_SIZE, BRUSHED_METAL_TEXTURE_SIZE),
            colors,
            positions,
            SKShaderTileMode.Clamp
        );

        canvas.DrawRect(
            0,
            0,
            BRUSHED_METAL_TEXTURE_SIZE,
            BRUSHED_METAL_TEXTURE_SIZE,
            gradientPaint);
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
                _overlayStateChanged = true;

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
        _staticBitmap?.Dispose();
        _ledColorVariations.Clear();
        _ledVariations.Clear();
    }
}