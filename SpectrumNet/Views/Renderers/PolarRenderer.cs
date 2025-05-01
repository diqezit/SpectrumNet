#nullable enable

using SpectrumNet.Service.Enums;

namespace SpectrumNet.Views.Renderers;

/// <summary>
/// Renderer that visualizes spectrum data as a polar graph with animated effects.
/// </summary>
public sealed class PolarRenderer : BaseSpectrumRenderer
{
    #region Singleton Pattern
    private static readonly Lazy<PolarRenderer> _instance = new(() => new PolarRenderer());
    private PolarRenderer() { } // Приватный конструктор
    public static PolarRenderer GetInstance() => _instance.Value;
    #endregion

    #region Constants
    private static class Constants
    {
        // Logging
        public const string LOG_PREFIX = "PolarRenderer";

        // Rendering core properties
        public const float MIN_RADIUS = 30f;            // Minimum base radius for visualization
        public const float RADIUS_MULTIPLIER = 200f;    // Multiplier for spectrum values to radius
        public const float INNER_RADIUS_RATIO = 0.5f;    // Ratio between inner and outer path radius
        public const float MAX_SPECTRUM_VALUE = 1.0f;    // Maximum clamped spectrum value
        public const float SPECTRUM_SCALE = 2.0f;       // Scaling factor for raw spectrum data
        public const float CHANGE_THRESHOLD = 0.01f;    // Minimum change to trigger path updates
        public const float DEG_TO_RAD = (float)(PI / 180.0); // Degrees to radians conversion

        // Animation properties
        public const float ROTATION_SPEED = 0.3f;       // Speed of rotation animation
        public const float TIME_STEP = 0.016f;          // Time increment per frame (~60 FPS)
        public const float TIME_MODIFIER = 0.01f;       // Time scaling for rotation effect
        public const float MODULATION_FACTOR = 0.3f;    // Amplitude of radius modulation
        public const float MODULATION_FREQ = 5f;        // Frequency of radius modulation
        public const float PULSE_SPEED = 2.0f;          // Speed of pulsation effect
        public const float PULSE_AMPLITUDE = 0.2f;      // Amplitude of pulsation effect
        public const float DASH_PHASE_SPEED = 0.5f;      // Animation speed for dash pattern

        // Visual elements
        public const float DEFAULT_STROKE_WIDTH = 1.5f;  // Base width for stroke paths
        public const float CENTER_CIRCLE_SIZE = 6f;      // Size of center circle element
        public const byte FILL_ALPHA = 120;             // Alpha for gradient fill
        public const float DASH_LENGTH = 6.0f;          // Length of dash segments
        public const float HIGHLIGHT_FACTOR = 1.4f;     // Color multiplier for highlights
        public const float GLOW_RADIUS = 8.0f;          // Radius of glow blur effect
        public const float GLOW_SIGMA = 2.5f;           // Sigma parameter for glow blur
        public const byte GLOW_ALPHA = 80;              // Alpha for glow effect
        public const byte HIGHLIGHT_ALPHA = 160;        // Alpha for highlight elements

        // Point counts and sampling
        public const int MAX_POINT_COUNT = 120;          // Maximum number of points for rendering
        public const int POINT_COUNT_OVERLAY = 80;       // Number of points in overlay mode
        public const int MIN_POINT_COUNT = 24;           // Minimum points for smooth visualization
        public const float MIN_BAR_WIDTH = 0.5f;         // Minimum width for visible bars
        public const float MAX_BAR_WIDTH = 4.0f;         // Maximum width to limit GPU load

        // Quality-specific properties
        public static class Low
        {
            public const float SMOOTHING_FACTOR = 0.10f;  // Spectrum smoothing intensity
            public const int MAX_POINTS = 40;             // Maximum number of points
            public const bool USE_ANTI_ALIAS = false;      // Anti-aliasing setting
            public const bool USE_ADVANCED_EFFECTS = false; // Enable complex visual effects
            public const float STROKE_MULTIPLIER = 0.75f; // Stroke width scaling
            public const bool USE_GLOW = false;           // Enable glow effect
            public const bool USE_HIGHLIGHT = false;      // Enable highlight effect
            public const bool USE_PULSE_EFFECT = false;    // Enable pulsation animation
            public const bool USE_DASH_EFFECT = false;     // Enable dash pattern
            public const float PATH_SIMPLIFICATION = 0.5f; // Point reduction factor
        }

        public static class Medium
        {
            public const float SMOOTHING_FACTOR = 0.15f;  // Spectrum smoothing intensity
            public const int MAX_POINTS = 80;             // Maximum number of points
            public const bool USE_ANTI_ALIAS = true;       // Anti-aliasing setting
            public const bool USE_ADVANCED_EFFECTS = true; // Enable complex visual effects
            public const float STROKE_MULTIPLIER = 1.0f;  // Stroke width scaling
            public const bool USE_GLOW = false;           // Enable glow effect
            public const bool USE_HIGHLIGHT = true;       // Enable highlight effect
            public const bool USE_PULSE_EFFECT = true;     // Enable pulsation animation
            public const bool USE_DASH_EFFECT = true;      // Enable dash pattern
            public const float PATH_SIMPLIFICATION = 0.25f; // Point reduction factor
        }

        public static class High
        {
            public const float SMOOTHING_FACTOR = 0.20f;  // Spectrum smoothing intensity
            public const int MAX_POINTS = 120;            // Maximum number of points
            public const bool USE_ANTI_ALIAS = true;       // Anti-aliasing setting
            public const bool USE_ADVANCED_EFFECTS = true; // Enable complex visual effects
            public const float STROKE_MULTIPLIER = 1.25f; // Stroke width scaling
            public const bool USE_GLOW = true;            // Enable glow effect
            public const bool USE_HIGHLIGHT = true;       // Enable highlight effect
            public const bool USE_PULSE_EFFECT = true;     // Enable pulsation animation
            public const bool USE_DASH_EFFECT = true;      // Enable dash pattern
            public const float PATH_SIMPLIFICATION = 0.0f; // Point reduction factor
        }
    }
    #endregion

    #region Fields
    // Synchronization and state
    private readonly SemaphoreSlim _spectrumSemaphore = new(1, 1);
    private readonly SemaphoreSlim _pathUpdateSemaphore = new(1, 1);
    private bool _isOverlayActive;
    private bool _pathsNeedUpdate;
    private new bool _disposed;

    // Spectrum data
    private float[]? _processedSpectrum;
    private float[]? _previousSpectrum;
    private float[]? _tempSpectrum;

    // Rendering resources
    private SKPath? _outerPath;
    private SKPath? _innerPath;
    private readonly ObjectPool<SKPath> _pathPool = new(() => new SKPath(), path => path.Reset(), 4);
    private SKPaint? _fillPaint;
    private SKPaint? _strokePaint;
    private SKPaint? _centerPaint;
    private SKPaint? _glowPaint;
    private SKPaint? _highlightPaint;
    private SKShader? _gradientShader;
    private SKPathEffect? _dashEffect;
    private SKImageFilter? _glowFilter;
    private SKPicture? _cachedCenterCircle;

    // Animation state
    private float _rotation;
    private float _time;
    private float _pulseEffect;

    // Configuration
    private int _currentPointCount;
    private float _smoothingFactor = Constants.Medium.SMOOTHING_FACTOR;
    private new SKSamplingOptions _samplingOptions = new(SKFilterMode.Linear, SKMipmapMode.Linear);
    private new bool _useAntiAlias = Constants.Medium.USE_ANTI_ALIAS;
    private new bool _useAdvancedEffects = Constants.Medium.USE_ADVANCED_EFFECTS;
    private bool _useGlow = Constants.Medium.USE_GLOW;
    private bool _useHighlight = Constants.Medium.USE_HIGHLIGHT;
    private bool _usePulseEffect = Constants.Medium.USE_PULSE_EFFECT;
    private bool _useDashEffect = Constants.Medium.USE_DASH_EFFECT;
    private float _pathSimplification = Constants.Medium.PATH_SIMPLIFICATION;
    private float _strokeMultiplier = Constants.Medium.STROKE_MULTIPLIER;
    private int _maxPoints = Constants.Medium.MAX_POINTS;

    // SIMD vectors
    private Vector<float> _smoothingVec;
    private Vector<float> _oneMinusSmoothing;

    // Drawing state
    private SKPoint[]? _outerPoints;
    private SKPoint[]? _innerPoints;
    private SKColor _lastBaseColor;
    private SKRect _centerCircleBounds;
    private SKRect _clipBounds;

    // Performance tracking
    private int _frameCounter;
    private float _lastFrameTime;
    private float _avgFrameTime;
    private const int FRAME_AVERAGE_COUNT = 30;
    #endregion

    #region Initialization and Configuration
    /// <summary>
    /// Initializes the renderer and prepares resources for rendering.
    /// </summary>
    public override void Initialize()
    {
        Safe(() =>
        {
            base.Initialize();

            // Create paths with initial capacity
            _outerPath = _pathPool.Get();
            _innerPath = _pathPool.Get();

            InitializePoints();
            InitializePaints();
            InitializeSpectrum();

            _currentPointCount = Constants.MAX_POINT_COUNT;
            _pathsNeedUpdate = true;

            _centerCircleBounds = new SKRect(
                -Constants.CENTER_CIRCLE_SIZE * 1.5f,
                -Constants.CENTER_CIRCLE_SIZE * 1.5f,
                Constants.CENTER_CIRCLE_SIZE * 1.5f,
                Constants.CENTER_CIRCLE_SIZE * 1.5f
            );

            UpdateCenterCircle(SKColors.White);

            Log(LogLevel.Debug, Constants.LOG_PREFIX, "Initialized");
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.Initialize",
            ErrorMessage = "Failed to initialize renderer"
        });
    }

    /// <summary>
    /// Initializes points arrays for path generation.
    /// </summary>
    private void InitializePoints()
    {
        Safe(() =>
        {
            _outerPoints = new SKPoint[Constants.MAX_POINT_COUNT + 1];
            _innerPoints = new SKPoint[Constants.MAX_POINT_COUNT + 1];
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.InitializePoints",
            ErrorMessage = "Failed to initialize points"
        });
    }

    /// <summary>
    /// Initializes paint objects for rendering.
    /// </summary>
    private void InitializePaints()
    {
        Safe(() =>
        {
            _fillPaint = new SKPaint
            {
                IsAntialias = _useAntiAlias,
                Style = Fill
            };

            _strokePaint = new SKPaint
            {
                IsAntialias = _useAntiAlias,
                Style = Stroke,
                StrokeWidth = Constants.DEFAULT_STROKE_WIDTH * _strokeMultiplier,
                StrokeJoin = SKStrokeJoin.Round,
                StrokeCap = SKStrokeCap.Round
            };

            _centerPaint = new SKPaint
            {
                IsAntialias = _useAntiAlias,
                Style = Fill
            };

            _glowPaint = new SKPaint
            {
                IsAntialias = _useAntiAlias,
                Style = Stroke,
                StrokeWidth = Constants.DEFAULT_STROKE_WIDTH * 1.5f * _strokeMultiplier,
                BlendMode = SKBlendMode.SrcOver
            };

            _highlightPaint = new SKPaint
            {
                IsAntialias = _useAntiAlias,
                Style = Stroke,
                StrokeWidth = Constants.DEFAULT_STROKE_WIDTH * 0.5f * _strokeMultiplier,
                StrokeCap = SKStrokeCap.Round
            };
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.InitializePaints",
            ErrorMessage = "Failed to initialize paints"
        });
    }

    /// <summary>
    /// Initializes spectrum data arrays.
    /// </summary>
    private void InitializeSpectrum()
    {
        Safe(() =>
        {
            _processedSpectrum = new float[Constants.MAX_POINT_COUNT];
            _previousSpectrum = new float[Constants.MAX_POINT_COUNT];
            _tempSpectrum = new float[Constants.MAX_POINT_COUNT];

            _smoothingVec = new Vector<float>(_smoothingFactor);
            _oneMinusSmoothing = new Vector<float>(1 - _smoothingFactor);
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.InitializeSpectrum",
            ErrorMessage = "Failed to initialize spectrum data"
        });
    }

    /// <summary>
    /// Configures the renderer with overlay status and quality settings.
    /// </summary>
    /// <param name="isOverlayActive">Indicates if the renderer is used in overlay mode.</param>
    /// <param name="quality">The rendering quality level.</param>
    public override void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium)
    {
        Safe(() =>
        {
            base.Configure(isOverlayActive, quality);

            bool overlayChanged = _isOverlayActive != isOverlayActive;
            bool qualityChanged = _quality != quality;

            _isOverlayActive = isOverlayActive;

            if (overlayChanged)
            {
                _currentPointCount = isOverlayActive
                    ? Constants.POINT_COUNT_OVERLAY
                    : Min(_maxPoints, Constants.MAX_POINT_COUNT);
                _pathsNeedUpdate = true;
            }

            // Update quality if needed
            if (qualityChanged)
            {
                _quality = quality;
                ApplyQualitySettings();
            }

            // Reset caches if any parameter changed
            if (overlayChanged || qualityChanged)
            {
                InvalidateCachedResources();
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

        // Invalidate caches before changing settings
        InvalidateCachedResources();

        switch (_quality)
        {
            case RenderQuality.Low:
                _smoothingFactor = Constants.Low.SMOOTHING_FACTOR;
                _maxPoints = Constants.Low.MAX_POINTS;
                    _useAntiAlias = Constants.Low.USE_ANTI_ALIAS;
                    _useAdvancedEffects = Constants.Low.USE_ADVANCED_EFFECTS;
                    _samplingOptions = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);
                    _strokeMultiplier = Constants.Low.STROKE_MULTIPLIER;
                    _useGlow = Constants.Low.USE_GLOW;
                    _useHighlight = Constants.Low.USE_HIGHLIGHT;
                    _usePulseEffect = Constants.Low.USE_PULSE_EFFECT;
                    _useDashEffect = Constants.Low.USE_DASH_EFFECT;
                    _pathSimplification = Constants.Low.PATH_SIMPLIFICATION;
                    break;

                case RenderQuality.Medium:
                    _smoothingFactor = Constants.Medium.SMOOTHING_FACTOR;
                    _maxPoints = Constants.Medium.MAX_POINTS;
                    _useAntiAlias = Constants.Medium.USE_ANTI_ALIAS;
                    _useAdvancedEffects = Constants.Medium.USE_ADVANCED_EFFECTS;
                    _samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                    _strokeMultiplier = Constants.Medium.STROKE_MULTIPLIER;
                    _useGlow = Constants.Medium.USE_GLOW;
                    _useHighlight = Constants.Medium.USE_HIGHLIGHT;
                    _usePulseEffect = Constants.Medium.USE_PULSE_EFFECT;
                    _useDashEffect = Constants.Medium.USE_DASH_EFFECT;
                    _pathSimplification = Constants.Medium.PATH_SIMPLIFICATION;
                    break;

                case RenderQuality.High:
                    _smoothingFactor = Constants.High.SMOOTHING_FACTOR;
                    _maxPoints = Constants.High.MAX_POINTS;
                    _useAntiAlias = Constants.High.USE_ANTI_ALIAS;
                    _useAdvancedEffects = Constants.High.USE_ADVANCED_EFFECTS;
                    _samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                    _strokeMultiplier = Constants.High.STROKE_MULTIPLIER;
                    _useGlow = Constants.High.USE_GLOW;
                    _useHighlight = Constants.High.USE_HIGHLIGHT;
                    _usePulseEffect = Constants.High.USE_PULSE_EFFECT;
                    _useDashEffect = Constants.High.USE_DASH_EFFECT;
                    _pathSimplification = Constants.High.PATH_SIMPLIFICATION;
                    break;
            }

            _currentPointCount = _isOverlayActive
                ? Constants.POINT_COUNT_OVERLAY
                : Min(_maxPoints, Constants.MAX_POINT_COUNT);

            // Update smoothing vectors
            _smoothingVec = new Vector<float>(_smoothingFactor);
            _oneMinusSmoothing = new Vector<float>(1 - _smoothingFactor);

            // Update paint settings
            if (_fillPaint != null && _strokePaint != null &&
                _centerPaint != null && _glowPaint != null && _highlightPaint != null)
            {
                _fillPaint.IsAntialias = _useAntiAlias;
                _strokePaint.IsAntialias = _useAntiAlias;
                _strokePaint.StrokeWidth = Constants.DEFAULT_STROKE_WIDTH * _strokeMultiplier;
                _centerPaint.IsAntialias = _useAntiAlias;
                _glowPaint.IsAntialias = _useAntiAlias;
                _glowPaint.StrokeWidth = Constants.DEFAULT_STROKE_WIDTH * 1.5f * _strokeMultiplier;
                _highlightPaint.IsAntialias = _useAntiAlias;
                _highlightPaint.StrokeWidth = Constants.DEFAULT_STROKE_WIDTH * 0.5f * _strokeMultiplier;
            }

            _pathsNeedUpdate = true;

            Log(LogLevel.Debug, Constants.LOG_PREFIX, $"Quality changed to {_quality}");
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.ApplyQualitySettings",
            ErrorMessage = "Failed to apply quality settings"
        });
    }

    /// <summary>
    /// Invalidates cached resources to force regeneration.
    /// </summary>
    private void InvalidateCachedResources()
    {
        Safe(() =>
        {
            _cachedCenterCircle?.Dispose();
            _cachedCenterCircle = null;

            _dashEffect?.Dispose();
            _dashEffect = null;

            _gradientShader?.Dispose();
            _gradientShader = null;

            _glowFilter?.Dispose();
            _glowFilter = null;

            _pathsNeedUpdate = true;
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.InvalidateCachedResources",
            ErrorMessage = "Failed to invalidate cached resources"
        });
    }

    /// <summary>
    /// Updates the center circle visualization.
    /// </summary>
    private void UpdateCenterCircle(SKColor baseColor)
    {
        Safe(() =>
        {
            if (_centerPaint == null) return;

            _cachedCenterCircle?.Dispose();

            float effectiveGlowRadius = _useGlow ? Constants.GLOW_RADIUS : Constants.GLOW_RADIUS * 0.5f;
            float effectiveGlowSigma = _useGlow ? Constants.GLOW_SIGMA : Constants.GLOW_SIGMA * 0.5f;

            _glowFilter?.Dispose();
            _glowFilter = SKImageFilter.CreateBlur(effectiveGlowRadius, effectiveGlowSigma);

            using (SKPictureRecorder recorder = new SKPictureRecorder())
            {
                SKCanvas pictureCanvas = recorder.BeginRecording(_centerCircleBounds);

                if (_useGlow)
                {
                    using (SKPaint glowPaint = new SKPaint
                    {
                        IsAntialias = _useAntiAlias,
                        Style = Fill,
                        Color = baseColor.WithAlpha(Constants.GLOW_ALPHA),
                        ImageFilter = _glowFilter
                    })
                    {
                        pictureCanvas.DrawCircle(0, 0, Constants.CENTER_CIRCLE_SIZE * 0.8f, glowPaint);
                    }
                }

                pictureCanvas.DrawCircle(0, 0, Constants.CENTER_CIRCLE_SIZE * 0.7f, _centerPaint);

                if (_useHighlight)
                {
                    using (SKPaint highlightPaint = new SKPaint
                    {
                        IsAntialias = _useAntiAlias,
                        Style = Fill,
                        Color = SKColors.White.WithAlpha(Constants.HIGHLIGHT_ALPHA)
                    })
                    {
                        pictureCanvas.DrawCircle(
                            -Constants.CENTER_CIRCLE_SIZE * 0.25f,
                            -Constants.CENTER_CIRCLE_SIZE * 0.25f,
                            Constants.CENTER_CIRCLE_SIZE * 0.2f,
                            highlightPaint);
                    }
                }

                _cachedCenterCircle = recorder.EndRecording();
            }
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.UpdateCenterCircle",
            ErrorMessage = "Failed to update center circle"
        });
    }

    /// <summary>
    /// Updates visual effects based on the current color.
    /// </summary>
    private void UpdateVisualEffects(SKColor baseColor)
    {
        Safe(() =>
        {
            if (_fillPaint == null || _strokePaint == null ||
                _glowPaint == null || _highlightPaint == null)
                return;

            // Optimization: check if effects need to be updated
            bool colorChanged =
                baseColor.Red != _lastBaseColor.Red ||
                baseColor.Green != _lastBaseColor.Green ||
                baseColor.Blue != _lastBaseColor.Blue;

            if (!colorChanged && _gradientShader != null)
                return;

            _lastBaseColor = baseColor;

            // Gradient for fill
            SKColor gradientStart = baseColor.WithAlpha(Constants.FILL_ALPHA);
            SKColor gradientEnd = new SKColor(
                (byte)Min(255, baseColor.Red * 0.7),
                (byte)Min(255, baseColor.Green * 0.7),
                (byte)Min(255, baseColor.Blue * 0.7),
                20);

            _gradientShader?.Dispose();
            _gradientShader = SKShader.CreateRadialGradient(
                new SKPoint(0, 0),
                Constants.MIN_RADIUS + Constants.MAX_SPECTRUM_VALUE * Constants.RADIUS_MULTIPLIER,
                new[] { gradientStart, gradientEnd },
                SKShaderTileMode.Clamp);

            _fillPaint.Shader = _gradientShader;
            _strokePaint.Color = baseColor;

            // Glow effect for high quality or when explicitly enabled
            if (_useGlow)
            {
                _glowFilter?.Dispose();
                _glowFilter = SKImageFilter.CreateBlur(Constants.GLOW_RADIUS, Constants.GLOW_SIGMA);
                _glowPaint.Color = baseColor.WithAlpha(Constants.GLOW_ALPHA);
                _glowPaint.ImageFilter = _glowFilter;
            }

            // Highlight effect
            if (_useHighlight)
            {
                _highlightPaint.Color = new SKColor(
                    (byte)Min(255, baseColor.Red * Constants.HIGHLIGHT_FACTOR),
                    (byte)Min(255, baseColor.Green * Constants.HIGHLIGHT_FACTOR),
                    (byte)Min(255, baseColor.Blue * Constants.HIGHLIGHT_FACTOR),
                    Constants.HIGHLIGHT_ALPHA);
            }

            // Dash effect only for certain quality settings
            if (_useDashEffect)
            {
                float[] intervals = { Constants.DASH_LENGTH, Constants.DASH_LENGTH * 2 };
                _dashEffect?.Dispose();
                _dashEffect = SKPathEffect.CreateDash(
                    intervals,
                    _time * Constants.DASH_PHASE_SPEED % (Constants.DASH_LENGTH * 3));
            }

            _centerPaint!.Color = baseColor;
            UpdateCenterCircle(baseColor);
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.UpdateVisualEffects",
            ErrorMessage = "Failed to update visual effects"
        });
    }
    #endregion

    #region Rendering
    /// <summary>
    /// Renders the polar graph visualization on the canvas using spectrum data.
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
        // Track frame time for performance monitoring
        float frameStartTime = (float)Now.Ticks / TimeSpan.TicksPerSecond;

        // Validate rendering parameters
        if (!ValidateRenderParameters(canvas, spectrum, info, paint))
        {
            drawPerformanceInfo?.Invoke(canvas!, info);
            return;
        }

        Safe(() =>
        {
            int safeBarCount = Min(Max(barCount, Constants.MIN_POINT_COUNT), _maxPoints);
            float safeBarWidth = Clamp(barWidth, Constants.MIN_BAR_WIDTH, Constants.MAX_BAR_WIDTH);

            // Quick check for rendering area visibility
            float maxRadius = Constants.MIN_RADIUS + Constants.MAX_SPECTRUM_VALUE *
                Constants.RADIUS_MULTIPLIER * (1 + Constants.MODULATION_FACTOR);
            _clipBounds = new SKRect(
                -maxRadius, -maxRadius,
                maxRadius, maxRadius
            );

            // Check if rendering area is in the visible part of canvas
            SKRect canvasBounds = new SKRect(0, 0, info.Width, info.Height);
            if (canvas!.QuickReject(canvasBounds))
            {
                drawPerformanceInfo?.Invoke(canvas, info);
                return;
            }

            // Process spectrum data in a separate thread
            Task processTask = Task.Run(() => ProcessSpectrum(spectrum!, safeBarCount));

            // Update paths if needed (can be done in a separate thread)
            if (_pathsNeedUpdate)
            {
                bool lockAcquired = _pathUpdateSemaphore.Wait(0);
                if (lockAcquired)
                {
                    try
                    {
                        UpdatePolarPaths(info, safeBarCount);
                        _pathsNeedUpdate = false;
                    }
                    finally
                    {
                        _pathUpdateSemaphore.Release();
                    }
                }
            }

            // Wait for spectrum processing to complete
            processTask.Wait();

            // Update visual effects based on base paint color
            UpdateVisualEffects(paint!.Color);

            // Render with quality considerations
            RenderPolarGraph(canvas!, info, paint!, safeBarWidth);
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.Render",
            ErrorMessage = "Error during rendering"
        });

        // Display performance information
        drawPerformanceInfo?.Invoke(canvas!, info);

        // Track frame time for performance monitoring
        TrackFrameTime(frameStartTime);
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

        if (spectrum.Length < 2)
        {
            Log(LogLevel.Warning, Constants.LOG_PREFIX, "Spectrum must have at least 2 elements");
            return false;
        }

        return true;
    }
    #endregion

    #region Rendering Implementation
    /// <summary>
    /// Renders the polar graph with all visual effects.
    /// </summary>
    private void RenderPolarGraph(SKCanvas canvas, SKImageInfo info, SKPaint basePaint, float barWidth)
    {
        Safe(() =>
        {
        if (_outerPath == null || _innerPath == null ||
            _fillPaint == null || _strokePaint == null ||
            _centerPaint == null || _cachedCenterCircle == null ||
            _glowPaint == null || _highlightPaint == null)
            return;

            // Update pulse effect animation
            if (_usePulseEffect)
            {
                _pulseEffect = (float)Sin(_time * Constants.PULSE_SPEED) *
                            Constants.PULSE_AMPLITUDE + 1.0f;
            }
            else
            {
                _pulseEffect = 1.0f;
            }

            // Adjust stroke widths based on pulse and bar width
            _strokePaint.StrokeWidth = barWidth * _pulseEffect * _strokeMultiplier;

            if (_useGlow)
            {
                _glowPaint.StrokeWidth = barWidth * 1.5f * _pulseEffect * _strokeMultiplier;
            }

            if (_useHighlight)
            {
                _highlightPaint.StrokeWidth = barWidth * 0.5f * _pulseEffect * _strokeMultiplier;
            }

            // Use animated dash effect for inner path when enabled
            if (_useDashEffect && _dashEffect != null)
            {
                _strokePaint.PathEffect = _dashEffect;
            }
            else
            {
                _strokePaint.PathEffect = null;
            }

            canvas.Save();
            canvas.Translate(info.Width / 2f, info.Height / 2f);
            canvas.RotateDegrees(_rotation);

            // Check visibility of rendering area
            if (!canvas.QuickReject(_clipBounds))
            {
                // Optimization: use a single DrawPicture for complex effects at high quality
                if (_useAdvancedEffects && _quality == RenderQuality.High)
                {
                    using (SKPictureRecorder recorder = new SKPictureRecorder())
                    {
                        SKCanvas pictureCanvas = recorder.BeginRecording(_clipBounds);

                        // Draw all effects on pictureCanvas
                        if (_useGlow)
                        {
                            pictureCanvas.DrawPath(_outerPath, _glowPaint);
                        }

                        pictureCanvas.DrawPath(_outerPath, _fillPaint);
                        pictureCanvas.DrawPath(_outerPath, _strokePaint);

                        SKPathEffect? originalEffect = _strokePaint.PathEffect;
                        _strokePaint.PathEffect = _dashEffect;
                        pictureCanvas.DrawPath(_innerPath, _strokePaint);
                        _strokePaint.PathEffect = originalEffect;

                        if (_useHighlight)
                        {
                            pictureCanvas.DrawPath(_innerPath, _highlightPaint);
                        }

                        using (SKPicture combinedPicture = recorder.EndRecording())
                        {
                            canvas.DrawPicture(combinedPicture);
                        }
                    }
                }
                else
                {
                    // Standard rendering with call group optimization
                    if (_useGlow)
                    {
                        canvas.DrawPath(_outerPath, _glowPaint);
                    }

                    canvas.DrawPath(_outerPath, _fillPaint);
                    canvas.DrawPath(_outerPath, _strokePaint);

                    if (_useDashEffect && _dashEffect != null)
                    {
                        _strokePaint.PathEffect = _dashEffect;
                    }
                    canvas.DrawPath(_innerPath, _strokePaint);
                    _strokePaint.PathEffect = null;

                    if (_useHighlight)
                    {
                        canvas.DrawPath(_innerPath, _highlightPaint);
                    }
                }

                // Draw center circle with pulsation
                float pulseScale = _usePulseEffect
                    ? 1.0f + (float)Sin(_time * Constants.PULSE_SPEED * 0.5f) * 0.1f
                    : 1.0f;

                canvas.Save();
                canvas.Scale(pulseScale, pulseScale);
                canvas.DrawPicture(_cachedCenterCircle);
                canvas.Restore();
            }

            canvas.Restore();
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.RenderPolarGraph",
            ErrorMessage = "Failed to render polar graph"
        });
    }

    /// <summary>
    /// Tracks frame rendering time for performance monitoring.
    /// </summary>
    private void TrackFrameTime(float frameStartTime)
    {
        Safe(() =>
        {
            float frameEndTime = (float)Now.Ticks / TimeSpan.TicksPerSecond;
            float frameTime = frameEndTime - frameStartTime;

            _lastFrameTime = frameTime;
            _avgFrameTime = (_avgFrameTime * _frameCounter + frameTime) / (_frameCounter + 1);

            _frameCounter = (_frameCounter + 1) % FRAME_AVERAGE_COUNT;
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.TrackFrameTime",
            ErrorMessage = "Failed to track frame time"
        });
    }
    #endregion

    #region Path Generation
    /// <summary>
    /// Updates polar paths based on current spectrum data.
    /// </summary>
    [MethodImpl(AggressiveOptimization)]
    private void UpdatePolarPaths(SKImageInfo info, int barCount)
    {
        Safe(() =>
        {
            if (_processedSpectrum == null || _outerPath == null || _innerPath == null ||
                _outerPoints == null || _innerPoints == null)
                return;

            _time += Constants.TIME_STEP;
            _rotation += Constants.ROTATION_SPEED * _time * Constants.TIME_MODIFIER;

            int effectivePointCount = Min(barCount, _currentPointCount);

            // Apply path simplification based on quality level
            int skipFactor = _pathSimplification > 0
                ? Max(1, (int)(1.0f / (1.0f - _pathSimplification)))
                : 1;

            int actualPoints = effectivePointCount / skipFactor;
            float angleStep = 360f / actualPoints;

            for (int i = 0, pointIndex = 0; i <= effectivePointCount; i += skipFactor, pointIndex++)
            {
                float angle = pointIndex * angleStep * Constants.DEG_TO_RAD;
                float cosAngle = (float)Cos(angle);
                float sinAngle = (float)Sin(angle);

                int spectrumIndex = i % effectivePointCount;
                float spectrumValue = spectrumIndex < _processedSpectrum.Length
                    ? _processedSpectrum[spectrumIndex]
                    : 0f;

                // Simpler modulation at low quality
                float timeOffset = _time * 0.5f + pointIndex * 0.1f;
                float modulation;

                if (_quality == RenderQuality.Low)
                {
                    modulation = 1.0f;
                }
                else
                {
                    modulation = 1 + Constants.MODULATION_FACTOR *
                        (float)Sin(pointIndex * angleStep * Constants.MODULATION_FREQ * Constants.DEG_TO_RAD + _time * 2);

                    if (_usePulseEffect)
                    {
                        modulation += Constants.PULSE_AMPLITUDE * 0.5f * (float)Sin(timeOffset);
                    }
                }

                float outerRadius = Constants.MIN_RADIUS + spectrumValue * modulation * Constants.RADIUS_MULTIPLIER;
                if (pointIndex < _outerPoints.Length)
                {
                    _outerPoints[pointIndex] = new SKPoint(
                        outerRadius * cosAngle,
                        outerRadius * sinAngle
                    );
                }

                float innerSpectrumValue = spectrumValue * Constants.INNER_RADIUS_RATIO;
                float innerModulation;

                if (_quality == RenderQuality.Low)
                {
                    innerModulation = 1.0f;
                }
                else
                {
                    innerModulation = 1 + Constants.MODULATION_FACTOR *
                        (float)Sin(pointIndex * angleStep * Constants.MODULATION_FREQ * Constants.DEG_TO_RAD + _time * 2 + PI);

                    if (_usePulseEffect)
                    {
                        innerModulation += Constants.PULSE_AMPLITUDE * 0.5f * (float)Sin(timeOffset + PI);
                    }
                }

                float innerRadius = Constants.MIN_RADIUS + innerSpectrumValue * innerModulation * Constants.RADIUS_MULTIPLIER;
                if (pointIndex < _innerPoints.Length)
                {
                    _innerPoints[pointIndex] = new SKPoint(
                        innerRadius * cosAngle,
                        innerRadius * sinAngle
                    );
                }
            }

            _outerPath.Reset();
            _innerPath.Reset();

            try
            {
                int pointsToUse = Min(actualPoints + 1, _outerPoints.Length);

                // Optimization: use native polygon addition instead of LINQ
                SKPoint[] outerPointsSlice = new SKPoint[pointsToUse];
                SKPoint[] innerPointsSlice = new SKPoint[pointsToUse];

                Array.Copy(_outerPoints, outerPointsSlice, pointsToUse);
                Array.Copy(_innerPoints, innerPointsSlice, pointsToUse);

                _outerPath.AddPoly(outerPointsSlice, true);
                _innerPath.AddPoly(innerPointsSlice, true);
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, Constants.LOG_PREFIX, $"Failed to create path: {ex.Message}");

                // Fallback method in case of error
                _outerPath.Reset();
                _innerPath.Reset();

                for (int i = 0, pointIndex = 0; i <= effectivePointCount; i += skipFactor, pointIndex++)
                {
                    int safeIndex = Min(pointIndex, _outerPoints.Length - 1);

                    if (pointIndex == 0)
                    {
                        _outerPath.MoveTo(_outerPoints[safeIndex]);
                        _innerPath.MoveTo(_innerPoints[safeIndex]);
                    }
                    else
                    {
                        _outerPath.LineTo(_outerPoints[safeIndex]);
                        _innerPath.LineTo(_innerPoints[safeIndex]);
                    }
                }

                _outerPath.Close();
                _innerPath.Close();
            }
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.UpdatePolarPaths",
            ErrorMessage = "Failed to update polar paths"
        });
    }
    #endregion

    #region Spectrum Processing
    /// <summary>
    /// Processes spectrum data for visualization.
    /// </summary>
    [MethodImpl(AggressiveOptimization)]
    private void ProcessSpectrum(float[] spectrum, int barCount)
    {
        Safe(() =>
        {
            if (_disposed || _tempSpectrum == null || _previousSpectrum == null ||
                _processedSpectrum == null)
                return;

            try
            {
                _spectrumSemaphore.Wait();

                int pointCount = Min(barCount, _currentPointCount);

                // Fast spectrum reading with quality-based sampling and interpolation
                ExtractSpectrumPoints(spectrum, pointCount);

                // Smooth spectrum using SIMD
                float maxChange = SmoothSpectrumSIMD(pointCount);

                if (maxChange > Constants.CHANGE_THRESHOLD)
                    _pathsNeedUpdate = true;
            }
            finally
            {
                _spectrumSemaphore.Release();
            }
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.ProcessSpectrum",
            ErrorMessage = "Error processing spectrum data"
        });
    }

    /// <summary>
    /// Extracts and interpolates points from the spectrum data.
    /// </summary>
    private void ExtractSpectrumPoints(float[] spectrum, int pointCount)
    {
        Safe(() =>
        {
            if (_tempSpectrum == null)
            {
                Log(LogLevel.Error, Constants.LOG_PREFIX, "Temporary spectrum array is null");
                return;
            }

            for (int i = 0; i < pointCount && i < _tempSpectrum.Length; i++)
            {
                float spectrumIndex = i * spectrum.Length / (2f * pointCount);
                int baseIndex = (int)spectrumIndex;
                float fraction = spectrumIndex - baseIndex;

                if (baseIndex >= spectrum.Length / 2 - 1)
                {
                    _tempSpectrum[i] = spectrum[Min(spectrum.Length / 2 - 1, spectrum.Length - 1)];
                }
                else if (baseIndex + 1 < spectrum.Length)
                {
                    _tempSpectrum[i] = spectrum[baseIndex] * (1 - fraction) + spectrum[baseIndex + 1] * fraction;
                }
                else
                {
                    _tempSpectrum[i] = spectrum[baseIndex];
                }

                _tempSpectrum[i] = Min(_tempSpectrum[i] * Constants.SPECTRUM_SCALE, Constants.MAX_SPECTRUM_VALUE);
            }
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.ExtractSpectrumPoints",
            ErrorMessage = "Failed to extract spectrum points"
        });
    }

    /// <summary>
    /// Smooths spectrum data using SIMD acceleration when available.
    /// </summary>
    [MethodImpl(AggressiveOptimization)]
    private float SmoothSpectrumSIMD(int pointCount)
    {
        float maxChange = 0f;

        Safe(() =>
        {
        // Check all arrays for null
        if (_tempSpectrum == null || _previousSpectrum == null || _processedSpectrum == null)
        {
                Log(LogLevel.Error, Constants.LOG_PREFIX, "Spectrum data arrays are null");
            return;
        }

        // Check array bounds
        int safePointCount = Min(pointCount,
            Min(_tempSpectrum.Length,
                Min(_previousSpectrum.Length, _processedSpectrum.Length)));

        // Use SIMD for faster processing
        if (IsHardwareAccelerated && safePointCount >= Vector<float>.Count)
        {
            for (int i = 0; i < safePointCount; i += Vector<float>.Count)
            {
                int remaining = Min(Vector<float>.Count, safePointCount - i);

                if (remaining < Vector<float>.Count)
                {
                    // Process remainder using standard approach
                    for (int j = 0; j < remaining; j++)
                    {
                        float newValue = _previousSpectrum[i + j] * (1 - _smoothingFactor) +
                                       _tempSpectrum[i + j] * _smoothingFactor;
                        float change = Abs(newValue - _previousSpectrum[i + j]);
                        maxChange = Max(maxChange, change);
                        _processedSpectrum[i + j] = newValue;
                        _previousSpectrum[i + j] = newValue;
                    }
                }
                    else
                    {
                        try
                        {
                            // SIMD-accelerated batch processing
                            Vector<float> current = new Vector<float>(_tempSpectrum, i);
                            Vector<float> previous = new Vector<float>(_previousSpectrum, i);
                            Vector<float> smoothed = previous * _oneMinusSmoothing + current * _smoothingVec;

                            // Calculate maximum change to determine if path update is needed
                            Vector<float> change = Abs(smoothed - previous);
                            float batchMaxChange = 0f;
                            for (int j = 0; j < Vector<float>.Count; j++)
                            {
                                if (change[j] > batchMaxChange)
                                    batchMaxChange = change[j];
                            }
                            maxChange = Max(maxChange, batchMaxChange);

                            // Safely copy results
                            smoothed.CopyTo(_processedSpectrum, i);
                            smoothed.CopyTo(_previousSpectrum, i);
                        }
                        catch (NullReferenceException)
                        {
                            Log(LogLevel.Error, Constants.LOG_PREFIX, "Null reference in SIMD processing");

                            // Fallback to standard approach in case of error
                            for (int j = 0; j < Vector<float>.Count && i + j < safePointCount; j++)
                            {
                                float newValue = _previousSpectrum[i + j] * (1 - _smoothingFactor) +
                                               _tempSpectrum[i + j] * _smoothingFactor;
                                float change = Abs(newValue - _previousSpectrum[i + j]);
                                maxChange = Max(maxChange, change);
                                _processedSpectrum[i + j] = newValue;
                                _previousSpectrum[i + j] = newValue;
                            }
                        }
                    }
                }
            }
            else
            {
                // Fallback for systems without SIMD
                for (int i = 0; i < safePointCount; i++)
                {
                    float newValue = _previousSpectrum[i] * (1 - _smoothingFactor) +
                                   _tempSpectrum[i] * _smoothingFactor;
                    float change = Abs(newValue - _previousSpectrum[i]);
                    maxChange = Max(maxChange, change);
                    _processedSpectrum[i] = newValue;
                    _previousSpectrum[i] = newValue;
                }
            }
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.SmoothSpectrumSIMD",
            ErrorMessage = "Failed to smooth spectrum data"
        });

        return maxChange;
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
                _spectrumSemaphore?.Dispose();
                _pathUpdateSemaphore?.Dispose();

                // Dispose graphic resources
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

                // Dispose path pool
                _pathPool.Dispose();

                // Clear references
                _outerPath = null;
                _innerPath = null;
                _fillPaint = null;
                _strokePaint = null;
                _centerPaint = null;
                _cachedCenterCircle = null;
                _processedSpectrum = null;
                _previousSpectrum = null;
                _tempSpectrum = null;
                _outerPoints = null;
                _innerPoints = null;
                _glowPaint = null;
                _highlightPaint = null;
                _gradientShader = null;
                _dashEffect = null;
                _glowFilter = null;

                // Call base disposal
                base.Dispose();

                Log(LogLevel.Debug, Constants.LOG_PREFIX, "Disposed");
            }, new ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.Dispose",
                ErrorMessage = "Error during disposal"
            });

            _disposed = true;
        }
    }
    #endregion
}