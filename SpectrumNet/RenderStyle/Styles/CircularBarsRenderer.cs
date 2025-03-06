#nullable enable

namespace SpectrumNet
{
    public class CircularBarsRenderer : ISpectrumRenderer, IDisposable
    {
        #region Constants
        private const float RADIUS_PROPORTION = 0.8f;  // Proportional radius of the visualization circle relative to canvas size
        private const float INNER_RADIUS_FACTOR = 0.9f;  // Proportion of the main radius for inner circle
        private const float MIN_STROKE_WIDTH = 2f;    // Minimum width for bar strokes
        private const float SPECTRUM_MULTIPLIER = 0.5f;  // Amplifies spectrum values for more visible effect
        private const float SMOOTHING_FACTOR = 0.3f;  // Controls how quickly the bars respond to changes (0-1, higher = more responsive)
        private const float MIN_MAGNITUDE_THRESHOLD = 0.01f; // Minimum magnitude threshold for rendering a bar
        private const float MAX_BAR_HEIGHT = 1.5f;  // Maximum height multiplier for bars
        private const float MIN_BAR_HEIGHT = 0.01f; // Minimum height multiplier for bars
        private const float GLOW_RADIUS = 3f;    // Blur radius for glow effects
        private const float HIGHLIGHT_ALPHA = 0.7f;  // Alpha value for highlight effects
        private const float GLOW_INTENSITY = 0.4f;  // Intensity multiplier for glow effects
        private const float BAR_SPACING_FACTOR = 0.7f;  // Bar spacing factor to prevent overlap
        private const float HIGHLIGHT_POSITION = 0.7f;  // Highlight position factor (0-1)
        private const float HIGHLIGHT_INTENSITY = 0.5f;  // Highlight intensity multiplier
        private const byte INNER_CIRCLE_ALPHA = 80;    // Inner circle alpha (0-255)
        private const int PARALLEL_BATCH_SIZE = 32;    // Batch size for parallel processing
        private const float GLOW_THRESHOLD = 0.6f;  // Threshold for applying glow effects
        private const float HIGHLIGHT_THRESHOLD = 0.4f;  // Threshold for applying highlight effects
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
        #endregion

        #region Constructor and Initialization
        private CircularBarsRenderer()
        {
            InitializePaints();
        }

        private void InitializePaints()
        {
            _barPaint.IsAntialias = true;
            _barPaint.Style = SKPaintStyle.Stroke;
            _barPaint.StrokeCap = SKStrokeCap.Round;

            _highlightPaint.IsAntialias = true;
            _highlightPaint.Style = SKPaintStyle.Stroke;
            _highlightPaint.Color = SKColors.White.WithAlpha((byte)(255 * HIGHLIGHT_ALPHA));

            _glowPaint.IsAntialias = true;
            _glowPaint.Style = SKPaintStyle.Stroke;
            _glowPaint.ImageFilter = SKImageFilter.CreateBlur(GLOW_RADIUS, GLOW_RADIUS);

            _innerCirclePaint.Style = SKPaintStyle.Stroke;
        }

        public static CircularBarsRenderer GetInstance() => _instance ??= new CircularBarsRenderer();

        public void Initialize()
        {
            if (_isInitialized) return;
            _isInitialized = true;
            Log.Debug("CircularBarsRenderer initialized");
        }

        public void Configure(bool isOverlayActive) { }
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
                Log.Error($"Error processing spectrum: {ex.Message}");
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
            float mainRadius = Math.Min(centerX, centerY) * RADIUS_PROPORTION;
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
            float circumference = (float)(2 * Math.PI * RADIUS_PROPORTION * minDimension / 2);
            float maxWidth = circumference / barCount * BAR_SPACING_FACTOR;
            return Math.Max(Math.Min(barWidth, maxWidth), MIN_STROKE_WIDTH);
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
                Log.Error("CircularBarsRenderer not initialized before rendering");
                return false;
            }

            if (canvas == null ||
                spectrum == null || spectrum.Length < 2 ||
                basePaint == null ||
                info.Width <= 0 || info.Height <= 0)
            {
                Log.Error("Invalid render parameters for CircularBarsRenderer");
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

            if (targetCount >= PARALLEL_BATCH_SIZE && Vector.IsHardwareAccelerated)
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
                    Vector<float> smoothedValues = previousValues + (currentValues - previousValues) * SMOOTHING_FACTOR;
                    Vector<float> minVector = new Vector<float>(MIN_BAR_HEIGHT);
                    Vector<float> maxVector = new Vector<float>(MAX_BAR_HEIGHT);
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
            float smoothedValue = _previousSpectrum[i] + (currentValue - _previousSpectrum[i]) * SMOOTHING_FACTOR;
            scaledSpectrum[i] = Math.Clamp(smoothedValue, MIN_BAR_HEIGHT, MAX_BAR_HEIGHT);
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
            _barPaint.Color = basePaint.Color;
            _barPaint.StrokeWidth = barWidth;
            _highlightPaint.StrokeWidth = barWidth * 0.6f;
            _glowPaint.Color = basePaint.Color;
            _glowPaint.StrokeWidth = barWidth * 1.2f;
            _innerCirclePaint.Color = basePaint.Color.WithAlpha(INNER_CIRCLE_ALPHA);
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
            canvas.DrawCircle(centerX, centerY, mainRadius * INNER_RADIUS_FACTOR, _innerCirclePaint);
            EnsureBarVectors(barCount);
            RenderGlowEffects(canvas, spectrum, barCount, centerX, centerY, mainRadius);
            RenderMainBars(canvas, spectrum, barCount, centerX, centerY, mainRadius);
            RenderHighlights(canvas, spectrum, barCount, centerX, centerY, mainRadius);
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
                if (magnitude <= GLOW_THRESHOLD) continue;
                float radius = mainRadius + magnitude * mainRadius * SPECTRUM_MULTIPLIER;
                byte glowAlpha = (byte)(255 * magnitude * GLOW_INTENSITY);
                var path = _barPathPool.Get();
                AddBarToPath(path, i, centerX, centerY, mainRadius, radius);
                batchPath.AddPath(path);
                _barPathPool.Return(path);
            }

            if (!batchPath.IsEmpty)
            {
                _glowPaint.Color = _glowPaint.Color.WithAlpha((byte)(255 * GLOW_INTENSITY));
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
                if (magnitude < MIN_MAGNITUDE_THRESHOLD) continue;
                float radius = mainRadius + magnitude * mainRadius * SPECTRUM_MULTIPLIER;
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
                if (magnitude <= HIGHLIGHT_THRESHOLD) continue;
                float radius = mainRadius + magnitude * mainRadius * SPECTRUM_MULTIPLIER;
                float innerPoint = mainRadius + (radius - mainRadius) * HIGHLIGHT_POSITION;
                var path = _barPathPool.Get();
                AddBarToPath(path, i, centerX, centerY, innerPoint, radius);
                batchPath.AddPath(path);
                _barPathPool.Return(path);
            }

            if (!batchPath.IsEmpty)
            {
                _highlightPaint.Color = _highlightPaint.Color.WithAlpha((byte)(255 * HIGHLIGHT_INTENSITY));
                canvas.DrawPath(batchPath, _highlightPaint);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddBarToPath(SKPath path, int index, float centerX, float centerY, float innerRadius, float outerRadius)
        {
            if (_barVectors == null) return;
            Vector2 vector = _barVectors[index];
            path.MoveTo(
                centerX + innerRadius * vector.X,
                centerY + innerRadius * vector.Y
            );
            path.LineTo(
                centerX + outerRadius * vector.X,
                centerY + outerRadius * vector.Y
            );
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
            Log.Debug("CircularBarsRenderer disposed");
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}