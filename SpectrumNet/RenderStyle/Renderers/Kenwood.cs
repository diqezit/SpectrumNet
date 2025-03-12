#nullable enable

namespace SpectrumNet
{

    public sealed class KenwoodRenderer : ISpectrumRenderer, IDisposable
    {
        #region Constants
        private static class KenwoodConstants
        {
            // Quality settings
            public const RenderQuality DefaultQuality = RenderQuality.Medium; // Default rendering quality level

            // Animation and smoothing factors
            public const float AnimationSpeed = 0.85f;      // Speed of animation transitions
            public const float ReflectionOpacity = 0.4f;    // Opacity of reflection effect
            public const float ReflectionHeight = 0.6f;     // Height factor for reflection effect
            public const float PeakFallSpeed = 0.007f;      // Speed at which peaks fall
            public const float ScaleFactor = 0.9f;          // Scaling factor for segment sizes
            public const float DesiredSegmentHeight = 10f;  // Desired height of each segment
            public const float SegmentRoundness = 4f; // Roundness factor for segments

            // Smoothing and transition defaults
            public const float DefaultSmoothingFactor = 1.5f;   // Default factor for smoothing spectrum data
            public const float DefaultTransitionSmoothness = 1f; // Default smoothness for transitions

            // Buffer and pool settings
            public const int MaxBufferPoolSize = 16;     // Maximum size of buffer pools
            public const int InitialBufferSize = 1024;   // Initial size for float and DateTime arrays

            // Rendering geometry
            public const float SegmentGap = 2f;     // Gap between segments
            public const int PeakHoldTimeMs = 500;  // Time in milliseconds to hold peak values

            // Gradient and shadow
            public const float OutlineStrokeWidth = 1.2f;   // Width of segment outlines
            public const float ShadowBlurRadius = 3f;       // Blur radius for shadow effect

            // Spectrum processing factors
            public const float SpectrumWeight = 0.3f;   // Weight of peak values in spectrum scaling
            public const float BoostFactor = 0.3f;      // Boost factor for higher frequencies

            // Logging
            public const string LogPrefix = "[KenwoodRenderer] "; // Prefix for log messages
        }
        #endregion

        #region Fields
        // Singleton instance
        private static KenwoodRenderer? _instance;

        // Initialization and disposal flags
        private bool _isInitialized, _disposed;

        // Quality settings fields
        private RenderQuality _quality = KenwoodConstants.DefaultQuality;
        private bool _useAntiAlias = true;
        private SKFilterQuality _filterQuality = SKFilterQuality.Medium;
        private bool _enableShadows = true;
        private bool _enableReflections = true;
        private float _segmentRoundness = KenwoodConstants.SegmentRoundness;
        private bool _useSegmentedBars = true;

        // Smoothing and transition factors
        private float _smoothingFactor = KenwoodConstants.DefaultSmoothingFactor;
        private float _transitionSmoothness = KenwoodConstants.DefaultTransitionSmoothness;

        // Configuration values
        private readonly float _animationSpeed = KenwoodConstants.AnimationSpeed;
        private readonly float _reflectionOpacity = KenwoodConstants.ReflectionOpacity;
        private readonly float _reflectionHeight = KenwoodConstants.ReflectionHeight;
        private readonly float _peakFallSpeed = KenwoodConstants.PeakFallSpeed;
        private readonly float _scaleFactor = KenwoodConstants.ScaleFactor;
        private readonly float _desiredSegmentHeight = KenwoodConstants.DesiredSegmentHeight;

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
            Style = SKPaintStyle.Fill,
            Color = SKColors.White // White for peak indicators
        };

        private readonly SKPaint _segmentShadowPaint = new()
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = new SKColor(0, 0, 0, 80), // Semi-transparent black for shadow
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, KenwoodConstants.ShadowBlurRadius)
        };

        private readonly SKPaint _outlinePaint = new()
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(255, 255, 255, 60), // Semi-transparent white for outline
            StrokeWidth = KenwoodConstants.OutlineStrokeWidth
        };

        private readonly SKPaint _reflectionPaint = new()
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            BlendMode = SKBlendMode.SrcOver
        };

        // Color definitions for gradients
        private static readonly SKColor GreenStartColor = new(0, 230, 120, 255); // Bottom of green gradient
        private static readonly SKColor GreenEndColor = new(0, 255, 0, 255);     // Top of green gradient
        private static readonly SKColor YellowStartColor = new(255, 230, 0, 255); // Bottom of yellow gradient
        private static readonly SKColor YellowEndColor = new(255, 180, 0, 255);   // Top of yellow gradient
        private static readonly SKColor RedStartColor = new(255, 80, 0, 255);     // Bottom of red gradient
        private static readonly SKColor RedEndColor = new(255, 30, 0, 255);       // Top of red gradient

        // Calculation thread objects
        private Task? _calculationTask;
        private CancellationTokenSource? _calculationCts;
        private SKRect _lastCanvasRect = SKRect.Empty;
        #endregion

        #region Singleton & Initialization
        private KenwoodRenderer() { }

        public static KenwoodRenderer GetInstance() => _instance ??= new KenwoodRenderer();

        public void Initialize()
        {
            if (_isInitialized)
                return;

            _isInitialized = true;

            // Pre-fill buffer pools
            for (int i = 0; i < 8; i++)
            {
                _floatBufferPool.Enqueue(new float[KenwoodConstants.InitialBufferSize]);
                _dateTimeBufferPool.Enqueue(new DateTime[KenwoodConstants.InitialBufferSize]);
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
        }

        private void StartCalculationThread()
        {
            _calculationCts = new CancellationTokenSource();
            _calculationTask = Task.Factory.StartNew(() => CalculationThreadMain(_calculationCts.Token),
                _calculationCts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
        #endregion

        #region Quality Settings
        public RenderQuality Quality
        {
            get => _quality;
            set
            {
                if (_quality != value)
                {
                    _quality = value;
                    ApplyQualitySettings();
                    _pathsNeedRebuild = true; // Invalidate paths when quality changes
                    SmartLogger.Log(LogLevel.Debug, KenwoodConstants.LogPrefix, $"Quality changed to {_quality}");
                }
            }
        }

        private void ApplyQualitySettings()
        {
            switch (_quality)
            {
                case RenderQuality.Low:
                    _useAntiAlias = false;
                    _filterQuality = SKFilterQuality.Low;
                    _enableShadows = false;
                    _enableReflections = false;
                    _segmentRoundness = 0f; // No rounding for faster rendering
                    _useSegmentedBars = false; // Single gradient rectangle per bar
                    break;
                case RenderQuality.Medium:
                    _useAntiAlias = true;
                    _filterQuality = SKFilterQuality.Medium;
                    _enableShadows = true;
                    _enableReflections = false;
                    _segmentRoundness = 2f; // Moderate rounding
                    _useSegmentedBars = true; // Segmented bars
                    break;
                case RenderQuality.High:
                    _useAntiAlias = true;
                    _filterQuality = SKFilterQuality.High;
                    _enableShadows = true;
                    _enableReflections = true;
                    _segmentRoundness = KenwoodConstants.SegmentRoundness; // Full rounding
                    _useSegmentedBars = true; // Segmented bars with all effects
                    break;
            }

            // Update paint properties
            _peakPaint.IsAntialias = _useAntiAlias;
            _segmentShadowPaint.IsAntialias = _useAntiAlias;
            _outlinePaint.IsAntialias = _useAntiAlias;
            _reflectionPaint.IsAntialias = _useAntiAlias;
        }
        #endregion

        #region Configuration
        public void Configure(bool isOverlayActive, RenderQuality quality = KenwoodConstants.DefaultQuality)
        {
            Quality = quality;
            _smoothingFactor = isOverlayActive ? 0.6f : KenwoodConstants.DefaultSmoothingFactor * 0.2f;
            _transitionSmoothness = isOverlayActive ? 0.7f : 0.5f;
            _pathsNeedRebuild = true;
        }
        #endregion

        #region Buffer Management
        private float[] GetFloatBuffer(int size)
        {
            if (_floatBufferPool.TryDequeue(out var buffer) && buffer.Length >= size)
                return buffer;
            return new float[Math.Max(size, KenwoodConstants.InitialBufferSize)];
        }

        private DateTime[] GetDateTimeBuffer(int size)
        {
            if (_dateTimeBufferPool.TryDequeue(out var buffer) && buffer.Length >= size)
                return buffer;
            return new DateTime[Math.Max(size, KenwoodConstants.InitialBufferSize)];
        }

        private void ReturnBufferToPool(float[]? buffer)
        {
            if (buffer != null && _floatBufferPool.Count < KenwoodConstants.MaxBufferPoolSize)
                _floatBufferPool.Enqueue(buffer);
        }

        private void ReturnBufferToPool(DateTime[]? buffer)
        {
            if (buffer != null && _dateTimeBufferPool.Count < KenwoodConstants.MaxBufferPoolSize)
                _dateTimeBufferPool.Enqueue(buffer);
        }
        #endregion

        #region Calculation Thread
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
                    SmartLogger.Log(LogLevel.Error, KenwoodConstants.LogPrefix, $"Calculation error: {ex.Message}");
                    Thread.Sleep(100);
                }
            }
        }

        private void ProcessSpectrumData(float[] spectrum, int barCount, float canvasHeight)
        {
            int spectrumLength = spectrum.Length;
            int actualBarCount = Math.Min(spectrumLength, barCount);

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

            float[] scaledSpectrum = GetFloatBuffer(actualBarCount);
            ScaleSpectrum(spectrum, scaledSpectrum, actualBarCount, spectrumLength);

            float[] computedBarValues = GetFloatBuffer(actualBarCount);
            float[] computedPeaks = GetFloatBuffer(actualBarCount);
            DateTime currentTime = DateTime.Now;

            float smoothFactor = _smoothingFactor * _animationSpeed;
            float peakFallRate = _peakFallSpeed * (float)canvasHeight * _animationSpeed;
            double peakHoldTimeMs = KenwoodConstants.PeakHoldTimeMs;

            float maxChangeThreshold = canvasHeight * 0.3f;
            float velocityDamping = 0.8f * _transitionSmoothness;
            float springStiffness = 0.2f * (1 - _transitionSmoothness);

            const int batchSize = 64;
            for (int batchStart = 0; batchStart < actualBarCount; batchStart += batchSize)
            {
                int batchEnd = Math.Min(batchStart + batchSize, actualBarCount);

                for (int i = batchStart; i < batchEnd; i++)
                {
                    float targetValue = scaledSpectrum[i];
                    float currentValue = _previousSpectrum[i];
                    float delta = targetValue - currentValue;
                    float adaptiveSmoothFactor = smoothFactor;
                    float absDelta = Math.Abs(delta * canvasHeight);

                    if (absDelta > maxChangeThreshold)
                    {
                        float changeRatio = Math.Min(1.0f, absDelta / (canvasHeight * 0.7f));
                        adaptiveSmoothFactor = Math.Max(smoothFactor * 0.3f,
                            smoothFactor * (1.0f + changeRatio * 0.5f));
                    }

                    _velocities![i] = _velocities[i] * velocityDamping + delta * springStiffness;
                    float newValue = currentValue + _velocities[i] + delta * adaptiveSmoothFactor;
                    newValue = Math.Max(0f, Math.Min(1f, newValue));

                    _previousSpectrum[i] = newValue;
                    float barValue = newValue * canvasHeight;
                    computedBarValues[i] = barValue;

                    if (_peaks != null && _peakHoldTimes != null)
                    {
                        float currentPeak = _peaks[i];
                        if (barValue > currentPeak)
                        {
                            _peaks[i] = barValue;
                            _peakHoldTimes[i] = currentTime;
                        }
                        else if ((currentTime - _peakHoldTimes[i]).TotalMilliseconds > peakHoldTimeMs)
                        {
                            float peakDelta = currentPeak - barValue;
                            float adaptiveFallRate = peakFallRate;

                            if (peakDelta > maxChangeThreshold)
                                adaptiveFallRate = peakFallRate * (1.0f + (peakDelta / canvasHeight) * 2.0f);

                            _peaks[i] = Math.Max(0f, currentPeak - adaptiveFallRate);
                        }
                        computedPeaks[i] = _peaks[i];
                    }
                }
            }

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

            ReturnBufferToPool(scaledSpectrum);
            ReturnBufferToPool(computedBarValues);
            ReturnBufferToPool(computedPeaks);
            _pathsNeedRebuild = true;
        }
        #endregion

        #region Rendering
        public void Render(
            SKCanvas? canvas,
            float[]? spectrum,
            SKImageInfo info,
            float barWidth,
            float barSpacing,
            int barCount,
            SKPaint? paint,
            Action<SKCanvas, SKImageInfo>? drawPerformanceInfo)
        {
            if (!ValidateRenderParameters(canvas, spectrum, info, paint))
                return;

            if (canvas!.QuickReject(new SKRect(0, 0, info.Width, info.Height)))
                return;

            float totalWidth = info.Width;
            float totalBarWidth = totalWidth / barCount;
            barWidth = totalBarWidth * 0.7f;
            barSpacing = totalBarWidth * 0.3f;

            bool canvasSizeChanged = Math.Abs(_lastCanvasRect.Height - info.Height) > 0.5f ||
                                     Math.Abs(_lastCanvasRect.Width - info.Width) > 0.5f;

            bool transformChanged = canvas.TotalMatrix != _lastTransform;
            if (transformChanged)
                _lastTransform = canvas.TotalMatrix;

            if (canvasSizeChanged)
            {
                _currentSegmentCount = Math.Max(1, (int)(info.Height / (_desiredSegmentHeight + KenwoodConstants.SegmentGap)));
                CreateGradients(info.Height);
                _lastCanvasRect = info.Rect;
                _pathsNeedRebuild = true;
            }

            _dataSemaphore.Wait();
            try
            {
                _pendingSpectrum = spectrum;
                _pendingBarCount = barCount;
                _pendingCanvasHeight = info.Height;

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

            if (_renderBarValues == null || _renderPeaks == null || _currentBarCount == 0)
                return;

            int renderCount = Math.Min(_currentBarCount, _renderBarValues.Length);
            if (renderCount <= 0)
                return;

            float totalGap = (_currentSegmentCount - 1) * KenwoodConstants.SegmentGap;
            float segmentHeight = (info.Height - totalGap) / _currentSegmentCount * _scaleFactor;

            bool dimensionsChanged = Math.Abs(_lastTotalBarWidth - totalBarWidth) > 0.01f ||
                                     Math.Abs(_lastBarWidth - barWidth) > 0.01f ||
                                     Math.Abs(_lastBarSpacing - barSpacing) > 0.01f ||
                                     Math.Abs(_lastSegmentHeight - segmentHeight) > 0.01f ||
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
                if (_cachedBarPath != null)
                {
                    using var barPaint = new SKPaint
                    {
                        IsAntialias = _useAntiAlias,
                        Style = SKPaintStyle.Fill,
                        Shader = _barGradient,
                        FilterQuality = _filterQuality
                    };
                    canvas.DrawPath(_cachedBarPath, barPaint);
                }
            }

            if (_cachedPeakPath != null)
                canvas.DrawPath(_cachedPeakPath, _peakPaint);

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

            drawPerformanceInfo?.Invoke(canvas, info);
        }

        private bool ValidateRenderParameters(SKCanvas? canvas, float[]? spectrum, SKImageInfo info, SKPaint? paint)
        {
            return canvas != null && spectrum != null && spectrum.Length >= 2 && paint != null && info.Width > 0 && info.Height > 0;
        }

        private void CreateGradients(float height)
        {
            _greenGradient?.Dispose();
            _yellowGradient?.Dispose();
            _redGradient?.Dispose();
            _reflectionGradient?.Dispose();
            _barGradient?.Dispose();

            _greenGradient = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(0, height / 3),
                new[] { GreenStartColor, GreenEndColor },
                null,
                SKShaderTileMode.Clamp);

            _yellowGradient = SKShader.CreateLinearGradient(
                new SKPoint(0, height / 3),
                new SKPoint(0, height * 2 / 3),
                new[] { YellowStartColor, YellowEndColor },
                null,
                SKShaderTileMode.Clamp);

            _redGradient = SKShader.CreateLinearGradient(
                new SKPoint(0, height * 2 / 3),
                new SKPoint(0, height),
                new[] { RedStartColor, RedEndColor },
                null,
                SKShaderTileMode.Clamp);

            _reflectionGradient = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(0, height * _reflectionHeight),
                new[] { new SKColor(255, 255, 255, (byte)(_reflectionOpacity * 255)), SKColors.Transparent },
                null,
                SKShaderTileMode.Clamp);

            _barGradient = SKShader.CreateLinearGradient(
                new SKPoint(0, height), // Bottom
                new SKPoint(0, 0),      // Top
                new[] { GreenStartColor, GreenEndColor, YellowStartColor, YellowEndColor, RedStartColor, RedEndColor },
                new float[] { 0f, 0.6f, 0.6f, 0.85f, 0.85f, 1f },
                SKShaderTileMode.Clamp);
        }

        private readonly Dictionary<int, SKPaint> _segmentPaints = new();

        private SKPaint EnsureSegmentPaint(int index, SKShader? shader)
        {
            if (!_segmentPaints.TryGetValue(index, out var paint) || paint == null)
            {
                paint = new SKPaint
                {
                    IsAntialias = _useAntiAlias,
                    Style = SKPaintStyle.Fill,
                    FilterQuality = _filterQuality,
                    Shader = shader
                };
                _segmentPaints[index] = paint;
            }
            return paint;
        }

        private void RebuildPaths(SKImageInfo info, float barWidth, float barSpacing, float totalBarWidth, float segmentHeight, int renderCount)
        {
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
                int greenCount = (int)(_currentSegmentCount * 0.6);
                int yellowCount = (int)(_currentSegmentCount * 0.25);

                if (_enableReflections && info.Height > 200)
                    BuildReflectionPath(info, barWidth, barSpacing, totalBarWidth, segmentHeight, renderCount, minVisibleValue);

                for (int i = 0; i < renderCount; i++)
                {
                    float x = i * totalBarWidth + barSpacing / 2;
                    float barValue = _renderBarValues![i];

                    if (barValue > minVisibleValue)
                    {
                        int segmentsToRender = Math.Min((int)Math.Ceiling(barValue / (segmentHeight + KenwoodConstants.SegmentGap)), _currentSegmentCount);
                        bool needShadow = _enableShadows && barValue > info.Height * 0.5f;

                        if (needShadow)
                        {
                            for (int segIndex = 0; segIndex < segmentsToRender; segIndex++)
                            {
                                float segmentBottom = info.Height - segIndex * (segmentHeight + KenwoodConstants.SegmentGap);
                                float segmentTop = segmentBottom - segmentHeight;
                                AddRoundRectToPath(_cachedShadowPath!, x + 2, segmentTop + 2, barWidth, segmentHeight);
                            }
                        }

                        for (int segIndex = 0; segIndex < segmentsToRender; segIndex++)
                        {
                            float segmentBottom = info.Height - segIndex * (segmentHeight + KenwoodConstants.SegmentGap);
                            float segmentTop = segmentBottom - segmentHeight;

                            SKPath targetPath = segIndex < greenCount ? _cachedGreenSegmentsPath!
                                : segIndex < greenCount + yellowCount ? _cachedYellowSegmentsPath! : _cachedRedSegmentsPath!;

                            AddRoundRectToPath(targetPath, x, segmentTop, barWidth, segmentHeight);
                            AddRoundRectToPath(_cachedOutlinePath!, x, segmentTop, barWidth, segmentHeight);
                        }
                    }

                    if (_renderPeaks![i] > minVisibleValue)
                    {
                        float peakY = info.Height - _renderPeaks[i];
                        float peakHeight = 3f;
                        _cachedPeakPath!.AddRect(SKRect.Create(x, peakY - peakHeight, barWidth, peakHeight));
                    }
                }
            }
        }

        private void BuildReflectionPath(SKImageInfo info, float barWidth, float barSpacing, float totalBarWidth, float segmentHeight, int renderCount, float minVisibleValue)
        {
            int reflectionBarStep = Math.Max(1, renderCount / 60);

            for (int i = 0; i < renderCount; i += reflectionBarStep)
            {
                float x = i * totalBarWidth + barSpacing / 2;
                float barValue = _renderBarValues![i];

                if (barValue > minVisibleValue)
                {
                    int maxReflectionSegments = Math.Min(3, _currentSegmentCount);
                    int segmentsToRender = Math.Min((int)Math.Ceiling(barValue / (segmentHeight + KenwoodConstants.SegmentGap)), maxReflectionSegments);

                    for (int segIndex = 0; segIndex < segmentsToRender; segIndex++)
                    {
                        float segmentBottom = info.Height - segIndex * (segmentHeight + KenwoodConstants.SegmentGap);
                        float segmentTop = segmentBottom - segmentHeight;
                        var rect = SKRect.Create(x, segmentTop, barWidth, segmentHeight * _reflectionHeight);
                        if (_segmentRoundness > 0)
                            _cachedReflectionPath!.AddRoundRect(rect, _segmentRoundness, _segmentRoundness);
                        else
                            _cachedReflectionPath!.AddRect(rect);
                    }
                }

                if (_renderPeaks![i] > minVisibleValue)
                {
                    float peakY = info.Height - _renderPeaks[i];
                    float peakHeight = 3f;
                    _cachedReflectionPath!.AddRect(SKRect.Create(x, peakY - peakHeight, barWidth, peakHeight * _reflectionHeight));
                }
            }
        }

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
        private void ScaleSpectrum(float[] spectrum, float[] scaledSpectrum, int barCount, int spectrumLength)
        {
            float blockSize = spectrumLength / (float)barCount;
            int maxThreads = Math.Max(2, Environment.ProcessorCount / 2);

            Parallel.For(0, barCount, new ParallelOptions { MaxDegreeOfParallelism = maxThreads }, i =>
            {
                int start = (int)(i * blockSize);
                int end = Math.Min((int)((i + 1) * blockSize), spectrumLength);
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

                Vector<float> sumVector = Vector<float>.Zero;
                Vector<float> maxVector = new Vector<float>(float.MinValue);

                for (; j <= end - vectorSize; j += vectorSize)
                {
                    var vec = new Vector<float>(spectrum, j);
                    sumVector += vec;
                    maxVector = Vector.Max(maxVector, vec);
                }

                for (int k = 0; k < vectorSize; k++)
                {
                    sum += sumVector[k];
                    peak = Math.Max(peak, maxVector[k]);
                }

                for (; j < end; j++)
                {
                    float value = spectrum[j];
                    sum += value;
                    peak = Math.Max(peak, value);
                }

                float average = sum / count;
                float weight = KenwoodConstants.SpectrumWeight;
                scaledSpectrum[i] = average * (1.0f - weight) + peak * weight;

                if (i > barCount / 2)
                {
                    float boost = 1.0f + ((float)i / barCount) * KenwoodConstants.BoostFactor;
                    scaledSpectrum[i] *= boost;
                }

                scaledSpectrum[i] = Math.Min(1.0f, scaledSpectrum[i]);
            });
        }
        #endregion

        #region Disposal
        public void Dispose()
        {
            if (_disposed)
                return;

            _calculationCts?.Cancel();
            _dataAvailableEvent.Set();

            try
            {
                _calculationTask?.Wait(500);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, KenwoodConstants.LogPrefix, $"Dispose wait error: {ex.Message}");
            }

            _calculationCts?.Dispose();
            _dataAvailableEvent.Dispose();
            _dataSemaphore.Dispose();

            ReturnBufferToPool(_previousSpectrum);
            ReturnBufferToPool(_peaks);
            ReturnBufferToPool(_peakHoldTimes);
            ReturnBufferToPool(_renderBarValues);
            ReturnBufferToPool(_renderPeaks);
            ReturnBufferToPool(_processingBarValues);
            ReturnBufferToPool(_processingPeaks);
            ReturnBufferToPool(_velocities);

            while (_floatBufferPool.TryDequeue(out _)) { }
            while (_dateTimeBufferPool.TryDequeue(out _)) { }

            foreach (var paint in _segmentPaints.Values)
                paint.Dispose();
            _segmentPaints.Clear();

            _cachedBarPath?.Dispose();
            _cachedGreenSegmentsPath?.Dispose();
            _cachedYellowSegmentsPath?.Dispose();
            _cachedRedSegmentsPath?.Dispose();
            _cachedOutlinePath?.Dispose();
            _cachedPeakPath?.Dispose();
            _cachedShadowPath?.Dispose();
            _cachedReflectionPath?.Dispose();

            _greenGradient?.Dispose();
            _yellowGradient?.Dispose();
            _redGradient?.Dispose();
            _reflectionGradient?.Dispose();
            _barGradient?.Dispose();

            _peakPaint.Dispose();
            _segmentShadowPaint.Dispose();
            _outlinePaint.Dispose();
            _reflectionPaint.Dispose();

            _disposed = true;
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}