#nullable enable

using Parallel = System.Threading.Tasks.Parallel;

namespace SpectrumNet
{
    /// <summary>
    /// Base class for spectrum renderers, providing common functionalities for spectrum data processing,
    /// rendering quality settings, and resource management.
    /// </summary>
    public abstract class BaseSpectrumRenderer : ISpectrumRenderer
    {
        #region Constants
        /// <summary>
        /// Default smoothing factor for spectrum data.
        /// </summary>
        protected const float DEFAULT_SMOOTHING_FACTOR = 0.3f;
        /// <summary>
        /// Smoothing factor for spectrum data when overlay is active.
        /// </summary>
        protected const float OVERLAY_SMOOTHING_FACTOR = 0.5f;
        /// <summary>
        /// Minimum magnitude threshold to consider spectrum data significant.
        /// </summary>
        protected const float MIN_MAGNITUDE_THRESHOLD = 0.01f;
        /// <summary>
        /// Batch size for parallel processing of spectrum data.
        /// </summary>
        protected const int PARALLEL_BATCH_SIZE = 32;
        #endregion

        #region Fields
        /// <summary>
        /// Flag indicating if the renderer is initialized.
        /// </summary>
        protected bool _isInitialized;
        /// <summary>
        /// Current rendering quality level.
        /// </summary>
        protected RenderQuality _quality = RenderQuality.Medium;
        /// <summary>
        /// Stores the previous and processed spectrum data for smoothing.
        /// </summary>
        protected float[]? _previousSpectrum, _processedSpectrum;
        /// <summary>
        /// Smoothing factor applied to the spectrum data.
        /// </summary>
        protected float _smoothingFactor = DEFAULT_SMOOTHING_FACTOR;
        /// <summary>
        /// Flags to control anti-aliasing and advanced rendering effects.
        /// </summary>
        protected bool _useAntiAlias = true, _useAdvancedEffects = true;
        /// <summary>
        /// Filter quality used for SkiaSharp rendering.
        /// </summary>
        protected SKFilterQuality _filterQuality = SKFilterQuality.Medium;
        /// <summary>
        /// Semaphore to control access to spectrum processing for thread safety.
        /// </summary>
        protected readonly SemaphoreSlim _spectrumSemaphore = new(1, 1);
        /// <summary>
        /// Lock object for thread-safe operations on spectrum data.
        /// </summary>
        protected readonly object _spectrumLock = new();
        /// <summary>
        /// Flag indicating if the renderer has been disposed.
        /// </summary>
        protected bool _disposed;
        #endregion

        #region Properties
        /// <summary>
        /// Gets or sets the rendering quality. Changing the quality will trigger ApplyQualitySettings.
        /// </summary>
        public RenderQuality Quality
        {
            get => _quality;
            set
            {
                if (_quality != value)
                {
                    _quality = value;
                    ApplyQualitySettings();
                }
            }
        }
        #endregion

        #region Initialization and Configuration
        /// <summary>
        /// Initializes the renderer, setting the initialization flag and logging the event.
        /// </summary>
        public virtual void Initialize() => SmartLogger.Safe(
            () =>
            {
                if (!_isInitialized)
                {
                    _isInitialized = true;
                    SmartLogger.Log(LogLevel.Debug, $"[{GetType().Name}]", "Initialized");
                }
            },
            new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{GetType().Name}.Initialize",
                ErrorMessage = "Failed to initialize renderer"
            });

        /// <summary>
        /// Configures the renderer with overlay status and rendering quality, setting smoothing factor and quality settings.
        /// </summary>
        /// <param name="isOverlayActive">Indicates if the renderer is used as an overlay.</param>
        /// <param name="quality">Desired rendering quality level.</param>
        public virtual void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium) => SmartLogger.Safe(
            () =>
            {
                Quality = quality;
                _smoothingFactor = isOverlayActive ? OVERLAY_SMOOTHING_FACTOR : DEFAULT_SMOOTHING_FACTOR;
            },
            new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{GetType().Name}.Configure",
                ErrorMessage = "Failed to configure renderer"
            });
        #endregion

        #region Abstract Methods
        /// <summary>
        /// Abstract method to render the spectrum visualization. Must be implemented by derived classes.
        /// </summary>
        /// <param name="canvas">SkiaSharp canvas to draw on.</param>
        /// <param name="spectrum">Spectrum data array.</param>
        /// <param name="info">Image information for rendering context.</param>
        /// <param name="barWidth">Width of individual bars in the visualization.</param>
        /// <param name="barSpacing">Spacing between bars.</param>
        /// <param name="barCount">Number of bars to render.</param>
        /// <param name="paint">Paint object for styling the bars.</param>
        /// <param name="drawPerformanceInfo">Action to draw performance information on the canvas.</param>
        public abstract void Render(
            SKCanvas? canvas,
            float[]? spectrum,
            SKImageInfo info,
            float barWidth,
            float barSpacing,
            int barCount,
            SKPaint? paint,
            Action<SKCanvas, SKImageInfo> drawPerformanceInfo);
        #endregion

        #region Spectrum Processing
        /// <summary>
        /// Scales the spectrum data to a target bar count, averaging spectrum blocks.
        /// </summary>
        /// <param name="spectrum">Input spectrum data.</param>
        /// <param name="targetCount">Target number of data points.</param>
        /// <param name="spectrumLength">Original length of the spectrum data.</param>
        /// <returns>Scaled spectrum data array.</returns>
        protected float[] ScaleSpectrum(float[] spectrum, int targetCount, int spectrumLength)
        {
            float[] scaledSpectrum = new float[targetCount];
            float blockSize = (float)spectrumLength / targetCount;

            if (targetCount >= PARALLEL_BATCH_SIZE && Vector.IsHardwareAccelerated)
            {
                Parallel.For(0, targetCount, i =>
                {
                    int start = (int)(i * blockSize);
                    int end = Math.Min((int)((i + 1) * blockSize), spectrumLength);
                    scaledSpectrum[i] = CalculateBlockAverage(spectrum, start, end);
                });
            }
            else
            {
                for (int i = 0; i < targetCount; i++)
                {
                    int start = (int)(i * blockSize);
                    int end = Math.Min((int)((i + 1) * blockSize), spectrumLength);
                    scaledSpectrum[i] = CalculateBlockAverage(spectrum, start, end);
                }
            }

            return scaledSpectrum;
        }

        /// <summary>
        /// Calculates the average value of a block within the spectrum data.
        /// </summary>
        /// <param name="spectrum">Spectrum data array.</param>
        /// <param name="start">Start index of the block.</param>
        /// <param name="end">End index of the block.</param>
        /// <returns>Average value of the block.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float CalculateBlockAverage(float[] spectrum, int start, int end)
        {
            float sum = 0;
            for (int j = start; j < end; j++)
                sum += spectrum[j];
            return sum / (end - start);
        }

        /// <summary>
        /// Smooths the spectrum data using a smoothing factor, applying vectorization for performance if available.
        /// </summary>
        /// <param name="spectrum">Input spectrum data.</param>
        /// <param name="targetCount">Target number of data points.</param>
        /// <param name="customSmoothingFactor">Optional custom smoothing factor. If null, default smoothing factor is used.</param>
        /// <returns>Smoothed spectrum data array.</returns>
        protected float[] SmoothSpectrum(float[] spectrum, int targetCount, float? customSmoothingFactor = null)
        {
            float smoothing = customSmoothingFactor ?? _smoothingFactor;
            if (_previousSpectrum == null || _previousSpectrum.Length != targetCount)
                _previousSpectrum = new float[targetCount];

            float[] smoothedSpectrum = new float[targetCount];

            if (Vector.IsHardwareAccelerated && targetCount >= Vector<float>.Count)
            {
                int vectorSize = Vector<float>.Count;
                int vectorizedLength = targetCount - (targetCount % vectorSize);

                for (int i = 0; i < vectorizedLength; i += vectorSize)
                {
                    Vector<float> currentValues = new(spectrum, i);
                    Vector<float> previousValues = new(_previousSpectrum, i);
                    Vector<float> smoothedValues = previousValues * (1 - smoothing) + currentValues * smoothing;

                    smoothedValues.CopyTo(smoothedSpectrum, i);
                    smoothedValues.CopyTo(_previousSpectrum, i);
                }

                for (int i = vectorizedLength; i < targetCount; i++)
                    ProcessSingleSpectrumValue(spectrum, smoothedSpectrum, i, smoothing);
            }
            else
            {
                for (int i = 0; i < targetCount; i++)
                    ProcessSingleSpectrumValue(spectrum, smoothedSpectrum, i, smoothing);
            }

            return smoothedSpectrum;
        }

        /// <summary>
        /// Processes a single spectrum value for smoothing, using previous value and smoothing factor.
        /// </summary>
        /// <param name="spectrum">Input spectrum data.</param>
        /// <param name="smoothedSpectrum">Output smoothed spectrum data array.</param>
        /// <param name="i">Current index.</param>
        /// <param name="smoothing">Smoothing factor.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void ProcessSingleSpectrumValue(float[] spectrum, float[] smoothedSpectrum, int i, float smoothing)
        {
            if (_previousSpectrum == null) return;

            float currentValue = spectrum[i];
            float smoothedValue = _previousSpectrum[i] * (1 - smoothing) + currentValue * smoothing;

            smoothedSpectrum[i] = smoothedValue;
            _previousSpectrum[i] = smoothedValue;
        }
        #endregion

        #region Enhanced Methods
        /// <summary>
        /// Prepares the spectrum data by scaling and smoothing it. Uses a semaphore to ensure thread-safe processing.
        /// </summary>
        /// <param name="spectrum">Input spectrum data.</param>
        /// <param name="targetCount">Target number of data points.</param>
        /// <param name="spectrumLength">Original length of the spectrum data.</param>
        /// <returns>Processed spectrum data array.</returns>
        protected float[] PrepareSpectrum(float[] spectrum, int targetCount, int spectrumLength)
        {
            bool semaphoreAcquired = false;
            try
            {
                semaphoreAcquired = _spectrumSemaphore.Wait(0);
                if (semaphoreAcquired)
                {
                    float[] scaledSpectrum = ScaleSpectrum(spectrum, targetCount, spectrumLength);
                    _processedSpectrum = SmoothSpectrum(scaledSpectrum, targetCount);
                }

                lock (_spectrumLock)
                {
                    return _processedSpectrum != null && _processedSpectrum.Length == targetCount
                        ? _processedSpectrum
                        : ScaleSpectrum(spectrum, targetCount, spectrumLength);
                }
            }
            finally
            {
                if (semaphoreAcquired)
                    _spectrumSemaphore.Release();
            }
        }

        /// <summary>
        /// Performs a quick validation to ensure all required parameters are valid before rendering.
        /// </summary>
        /// <param name="canvas">SkiaSharp canvas object.</param>
        /// <param name="spectrum">Spectrum data array.</param>
        /// <param name="info">Image information object.</param>
        /// <param name="paint">Paint object.</param>
        /// <returns>True if all parameters are valid, otherwise false.</returns>
        protected bool QuickValidate(SKCanvas? canvas, float[]? spectrum, SKImageInfo info, SKPaint? paint) =>
            _isInitialized && canvas != null && spectrum != null && spectrum.Length > 0 && paint != null && info.Width > 0 && info.Height > 0;

        /// <summary>
        /// Checks if a given render area is visible on the canvas to optimize rendering.
        /// </summary>
        /// <param name="canvas">SkiaSharp canvas object.</param>
        /// <param name="x">X-coordinate of the render area.</param>
        /// <param name="y">Y-coordinate of the render area.</param>
        /// <param name="width">Width of the render area.</param>
        /// <param name="height">Height of the render area.</param>
        /// <returns>True if the render area is visible, otherwise false.</returns>
        protected bool IsRenderAreaVisible(SKCanvas canvas, float x, float y, float width, float height) =>
            !canvas.QuickReject(new SKRect(x, y, x + width, y + height));
        #endregion

        #region Paint Utilities
        /// <summary>
        /// Creates a basic SKPaint object with specified color and style, using current anti-alias and filter quality settings.
        /// </summary>
        /// <param name="color">Color of the paint.</param>
        /// <param name="style">Paint style (Fill, Stroke, etc.). Default is Fill.</param>
        /// <returns>A new SKPaint object.</returns>
        protected SKPaint CreateBasicPaint(SKColor color, SKPaintStyle style = SKPaintStyle.Fill) => new()
        {
            Color = color,
            Style = style,
            IsAntialias = _useAntiAlias,
            FilterQuality = _filterQuality
        };

        /// <summary>
        /// Creates an SKPaint object with a glow effect, using current anti-alias and filter quality settings.
        /// </summary>
        /// <param name="color">Base color of the glow.</param>
        /// <param name="blurRadius">Radius of the blur effect.</param>
        /// <param name="alpha">Alpha value of the glow color.</param>
        /// <returns>A new SKPaint object with glow effect.</returns>
        protected SKPaint CreateGlowPaint(SKColor color, float blurRadius, byte alpha) => new()
        {
            Color = color.WithAlpha(alpha),
            IsAntialias = _useAntiAlias,
            FilterQuality = _filterQuality,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, blurRadius)
        };
        #endregion

        #region Drawing Utilities
        /// <summary>
        /// Draws a path with a linear gradient fill.
        /// </summary>
        /// <param name="canvas">SkiaSharp canvas to draw on.</param>
        /// <param name="path">Path to be drawn.</param>
        /// <param name="start">Start point of the gradient.</param>
        /// <param name="end">End point of the gradient.</param>
        /// <param name="startColor">Starting color of the gradient.</param>
        /// <param name="endColor">Ending color of the gradient.</param>
        /// <param name="style">Paint style (Fill, Stroke, etc.). Default is Fill.</param>
        protected void DrawGradientPath(
            SKCanvas canvas,
            SKPath path,
            SKPoint start,
            SKPoint end,
            SKColor startColor,
            SKColor endColor,
            SKPaintStyle style = SKPaintStyle.Fill)
        {
            using var paint = new SKPaint
            {
                IsAntialias = _useAntiAlias,
                FilterQuality = _filterQuality,
                Style = style,
                Shader = SKShader.CreateLinearGradient(
                    start,
                    end,
                    new[] { startColor, endColor },
                    null,
                    SKShaderTileMode.Clamp)
            };

            canvas.DrawPath(path, paint);
        }

        /// <summary>
        /// Renders visualization using a template method, handling validation, spectrum processing, and performance info drawing.
        /// </summary>
        /// <param name="canvas">SkiaSharp canvas to draw on.</param>
        /// <param name="spectrum">Spectrum data array.</param>
        /// <param name="info">Image information for rendering context.</param>
        /// <param name="barWidth">Width of individual bars.</param>
        /// <param name="barSpacing">Spacing between bars.</param>
        /// <param name="barCount">Number of bars to render.</param>
        /// <param name="paint">Paint object for styling.</param>
        /// <param name="drawPerformanceInfo">Action to draw performance information.</param>
        /// <param name="renderImplementation">Action that implements the specific rendering logic.</param>
        protected void RenderWithTemplate(
            SKCanvas? canvas,
            float[]? spectrum,
            SKImageInfo info,
            float barWidth,
            float barSpacing,
            int barCount,
            SKPaint? paint,
            Action<SKCanvas, SKImageInfo> drawPerformanceInfo,
            Action<SKCanvas, float[], SKImageInfo, float, float, SKPaint> renderImplementation)
        {
            if (!QuickValidate(canvas, spectrum, info, paint))
                return;

            if (canvas!.QuickReject(new SKRect(0, 0, info.Width, info.Height)))
            {
                drawPerformanceInfo?.Invoke(canvas, info);
                return;
            }

            int spectrumLength = spectrum!.Length;
            int actualBarCount = Math.Min(spectrumLength, barCount);
            float[] processedSpectrum = PrepareSpectrum(spectrum, actualBarCount, spectrumLength);

            renderImplementation(canvas!, processedSpectrum, info, barWidth, barSpacing, paint!);
            drawPerformanceInfo?.Invoke(canvas!, info);
        }
        #endregion

        #region Quality Settings
        /// <summary>
        /// Applies quality settings by updating anti-alias, filter quality, and advanced effects flags based on the current quality level.
        /// </summary>
        protected virtual void ApplyQualitySettings()
        {
            (_useAntiAlias, _filterQuality, _useAdvancedEffects) = QualityBasedSettings();
        }

        /// <summary>
        /// Determines quality-based settings based on the current RenderQuality enum value.
        /// </summary>
        /// <returns>A tuple containing useAntiAlias, filterQuality, and useAdvancedEffects settings.</returns>
        protected virtual (bool useAntiAlias, SKFilterQuality filterQuality, bool useAdvancedEffects) QualityBasedSettings() => _quality switch
        {
            RenderQuality.Low => (false, SKFilterQuality.Low, false),
            RenderQuality.Medium => (true, SKFilterQuality.Medium, true),
            RenderQuality.High => (true, SKFilterQuality.High, true),
            _ => (true, SKFilterQuality.Medium, true)
        };
        #endregion

        #region Disposal
        /// <summary>
        /// Disposes of resources used by the renderer, including semaphore and spectrum data arrays.
        /// </summary>
        public virtual void Dispose()
        {
            if (!_disposed)
            {
                _spectrumSemaphore?.Dispose();
                _previousSpectrum = null;
                _processedSpectrum = null;
                _disposed = true;
                SmartLogger.Log(LogLevel.Debug, $"[{GetType().Name}]", "Disposed");
            }
        }
        #endregion
    }
}

#region Guide to Developing Renderers Based on `BaseSpectrumRenderer`

/// #region Guide to Developing Renderers Based on `BaseSpectrumRenderer`
///
/// This guide explains how to create custom spectrum renderers by extending the `BaseSpectrumRenderer` class.
/// It covers the structure, key components, and best practices for developing efficient and maintainable renderers.
///
/// ### Class Structure and Singleton Pattern
///
/// Custom renderers should follow a Singleton pattern and inherit from `BaseSpectrumRenderer`. Here’s a basic example:
///
/// 
/// public sealed class CustomRenderer : BaseSpectrumRenderer
/// {
///      #region Constants // Constants are grouped here
///      private static class Constants
///      {
///          public const string LOG_PREFIX = "CustomRenderer"; // Prefix for logging messages
///
///          // Rendering parameters - settings related to visual appearance
///          public const float DEFAULT_ROTATION_SPEED = 0.5f;
///          public const float RADIUS_PROPORTION = 0.4f;
///
///          // Animation constants - settings for animations and dynamic effects
///          public const float MIN_AMPLITUDE = 0.01f;
///          public const float MAX_AMPLITUDE = 1.0f;
///      }
///      #endregion
///
///      #region Fields // Class variables are declared here
///      private static readonly Lazy<CustomRenderer> _instance = new(() => new CustomRenderer()); // Singleton instance
///      private readonly SKPath _renderPath = new(); // Path for rendering, optimized for GPU
///      #endregion
///
///      private CustomRenderer() { } // Private constructor to enforce Singleton pattern
///      public static CustomRenderer GetInstance() => _instance.Value; // Method to access the Singleton instance
/// }
/// 
///
/// **Key points:**
///
/// *   **Singleton:** Ensures only one instance of the renderer exists, which is efficient for resource management.
/// *   **Constants:**  Use a nested `Constants` class to organize all constant values. This makes it easier to find and modify settings.
/// *   **Regions:**  Use `#region` to group code into logical blocks like Constants and Fields, improving code readability.
///
/// ## Inheritance and Using Base Class Features
///
/// Your custom renderer inherits functionalities from `BaseSpectrumRenderer`. Here are the important parts of the base class you'll be using:
///
/// ### Protected Fields in `BaseSpectrumRenderer`
///
/// These fields control the renderer's state and behavior. You can access them in your custom renderer.
///
/// 
/// // State and Settings
/// protected bool _isInitialized; // Tracks if the renderer is initialized
/// protected RenderQuality _quality; // Current rendering quality (Low, Medium, High)
/// protected float _smoothingFactor; // Smoothing factor for spectrum animation
/// protected bool _disposed; // Indicates if resources have been released
///
/// // Quality Settings - controlled by RenderQuality property
/// protected bool _useAntiAlias; // Enables/disables anti-aliasing for smoother visuals
/// protected SKFilterQuality _filterQuality; // Quality of image filtering (Low, Medium, High)
/// protected bool _useAdvancedEffects; // Enables/disables advanced visual effects
///
/// // Spectrum Processing - for managing audio spectrum data
/// protected float[]? _previousSpectrum, _processedSpectrum; // Stores spectrum data for smoothing and processing
/// protected readonly SemaphoreSlim _spectrumSemaphore = new(1, 1); // Controls access to spectrum processing for thread safety
/// protected readonly object _spectrumLock = new(); // Lock object for thread-safe operations
/// 
///
/// ### Key Methods in the Base Class
///
/// These methods provide ready-to-use functionalities in your custom renderer.
///
/// 
/// // Spectrum Processing - methods to manipulate spectrum data
/// protected float[] ScaleSpectrum(float[] spectrum, int targetCount, int spectrumLength); // Scales spectrum data to a target bar count
/// protected float[] SmoothSpectrum(float[] spectrum, int targetCount, float? customSmoothingFactor = null); // Smooths spectrum for visual continuity
/// protected float[] PrepareSpectrum(float[] spectrum, int targetCount, int spectrumLength); // Combines scaling and smoothing for optimized spectrum data
///
/// // Validation and Optimization - methods for performance and error checking
/// protected bool QuickValidate(SKCanvas? canvas, float[]? spectrum, SKImageInfo info, SKPaint? paint); // Quick checks to ensure valid rendering parameters
/// protected bool IsRenderAreaVisible(SKCanvas canvas, float x, float y, float width, float height); // Checks if a given area is within the visible canvas
///
/// // Utility Methods - for creating drawing tools
/// protected SKPaint CreateBasicPaint(SKColor color, SKPaintStyle style = SKPaintStyle.Fill); // Creates a basic SKPaint object with color and style
/// protected SKPaint CreateGlowPaint(SKColor color, float blurRadius, byte alpha); // Creates an SKPaint object with a glow effect
/// 
///
/// ## Overriding Virtual Methods
///
/// You'll override virtual methods from `BaseSpectrumRenderer` to customize the renderer's behavior.
///
/// ### `Initialize`
///
/// Called when the renderer starts up. Use this to set up your renderer's specific resources.
///
/// 
/// #region Initialization
/// public override void Initialize() => SmartLogger.Safe(() =>
/// {
///      base.Initialize(); // Call the base class initialization first
///      _renderPath?.Reset(); // Reset any paths or geometries
///      InitializeRenderResources(); // Initialize custom drawing resources (like brushes)
///      SmartLogger.Log(LogLevel.Debug, Constants.LOG_PREFIX, "Initialized"); // Log the initialization event
/// }, new SmartLogger.ErrorHandlingOptions
/// {
///      Source = $"{Constants.LOG_PREFIX}.Initialize",
///      ErrorMessage = "Failed to initialize renderer" // Error message for logging
/// });
/// #endregion
/// 
///
/// ### `Configure` and `ApplyQualitySettings`
///
/// `Configure` is called to set initial settings, and `ApplyQualitySettings` is called when the rendering quality changes.
///
/// 
/// #region Configuration
/// public override void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium) =>
///      SmartLogger.Safe(() =>
///      {
///          base.Configure(isOverlayActive, quality); // Configure base settings first
///          ApplyQualitySettings(); // Apply quality-specific settings
///      }, new SmartLogger.ErrorHandlingOptions
///      {
///          Source = $"{Constants.LOG_PREFIX}.Configure",
///          ErrorMessage = "Failed to configure renderer" // Error message for logging
///      });
///
/// protected override void ApplyQualitySettings() => SmartLogger.Safe(() =>
/// {
///      base.ApplyQualitySettings(); // Apply base quality settings first
///
///      _useGlowEffects = _quality switch // Example: enable glow effects based on quality
///      {
///          RenderQuality.Low => false,
///          RenderQuality.Medium => true,
///          RenderQuality.High => true,
///          _ => true
///      };
///
///      InitializeRenderResources(); // Re-initialize resources based on new quality settings
/// }, new SmartLogger.ErrorHandlingOptions
/// {
///      Source = $"{Constants.LOG_PREFIX}.ApplyQualitySettings",
///      ErrorMessage = "Failed to apply quality settings" // Error message for logging
/// });
/// #endregion
/// 
///
/// ### `Render`
///
/// This is the core method where you draw your spectrum visualization.
///
/// 
/// #region Rendering
/// public override void Render(
///      SKCanvas? canvas,
///      float[]? spectrum,
///      SKImageInfo info,
///      float barWidth,
///      float barSpacing,
///      int barCount,
///      SKPaint? paint,
///      Action<SKCanvas, SKImageInfo>? drawPerformanceInfo)
/// {
///      // Basic validation using base class method
///      if (!QuickValidate(canvas, spectrum, info, paint))
///      {
///          drawPerformanceInfo?.Invoke(canvas!, info); // Still call performance info even if validation fails
///          return;
///      }
///
///      // Calculate rendering parameters - specific to your visual style
///      int pointCount = Math.Min(spectrum!.Length, barCount);
///      float centerX = info.Width / 2f, centerY = info.Height / 2f;
///      float radius = MathF.Min(info.Width, info.Height) * Constants.RADIUS_PROPORTION;
///
///      // Check if the render area is visible for optimization
///      SKRect renderBounds = new(
///          centerX - radius * 2f,
///          centerY - radius * 2f,
///          centerX + radius * 2f,
///          centerY + radius * 2f
///      );
///
///      if (canvas!.QuickReject(renderBounds)) // Skip rendering if area is not visible
///      {
///          drawPerformanceInfo?.Invoke(canvas, info); // Still call performance info if rejected
///          return;
///      }
///
///      // Spectrum processing and rendering within SmartLogger.Safe for error handling
///      SmartLogger.Safe(() =>
///      {
///          // Use base class method to prepare spectrum data (scaling and smoothing)
///          float[] processedSpectrum = PrepareSpectrum(spectrum, pointCount, spectrum.Length);
///
///          // Perform the custom visual rendering - this is where your unique drawing logic goes
///          RenderVisualEffect(canvas, processedSpectrum, info, paint!, barWidth);
///      }, new SmartLogger.ErrorHandlingOptions
///      {
///          Source = $"{Constants.LOG_PREFIX}.Render",
///          ErrorMessage = "Error during rendering" // Error message for logging
///      });
///
///      drawPerformanceInfo?.Invoke(canvas, info); // Call performance info after rendering
/// }
/// #endregion
/// 
///
/// ## GPU-Optimized Rendering
///
/// This section describes how to optimize rendering for the GPU using `SKPath` and efficient drawing operations.
///
/// 
/// #region GPU Rendering
/// private void RenderVisualEffect(
///      SKCanvas canvas,
///      float[] spectrum,
///      SKImageInfo info,
///      SKPaint basePaint,
///      float barWidth) => SmartLogger.Safe(() =>
/// {
///      if (canvas == null || spectrum == null || basePaint == null || _disposed)
///          return;
///
///      // Prepare render points array if needed, matching spectrum length
///      if (_renderPoints == null || _renderPoints.Length != spectrum.Length)
///          _renderPoints = new SKPoint[spectrum.Length];
///
///      // Populate the render points array based on spectrum data - IMPLEMENT THIS IN YOUR RENDERER
///      int validPointCount = PrepareRenderPoints(spectrum, info, _renderPoints);
///
///      if (validPointCount == 0) // Exit if no points to render
///          return;
///
///      // Reset the render path to prepare for new frame
///      _renderPath.Reset();
///
///      // Create a new array with only valid points to optimize path creation
///      SKPoint[] validPoints = new SKPoint[validPointCount];
///      Array.Copy(_renderPoints, 0, validPoints, 0, validPointCount);
///
///      // Add all points to the path in a single call for GPU efficiency
///      _renderPath.AddPoly(validPoints, true);
///
///      // Render the visual effect with path and paints, applying quality-based effects
///      RenderWithEffects(canvas, _renderPath, basePaint, barWidth);
///
/// }, new SmartLogger.ErrorHandlingOptions
/// {
///      Source = $"{Constants.LOG_PREFIX}.RenderVisualEffect",
///      ErrorMessage = "Error rendering visual effect" // Error message for logging
/// });
/// #endregion
/// 
///
/// **Important:**
///
/// *   **`PrepareRenderPoints`**:  This method (not provided in the base class, you need to implement it) is crucial.
///     It calculates the `SKPoint` array (`_renderPoints`) based on the `spectrum` data and your desired visual style.
///     This is where you define the geometry of your visualization.
/// *   **`SKPath.AddPoly`**:  Efficiently creates a path from an array of points, optimized for GPU rendering.
/// *   **`RenderWithEffects`**: This method (also needs to be implemented) applies visual effects (like glow, outlines, fills)
///     based on the rendering quality and your design.
///
/// ## GPU Resource Management
///
/// Efficiently manage `SKPaint` objects for drawing.
///
/// 
/// #region Resource Management
/// private void InitializeRenderResources() => SmartLogger.Safe(() =>
/// {
///      DisposeRenderPaints(); // Ensure old paints are disposed before creating new ones
///
///      _glowPaint = new SKPaint // Paint for glow effect
///      {
///          IsAntialias = _useAntiAlias,
///          FilterQuality = _filterQuality,
///          Style = SKPaintStyle.Fill
///      };
///
///      _fillPaint = new SKPaint // Paint for filling shapes
///      {
///          IsAntialias = _useAntiAlias,
///          FilterQuality = _filterQuality,
///          Style = SKPaintStyle.Fill
///      };
///
///      _outlinePaint = new SKPaint // Paint for outlines
///      {
///          IsAntialias = _useAntiAlias,
///          FilterQuality = _filterQuality,
///          Style = SKPaintStyle.Stroke
///      };
/// }, new SmartLogger.ErrorHandlingOptions
/// {
///      Source = $"{Constants.LOG_PREFIX}.InitializeRenderResources",
///      ErrorMessage = "Failed to initialize render resources" // Error message for logging
/// });
///
/// private void DisposeRenderPaints() => SmartLogger.Safe(() =>
/// {
///      SmartLogger.SafeDispose(_glowPaint, "glowPaint"); // Safely dispose of SKPaint objects
///      SmartLogger.SafeDispose(_fillPaint, "fillPaint");
///      SmartLogger.SafeDispose(_outlinePaint, "outlinePaint");
///
///      _glowPaint = _fillPaint = _outlinePaint = null; // Set paint variables to null after disposing
/// }, new SmartLogger.ErrorHandlingOptions
/// {
///      Source = $"{Constants.LOG_PREFIX}.DisposeRenderPaints",
///      ErrorMessage = "Failed to dispose render paints" // Error message for logging
/// });
/// #endregion
/// 
///
/// **Key points:**
///
/// *   **Initialization:**  `InitializeRenderResources` creates `SKPaint` objects when the renderer starts or when quality settings change.
/// *   **Disposal:** `DisposeRenderPaints` releases GPU resources when they are no longer needed, preventing memory leaks.
/// *   **Re-use:**  `SKPaint` objects are created once and reused for each frame, only their properties (like `Color`) are updated for different rendering styles.
///
/// ## Proper Resource Disposal
///
/// Override the `Dispose` method to release all resources when the renderer is no longer needed.
///
/// 
/// #region Disposal
/// public override void Dispose()
/// {
///      if (!_disposed)
///      {
///          SmartLogger.Safe(() =>
///          {
///              SmartLogger.SafeDispose(_renderPath, "renderPath"); // Dispose of SKPath
///              DisposeRenderPaints(); // Dispose of SKPaint objects
///              _renderPoints = null; // Release point array
///
///              base.Dispose(); // Call base class disposal to release base resources
///          }, new SmartLogger.ErrorHandlingOptions
///          {
///              Source = $"{Constants.LOG_PREFIX}.Dispose",
///              ErrorMessage = "Error disposing renderer" // Error message for logging
///          });
///
///          _disposed = true; // Mark as disposed to prevent double disposal
///          SmartLogger.Log(LogLevel.Debug, Constants.LOG_PREFIX, "Disposed"); // Log disposal event
///      }
/// }
/// #endregion
/// 
///
/// ## Key Development Principles
///
/// Follow these principles to create well-structured and efficient renderers:
///
/// 1.  **English-Only Code:**
///      *   Code comments should be minimal and in English, mainly for constant descriptions.
///      *   Use self-documenting names for methods and variables (English names).
///      *   Use `#region` blocks to organize code logically.
///
/// 2.  **Organize Constants:**
///      
///      private static class Constants
///      {
///          public const string LOG_PREFIX = "CustomRenderer";
///
///          // Rendering parameters
///          public const float DEFAULT_ROTATION_SPEED = 0.5f;
///
///          // Animation constants
///          public const float MIN_AMPLITUDE = 0.01f;
///      }
///      
///      *   Group constants within a nested `Constants` class.
///      *   Indent constants by category for clarity.
///      *   Use `ALL_CAPS` for constant names.
///      *   Prefix default values with `DEFAULT_`.
///
/// 3.  **Maximize Base Class Usage:**
///      *   Call `base.` methods when overriding virtual methods to reuse base functionality.
///      *   Utilize protected fields from `BaseSpectrumRenderer`.
///      *   Use provided spectrum processing methods (`ScaleSpectrum`, `SmoothSpectrum`, `PrepareSpectrum`).
///
/// 4.  **Optimize for GPU:**
///      *   Create and reuse resources (like `SKPaint` objects).
///      *   Use `QuickReject` to skip rendering of invisible areas.
///      *   Minimize draw calls by using `AddPoly` for paths.
///      *   Adjust visual effects based on the rendering quality.
///      *   Create `SKPaint` instances once during initialization and reuse them, updating properties as needed in the `RenderBars` method.
///      *   Use `DrawRoundRect` directly for drawing rounded rectangles.
///
/// 5.  **Handle Errors with `SmartLogger`:**
///      *   Wrap all methods in `SmartLogger.Safe` for robust error handling.
///      *   Provide clear source and error messages in `ErrorHandlingOptions`.
///      *   Use `SmartLogger.SafeDispose` for resource disposal.
///
/// 6.  **Adapt Rendering to Quality Settings:**
///      *   Use `_quality`, `_useAntiAlias`, `_filterQuality` to control rendering details.
///      *   Disable complex effects at lower quality settings for performance.
///      *   Update resources in `ApplyQualitySettings` when quality changes.
///
/// 7.  **Structure Code with Regions:**
///      
///      #region Constants
///      // Constants...
///      #endregion
///
///      #region Fields
///      // Fields...
///      #endregion
///      
///      *   Use regions to group code logically (Constants, Fields, Initialization, Rendering, etc.).
///
/// 8.  **Efficient Spectrum Processing:**
///      *   Use `PrepareSpectrum` for optimized spectrum data preparation.
///      *   Employ semaphores (`_spectrumSemaphore`) for thread safety during spectrum processing.
///      *   Cache processed spectrum data (`_processedSpectrum`, `_previousSpectrum`) for reuse and smoothing.
///
/// 9.  **Proper Resource Lifecycle Management:**
///      *   Initialize resources in `Initialize` or `InitializeRenderResources`.
///      *   Update resources in `ApplyQualitySettings` when quality changes.
///      *   Release all resources in `Dispose` to prevent leaks.
///
/// By following these guidelines, you can develop high-performance, GPU-optimized spectrum visualizers based on
/// `BaseSpectrumRenderer` with clean, maintainable, and English-language codebase.
#endregion