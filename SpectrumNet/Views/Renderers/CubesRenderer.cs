#nullable enable

using SpectrumNet.Service.Enums;

namespace SpectrumNet.Views.Renderers;

/// <summary>
/// Renderer that creates 3D cube visualizations for spectrum data.
/// </summary>
public sealed class CubesRenderer : BaseSpectrumRenderer
{
    #region Constants
    private static class Constants
    {
        // Logging
        public const string LOG_PREFIX = "CubesRenderer";

        // Rendering constants
        public const float CUBE_TOP_WIDTH_PROPORTION = 0.75f;   // Proportion of bar width for cube top
        public const float CUBE_TOP_HEIGHT_PROPORTION = 0.25f;  // Proportion of bar width for cube top height
        public const float ALPHA_MULTIPLIER = 255f;             // Multiplier for alpha calculation
        public const float TOP_ALPHA_FACTOR = 0.8f;             // Alpha factor for cube top
        public const float SIDE_FACE_ALPHA_FACTOR = 0.6f;       // Alpha factor for cube side face
        public const float MIN_MAGNITUDE_THRESHOLD = 0.01f;     // Minimum magnitude threshold for rendering

        // Performance settings
        public const int BATCH_SIZE = 32;                       // Batch size for operations
    }
    #endregion

    #region Fields
    private static readonly Lazy<CubesRenderer> _instance = new(() => new CubesRenderer());

    // Object pools for efficient resource management
    private readonly ObjectPool<SKPath> _pathPool = new(() => new SKPath(), path => path.Reset(), 5);
    private readonly ObjectPool<SKPaint> _paintPool = new(() => new SKPaint(), paint => paint.Reset(), 3);

    // Rendering state
    private readonly SKPath _cubeTopPath = new();

    // Quality-dependent settings
    private new bool _useAntiAlias = true;
    private SKSamplingOptions _samplingOptions = new(SKFilterMode.Linear, SKMipmapMode.Linear);
    private bool _useGlowEffects = true;

    // Thread safety
    private readonly object _renderLock = new();
    private new bool _disposed;
    #endregion

    #region Singleton Pattern
    private CubesRenderer() { }

    /// <summary>
    /// Gets the singleton instance of the cubes renderer.
    /// </summary>
    public static CubesRenderer GetInstance() => _instance.Value;
    #endregion

    #region Initialization and Configuration
    /// <summary>
    /// Initializes the cubes renderer and prepares rendering resources.
    /// </summary>
    public override void Initialize()
    {
        Safe(() =>
        {
            base.Initialize();

            // Apply initial quality settings
            ApplyQualitySettings();

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
    public override void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium)
    {
        Safe(() =>
        {
            base.Configure(isOverlayActive, quality);

            // Apply quality settings if changed
            if (_quality != quality)
            {
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

            _useGlowEffects = _quality switch
            {
                RenderQuality.Low => false,
                RenderQuality.Medium => true,
                RenderQuality.High => true,
                _ => true
            };

            _useAntiAlias = _quality != RenderQuality.Low;

            _samplingOptions = _quality switch
            {
                RenderQuality.Low => new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None),
                RenderQuality.Medium => new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),
                RenderQuality.High => new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),
                _ => new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)
            };
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.ApplyQualitySettings",
            ErrorMessage = "Failed to apply quality settings"
        });
    }
    #endregion

    #region Rendering
    /// <summary>
    /// Renders the 3D cubes visualization on the canvas using spectrum data.
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

        // Define render bounds and quick reject if not visible
        SKRect renderBounds = new(0, 0, info.Width, info.Height);
        if (canvas!.QuickReject(renderBounds))
        {
            drawPerformanceInfo?.Invoke(canvas, info);
            return;
        }

        Safe(() =>
        {
            int spectrumLength = spectrum!.Length;
            int actualBarCount = Min(spectrumLength, barCount);
            float[] processedSpectrum = PrepareSpectrum(spectrum, actualBarCount, spectrumLength);

            RenderSpectrum(canvas, processedSpectrum, info, barWidth, barSpacing, paint!);
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.Render",
            ErrorMessage = "Error during rendering"
        });

        drawPerformanceInfo?.Invoke(canvas!, info);
    }

    /// <summary>
    /// Renders the spectrum as 3D cubes.
    /// </summary>
    [MethodImpl(AggressiveOptimization)]
    private void RenderSpectrum(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        SKPaint basePaint)
    {
        Safe(() =>
        {
            float canvasHeight = info.Height;

            using var cubePaint = _paintPool.Get();
            cubePaint.Color = basePaint.Color;
            cubePaint.IsAntialias = _useAntiAlias;

            for (int i = 0; i < spectrum.Length; i++)
            {
                float magnitude = spectrum[i];
                if (magnitude < Constants.MIN_MAGNITUDE_THRESHOLD)
                    continue;

                float height = magnitude * canvasHeight;
                float x = i * (barWidth + barSpacing);
                float y = canvasHeight - height;

                // Skip rendering if the cube is outside the visible area
                if (canvas.QuickReject(new SKRect(x, y, x + barWidth, y + height)))
                    continue;

                // Set the alpha value based on magnitude
                cubePaint.Color = basePaint.Color.WithAlpha((byte)(magnitude * Constants.ALPHA_MULTIPLIER));

                // Render the cube
                RenderCube(canvas, x, y, barWidth, height, magnitude, cubePaint);
            }
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.RenderSpectrum",
            ErrorMessage = "Error rendering spectrum"
        });
    }

    /// <summary>
    /// Renders a single 3D cube at the specified position.
    /// </summary>
    private void RenderCube(
        SKCanvas canvas,
        float x,
        float y,
        float barWidth,
        float height,
        float magnitude,
        SKPaint paint)
    {
        Safe(() =>
        {
            // Front face rendering
            canvas.DrawRect(x, y, barWidth, height, paint);

            // Only render 3D effects if enabled
            if (_useGlowEffects)
            {
                float topRightX = x + barWidth;
                float topOffsetX = barWidth * Constants.CUBE_TOP_WIDTH_PROPORTION;
                float topOffsetY = barWidth * Constants.CUBE_TOP_HEIGHT_PROPORTION;

                // Top face rendering
                using var topPath = _pathPool.Get();
                topPath.Reset();
                topPath.MoveTo(x, y);
                topPath.LineTo(topRightX, y);
                topPath.LineTo(x + topOffsetX, y - topOffsetY);
                topPath.LineTo(x - (barWidth - topOffsetX), y - topOffsetY);
                topPath.Close();

                using var topPaint = _paintPool.Get();
                topPaint.Color = paint.Color.WithAlpha(
                    (byte)(magnitude * Constants.ALPHA_MULTIPLIER * Constants.TOP_ALPHA_FACTOR));
                topPaint.IsAntialias = paint.IsAntialias;
                topPaint.Style = paint.Style;
                canvas.DrawPath(topPath, topPaint);

                // Side face rendering
                using var sidePath = _pathPool.Get();
                sidePath.Reset();
                sidePath.MoveTo(topRightX, y);
                sidePath.LineTo(topRightX, y + height);
                sidePath.LineTo(x + topOffsetX, y - topOffsetY + height);
                sidePath.LineTo(x + topOffsetX, y - topOffsetY);
                sidePath.Close();

                using var sidePaint = _paintPool.Get();
                sidePaint.Color = paint.Color.WithAlpha(
                    (byte)(magnitude * Constants.ALPHA_MULTIPLIER * Constants.SIDE_FACE_ALPHA_FACTOR));
                sidePaint.IsAntialias = paint.IsAntialias;
                sidePaint.Style = paint.Style;
                canvas.DrawPath(sidePath, sidePaint);
            }
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.RenderCube",
            ErrorMessage = "Error rendering cube"
        });
    }
    #endregion

    #region Helper Methods
    /// <summary>
    /// Determines if the magnitude is significant enough to render.
    /// </summary>
    [MethodImpl(AggressiveInlining)]
    private bool IsSignificantMagnitude(float magnitude) =>
        magnitude >= Constants.MIN_MAGNITUDE_THRESHOLD;

    /// <summary>
    /// Calculates the alpha value based on magnitude and a factor.
    /// </summary>
    [MethodImpl(AggressiveInlining)]
    private byte CalculateAlpha(float magnitude, float factor) =>
        (byte)Min(magnitude * Constants.ALPHA_MULTIPLIER * factor, 255);
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
                _cubeTopPath?.Dispose();
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