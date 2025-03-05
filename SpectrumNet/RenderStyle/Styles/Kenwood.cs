namespace SpectrumNet
{
    public class KenwoodRenderer : ISpectrumRenderer, IDisposable
    {
        #region Singleton & Fields

        private static KenwoodRenderer? _instance;
        private bool _isInitialized, _disposed, _initialDataReceived, _enableReflection = true;

        private float 
            _smoothingFactor = 1.5f,
            _pendingCanvasHeight,
            _transitionSmoothness = 1f;

        private readonly float
            _animationSpeed = 0.85f,
            _segmentRoundness = 4.5f,
            _reflectionOpacity = 0.4f,
            _reflectionHeight = 0.6f,
            _peakFallSpeed = 0.007f,
            _scaleFactor = 0.9f,
            _desiredSegmentHeight = 10f;

        private int 
            _currentSegmentCount,
            _currentBarCount,
            _pendingBarCount;

        private float _lastTotalBarWidth,
                      _lastBarWidth,
                      _lastBarSpacing,
                      _lastSegmentHeight;
        private int _lastRenderCount;

        private float[]? _previousSpectrum,
                        _peaks,
                        _renderBarValues,
                        _renderPeaks,
                        _processingBarValues,
                        _processingPeaks,
                        _pendingSpectrum,
                        _velocities;

        private DateTime[]? _peakHoldTimes;

        private SKPath? _cachedGreenSegmentsPath,
                        _cachedYellowSegmentsPath,
                        _cachedRedSegmentsPath,
                        _cachedOutlinePath,
                        _cachedPeakPath,
                        _cachedShadowPath,
                        _cachedReflectionPath;

        private bool _pathsNeedRebuild = true;
        private SKMatrix _lastTransform = SKMatrix.Identity;

        private const float SegmentGap = 2f;
        private const int PeakHoldTimeMs = 500, MaxBufferPoolSize = 16;

        private readonly ConcurrentQueue<float[]> _floatBufferPool = new();
        private readonly ConcurrentQueue<DateTime[]> _dateTimeBufferPool = new();
        private readonly SemaphoreSlim _dataSemaphore = new(1, 1);
        private readonly AutoResetEvent _dataAvailableEvent = new(false);
        private readonly Dictionary<int, SKPaint> _segmentPaints = new();

        private Task? _calculationTask;
        private CancellationTokenSource? _calculationCts;
        private SKRect _lastCanvasRect = SKRect.Empty;

        private SKShader? _greenGradient, _yellowGradient, _redGradient, _reflectionGradient;

        private readonly SKPaint _peakPaint = new()
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = new SKColor(255, 255, 255)
        };

        private readonly SKPaint _segmentShadowPaint = new()
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = new SKColor(0, 0, 0, 80),
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3)
        };

        private readonly SKPaint _outlinePaint = new()
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(255, 255, 255, 60),
            StrokeWidth = 1.2f
        };

        private readonly SKPaint _reflectionPaint = new()
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            BlendMode = SKBlendMode.SrcOver
        };

        private static readonly SKColor GreenStartColor = new(0, 230, 120, 255);
        private static readonly SKColor GreenEndColor = new(0, 255, 0, 255);
        private static readonly SKColor YellowStartColor = new(255, 230, 0, 255);
        private static readonly SKColor YellowEndColor = new(255, 180, 0, 255);
        private static readonly SKColor RedStartColor = new(255, 80, 0, 255);
        private static readonly SKColor RedEndColor = new(255, 30, 0, 255);

        #endregion

        #region Initialization & Configuration

        private KenwoodRenderer() { }

        public static KenwoodRenderer GetInstance() => _instance ??= new KenwoodRenderer();

        public void Initialize()
        {
            if (_isInitialized) return;

            _isInitialized = true;

            for (int i = 0; i < 8; i++)
            {
                _floatBufferPool.Enqueue(new float[1024]);
                _dateTimeBufferPool.Enqueue(new DateTime[1024]);
            }

            // Initialize path caches
            _cachedGreenSegmentsPath = new SKPath();
            _cachedYellowSegmentsPath = new SKPath();
            _cachedRedSegmentsPath = new SKPath();
            _cachedOutlinePath = new SKPath();
            _cachedPeakPath = new SKPath();
            _cachedShadowPath = new SKPath();
            _cachedReflectionPath = new SKPath();

            StartCalculationThread();
        }

        private void StartCalculationThread()
        {
            _calculationCts = new CancellationTokenSource();
            _calculationTask = Task.Factory.StartNew(() => CalculationThreadMain(_calculationCts.Token),
                _calculationCts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        public void Configure(bool isOverlayActive)
        {
            _smoothingFactor = isOverlayActive ? 0.6f : 0.3f;
            _transitionSmoothness = isOverlayActive ? 0.7f : 0.5f;
            _pathsNeedRebuild = true;
        }

        #endregion

        #region Buffer Management

        private float[] GetFloatBuffer(int size)
        {
            if (_floatBufferPool.TryDequeue(out var buffer) && buffer.Length >= size)
                return buffer;
            return new float[Math.Max(size, 1024)];
        }

        private DateTime[] GetDateTimeBuffer(int size)
        {
            if (_dateTimeBufferPool.TryDequeue(out var buffer) && buffer.Length >= size)
                return buffer;
            return new DateTime[Math.Max(size, 1024)];
        }

        private void ReturnBufferToPool(float[]? buffer)
        {
            if (buffer != null && _floatBufferPool.Count < MaxBufferPoolSize)
                _floatBufferPool.Enqueue(buffer);
        }

        private void ReturnBufferToPool(DateTime[]? buffer)
        {
            if (buffer != null && _dateTimeBufferPool.Count < MaxBufferPoolSize)
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
                    if (!_dataAvailableEvent.WaitOne(50)) continue;
                    if (ct.IsCancellationRequested) break;

                    float[]? spectrumData = null;
                    int barCount = 0;
                    float canvasHeight = 0;

                    _dataSemaphore.Wait(ct);
                    try
                    {
                        if (_pendingSpectrum != null)
                        {
                            spectrumData = _pendingSpectrum;
                            barCount = _pendingBarCount;
                            canvasHeight = _pendingCanvasHeight;
                            _pendingSpectrum = null;
                        }
                    }
                    finally { _dataSemaphore.Release(); }

                    if (spectrumData != null && spectrumData.Length > 0)
                        ProcessSpectrumData(spectrumData, barCount, canvasHeight);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    Thread.Sleep(100); // Prevent CPU spinning in case of errors
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
            float peakFallRate = _peakFallSpeed * canvasHeight * _animationSpeed;
            double peakHoldTimeMs = PeakHoldTimeMs;

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
                            {
                                adaptiveFallRate = peakFallRate * (1.0f + (peakDelta / canvasHeight) * 2.0f);
                            }

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

                if (!_initialDataReceived)
                {
                    _renderBarValues = GetFloatBuffer(actualBarCount);
                    _renderPeaks = GetFloatBuffer(actualBarCount);
                    Buffer.BlockCopy(_processingBarValues!, 0, _renderBarValues!, 0, actualBarCount * sizeof(float));
                    Buffer.BlockCopy(_processingPeaks!, 0, _renderPeaks!, 0, actualBarCount * sizeof(float));
                    _initialDataReceived = true;
                }

                _pathsNeedRebuild = true;
            }
            finally
            {
                _dataSemaphore.Release();
            }

            ReturnBufferToPool(scaledSpectrum);
            ReturnBufferToPool(computedBarValues);
            ReturnBufferToPool(computedPeaks);
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
            if (!_isInitialized || canvas == null || spectrum == null || spectrum.Length < 2 ||
                paint == null || info.Width <= 0 || info.Height <= 0)
                return;

            float totalWidth = info.Width;
            float totalBarWidth = totalWidth / barCount;
            barWidth = totalBarWidth * 0.7f; 
            barSpacing = totalBarWidth * 0.3f;

            // Check if we need to rebuild paths
            bool canvasSizeChanged = Math.Abs(_lastCanvasRect.Height - info.Rect.Height) > 0.5f ||
                                    Math.Abs(_lastCanvasRect.Width - info.Rect.Width) > 0.5f;

            bool transformChanged = false;
            if (canvas.TotalMatrix != _lastTransform)
            {
                _lastTransform = canvas.TotalMatrix;
                transformChanged = true;
            }

            if (canvasSizeChanged)
            {
                _currentSegmentCount = Math.Max(1, (int)(info.Height / (_desiredSegmentHeight + SegmentGap)));

                foreach (var p in _segmentPaints.Values) p.Dispose();
                _segmentPaints.Clear();

                _greenGradient?.Dispose();
                _yellowGradient?.Dispose();
                _redGradient?.Dispose();
                _reflectionGradient?.Dispose();
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

            if (!_initialDataReceived || _renderBarValues == null || _renderPeaks == null)
                return;

            int renderCount = Math.Min(_currentBarCount, _renderBarValues.Length);
            if (renderCount <= 0)
                return;

            float totalGap = (_currentSegmentCount - 1) * SegmentGap;
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

            if (_enableReflection && info.Height > 200 && _cachedReflectionPath != null)
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

            if (_cachedShadowPath != null)
                canvas.DrawPath(_cachedShadowPath, _segmentShadowPaint);

            EnsureSegmentPaints();

            if (_cachedGreenSegmentsPath != null)
                canvas.DrawPath(_cachedGreenSegmentsPath, _segmentPaints[0]);
            if (_cachedYellowSegmentsPath != null)
                canvas.DrawPath(_cachedYellowSegmentsPath, _segmentPaints[1]);
            if (_cachedRedSegmentsPath != null)
                canvas.DrawPath(_cachedRedSegmentsPath, _segmentPaints[2]);
            if (_cachedOutlinePath != null)
                canvas.DrawPath(_cachedOutlinePath, _outlinePaint);
            if (_cachedPeakPath != null)
                canvas.DrawPath(_cachedPeakPath, _peakPaint);

            drawPerformanceInfo?.Invoke(canvas, info);
        }

        private void CreateGradients(float height)
        {
            _greenGradient?.Dispose();
            _yellowGradient?.Dispose();
            _redGradient?.Dispose();
            _reflectionGradient?.Dispose();

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
                new[] {
            new SKColor(255, 255, 255, (byte)(_reflectionOpacity * 255)),
            new SKColor(255, 255, 255, 0)
                },
                null,
                SKShaderTileMode.Clamp);
        }

        private void EnsureSegmentPaints()
        {
            if (!_segmentPaints.TryGetValue(0, out SKPaint? greenPaint) || greenPaint == null)
            {
                _segmentPaints[0] = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Fill,
                    Color = GreenStartColor,
                    Shader = _greenGradient
                };
            }

            if (!_segmentPaints.TryGetValue(1, out SKPaint? yellowPaint) || yellowPaint == null)
            {
                _segmentPaints[1] = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Fill,
                    Color = YellowStartColor,
                    Shader = _yellowGradient
                };
            }

            if (!_segmentPaints.TryGetValue(2, out SKPaint? redPaint) || redPaint == null)
            {
                _segmentPaints[2] = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Fill,
                    Color = RedStartColor,
                    Shader = _redGradient
                };
            }
        }

        private void RebuildPaths(SKImageInfo info, float barWidth, float barSpacing,
                                float totalBarWidth, float segmentHeight, int renderCount)
        {
            if (_cachedGreenSegmentsPath == null || _cachedYellowSegmentsPath == null ||
                _cachedRedSegmentsPath == null || _cachedOutlinePath == null ||
                _cachedPeakPath == null || _cachedShadowPath == null || _cachedReflectionPath == null)
                return;

            _cachedGreenSegmentsPath.Reset();
            _cachedYellowSegmentsPath.Reset();
            _cachedRedSegmentsPath.Reset();
            _cachedOutlinePath.Reset();
            _cachedPeakPath.Reset();
            _cachedShadowPath.Reset();
            _cachedReflectionPath.Reset();

            float minVisibleValue = segmentHeight * 0.5f;
            int greenCount = (int)(_currentSegmentCount * 0.6);
            int yellowCount = (int)(_currentSegmentCount * 0.25);

            if (_enableReflection && info.Height > 200)
            {
                BuildReflectionPath(info, barWidth, barSpacing, totalBarWidth, segmentHeight,
                                    renderCount, minVisibleValue);
            }

            for (int i = 0; i < renderCount; i++)
            {
                float x = i * totalBarWidth + barSpacing / 2;
                float barValue = _renderBarValues![i];

                if (barValue > minVisibleValue)
                {
                    int segmentsToRender = (int)Math.Ceiling(barValue / (segmentHeight + SegmentGap));
                    segmentsToRender = Math.Min(segmentsToRender, _currentSegmentCount);
                    bool needShadow = barValue > info.Height * 0.5f;

                    if (needShadow)
                    {
                        for (int segIndex = 0; segIndex < segmentsToRender; segIndex++)
                        {
                            float segmentBottom = info.Height - segIndex * (segmentHeight + SegmentGap);
                            float segmentTop = segmentBottom - segmentHeight;
                            AddRoundRectToPath(_cachedShadowPath, x + 2, segmentTop + 2, barWidth, segmentHeight);
                        }
                    }

                    for (int segIndex = 0; segIndex < segmentsToRender; segIndex++)
                    {
                        float segmentBottom = info.Height - segIndex * (segmentHeight + SegmentGap);
                        float segmentTop = segmentBottom - segmentHeight;

                        SKPath targetPath;
                        if (segIndex < greenCount)
                        {
                            targetPath = _cachedGreenSegmentsPath;
                        }
                        else if (segIndex < greenCount + yellowCount)
                        {
                            targetPath = _cachedYellowSegmentsPath;
                        }
                        else
                        {
                            targetPath = _cachedRedSegmentsPath;
                        }

                        AddRoundRectToPath(targetPath, x, segmentTop, barWidth, segmentHeight);
                        AddRoundRectToPath(_cachedOutlinePath, x, segmentTop, barWidth, segmentHeight);
                    }
                }

                if (_renderPeaks![i] > minVisibleValue)
                {
                    float peakY = info.Height - _renderPeaks[i];
                    float peakHeight = 3f;
                    _cachedPeakPath.AddRect(SKRect.Create(x, peakY - peakHeight, barWidth, peakHeight));
                }
            }
        }

        private void BuildReflectionPath(SKImageInfo info, float barWidth, float barSpacing,
                                        float totalBarWidth, float segmentHeight, int renderCount,
                                        float minVisibleValue)
        {
            if (_cachedReflectionPath == null)
                return;

            int reflectionBarStep = Math.Max(1, renderCount / 60);

            for (int i = 0; i < renderCount; i += reflectionBarStep)
            {
                float x = i * totalBarWidth + barSpacing / 2;
                float barValue = _renderBarValues![i];

                if (barValue > minVisibleValue)
                {
                    int maxReflectionSegments = Math.Min(3, _currentSegmentCount);
                    int segmentsToRender = (int)Math.Ceiling(barValue / (segmentHeight + SegmentGap));
                    segmentsToRender = Math.Min(segmentsToRender, maxReflectionSegments);

                    for (int segIndex = 0; segIndex < segmentsToRender; segIndex++)
                    {
                        float segmentBottom = info.Height - segIndex * (segmentHeight + SegmentGap);
                        float segmentTop = segmentBottom - segmentHeight;
                        var rect = SKRect.Create(x, segmentTop, barWidth, segmentHeight * _reflectionHeight);

                        if (_segmentRoundness > 0)
                            _cachedReflectionPath.AddRoundRect(rect, _segmentRoundness, _segmentRoundness);
                        else
                            _cachedReflectionPath.AddRect(rect);
                    }
                }

                if (_renderPeaks![i] > info.Height * 0.5f)
                {
                    float peakY = info.Height - _renderPeaks[i];
                    float peakHeight = 3f;
                    var peakRect = SKRect.Create(x, peakY - peakHeight, barWidth, peakHeight * _reflectionHeight);
                    _cachedReflectionPath.AddRect(peakRect);
                }
            }
        }

        private void AddRoundRectToPath(SKPath path, float x, float y, float width, float height)
        {
            if (path == null) return;

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
            int maxThreads = Math.Max(2, Environment.ProcessorCount / 2); // Use half of available cores

            Parallel.For(0, barCount, new ParallelOptions { MaxDegreeOfParallelism = maxThreads }, i =>
            {
                float sum = 0;
                float peak = 0;
                int start = (int)(i * blockSize);
                int end = Math.Min((int)((i + 1) * blockSize), spectrumLength);
                int count = end - start;

                if (count <= 0)
                {
                    scaledSpectrum[i] = 0f;
                    return;
                }

                // Use SIMD-friendly processing in chunks of 16
                int j = start;
                int chunkEnd = start + (count / 16 * 16);

                while (j < chunkEnd)
                {
                    peak = Math.Max(peak, Math.Max(
                        Math.Max(Math.Max(spectrum[j], spectrum[j + 1]), Math.Max(spectrum[j + 2], spectrum[j + 3])),
                        Math.Max(Math.Max(spectrum[j + 4], spectrum[j + 5]), Math.Max(spectrum[j + 6], spectrum[j + 7]))
                    ));
                    peak = Math.Max(peak, Math.Max(
                        Math.Max(Math.Max(spectrum[j + 8], spectrum[j + 9]), Math.Max(spectrum[j + 10], spectrum[j + 11])),
                        Math.Max(Math.Max(spectrum[j + 12], spectrum[j + 13]), Math.Max(spectrum[j + 14], spectrum[j + 15]))
                    ));

                    sum += spectrum[j] + spectrum[j + 1] +
                           spectrum[j + 2] + spectrum[j + 3] +
                           spectrum[j + 4] + spectrum[j + 5] +
                           spectrum[j + 6] + spectrum[j + 7] +
                           spectrum[j + 8] + spectrum[j + 9] +
                           spectrum[j + 10] + spectrum[j + 11] +
                           spectrum[j + 12] + spectrum[j + 13] +
                           spectrum[j + 14] + spectrum[j + 15];
                    j += 16;
                }

                while (j < end)
                {
                    float value = spectrum[j];
                    sum += value;
                    peak = Math.Max(peak, value);
                    j++;
                }

                float average = sum / count;
                float weight = 0.3f; 
                scaledSpectrum[i] = average * (1.0f - weight) + peak * weight;

                if (i > barCount / 2)
                {
                    float boost = 1.0f + ((float)i / barCount) * 0.3f;
                    scaledSpectrum[i] *= boost;
                }

                scaledSpectrum[i] = Math.Min(1.0f, scaledSpectrum[i]);
            });
        }

        #endregion

        #region Disposal

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                _calculationCts?.Cancel();
                _dataAvailableEvent.Set();

                try
                {
                    _calculationTask?.Wait(500);
                }
                catch { /* Ignore task cancellation exceptions */ }

                _calculationCts?.Dispose();
                _dataAvailableEvent.Dispose();
                _dataSemaphore.Dispose();

                _previousSpectrum = null;
                _peaks = null;
                _peakHoldTimes = null;
                _renderBarValues = null;
                _renderPeaks = null;
                _processingBarValues = null;
                _processingPeaks = null;
                _velocities = null;

                while (_floatBufferPool.TryDequeue(out _)) { }
                while (_dateTimeBufferPool.TryDequeue(out _)) { }

                foreach (var paint in _segmentPaints.Values)
                    paint.Dispose();
                _segmentPaints.Clear();

                _greenGradient?.Dispose();
                _yellowGradient?.Dispose();
                _redGradient?.Dispose();
                _reflectionGradient?.Dispose();

                _cachedGreenSegmentsPath?.Dispose();
                _cachedYellowSegmentsPath?.Dispose();
                _cachedRedSegmentsPath?.Dispose();
                _cachedOutlinePath?.Dispose();
                _cachedPeakPath?.Dispose();
                _cachedShadowPath?.Dispose();
                _cachedReflectionPath?.Dispose();

                _peakPaint.Dispose();
                _segmentShadowPaint.Dispose();
                _outlinePaint.Dispose();
                _reflectionPaint.Dispose();
            }
            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}