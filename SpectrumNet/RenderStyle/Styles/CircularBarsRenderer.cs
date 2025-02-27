namespace SpectrumNet
{
    public class CircularBarsRenderer : ISpectrumRenderer, IDisposable
    {
        #region Fields
        private static CircularBarsRenderer? _instance;
        private bool _isInitialized;
        private readonly SKPath _path = new();
        private float[]? _cosValues, _sinValues, _previousSpectrum;
        private float[]? _processedSpectrum;
        private readonly SemaphoreSlim _spectrumSemaphore = new(1, 1);
        private readonly object _spectrumLock = new();
        private volatile bool _disposed;

        private const float RadiusProportion = 0.8f;
        private const float SpectrumMultiplier = 0.5f;
        private const float SmoothingFactor = 0.3f;
        private const float MinMagnitudeThreshold = 0.01f;
        private const float MaxBarHeight = 1.5f;
        private const float MinBarHeight = 0.01f;
        private const float GlowRadius = 3f;
        private const float HighlightAlpha = 0.7f;
        private const float GlowIntensity = 0.4f;
        private const float InnerRadiusFactor = 0.9f;
        private const float MinStrokeWidth = 2f;
        #endregion

        #region Constructor and Initialization
        private CircularBarsRenderer() { }

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
            int halfSpectrumLength = spectrum!.Length / 2;
            int actualBarCount = Math.Min(halfSpectrumLength, barCount);

            try
            {
                semaphoreAcquired = _spectrumSemaphore.Wait(0);

                if (semaphoreAcquired)
                {
                    float[] downscaledSpectrum = ScaleSpectrum(spectrum, actualBarCount, halfSpectrumLength);
                    _processedSpectrum = SmoothSpectrum(downscaledSpectrum, actualBarCount);
                    EnsureTrigArrays(actualBarCount);
                }

                lock (_spectrumLock)
                {
                    renderSpectrum = _processedSpectrum ??
                                    ProcessSynchronously(spectrum, actualBarCount, halfSpectrumLength);
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
            float mainRadius = Math.Min(centerX, centerY) * RadiusProportion;

            // Adjust barWidth based on barCount to prevent overlap
            float adjustedBarWidth = AdjustBarWidthForBarCount(barWidth, actualBarCount);

            RenderCircularBars(canvas!, renderSpectrum, actualBarCount, centerX, centerY, mainRadius, adjustedBarWidth, basePaint!);
            drawPerformanceInfo?.Invoke(canvas!, info);
        }

        private float AdjustBarWidthForBarCount(float barWidth, int barCount)
        {
            // Calculate circumference of the visualization circle
            float circumference = (float)(2 * Math.PI * RadiusProportion * Math.Min(800, 600) / 2);

            // Calculate maximum width that won't cause overlap (leaving small gaps)
            float maxWidth = circumference / barCount * 0.7f;

            // Ensure minimum stroke width
            return Math.Max(Math.Min(barWidth, maxWidth), MinStrokeWidth);
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

        private float[] ProcessSynchronously(float[] spectrum, int actualBarCount, int halfSpectrumLength)
        {
            float[] downscaledSpectrum = ScaleSpectrum(spectrum, actualBarCount, halfSpectrumLength);
            return SmoothSpectrum(downscaledSpectrum, actualBarCount);
        }

        private static float[] ScaleSpectrum(float[] spectrum, int targetCount, int halfSpectrumLength)
        {
            float[] scaledSpectrum = new float[targetCount];
            float blockSize = (float)halfSpectrumLength / targetCount;

            for (int i = 0; i < targetCount; i++)
            {
                int start = (int)(i * blockSize);
                int end = (int)((i + 1) * blockSize);
                end = Math.Min(end, halfSpectrumLength);

                float sum = 0;
                for (int j = start; j < end; j++)
                    sum += spectrum[j];

                scaledSpectrum[i] = sum / (end - start);
            }

            return scaledSpectrum;
        }

        private float[] SmoothSpectrum(float[] spectrum, int targetCount)
        {
            if (_previousSpectrum == null || _previousSpectrum.Length != targetCount)
                _previousSpectrum = new float[targetCount];

            var scaledSpectrum = new float[targetCount];

            for (int i = 0; i < targetCount; i++)
            {
                float currentValue = spectrum[i];
                float smoothedValue = _previousSpectrum[i] + (currentValue - _previousSpectrum[i]) * SmoothingFactor;
                scaledSpectrum[i] = Math.Clamp(smoothedValue, MinBarHeight, MaxBarHeight);
                _previousSpectrum[i] = scaledSpectrum[i];
            }

            return scaledSpectrum;
        }

        private void EnsureTrigArrays(int barCount)
        {
            if (_cosValues == null || _sinValues == null || _cosValues.Length != barCount)
            {
                _cosValues = new float[barCount];
                _sinValues = new float[barCount];

                for (int i = 0; i < barCount; i++)
                {
                    float angle = (float)(2 * Math.PI * i / barCount);
                    _cosValues[i] = (float)Math.Cos(angle);
                    _sinValues[i] = (float)Math.Sin(angle);
                }
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
            using var barPaint = basePaint.Clone();
            barPaint.IsAntialias = true;
            barPaint.Style = SKPaintStyle.Stroke;
            barPaint.StrokeWidth = barWidth;
            barPaint.StrokeCap = SKStrokeCap.Round;

            using var highlightPaint = basePaint.Clone();
            highlightPaint.IsAntialias = true;
            highlightPaint.Style = SKPaintStyle.Stroke;
            highlightPaint.StrokeWidth = barWidth * 0.6f;
            highlightPaint.Color = SKColors.White.WithAlpha((byte)(255 * HighlightAlpha));

            using var glowPaint = basePaint.Clone();
            glowPaint.IsAntialias = true;
            glowPaint.Style = SKPaintStyle.Stroke;
            glowPaint.StrokeWidth = barWidth * 1.2f;
            glowPaint.ImageFilter = SKImageFilter.CreateBlur(GlowRadius, GlowRadius);

            using var innerCirclePaint = basePaint.Clone();
            innerCirclePaint.Style = SKPaintStyle.Stroke;
            innerCirclePaint.StrokeWidth = barWidth * 0.5f;
            innerCirclePaint.Color = innerCirclePaint.Color.WithAlpha(80);
            canvas.DrawCircle(centerX, centerY, mainRadius * InnerRadiusFactor, innerCirclePaint);

            RenderBars(canvas, spectrum, barCount, centerX, centerY, mainRadius, barWidth, barPaint, highlightPaint, glowPaint);
        }

        private void RenderBars(
            SKCanvas canvas,
            float[] spectrum,
            int barCount,
            float centerX,
            float centerY,
            float mainRadius,
            float barWidth,
            SKPaint barPaint,
            SKPaint highlightPaint,
            SKPaint glowPaint)
        {
            // First layer - Glow effects for high-intensity bars
            for (int i = 0; i < barCount; i++)
            {
                float magnitude = spectrum[i];
                if (magnitude <= 0.6f) continue;

                float radius = mainRadius + magnitude * mainRadius * SpectrumMultiplier;
                byte glowAlpha = (byte)(255 * magnitude * GlowIntensity);
                glowPaint.Color = glowPaint.Color.WithAlpha(glowAlpha);

                _path.Reset();
                AddBarToPath(i, centerX, centerY, mainRadius, radius);
                canvas.DrawPath(_path, glowPaint);
            }

            // Second layer - Main bars
            for (int i = 0; i < barCount; i++)
            {
                float magnitude = spectrum[i];
                if (magnitude < MinMagnitudeThreshold) continue;

                float radius = mainRadius + magnitude * mainRadius * SpectrumMultiplier;
                byte alpha = (byte)(255 * Math.Min(magnitude * 1.5f, 1f));
                barPaint.Color = barPaint.Color.WithAlpha(alpha);

                _path.Reset();
                AddBarToPath(i, centerX, centerY, mainRadius, radius);
                canvas.DrawPath(_path, barPaint);
            }

            // Third layer - Highlights for medium to high-intensity bars
            for (int i = 0; i < barCount; i++)
            {
                float magnitude = spectrum[i];
                if (magnitude <= 0.4f) continue;

                float radius = mainRadius + magnitude * mainRadius * SpectrumMultiplier;
                float innerPoint = mainRadius + (radius - mainRadius) * 0.7f;
                highlightPaint.Color = highlightPaint.Color.WithAlpha((byte)(255 * magnitude * 0.5f));

                _path.Reset();
                AddBarToPath(i, centerX, centerY, innerPoint, radius);
                canvas.DrawPath(_path, highlightPaint);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddBarToPath(int index, float centerX, float centerY, float innerRadius, float outerRadius)
        {
            if (_cosValues == null || _sinValues == null) return;

            _path.MoveTo(
                centerX + innerRadius * _cosValues[index],
                centerY + innerRadius * _sinValues[index]
            );
            _path.LineTo(
                centerX + outerRadius * _cosValues[index],
                centerY + outerRadius * _sinValues[index]
            );
        }
        #endregion

        #region Disposal
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                _path.Dispose();
                _spectrumSemaphore.Dispose();
                _cosValues = null;
                _sinValues = null;
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