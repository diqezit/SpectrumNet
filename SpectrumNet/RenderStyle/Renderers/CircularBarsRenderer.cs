#nullable enable

namespace SpectrumNet
{
    public class CircularBarsRenderer : BaseSpectrumRenderer
    {
        #region Constants
        private static class Constants
        {
            public const string LOG_PREFIX = "CircularBarsRenderer";
            public const float RADIUS_PROPORTION = 0.8f;      // Доля радиуса визуализации относительно размера холста
            public const float INNER_RADIUS_FACTOR = 0.9f;    // Доля основного радиуса для внутреннего круга
            public const float MIN_STROKE_WIDTH = 2f;         // Минимальная ширина линий баров
            public const float SPECTRUM_MULTIPLIER = 0.5f;    // Усиление значений спектра для визуального эффекта
            public const float MIN_MAGNITUDE_THRESHOLD = 0.01f; // Минимальный порог величины для отрисовки бара
            public const float MAX_BAR_HEIGHT = 1.5f;         // Максимальный множитель высоты баров
            public const float MIN_BAR_HEIGHT = 0.01f;        // Минимальный множитель высоты баров
            public const float GLOW_RADIUS = 3f;              // Радиус размытия для эффекта свечения
            public const float HIGHLIGHT_ALPHA = 0.7f;        // Альфа-значение для эффекта подсветки
            public const float GLOW_INTENSITY = 0.4f;         // Интенсивность свечения
            public const float BAR_SPACING_FACTOR = 0.7f;     // Фактор расстояния между барами
            public const float HIGHLIGHT_POSITION = 0.7f;     // Позиция подсветки (0-1)
            public const float HIGHLIGHT_INTENSITY = 0.5f;    // Интенсивность подсветки
            public const byte INNER_CIRCLE_ALPHA = 80;        // Альфа внутреннего круга (0-255)
            public const int PARALLEL_BATCH_SIZE = 32;        // Размер пакета для параллельной обработки
            public const float GLOW_THRESHOLD = 0.6f;         // Порог для эффекта свечения
            public const float HIGHLIGHT_THRESHOLD = 0.4f;    // Порог для эффекта подсветки
        }
        #endregion

        #region Fields
        private static CircularBarsRenderer? _instance;
        private readonly SKPath _pathPool = new();
        private readonly SKPathPool _barPathPool = new(8);
        private Vector2[]? _barVectors;
        private int _previousBarCount;
        #endregion

        #region Constructor and Initialization
        private CircularBarsRenderer() { }

        public static CircularBarsRenderer GetInstance() => _instance ??= new CircularBarsRenderer();

        public override void Initialize()
        {
            if (!_isInitialized)
            {
                base.Initialize();
                _isInitialized = true;
                SmartLogger.Log(LogLevel.Debug, Constants.LOG_PREFIX, "CircularBarsRenderer initialized");
            }
        }
        #endregion

        #region Rendering
        public override void Render(
            SKCanvas? canvas,
            float[]? spectrum,
            SKImageInfo info,
            float barWidth,
            float barSpacing,
            int barCount,
            SKPaint? basePaint,
            Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
        {
            if (!ValidateRenderParameters(canvas, spectrum, info, basePaint, "CircularBarsRenderer"))
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
                    float[] scaledSpectrum = ScaleSpectrum(spectrum, actualBarCount, spectrumLength);
                    _processedSpectrum = SmoothSpectrum(scaledSpectrum, actualBarCount);
                    EnsureBarVectors(actualBarCount);
                }

                lock (_spectrumLock)
                {
                    renderSpectrum = _processedSpectrum ??
                                    ScaleSpectrum(spectrum, actualBarCount, spectrumLength);
                }
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, Constants.LOG_PREFIX, $"Error processing spectrum: {ex.Message}");
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

            // Проверка видимости области рендеринга
            if (canvas!.QuickReject(new SKRect(centerX - mainRadius, centerY - mainRadius,
                                              centerX + mainRadius, centerY + mainRadius)))
            {
                drawPerformanceInfo?.Invoke(canvas, info);
                return;
            }

            RenderCircularBars(canvas, renderSpectrum, actualBarCount, centerX, centerY, mainRadius, adjustedBarWidth, basePaint!);
            drawPerformanceInfo?.Invoke(canvas, info);
        }

        private float AdjustBarWidthForBarCount(float barWidth, int barCount, float minDimension)
        {
            float circumference = (float)(2 * Math.PI * Constants.RADIUS_PROPORTION * minDimension / 2);
            float maxWidth = circumference / barCount * Constants.BAR_SPACING_FACTOR;
            return Math.Max(Math.Min(barWidth, maxWidth), Constants.MIN_STROKE_WIDTH);
        }
        #endregion

        #region Bar Vectors
        private void EnsureBarVectors(int barCount)
        {
            if (_barVectors == null || _barVectors.Length != barCount || _previousBarCount != barCount)
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
                _previousBarCount = barCount;
            }
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
            float barWidth,
            SKPaint basePaint)
        {
            if (canvas == null || spectrum == null || basePaint == null || _disposed)
                return;

            // Рисуем внутренний круг
            using (var innerCirclePaint = new SKPaint
            {
                IsAntialias = _useAntiAlias,
                FilterQuality = _filterQuality,
                Style = SKPaintStyle.Stroke,
                Color = basePaint.Color.WithAlpha(Constants.INNER_CIRCLE_ALPHA),
                StrokeWidth = barWidth * 0.5f
            })
            {
                canvas.DrawCircle(centerX, centerY, mainRadius * Constants.INNER_RADIUS_FACTOR, innerCirclePaint);
            }

            EnsureBarVectors(barCount);

            if (_useAdvancedEffects)
            {
                RenderGlowEffects(canvas, spectrum, barCount, centerX, centerY, mainRadius, barWidth, basePaint);
            }

            RenderMainBars(canvas, spectrum, barCount, centerX, centerY, mainRadius, barWidth, basePaint);

            if (_useAdvancedEffects)
            {
                RenderHighlights(canvas, spectrum, barCount, centerX, centerY, mainRadius, barWidth, basePaint);
            }
        }

        private void RenderGlowEffects(
            SKCanvas canvas,
            float[] spectrum,
            int barCount,
            float centerX,
            float centerY,
            float mainRadius,
            float barWidth,
            SKPaint basePaint)
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
                using (var glowPaint = new SKPaint
                {
                    IsAntialias = _useAntiAlias,
                    FilterQuality = _filterQuality,
                    Style = SKPaintStyle.Stroke,
                    Color = basePaint.Color.WithAlpha((byte)(255 * Constants.GLOW_INTENSITY)),
                    StrokeWidth = barWidth * 1.2f,
                    MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Constants.GLOW_RADIUS)
                })
                {
                    canvas.DrawPath(batchPath, glowPaint);
                }
            }
        }

        private void RenderMainBars(
            SKCanvas canvas,
            float[] spectrum,
            int barCount,
            float centerX,
            float centerY,
            float mainRadius,
            float barWidth,
            SKPaint basePaint)
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
                using (var barPaint = new SKPaint
                {
                    IsAntialias = _useAntiAlias,
                    FilterQuality = _filterQuality,
                    Style = SKPaintStyle.Stroke,
                    StrokeCap = SKStrokeCap.Round,
                    Color = basePaint.Color,
                    StrokeWidth = barWidth
                })
                {
                    canvas.DrawPath(batchPath, barPaint);
                }
            }
        }

        private void RenderHighlights(
            SKCanvas canvas,
            float[] spectrum,
            int barCount,
            float centerX,
            float centerY,
            float mainRadius,
            float barWidth,
            SKPaint basePaint)
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
                using (var highlightPaint = new SKPaint
                {
                    IsAntialias = _useAntiAlias,
                    FilterQuality = _filterQuality,
                    Style = SKPaintStyle.Stroke,
                    Color = SKColors.White.WithAlpha((byte)(255 * Constants.HIGHLIGHT_INTENSITY)),
                    StrokeWidth = barWidth * 0.6f
                })
                {
                    canvas.DrawPath(batchPath, highlightPaint);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddBarToPath(SKPath path, int index, float centerX, float centerY, float innerRadius, float outerRadius)
        {
            if (_barVectors == null || path == null) return;
            Vector2 vector = _barVectors[index];
            path.MoveTo(centerX + innerRadius * vector.X, centerY + innerRadius * vector.Y);
            path.LineTo(centerX + outerRadius * vector.X, centerY + outerRadius * vector.Y);
        }
        #endregion

        #region Path Pooling
        private class SKPathPool : IDisposable
        {
            private readonly List<SKPath> _paths;
            private readonly HashSet<SKPath> _inUse;
            private readonly object _lockObject = new object();
            private bool _disposed = false;

            public SKPathPool(int capacity)
            {
                _paths = new List<SKPath>(capacity);
                _inUse = new HashSet<SKPath>();

                for (int i = 0; i < capacity; i++)
                {
                    _paths.Add(new SKPath());
                }
            }

            public SKPath Get()
            {
                lock (_lockObject)
                {
                    if (_disposed)
                    {
                        return new SKPath(); // Если пул уже освобожден, создаем новый путь
                    }

                    // Ищем доступный путь
                    foreach (var path in _paths)
                    {
                        if (!_inUse.Contains(path))
                        {
                            _inUse.Add(path);
                            path.Reset(); // Сбрасываем к пустому пути
                            return path;
                        }
                    }

                    // Если все пути используются, создаем новый
                    return new SKPath();
                }
            }

            public void Return(SKPath path)
            {
                if (path == null || _disposed) return;

                lock (_lockObject)
                {
                    // Проверяем, принадлежит ли путь нашему пулу
                    if (_paths.Contains(path))
                    {
                        path.Reset();
                        _inUse.Remove(path);
                    }
                    else
                    {
                        // Если путь не из пула, просто освобождаем его
                        path.Dispose();
                    }
                }
            }

            public void Dispose()
            {
                if (_disposed) return;

                lock (_lockObject)
                {
                    _disposed = true;
                    foreach (var path in _paths)
                    {
                        path.Dispose();
                    }
                    _paths.Clear();
                    _inUse.Clear();
                }
            }
        }
        #endregion

        #region Disposal
        public override void Dispose()
        {
            if (!_disposed)
            {
                base.Dispose();
                _pathPool.Dispose();
                _barPathPool.Dispose();
                _barVectors = null;
                _disposed = true;
                SmartLogger.Log(LogLevel.Debug, Constants.LOG_PREFIX, "CircularBarsRenderer disposed");
            }
        }
        #endregion
    }
}