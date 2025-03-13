#nullable enable

namespace SpectrumNet
{
    public class CircularBarsRenderer : BaseSpectrumRenderer
    {
        #region Constants and Records
        private static class Constants
        {
            public const string LOG_PREFIX = "CircularBarsRenderer";
            public const float RADIUS_PROPORTION = 0.8f;
            public const float INNER_RADIUS_FACTOR = 0.9f;
            public const float MIN_STROKE_WIDTH = 2f;
            public const float SPECTRUM_MULTIPLIER = 0.5f;
            public const float MIN_MAGNITUDE_THRESHOLD = 0.01f;
            public const float MAX_BAR_HEIGHT = 1.5f;
            public const float MIN_BAR_HEIGHT = 0.01f;
            public const float GLOW_RADIUS = 3f;
            public const float HIGHLIGHT_ALPHA = 0.7f;
            public const float GLOW_INTENSITY = 0.4f;
            public const float BAR_SPACING_FACTOR = 0.7f;
            public const float HIGHLIGHT_POSITION = 0.7f;
            public const float HIGHLIGHT_INTENSITY = 0.5f;
            public const byte INNER_CIRCLE_ALPHA = 80;
            public const int PARALLEL_BATCH_SIZE = 32;
            public const float GLOW_THRESHOLD = 0.6f;
            public const float HIGHLIGHT_THRESHOLD = 0.4f;
        }

        public record RenderConfig(float BarWidth, float BarSpacing, int BarCount);
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

        public override void Initialize() =>
            SmartLogger.Safe(() =>
            {
                if (!_isInitialized)
                {
                    base.Initialize();
                    _isInitialized = true;
                    SmartLogger.Log(LogLevel.Debug, Constants.LOG_PREFIX, "CircularBarsRenderer initialized");
                }
            },
            new SmartLogger.ErrorHandlingOptions
            {
                Source = "CircularBarsRenderer.Initialize",
                ErrorMessage = "Ошибка инициализации CircularBarsRenderer"
            });
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
            if (!QuickValidate(canvas, spectrum, info, basePaint))
                return;

            (float[] processedSpectrum, int count) processedData = (Array.Empty<float>(), 0);
            SmartLogger.Safe(() =>
            {
                processedData = ProcessSpectrumInternal(spectrum!, barCount);
            },
            new SmartLogger.ErrorHandlingOptions
            {
                Source = "CircularBarsRenderer.ProcessSpectrum",
                ErrorMessage = "Ошибка обработки спектра"
            });

            if (processedData.count == 0)
            {
                drawPerformanceInfo?.Invoke(canvas!, info);
                return;
            }

            float centerX = info.Width / 2f;
            float centerY = info.Height / 2f;
            float mainRadius = MathF.Min(centerX, centerY) * Constants.RADIUS_PROPORTION;
            float adjustedBarWidth = AdjustBarWidthForBarCount(barWidth, processedData.count, MathF.Min(info.Width, info.Height));

            if (canvas!.QuickReject(new SKRect(centerX - mainRadius, centerY - mainRadius, centerX + mainRadius, centerY + mainRadius)))
            {
                drawPerformanceInfo?.Invoke(canvas, info);
                return;
            }

            RenderCircularBars(canvas, processedData.processedSpectrum, processedData.count, centerX, centerY, mainRadius, adjustedBarWidth, basePaint!);
            drawPerformanceInfo?.Invoke(canvas, info);
        }

        private (float[] processedSpectrum, int count) ProcessSpectrumInternal(float[] spectrum, int barCount, CancellationToken ct = default)
        {
            bool acquired = _spectrumSemaphore.Wait(0);
            try
            {
                if (acquired)
                {
                    int targetCount = Math.Min(spectrum.Length, barCount);
                    _processedSpectrum = SmoothSpectrum(ScaleSpectrum(spectrum, targetCount, spectrum.Length), targetCount);
                    EnsureBarVectors(targetCount);
                }
                float[] result = _processedSpectrum ?? ScaleSpectrum(spectrum, barCount, spectrum.Length);
                return (result, result.Length);
            }
            finally
            {
                if (acquired)
                    _spectrumSemaphore.Release();
            }
        }

        private float AdjustBarWidthForBarCount(float barWidth, int barCount, float minDimension) =>
            MathF.Max(MathF.Min(barWidth, (2 * MathF.PI * Constants.RADIUS_PROPORTION * minDimension / 2) / barCount * Constants.BAR_SPACING_FACTOR), Constants.MIN_STROKE_WIDTH);

        private void RenderCircularBars(SKCanvas canvas, float[] spectrum, int barCount, float centerX, float centerY, float mainRadius, float barWidth, SKPaint basePaint)
        {
            using var innerCirclePaint = new SKPaint
            {
                IsAntialias = _useAntiAlias,
                FilterQuality = _filterQuality,
                Style = SKPaintStyle.Stroke,
                Color = basePaint.Color.WithAlpha(Constants.INNER_CIRCLE_ALPHA),
                StrokeWidth = barWidth * 0.5f
            };
            canvas.DrawCircle(centerX, centerY, mainRadius * Constants.INNER_RADIUS_FACTOR, innerCirclePaint);

            EnsureBarVectors(barCount);
            if (_useAdvancedEffects)
                RenderGlowEffects(canvas, spectrum, barCount, centerX, centerY, mainRadius, barWidth, basePaint);
            RenderMainBars(canvas, spectrum, barCount, centerX, centerY, mainRadius, barWidth, basePaint);
            if (_useAdvancedEffects)
                RenderHighlights(canvas, spectrum, barCount, centerX, centerY, mainRadius, barWidth, basePaint);
        }
        #endregion

        #region Bar Vectors
        private void EnsureBarVectors(int barCount)
        {
            if (_barVectors == null || _barVectors.Length != barCount || _previousBarCount != barCount)
            {
                _barVectors = new Vector2[barCount];
                float angleStep = 2 * MathF.PI / barCount;
                for (int i = 0; i < barCount; i++)
                    _barVectors[i] = new Vector2(MathF.Cos(angleStep * i), MathF.Sin(angleStep * i));
                _previousBarCount = barCount;
            }
        }
        #endregion

        #region Rendering Methods
        private void RenderGlowEffects(SKCanvas canvas, float[] spectrum, int barCount, float centerX, float centerY, float mainRadius, float barWidth, SKPaint basePaint)
        {
            using var batchPath = new SKPath();
            for (int i = 0; i < barCount; i++)
            {
                if (spectrum[i] <= Constants.GLOW_THRESHOLD)
                    continue;
                float radius = mainRadius + spectrum[i] * mainRadius * Constants.SPECTRUM_MULTIPLIER;
                var path = _barPathPool.Get();
                AddBarToPath(path, i, centerX, centerY, mainRadius, radius);
                batchPath.AddPath(path);
                _barPathPool.Return(path);
            }
            if (!batchPath.IsEmpty)
            {
                using var glowPaint = new SKPaint
                {
                    IsAntialias = _useAntiAlias,
                    FilterQuality = _filterQuality,
                    Style = SKPaintStyle.Stroke,
                    Color = basePaint.Color.WithAlpha((byte)(255 * Constants.GLOW_INTENSITY)),
                    StrokeWidth = barWidth * 1.2f,
                    MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Constants.GLOW_RADIUS)
                };
                canvas.DrawPath(batchPath, glowPaint);
            }
        }

        private void RenderMainBars(SKCanvas canvas, float[] spectrum, int barCount, float centerX, float centerY, float mainRadius, float barWidth, SKPaint basePaint)
        {
            using var batchPath = new SKPath();
            for (int i = 0; i < barCount; i++)
            {
                if (spectrum[i] < Constants.MIN_MAGNITUDE_THRESHOLD)
                    continue;
                float radius = mainRadius + spectrum[i] * mainRadius * Constants.SPECTRUM_MULTIPLIER;
                var path = _barPathPool.Get();
                AddBarToPath(path, i, centerX, centerY, mainRadius, radius);
                batchPath.AddPath(path);
                _barPathPool.Return(path);
            }
            if (!batchPath.IsEmpty)
            {
                using var barPaint = new SKPaint
                {
                    IsAntialias = _useAntiAlias,
                    FilterQuality = _filterQuality,
                    Style = SKPaintStyle.Stroke,
                    StrokeCap = SKStrokeCap.Round,
                    Color = basePaint.Color,
                    StrokeWidth = barWidth
                };
                canvas.DrawPath(batchPath, barPaint);
            }
        }

        private void RenderHighlights(SKCanvas canvas, float[] spectrum, int barCount, float centerX, float centerY, float mainRadius, float barWidth, SKPaint basePaint)
        {
            using var batchPath = new SKPath();
            for (int i = 0; i < barCount; i++)
            {
                if (spectrum[i] <= Constants.HIGHLIGHT_THRESHOLD)
                    continue;
                float radius = mainRadius + spectrum[i] * mainRadius * Constants.SPECTRUM_MULTIPLIER;
                float innerPoint = mainRadius + (radius - mainRadius) * Constants.HIGHLIGHT_POSITION;
                var path = _barPathPool.Get();
                AddBarToPath(path, i, centerX, centerY, innerPoint, radius);
                batchPath.AddPath(path);
                _barPathPool.Return(path);
            }
            if (!batchPath.IsEmpty)
            {
                using var highlightPaint = new SKPaint
                {
                    IsAntialias = _useAntiAlias,
                    FilterQuality = _filterQuality,
                    Style = SKPaintStyle.Stroke,
                    Color = SKColors.White.WithAlpha((byte)(255 * Constants.HIGHLIGHT_INTENSITY)),
                    StrokeWidth = barWidth * 0.6f
                };
                canvas.DrawPath(batchPath, highlightPaint);
            }
        }

        private void AddBarToPath(SKPath path, int index, float centerX, float centerY, float innerRadius, float outerRadius)
        {
            if (_barVectors == null)
                return;
            Vector2 vector = _barVectors[index];
            path.MoveTo(centerX + innerRadius * vector.X, centerY + innerRadius * vector.Y);
            path.LineTo(centerX + outerRadius * vector.X, centerY + outerRadius * vector.Y);
        }
        #endregion

        #region Path Pooling
        private class SKPathPool : IDisposable
        {
            private readonly List<SKPath> _paths, _inUse;
            private readonly object _lockObject = new();
            private bool _disposed = false;

            public SKPathPool(int capacity)
            {
                _paths = new List<SKPath>(capacity);
                _inUse = new List<SKPath>(capacity);
                for (int i = 0; i < capacity; i++)
                    _paths.Add(new SKPath());
            }

            public SKPath Get()
            {
                lock (_lockObject)
                {
                    if (_disposed)
                        return new SKPath();
                    foreach (var path in _paths)
                    {
                        if (!_inUse.Contains(path))
                        {
                            _inUse.Add(path);
                            path.Reset();
                            return path;
                        }
                    }
                    return new SKPath();
                }
            }

            public void Return(SKPath path)
            {
                if (path == null || _disposed)
                    return;
                lock (_lockObject)
                {
                    if (_paths.Contains(path))
                    {
                        path.Reset();
                        _inUse.Remove(path);
                    }
                    else
                    {
                        path.Dispose();
                    }
                }
            }

            public void Dispose()
            {
                if (_disposed)
                    return;
                lock (_lockObject)
                {
                    _disposed = true;
                    foreach (var path in _paths)
                        path.Dispose();
                    _paths.Clear();
                    _inUse.Clear();
                }
            }
        }
        #endregion

        #region Disposal
        public override void Dispose()
        {
            SmartLogger.SafeDispose(_pathPool, "CircularBarsRenderer._pathPool",
                new SmartLogger.ErrorHandlingOptions
                {
                    Source = "CircularBarsRenderer.Dispose",
                    ErrorMessage = "Ошибка освобождения _pathPool"
                });
            SmartLogger.SafeDispose(_barPathPool, "CircularBarsRenderer._barPathPool",
                new SmartLogger.ErrorHandlingOptions
                {
                    Source = "CircularBarsRenderer.Dispose",
                    ErrorMessage = "Ошибка освобождения _barPathPool"
                });
            _barVectors = null;
            _disposed = true;
            SmartLogger.Log(LogLevel.Debug, Constants.LOG_PREFIX, "CircularBarsRenderer disposed");
        }
        #endregion
    }
}