#nullable enable

namespace SpectrumNet
{
    public class DotsRenderer : ISpectrumRenderer, IDisposable
    {
        #region Singleton and Fields

        private static DotsRenderer? _instance;
        private bool _isInitialized;
        private volatile bool _disposed;
        private readonly object _lockObject = new();

        private RenderQuality _quality = RenderQuality.Medium;
        private bool _useAntiAlias = true;
        private SKFilterQuality _filterQuality = SKFilterQuality.Medium;
        private bool _useAdvancedEffects = true;
        private bool _useHardwareAcceleration = true;
        private bool _useVectorization = true;

        // Cached objects
        private SKPaint? _dotPaint;
        private SKPicture? _cachedBackground;
        private float[]? _previousSpectrum;
        private Task? _backgroundCalculationTask;

        private const string LogPrefix = "[DotsRenderer] ";

        #endregion

        #region Constants

        private static class Constants
        {
            // Rendering thresholds and limits
            public const float MIN_INTENSITY_THRESHOLD = 0.01f;  // Minimum intensity to render a dot
            public const float MIN_DOT_RADIUS = 2.0f;           // Minimum dot radius in pixels
            public const float MAX_DOT_MULTIPLIER = 0.5f;       // Maximum multiplier for dot size

            // Alpha and binning parameters
            public const float ALPHA_MULTIPLIER = 255.0f;       // Multiplier for alpha calculation
            public const int ALPHA_BINS = 16;                   // Number of alpha bins for batching

            // Smoothing parameters
            public const float SMOOTHING_FACTOR_NORMAL = 0.3f;  // Smoothing for normal mode
            public const float SMOOTHING_FACTOR_OVERLAY = 0.5f; // Smoothing for overlay mode

            // Dot size parameters
            public const float NORMAL_DOT_MULTIPLIER = 1.0f;    // Normal dot size multiplier
            public const float OVERLAY_DOT_MULTIPLIER = 1.5f;   // Overlay dot size multiplier

            // Performance settings
            public const int VECTOR_SIZE = 4;                   // Size for SIMD vectorization
            public const int MIN_BATCH_SIZE = 32;               // Minimum batch size for parallel processing
        }

        #endregion

        #region Constructor and Initialization

        private DotsRenderer() { }

        public static DotsRenderer GetInstance() => _instance ??= new DotsRenderer();

        public void Initialize()
        {
            if (!_isInitialized)
            {
                lock (_lockObject)
                {
                    if (!_isInitialized)
                    {
                        _isInitialized = true;
                        _previousSpectrum = null;
                        InitializePaints();
                        SmartLogger.Log(LogLevel.Debug, LogPrefix, "Initialized");
                    }
                }
            }
        }

        private void InitializePaints()
        {
            _dotPaint = new SKPaint
            {
                IsAntialias = _useAntiAlias,
                FilterQuality = _filterQuality,
                Style = SKPaintStyle.Fill
            };
        }

        #endregion

        #region Configuration

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

        public void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium)
        {
            _dotRadiusMultiplier = isOverlayActive ?
                Constants.OVERLAY_DOT_MULTIPLIER :
                Constants.NORMAL_DOT_MULTIPLIER;

            _smoothingFactor = isOverlayActive ?
                Constants.SMOOTHING_FACTOR_OVERLAY :
                Constants.SMOOTHING_FACTOR_NORMAL;

            Quality = quality;
        }

        private void ApplyQualitySettings()
        {
            switch (_quality)
            {
                case RenderQuality.Low:
                    _useAntiAlias = false;
                    _filterQuality = SKFilterQuality.Low;
                    _useAdvancedEffects = false;
                    _useHardwareAcceleration = true;
                    _useVectorization = false;
                    _batchProcessing = false;
                    break;

                case RenderQuality.Medium:
                    _useAntiAlias = true;
                    _filterQuality = SKFilterQuality.Medium;
                    _useAdvancedEffects = true;
                    _useHardwareAcceleration = true;
                    _useVectorization = true;
                    _batchProcessing = true;
                    break;

                case RenderQuality.High:
                    _useAntiAlias = true;
                    _filterQuality = SKFilterQuality.High;
                    _useAdvancedEffects = true;
                    _useHardwareAcceleration = true;
                    _useVectorization = true;
                    _batchProcessing = true;
                    break;
            }

            if (_dotPaint != null)
            {
                _dotPaint.IsAntialias = _useAntiAlias;
                _dotPaint.FilterQuality = _filterQuality;
            }

            // Invalidate cached objects that depend on quality
            _cachedBackground = null;
        }

        #endregion

        #region Rendering State

        private float _dotRadiusMultiplier = Constants.NORMAL_DOT_MULTIPLIER;
        private float _smoothingFactor = Constants.SMOOTHING_FACTOR_NORMAL;
        private bool _batchProcessing = true;

        private struct CircleData
        {
            public float X;
            public float Y;
            public float Radius;
            public float Intensity;
        }

        #endregion

        #region Rendering Methods

        public void Render(
            SKCanvas? canvas,
            float[]? spectrum,
            SKImageInfo info,
            float barWidth,
            float barSpacing,
            int barCount,
            SKPaint? basePaint,
            Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
        {
            if (!ValidateRenderParameters(canvas, spectrum, basePaint, info))
            {
                return;
            }

            UpdatePaint(basePaint);

            int spectrumLength = spectrum!.Length;
            int actualBarCount = Math.Min(spectrumLength, barCount);
            float canvasHeight = info.Height;
            float calculatedBarWidth = barWidth * Constants.MAX_DOT_MULTIPLIER * _dotRadiusMultiplier;
            float totalWidth = barWidth + barSpacing;

            // Scale and smooth spectrum data
            float[] processedSpectrum = ProcessSpectrum(spectrum, actualBarCount, spectrumLength);

            // Calculate circle data 
            List<CircleData> circles = CalculateCircleData(
                processedSpectrum,
                calculatedBarWidth,
                totalWidth,
                canvasHeight);

            // Group circles by alpha for efficient rendering
            List<List<CircleData>> circleBins = GroupCirclesByAlphaBin(circles);

            // Draw all circles with minimal state changes
            DrawCircles(canvas!, circleBins);

            // Draw performance info if requested
            drawPerformanceInfo?.Invoke(canvas!, info);
        }

        private bool ValidateRenderParameters(SKCanvas? canvas, float[]? spectrum, SKPaint? basePaint, SKImageInfo info)
        {
            if (!_isInitialized || canvas == null || spectrum == null || spectrum.Length < 2 ||
                basePaint == null || info.Width <= 0 || info.Height <= 0)
            {
                SmartLogger.Log(LogLevel.Warning, LogPrefix, "Invalid render parameters or renderer not initialized");
                return false;
            }
            return true;
        }

        private void UpdatePaint(SKPaint basePaint)
        {
            if (_dotPaint == null)
            {
                InitializePaints();
            }

            _dotPaint!.Color = basePaint.Color;
            _dotPaint.IsAntialias = _useAntiAlias;
            _dotPaint.FilterQuality = _filterQuality;
        }

        private float[] ProcessSpectrum(float[] spectrum, int targetCount, int spectrumLength)
        {
            float[] scaledSpectrum = ScaleSpectrum(spectrum, targetCount, spectrumLength);
            return SmoothSpectrum(scaledSpectrum, targetCount);
        }

        #endregion

        #region Circle Calculation and Drawing

        private List<CircleData> CalculateCircleData(
            float[] smoothedSpectrum,
            float multiplier,
            float totalWidth,
            float canvasHeight)
        {
            var circles = new List<CircleData>(smoothedSpectrum.Length);

            if (_useVectorization && Vector.IsHardwareAccelerated && smoothedSpectrum.Length >= 4)
            {
                return CalculateCircleDataOptimized(smoothedSpectrum, multiplier, totalWidth, canvasHeight);
            }

            for (int i = 0; i < smoothedSpectrum.Length; i++)
            {
                float intensity = smoothedSpectrum[i];
                if (intensity < Constants.MIN_INTENSITY_THRESHOLD) continue;

                float dotRadius = multiplier * intensity;
                if (dotRadius < Constants.MIN_DOT_RADIUS)
                    dotRadius = Constants.MIN_DOT_RADIUS;

                float x = i * totalWidth + dotRadius;
                float y = canvasHeight - (intensity * canvasHeight);

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

        private List<CircleData> CalculateCircleDataOptimized(
            float[] smoothedSpectrum,
            float multiplier,
            float totalWidth,
            float canvasHeight)
        {
            var circles = new List<CircleData>(smoothedSpectrum.Length);

            // Process in chunks for better cache locality
            const int chunkSize = 16; // Process 16 elements at a time
            int chunks = smoothedSpectrum.Length / chunkSize;

            for (int chunk = 0; chunk < chunks; chunk++)
            {
                int startIdx = chunk * chunkSize;
                int endIdx = startIdx + chunkSize;

                for (int i = startIdx; i < endIdx; i++)
                {
                    float intensity = smoothedSpectrum[i];
                    if (intensity < Constants.MIN_INTENSITY_THRESHOLD) continue;

                    float dotRadius = Math.Max(multiplier * intensity, Constants.MIN_DOT_RADIUS);
                    float x = i * totalWidth + dotRadius;
                    float y = canvasHeight - (intensity * canvasHeight);

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

                float dotRadius = Math.Max(multiplier * intensity, Constants.MIN_DOT_RADIUS);
                float x = i * totalWidth + dotRadius;
                float y = canvasHeight - (intensity * canvasHeight);

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

        private List<List<CircleData>> GroupCirclesByAlphaBin(List<CircleData> circles)
        {
            // Pre-allocate all bins
            List<List<CircleData>> circleBins = new List<List<CircleData>>(Constants.ALPHA_BINS);
            for (int i = 0; i < Constants.ALPHA_BINS; i++)
            {
                circleBins.Add(new List<CircleData>(circles.Count / Constants.ALPHA_BINS + 1));
            }

            // Calculate bin step once
            float binStep = 255f / (Constants.ALPHA_BINS - 1);

            foreach (var circle in circles)
            {
                byte alpha = (byte)Math.Min(circle.Intensity * Constants.ALPHA_MULTIPLIER, 255);
                int binIndex = Math.Min((int)(alpha / binStep), Constants.ALPHA_BINS - 1);
                circleBins[binIndex].Add(circle);
            }

            return circleBins;
        }

        private void DrawCircles(SKCanvas canvas, List<List<CircleData>> circleBins)
        {
            // Use quick reject to avoid drawing outside the canvas bounds
            SKRect canvasBounds = new SKRect(0, 0, canvas.DeviceClipBounds.Width, canvas.DeviceClipBounds.Height);

            float binStep = 255f / (Constants.ALPHA_BINS - 1);

            for (int binIndex = 0; binIndex < Constants.ALPHA_BINS; binIndex++)
            {
                var bin = circleBins[binIndex];
                if (bin.Count == 0)
                    continue;

                byte binAlpha = (byte)(binIndex * binStep);
                _dotPaint!.Color = _dotPaint.Color.WithAlpha(binAlpha);

                // For small numbers of circles, draw individually
                if (bin.Count <= 5)
                {
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
                    // For larger numbers, batch draw with a path
                    using var path = new SKPath();

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
                            path.AddCircle(circle.X, circle.Y, circle.Radius);
                        }
                    }

                    canvas.DrawPath(path, _dotPaint);
                }
            }
        }

        #endregion

        #region Spectrum Processing

        private static float[] ScaleSpectrum(float[] spectrum, int targetCount, int spectrumLength)
        {
            float[] scaledSpectrum = new float[targetCount];
            float blockSize = (float)spectrumLength / targetCount;

            // Process in chunks for better cache locality
            const int chunkSize = 16; // Process 16 elements at a time
            int chunks = targetCount / chunkSize;

            // Process in chunks
            for (int chunk = 0; chunk < chunks; chunk++)
            {
                int startIdx = chunk * chunkSize;
                int endIdx = startIdx + chunkSize;

                for (int i = startIdx; i < endIdx; i++)
                {
                    float startFloat = i * blockSize;
                    float endFloat = (i + 1) * blockSize;

                    int start = (int)Math.Floor(startFloat);
                    int end = Math.Min((int)Math.Ceiling(endFloat), spectrumLength);

                    if (start >= end)
                    {
                        scaledSpectrum[i] = 0f;
                    }
                    else
                    {
                        float sum = 0f;
                        for (int j = start; j < end; j++)
                        {
                            sum += spectrum[j];
                        }
                        scaledSpectrum[i] = sum / (end - start);
                    }
                }
            }

            // Process remaining elements
            for (int i = chunks * chunkSize; i < targetCount; i++)
            {
                float startFloat = i * blockSize;
                float endFloat = (i + 1) * blockSize;

                int start = (int)Math.Floor(startFloat);
                int end = Math.Min((int)Math.Ceiling(endFloat), spectrumLength);

                if (start >= end)
                {
                    scaledSpectrum[i] = 0f;
                }
                else
                {
                    float sum = 0f;
                    for (int j = start; j < end; j++)
                    {
                        sum += spectrum[j];
                    }
                    scaledSpectrum[i] = sum / (end - start);
                }
            }

            return scaledSpectrum;
        }

        private float[] SmoothSpectrum(float[] spectrum, int targetCount)
        {
            if (_previousSpectrum == null || _previousSpectrum.Length != targetCount)
            {
                _previousSpectrum = new float[targetCount];
                Array.Copy(spectrum, _previousSpectrum, targetCount);
                return spectrum;
            }

            var smoothedSpectrum = new float[targetCount];

            if (_useVectorization && Vector.IsHardwareAccelerated && targetCount >= 4)
            {
                // Process in chunks for better cache locality
                const int chunkSize = 16; // Process 16 elements at a time
                int chunks = targetCount / chunkSize;

                for (int chunk = 0; chunk < chunks; chunk++)
                {
                    int startIdx = chunk * chunkSize;
                    int endIdx = startIdx + chunkSize;

                    for (int i = startIdx; i < endIdx; i++)
                    {
                        float currentValue = spectrum[i];
                        float previousValue = _previousSpectrum[i];
                        float smoothedValue = previousValue + (currentValue - previousValue) * _smoothingFactor;

                        smoothedSpectrum[i] = smoothedValue;
                        _previousSpectrum[i] = smoothedValue;
                    }
                }

                // Process remaining elements
                for (int i = chunks * chunkSize; i < targetCount; i++)
                {
                    float currentValue = spectrum[i];
                    float previousValue = _previousSpectrum[i];
                    float smoothedValue = previousValue + (currentValue - previousValue) * _smoothingFactor;

                    smoothedSpectrum[i] = smoothedValue;
                    _previousSpectrum[i] = smoothedValue;
                }
            }
            else
            {
                // Original implementation
                for (int i = 0; i < targetCount; i++)
                {
                    float currentValue = spectrum[i];
                    float previousValue = _previousSpectrum[i];
                    float smoothedValue = previousValue + (currentValue - previousValue) * _smoothingFactor;

                    smoothedSpectrum[i] = smoothedValue;
                    _previousSpectrum[i] = smoothedValue;
                }
            }

            return smoothedSpectrum;
        }

        #endregion

        #region Background Processing

        private void ProcessBackgroundTasks()
        {
            if (_backgroundCalculationTask != null && !_backgroundCalculationTask.IsCompleted)
            {
                return; // Already processing
            }

            // Example of offloading work to background thread
            _backgroundCalculationTask = Task.Run(() => {
                // Pre-calculate any resource-intensive operations here
                // For example, generate lookup tables, prepare gradients, etc.
            });
        }

        #endregion

        #region GPU Acceleration

        private void ConfigureHardwareAcceleration(SKCanvas canvas)
        {
            if (_useHardwareAcceleration && _useAdvancedEffects)
            {
                // Apply hardware-optimized settings when available
                // This is a placeholder for specific SkiaSharp GPU optimizations

                // Example: Use high-quality filtering only when hardware acceleration is available
                if (canvas.TotalMatrix.ScaleX > 1.5f || canvas.TotalMatrix.ScaleY > 1.5f)
                {
                    _dotPaint!.FilterQuality = SKFilterQuality.High;
                }

                // Example: Use hardware-optimized effects
                if (_useAdvancedEffects)
                {
                    // Configure any hardware-accelerated effects here
                }
            }
        }

        #endregion

        #region Disposal

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _dotPaint?.Dispose();
                    _dotPaint = null;

                    _cachedBackground?.Dispose();
                    _cachedBackground = null;

                    _previousSpectrum = null;

                    // Wait for any background tasks to complete
                    _backgroundCalculationTask?.Wait();
                }

                _disposed = true;
                SmartLogger.Log(LogLevel.Debug, LogPrefix, "Disposed");
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}