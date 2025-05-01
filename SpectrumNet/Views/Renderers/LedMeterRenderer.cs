#nullable enable

using SpectrumNet.Service.Enums;

namespace SpectrumNet.Views.Renderers;

/// <summary>
/// Renderer that visualizes spectrum data as an LED VU meter with realistic lighting and animations.
/// </summary>
public sealed class LedMeterRenderer : BaseSpectrumRenderer
{
    #region Singleton Pattern
    private static readonly Lazy<LedMeterRenderer> _instance = new(() => new LedMeterRenderer());
    private LedMeterRenderer() { } // Приватный конструктор
    public static LedMeterRenderer GetInstance() => _instance.Value;
    #endregion

    #region Constants
    private static class Constants
    {
        // Logging
        public const string LOG_PREFIX = "LedMeterRenderer";

        // Quality Settings
        public const RenderQuality DEFAULT_QUALITY = RenderQuality.Medium;

        // Animation & Smoothing
        public const float ANIMATION_SPEED = 0.015f;
        public const float SMOOTHING_FACTOR_NORMAL = 0.3f;
        public const float SMOOTHING_FACTOR_OVERLAY = 0.5f;
        public const float PEAK_DECAY_RATE = 0.04f;
        public const float GLOW_INTENSITY = 0.3f;

        // Loudness Thresholds & LED Count
        public const float MIN_LOUDNESS_THRESHOLD = 0.001f;
        public const float HIGH_LOUDNESS_THRESHOLD = 0.7f;
        public const float MEDIUM_LOUDNESS_THRESHOLD = 0.4f;
        public const int DEFAULT_LED_COUNT = 22;
        public const float LED_SPACING = 0.1f;

        // Panel & Geometry
        public const float LED_ROUNDING_RADIUS = 2.5f;
        public const float PANEL_PADDING = 12f;
        public const float TICK_MARK_WIDTH = 22f;
        public const float BEVEL_SIZE = 3f;
        public const float CORNER_RADIUS = 14f;
        public const int PERFORMANCE_INFO_BOTTOM_MARGIN = 30;

        // Screw & Texture Dimensions
        public const int SCREW_TEXTURE_SIZE = 24;
        public const int BRUSHED_METAL_TEXTURE_SIZE = 100;

        // Quality-specific settings
        public static class Quality
        {
            // Low quality settings
            public const bool LOW_USE_ADVANCED_EFFECTS = false;
            public const SKFilterMode LOW_FILTER_MODE = SKFilterMode.Nearest;
            public const SKMipmapMode LOW_MIPMAP_MODE = SKMipmapMode.None;

            // Medium quality settings
            public const bool MEDIUM_USE_ADVANCED_EFFECTS = true;
            public const SKFilterMode MEDIUM_FILTER_MODE = SKFilterMode.Linear;
            public const SKMipmapMode MEDIUM_MIPMAP_MODE = SKMipmapMode.Linear;

            // High quality settings
            public const bool HIGH_USE_ADVANCED_EFFECTS = true;
            public const SKFilterMode HIGH_FILTER_MODE = SKFilterMode.Linear;
            public const SKMipmapMode HIGH_MIPMAP_MODE = SKMipmapMode.Linear;
        }
    }
    #endregion

    #region Fields
    // Animation and state
    private float _animationPhase = 0f;
    private float _vibrationOffset = 0f;
    private float _previousLoudness = 0f;
    private float _peakLoudness = 0f;
    private float? _cachedLoudness;
    private float[] _ledAnimationPhases = Array.Empty<float>();

    // Quality-dependent settings
    private new bool _useAntiAlias = true;
    private new SKSamplingOptions _samplingOptions = new(SKFilterMode.Linear, SKMipmapMode.Linear);
    private new bool _useAdvancedEffects = true;

    // Cached paths and resources
    private readonly SKPath _ledPath = new();
    private readonly SKPath _highlightPath = new();
    private readonly float[] _screwAngles = { 45f, 120f, 10f, 80f };

    // Cached bitmaps & paints
    private SKBitmap? _screwBitmap;
    private SKBitmap? _brushedMetalBitmap;
    private readonly ObjectPool<SKPaint> _paintPool = new(() => new SKPaint(), paint => paint.Reset(), 5);

    // LED variations & color variations (precomputed)
    private readonly List<float> _ledVariations = new(30);
    private readonly List<SKColor> _ledColorVariations = new(30);

    // Synchronization
    private readonly SemaphoreSlim _renderSemaphore = new(1, 1);
    private readonly object _loudnessLock = new();
    private new bool _disposed;
    #endregion

    #region Initialization and Configuration
    /// <summary>
    /// Initializes the LED meter renderer and prepares resources for rendering.
    /// </summary>
    public override void Initialize()
    {
        Safe(() =>
        {
            base.Initialize();

            // Initialize variations and textures
            InitializeVariationsAndTextures();

            // Create cached resources
            CreateCachedResources();

            // Apply initial quality settings
            ApplyQualitySettings();

            _previousLoudness = 0f;
            _peakLoudness = 0f;

            Log(LogLevel.Debug, Constants.LOG_PREFIX, "Initialized");
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.Initialize",
            ErrorMessage = "Failed to initialize renderer"
        });
    }

    /// <summary>
    /// Configures the renderer with overlay status and quality settings.
    /// </summary>
    /// <param name="isOverlayActive">Indicates if the renderer is used in overlay mode.</param>
    /// <param name="quality">The rendering quality level.</param>
    public override void Configure(bool isOverlayActive, RenderQuality quality = Constants.DEFAULT_QUALITY)
    {
        Safe(() =>
        {
            base.Configure(isOverlayActive, quality);

            // Update smoothing factor based on overlay mode
            _smoothingFactor = isOverlayActive ?
                Constants.SMOOTHING_FACTOR_OVERLAY :
                Constants.SMOOTHING_FACTOR_NORMAL;

            // Update quality if needed
            if (_quality != quality)
            {
                _quality = quality;
                ApplyQualitySettings();
            }
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.Configure",
            ErrorMessage = "Failed to configure renderer"
        });
    }

    /// <summary>
    /// Applies quality settings based on the current quality level.
    /// </summary>
    protected override void ApplyQualitySettings()
    {
        Safe(() =>
        {
            base.ApplyQualitySettings();

            switch (_quality)
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
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.ApplyQualitySettings",
            ErrorMessage = "Failed to apply quality settings"
        });
    }

    /// <summary>
    /// Initializes LED variations and textures with fixed randomization for consistency.
    /// </summary>
    private void InitializeVariationsAndTextures()
    {
        Safe(() =>
        {
            // Precompute LED brightness variations and color variations with fixed seed
            Random fixedRandom = new(42);

            // Generate brightness variations
            for (int i = 0; i < 30; i++)
                _ledVariations.Add(0.85f + (float)fixedRandom.NextDouble() * 0.3f);

            // Base colors for LED groups
            SKColor greenBase = new SKColor(30, 200, 30);
            SKColor yellowBase = new SKColor(220, 200, 0);
            SKColor redBase = new SKColor(230, 30, 30);

            // Generate color variations for each group
            for (int j = 0; j < 10; j++)
            {
                _ledColorVariations.Add(new SKColor(
                    (byte)Clamp(greenBase.Red + fixedRandom.Next(-10, 10), 0, 255),
                    (byte)Clamp(greenBase.Green + fixedRandom.Next(-10, 10), 0, 255),
                    (byte)Clamp(greenBase.Blue + fixedRandom.Next(-10, 10), 0, 255)
                ));
            }
            for (int j = 0; j < 10; j++)
            {
                _ledColorVariations.Add(new SKColor(
                    (byte)Clamp(yellowBase.Red + fixedRandom.Next(-10, 10), 0, 255),
                    (byte)Clamp(yellowBase.Green + fixedRandom.Next(-10, 10), 0, 255),
                    (byte)Clamp(yellowBase.Blue + fixedRandom.Next(-10, 10), 0, 255)
                ));
            }
            for (int j = 0; j < 10; j++)
            {
                _ledColorVariations.Add(new SKColor(
                    (byte)Clamp(redBase.Red + fixedRandom.Next(-10, 10), 0, 255),
                    (byte)Clamp(redBase.Green + fixedRandom.Next(-10, 10), 0, 255),
                    (byte)Clamp(redBase.Blue + fixedRandom.Next(-10, 10), 0, 255)
                ));
            }
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.InitializeVariationsAndTextures",
            ErrorMessage = "Failed to initialize LED variations and textures"
        });
    }

    /// <summary>
    /// Creates and caches the textures and resources used for rendering.
    /// </summary>
    private void CreateCachedResources()
    {
        Safe(() =>
        {
            _screwBitmap = CreateScrewTexture();
            _brushedMetalBitmap = CreateBrushedMetalTexture();
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.CreateCachedResources",
            ErrorMessage = "Failed to create cached resources"
        });
    }

    /// <summary>
    /// Creates a texture for the screws in the panel.
    /// </summary>
    private SKBitmap CreateScrewTexture()
    {
        var bitmap = new SKBitmap(Constants.SCREW_TEXTURE_SIZE, Constants.SCREW_TEXTURE_SIZE);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        using var circlePaint = new SKPaint
        {
            IsAntialias = true,
            Style = Fill,
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(4, 4),
                new SKPoint(20, 20),
                new SKColor[] { new SKColor(220, 220, 220), new SKColor(140, 140, 140) },
                null,
                SKShaderTileMode.Clamp
            )
        };
        canvas.DrawCircle(12, 12, 10, circlePaint);

        using var slotPaint = new SKPaint
        {
            IsAntialias = true,
            Style = Stroke,
            StrokeWidth = 2.5f,
            Color = new SKColor(50, 50, 50, 180)
        };
        canvas.DrawLine(7, 12, 17, 12, slotPaint);

        using var highlightPaint = new SKPaint
        {
            IsAntialias = true,
            Style = Stroke,
            StrokeWidth = 1,
            Color = new SKColor(255, 255, 255, 100)
        };
        canvas.DrawArc(new SKRect(4, 4, 20, 20), 200, 160, false, highlightPaint);

        using var shadowPaint = new SKPaint
        {
            IsAntialias = true,
            Style = Stroke,
            StrokeWidth = 1.5f,
            Color = new SKColor(0, 0, 0, 100)
        };
        canvas.DrawCircle(12, 12, 9, shadowPaint);

        return bitmap;
    }

    /// <summary>
    /// Creates a brushed metal texture for the panel background.
    /// </summary>
    private SKBitmap CreateBrushedMetalTexture()
    {
        var bitmap = new SKBitmap(Constants.BRUSHED_METAL_TEXTURE_SIZE, Constants.BRUSHED_METAL_TEXTURE_SIZE);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(190, 190, 190));

        Random texRandom = new(42);
        using var linePaint = new SKPaint
        {
            IsAntialias = false,
            StrokeWidth = 1
        };

        for (int i = 0; i < 150; i++)
        {
            float y = (float)texRandom.NextDouble() * Constants.BRUSHED_METAL_TEXTURE_SIZE;
            linePaint.Color = new SKColor(210, 210, 210, (byte)texRandom.Next(10, 20));
            canvas.DrawLine(0, y, Constants.BRUSHED_METAL_TEXTURE_SIZE, y, linePaint);
        }
        for (int i = 0; i < 30; i++)
        {
            float y = (float)texRandom.NextDouble() * Constants.BRUSHED_METAL_TEXTURE_SIZE;
            linePaint.Color = new SKColor(100, 100, 100, (byte)texRandom.Next(5, 10));
            canvas.DrawLine(0, y, Constants.BRUSHED_METAL_TEXTURE_SIZE, y, linePaint);
        }
        using var gradientPaint = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
new SKPoint(Constants.BRUSHED_METAL_TEXTURE_SIZE, Constants.BRUSHED_METAL_TEXTURE_SIZE),
                new[] { new SKColor(255, 255, 255, 20), new SKColor(0, 0, 0, 20) },
                null,
                SKShaderTileMode.Clamp
            )
        };
        canvas.DrawRect(0, 0, Constants.BRUSHED_METAL_TEXTURE_SIZE, Constants.BRUSHED_METAL_TEXTURE_SIZE, gradientPaint);

        return bitmap;
    }
    #endregion

    #region Rendering
    /// <summary>
    /// Renders the LED meter visualization on the canvas using spectrum data.
    /// </summary>
    public override void Render(
        SKCanvas? canvas,
        float[]? spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint? paint,
        Action<SKCanvas, SKImageInfo>? drawPerformanceInfo)
    {
        // Validate rendering parameters
        if (!ValidateRenderParameters(canvas, spectrum, info, paint))
        {
            drawPerformanceInfo?.Invoke(canvas!, info);
            return;
        }

        // Quick reject if canvas area is not visible
        if (canvas!.QuickReject(new SKRect(0, 0, info.Width, info.Height)))
        {
            drawPerformanceInfo?.Invoke(canvas, info);
            return;
        }

        Safe(() =>
        {
            float loudness = 0f;
            bool semaphoreAcquired = false;

            try
            {
                // Try to acquire semaphore for updating animation state
                semaphoreAcquired = _renderSemaphore.Wait(0);

                if (semaphoreAcquired)
                {
                    // Update animation phase
                    _animationPhase = (_animationPhase + Constants.ANIMATION_SPEED) % 1.0f;

                    // Calculate and smooth loudness
                    loudness = CalculateAndSmoothLoudness(spectrum!);
                    _cachedLoudness = loudness;

                    // Update peak loudness with decay
                    if (loudness > _peakLoudness)
                        _peakLoudness = loudness;
                    else
                        _peakLoudness = Max(0, _peakLoudness - Constants.PEAK_DECAY_RATE);

                    // Apply vibration effect for high loudness
                    if (loudness > Constants.HIGH_LOUDNESS_THRESHOLD)
                    {
                        float vibrationIntensity = (loudness - Constants.HIGH_LOUDNESS_THRESHOLD) /
                                                   (1 - Constants.HIGH_LOUDNESS_THRESHOLD);
                        _vibrationOffset = (float)Sin(_animationPhase * PI * 10) * 0.8f * vibrationIntensity;
                    }
                    else
                    {
                        _vibrationOffset = 0;
                    }
                }
                else
                {
                    // Use cached loudness if semaphore is not available
                    lock (_loudnessLock)
                        loudness = _cachedLoudness ?? CalculateAndSmoothLoudness(spectrum!);
                }
            }
            finally
            {
                if (semaphoreAcquired)
                    _renderSemaphore.Release();
            }

            // Render LED meter content
            canvas.Save();
            RenderMeterContent(canvas, info, loudness, _peakLoudness, paint!);
            canvas.Restore();

            // Render performance info if provided
            if (drawPerformanceInfo != null)
            {
                canvas.Save();
                canvas.Translate(0, info.Height - Constants.PERFORMANCE_INFO_BOTTOM_MARGIN);
                drawPerformanceInfo(canvas, info);
                canvas.Restore();
            }
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.Render",
            ErrorMessage = "Error during rendering"
        });
    }

    /// <summary>
    /// Validates all render parameters before processing.
    /// </summary>
    private bool ValidateRenderParameters(SKCanvas? canvas, float[]? spectrum, SKImageInfo info, SKPaint? paint)
    {
        if (_disposed)
        {
            Log(LogLevel.Error, Constants.LOG_PREFIX, "Renderer is disposed");
            return false;
        }

        if (canvas == null || spectrum == null || paint == null)
        {
            Log(LogLevel.Error, Constants.LOG_PREFIX, "Invalid render parameters: null values");
            return false;
        }

        if (info.Width <= 0 || info.Height <= 0)
        {
            Log(LogLevel.Error, Constants.LOG_PREFIX, $"Invalid image dimensions: {info.Width}x{info.Height}");
            return false;
        }

        if (spectrum.Length == 0)
        {
            Log(LogLevel.Warning, Constants.LOG_PREFIX, "Empty spectrum data");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Calculates and smooths the loudness from spectrum data.
    /// </summary>
    [MethodImpl(AggressiveInlining)]
    private float CalculateAndSmoothLoudness(float[] spectrum)
    {
        float rawLoudness = CalculateLoudness(spectrum.AsSpan());
        float smoothedLoudness = _previousLoudness + (rawLoudness - _previousLoudness) * _smoothingFactor;
        smoothedLoudness = Clamp(smoothedLoudness, Constants.MIN_LOUDNESS_THRESHOLD, 1f);
        _previousLoudness = smoothedLoudness;
        return smoothedLoudness;
    }

    /// <summary>
    /// Calculates loudness from spectrum data using SIMD optimization when available.
    /// </summary>
    [MethodImpl(AggressiveOptimization)]
    private float CalculateLoudness(ReadOnlySpan<float> spectrum)
    {
        if (spectrum.IsEmpty)
            return 0f;

        // Optimization using SIMD for summing absolute values
        float sum = 0f;
        int vectorSize = Vector<float>.Count;
        int i = 0;

        if (IsHardwareAccelerated && spectrum.Length >= vectorSize)
        {
            Vector<float> sumVector = Vector<float>.Zero;

            for (; i <= spectrum.Length - vectorSize; i += vectorSize)
            {
                var vec = new Vector<float>(spectrum.Slice(i));
                vec = Abs(vec);
                sumVector += vec;
            }

            for (int j = 0; j < vectorSize; j++)
            {
                sum += sumVector[j];
            }
        }

        // Process remaining elements
        for (; i < spectrum.Length; i++)
        {
            sum += Abs(spectrum[i]);
        }

        return Clamp(sum / spectrum.Length, 0f, 1f);
    }
    #endregion

    #region Rendering Implementation
    /// <summary>
    /// Renders the LED meter content with all visual elements.
    /// </summary>
    private void RenderMeterContent(SKCanvas canvas, SKImageInfo info, float loudness, float peakLoudness, SKPaint basePaint)
    {
        if (loudness < Constants.MIN_LOUDNESS_THRESHOLD)
            return;

        canvas.Save();
        canvas.Translate(_vibrationOffset, 0);

        // Define panel dimensions
        float outerPadding = 5f;
        float panelLeft = Constants.PANEL_PADDING;
        float panelTop = Constants.PANEL_PADDING;
        float panelWidth = info.Width - Constants.PANEL_PADDING * 2;
        float panelHeight = info.Height - Constants.PANEL_PADDING * 2;

        SKRect outerRect = new SKRect(
            outerPadding,
            outerPadding,
            info.Width - outerPadding,
            info.Height - outerPadding
        );

        // Render outer case, panel, and labels
        RenderOuterCase(canvas, outerRect);
        SKRect panelRect = new SKRect(panelLeft, panelTop, panelLeft + panelWidth, panelTop + panelHeight);
        RenderPanel(canvas, panelRect);
        RenderLabels(canvas, panelRect);

        // Calculate LED meter dimensions
        float meterLeft = panelLeft + Constants.TICK_MARK_WIDTH + 5;
        float meterWidth = panelWidth - (Constants.TICK_MARK_WIDTH + 15);
        float meterHeight = panelHeight - 25;
        float meterTop = panelTop + 20;

        // Determine LED count based on available space
        int ledCount = Max(10, Min(Constants.DEFAULT_LED_COUNT, (int)(meterHeight / 12)));

        // Initialize LED animation phases if needed
        if (_ledAnimationPhases.Length != ledCount)
        {
            _ledAnimationPhases = new float[ledCount];
            Random phaseRandom = new(42);
            for (int i = 0; i < ledCount; i++)
                _ledAnimationPhases[i] = (float)phaseRandom.NextDouble();
        }

        // Calculate LED dimensions
        float totalLedSpace = meterHeight * 0.95f;
        float totalSpacingSpace = meterHeight * 0.05f;
        float ledHeight = (totalLedSpace - totalSpacingSpace) / ledCount;
        float spacing = totalSpacingSpace / (ledCount - 1);
        float ledWidth = meterWidth;

        // Render recessed panel for LEDs
        RenderRecessedLedPanel(canvas, meterLeft - 3, meterTop - 3, meterWidth + 6, meterHeight + 6);

        // Calculate active LED count and peak LED
        int activeLedCount = (int)(loudness * ledCount);
        int peakLedIndex = (int)(peakLoudness * ledCount);

        // Render tick marks, LED array, and decorative screws
        RenderTickMarks(canvas, panelLeft, meterTop, Constants.TICK_MARK_WIDTH, meterHeight);
        RenderLedArray(canvas, meterLeft, meterTop, ledWidth, ledHeight, spacing, ledCount, activeLedCount, peakLedIndex);
        RenderFixedScrews(canvas, panelRect);

        canvas.Restore();
    }

    /// <summary>
    /// Renders the outer case of the meter with a metallic finish.
    /// </summary>
    private void RenderOuterCase(SKCanvas canvas, SKRect rect)
    {
        // Create outer case paint if needed
        using var outerCasePaint = _paintPool.Get();
        outerCasePaint.Shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(0, rect.Height),
            new[] { new SKColor(70, 70, 70), new SKColor(40, 40, 40), new SKColor(55, 55, 55) },
            new[] { 0.0f, 0.7f, 1.0f },
            SKShaderTileMode.Clamp
        );
        outerCasePaint.IsAntialias = _useAntiAlias;

        // Draw rounded outer case
        canvas.DrawRoundRect(rect, Constants.CORNER_RADIUS, Constants.CORNER_RADIUS, outerCasePaint);

        // Add highlight to the top edge
        using var highlightPaint = _paintPool.Get();
        highlightPaint.IsAntialias = _useAntiAlias;
        highlightPaint.Style = Stroke;
        highlightPaint.StrokeWidth = 1.2f;
        highlightPaint.Color = new SKColor(255, 255, 255, 40);

        canvas.DrawLine(
            rect.Left + Constants.CORNER_RADIUS, rect.Top + 1.5f,
            rect.Right - Constants.CORNER_RADIUS, rect.Top + 1.5f,
            highlightPaint
        );
    }

    /// <summary>
    /// Renders the main panel with brushed metal texture.
    /// </summary>
    private void RenderPanel(SKCanvas canvas, SKRect rect)
    {
        using var roundRect = new SKRoundRect(rect, Constants.CORNER_RADIUS - 4, Constants.CORNER_RADIUS - 4);

        // Create panel paint with brushed metal texture
        using var panelPaint = _paintPool.Get();
        if (_brushedMetalBitmap != null)
        {
            panelPaint.Shader = SKShader.CreateBitmap(
                _brushedMetalBitmap,
                SKShaderTileMode.Repeat,
                SKShaderTileMode.Repeat,
                SKMatrix.CreateScale(1.5f, 1.5f)
            );
            panelPaint.IsAntialias = _useAntiAlias;
            canvas.DrawRoundRect(roundRect, panelPaint);
        }

        // Add bevel effect to panel
        RenderPanelBevel(canvas, roundRect);

        // Add vignette effect if advanced effects are enabled
        if (_useAdvancedEffects)
        {
            using var vignettePaint = _paintPool.Get();
            vignettePaint.IsAntialias = _useAntiAlias;
            vignettePaint.Shader = SKShader.CreateRadialGradient(
                new SKPoint(rect.MidX, rect.MidY),
                Max(rect.Width, rect.Height) * 0.75f,
                new[] { new SKColor(0, 0, 0, 0), new SKColor(0, 0, 0, 30) },
                null,
                SKShaderTileMode.Clamp
            );
            canvas.DrawRoundRect(roundRect, vignettePaint);
        }
    }

    /// <summary>
    /// Renders the beveled edge effect for the panel.
    /// </summary>
    private void RenderPanelBevel(SKCanvas canvas, SKRoundRect roundRect)
    {
        using var outerHighlightPaint = _paintPool.Get();
        outerHighlightPaint.IsAntialias = _useAntiAlias;
        outerHighlightPaint.Style = Stroke;
        outerHighlightPaint.StrokeWidth = Constants.BEVEL_SIZE;
        outerHighlightPaint.Color = new SKColor(255, 255, 255, 120);

        using var highlightPath = new SKPath();
        float radOffset = Constants.BEVEL_SIZE / 2;
        highlightPath.MoveTo(roundRect.Rect.Left + Constants.CORNER_RADIUS, roundRect.Rect.Bottom - radOffset);
        highlightPath.ArcTo(new SKRect(
            roundRect.Rect.Left + radOffset,
            roundRect.Rect.Bottom - Constants.CORNER_RADIUS * 2 + radOffset,
            roundRect.Rect.Left + Constants.CORNER_RADIUS * 2 - radOffset,
            roundRect.Rect.Bottom),
            90, 90, false);
        highlightPath.LineTo(roundRect.Rect.Left + radOffset, roundRect.Rect.Top + Constants.CORNER_RADIUS);
        highlightPath.ArcTo(new SKRect(
            roundRect.Rect.Left + radOffset,
            roundRect.Rect.Top + radOffset,
            roundRect.Rect.Left + Constants.CORNER_RADIUS * 2 - radOffset,
            roundRect.Rect.Top + Constants.CORNER_RADIUS * 2 - radOffset),
            180, 90, false);
        highlightPath.LineTo(roundRect.Rect.Right - Constants.CORNER_RADIUS, roundRect.Rect.Top + radOffset);
        canvas.DrawPath(highlightPath, outerHighlightPaint);

        using var outerShadowPaint = _paintPool.Get();
        outerShadowPaint.IsAntialias = _useAntiAlias;
        outerShadowPaint.Style = Stroke;
        outerShadowPaint.StrokeWidth = Constants.BEVEL_SIZE;
        outerShadowPaint.Color = new SKColor(0, 0, 0, 90);

        using var shadowPath = new SKPath();
        shadowPath.MoveTo(roundRect.Rect.Right - Constants.CORNER_RADIUS, roundRect.Rect.Top + radOffset);
        shadowPath.ArcTo(new SKRect(
            roundRect.Rect.Right - Constants.CORNER_RADIUS * 2 + radOffset,
            roundRect.Rect.Top + radOffset,
            roundRect.Rect.Right - radOffset,
            roundRect.Rect.Top + Constants.CORNER_RADIUS * 2 - radOffset),
            270, 90, false);
        shadowPath.LineTo(roundRect.Rect.Right - radOffset, roundRect.Rect.Bottom - Constants.CORNER_RADIUS);
        shadowPath.ArcTo(new SKRect(
            roundRect.Rect.Right - Constants.CORNER_RADIUS * 2 + radOffset,
            roundRect.Rect.Bottom - Constants.CORNER_RADIUS * 2 + radOffset,
            roundRect.Rect.Right - radOffset,
            roundRect.Rect.Bottom - radOffset),
            0, 90, false);
        shadowPath.LineTo(roundRect.Rect.Left + Constants.CORNER_RADIUS, roundRect.Rect.Bottom - radOffset);
        canvas.DrawPath(shadowPath, outerShadowPaint);
    }

    /// <summary>
    /// Renders the recessed panel that contains the LED array.
    /// </summary>
    private void RenderRecessedLedPanel(SKCanvas canvas, float x, float y, float width, float height)
    {
        float recessRadius = 6f;
        SKRect recessRect = new SKRect(x, y, x + width, y + height);
        using var recessRoundRect = new SKRoundRect(recessRect, recessRadius, recessRadius);

        using var backgroundPaint = _paintPool.Get();
        backgroundPaint.IsAntialias = _useAntiAlias;
        backgroundPaint.Color = new SKColor(12, 12, 12);
        canvas.DrawRoundRect(recessRoundRect, backgroundPaint);

        if (_useAdvancedEffects)
        {
            using var innerShadowPaint = _paintPool.Get();
            innerShadowPaint.IsAntialias = _useAntiAlias;
            innerShadowPaint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(x, y),
                new SKPoint(x, y + height * 0.2f),
                new SKColor[] { new SKColor(0, 0, 0, 120), new SKColor(0, 0, 0, 0) },
                null,
                SKShaderTileMode.Clamp
            );
            canvas.DrawRoundRect(recessRoundRect, innerShadowPaint);
        }

        using var borderPaint = _paintPool.Get();
        borderPaint.IsAntialias = _useAntiAlias;
        borderPaint.Style = Stroke;
        borderPaint.StrokeWidth = 1;
        borderPaint.Color = new SKColor(0, 0, 0, 180);
        canvas.DrawRoundRect(recessRoundRect, borderPaint);
    }

    /// <summary>
    /// Renders the decorative screws on the panel corners.
    /// </summary>
    private void RenderFixedScrews(SKCanvas canvas, SKRect panelRect)
    {
        if (_screwBitmap == null)
            return;

        float cornerOffset = Constants.CORNER_RADIUS - 4;

        // Draw screws at each corner with different angles
        DrawScrew(canvas, panelRect.Left + cornerOffset, panelRect.Top + cornerOffset, _screwAngles[0]);
        DrawScrew(canvas, panelRect.Right - cornerOffset, panelRect.Top + cornerOffset, _screwAngles[1]);
        DrawScrew(canvas, panelRect.Left + cornerOffset, panelRect.Bottom - cornerOffset, _screwAngles[2]);
        DrawScrew(canvas, panelRect.Right - cornerOffset, panelRect.Bottom - cornerOffset, _screwAngles[3]);

        // Draw branding text
        float labelX = panelRect.Right - 65;
        float labelY = panelRect.Bottom - 8;

        using var labelPaint = _paintPool.Get();
        labelPaint.IsAntialias = _useAntiAlias;
        labelPaint.Color = new SKColor(230, 230, 230, 120);

        var font = new SKFont(
            SKTypeface.FromFamilyName("Arial", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright),
            8
        );

        canvas.DrawText("SpectrumNet™ Audio", labelX, labelY, SKTextAlign.Right, font, labelPaint);
    }

    /// <summary>
    /// Draws a single screw texture at the specified position and angle.
    /// </summary>
    private void DrawScrew(SKCanvas canvas, float x, float y, float angle)
    {
        if (_screwBitmap == null)
            return;

        canvas.Save();
        canvas.Translate(x, y);
        canvas.RotateDegrees(angle);
        canvas.Translate(-12, -12);
        canvas.DrawBitmap(_screwBitmap, 0, 0);
        canvas.Restore();
    }

    /// <summary>
    /// Renders the labels and text on the panel.
    /// </summary>
    private void RenderLabels(SKCanvas canvas, SKRect panelRect)
    {
        float labelX = panelRect.Left + 10;
        float labelY = panelRect.Top + 14;

        using var labelPaint = _paintPool.Get();
        labelPaint.IsAntialias = _useAntiAlias;

        var boldTypeface = SKTypeface.FromFamilyName("Arial", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        var font14 = new SKFont(boldTypeface, 14);
        var font10 = new SKFont(boldTypeface, 10);
        var font8 = new SKFont(boldTypeface, 8);

        // Draw "VU" text with shadow
        labelPaint.Color = new SKColor(30, 30, 30, 180);
        canvas.DrawText("VU", labelX + 1, labelY + 1, SKTextAlign.Left, font14, labelPaint);
        labelPaint.Color = new SKColor(230, 230, 230, 200);
        canvas.DrawText("VU", labelX, labelY, SKTextAlign.Left, font14, labelPaint);

        // Draw "dB METER" text
        labelPaint.Color = new SKColor(200, 200, 200, 150);
        canvas.DrawText("dB METER", labelX + 30, labelY, SKTextAlign.Left, font10, labelPaint);

        // Draw "PRO SERIES" text
        labelPaint.Color = new SKColor(200, 200, 200, 120);
        canvas.DrawText("PRO SERIES", panelRect.Right - 10, panelRect.Top + 14, SKTextAlign.Right, font8, labelPaint);

        // Draw "dB" label
        canvas.DrawText("dB", panelRect.Left + 10, panelRect.Bottom - 10, SKTextAlign.Left, font8, labelPaint);
    }

    /// <summary>
    /// Renders the tick marks and dB labels on the side of the meter.
    /// </summary>
    private void RenderTickMarks(SKCanvas canvas, float x, float y, float width, float height)
    {
        // Draw tick mark background area
        using var tickAreaPaint = _paintPool.Get();
        tickAreaPaint.IsAntialias = _useAntiAlias;
        tickAreaPaint.Color = new SKColor(30, 30, 30, 70);

        SKRect tickAreaRect = new SKRect(x, y, x + width - 2, y + height);
        canvas.DrawRect(tickAreaRect, tickAreaPaint);

        // Setup tick mark paint
        using var tickPaint = _paintPool.Get();
        tickPaint.Style = Stroke;
        tickPaint.StrokeWidth = 1;
        tickPaint.Color = SKColors.LightGray.WithAlpha(150);
        tickPaint.IsAntialias = _useAntiAlias;

        // Setup text paint and font
        using var textPaint = _paintPool.Get();
        textPaint.Color = SKColors.LightGray.WithAlpha(180);
        textPaint.IsAntialias = _useAntiAlias;

        var font = new SKFont(
            SKTypeface.FromFamilyName("Arial", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright),
            9
        );

        // Draw dB markers and labels
        string[] dbValues = { "0", "-3", "-6", "-10", "-20", "-40" };
        float[] dbPositions = { 1.0f, 0.85f, 0.7f, 0.55f, 0.3f, 0.0f };

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

        // Draw minor tick marks
        if (_useAdvancedEffects)
        {
            tickPaint.Color = tickPaint.Color.WithAlpha(80);
            for (int i = 0; i < 10; i++)
            {
                float ratio = i / 10f;
                float yPos = y + ratio * height;
                canvas.DrawLine(x, yPos, x + width * 0.6f, yPos, tickPaint);
            }
        }
    }

    /// <summary>
    /// Renders the array of LEDs with active and inactive states.
    /// </summary>
    private void RenderLedArray(
        SKCanvas canvas,
        float x,
        float y,
        float width,
        float height,
        float spacing,
        int ledCount,
        int activeLedCount,
        int peakLedIndex)
    {
        // First pass: render inactive LEDs
        for (int i = 0; i < ledCount; i++)
        {
            float normalizedPosition = (float)i / ledCount;
            float ledY = y + (ledCount - i - 1) * (height + spacing);
            SKColor color = GetLedColorForPosition(normalizedPosition, i);
            bool isActive = i < activeLedCount;
            bool isPeak = i == peakLedIndex;

            if (!isActive && !isPeak)
            {
                RenderInactiveLed(canvas, x, ledY, width, height, color);
            }
        }

        // Second pass: render active LEDs
        for (int i = 0; i < ledCount; i++)
        {
            float normalizedPosition = (float)i / ledCount;
            float ledY = y + (ledCount - i - 1) * (height + spacing);
            SKColor color = GetLedColorForPosition(normalizedPosition, i);
            bool isActive = i < activeLedCount;
            bool isPeak = i == peakLedIndex;

            if (isActive || isPeak)
            {
                // Update animation phase for this LED
                _ledAnimationPhases[i] = (_ledAnimationPhases[i] + Constants.ANIMATION_SPEED *
                                         (0.5f + normalizedPosition)) % 1.0f;

                RenderActiveLed(canvas, x, ledY, width, height, color, isActive, isPeak, i);
            }
        }
    }

    /// <summary>
    /// Renders an inactive (off) LED.
    /// </summary>
    private void RenderInactiveLed(SKCanvas canvas, float x, float y, float width, float height, SKColor color)
    {
        _ledPath.Reset();
        var ledRect = new SKRoundRect(
            new SKRect(x, y, x + width, y + height),
            Constants.LED_ROUNDING_RADIUS,
            Constants.LED_ROUNDING_RADIUS
        );
        _ledPath.AddRoundRect(ledRect);

        // Draw LED base
        using var ledBasePaint = _paintPool.Get();
        ledBasePaint.Style = Fill;
        ledBasePaint.Color = new SKColor(8, 8, 8);
        ledBasePaint.IsAntialias = _useAntiAlias;
        canvas.DrawPath(_ledPath, ledBasePaint);

        // Draw LED surface
        float inset = 1f;
        var ledSurfaceRect = new SKRoundRect(
            new SKRect(x + inset, y + inset, x + width - inset, y + height - inset),
            Max(1, Constants.LED_ROUNDING_RADIUS - inset * 0.5f),
            Max(1, Constants.LED_ROUNDING_RADIUS - inset * 0.5f)
        );

        using var inactiveLedPaint = _paintPool.Get();
        inactiveLedPaint.Style = Fill;
        inactiveLedPaint.Color = MultiplyColor(color, 0.10f);
        inactiveLedPaint.IsAntialias = _useAntiAlias;
        canvas.DrawRoundRect(ledSurfaceRect, inactiveLedPaint);
    }

    /// <summary>
    /// Renders an active (on) LED with glow and highlight effects.
    /// </summary>
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
        // Get variation factors for this LED
        float brightnessVariation = _ledVariations[index % _ledVariations.Count];
        float animPhase = _ledAnimationPhases[index % _ledAnimationPhases.Length];

        // Draw LED base
        _ledPath.Reset();
        var ledRect = new SKRoundRect(
            new SKRect(x, y, x + width, y + height),
            Constants.LED_ROUNDING_RADIUS,
            Constants.LED_ROUNDING_RADIUS
        );
        _ledPath.AddRoundRect(ledRect);

        using var ledBasePaint = _paintPool.Get();
        ledBasePaint.Style = Fill;
        ledBasePaint.Color = new SKColor(8, 8, 8);
        ledBasePaint.IsAntialias = _useAntiAlias;
        canvas.DrawPath(_ledPath, ledBasePaint);

        // Define LED surface dimensions
        float inset = 1f;
        var ledSurfaceRect = new SKRoundRect(
            new SKRect(x + inset, y + inset, x + width - inset, y + height - inset),
            Max(1, Constants.LED_ROUNDING_RADIUS - inset * 0.5f),
            Max(1, Constants.LED_ROUNDING_RADIUS - inset * 0.5f)
        );

        // Determine LED color and pulse effect
        SKColor ledOnColor = color;
        SKColor ledOffColor = new SKColor(10, 10, 10, 220);

        float pulse = isPeak ?
            0.7f + (float)Sin(animPhase * PI * 2) * 0.3f :
            brightnessVariation;

        ledOnColor = MultiplyColor(ledOnColor, pulse);

        // Draw glow effect for high-intensity LEDs if advanced effects are enabled
        if (_useAdvancedEffects && index > ledRect.Rect.Height * 0.7f)
        {
            float glowIntensity = Constants.GLOW_INTENSITY *
                                 (0.8f + (float)Sin(animPhase * PI * 2) * 0.2f * brightnessVariation);

            using var glowPaint = _paintPool.Get();
            glowPaint.Style = Fill;
            glowPaint.Color = ledOnColor.WithAlpha((byte)(glowIntensity * 160 * brightnessVariation));
            glowPaint.IsAntialias = _useAntiAlias;
            glowPaint.MaskFilter = SKMaskFilter.CreateBlur(Normal, 2);
            canvas.DrawRoundRect(ledSurfaceRect, glowPaint);
        }

        // Draw LED with gradient effect
        using var ledPaint = _paintPool.Get();
        ledPaint.Style = Fill;
        ledPaint.IsAntialias = _useAntiAlias;
        ledPaint.Shader = SKShader.CreateLinearGradient(
            new SKPoint(x, y),
            new SKPoint(x, y + height),
            new[] { ledOnColor, MultiplyColor(ledOnColor, 0.9f), ledOffColor },
            new[] { 0.0f, 0.7f, 1.0f },
            SKShaderTileMode.Clamp
        );
        canvas.DrawRoundRect(ledSurfaceRect, ledPaint);

        // Draw highlight effect
        if (_useAdvancedEffects)
        {
            _highlightPath.Reset();
            float arcWidth = width * 0.9f;
            float arcHeight = height * 0.4f;
            float arcX = x + (width - arcWidth) / 2;
            float arcY = y + height * 0.05f;

            _highlightPath.AddRoundRect(new SKRoundRect(
                new SKRect(arcX, arcY, arcX + arcWidth, arcY + arcHeight),
                Constants.LED_ROUNDING_RADIUS,
                Constants.LED_ROUNDING_RADIUS
            ));

            using var highlightFillPaint = _paintPool.Get();
            highlightFillPaint.Color = new SKColor(255, 255, 255, 50);
            highlightFillPaint.IsAntialias = _useAntiAlias;
            highlightFillPaint.Style = Fill;
            canvas.DrawPath(_highlightPath, highlightFillPaint);

            using var highlightPaint = _paintPool.Get();
            highlightPaint.Style = Stroke;
            highlightPaint.StrokeWidth = 0.7f;
            highlightPaint.Color = new SKColor(255, 255, 255, 180);
            highlightPaint.IsAntialias = _useAntiAlias;
            canvas.DrawPath(_highlightPath, highlightPaint);
        }
    }

    /// <summary>
    /// Determines the appropriate color for an LED based on its position in the meter.
    /// </summary>
    private SKColor GetLedColorForPosition(float normalizedPosition, int index)
    {
        // Determine color group based on position
        int colorGroup;
        if (normalizedPosition >= Constants.HIGH_LOUDNESS_THRESHOLD)
            colorGroup = 2; // Red
        else if (normalizedPosition >= Constants.MEDIUM_LOUDNESS_THRESHOLD)
            colorGroup = 1; // Yellow
        else
            colorGroup = 0; // Green

        // Apply color variation within group
        int variationIndex = index % 10;
        int colorIndex = colorGroup * 10 + variationIndex;

        if (colorIndex < _ledColorVariations.Count)
            return _ledColorVariations[colorIndex];

        // Fallback colors if variations aren't available
        if (colorGroup == 2) return new SKColor(220, 30, 30);
        if (colorGroup == 1) return new SKColor(230, 200, 0);
        return new SKColor(40, 200, 40);
    }

    /// <summary>
    /// Multiplies the RGB components of a color by a factor while preserving alpha.
    /// </summary>
    [MethodImpl(AggressiveInlining)]
    private SKColor MultiplyColor(SKColor color, float factor)
    {
        return new SKColor(
            (byte)Clamp(color.Red * factor, 0, 255),
            (byte)Clamp(color.Green * factor, 0, 255),
            (byte)Clamp(color.Blue * factor, 0, 255),
            color.Alpha
        );
    }
    #endregion

    #region Disposal
    /// <summary>
    /// Disposes of resources used by the renderer.
    /// </summary>
    public override void Dispose()
    {
        if (!_disposed)
        {
            Safe(() =>
            {
                // Dispose synchronization primitives
                _renderSemaphore?.Dispose();

                // Dispose paths
                _ledPath?.Dispose();
                _highlightPath?.Dispose();

                // Dispose bitmaps
                _screwBitmap?.Dispose();
                _brushedMetalBitmap?.Dispose();

                // Dispose object pools
                _paintPool?.Dispose();

                // Clean up cached data
                _ledColorVariations.Clear();
                _ledVariations.Clear();

                // Call base implementation
                base.Dispose();
            }, new ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.Dispose",
                ErrorMessage = "Error during disposal"
            });

            _disposed = true;
            Log(LogLevel.Debug, Constants.LOG_PREFIX, "Disposed");
        }
    }
    #endregion
}