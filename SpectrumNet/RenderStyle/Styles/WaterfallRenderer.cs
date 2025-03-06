#nullable enable

namespace SpectrumNet
{
    public sealed class WaterfallRenderer : ISpectrumRenderer, IDisposable
    {
        #region Fields
        private static WaterfallRenderer? _instance;
        private bool _isInitialized;
        private bool _isOverlayActive;
        private volatile bool _disposed;
        private readonly SemaphoreSlim _spectrumSemaphore = new(1, 1);
        private const string LogPrefix = "[WaterfallRenderer] ";

        private const float MIN_SIGNAL = 1e-6f;          // Minimum signal value to avoid log(0)
        private const int DEFAULT_BUFFER_HEIGHT = 256;   // Default spectrogram buffer height
        private const int OVERLAY_BUFFER_HEIGHT = 128;   // Buffer height when overlay is active
        private const int DEFAULT_SPECTRUM_WIDTH = 1024; // Default spectrum width
        private const int COLOR_PALETTE_SIZE = 256;      // Size of the color palette array
        private const int SEMAPHORE_TIMEOUT_MS = 5;      // Timeout for semaphore wait in ms
        private const int MIN_BAR_COUNT = 10;            // Minimum number of bars
        private const float DETAIL_SCALE_FACTOR = 0.8f;  // Factor for detail scaling based on bar width
        private const float ZOOM_THRESHOLD = 2.0f;       // Threshold for applying zoom-based enhancement

        private const float BLUE_THRESHOLD = 0.25f;      // Threshold for deep blue to light blue
        private const float CYAN_THRESHOLD = 0.5f;       // Threshold for cyan to yellow
        private const float YELLOW_THRESHOLD = 0.75f;    // Threshold for yellow to red

        private const int LOW_BAR_COUNT_THRESHOLD = 64;  // Threshold for low detail rendering
        private const int HIGH_BAR_COUNT_THRESHOLD = 256; // Threshold for high detail rendering

        private float[]? _currentSpectrum;
        private float[][]? _spectrogramBuffer;
        private int _bufferHead;
        private SKBitmap? _waterfallBitmap;
        private float _lastBarWidth;
        private int _lastBarCount;
        private float _lastBarSpacing;
        private bool _needsBufferResize;

        private readonly ObjectPool<float[]> _spectrumPool = new(
            () => new float[DEFAULT_SPECTRUM_WIDTH],
            array => Array.Clear(array, 0, array.Length),
            initialCount: 2);

        private static readonly int[] _colorPalette = InitColorPalette();
        #endregion

        #region Constructors
        private WaterfallRenderer() { }
        #endregion

        #region Public Methods
        public static WaterfallRenderer GetInstance() => _instance ??= new WaterfallRenderer();

        public void Initialize()
        {
            if (_isInitialized) return;

            int bufferHeight = _isOverlayActive ? OVERLAY_BUFFER_HEIGHT : DEFAULT_BUFFER_HEIGHT;
            InitializeSpectrogramBuffer(bufferHeight, DEFAULT_SPECTRUM_WIDTH);
            _isInitialized = true;

            SmartLogger.Log(LogLevel.Debug, LogPrefix, "Initialized for SDR waterfall visualization");
        }

        public void Configure(bool isOverlayActive)
        {
            if (_isOverlayActive == isOverlayActive) return;

            _isOverlayActive = isOverlayActive;
            int bufferHeight = _isOverlayActive ? OVERLAY_BUFFER_HEIGHT : DEFAULT_BUFFER_HEIGHT;

            if (_spectrogramBuffer != null)
            {
                int currentWidth = _spectrogramBuffer[0].Length;
                InitializeSpectrogramBuffer(bufferHeight, currentWidth);
            }
            else
            {
                InitializeSpectrogramBuffer(bufferHeight, DEFAULT_SPECTRUM_WIDTH);
            }
        }

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
            if (canvas == null)
            {
                SmartLogger.Log(LogLevel.Warning, LogPrefix, "Canvas cannot be null");
                return;
            }

            if (!ValidateRenderParams(canvas, info, barWidth, barCount, barSpacing))
                return;

            bool parametersChanged = CheckParametersChanged(barWidth, barSpacing, barCount);

            bool semaphoreAcquired = false;
            try
            {
                semaphoreAcquired = _spectrumSemaphore.Wait(SEMAPHORE_TIMEOUT_MS);
                if (semaphoreAcquired)
                {
                    float[]? adjustedSpectrum = spectrum;
                    if (spectrum != null && parametersChanged)
                    {
                        adjustedSpectrum = AdjustSpectrumResolution(spectrum, barCount, barWidth);
                    }

                    UpdateSpectrogramBuffer(adjustedSpectrum);

                    if (_needsBufferResize && parametersChanged)
                    {
                        ResizeBufferForBarParameters(barCount);
                        _needsBufferResize = false;
                    }
                }

                if (_spectrogramBuffer == null || _disposed)
                {
                    SmartLogger.Log(LogLevel.Warning, LogPrefix, "Spectrogram buffer is null or renderer is disposed");
                    return;
                }

                int bufferHeight = _spectrogramBuffer.Length;
                int spectrumWidth = _spectrogramBuffer[0].Length;

                if (_waterfallBitmap == null ||
                    _waterfallBitmap.Width != spectrumWidth ||
                    _waterfallBitmap.Height != bufferHeight)
                {
                    _waterfallBitmap?.Dispose();
                    _waterfallBitmap = new SKBitmap(spectrumWidth, bufferHeight);
                }

                RenderQuality quality = CalculateRenderQuality(barCount);

                UpdateBitmapSafe(_waterfallBitmap, _spectrogramBuffer, _bufferHead, spectrumWidth, bufferHeight, quality);

                float totalBarsWidth = barCount * barWidth + (barCount - 1) * barSpacing;
                float startX = (info.Width - totalBarsWidth) / 2;

                SKRect destRect = new(
                    left: Math.Max(startX, 0),
                    top: 0,
                    right: Math.Min(startX + totalBarsWidth, info.Width),
                    bottom: info.Height);

                if (canvas.QuickReject(destRect)) return;

                SKFilterQuality filterQuality = barCount > HIGH_BAR_COUNT_THRESHOLD
                    ? SKFilterQuality.High
                    : (barCount < LOW_BAR_COUNT_THRESHOLD ? SKFilterQuality.Low : SKFilterQuality.Medium);

                using var renderPaint = new SKPaint
                {
                    FilterQuality = filterQuality,
                    IsAntialias = barCount > HIGH_BAR_COUNT_THRESHOLD
                };

                canvas.Clear(SKColors.Black);
                canvas.DrawBitmap(_waterfallBitmap, destRect, renderPaint);

                if (barWidth > ZOOM_THRESHOLD)
                {
                    ApplyZoomEnhancement(canvas, destRect, barWidth, barCount, barSpacing);
                }

                drawPerformanceInfo?.Invoke(canvas, info);
            }
            catch (ObjectDisposedException ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Resource already disposed: {ex.Message}");
            }
            catch (ArgumentException ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Invalid argument: {ex.Message}");
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Render error: {ex.Message}");
            }
            finally
            {
                if (semaphoreAcquired)
                    _spectrumSemaphore.Release();
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            _spectrumSemaphore.Dispose();
            _waterfallBitmap?.Dispose();
            _spectrogramBuffer = null;
            _currentSpectrum = null;
            _isInitialized = false;

            _spectrumPool.Clear();

            GC.SuppressFinalize(this);
        }
        #endregion

        #region Private Methods
        private static int[] InitColorPalette()
        {
            int[] palette = new int[COLOR_PALETTE_SIZE];

            for (int i = 0; i < COLOR_PALETTE_SIZE; i++)
            {
                float normalized = i / (float)(COLOR_PALETTE_SIZE - 1);
                SKColor color = GetSpectrogramColor(normalized);
                palette[i] = (color.Alpha << 24) | (color.Red << 16) | (color.Green << 8) | color.Blue;
            }

            return palette;
        }

        private static SKColor GetSpectrogramColor(float normalized)
        {
            normalized = Math.Clamp(normalized, 0f, 1f);

            if (normalized < BLUE_THRESHOLD)
            {
                float t = normalized / BLUE_THRESHOLD;
                return new SKColor(0, (byte)(t * 50), (byte)(50 + t * 205), 255);
            }
            else if (normalized < CYAN_THRESHOLD)
            {
                float t = (normalized - BLUE_THRESHOLD) / (CYAN_THRESHOLD - BLUE_THRESHOLD);
                return new SKColor(0, (byte)(50 + t * 205), 255, 255);
            }
            else if (normalized < YELLOW_THRESHOLD)
            {
                float t = (normalized - CYAN_THRESHOLD) / (YELLOW_THRESHOLD - CYAN_THRESHOLD);
                return new SKColor((byte)(t * 255), 255, (byte)(255 - t * 255), 255);
            }
            else
            {
                float t = (normalized - YELLOW_THRESHOLD) / (1f - YELLOW_THRESHOLD);
                return new SKColor(255, (byte)(255 - t * 255), 0, 255);
            }
        }

        private bool CheckParametersChanged(float barWidth, float barSpacing, int barCount)
        {
            bool changed = Math.Abs(_lastBarWidth - barWidth) > 0.1f ||
                           Math.Abs(_lastBarSpacing - barSpacing) > 0.1f ||
                           _lastBarCount != barCount;

            if (changed)
            {
                _lastBarWidth = barWidth;
                _lastBarSpacing = barSpacing;
                _lastBarCount = barCount;
                _needsBufferResize = true;
            }

            return changed;
        }

        private void ResizeBufferForBarParameters(int barCount)
        {
            if (_spectrogramBuffer == null) return;

            int bufferHeight = _spectrogramBuffer.Length;
            int newWidth = CalculateOptimalSpectrumWidth(barCount);

            if (_spectrogramBuffer[0].Length != newWidth)
            {
                InitializeSpectrogramBuffer(bufferHeight, newWidth);
            }
        }

        private int CalculateOptimalSpectrumWidth(int barCount)
        {
            int baseWidth = Math.Max(barCount, MIN_BAR_COUNT);
            int width = 1;
            while (width < baseWidth)
            {
                width *= 2;
            }

            return Math.Max(width, DEFAULT_SPECTRUM_WIDTH / 4);
        }

        private float[] AdjustSpectrumResolution(float[] spectrum, int barCount, float barWidth)
        {
            if (spectrum == null || barCount <= 0)
                return spectrum ?? Array.Empty<float>();

            if (spectrum.Length == barCount) return spectrum;

            float[] result = _spectrumPool.Get(barCount);

            float detailLevel = Math.Max(0.1f, Math.Min(1.0f, barWidth * DETAIL_SCALE_FACTOR));
            float ratio = (float)spectrum.Length / barCount;

            if (ratio > 1.0f)
            {
                Parallel.For(0, barCount, i =>
                {
                    int startIdx = (int)(i * ratio);
                    int endIdx = Math.Min((int)((i + 1) * ratio), spectrum.Length);

                    float sum = 0;
                    float peak = 0;
                    int count = endIdx - startIdx;

                    if (count <= 0) return;

                    if (Vector.IsHardwareAccelerated && count >= Vector<float>.Count)
                    {
                        int vectorSize = Vector<float>.Count;
                        int vectorCount = count / vectorSize;

                        for (int v = 0; v < vectorCount; v++)
                        {
                            Vector<float> values = new Vector<float>(spectrum, startIdx + v * vectorSize);
                            sum += VectorSum(values);
                            peak = Math.Max(peak, VectorMax(values));
                        }

                        for (int j = startIdx + vectorCount * vectorSize; j < endIdx; j++)
                        {
                            sum += spectrum[j];
                            peak = Math.Max(peak, spectrum[j]);
                        }
                    }
                    else
                    {
                        for (int j = startIdx; j < endIdx; j++)
                        {
                            sum += spectrum[j];
                            peak = Math.Max(peak, spectrum[j]);
                        }
                    }

                    result[i] = (sum / count) * (1 - detailLevel) + peak * detailLevel;
                });
            }
            else
            {
                for (int i = 0; i < barCount; i++)
                {
                    float exactIdx = i * ratio;
                    int idx1 = (int)exactIdx;
                    int idx2 = Math.Min(idx1 + 1, spectrum.Length - 1);
                    float frac = exactIdx - idx1;

                    result[i] = spectrum[idx1] * (1 - frac) + spectrum[idx2] * frac;
                }
            }

            return result;
        }

        private enum RenderQuality { Low, Medium, High }

        private RenderQuality CalculateRenderQuality(int barCount)
        {
            if (barCount > HIGH_BAR_COUNT_THRESHOLD) return RenderQuality.High;
            if (barCount < LOW_BAR_COUNT_THRESHOLD) return RenderQuality.Low;
            return RenderQuality.Medium;
        }

        private void ApplyZoomEnhancement(SKCanvas canvas, SKRect destRect, float barWidth, int barCount, float barSpacing)
        {
            if (canvas == null || _disposed) return;

            if (barWidth < ZOOM_THRESHOLD) return;

            using var paint = new SKPaint
            {
                Color = new SKColor(255, 255, 255, 40),
                StrokeWidth = 1,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke
            };

            int gridStep = Math.Max(1, barCount / 10);

            using (var path = new SKPath())
            {
                for (int i = 0; i < barCount; i += gridStep)
                {
                    float x = destRect.Left + i * (barWidth + barSpacing) + barWidth / 2;
                    path.MoveTo(x, destRect.Top);
                    path.LineTo(x, destRect.Bottom);
                }
                canvas.DrawPath(path, paint);
            }

            if (barWidth > ZOOM_THRESHOLD * 1.5f)
            {
                using var textPaint = new SKPaint
                {
                    Color = SKColors.White,
                    TextSize = 10,
                    IsAntialias = true
                };

                for (int i = 0; i < barCount; i += gridStep * 2)
                {
                    float x = destRect.Left + i * (barWidth + barSpacing) + barWidth / 2;
                    string marker = $"{i}";
                    float textWidth = textPaint.MeasureText(marker);
                    canvas.DrawText(marker, x - textWidth / 2, destRect.Bottom - 5, textPaint);
                }
            }
        }

        private void InitializeSpectrogramBuffer(int bufferHeight, int spectrumWidth)
        {
            if (bufferHeight <= 0 || spectrumWidth <= 0)
            {
                throw new ArgumentException("Buffer dimensions must be positive");
            }

            if (_spectrogramBuffer == null ||
                _spectrogramBuffer.Length != bufferHeight ||
                _spectrogramBuffer[0].Length != spectrumWidth)
            {
                if (_spectrogramBuffer != null)
                {
                    for (int i = 0; i < _spectrogramBuffer.Length; i++)
                    {
                        _spectrogramBuffer[i] = null!;
                    }
                }

                _spectrogramBuffer = new float[bufferHeight][];

                if (bufferHeight > 32)
                {
                    Parallel.For(0, bufferHeight, i =>
                    {
                        _spectrogramBuffer[i] = new float[spectrumWidth];
                        Array.Fill(_spectrogramBuffer[i], MIN_SIGNAL);
                    });
                }
                else
                {
                    for (int i = 0; i < bufferHeight; i++)
                    {
                        _spectrogramBuffer[i] = new float[spectrumWidth];
                        Array.Fill(_spectrogramBuffer[i], MIN_SIGNAL);
                    }
                }

                _bufferHead = 0;
            }
        }

        private void UpdateSpectrogramBuffer(float[]? spectrum)
        {
            if (_spectrogramBuffer == null || _disposed)
                return;

            int bufferHeight = _spectrogramBuffer.Length;
            int spectrumWidth = _spectrogramBuffer[0].Length;

            if (spectrum == null)
            {
                Array.Fill(_spectrogramBuffer[_bufferHead], MIN_SIGNAL);
            }
            else
            {
                if (_currentSpectrum == null || _currentSpectrum.Length != spectrum.Length)
                {
                    _currentSpectrum = new float[spectrum.Length];
                }

                Array.Copy(spectrum, _currentSpectrum, spectrum.Length);

                if (spectrum.Length == spectrumWidth)
                {
                    Array.Copy(spectrum, _spectrogramBuffer[_bufferHead], spectrumWidth);
                }
                else if (spectrum.Length > spectrumWidth)
                {
                    float ratio = (float)spectrum.Length / spectrumWidth;

                    if (Vector.IsHardwareAccelerated && spectrum.Length >= Vector<float>.Count)
                    {
                        Parallel.For(0, spectrumWidth, i =>
                        {
                            int startIdx = (int)(i * ratio);
                            int endIdx = Math.Min((int)((i + 1) * ratio), spectrum.Length);

                            if (endIdx - startIdx >= Vector<float>.Count)
                            {
                                int vectorSize = Vector<float>.Count;
                                int vectorizedLength = ((endIdx - startIdx) / vectorSize) * vectorSize;

                                float maxVal = MIN_SIGNAL;
                                for (int j = startIdx; j < startIdx + vectorizedLength; j += vectorSize)
                                {
                                    Vector<float> values = new Vector<float>(spectrum, j);
                                    maxVal = Math.Max(maxVal, VectorMax(values));
                                }

                                for (int j = startIdx + vectorizedLength; j < endIdx; j++)
                                {
                                    maxVal = Math.Max(maxVal, spectrum[j]);
                                }

                                _spectrogramBuffer[_bufferHead][i] = maxVal;
                            }
                            else
                            {
                                float maxVal = MIN_SIGNAL;
                                for (int j = startIdx; j < endIdx; j++)
                                {
                                    maxVal = Math.Max(maxVal, spectrum[j]);
                                }
                                _spectrogramBuffer[_bufferHead][i] = maxVal;
                            }
                        });
                    }
                    else
                    {
                        for (int i = 0; i < spectrumWidth; i++)
                        {
                            int startIdx = (int)(i * ratio);
                            int endIdx = Math.Min((int)((i + 1) * ratio), spectrum.Length);
                            float maxVal = MIN_SIGNAL;

                            for (int j = startIdx; j < endIdx; j++)
                            {
                                maxVal = Math.Max(maxVal, spectrum[j]);
                            }

                            _spectrogramBuffer[_bufferHead][i] = maxVal;
                        }
                    }
                }
                else
                {
                    float ratio = (float)spectrum.Length / spectrumWidth;
                    for (int i = 0; i < spectrumWidth; i++)
                    {
                        float exactIdx = i * ratio;
                        int idx1 = (int)exactIdx;
                        int idx2 = Math.Min(idx1 + 1, spectrum.Length - 1);
                        float frac = exactIdx - idx1;

                        _spectrogramBuffer[_bufferHead][i] =
                            spectrum[idx1] * (1 - frac) + spectrum[idx2] * frac;
                    }
                }
            }

            _bufferHead = (_bufferHead + 1) % bufferHeight;
        }

        private bool ValidateRenderParams(
            SKCanvas canvas,
            SKImageInfo info,
            float barWidth,
            int barCount,
            float barSpacing)
        {
            if (_disposed)
            {
                SmartLogger.Log(LogLevel.Warning, LogPrefix, "Cannot render: instance is disposed");
                return false;
            }

            if (!_isInitialized)
            {
                Initialize();
                return false;
            }

            if (info.Width <= 0 || info.Height <= 0)
            {
                SmartLogger.Log(LogLevel.Warning, LogPrefix, "Invalid canvas dimensions");
                return false;
            }

            if (barCount <= 0)
            {
                SmartLogger.Log(LogLevel.Warning, LogPrefix, "Bar count must be positive");
                return false;
            }

            if (barWidth <= 0)
            {
                SmartLogger.Log(LogLevel.Warning, LogPrefix, "Bar width must be positive");
                return false;
            }

            if (barSpacing < 0)
            {
                SmartLogger.Log(LogLevel.Warning, LogPrefix, "Bar spacing cannot be negative");
                return false;
            }

            return true;
        }

        private void UpdateBitmapSafe(
            SKBitmap bitmap,
            float[][] spectrogramBuffer,
            int bufferHead,
            int spectrumWidth,
            int bufferHeight,
            RenderQuality quality)
        {
            if (bitmap == null || spectrogramBuffer == null || _disposed)
                return;

            IntPtr pixelsPtr = bitmap.GetPixels();
            if (pixelsPtr == IntPtr.Zero)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Invalid bitmap pixels pointer");
                return;
            }

            int[] pixels = ArrayPool<int>.Shared.Rent(spectrumWidth * bufferHeight);

            try
            {
                float colorEnhancement = quality switch
                {
                    RenderQuality.High => 1.2f,
                    RenderQuality.Low => 0.9f,
                    _ => 1.0f
                };

                if (Vector.IsHardwareAccelerated && spectrumWidth >= Vector<float>.Count)
                {
                    Parallel.For(0, bufferHeight, y =>
                    {
                        int bufferIndex = (bufferHead + 1 + y) % bufferHeight;
                        int rowOffset = y * spectrumWidth;
                        int vectorizedWidth = spectrumWidth - (spectrumWidth % Vector<float>.Count);

                        for (int x = 0; x < vectorizedWidth; x += Vector<float>.Count)
                        {
                            Vector<float> values = new Vector<float>(spectrogramBuffer[bufferIndex], x);

                            if (Math.Abs(colorEnhancement - 1.0f) > float.Epsilon)
                            {
                                values = Vector.Multiply(values, colorEnhancement);
                            }

                            for (int i = 0; i < Vector<float>.Count; i++)
                            {
                                float normalized = Math.Clamp(values[i], 0f, 1f);
                                int paletteIndex = (int)(normalized * (COLOR_PALETTE_SIZE - 1));
                                pixels[rowOffset + x + i] = _colorPalette[paletteIndex];
                            }
                        }

                        for (int x = vectorizedWidth; x < spectrumWidth; x++)
                        {
                            float value = spectrogramBuffer[bufferIndex][x];
                            if (Math.Abs(colorEnhancement - 1.0f) > float.Epsilon)
                            {
                                value *= colorEnhancement;
                            }
                            float normalized = Math.Clamp(value, 0f, 1f);
                            int paletteIndex = (int)(normalized * (COLOR_PALETTE_SIZE - 1));
                            pixels[rowOffset + x] = _colorPalette[paletteIndex];
                        }
                    });
                }
                else
                {
                    Parallel.For(0, bufferHeight, y =>
                    {
                        int bufferIndex = (bufferHead + 1 + y) % bufferHeight;
                        int rowOffset = y * spectrumWidth;

                        for (int x = 0; x < spectrumWidth; x++)
                        {
                            float value = spectrogramBuffer[bufferIndex][x];
                            if (Math.Abs(colorEnhancement - 1.0f) > float.Epsilon)
                            {
                                value *= colorEnhancement;
                            }
                            float normalized = Math.Clamp(value, 0f, 1f);
                            int paletteIndex = (int)(normalized * (COLOR_PALETTE_SIZE - 1));
                            pixels[rowOffset + x] = _colorPalette[paletteIndex];
                        }
                    });
                }

                Marshal.Copy(pixels, 0, pixelsPtr, spectrumWidth * bufferHeight);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Bitmap update error: {ex.Message}");
            }
            finally
            {
                ArrayPool<int>.Shared.Return(pixels);
            }
        }

        private static float VectorSum(Vector<float> vector)
        {
            float sum = 0;
            for (int i = 0; i < Vector<float>.Count; i++)
            {
                sum += vector[i];
            }
            return sum;
        }

        private static float VectorMax(Vector<float> vector)
        {
            float max = float.MinValue;
            for (int i = 0; i < Vector<float>.Count; i++)
            {
                max = Math.Max(max, vector[i]);
            }
            return max;
        }
        #endregion

        #region Object Pool Implementation
        private class ObjectPool<T>
        {
            private readonly ConcurrentBag<T> _objects;
            private readonly Func<T> _objectGenerator;
            private readonly Action<T> _objectReset;

            public ObjectPool(Func<T> objectGenerator, Action<T> objectReset, int initialCount = 0)
            {
                _objectGenerator = objectGenerator ?? throw new ArgumentNullException(nameof(objectGenerator));
                _objectReset = objectReset ?? throw new ArgumentNullException(nameof(objectReset));
                _objects = new ConcurrentBag<T>();

                for (int i = 0; i < initialCount; i++)
                {
                    _objects.Add(_objectGenerator());
                }
            }

            public T Get(int size = 0)
            {
                if (_objects.TryTake(out T? item))
                {
                    return item;
                }
                return _objectGenerator();
            }

            public void Return(T item)
            {
                _objectReset(item);
                _objects.Add(item);
            }

            public void Clear()
            {
                while (_objects.TryTake(out _)) { }
            }
        }
        #endregion
    }
}