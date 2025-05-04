#nullable enable

using static SpectrumNet.Views.Renderers.LedMeterRenderer.Constants;

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
            public const bool LOW_USE_ADVANCED_EFFECTS = false;
            public const SKFilterMode LOW_FILTER_MODE = SKFilterMode.Nearest;
            public const SKMipmapMode LOW_MIPMAP_MODE = SKMipmapMode.None;

            public const bool MEDIUM_USE_ADVANCED_EFFECTS = true;
            public const SKFilterMode MEDIUM_FILTER_MODE = SKFilterMode.Linear;
            public const SKMipmapMode MEDIUM_MIPMAP_MODE = SKMipmapMode.Linear;

            public const bool HIGH_USE_ADVANCED_EFFECTS = true;
            public const SKFilterMode HIGH_FILTER_MODE = SKFilterMode.Linear;
            public const SKMipmapMode HIGH_MIPMAP_MODE = SKMipmapMode.Linear;
        }
    }

    private float _animationPhase;
    private float _vibrationOffset;
    private float _previousLoudness;
    private float _peakLoudness;
    private float? _cachedLoudness;
    private float[] _ledAnimationPhases = [];

    private new SKSamplingOptions _samplingOptions = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    private readonly SKPath _ledPath = new();
    private readonly SKPath _highlightPath = new();
    private readonly float[] _screwAngles = [45f, 120f, 10f, 80f];

    private SKBitmap? _screwBitmap;
    private SKBitmap? _brushedMetalBitmap;

    private readonly List<float> _ledVariations = new(30);
    private readonly List<SKColor> _ledColorVariations = new(30);
    private readonly object _loudnessLock = new();

    private int _currentWidth;
    private int _currentHeight;
    private int _ledCount = DEFAULT_LED_COUNT;
    private SKImageInfo _lastImageInfo;

    public override void Initialize()
    {
        Safe(
            () =>
            {
                base.Initialize();
                InitializeResources();
                ApplyQualitySettings();
                Log(LogLevel.Debug, LOG_PREFIX, "Initialized");
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.Initialize",
                ErrorMessage = "Failed to initialize renderer"
            }
        );
    }

    private void InitializeResources()
    {
        Safe(
            () =>
            {
                InitializeVariationsAndTextures();
                CreateCachedResources();
                ResetState();
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.InitializeResources",
                ErrorMessage = "Failed to initialize renderer resources"
            }
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
        using var linePaint = _paintPool.Get();
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

    public override void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium)
    {
        Safe(
            () =>
            {
                bool configChanged = _isOverlayActive != isOverlayActive || Quality != quality;
                base.Configure(isOverlayActive, quality);

                if (configChanged)
                {
                    OnConfigurationChanged();
                }
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.Configure",
                ErrorMessage = "Failed to configure renderer"
            }
        );
    }

    protected override void OnConfigurationChanged()
    {
        Safe(
            () =>
            {
                _smoothingFactor = _isOverlayActive ?
                    SMOOTHING_FACTOR_OVERLAY :
                    SMOOTHING_FACTOR_NORMAL;

                base.OnConfigurationChanged();
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.OnConfigurationChanged",
                ErrorMessage = "Failed to apply configuration changes"
            }
        );
    }

    protected override void ApplyQualitySettings()
    {
        Safe(
            () =>
            {
                base.ApplyQualitySettings();

                switch (Quality)
                {
                    case RenderQuality.Low:
                        _useAntiAlias = false;
                        _samplingOptions = new SKSamplingOptions(
                            Constants.Quality.LOW_FILTER_MODE,
                            Constants.Quality.LOW_MIPMAP_MODE);
                        _useAdvancedEffects = Constants.Quality.LOW_USE_ADVANCED_EFFECTS;
                        break;

                    case RenderQuality.Medium:
                        _useAntiAlias = true;
                        _samplingOptions = new SKSamplingOptions(
                            Constants.Quality.MEDIUM_FILTER_MODE,
                            Constants.Quality.MEDIUM_MIPMAP_MODE);
                        _useAdvancedEffects = Constants.Quality.MEDIUM_USE_ADVANCED_EFFECTS;
                        break;

                    case RenderQuality.High:
                        _useAntiAlias = true;
                        _samplingOptions = new SKSamplingOptions(
                            Constants.Quality.HIGH_FILTER_MODE,
                            Constants.Quality.HIGH_MIPMAP_MODE);
                        _useAdvancedEffects = Constants.Quality.HIGH_USE_ADVANCED_EFFECTS;
                        break;
                }

                OnQualitySettingsApplied();
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.ApplyQualitySettings",
                ErrorMessage = "Failed to apply quality settings"
            }
        );
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

        Safe(
            () =>
            {
                _lastImageInfo = info;

                if (canvas.QuickReject(new SKRect(0, 0, info.Width, info.Height)))
                    return;

                if (_currentWidth != info.Width || _currentHeight != info.Height)
                {
                    UpdateDimensions(info);
                }

                float loudness = UpdateState(spectrum);
                RenderMeter(canvas, info, loudness, _peakLoudness);
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.RenderEffect",
                ErrorMessage = "Error in RenderEffect method"
            }
        );
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
        if (!_isInitialized)
        {
            Log(LogLevel.Error, LOG_PREFIX, "Renderer is not initialized");
            return false;
        }

        if (canvas == null || spectrum == null || paint == null)
        {
            Log(LogLevel.Error, LOG_PREFIX, "Invalid render parameters: null values");
            return false;
        }

        if (info.Width <= 0 || info.Height <= 0)
        {
            Log(LogLevel.Error, LOG_PREFIX, $"Invalid image dimensions: {info.Width}x{info.Height}");
            return false;
        }

        if (spectrum.Length == 0)
        {
            Log(LogLevel.Warning, LOG_PREFIX, "Empty spectrum data");
            return false;
        }

        if (_disposed)
        {
            Log(LogLevel.Error, LOG_PREFIX, "Renderer is disposed");
            return false;
        }

        return true;
    }

    private float UpdateState(float[] spectrum)
    {
        UpdateAnimationPhase();

        float loudness = CalculateAndSmoothLoudness(spectrum);
        _cachedLoudness = loudness;

        UpdatePeakLoudness(loudness);
        UpdateVibrationEffect(loudness);

        return loudness;
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
            values = Abs(values);
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
        using var outerCasePaint = _paintPool.Get();

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
        outerCasePaint.IsAntialias = _useAntiAlias;

        canvas.DrawRoundRect(rect, CORNER_RADIUS, CORNER_RADIUS, outerCasePaint);

        RenderCaseHighlight(canvas, rect);
    }

    private void RenderCaseHighlight(SKCanvas canvas, SKRect rect)
    {
        using var highlightPaint = _paintPool.Get();
        highlightPaint.IsAntialias = _useAntiAlias;
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

        if (_useAdvancedEffects)
        {
            RenderPanelVignette(canvas, roundRect, rect);
        }
    }

    private void RenderPanelBackground(SKCanvas canvas, SKRoundRect roundRect)
    {
        if (_brushedMetalBitmap == null)
            return;

        using var panelPaint = _paintPool.Get();
        panelPaint.Shader = SKShader.CreateBitmap(
            _brushedMetalBitmap,
            SKShaderTileMode.Repeat,
            SKShaderTileMode.Repeat,
            SKMatrix.CreateScale(1.5f, 1.5f)
        );
        panelPaint.IsAntialias = _useAntiAlias;
        canvas.DrawRoundRect(roundRect, panelPaint);
    }

    private void RenderPanelVignette(SKCanvas canvas, SKRoundRect roundRect, SKRect rect)
    {
        using var vignettePaint = _paintPool.Get();
        vignettePaint.IsAntialias = _useAntiAlias;

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
        using var outerHighlightPaint = _paintPool.Get();
        outerHighlightPaint.IsAntialias = _useAntiAlias;
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
        using var outerShadowPaint = _paintPool.Get();
        outerShadowPaint.IsAntialias = _useAntiAlias;
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

        using var backgroundPaint = _paintPool.Get();
        backgroundPaint.IsAntialias = _useAntiAlias;
        backgroundPaint.Color = new SKColor(12, 12, 12);
        canvas.DrawRoundRect(recessRoundRect, backgroundPaint);

        if (_useAdvancedEffects)
        {
            RenderLedPanelShadow(canvas, recessRoundRect, rect);
        }

        using var borderPaint = _paintPool.Get();
        borderPaint.IsAntialias = _useAntiAlias;
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
        using var innerShadowPaint = _paintPool.Get();
        innerShadowPaint.IsAntialias = _useAntiAlias;

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

        using var labelPaint = _paintPool.Get();
        labelPaint.IsAntialias = _useAntiAlias;
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
        using var labelPaint = _paintPool.Get();
        labelPaint.IsAntialias = _useAntiAlias;

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

        if (_useAdvancedEffects)
        {
            DrawMinorTicks(canvas, meterRect);
        }
    }

    private void DrawTickBackground(SKCanvas canvas, SKRect panelRect, SKRect meterRect)
    {
        using var tickAreaPaint = _paintPool.Get();
        tickAreaPaint.IsAntialias = _useAntiAlias;
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
        using var tickPaint = _paintPool.Get();
        tickPaint.Style = SKPaintStyle.Stroke;
        tickPaint.StrokeWidth = 1;
        tickPaint.Color = SKColors.LightGray.WithAlpha(150);
        tickPaint.IsAntialias = _useAntiAlias;

        using var textPaint = _paintPool.Get();
        textPaint.Color = SKColors.LightGray.WithAlpha(180);
        textPaint.IsAntialias = _useAntiAlias;

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

            if (_useAdvancedEffects)
            {
                using var shadowPaint = _paintPool.Get();
                shadowPaint.Color = SKColors.Black.WithAlpha(80);
                shadowPaint.IsAntialias = _useAntiAlias;
                canvas.DrawText(dbValues[i], x + width - 7, yPos + 3.5f, SKTextAlign.Right, font, shadowPaint);
            }

            canvas.DrawText(dbValues[i], x + width - 8, yPos + 3, SKTextAlign.Right, font, textPaint);
        }
    }

    private void DrawMinorTicks(SKCanvas canvas, SKRect meterRect)
    {
        using var tickPaint = _paintPool.Get();
        tickPaint.Style = SKPaintStyle.Stroke;
        tickPaint.StrokeWidth = 1;
        tickPaint.Color = SKColors.LightGray.WithAlpha(80);
        tickPaint.IsAntialias = _useAntiAlias;

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

        using var ledBasePaint = _paintPool.Get();
        ledBasePaint.Style = SKPaintStyle.Fill;
        ledBasePaint.Color = new SKColor(8, 8, 8);
        ledBasePaint.IsAntialias = _useAntiAlias;
        canvas.DrawPath(_ledPath, ledBasePaint);

        float inset = 1f;
        using var ledSurfaceRect = new SKRoundRect(
            new SKRect(x + inset, y + inset, x + width - inset, y + height - inset),
            Max(1, LED_ROUNDING_RADIUS - inset * 0.5f),
            Max(1, LED_ROUNDING_RADIUS - inset * 0.5f)
        );

        using var inactiveLedPaint = _paintPool.Get();
        inactiveLedPaint.Style = SKPaintStyle.Fill;
        inactiveLedPaint.Color = MultiplyColor(color, 0.10f);
        inactiveLedPaint.IsAntialias = _useAntiAlias;
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

        using var ledBasePaint = _paintPool.Get();
        ledBasePaint.Style = SKPaintStyle.Fill;
        ledBasePaint.Color = new SKColor(8, 8, 8);
        ledBasePaint.IsAntialias = _useAntiAlias;
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
        if (!_useAdvancedEffects || index <= ledSurfaceRect.Rect.Height * 0.7f)
            return;

        float glowIntensity = GLOW_INTENSITY
            * (0.8f + MathF.Sin(animPhase * MathF.PI * 2) * 0.2f * brightnessVariation);

        using var glowPaint = _paintPool.Get();
        glowPaint.Style = SKPaintStyle.Fill;
        glowPaint.Color = ledOnColor.WithAlpha((byte)(glowIntensity * 160 * brightnessVariation));
        glowPaint.IsAntialias = _useAntiAlias;
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
        using var ledPaint = _paintPool.Get();
        ledPaint.Style = SKPaintStyle.Fill;
        ledPaint.IsAntialias = _useAntiAlias;

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
        if (!_useAdvancedEffects)
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

        using var highlightFillPaint = _paintPool.Get();
        highlightFillPaint.Color = new SKColor(255, 255, 255, 50);
        highlightFillPaint.IsAntialias = _useAntiAlias;
        highlightFillPaint.Style = SKPaintStyle.Fill;
        canvas.DrawPath(_highlightPath, highlightFillPaint);

        using var highlightPaint = _paintPool.Get();
        highlightPaint.Style = SKPaintStyle.Stroke;
        highlightPaint.StrokeWidth = 0.7f;
        highlightPaint.Color = new SKColor(255, 255, 255, 180);
        highlightPaint.IsAntialias = _useAntiAlias;
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
        Safe(
            () =>
            {
                base.OnInvalidateCachedResources();
                _cachedLoudness = null;
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.OnInvalidateCachedResources",
                ErrorMessage = "Failed to invalidate cached resources"
            }
        );
    }

    protected override void OnDispose()
    {
        Safe(
            () =>
            {
                DisposeManagedResources();
                base.OnDispose();
                Log(LogLevel.Debug, LOG_PREFIX, "Disposed");
            },
            new ErrorHandlingOptions
            {
                Source = $"{LOG_PREFIX}.OnDispose",
                ErrorMessage = "Error during disposal"
            }
        );
    }

    private void DisposeManagedResources()
    {
        _ledPath?.Dispose();
        _highlightPath?.Dispose();
        _screwBitmap?.Dispose();
        _brushedMetalBitmap?.Dispose();
        _paintPool?.Dispose();
        _ledColorVariations.Clear();
        _ledVariations.Clear();
    }
}