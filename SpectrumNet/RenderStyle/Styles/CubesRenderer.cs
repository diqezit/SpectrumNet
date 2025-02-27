namespace SpectrumNet
{
    public class CubesRenderer : ISpectrumRenderer, IDisposable
    {
        #region Fields
        private static CubesRenderer? _instance;
        private bool _isInitialized;
        private readonly SKPath _cubeTopPath = new();
        private volatile bool _disposed;
        private readonly SemaphoreSlim _spectrumSemaphore = new(1, 1);

        private const float MinMagnitudeThreshold = 0.01f;
        private const float CubeTopWidthProportion = 0.75f;
        private const float CubeTopHeightProportion = 0.25f;
        private const float AlphaMultiplier = 255f;
        private const float TopAlphaFactor = 0.8f;
        private const float SideFaceAlphaFactor = 0.6f;

        private float _smoothingFactor = 0.3f;
        private float[]? _previousSpectrum;
        private float[]? _processedSpectrum;
        #endregion

        #region Constructor and Initialization
        private CubesRenderer() { }

        public static CubesRenderer GetInstance() => _instance ??= new CubesRenderer();

        public void Initialize()
        {
            if (!_isInitialized)
            {
                _isInitialized = true;
                Log.Debug("CubesRenderer initialized");
            }
        }
        #endregion

        #region Configuration
        public void Configure(bool isOverlayActive)
        {
            _smoothingFactor = isOverlayActive ? 0.5f : 0.3f;
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
            SKPaint? basePaint,
            Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
        {
            if (!ValidateRenderParameters(canvas, spectrum, info, basePaint))
                return;

            float[] renderSpectrum;
            bool semaphoreAcquired = false;

            try
            {
                semaphoreAcquired = _spectrumSemaphore.Wait(0);

                if (semaphoreAcquired)
                {
                    int halfSpectrumLength = spectrum!.Length / 2;
                    int actualBarCount = Math.Min(halfSpectrumLength, barCount);

                    float[] scaledSpectrum = ScaleSpectrum(spectrum, actualBarCount, halfSpectrumLength);
                    _processedSpectrum = SmoothSpectrum(scaledSpectrum, actualBarCount);
                }

                renderSpectrum = _processedSpectrum ??
                                 ProcessSynchronously(spectrum!, Math.Min(spectrum!.Length / 2, barCount));
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

            RenderSpectrum(canvas!, renderSpectrum, info, barWidth, barSpacing, basePaint!);
            drawPerformanceInfo?.Invoke(canvas!, info);
        }

        private bool ValidateRenderParameters(
            SKCanvas? canvas,
            float[]? spectrum,
            SKImageInfo info,
            SKPaint? basePaint)
        {
            if (!_isInitialized)
            {
                Log.Error("CubesRenderer not initialized before rendering");
                return false;
            }

            if (canvas == null ||
                spectrum == null || spectrum.Length < 2 ||
                basePaint == null ||
                info.Width <= 0 || info.Height <= 0)
            {
                Log.Error("Invalid render parameters for CubesRenderer");
                return false;
            }

            return true;
        }

        private float[] ProcessSynchronously(float[] spectrum, int targetCount)
        {
            int halfSpectrumLength = spectrum.Length / 2;
            var scaledSpectrum = ScaleSpectrum(spectrum, targetCount, halfSpectrumLength);
            return SmoothSpectrum(scaledSpectrum, targetCount);
        }

        private void RenderSpectrum(
            SKCanvas canvas,
            float[] spectrum,
            SKImageInfo info,
            float barWidth,
            float barSpacing,
            SKPaint basePaint)
        {
            float canvasHeight = info.Height;

            using var cubePaint = basePaint.Clone();
            cubePaint.IsAntialias = true;
            cubePaint.FilterQuality = SKFilterQuality.High;

            for (int i = 0; i < spectrum.Length; i++)
            {
                float magnitude = spectrum[i];
                if (magnitude < MinMagnitudeThreshold)
                    continue;

                float height = magnitude * canvasHeight;
                float x = i * (barWidth + barSpacing);
                float y = canvasHeight - height;

                cubePaint.Color = basePaint.Color.WithAlpha((byte)(magnitude * AlphaMultiplier));
                RenderCube(canvas, x, y, barWidth, height, magnitude, cubePaint);
            }
        }

        private void RenderCube(
            SKCanvas canvas,
            float x,
            float y,
            float barWidth,
            float height,
            float magnitude,
            SKPaint paint)
        {
            canvas.DrawRect(x, y, barWidth, height, paint);

            float topRightX = x + barWidth;
            float topOffsetX = barWidth * CubeTopWidthProportion;
            float topOffsetY = barWidth * CubeTopHeightProportion;

            _cubeTopPath.Reset();
            _cubeTopPath.MoveTo(x, y);
            _cubeTopPath.LineTo(topRightX, y);
            _cubeTopPath.LineTo(x + topOffsetX, y - topOffsetY);
            _cubeTopPath.LineTo(x - (barWidth - topOffsetX), y - topOffsetY);
            _cubeTopPath.Close();

            using var topPaint = paint.Clone();
            topPaint.Color = paint.Color.WithAlpha((byte)(magnitude * AlphaMultiplier * TopAlphaFactor));
            canvas.DrawPath(_cubeTopPath, topPaint);

            using var sidePath = new SKPath();
            sidePath.MoveTo(topRightX, y);
            sidePath.LineTo(topRightX, y + height);
            sidePath.LineTo(x + topOffsetX, y - topOffsetY + height);
            sidePath.LineTo(x + topOffsetX, y - topOffsetY);
            sidePath.Close();

            using var sidePaint = paint.Clone();
            sidePaint.Color = paint.Color.WithAlpha((byte)(magnitude * AlphaMultiplier * SideFaceAlphaFactor));
            canvas.DrawPath(sidePath, sidePaint);
        }
        #endregion

        #region Spectrum Processing
        private static float[] ScaleSpectrum(float[] spectrum, int targetCount, int halfSpectrumLength)
        {
            float[] scaledSpectrum = new float[targetCount];
            float blockSize = (float)halfSpectrumLength / targetCount;

            for (int i = 0; i < targetCount; i++)
            {
                int start = (int)Math.Floor(i * blockSize);
                int end = (int)Math.Ceiling((i + 1) * blockSize);
                end = end <= start ? start + 1 : Math.Min(end, halfSpectrumLength);

                float sum = 0;
                for (int j = start; j < end; j++)
                {
                    sum += spectrum[j];
                }

                scaledSpectrum[i] = sum / (end - start);
            }

            return scaledSpectrum;
        }

        private float[] SmoothSpectrum(float[] spectrum, int targetCount)
        {
            if (_previousSpectrum == null || _previousSpectrum.Length != targetCount)
            {
                _previousSpectrum = new float[targetCount];
            }

            float[] smoothedSpectrum = new float[targetCount];

            for (int i = 0; i < targetCount; i++)
            {
                float delta = spectrum[i] - _previousSpectrum[i];
                smoothedSpectrum[i] = _previousSpectrum[i] + delta * _smoothingFactor;
                _previousSpectrum[i] = smoothedSpectrum[i];
            }

            return smoothedSpectrum;
        }
        #endregion

        #region Disposal
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _spectrumSemaphore?.Dispose();
                    _cubeTopPath?.Dispose();
                    _previousSpectrum = null;
                    _processedSpectrum = null;
                }

                _disposed = true;
                Log.Debug("CubesRenderer disposed");
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