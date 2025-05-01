#nullable enable

using SpectrumNet.Service.Enums;

namespace SpectrumNet.Views.Renderers;

/// <summary>
/// Renderer that visualizes spectrum data as dots with various optimizations.
/// </summary>
public sealed class DotsRenderer : BaseSpectrumRenderer
{
    #region Constants
    private static class Constants
    {
        // Logging
        public const string LOG_PREFIX = "DotsRenderer";

        // Rendering thresholds and limits
        public const float MIN_INTENSITY_THRESHOLD = 0.01f;  // Minimum intensity to render a dot
        public const float MIN_DOT_RADIUS = 2.0f;           // Minimum dot radius in pixels
        public const float MAX_DOT_MULTIPLIER = 0.5f;       // Maximum multiplier for dot size

        // Alpha and binning parameters
        public const float ALPHA_MULTIPLIER = 255.0f;       // Multiplier for alpha calculation
        public const int ALPHA_BINS = 16;                   // Number of alpha bins for batching

        // Dot size parameters
        public const float NORMAL_DOT_MULTIPLIER = 1.0f;    // Normal dot size multiplier
        public const float OVERLAY_DOT_MULTIPLIER = 1.5f;   // Overlay dot size multiplier

        // Performance settings
        public const int VECTOR_SIZE = 4;                   // Size for SIMD vectorization
        public const int MIN_BATCH_SIZE = 32;               // Minimum batch size for parallel processing
    }
    #endregion

    #region Fields
    private static readonly Lazy<DotsRenderer> _instance = new(() => new DotsRenderer());

    // Object pools for efficient resource management
    private readonly ObjectPool<SKPath> _pathPool = new(() => new SKPath(), path => path.Reset(), 5);
    private readonly ObjectPool<SKPaint> _paintPool = new(() => new SKPaint(), paint => paint.Reset(), 3);

    // Rendering state
    private float _dotRadiusMultiplier = Constants.NORMAL_DOT_MULTIPLIER;

    // Quality-dependent settings
    private new bool _useAntiAlias = true;
    private SKSamplingOptions _samplingOptions = new(SKFilterMode.Linear, SKMipmapMode.Linear);
    private bool _useHardwareAcceleration = true;
    private bool _useVectorization = true;
    private bool _batchProcessing = true;

    // Cached resources
    private SKPaint? _dotPaint;
    private SKPicture? _cachedBackground;
    private readonly object _lockObject = new();
    private Task? _backgroundCalculationTask;
    private new bool _disposed;
    #endregion

    #region Structures
    /// <summary>
    /// Represents data for a single circle in the visualization.
    /// </summary>
    private struct CircleData
    {
        public float X;
        public float Y;
        public float Radius;
        public float Intensity;
    }
    #endregion

    #region Singleton Pattern
    private DotsRenderer() { }

    /// <summary>
    /// Gets the singleton instance of the dots renderer.
    /// </summary>
    public static DotsRenderer GetInstance() => _instance.Value;
    #endregion

    #region Initialization and Configuration
    /// <summary>
    /// Initializes the dots renderer and prepares rendering resources.
    /// </summary>
    public override void Initialize()
    {
        Safe(() =>
        {
            base.Initialize();
            InitializePaints();
            Log(LogLevel.Debug, Constants.LOG_PREFIX, "Initialized");
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.Initialize",
            ErrorMessage = "Failed to initialize renderer"
        });
    }

    /// <summary>
    /// Initializes paint objects used for rendering.
    /// </summary>
    private void InitializePaints()
    {
        Safe(() =>
        {
            _dotPaint?.Dispose();
            _dotPaint = new SKPaint
            {
                IsAntialias = _useAntiAlias,
                Style = Fill
            };
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.InitializePaints",
            ErrorMessage = "Failed to initialize paints"
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

            _dotRadiusMultiplier = isOverlayActive ?
                Constants.OVERLAY_DOT_MULTIPLIER :
                Constants.NORMAL_DOT_MULTIPLIER;

            // Invalidate cached resources
            _cachedBackground?.Dispose();
            _cachedBackground = null;
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
                    _samplingOptions = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);
                    _useHardwareAcceleration = true;
                    _useVectorization = false;
                    _batchProcessing = false;
                    break;

                case RenderQuality.Medium:
                    _useAntiAlias = true;
                    _samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                    _useHardwareAcceleration = true;
                    _useVectorization = true;
                    _batchProcessing = true;
                    break;

                case RenderQuality.High:
                    _useAntiAlias = true;
                    _samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                    _useHardwareAcceleration = true;
                    _useVectorization = true;
                    _batchProcessing = true;
                    break;
            }

            if (_dotPaint != null)
            {
                _dotPaint.IsAntialias = _useAntiAlias;
            }

            _cachedBackground?.Dispose();
            _cachedBackground = null;
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.ApplyQualitySettings",
            ErrorMessage = "Failed to apply quality settings"
        });
    }
    #endregion

    #region Rendering
    /// <summary>
    /// Renders the dots visualization on the canvas using spectrum data.
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
        if (!QuickValidate(canvas, spectrum, info, paint))
        {
            drawPerformanceInfo?.Invoke(canvas!, info);
            return;
        }

        if (canvas!.QuickReject(new SKRect(0, 0, info.Width, info.Height)))
        {
            drawPerformanceInfo?.Invoke(canvas, info);
            return;
        }

        Safe(() =>
        {
            UpdatePaint(paint!);

            int spectrumLength = spectrum!.Length;
            int actualBarCount = Min(spectrumLength, barCount);
            float canvasHeight = info.Height;
            float calculatedBarWidth = barWidth * Constants.MAX_DOT_MULTIPLIER * _dotRadiusMultiplier;
            float totalWidth = barWidth + barSpacing;

            float[] processedSpectrum = PrepareSpectrum(spectrum, actualBarCount, spectrumLength);

            List<CircleData> circles = CalculateCircleData(
                processedSpectrum,
                calculatedBarWidth,
                totalWidth,
                canvasHeight);

            List<List<CircleData>> circleBins = GroupCirclesByAlphaBin(circles);

            DrawCircles(canvas!, circleBins);
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.Render",
            ErrorMessage = "Error during rendering"
        });

        drawPerformanceInfo?.Invoke(canvas!, info);
    }

    /// <summary>
    /// Updates the paint object with current settings.
    /// </summary>
    private void UpdatePaint(SKPaint basePaint)
    {
        Safe(() =>
        {
            if (_dotPaint == null)
            {
                InitializePaints();
            }

            if (_dotPaint != null)
            {
                _dotPaint.Color = basePaint.Color;
                _dotPaint.IsAntialias = _useAntiAlias;
            }
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.UpdatePaint",
            ErrorMessage = "Failed to update paint"
        });
    }
    #endregion

    #region Circle Calculation and Rendering
    /// <summary>
    /// Calculates circle data based on spectrum values.
    /// </summary>
    [MethodImpl(AggressiveOptimization)]
    private List<CircleData> CalculateCircleData(
        float[] smoothedSpectrum,
        float multiplier,
        float totalWidth,
        float canvasHeight)
    {
        var circles = new List<CircleData>(smoothedSpectrum.Length);

        if (_useVectorization && IsHardwareAccelerated && smoothedSpectrum.Length >= 4)
        {
            return CalculateCircleDataOptimized(smoothedSpectrum, multiplier, totalWidth, canvasHeight);
        }

        for (int i = 0; i < smoothedSpectrum.Length; i++)
        {
            float intensity = smoothedSpectrum[i];
            if (intensity < Constants.MIN_INTENSITY_THRESHOLD) continue;

            float dotRadius = Max(multiplier * intensity, Constants.MIN_DOT_RADIUS);
            float x = i * totalWidth + dotRadius;
            float y = canvasHeight - intensity * canvasHeight;

            circles.Add(new CircleData
            {
                X = x,
                Y = y,
                Radius = dotRadius,
                Intensity = intensity
            });
        }

        return circles;
    }

    /// <summary>
    /// Calculates circle data with optimizations for large datasets.
    /// </summary>
    [MethodImpl(AggressiveOptimization)]
    private List<CircleData> CalculateCircleDataOptimized(
       float[] smoothedSpectrum,
       float multiplier,
       float totalWidth,
       float canvasHeight)
    {
        var circles = new List<CircleData>(smoothedSpectrum.Length);
        const int chunkSize = 16;
        int chunks = smoothedSpectrum.Length / chunkSize;

        for (int chunk = 0; chunk < chunks; chunk++)
        {
            int startIdx = chunk * chunkSize;
            int endIdx = startIdx + chunkSize;

            for (int i = startIdx; i < endIdx; i++)
            {
                float intensity = smoothedSpectrum[i];
                if (intensity < Constants.MIN_INTENSITY_THRESHOLD) continue;

                float dotRadius = Max(multiplier * intensity, Constants.MIN_DOT_RADIUS);
                float x = i * totalWidth + dotRadius;
                float y = canvasHeight - intensity * canvasHeight;

                circles.Add(new CircleData
                {
                    X = x,
                    Y = y,
                    Radius = dotRadius,
                    Intensity = intensity
                });
            }
        }

        // Process remaining elements
        for (int i = chunks * chunkSize; i < smoothedSpectrum.Length; i++)
        {
            float intensity = smoothedSpectrum[i];
            if (intensity < Constants.MIN_INTENSITY_THRESHOLD) continue;

            float dotRadius = Max(multiplier * intensity, Constants.MIN_DOT_RADIUS);
            float x = i * totalWidth + dotRadius;
            float y = canvasHeight - intensity * canvasHeight;

            circles.Add(new CircleData
            {
                X = x,
                Y = y,
                Radius = dotRadius,
                Intensity = intensity
            });
        }

        return circles;
    }

    /// <summary>
    /// Groups circles into bins by alpha value for efficient rendering.
    /// </summary>
    private List<List<CircleData>> GroupCirclesByAlphaBin(List<CircleData> circles)
    {
        List<List<CircleData>> circleBins = new List<List<CircleData>>(Constants.ALPHA_BINS);

        for (int i = 0; i < Constants.ALPHA_BINS; i++)
        {
            circleBins.Add(new List<CircleData>(circles.Count / Constants.ALPHA_BINS + 1));
        }

        float binStep = 255f / (Constants.ALPHA_BINS - 1);

        foreach (var circle in circles)
        {
            byte alpha = (byte)Min(circle.Intensity * Constants.ALPHA_MULTIPLIER, 255);
            int binIndex = Min((int)(alpha / binStep), Constants.ALPHA_BINS - 1);
            circleBins[binIndex].Add(circle);
        }

        return circleBins;
    }

    /// <summary>
    /// Draws circles efficiently by grouping them by alpha value.
    /// </summary>
    [MethodImpl(AggressiveOptimization)]
    private void DrawCircles(SKCanvas canvas, List<List<CircleData>> circleBins)
    {
        Safe(() =>
        {
            if (_dotPaint == null) return;

            SKRect canvasBounds = new SKRect(0, 0, canvas.DeviceClipBounds.Width, canvas.DeviceClipBounds.Height);
            float binStep = 255f / (Constants.ALPHA_BINS - 1);

            for (int binIndex = 0; binIndex < Constants.ALPHA_BINS; binIndex++)
            {
                var bin = circleBins[binIndex];
                if (bin.Count == 0)
                    continue;

                byte binAlpha = (byte)(binIndex * binStep);
                _dotPaint.Color = _dotPaint.Color.WithAlpha(binAlpha);

                if (bin.Count <= 5 || !_batchProcessing)
                {
                    // Draw individual circles for small batches
                    foreach (var circle in bin)
                    {
                        // Skip circles outside the canvas
                        SKRect circleBounds = new SKRect(
                            circle.X - circle.Radius,
                            circle.Y - circle.Radius,
                            circle.X + circle.Radius,
                            circle.Y + circle.Radius);

                        if (!canvas.QuickReject(circleBounds))
                        {
                            canvas.DrawCircle(circle.X, circle.Y, circle.Radius, _dotPaint);
                        }
                    }
                }
                else
                {
                    // Batch drawing for better performance
                    using var path = _pathPool.Get();
                    path.Reset();

                    foreach (var circle in bin)
                    {
                        SKRect circleBounds = new SKRect(
                            circle.X - circle.Radius,
                            circle.Y - circle.Radius,
                            circle.X + circle.Radius,
                            circle.Y + circle.Radius);

                        if (!canvas.QuickReject(circleBounds))
                        {
                            path.AddCircle(circle.X, circle.Y, circle.Radius);
                        }
                    }

                    canvas.DrawPath(path, _dotPaint);
                }
            }
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.DrawCircles",
            ErrorMessage = "Error drawing circles"
        });
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
                _dotPaint?.Dispose();
                _dotPaint = null;

                _cachedBackground?.Dispose();
                _cachedBackground = null;

                _backgroundCalculationTask?.Wait(100);

                _pathPool?.Dispose();
                _paintPool?.Dispose();

                base.Dispose();

                _disposed = true;
                Log(LogLevel.Debug, Constants.LOG_PREFIX, "Disposed");
            }, new ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.Dispose",
                ErrorMessage = "Error during disposal"
            });
        }
    }
    #endregion
}