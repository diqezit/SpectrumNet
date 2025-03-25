#nullable enable

using Parallel = System.Threading.Tasks.Parallel;
using static System.Math;
using static System.Numerics.Vector;
using static System.Runtime.CompilerServices.MethodImplOptions;
using static SkiaSharp.SKPaintStyle;
using static SkiaSharp.SKBlurStyle;
 
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
        protected SKSamplingOptions _samplingOptions = new(SKFilterMode.Linear, SKMipmapMode.Linear);
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

            if (targetCount >= PARALLEL_BATCH_SIZE && IsHardwareAccelerated)
            {
                Parallel.For(0, targetCount, i =>
                {
                    int start = (int)(i * blockSize);
                    int end = Min((int)((i + 1) * blockSize), spectrumLength);
                    scaledSpectrum[i] = CalculateBlockAverage(spectrum, start, end);
                });
            }
            else
            {
                for (int i = 0; i < targetCount; i++)
                {
                    int start = (int)(i * blockSize);
                    int end = Min((int)((i + 1) * blockSize), spectrumLength);
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
        [MethodImpl(AggressiveInlining)]
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

            if (IsHardwareAccelerated && targetCount >= System.Numerics.Vector<float>.Count)
            {
                int vectorSize = System.Numerics.Vector<float>.Count;
                int vectorizedLength = targetCount - (targetCount % vectorSize);

                for (int i = 0; i < vectorizedLength; i += vectorSize)
                {
                    System.Numerics.Vector<float> currentValues = new(spectrum, i);
                    System.Numerics.Vector<float> previousValues = new(_previousSpectrum, i);
                    System.Numerics.Vector<float> smoothedValues = previousValues * (1 - smoothing) + currentValues * smoothing;

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
        [MethodImpl(AggressiveInlining)]
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
        protected SKPaint CreateBasicPaint(SKColor color, SKPaintStyle style = Fill) => new()
        {
            Color = color,
            Style = style,
            IsAntialias = _useAntiAlias
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
            MaskFilter = SKMaskFilter.CreateBlur(Normal, blurRadius)
        };
        #endregion

        #region Drawing Utilities

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
            int actualBarCount = Min(spectrumLength, barCount);
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
            (_useAntiAlias, _useAdvancedEffects) = QualityBasedSettings();
        }

        /// <summary>
        /// Determines quality-based settings based on the current RenderQuality enum value.
        /// </summary>
        /// <returns>A tuple containing useAntiAlias, filterQuality, and useAdvancedEffects settings.</returns>
        protected virtual (bool useAntiAlias, bool useAdvancedEffects) QualityBasedSettings() => _quality switch
        {
            RenderQuality.Low => (false, false),
            RenderQuality.Medium => (true, true),
            RenderQuality.High => (true, true),
            _ => (true, true)
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