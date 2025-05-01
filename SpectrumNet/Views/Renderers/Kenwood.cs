#nullable enable

namespace SpectrumNet.Views.Renderers;

/// <summary>
/// Renderer that visualizes spectrum data as a classic Kenwood-style equalizer.
/// </summary>
public sealed class KenwoodRenderer : BaseSpectrumRenderer
{
    #region Singleton Pattern
    private static readonly Lazy<KenwoodRenderer> _instance = new(() => new KenwoodRenderer());
    private KenwoodRenderer() { } // Приватный конструктор
    public static KenwoodRenderer GetInstance() => _instance.Value;
    #endregion

    #region Constants
    private static class Constants
    {
        // Logging
        public const string LOG_PREFIX = "KenwoodRenderer";

        // Animation and smoothing factors
        public const float ANIMATION_SPEED = 0.85f;         // Speed of animation transitions
        public const float REFLECTION_OPACITY = 0.4f;       // Opacity of reflection effect
        public const float REFLECTION_HEIGHT = 0.6f;        // Height factor for reflection effect
        public const float PEAK_FALL_SPEED = 0.007f;        // Speed at which peaks fall
        public const float SCALE_FACTOR = 0.9f;             // Scaling factor for segment sizes
        public const float DESIRED_SEGMENT_HEIGHT = 10f;    // Desired height of each segment
        public const float SEGMENT_ROUNDNESS = 4f;          // Roundness factor for segments

        // Smoothing and transition defaults
        public const float DEFAULT_SMOOTHING_FACTOR = 1.5f; // Default factor for smoothing spectrum data
        public const float DEFAULT_TRANSITION_SMOOTHNESS = 1f; // Default smoothness for transitions

        // Buffer and pool settings
        public const int MAX_BUFFER_POOL_SIZE = 16;         // Maximum size of buffer pools
        public const int INITIAL_BUFFER_SIZE = 1024;        // Initial size for float and DateTime arrays

        // Rendering geometry
        public const float SEGMENT_GAP = 2f;                // Gap between segments
        public const int PEAK_HOLD_TIME_MS = 500;           // Time in milliseconds to hold peak values

        // Gradient and shadow
        public const float OUTLINE_STROKE_WIDTH = 1.2f;     // Width of segment outlines
        public const float SHADOW_BLUR_RADIUS = 3f;         // Blur radius for shadow effect

        // Spectrum processing factors
        public const float SPECTRUM_WEIGHT = 0.3f;          // Weight of peak values in spectrum scaling
        public const float BOOST_FACTOR = 0.3f;             // Boost factor for higher frequencies

        // Default quality setting
        public const RenderQuality DEFAULT_QUALITY = RenderQuality.Medium; // Default rendering quality level
    }
    #endregion

    #region Fields
    // Quality settings
    private new bool _useAntiAlias = true;
    private new SKSamplingOptions _samplingOptions = new(SKFilterMode.Linear, SKMipmapMode.Linear);
    private bool _enableShadows = true;
    private bool _enableReflections = true;
    private float _segmentRoundness = Constants.SEGMENT_ROUNDNESS;
    private bool _useSegmentedBars = true;

    // Smoothing and transition factors
    private float _smoothingFactor = Constants.DEFAULT_SMOOTHING_FACTOR;
    private float _transitionSmoothness = Constants.DEFAULT_TRANSITION_SMOOTHNESS;

    // Configuration values
    private readonly float _animationSpeed = Constants.ANIMATION_SPEED;
    private readonly float _reflectionOpacity = Constants.REFLECTION_OPACITY;
    private readonly float _reflectionHeight = Constants.REFLECTION_HEIGHT;
    private readonly float _peakFallSpeed = Constants.PEAK_FALL_SPEED;
    private readonly float _scaleFactor = Constants.SCALE_FACTOR;
    private readonly float _desiredSegmentHeight = Constants.DESIRED_SEGMENT_HEIGHT;

    // Bar and segment counters
    private int _currentSegmentCount, _currentBarCount, _lastRenderCount;
    private float _lastTotalBarWidth, _lastBarWidth, _lastBarSpacing, _lastSegmentHeight;
    private int _pendingBarCount;
    private float _pendingCanvasHeight;

    // Spectrum and processing buffers
    private float[]? _previousSpectrum, _peaks, _renderBarValues, _renderPeaks;
    private float[]? _processingBarValues, _processingPeaks, _pendingSpectrum, _velocities;
    private DateTime[]? _peakHoldTimes;

    // Cached SKPath objects
    private SKPath? _cachedBarPath, _cachedGreenSegmentsPath, _cachedYellowSegmentsPath, _cachedRedSegmentsPath;
    private SKPath? _cachedOutlinePath, _cachedPeakPath, _cachedShadowPath, _cachedReflectionPath;
    private bool _pathsNeedRebuild = true;
    private SKMatrix _lastTransform = SKMatrix.Identity;

    // Buffer pools
    private readonly ConcurrentQueue<float[]> _floatBufferPool = new();
    private readonly ConcurrentQueue<DateTime[]> _dateTimeBufferPool = new();

    // Synchronization primitives
    private readonly SemaphoreSlim _dataSemaphore = new(1, 1);
    private readonly AutoResetEvent _dataAvailableEvent = new(false);

    // Gradient shaders
    private SKShader? _barGradient, _greenGradient, _yellowGradient, _redGradient, _reflectionGradient;

    // Pre-configured SKPaint objects
    private readonly SKPaint _peakPaint = new()
    {
        IsAntialias = true,
        Style = Fill,
        Color = SKColors.White // White for peak indicators
    };

    private readonly SKPaint _segmentShadowPaint = new()
    {
        IsAntialias = true,
        Style = Fill,
        Color = new SKColor(0, 0, 0, 80), // Semi-transparent black for shadow
        MaskFilter = SKMaskFilter.CreateBlur(Normal, Constants.SHADOW_BLUR_RADIUS)
    };

    private readonly SKPaint _outlinePaint = new()
    {
        IsAntialias = true,
        Style = Stroke,
        Color = new SKColor(255, 255, 255, 60), // Semi-transparent white for outline
        StrokeWidth = Constants.OUTLINE_STROKE_WIDTH
    };

    private readonly SKPaint _reflectionPaint = new()
    {
        IsAntialias = true,
        Style = Fill,
        BlendMode = SKBlendMode.SrcOver
    };

    // Color definitions for gradients
    private static readonly SKColor _greenStartColor = new(0, 230, 120, 255); // Bottom of green gradient
    private static readonly SKColor _greenEndColor = new(0, 255, 0, 255);     // Top of green gradient
    private static readonly SKColor _yellowStartColor = new(255, 230, 0, 255); // Bottom of yellow gradient
    private static readonly SKColor _yellowEndColor = new(255, 180, 0, 255);   // Top of yellow gradient
    private static readonly SKColor _redStartColor = new(255, 80, 0, 255);     // Bottom of red gradient
    private static readonly SKColor _redEndColor = new(255, 30, 0, 255);       // Top of red gradient

    // Calculation thread objects
    private Task? _calculationTask;
    private CancellationTokenSource? _calculationCts;
    private SKRect _lastCanvasRect = SKRect.Empty;

    // Segment paint cache
    private readonly Dictionary<int, SKPaint> _segmentPaints = new();

    // Disposal flag
    private new bool _disposed;
    #endregion

    #region Initialization and Configuration
    /// <summary>
    /// Initializes the Kenwood renderer and prepares resources for rendering.
    /// </summary>
    public override void Initialize()
    {
        Safe(() =>
        {
            if (_isInitialized)
                return;

            base.Initialize();

            // Pre-fill buffer pools
            for (int i = 0; i < 8; i++)
            {
                _floatBufferPool.Enqueue(new float[Constants.INITIAL_BUFFER_SIZE]);
                _dateTimeBufferPool.Enqueue(new DateTime[Constants.INITIAL_BUFFER_SIZE]);
            }

            // Initialize cached SKPath objects
            _cachedBarPath = new SKPath();
            _cachedGreenSegmentsPath = new SKPath();
            _cachedYellowSegmentsPath = new SKPath();
            _cachedRedSegmentsPath = new SKPath();
            _cachedOutlinePath = new SKPath();
            _cachedPeakPath = new SKPath();
            _cachedShadowPath = new SKPath();
            _cachedReflectionPath = new SKPath();

            ApplyQualitySettings();
            StartCalculationThread();

            Log(LogLevel.Debug, Constants.LOG_PREFIX, "Initialized");
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.Initialize",
            ErrorMessage = "Failed to initialize renderer"
        });
    }

    /// <summary>
    /// Starts the background calculation thread.
    /// </summary>
    private void StartCalculationThread()
    {
        _calculationCts = new CancellationTokenSource();
        _calculationTask = Task.Factory.StartNew(() => CalculationThreadMain(_calculationCts.Token),
            _calculationCts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
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

            _smoothingFactor = isOverlayActive ? 0.6f : Constants.DEFAULT_SMOOTHING_FACTOR * 0.2f;
            _transitionSmoothness = isOverlayActive ? 0.7f : 0.5f;
            _pathsNeedRebuild = true;

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

            switch (_quality)
            {
                case RenderQuality.Low:
                    _useAntiAlias = false;
                    _samplingOptions = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);
                    _enableShadows = false;
                    _enableReflections = false;
                    _segmentRoundness = 0f; // No rounding for faster rendering
                    _useSegmentedBars = false; // Single gradient rectangle per bar
                    break;

                case RenderQuality.Medium:
                    _useAntiAlias = true;
                    _samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                    _enableShadows = true;
                    _enableReflections = false;
                    _segmentRoundness = 2f; // Moderate rounding
                    _useSegmentedBars = true; // Segmented bars
                    break;

                case RenderQuality.High:
                    _useAntiAlias = true;
                    _samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                    _enableShadows = true;
                    _enableReflections = true;
                    _segmentRoundness = Constants.SEGMENT_ROUNDNESS; // Full rounding
                    _useSegmentedBars = true; // Segmented bars with all effects
                    break;
            }

            // Update paint properties
            _peakPaint.IsAntialias = _useAntiAlias;
            _segmentShadowPaint.IsAntialias = _useAntiAlias;
            _outlinePaint.IsAntialias = _useAntiAlias;
            _reflectionPaint.IsAntialias = _useAntiAlias;

            _pathsNeedRebuild = true; // Invalidate paths when quality changes

            Log(LogLevel.Debug, Constants.LOG_PREFIX, $"Quality changed to {_quality}");
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.ApplyQualitySettings",
            ErrorMessage = "Failed to apply quality settings"
        });
    }
    #endregion

    #region Buffer Management
    /// <summary>
    /// Gets a float buffer from the pool or creates a new one if needed.
    /// </summary>
    private float[] GetFloatBuffer(int size)
    {
        if (_floatBufferPool.TryDequeue(out var buffer) && buffer.Length >= size)
            return buffer;
        return new float[Max(size, Constants.INITIAL_BUFFER_SIZE)];
    }

    /// <summary>
    /// Gets a DateTime buffer from the pool or creates a new one if needed.
    /// </summary>
    private DateTime[] GetDateTimeBuffer(int size)
    {
        if (_dateTimeBufferPool.TryDequeue(out var buffer) && buffer.Length >= size)
            return buffer;
        return new DateTime[Max(size, Constants.INITIAL_BUFFER_SIZE)];
    }

    /// <summary>
    /// Returns a float buffer to the pool if space is available.
    /// </summary>
    private void ReturnBufferToPool(float[]? buffer)
    {
        if (buffer != null && _floatBufferPool.Count < Constants.MAX_BUFFER_POOL_SIZE)
            _floatBufferPool.Enqueue(buffer);
    }

    /// <summary>
    /// Returns a DateTime buffer to the pool if space is available.
    /// </summary>
    private void ReturnBufferToPool(DateTime[]? buffer)
    {
        if (buffer != null && _dateTimeBufferPool.Count < Constants.MAX_BUFFER_POOL_SIZE)
            _dateTimeBufferPool.Enqueue(buffer);
    }
    #endregion

    #region Calculation Thread
    /// <summary>
    /// Main function for the background calculation thread.
    /// </summary>
    private void CalculationThreadMain(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_dataAvailableEvent.WaitOne(50))
                    continue;
                if (ct.IsCancellationRequested)
                    break;

                float[]? spectrumData = null;
                int barCount = 0;
                int canvasHeight = 0;

                _dataSemaphore.Wait(ct);
                try
                {
                    if (_pendingSpectrum != null)
                    {
                        spectrumData = _pendingSpectrum;
                        barCount = _pendingBarCount;
                        canvasHeight = (int)_pendingCanvasHeight;
                        _pendingSpectrum = null;
                    }
                }
                finally
                {
                    _dataSemaphore.Release();
                }

                if (spectrumData != null && spectrumData.Length > 0)
                    ProcessSpectrumData(spectrumData, barCount, canvasHeight);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, Constants.LOG_PREFIX, $"Calculation error: {ex.Message}");
                Thread.Sleep(100);
            }
        }
    }

    /// <summary>
    /// Processes spectrum data to calculate bar heights and peak positions.
    /// </summary>
    private void ProcessSpectrumData(float[] spectrum, int barCount, float canvasHeight)
    {
        int spectrumLength = spectrum.Length;
        int actualBarCount = Min(spectrumLength, barCount);

        // Initialize buffers if needed
        if (_previousSpectrum == null || _previousSpectrum.Length != actualBarCount)
        {
            ReturnBufferToPool(_previousSpectrum);
            ReturnBufferToPool(_peaks);
            ReturnBufferToPool(_peakHoldTimes);
            ReturnBufferToPool(_velocities);

            _previousSpectrum = GetFloatBuffer(actualBarCount);
            _peaks = GetFloatBuffer(actualBarCount);
            _peakHoldTimes = GetDateTimeBuffer(actualBarCount);
            _velocities = GetFloatBuffer(actualBarCount);

            Array.Fill(_peakHoldTimes, DateTime.MinValue);
            Array.Fill(_velocities, 0f);
        }

        // Scale spectrum data to bar count
        float[] scaledSpectrum = GetFloatBuffer(actualBarCount);
        ScaleSpectrum(spectrum, scaledSpectrum, actualBarCount, spectrumLength);

        // Prepare buffers for new computed values
        float[] computedBarValues = GetFloatBuffer(actualBarCount);
        float[] computedPeaks = GetFloatBuffer(actualBarCount);
        DateTime currentTime = Now;

        // Animation and smoothing parameters
        float smoothFactor = _smoothingFactor * _animationSpeed;
        float peakFallRate = _peakFallSpeed * (float)canvasHeight * _animationSpeed;
        double peakHoldTimeMs = Constants.PEAK_HOLD_TIME_MS;

        // Physics parameters for smooth transitions
        float maxChangeThreshold = canvasHeight * 0.3f;
        float velocityDamping = 0.8f * _transitionSmoothness;
        float springStiffness = 0.2f * (1 - _transitionSmoothness);

        // Process spectrum data in batches for better cache locality
        const int batchSize = 64;
        for (int batchStart = 0; batchStart < actualBarCount; batchStart += batchSize)
        {
            int batchEnd = Min(batchStart + batchSize, actualBarCount);

            for (int i = batchStart; i < batchEnd; i++)
            {
                float targetValue = scaledSpectrum[i];
                float currentValue = _previousSpectrum[i];
                float delta = targetValue - currentValue;
                float adaptiveSmoothFactor = smoothFactor;
                float absDelta = Abs(delta * canvasHeight);

                // Use adaptive smoothing for large changes
                if (absDelta > maxChangeThreshold)
                {
                    float changeRatio = Min(1.0f, absDelta / (canvasHeight * 0.7f));
                    adaptiveSmoothFactor = Max(smoothFactor * 0.3f,
                        smoothFactor * (1.0f + changeRatio * 0.5f));
                }

                // Apply spring physics for smooth transitions
                _velocities![i] = _velocities[i] * velocityDamping + delta * springStiffness;
                float newValue = currentValue + _velocities[i] + delta * adaptiveSmoothFactor;
                newValue = Max(0f, Min(1f, newValue));

                // Store results
                _previousSpectrum[i] = newValue;
                float barValue = newValue * canvasHeight;
                computedBarValues[i] = barValue;

                // Process peak values
                if (_peaks != null && _peakHoldTimes != null)
                {
                    float currentPeak = _peaks[i];
                    if (barValue > currentPeak)
                    {
                        // New peak found
                        _peaks[i] = barValue;
                        _peakHoldTimes[i] = currentTime;
                    }
                    else if ((currentTime - _peakHoldTimes[i]).TotalMilliseconds > peakHoldTimeMs)
                    {
                        // Peak hold time expired, start falling
                        float peakDelta = currentPeak - barValue;
                        float adaptiveFallRate = peakFallRate;

                        // Faster fall for peaks far from bar
                        if (peakDelta > maxChangeThreshold)
                            adaptiveFallRate = peakFallRate * (1.0f + peakDelta / canvasHeight * 2.0f);

                        _peaks[i] = Max(0f, currentPeak - adaptiveFallRate);
                    }
                    computedPeaks[i] = _peaks[i];
                }
            }
        }

        // Update processing and rendering buffers
        _dataSemaphore.Wait();
        try
        {
            if (_processingBarValues == null || _processingBarValues.Length != actualBarCount)
            {
                ReturnBufferToPool(_processingBarValues);
                ReturnBufferToPool(_processingPeaks);

                _processingBarValues = GetFloatBuffer(actualBarCount);
                _processingPeaks = GetFloatBuffer(actualBarCount);
            }

            Buffer.BlockCopy(computedBarValues, 0, _processingBarValues!, 0, actualBarCount * sizeof(float));
            Buffer.BlockCopy(computedPeaks, 0, _processingPeaks!, 0, actualBarCount * sizeof(float));
            _currentBarCount = actualBarCount;

            if (_renderBarValues == null || _renderBarValues.Length != actualBarCount)
            {
                ReturnBufferToPool(_renderBarValues);
                ReturnBufferToPool(_renderPeaks);

                _renderBarValues = GetFloatBuffer(actualBarCount);
                _renderPeaks = GetFloatBuffer(actualBarCount);
                Buffer.BlockCopy(_processingBarValues!, 0, _renderBarValues!, 0, actualBarCount * sizeof(float));
                Buffer.BlockCopy(_processingPeaks!, 0, _renderPeaks!, 0, actualBarCount * sizeof(float));
            }
        }
        finally
        {
            _dataSemaphore.Release();
        }

        // Return temporary buffers to pool
        ReturnBufferToPool(scaledSpectrum);
        ReturnBufferToPool(computedBarValues);
        ReturnBufferToPool(computedPeaks);
        _pathsNeedRebuild = true;
    }
    #endregion

    #region Rendering
    /// <summary>
    /// Renders the Kenwood-style visualization on the canvas using spectrum data.
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
            // Calculate bar dimensions
            float totalWidth = info.Width;
            float totalBarWidth = totalWidth / barCount;
            barWidth = totalBarWidth * 0.7f;
            barSpacing = totalBarWidth * 0.3f;

            // Check if canvas size or transform changed
            bool canvasSizeChanged = Abs(_lastCanvasRect.Height - info.Height) > 0.5f ||
                                    Abs(_lastCanvasRect.Width - info.Width) > 0.5f;

            bool transformChanged = canvas.TotalMatrix != _lastTransform;
            if (transformChanged)
                _lastTransform = canvas.TotalMatrix;

            // Update segment count and gradients if canvas size changed
            if (canvasSizeChanged)
            {
                _currentSegmentCount = Max(1, (int)(info.Height / (_desiredSegmentHeight + Constants.SEGMENT_GAP)));
                CreateGradients(info.Height);
                _lastCanvasRect = info.Rect;
                _pathsNeedRebuild = true;
            }

            // Submit spectrum data for processing
            _dataSemaphore.Wait();
            try
            {
                _pendingSpectrum = spectrum;
                _pendingBarCount = barCount;
                _pendingCanvasHeight = info.Height;

                // Swap processing and rendering buffers
                if (_processingBarValues != null && _renderBarValues != null &&
                    _processingPeaks != null && _renderPeaks != null &&
                    _processingBarValues.Length == _renderBarValues.Length)
                {
                    var tempBarValues = _renderBarValues;
                    _renderBarValues = _processingBarValues;
                    _processingBarValues = tempBarValues;

                    var tempPeaks = _renderPeaks;
                    _renderPeaks = _processingPeaks;
                    _processingPeaks = tempPeaks;
                }
            }
            finally
            {
                _dataSemaphore.Release();
                _dataAvailableEvent.Set();
            }

            // Skip rendering if no data available
            if (_renderBarValues == null || _renderPeaks == null || _currentBarCount == 0)
                return;

            int renderCount = Min(_currentBarCount, _renderBarValues.Length);
            if (renderCount <= 0)
                return;

            // Calculate segment dimensions
            float totalGap = (_currentSegmentCount - 1) * Constants.SEGMENT_GAP;
            float segmentHeight = (info.Height - totalGap) / _currentSegmentCount * _scaleFactor;

            // Check if paths need to be rebuilt
            bool dimensionsChanged = Abs(_lastTotalBarWidth - totalBarWidth) > 0.01f ||
                                    Abs(_lastBarWidth - barWidth) > 0.01f ||
                                    Abs(_lastBarSpacing - barSpacing) > 0.01f ||
                                    Abs(_lastSegmentHeight - segmentHeight) > 0.01f ||
                                    _lastRenderCount != renderCount;

            if (_pathsNeedRebuild || dimensionsChanged || transformChanged || canvasSizeChanged)
            {
                RebuildPaths(info, barWidth, barSpacing, totalBarWidth, segmentHeight, renderCount);
                _lastTotalBarWidth = totalBarWidth;
                _lastBarWidth = barWidth;
                _lastBarSpacing = barSpacing;
                _lastSegmentHeight = segmentHeight;
                _lastRenderCount = renderCount;
                _pathsNeedRebuild = false;
            }

            // Render segmented bars
            if (_useSegmentedBars)
            {
                if (_enableShadows && _cachedShadowPath != null)
                    canvas.DrawPath(_cachedShadowPath, _segmentShadowPaint);

                if (_cachedGreenSegmentsPath != null)
                    canvas.DrawPath(_cachedGreenSegmentsPath, EnsureSegmentPaint(0, _greenGradient));
                if (_cachedYellowSegmentsPath != null)
                    canvas.DrawPath(_cachedYellowSegmentsPath, EnsureSegmentPaint(1, _yellowGradient));
                if (_cachedRedSegmentsPath != null)
                    canvas.DrawPath(_cachedRedSegmentsPath, EnsureSegmentPaint(2, _redGradient));
                if (_cachedOutlinePath != null)
                    canvas.DrawPath(_cachedOutlinePath, _outlinePaint);
            }
            else
            {
                // Render simple bars with gradient
                if (_cachedBarPath != null)
                {
                    using var barPaint = new SKPaint
                    {
                        IsAntialias = _useAntiAlias,
                        Style = Fill,
                        Shader = _barGradient
                    };
                    canvas.DrawPath(_cachedBarPath, barPaint);
                }
            }

            // Render peak indicators
            if (_cachedPeakPath != null)
                canvas.DrawPath(_cachedPeakPath, _peakPaint);

            // Render reflection effect
            if (_enableReflections && info.Height > 200 && _cachedReflectionPath != null)
            {
                canvas.Save();
                canvas.Scale(1, -1);
                canvas.Translate(0, -info.Height * 2);

                if (_reflectionPaint.Color.Alpha == 0)
                    _reflectionPaint.Color = new SKColor(255, 255, 255, (byte)(_reflectionOpacity * 255));
                if (_reflectionGradient != null)
                    _reflectionPaint.Shader = _reflectionGradient;

                canvas.DrawPath(_cachedReflectionPath, _reflectionPaint);
                canvas.Restore();
            }
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.Render",
            ErrorMessage = "Error during rendering"
        });

        // Draw performance info
        drawPerformanceInfo?.Invoke(canvas!, info);
    }

    /// <summary>
    /// Validates rendering parameters.
    /// </summary>
    private bool ValidateRenderParameters(SKCanvas? canvas, float[]? spectrum, SKImageInfo info, SKPaint? paint)
    {
        return canvas != null &&
               spectrum != null &&
               spectrum.Length >= 2 &&
               paint != null &&
               info.Width > 0 &&
               info.Height > 0;
    }

    /// <summary>
    /// Creates gradient shaders for different bar sections.
    /// </summary>
    private void CreateGradients(float height)
    {
        // Dispose existing gradients
        _greenGradient?.Dispose();
        _yellowGradient?.Dispose();
        _redGradient?.Dispose();
        _reflectionGradient?.Dispose();
        _barGradient?.Dispose();

        // Create green gradient (bottom section)
        _greenGradient = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(0, height / 3),
            new[] { _greenStartColor, _greenEndColor },
            null,
            SKShaderTileMode.Clamp);

        // Create yellow gradient (middle section)
        _yellowGradient = SKShader.CreateLinearGradient(
            new SKPoint(0, height / 3),
            new SKPoint(0, height * 2 / 3),
            new[] { _yellowStartColor, _yellowEndColor },
            null,
            SKShaderTileMode.Clamp);

        // Create red gradient (top section)
        _redGradient = SKShader.CreateLinearGradient(
            new SKPoint(0, height * 2 / 3),
            new SKPoint(0, height),
            new[] { _redStartColor, _redEndColor },
            null,
            SKShaderTileMode.Clamp);

        // Create reflection gradient
        _reflectionGradient = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(0, height * _reflectionHeight),
            new[] { new SKColor(255, 255, 255, (byte)(_reflectionOpacity * 255)), SKColors.Transparent },
            null,
            SKShaderTileMode.Clamp);

        // Create combined gradient for non-segmented bars
        _barGradient = SKShader.CreateLinearGradient(
            new SKPoint(0, height), // Bottom
            new SKPoint(0, 0),      // Top
            new[] { _greenStartColor, _greenEndColor, _yellowStartColor, _yellowEndColor, _redStartColor, _redEndColor },
            new float[] { 0f, 0.6f, 0.6f, 0.85f, 0.85f, 1f },
            SKShaderTileMode.Clamp);
    }

    /// <summary>
    /// Gets or creates a paint object for a segment type.
    /// </summary>
    private SKPaint EnsureSegmentPaint(int index, SKShader? shader)
    {
        if (!_segmentPaints.TryGetValue(index, out var paint) || paint == null)
        {
            paint = new SKPaint
            {
                IsAntialias = _useAntiAlias,
                Style = Fill,
                Shader = shader
            };
            _segmentPaints[index] = paint;
        }
        return paint;
    }

    /// <summary>
    /// Rebuilds all rendering paths based on current spectrum data.
    /// </summary>
    private void RebuildPaths(SKImageInfo info, float barWidth, float barSpacing, float totalBarWidth, float segmentHeight, int renderCount)
    {
        // Reset all paths
        _cachedBarPath?.Reset();
        _cachedGreenSegmentsPath?.Reset();
        _cachedYellowSegmentsPath?.Reset();
        _cachedRedSegmentsPath?.Reset();
        _cachedOutlinePath?.Reset();
        _cachedPeakPath?.Reset();
        _cachedShadowPath?.Reset();
        _cachedReflectionPath?.Reset();

        float minVisibleValue = segmentHeight * 0.5f;

        if (!_useSegmentedBars)
        {
            // Simple bar mode - just draw rectangles
            for (int i = 0; i < renderCount; i++)
            {
                float x = i * totalBarWidth + barSpacing / 2;
                float barValue = _renderBarValues![i];

                if (barValue > minVisibleValue)
                {
                    float barTop = info.Height - barValue;
                    _cachedBarPath!.AddRect(SKRect.Create(x, barTop, barWidth, barValue));
                }

                if (_renderPeaks![i] > minVisibleValue)
                {
                    float peakY = info.Height - _renderPeaks[i];
                    float peakHeight = 3f;
                    _cachedPeakPath!.AddRect(SKRect.Create(x, peakY - peakHeight, barWidth, peakHeight));
                }
            }
        }
        else
        {
            // Segmented bar mode - draw segments with color zones
            int greenCount = (int)(_currentSegmentCount * 0.6);
            int yellowCount = (int)(_currentSegmentCount * 0.25);

            // Build reflection path if enabled
            if (_enableReflections && info.Height > 200)
                BuildReflectionPath(info, barWidth, barSpacing, totalBarWidth, segmentHeight, renderCount, minVisibleValue);

            // Build segments for each bar
            for (int i = 0; i < renderCount; i++)
            {
                float x = i * totalBarWidth + barSpacing / 2;
                float barValue = _renderBarValues![i];

                if (barValue > minVisibleValue)
                {
                    int segmentsToRender = Min(
                        (int)Ceiling(barValue / (segmentHeight + Constants.SEGMENT_GAP)),
                        _currentSegmentCount);

                    bool needShadow = _enableShadows && barValue > info.Height * 0.5f;

                    // Add shadow segments
                    if (needShadow)
                    {
                        for (int segIndex = 0; segIndex < segmentsToRender; segIndex++)
                        {
                            float segmentBottom = info.Height - segIndex * (segmentHeight + Constants.SEGMENT_GAP);
                            float segmentTop = segmentBottom - segmentHeight;
                            AddRoundRectToPath(_cachedShadowPath!, x + 2, segmentTop + 2, barWidth, segmentHeight);
                        }
                    }

                    // Add color segments
                    for (int segIndex = 0; segIndex < segmentsToRender; segIndex++)
                    {
                        float segmentBottom = info.Height - segIndex * (segmentHeight + Constants.SEGMENT_GAP);
                        float segmentTop = segmentBottom - segmentHeight;

                        // Select target path based on segment position (green, yellow, or red zone)
                        SKPath targetPath = segIndex < greenCount ? _cachedGreenSegmentsPath!
                            : segIndex < greenCount + yellowCount ? _cachedYellowSegmentsPath! : _cachedRedSegmentsPath!;

                        AddRoundRectToPath(targetPath, x, segmentTop, barWidth, segmentHeight);
                        AddRoundRectToPath(_cachedOutlinePath!, x, segmentTop, barWidth, segmentHeight);
                    }
                }

                // Add peak indicators
                if (_renderPeaks![i] > minVisibleValue)
                {
                    float peakY = info.Height - _renderPeaks[i];
                    float peakHeight = 3f;
                    _cachedPeakPath!.AddRect(SKRect.Create(x, peakY - peakHeight, barWidth, peakHeight));
                }
            }
        }
    }

    /// <summary>
    /// Builds the reflection path for all bars.
    /// </summary>
    private void BuildReflectionPath(SKImageInfo info, float barWidth, float barSpacing, float totalBarWidth, float segmentHeight, int renderCount, float minVisibleValue)
    {
        // Use fewer bars for reflection to improve performance
        int reflectionBarStep = Max(1, renderCount / 60);

        for (int i = 0; i < renderCount; i += reflectionBarStep)
        {
            float x = i * totalBarWidth + barSpacing / 2;
            float barValue = _renderBarValues![i];

            if (barValue > minVisibleValue)
            {
                // Limit reflection to first few segments
                int maxReflectionSegments = Min(3, _currentSegmentCount);
                int segmentsToRender = Min(
                    (int)Ceiling(barValue / (segmentHeight + Constants.SEGMENT_GAP)),
                    maxReflectionSegments);

                // Add reflection segments
                for (int segIndex = 0; segIndex < segmentsToRender; segIndex++)
                {
                    float segmentBottom = info.Height - segIndex * (segmentHeight + Constants.SEGMENT_GAP);
                    float segmentTop = segmentBottom - segmentHeight;
                    var rect = SKRect.Create(x, segmentTop, barWidth, segmentHeight * _reflectionHeight);

                    if (_segmentRoundness > 0)
                        _cachedReflectionPath!.AddRoundRect(rect, _segmentRoundness, _segmentRoundness);
                    else
                        _cachedReflectionPath!.AddRect(rect);
                }
            }

            // Add peak reflections
            if (_renderPeaks![i] > minVisibleValue)
            {
                float peakY = info.Height - _renderPeaks[i];
                float peakHeight = 3f;
                _cachedReflectionPath!.AddRect(
                    SKRect.Create(x, peakY - peakHeight, barWidth, peakHeight * _reflectionHeight));
            }
        }
    }

    /// <summary>
    /// Adds a rounded rectangle to the specified path.
    /// </summary>
    private void AddRoundRectToPath(SKPath path, float x, float y, float width, float height)
    {
        var rect = SKRect.Create(x, y, width, height);
        if (_segmentRoundness > 0.5f)
            path.AddRoundRect(rect, _segmentRoundness, _segmentRoundness);
        else
            path.AddRect(rect);
    }
    #endregion

    #region Spectrum Processing
    /// <summary>
    /// Scales spectrum data to match the target bar count.
    /// </summary>
    private void ScaleSpectrum(float[] spectrum, float[] scaledSpectrum, int barCount, int spectrumLength)
    {
        float blockSize = spectrumLength / (float)barCount;
        int maxThreads = Max(2, ProcessorCount / 2);

        Parallel.For(0, barCount, new ParallelOptions { MaxDegreeOfParallelism = maxThreads }, i =>
        {
            int start = (int)(i * blockSize);
            int end = Min((int)((i + 1) * blockSize), spectrumLength);
            int count = end - start;

            if (count <= 0)
            {
                scaledSpectrum[i] = 0f;
                return;
            }

            float sum = 0f;
            float peak = float.MinValue;
            int vectorSize = Vector<float>.Count;
            int j = start;

            // Use SIMD for faster processing
            Vector<float> sumVector = Vector<float>.Zero;
            Vector<float> maxVector = new Vector<float>(float.MinValue);

            for (; j <= end - vectorSize; j += vectorSize)
            {
                var vec = new Vector<float>(spectrum, j);
                sumVector += vec;
                maxVector = Max(maxVector, vec);
            }

            // Extract values from vectors
            for (int k = 0; k < vectorSize; k++)
            {
                sum += sumVector[k];
                peak = Max(peak, maxVector[k]);
            }

            // Process remaining elements
            for (; j < end; j++)
            {
                float value = spectrum[j];
                sum += value;
                peak = Max(peak, value);
            }

            // Calculate weighted average between mean and peak
            float average = sum / count;
            float weight = Constants.SPECTRUM_WEIGHT;
            scaledSpectrum[i] = average * (1.0f - weight) + peak * weight;

            // Apply frequency boost to higher frequencies
            if (i > barCount / 2)
            {
                float boost = 1.0f + (float)i / barCount * Constants.BOOST_FACTOR;
                scaledSpectrum[i] *= boost;
            }

            // Clamp final value
            scaledSpectrum[i] = Min(1.0f, scaledSpectrum[i]);
        });
    }
    #endregion

    #region Disposal
    /// <summary>
    /// Disposes of resources used by the renderer.
    /// </summary>
    public override void Dispose()
    {
        if (_disposed)
            return;

        Safe(() =>
        {
            // Stop calculation thread
            _calculationCts?.Cancel();
            _dataAvailableEvent.Set();

            try
            {
                _calculationTask?.Wait(500);
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, Constants.LOG_PREFIX, $"Dispose wait error: {ex.Message}");
            }

            // Dispose synchronization objects
            _calculationCts?.Dispose();
            _dataAvailableEvent.Dispose();
            _dataSemaphore.Dispose();

            // Return buffers to pool
            ReturnBufferToPool(_previousSpectrum);
            ReturnBufferToPool(_peaks);
            ReturnBufferToPool(_peakHoldTimes);
            ReturnBufferToPool(_renderBarValues);
            ReturnBufferToPool(_renderPeaks);
            ReturnBufferToPool(_processingBarValues);
            ReturnBufferToPool(_processingPeaks);
            ReturnBufferToPool(_velocities);

            // Clear buffer pools
            while (_floatBufferPool.TryDequeue(out _)) { }
            while (_dateTimeBufferPool.TryDequeue(out _)) { }

            // Dispose paints
            foreach (var paint in _segmentPaints.Values)
                paint.Dispose();
            _segmentPaints.Clear();

            // Dispose paths
            _cachedBarPath?.Dispose();
            _cachedGreenSegmentsPath?.Dispose();
            _cachedYellowSegmentsPath?.Dispose();
            _cachedRedSegmentsPath?.Dispose();
            _cachedOutlinePath?.Dispose();
            _cachedPeakPath?.Dispose();
            _cachedShadowPath?.Dispose();
            _cachedReflectionPath?.Dispose();

            // Dispose shaders
            _greenGradient?.Dispose();
            _yellowGradient?.Dispose();
            _redGradient?.Dispose();
            _reflectionGradient?.Dispose();
            _barGradient?.Dispose();

            // Dispose paint objects
            _peakPaint.Dispose();
            _segmentShadowPaint.Dispose();
            _outlinePaint.Dispose();
            _reflectionPaint.Dispose();

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
    #endregion
}