#nullable enable

namespace SpectrumNet
{
    public class CircularBarsRenderer : ISpectrumRenderer, IDisposable
    {
        #region Constants
        private static class Constants
        {
            public const float RADIUS_PROPORTION = 0.8f;     // Доля радиуса визуализации относительно размера холста
            public const float INNER_RADIUS_FACTOR = 0.9f;     // Доля основного радиуса для внутреннего круга
            public const float MIN_STROKE_WIDTH = 2f;       // Минимальная ширина линий баров
            public const float SPECTRUM_MULTIPLIER = 0.5f;     // Усиление значений спектра для визуального эффекта
            public const float SMOOTHING_FACTOR = 0.3f;     // Фактор сглаживания (0-1, выше = быстрее реакция)
            public const float MIN_MAGNITUDE_THRESHOLD = 0.01f;    // Минимальный порог величины для отрисовки бара
            public const float MAX_BAR_HEIGHT = 1.5f;     // Максимальный множитель высоты баров
            public const float MIN_BAR_HEIGHT = 0.01f;    // Минимальный множитель высоты баров
            public const float GLOW_RADIUS = 3f;       // Радиус размытия для эффекта свечения
            public const float HIGHLIGHT_ALPHA = 0.7f;     // Альфа-значение для эффекта подсветки
            public const float GLOW_INTENSITY = 0.4f;     // Интенсивность свечения
            public const float BAR_SPACING_FACTOR = 0.7f;     // Фактор расстояния между барами
            public const float HIGHLIGHT_POSITION = 0.7f;     // Позиция подсветки (0-1)
            public const float HIGHLIGHT_INTENSITY = 0.5f;     // Интенсивность подсветки
            public const byte INNER_CIRCLE_ALPHA = 80;       // Альфа внутреннего круга (0-255)
            public const int PARALLEL_BATCH_SIZE = 32;       // Размер пакета для параллельной обработки
            public const float GLOW_THRESHOLD = 0.6f;     // Порог для эффекта свечения
            public const float HIGHLIGHT_THRESHOLD = 0.4f;     // Порог для эффекта подсветки
        }
        #endregion

        #region Fields
        private static CircularBarsRenderer? _instance;
        private bool _isInitialized;
        private readonly SKPath _pathPool = new();
        private readonly SKPathPool _barPathPool = new(8);
        private Vector2[]? _barVectors;
        private float[]? _previousSpectrum;
        private float[]? _processedSpectrum;
        private readonly SemaphoreSlim _spectrumSemaphore = new(1, 1);
        private readonly object _spectrumLock = new();
        private volatile bool _disposed;
        private readonly SKPaint _barPaint = new();
        private readonly SKPaint _highlightPaint = new();
        private readonly SKPaint _glowPaint = new();
        private readonly SKPaint _innerCirclePaint = new();

        // Настройки качества отрисовки
        private RenderQuality _quality = RenderQuality.Medium;
        private bool _useAntiAlias = true;
        private SKFilterQuality _filterQuality = SKFilterQuality.Medium;
        private bool _useAdvancedEffects = true;
        #endregion

        #region Constructor and Initialization
        private CircularBarsRenderer()
        {
            InitializePaints();
        }

        private void InitializePaints()
        {
            _barPaint.Style = SKPaintStyle.Stroke;
            _barPaint.StrokeCap = SKStrokeCap.Round;

            _highlightPaint.Style = SKPaintStyle.Stroke;
            _highlightPaint.Color = SKColors.White.WithAlpha((byte)(255 * Constants.HIGHLIGHT_ALPHA));

            _glowPaint.Style = SKPaintStyle.Stroke;
            _glowPaint.ImageFilter = SKImageFilter.CreateBlur(Constants.GLOW_RADIUS, Constants.GLOW_RADIUS);

            _innerCirclePaint.Style = SKPaintStyle.Stroke;
        }

        public static CircularBarsRenderer GetInstance() => _instance ??= new CircularBarsRenderer();

        public void Initialize()
        {
            if (_isInitialized) return;
            _isInitialized = true;
            Log.Debug("[CircularBarsRenderer] CircularBarsRenderer initialized");
        }

        public void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium)
        {
            Quality = quality;
        }

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

        #region Public Rendering
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
            if (!ValidateRenderParameters(canvas, spectrum, info, basePaint))
                return;

            float[] renderSpectrum;
            bool semaphoreAcquired = false;
            int spectrumLength = spectrum!.Length;
            int actualBarCount = Math.Min(spectrumLength, barCount);

            try
            {
                semaphoreAcquired = _spectrumSemaphore.Wait(0);
                if (semaphoreAcquired)
                {
                    ProcessSpectrumAsync(spectrum, actualBarCount, spectrumLength).ConfigureAwait(false);
                }

                lock (_spectrumLock)
                {
                    renderSpectrum = _processedSpectrum ??
                                    ProcessSynchronously(spectrum, actualBarCount, spectrumLength);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[CircularBarsRenderer] Error processing spectrum: {ex.Message}");
                return;
            }
            finally
            {
                if (semaphoreAcquired)
                {
                    _spectrumSemaphore.Release();
                }
            }

            float centerX = info.Width / 2;
            float centerY = info.Height / 2;
            float mainRadius = Math.Min(centerX, centerY) * Constants.RADIUS_PROPORTION;
            float adjustedBarWidth = AdjustBarWidthForBarCount(barWidth, actualBarCount, Math.Min(info.Width, info.Height));

            UpdatePaintsFromBase(basePaint!, adjustedBarWidth);

            if (canvas!.QuickReject(new SKRect(centerX - mainRadius, centerY - mainRadius,
                                              centerX + mainRadius, centerY + mainRadius)))
            {
                drawPerformanceInfo?.Invoke(canvas, info);
                return;
            }

            RenderCircularBars(canvas, renderSpectrum, actualBarCount, centerX, centerY, mainRadius, adjustedBarWidth);
            drawPerformanceInfo?.Invoke(canvas, info);
        }

        private float AdjustBarWidthForBarCount(float barWidth, int barCount, float minDimension)
        {
            float circumference = (float)(2 * Math.PI * Constants.RADIUS_PROPORTION * minDimension / 2);
            float maxWidth = circumference / barCount * Constants.BAR_SPACING_FACTOR;
            return Math.Max(Math.Min(barWidth, maxWidth), Constants.MIN_STROKE_WIDTH);
        }
        #endregion

        #region Validation and Processing
        private bool ValidateRenderParameters(
            SKCanvas? canvas,
            float[]? spectrum,
            SKImageInfo info,
            SKPaint? basePaint)
        {
            if (!_isInitialized)
            {
                Log.Error("[CircularBarsRenderer] CircularBarsRenderer not initialized before rendering");
                return false;
            }

            if (canvas == null ||
                spectrum == null || spectrum.Length < 2 ||
                basePaint == null ||
                info.Width <= 0 || info.Height <= 0)
            {
                Log.Error("[CircularBarsRenderer] Invalid render parameters for CircularBarsRenderer");
                return false;
            }

            return true;
        }

        private async Task ProcessSpectrumAsync(float[] spectrum, int targetCount, int spectrumLength)
        {
            await Task.Run(() =>
            {
                float[] downscaledSpectrum = ScaleSpectrum(spectrum, targetCount, spectrumLength);
                _processedSpectrum = SmoothSpectrum(downscaledSpectrum, targetCount);
                EnsureBarVectors(targetCount);
            }).ConfigureAwait(false);
        }

        private float[] ProcessSynchronously(float[] spectrum, int targetCount, int spectrumLength)
        {
            float[] downscaledSpectrum = ScaleSpectrum(spectrum, targetCount, spectrumLength);
            return SmoothSpectrum(downscaledSpectrum, targetCount);
        }

        private static float[] ScaleSpectrum(float[] spectrum, int targetCount, int spectrumLength)
        {
            float[] scaledSpectrum = new float[targetCount];
            float blockSize = (float)spectrumLength / targetCount;

            if (targetCount >= Constants.PARALLEL_BATCH_SIZE && Vector.IsHardwareAccelerated)
            {
                Parallel.For(0, targetCount, i =>
                {
                    int start = (int)(i * blockSize);
                    int end = (int)((i + 1) * blockSize);
                    end = Math.Min(end, spectrumLength);
                    float sum = 0;
                    for (int j = start; j < end; j++)
                        sum += spectrum[j];
                    scaledSpectrum[i] = sum / (end - start);
                });
            }
            else
            {
                for (int i = 0; i < targetCount; i++)
                {
                    int start = (int)(i * blockSize);
                    int end = (int)((i + 1) * blockSize);
                    end = Math.Min(end, spectrumLength);
                    float sum = 0;
                    for (int j = start; j < end; j++)
                        sum += spectrum[j];
                    scaledSpectrum[i] = sum / (end - start);
                }
            }

            return scaledSpectrum;
        }

        private float[] SmoothSpectrum(float[] spectrum, int targetCount)
        {
            if (_previousSpectrum == null || _previousSpectrum.Length != targetCount)
                _previousSpectrum = new float[targetCount];

            var scaledSpectrum = new float[targetCount];

            if (targetCount >= Vector<float>.Count && Vector.IsHardwareAccelerated)
            {
                int vectorSize = Vector<float>.Count;
                int vectorizedLength = targetCount - (targetCount % vectorSize);
                for (int i = 0; i < vectorizedLength; i += vectorSize)
                {
                    Vector<float> currentValues = new Vector<float>(spectrum, i);
                    Vector<float> previousValues = new Vector<float>(_previousSpectrum!, i);
                    Vector<float> smoothedValues = previousValues + (currentValues - previousValues) * Constants.SMOOTHING_FACTOR;
                    Vector<float> minVector = new Vector<float>(Constants.MIN_BAR_HEIGHT);
                    Vector<float> maxVector = new Vector<float>(Constants.MAX_BAR_HEIGHT);
                    smoothedValues = Vector.Min(Vector.Max(smoothedValues, minVector), maxVector);
                    smoothedValues.CopyTo(scaledSpectrum, i);
                    smoothedValues.CopyTo(_previousSpectrum!, i);
                }

                for (int i = vectorizedLength; i < targetCount; i++)
                {
                    ProcessSingleSpectrumValue(spectrum, scaledSpectrum, i);
                }
            }
            else
            {
                for (int i = 0; i < targetCount; i++)
                {
                    ProcessSingleSpectrumValue(spectrum, scaledSpectrum, i);
                }
            }

            return scaledSpectrum;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ProcessSingleSpectrumValue(float[] spectrum, float[] scaledSpectrum, int i)
        {
            if (_previousSpectrum == null) return;
            float currentValue = spectrum[i];
            float smoothedValue = _previousSpectrum[i] + (currentValue - _previousSpectrum[i]) * Constants.SMOOTHING_FACTOR;
            scaledSpectrum[i] = Math.Clamp(smoothedValue, Constants.MIN_BAR_HEIGHT, Constants.MAX_BAR_HEIGHT);
            _previousSpectrum[i] = scaledSpectrum[i];
        }

        private void EnsureBarVectors(int barCount)
        {
            if (_barVectors == null || _barVectors.Length != barCount)
            {
                _barVectors = new Vector2[barCount];
                float angleStep = (float)(2 * Math.PI / barCount);
                for (int i = 0; i < barCount; i++)
                {
                    float angle = angleStep * i;
                    _barVectors[i] = new Vector2(
                        (float)Math.Cos(angle),
                        (float)Math.Sin(angle)
                    );
                }
            }
        }

        private void UpdatePaintsFromBase(SKPaint basePaint, float barWidth)
        {
            _barPaint.IsAntialias = _useAntiAlias;
            _barPaint.FilterQuality = _filterQuality;
            _barPaint.Color = basePaint.Color;
            _barPaint.StrokeWidth = barWidth;

            _highlightPaint.IsAntialias = _useAntiAlias;
            _highlightPaint.FilterQuality = _filterQuality;
            _highlightPaint.StrokeWidth = barWidth * 0.6f;

            _glowPaint.IsAntialias = _useAntiAlias;
            _glowPaint.FilterQuality = _filterQuality;
            _glowPaint.Color = basePaint.Color;
            _glowPaint.StrokeWidth = barWidth * 1.2f;

            _innerCirclePaint.IsAntialias = _useAntiAlias;
            _innerCirclePaint.FilterQuality = _filterQuality;
            _innerCirclePaint.Color = basePaint.Color.WithAlpha(Constants.INNER_CIRCLE_ALPHA);
            _innerCirclePaint.StrokeWidth = barWidth * 0.5f;
        }
        #endregion

        #region Rendering Methods
        private void RenderCircularBars(
            SKCanvas canvas,
            float[] spectrum,
            int barCount,
            float centerX,
            float centerY,
            float mainRadius,
            float barWidth)
        {
            canvas.DrawCircle(centerX, centerY, mainRadius * Constants.INNER_RADIUS_FACTOR, _innerCirclePaint);
            EnsureBarVectors(barCount);

            if (_useAdvancedEffects)
            {
                RenderGlowEffects(canvas, spectrum, barCount, centerX, centerY, mainRadius);
            }

            RenderMainBars(canvas, spectrum, barCount, centerX, centerY, mainRadius);

            if (_useAdvancedEffects)
            {
                RenderHighlights(canvas, spectrum, barCount, centerX, centerY, mainRadius);
            }
        }

        private void RenderGlowEffects(
            SKCanvas canvas,
            float[] spectrum,
            int barCount,
            float centerX,
            float centerY,
            float mainRadius)
        {
            using var batchPath = new SKPath();
            for (int i = 0; i < barCount; i++)
            {
                float magnitude = spectrum[i];
                if (magnitude <= Constants.GLOW_THRESHOLD) continue;
                float radius = mainRadius + magnitude * mainRadius * Constants.SPECTRUM_MULTIPLIER;
                var path = _barPathPool.Get();
                AddBarToPath(path, i, centerX, centerY, mainRadius, radius);
                batchPath.AddPath(path);
                _barPathPool.Return(path);
            }

            if (!batchPath.IsEmpty)
            {
                _glowPaint.Color = _glowPaint.Color.WithAlpha((byte)(255 * Constants.GLOW_INTENSITY));
                canvas.DrawPath(batchPath, _glowPaint);
            }
        }

        private void RenderMainBars(
            SKCanvas canvas,
            float[] spectrum,
            int barCount,
            float centerX,
            float centerY,
            float mainRadius)
        {
            using var batchPath = new SKPath();
            for (int i = 0; i < barCount; i++)
            {
                float magnitude = spectrum[i];
                if (magnitude < Constants.MIN_MAGNITUDE_THRESHOLD) continue;
                float radius = mainRadius + magnitude * mainRadius * Constants.SPECTRUM_MULTIPLIER;
                var path = _barPathPool.Get();
                AddBarToPath(path, i, centerX, centerY, mainRadius, radius);
                batchPath.AddPath(path);
                _barPathPool.Return(path);
            }

            if (!batchPath.IsEmpty)
            {
                canvas.DrawPath(batchPath, _barPaint);
            }
        }

        private void RenderHighlights(
            SKCanvas canvas,
            float[] spectrum,
            int barCount,
            float centerX,
            float centerY,
            float mainRadius)
        {
            using var batchPath = new SKPath();
            for (int i = 0; i < barCount; i++)
            {
                float magnitude = spectrum[i];
                if (magnitude <= Constants.HIGHLIGHT_THRESHOLD) continue;
                float radius = mainRadius + magnitude * mainRadius * Constants.SPECTRUM_MULTIPLIER;
                float innerPoint = mainRadius + (radius - mainRadius) * Constants.HIGHLIGHT_POSITION;
                var path = _barPathPool.Get();
                AddBarToPath(path, i, centerX, centerY, innerPoint, radius);
                batchPath.AddPath(path);
                _barPathPool.Return(path);
            }

            if (!batchPath.IsEmpty)
            {
                _highlightPaint.Color = _highlightPaint.Color.WithAlpha((byte)(255 * Constants.HIGHLIGHT_INTENSITY));
                canvas.DrawPath(batchPath, _highlightPaint);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddBarToPath(SKPath path, int index, float centerX, float centerY, float innerRadius, float outerRadius)
        {
            if (_barVectors == null) return;
            Vector2 vector = _barVectors[index];
            path.MoveTo(centerX + innerRadius * vector.X, centerY + innerRadius * vector.Y);
            path.LineTo(centerX + outerRadius * vector.X, centerY + outerRadius * vector.Y);
        }
        #endregion

        #region Path Pooling
        private class SKPathPool : IDisposable
        {
            private readonly SKPath[] _paths;
            private readonly bool[] _inUse;
            private int _index = 0;
            private bool _disposed = false;

            public SKPathPool(int capacity)
            {
                _paths = new SKPath[capacity];
                _inUse = new bool[capacity];
                for (int i = 0; i < capacity; i++)
                {
                    _paths[i] = new SKPath();
                }
            }

            public SKPath Get()
            {
                lock (this)
                {
                    for (int i = 0; i < _paths.Length; i++)
                    {
                        int idx = (_index + i) % _paths.Length;
                        if (!_inUse[idx])
                        {
                            _inUse[idx] = true;
                            _index = (idx + 1) % _paths.Length;
                            _paths[idx].Reset();
                            return _paths[idx];
                        }
                    }
                    return new SKPath();
                }
            }

            public void Return(SKPath path)
            {
                if (_disposed) return;
                lock (this)
                {
                    for (int i = 0; i < _paths.Length; i++)
                    {
                        if (_paths[i] == path)
                        {
                            _inUse[i] = false;
                            break;
                        }
                    }
                }
            }

            public void Dispose()
            {
                if (_disposed) return;
                foreach (var path in _paths)
                {
                    path.Dispose();
                }
                _disposed = true;
            }
        }
        #endregion

        #region Disposal
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing)
            {
                _pathPool.Dispose();
                _barPathPool.Dispose();
                _spectrumSemaphore.Dispose();
                _barPaint.Dispose();
                _highlightPaint.Dispose();
                _glowPaint.Dispose();
                _innerCirclePaint.Dispose();
                _barVectors = null;
                _previousSpectrum = null;
                _processedSpectrum = null;
            }
            _disposed = true;
            Log.Debug("[CircularBarsRenderer] CircularBarsRenderer disposed");
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion

        #region Quality Settings
        private void ApplyQualitySettings()
        {
            switch (_quality)
            {
                case RenderQuality.Low:
                    _useAntiAlias = false;
                    _filterQuality = SKFilterQuality.Low;
                    _useAdvancedEffects = false;
                    break;
                case RenderQuality.Medium:
                    _useAntiAlias = true;
                    _filterQuality = SKFilterQuality.Medium;
                    _useAdvancedEffects = true;
                    break;
                case RenderQuality.High:
                    _useAntiAlias = true;
                    _filterQuality = SKFilterQuality.High;
                    _useAdvancedEffects = true;
                    break;
            }

            UpdatePaintQuality(_barPaint);
            UpdatePaintQuality(_highlightPaint);
            UpdatePaintQuality(_glowPaint);
            UpdatePaintQuality(_innerCirclePaint);
        }

        private void UpdatePaintQuality(SKPaint paint)
        {
            paint.IsAntialias = _useAntiAlias;
            paint.FilterQuality = _filterQuality;
        }
        #endregion
    }
}