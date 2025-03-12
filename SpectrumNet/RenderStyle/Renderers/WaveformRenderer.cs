#nullable enable

namespace SpectrumNet
{
    public class WaveformRenderer : ISpectrumRenderer, IDisposable
    {
        #region Constants
        private static class Constants
        {
            // Spectrum processing constants
            public const float SmoothingFactorNormal = 0.3f;  // Smoothing factor for normal mode
            public const float SmoothingFactorOverlay = 0.5f;  // Smoothing factor for overlay mode
            public const float MinMagnitudeThreshold = 0.01f; // Minimum magnitude threshold for rendering
            public const float MaxSpectrumValue = 1.5f;  // Maximum spectrum value for clamping

            // Rendering constants
            public const float MinStrokeWidth = 2.0f;  // Minimum stroke width for waveform lines
            public const byte FillAlpha = 64;    // Alpha value for waveform fill
            public const float GlowIntensity = 0.4f;  // Intensity of glow effect
            public const float GlowRadius = 3.0f;  // Blur radius for glow effect
            public const float HighlightAlpha = 0.7f;  // Alpha value for highlight effect
            public const float HighAmplitudeThreshold = 0.6f;  // Threshold for high amplitude effects
        }
        #endregion

        #region Fields
        private static WaveformRenderer? _instance;
        private bool _isInitialized;
        private readonly SKPath _topPath = new();
        private readonly SKPath _bottomPath = new();
        private readonly SKPath _fillPath = new();
        private readonly SemaphoreSlim _spectrumSemaphore = new(1, 1);
        private volatile bool _disposed;

        private float _smoothingFactor = Constants.SmoothingFactorNormal;
        private float[]? _previousSpectrum;
        private float[]? _processedSpectrum;
        private SKPaint? _glowPaint;

        // RenderQuality fields
        private RenderQuality _quality = RenderQuality.Medium;
        private bool _useAntiAlias = true;
        private SKFilterQuality _filterQuality = SKFilterQuality.Medium;
        private bool _useAdvancedEffects = true;

        private const string LogPrefix = "[WaveformRenderer] ";
        #endregion

        #region Constructor and Initialization
        private WaveformRenderer() { }

        public static WaveformRenderer GetInstance() => _instance ??= new WaveformRenderer();

        public void Initialize()
        {
            if (!_isInitialized)
            {
                _isInitialized = true;
                _glowPaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    ImageFilter = SKImageFilter.CreateBlur(Constants.GlowRadius, Constants.GlowRadius)
                };
                SmartLogger.Log(LogLevel.Debug, LogPrefix, "WaveformRenderer initialized");
            }
        }
        #endregion

        #region Configuration
        public void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium)
        {
            _smoothingFactor = isOverlayActive ? Constants.SmoothingFactorOverlay : Constants.SmoothingFactorNormal;
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

            if (_glowPaint != null)
            {
                _glowPaint.IsAntialias = _useAntiAlias;
                _glowPaint.FilterQuality = _filterQuality;
            }
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
            {
                return;
            }

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
                }

                renderSpectrum = _processedSpectrum ??
                                 ProcessSynchronously(spectrum!, actualBarCount, spectrumLength);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error processing spectrum: {ex.Message}");
                return;
            }
            finally
            {
                if (semaphoreAcquired)
                {
                    _spectrumSemaphore.Release();
                }
            }

            RenderWaveform(canvas!, renderSpectrum, info, paint!);
            drawPerformanceInfo?.Invoke(canvas!, info);
        }

        private bool ValidateRenderParameters(
            SKCanvas? canvas,
            float[]? spectrum,
            SKImageInfo info,
            SKPaint? paint)
        {
            if (!_isInitialized)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "WaveformRenderer not initialized before rendering");
                return false;
            }

            if (canvas == null)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Canvas cannot be null");
                return false;
            }

            if (spectrum == null || spectrum.Length < 2)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Spectrum cannot be null or have fewer than 2 elements");
                return false;
            }

            if (paint == null)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Paint cannot be null");
                return false;
            }

            if (info.Width <= 0 || info.Height <= 0)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Invalid canvas dimensions");
                return false;
            }

            return true;
        }

        private float[] ProcessSynchronously(float[] spectrum, int actualBarCount, int spectrumLength)
        {
            float[] scaledSpectrum = ScaleSpectrum(spectrum, actualBarCount, spectrumLength);
            return SmoothSpectrum(scaledSpectrum, actualBarCount);
        }

        private void RenderWaveform(
            SKCanvas canvas,
            float[] spectrum,
            SKImageInfo info,
            SKPaint basePaint)
        {
            float midY = info.Height / 2;
            float xStep = (float)info.Width / spectrum.Length;

            using var waveformPaint = basePaint.Clone();
            waveformPaint.Style = SKPaintStyle.Stroke;
            waveformPaint.StrokeWidth = Math.Max(Constants.MinStrokeWidth, 50f / spectrum.Length);
            waveformPaint.IsAntialias = _useAntiAlias;
            waveformPaint.FilterQuality = _filterQuality;
            waveformPaint.StrokeCap = SKStrokeCap.Round;
            waveformPaint.StrokeJoin = SKStrokeJoin.Round;

            using var fillPaint = basePaint.Clone();
            fillPaint.Style = SKPaintStyle.Fill;
            fillPaint.Color = fillPaint.Color.WithAlpha(Constants.FillAlpha);
            fillPaint.IsAntialias = _useAntiAlias;
            fillPaint.FilterQuality = _filterQuality;

            using var highlightPaint = basePaint.Clone();
            highlightPaint.Style = SKPaintStyle.Stroke;
            highlightPaint.StrokeWidth = waveformPaint.StrokeWidth * 0.6f;
            highlightPaint.Color = SKColors.White.WithAlpha((byte)(255 * Constants.HighlightAlpha));
            highlightPaint.IsAntialias = _useAntiAlias;
            highlightPaint.FilterQuality = _filterQuality;

            if (_glowPaint != null && _useAdvancedEffects)
            {
                _glowPaint.Color = basePaint.Color;
                _glowPaint.StrokeWidth = waveformPaint.StrokeWidth * 1.5f;
            }

            CreateWavePaths(spectrum, midY, xStep);
            CreateFillPath(spectrum, midY, xStep, info.Width);

            if (_glowPaint != null && _useAdvancedEffects)
            {
                bool hasHighAmplitude = false;
                for (int i = 0; i < spectrum.Length; i++)
                {
                    if (spectrum[i] > Constants.HighAmplitudeThreshold)
                    {
                        hasHighAmplitude = true;
                        break;
                    }
                }

                if (hasHighAmplitude)
                {
                    _glowPaint.Color = _glowPaint.Color.WithAlpha((byte)(255 * Constants.GlowIntensity));
                    canvas.DrawPath(_topPath, _glowPaint);
                    canvas.DrawPath(_bottomPath, _glowPaint);
                }
            }

            canvas.DrawPath(_fillPath, fillPaint);
            canvas.DrawPath(_topPath, waveformPaint);
            canvas.DrawPath(_bottomPath, waveformPaint);

            if (_useAdvancedEffects)
            {
                for (int i = 0; i < spectrum.Length; i++)
                {
                    if (spectrum[i] > Constants.HighAmplitudeThreshold)
                    {
                        float x = i * xStep;
                        float topY = midY - (spectrum[i] * midY);
                        float bottomY = midY + (spectrum[i] * midY);

                        canvas.DrawPoint(x, topY, highlightPaint);
                        canvas.DrawPoint(x, bottomY, highlightPaint);
                    }
                }
            }
        }

        private void CreateWavePaths(float[] spectrum, float midY, float xStep)
        {
            _topPath.Reset();
            _bottomPath.Reset();

            float x = 0;
            float topY = midY - (spectrum[0] * midY);
            float bottomY = midY + (spectrum[0] * midY);

            _topPath.MoveTo(x, topY);
            _bottomPath.MoveTo(x, bottomY);

            for (int i = 1; i < spectrum.Length; i++)
            {
                float prevX = (i - 1) * xStep;
                float prevTopY = midY - (spectrum[i - 1] * midY);
                float prevBottomY = midY + (spectrum[i - 1] * midY);

                x = i * xStep;
                topY = midY - (spectrum[i] * midY);
                bottomY = midY + (spectrum[i] * midY);

                float controlX = (prevX + x) / 2;
                _topPath.CubicTo(controlX, prevTopY, controlX, topY, x, topY);
                _bottomPath.CubicTo(controlX, prevBottomY, controlX, bottomY, x, bottomY);
            }
        }

        private void CreateFillPath(float[] spectrum, float midY, float xStep, float width)
        {
            _fillPath.Reset();

            float startX = 0;
            float startTopY = midY - (spectrum[0] * midY);
            _fillPath.MoveTo(startX, startTopY);

            for (int i = 1; i < spectrum.Length; i++)
            {
                float prevX = (i - 1) * xStep;
                float prevTopY = midY - (spectrum[i - 1] * midY);

                float x = i * xStep;
                float topY = midY - (spectrum[i] * midY);

                float controlX = (prevX + x) / 2;
                _fillPath.CubicTo(controlX, prevTopY, controlX, topY, x, topY);
            }

            float endX = (spectrum.Length - 1) * xStep;
            float endBottomY = midY + (spectrum[spectrum.Length - 1] * midY);
            _fillPath.LineTo(endX, endBottomY);

            for (int i = spectrum.Length - 2; i >= 0; i--)
            {
                float prevX = (i + 1) * xStep;
                float prevBottomY = midY + (spectrum[i + 1] * midY);

                float x = i * xStep;
                float bottomY = midY + (spectrum[i] * midY);

                float controlX = (prevX + x) / 2;
                _fillPath.CubicTo(controlX, prevBottomY, controlX, bottomY, x, bottomY);
            }

            _fillPath.Close();
        }
        #endregion

        #region Spectrum Processing
        private static float[] ScaleSpectrum(float[] spectrum, int targetCount, int spectrumLength)
        {
            float[] scaledSpectrum = new float[targetCount];
            float blockSize = (float)spectrumLength / targetCount;

            for (int i = 0; i < targetCount; i++)
            {
                float sum = 0;
                int start = (int)(i * blockSize);
                int end = (int)((i + 1) * blockSize);
                int actualEnd = Math.Min(end, spectrumLength);
                int count = actualEnd - start;

                if (count <= 0)
                {
                    scaledSpectrum[i] = 0;
                    continue;
                }

                for (int j = start; j < actualEnd; j++)
                {
                    sum += spectrum[j];
                }

                scaledSpectrum[i] = sum / count;
            }

            return scaledSpectrum;
        }

        private float[] SmoothSpectrum(float[] spectrum, int targetCount)
        {
            if (_previousSpectrum == null || _previousSpectrum.Length != targetCount)
            {
                _previousSpectrum = new float[targetCount];
            }

            var smoothedSpectrum = new float[targetCount];

            for (int i = 0; i < targetCount; i++)
            {
                float currentValue = spectrum[i];
                float smoothedValue = _previousSpectrum[i] + (currentValue - _previousSpectrum[i]) * _smoothingFactor;
                smoothedSpectrum[i] = Math.Clamp(smoothedValue, Constants.MinMagnitudeThreshold, Constants.MaxSpectrumValue);
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
                    _topPath?.Dispose();
                    _bottomPath?.Dispose();
                    _fillPath?.Dispose();
                    _glowPaint?.Dispose();
                    _previousSpectrum = null;
                    _processedSpectrum = null;
                }

                _disposed = true;
                SmartLogger.Log(LogLevel.Debug, LogPrefix, "WaveformRenderer disposed");
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