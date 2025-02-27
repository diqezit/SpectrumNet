namespace SpectrumNet
{
    public class KenwoodRenderer : ISpectrumRenderer, IDisposable
    {
        private static KenwoodRenderer? _instance;
        private bool _isInitialized;
        private bool _disposed;
        private float[]? _previousSpectrum;
        private float[]? _peaks;
        private DateTime[]? _peakHoldTimes;
        private float _smoothingFactor = 0.3f;
        private float _peakFallSpeed = 0.007f;
        private const int SegmentCount = 16;
        private const float SegmentGap = 2f;
        private const int PeakHoldTimeMs = 400;
        private const int MaxBufferPoolSize = 10;

        private readonly ConcurrentQueue<float[]> _floatBufferPool = new();
        private readonly ConcurrentQueue<DateTime[]> _dateTimeBufferPool = new();
        private readonly SemaphoreSlim _dataSemaphore = new(1, 1);
        private readonly AutoResetEvent _dataAvailableEvent = new(false);
        private readonly Dictionary<int, SKPaint> _segmentPaints = new();
        private readonly Dictionary<int, SKPaint> _glowPaints = new();

        private Task? _calculationTask;
        private CancellationTokenSource? _calculationCts;
        private float[]? _renderBarValues;
        private float[]? _renderPeaks;
        private float[]? _processingBarValues;
        private float[]? _processingPeaks;
        private float[]? _pendingSpectrum;
        private int _currentBarCount;
        private int _pendingBarCount;
        private float _pendingCanvasHeight;
        private bool _initialDataReceived;
        private SKRect _lastCanvasRect = SKRect.Empty;

        private float _animationSpeed = 0.85f;
        private float _segmentRoundness = 4f;
        private float _glowIntensity = 1.5f;
        private bool _enableReflection = true;
        private float _reflectionOpacity = 0.3f;
        private float _reflectionHeight = 0.5f;

        private static readonly SKPaint PeakPaint = new()
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = new SKColor(255, 255, 255)
        };

        private static readonly SKPaint PeakGlowPaint = new()
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = new SKColor(255, 255, 255, 120),
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 8)
        };

        private static readonly SKPaint SegmentShadowPaint = new()
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = new SKColor(0, 0, 0, 100),
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 5)
        };

        private static readonly SKPaint OutlinePaint = new()
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(255, 255, 255, 80),
            StrokeWidth = 1.5f
        };

        private static readonly SKPaint ReflectionPaint = new()
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            BlendMode = SKBlendMode.SrcOver
        };

        private KenwoodRenderer() { }

        public static KenwoodRenderer GetInstance() => _instance ??= new KenwoodRenderer();

        public void Initialize()
        {
            if (!_isInitialized)
            {
                _isInitialized = true;
                for (int i = 0; i < 5; i++)
                {
                    _floatBufferPool.Enqueue(new float[1024]);
                    _dateTimeBufferPool.Enqueue(new DateTime[1024]);
                }
                StartCalculationThread();
            }
        }

        private void StartCalculationThread()
        {
            _calculationCts = new CancellationTokenSource();
            _calculationTask = Task.Factory.StartNew(() => CalculationThreadMain(_calculationCts.Token),
                _calculationCts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        private float[] GetFloatBuffer(int size) =>
            _floatBufferPool.TryDequeue(out var buffer) && buffer.Length >= size ? buffer : new float[size];

        private DateTime[] GetDateTimeBuffer(int size) =>
            _dateTimeBufferPool.TryDequeue(out var buffer) && buffer.Length >= size ? buffer : new DateTime[size];

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

        private void CalculationThreadMain(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
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
        }

        private void ProcessSpectrumData(float[] spectrum, int barCount, float canvasHeight)
        {
            int halfSpectrumLength = spectrum.Length / 2;
            int actualBarCount = Math.Min(halfSpectrumLength, barCount);

            if (_previousSpectrum == null || _previousSpectrum.Length != actualBarCount)
            {
                ReturnBufferToPool(_previousSpectrum);
                ReturnBufferToPool(_peaks);
                ReturnBufferToPool(_peakHoldTimes);

                _previousSpectrum = GetFloatBuffer(actualBarCount);
                _peaks = GetFloatBuffer(actualBarCount);
                _peakHoldTimes = GetDateTimeBuffer(actualBarCount);
                Array.Fill(_peakHoldTimes, DateTime.MinValue);
            }

            float[] scaledSpectrum = GetFloatBuffer(actualBarCount);
            ScaleSpectrum(spectrum, scaledSpectrum, actualBarCount, halfSpectrumLength);

            float[] computedBarValues = GetFloatBuffer(actualBarCount);
            float[] computedPeaks = GetFloatBuffer(actualBarCount);
            DateTime currentTime = DateTime.Now;

            for (int i = 0; i < actualBarCount; i++)
            {
                float smoothed = _previousSpectrum[i] * (1 - _smoothingFactor * _animationSpeed) +
                                 scaledSpectrum[i] * _smoothingFactor * _animationSpeed;
                _previousSpectrum[i] = smoothed;

                float barValue = smoothed * canvasHeight;
                computedBarValues[i] = barValue;

                if (_peaks != null && _peakHoldTimes != null)
                {
                    if (barValue > _peaks[i])
                    {
                        _peaks[i] = barValue;
                        _peakHoldTimes[i] = currentTime;
                    }
                    else if ((currentTime - _peakHoldTimes[i]).TotalMilliseconds > PeakHoldTimeMs)
                    {
                        _peaks[i] = Math.Max(0f, _peaks[i] - _peakFallSpeed * canvasHeight * _animationSpeed);
                    }
                    computedPeaks[i] = _peaks[i];
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
            }
            finally
            {
                _dataSemaphore.Release();
            }

            ReturnBufferToPool(scaledSpectrum);
            ReturnBufferToPool(computedBarValues);
            ReturnBufferToPool(computedPeaks);
        }

        public void Configure(bool isOverlayActive) =>
            _smoothingFactor = isOverlayActive ? 0.6f : 0.3f;

        public void SetVisualStyle(float animationSpeed = 0.85f, float segmentRoundness = 4f,
                                  float glowIntensity = 1.5f, bool enableReflection = true,
                                  float reflectionOpacity = 0.3f)
        {
            _animationSpeed = animationSpeed;
            _segmentRoundness = segmentRoundness;
            _glowIntensity = glowIntensity;
            _enableReflection = enableReflection;
            _reflectionOpacity = reflectionOpacity;

            foreach (var p in _segmentPaints.Values) p.Dispose();
            _segmentPaints.Clear();

            foreach (var p in _glowPaints.Values) p.Dispose();
            _glowPaints.Clear();
        }

        public void Render(
            SKCanvas canvas,
            float[] spectrum,
            SKImageInfo info,
            float barWidth,
            float barSpacing,
            int barCount,
            SKPaint paint,
            Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
        {
            if (!_isInitialized || canvas == null || spectrum == null || spectrum.Length < 2 ||
                paint == null || info.Width <= 0 || info.Height <= 0)
                return;

            if (_lastCanvasRect.Height != info.Rect.Height)
            {
                foreach (var p in _segmentPaints.Values) p.Dispose();
                _segmentPaints.Clear();

                foreach (var p in _glowPaints.Values) p.Dispose();
                _glowPaints.Clear();

                _lastCanvasRect = info.Rect;
            }

            _dataSemaphore.Wait();
            try
            {
                _pendingSpectrum = spectrum;
                _pendingBarCount = barCount;
                _pendingCanvasHeight = info.Height;

                if (_processingBarValues != null && _renderBarValues != null &&
                    _processingPeaks != null && _renderPeaks != null)
                {
                    ((_renderBarValues, _processingBarValues), (_renderPeaks, _processingPeaks)) =
                        ((_processingBarValues, _renderBarValues), (_processingPeaks, _renderPeaks));
                }
            }
            finally
            {
                _dataSemaphore.Release();
                _dataAvailableEvent.Set();
            }

            if (_initialDataReceived && _renderBarValues != null && _renderPeaks != null)
            {
                float totalBarWidth = barWidth + barSpacing;
                float totalGap = (SegmentCount - 1) * SegmentGap;
                float segmentHeight = (info.Height - totalGap) / SegmentCount;
                int renderCount = Math.Min(_currentBarCount, _renderBarValues.Length);

                if (_enableReflection)
                {
                    RenderReflections(canvas, info, barWidth, barSpacing, totalBarWidth, segmentHeight, renderCount);
                }

                RenderBarsAndPeaks(canvas, info, barWidth, barSpacing, totalBarWidth, segmentHeight, renderCount);
            }

            drawPerformanceInfo?.Invoke(canvas, info);
        }

        private void RenderReflections(SKCanvas canvas, SKImageInfo info, float barWidth, float barSpacing,
                                     float totalBarWidth, float segmentHeight, int renderCount)
        {
            canvas.Save();
            canvas.Scale(1, -1);
            canvas.Translate(0, -info.Height * 2);

            if (ReflectionPaint.Color.Alpha == 0)
                ReflectionPaint.Color = new SKColor(255, 255, 255, (byte)(_reflectionOpacity * 255));

            for (int i = 0; i < renderCount; i++)
            {
                float x = i * totalBarWidth + barSpacing / 2;
                float barValue = _renderBarValues![i];

                DrawSegmentsReflection(canvas, x, barWidth, barValue, info.Height, segmentHeight);

                if (_renderPeaks![i] > 0)
                    DrawPeakReflection(canvas, x, barWidth, _renderPeaks[i], info.Height);
            }

            canvas.Restore();
        }

        private void RenderBarsAndPeaks(SKCanvas canvas, SKImageInfo info, float barWidth, float barSpacing,
                                      float totalBarWidth, float segmentHeight, int renderCount)
        {
            for (int i = 0; i < renderCount; i++)
            {
                float x = i * totalBarWidth + barSpacing / 2;
                float barValue = _renderBarValues![i];

                DrawSegments(canvas, x, barWidth, barValue, info.Height, segmentHeight);

                if (_renderPeaks![i] > 0)
                    DrawPeak(canvas, x, barWidth, _renderPeaks[i], info.Height);
            }
        }

        private void ScaleSpectrum(float[] spectrum, float[] scaledSpectrum, int barCount, int halfSpectrumLength)
        {
            float blockSize = halfSpectrumLength / (float)barCount;
            int maxThreads = Math.Max(1, Environment.ProcessorCount / 2);

            Parallel.For(0, barCount, new ParallelOptions { MaxDegreeOfParallelism = maxThreads }, i =>
            {
                float sum = 0;
                int start = (int)(i * blockSize);
                int end = Math.Min((int)((i + 1) * blockSize), halfSpectrumLength);

                int j = start;
                int unrollCount = (end - start) / 4 * 4;
                int unrollEnd = start + unrollCount;

                while (j < unrollEnd)
                {
                    sum += spectrum[j] + spectrum[j + 1] + spectrum[j + 2] + spectrum[j + 3];
                    j += 4;
                }

                while (j < end)
                    sum += spectrum[j++];

                scaledSpectrum[i] = (end > start) ? sum / (end - start) : 0f;
            });
        }

        private void DrawSegments(SKCanvas canvas, float x, float barWidth, float barValue,
                                 float canvasHeight, float segmentHeight)
        {
            for (int segIndex = 0; segIndex < SegmentCount; segIndex++)
            {
                float segmentBottom = canvasHeight - segIndex * (segmentHeight + SegmentGap);
                float segmentTop = segmentBottom - segmentHeight;

                if (barValue >= (canvasHeight - segmentBottom))
                {
                    if (!_segmentPaints.TryGetValue(segIndex, out SKPaint? segmentPaint) || segmentPaint == null)
                    {
                        SKColor baseColor = GetSegmentColor(segIndex);
                        SKColor topColor = LightenColor(baseColor, 0.5f);
                        SKColor bottomColor = DarkenColor(baseColor, 0.3f);

                        segmentPaint = new SKPaint
                        {
                            IsAntialias = true,
                            Style = SKPaintStyle.Fill,
                            Shader = SKShader.CreateLinearGradient(
                                new SKPoint(x, segmentTop),
                                new SKPoint(x, segmentBottom),
                                new[] { topColor, bottomColor },
                                null,
                                SKShaderTileMode.Clamp)
                        };
                        _segmentPaints[segIndex] = segmentPaint;
                    }

                    bool isHighSegment = segIndex >= SegmentCount - 3;
                    if (_glowIntensity > 0 && isHighSegment &&
                        (!_glowPaints.TryGetValue(segIndex, out SKPaint? glowPaint) || glowPaint == null))
                    {
                        SKColor glowColor = GetSegmentColor(segIndex);
                        glowPaint = new SKPaint
                        {
                            IsAntialias = true,
                            Style = SKPaintStyle.Fill,
                            Color = new SKColor(glowColor.Red, glowColor.Green, glowColor.Blue,
                                               (byte)(120 * _glowIntensity)),
                            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 10 * _glowIntensity)
                        };
                        _glowPaints[segIndex] = glowPaint;
                    }

                    if (_glowIntensity > 0 && isHighSegment && _glowPaints.TryGetValue(segIndex, out var glow))
                        DrawRoundRectOrRect(canvas, x - 2, segmentTop - 2, barWidth + 4, segmentHeight + 4, glow);

                    DrawRoundRectOrRect(canvas, x + 3, segmentTop + 3, barWidth, segmentHeight, SegmentShadowPaint);
                    DrawRoundRectOrRect(canvas, x, segmentTop, barWidth, segmentHeight, segmentPaint);
                    DrawRoundRectOrRect(canvas, x, segmentTop, barWidth, segmentHeight, OutlinePaint);
                }
            }
        }

        private void DrawRoundRectOrRect(SKCanvas canvas, float x, float y, float width, float height, SKPaint paint)
        {
            var rect = SKRect.Create(x, y, width, height);
            if (_segmentRoundness > 0)
                canvas.DrawRoundRect(rect, _segmentRoundness, _segmentRoundness, paint);
            else
                canvas.DrawRect(rect, paint);
        }

        private void DrawSegmentsReflection(SKCanvas canvas, float x, float barWidth, float barValue,
                                          float canvasHeight, float segmentHeight)
        {
            for (int segIndex = 0; segIndex < SegmentCount; segIndex++)
            {
                float segmentBottom = canvasHeight - segIndex * (segmentHeight + SegmentGap);
                float segmentTop = segmentBottom - segmentHeight;

                if (barValue >= (canvasHeight - segmentBottom) &&
                    _segmentPaints.TryGetValue(segIndex, out SKPaint? segmentPaint) && segmentPaint != null)
                {
                    var rect = SKRect.Create(x, segmentTop, barWidth, segmentHeight * _reflectionHeight);
                    if (_segmentRoundness > 0)
                    {
                        canvas.DrawRoundRect(rect, _segmentRoundness, _segmentRoundness, segmentPaint);
                        canvas.DrawRoundRect(rect, _segmentRoundness, _segmentRoundness, ReflectionPaint);
                    }
                    else
                    {
                        canvas.DrawRect(rect, segmentPaint);
                        canvas.DrawRect(rect, ReflectionPaint);
                    }
                }
            }
        }

        private void DrawPeak(SKCanvas canvas, float x, float barWidth, float peakValue, float canvasHeight)
        {
            float peakY = canvasHeight - peakValue;
            float peakHeight = 4f;

            canvas.DrawRect(SKRect.Create(x - 3, peakY - peakHeight - 3, barWidth + 6, peakHeight + 6), PeakGlowPaint);
            canvas.DrawRect(SKRect.Create(x, peakY - peakHeight, barWidth, peakHeight), PeakPaint);
        }

        private void DrawPeakReflection(SKCanvas canvas, float x, float barWidth, float peakValue, float canvasHeight)
        {
            float peakY = canvasHeight - peakValue;
            float peakHeight = 4f;

            var peakRect = SKRect.Create(x, peakY - peakHeight, barWidth, peakHeight * _reflectionHeight);
            canvas.DrawRect(peakRect, PeakPaint);
            canvas.DrawRect(peakRect, ReflectionPaint);
        }

        private SKColor GetSegmentColor(int segIndex)
        {
            const int greenCount = 10, yellowCount = 3;

            if (segIndex < greenCount)
                return new SKColor(0, 230, 0);
            if (segIndex < greenCount + yellowCount)
                return new SKColor(255, 230, 0);
            return new SKColor(255, 50, 0);
        }

        private static SKColor LightenColor(SKColor color, float factor) =>
            new(
                (byte)Math.Min(255, color.Red + (255 - color.Red) * factor),
                (byte)Math.Min(255, color.Green + (255 - color.Green) * factor),
                (byte)Math.Min(255, color.Blue + (255 - color.Blue) * factor),
                color.Alpha
            );

        private static SKColor DarkenColor(SKColor color, float factor) =>
            new(
                (byte)(color.Red * (1 - factor)),
                (byte)(color.Green * (1 - factor)),
                (byte)(color.Blue * (1 - factor)),
                color.Alpha
            );

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                _calculationCts?.Cancel();
                _dataAvailableEvent.Set();
                _calculationTask?.Wait(500);
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

                while (_floatBufferPool.TryDequeue(out _)) { }
                while (_dateTimeBufferPool.TryDequeue(out _)) { }

                foreach (var paint in _segmentPaints.Values)
                    paint.Dispose();
                _segmentPaints.Clear();

                foreach (var paint in _glowPaints.Values)
                    paint.Dispose();
                _glowPaints.Clear();
            }
            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}