#nullable enable
using System;
using System.Numerics;
using System.Threading;
using SkiaSharp;

namespace SpectrumNet
{
    public sealed class CubesRenderer : ISpectrumRenderer, IDisposable
    {
        #region Constants
        private static class Constants
        {
            // Spectrum processing constants
            public const float MinMagnitudeThreshold = 0.01f;    // Minimum magnitude threshold for rendering
            public const float SmoothingFactorNormal = 0.3f;     // Smoothing factor for normal mode
            public const float SmoothingFactorOverlay = 0.5f;    // Smoothing factor for overlay mode

            // Rendering constants
            public const float CubeTopWidthProportion = 0.75f;   // Proportion of bar width for cube top
            public const float CubeTopHeightProportion = 0.25f;  // Proportion of bar width for cube top height
            public const float AlphaMultiplier = 255f;           // Multiplier for alpha calculation
            public const float TopAlphaFactor = 0.8f;            // Alpha factor for cube top
            public const float SideFaceAlphaFactor = 0.6f;       // Alpha factor for cube side face
        }
        #endregion

        #region Fields
        private static CubesRenderer? _instance;
        private bool _isInitialized;
        private readonly SKPath _cubeTopPath = new();
        private volatile bool _disposed;
        private readonly SemaphoreSlim _spectrumSemaphore = new(1, 1);

        private float _smoothingFactor = Constants.SmoothingFactorNormal;
        private float[]? _previousSpectrum;
        private float[]? _processedSpectrum;

        // RenderQuality fields
        private RenderQuality _quality = RenderQuality.Medium;
        private bool _useAntiAlias = true;
        private SKFilterQuality _filterQuality = SKFilterQuality.Medium;
        private bool _useAdvancedEffects = true;

        private const string LogPrefix = "[CubesRenderer] ";
        #endregion

        #region Constructor and Initialization
        private CubesRenderer() { }

        public static CubesRenderer GetInstance() => _instance ??= new CubesRenderer();

        public void Initialize()
        {
            if (!_isInitialized)
            {
                _isInitialized = true;
                SmartLogger.Log(LogLevel.Debug, LogPrefix, "CubesRenderer initialized");
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
            Action<SKCanvas, SKImageInfo>? drawPerformanceInfo)
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
                    int spectrumLength = spectrum!.Length;
                    int actualBarCount = Math.Min(spectrumLength, barCount);

                    float[] scaledSpectrum = ScaleSpectrum(spectrum, actualBarCount, spectrumLength);
                    _processedSpectrum = SmoothSpectrum(scaledSpectrum, actualBarCount);
                }

                renderSpectrum = _processedSpectrum ??
                                 ProcessSynchronously(spectrum!, Math.Min(spectrum!.Length, barCount));
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
                SmartLogger.Log(LogLevel.Error, LogPrefix, "CubesRenderer not initialized before rendering");
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

            if (basePaint == null)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Base paint cannot be null");
                return false;
            }

            if (info.Width <= 0 || info.Height <= 0)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Invalid canvas dimensions");
                return false;
            }

            return true;
        }

        private float[] ProcessSynchronously(float[] spectrum, int targetCount)
        {
            int spectrumLength = spectrum.Length;
            var scaledSpectrum = ScaleSpectrum(spectrum, targetCount, spectrumLength);
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
            cubePaint.IsAntialias = _useAntiAlias;
            cubePaint.FilterQuality = _filterQuality;

            for (int i = 0; i < spectrum.Length; i++)
            {
                float magnitude = spectrum[i];
                if (magnitude < Constants.MinMagnitudeThreshold)
                    continue;

                float height = magnitude * canvasHeight;
                float x = i * (barWidth + barSpacing);
                float y = canvasHeight - height;

                if (canvas.QuickReject(new SKRect(x, y, x + barWidth, y + height)))
                    continue;

                cubePaint.Color = basePaint.Color.WithAlpha((byte)(magnitude * Constants.AlphaMultiplier));
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
            // Front face rendering with DrawRect
            canvas.DrawRect(x, y, barWidth, height, paint);

            if (_useAdvancedEffects)
            {
                float topRightX = x + barWidth;
                float topOffsetX = barWidth * Constants.CubeTopWidthProportion;
                float topOffsetY = barWidth * Constants.CubeTopHeightProportion;

                // Top face rendering
                _cubeTopPath.Reset();
                _cubeTopPath.MoveTo(x, y);
                _cubeTopPath.LineTo(topRightX, y);
                _cubeTopPath.LineTo(x + topOffsetX, y - topOffsetY);
                _cubeTopPath.LineTo(x - (barWidth - topOffsetX), y - topOffsetY);
                _cubeTopPath.Close();

                using var topPaint = paint.Clone();
                topPaint.Color = paint.Color.WithAlpha((byte)(magnitude * Constants.AlphaMultiplier * Constants.TopAlphaFactor));
                canvas.DrawPath(_cubeTopPath, topPaint);

                // Side face rendering
                using var sidePath = new SKPath();
                sidePath.MoveTo(topRightX, y);
                sidePath.LineTo(topRightX, y + height);
                sidePath.LineTo(x + topOffsetX, y - topOffsetY + height);
                sidePath.LineTo(x + topOffsetX, y - topOffsetY);
                sidePath.Close();

                using var sidePaint = paint.Clone();
                sidePaint.Color = paint.Color.WithAlpha((byte)(magnitude * Constants.AlphaMultiplier * Constants.SideFaceAlphaFactor));
                canvas.DrawPath(sidePath, sidePaint);
            }
        }
        #endregion

        #region Spectrum Processing
        private static float[] ScaleSpectrum(float[] spectrum, int targetCount, int spectrumLength)
        {
            float[] scaledSpectrum = new float[targetCount];
            float blockSize = (float)spectrumLength / targetCount;

            for (int i = 0; i < targetCount; i++)
            {
                int start = (int)Math.Floor(i * blockSize);
                int end = (int)Math.Ceiling((i + 1) * blockSize);
                end = end <= start ? start + 1 : Math.Min(end, spectrumLength);

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

            if (Vector.IsHardwareAccelerated && targetCount >= Vector<float>.Count)
            {
                int vectorSize = Vector<float>.Count;
                int vectorizedLength = targetCount - (targetCount % vectorSize);

                for (int i = 0; i < vectorizedLength; i += vectorSize)
                {
                    Vector<float> currentValues = new Vector<float>(spectrum, i);
                    Vector<float> previousValues = new Vector<float>(_previousSpectrum, i);
                    Vector<float> delta = currentValues - previousValues;
                    Vector<float> smoothedValues = previousValues + delta * _smoothingFactor;
                    smoothedValues.CopyTo(smoothedSpectrum, i);
                    smoothedValues.CopyTo(_previousSpectrum, i);
                }

                for (int i = vectorizedLength; i < targetCount; i++)
                {
                    float delta = spectrum[i] - _previousSpectrum[i];
                    smoothedSpectrum[i] = _previousSpectrum[i] + delta * _smoothingFactor;
                    _previousSpectrum[i] = smoothedSpectrum[i];
                }
            }
            else
            {
                for (int i = 0; i < targetCount; i++)
                {
                    float delta = spectrum[i] - _previousSpectrum[i];
                    smoothedSpectrum[i] = _previousSpectrum[i] + delta * _smoothingFactor;
                    _previousSpectrum[i] = smoothedSpectrum[i];
                }
            }

            return smoothedSpectrum;
        }
        #endregion

        #region Disposal
        protected void Dispose(bool disposing)
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
                SmartLogger.Log(LogLevel.Debug, LogPrefix, "CubesRenderer disposed");
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