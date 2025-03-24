#nullable enable

using System.Numerics;
using System.Runtime.CompilerServices;

namespace SpectrumNet
{
    /// <summary>
    /// Renderer that visualizes spectrum data as a vertical loudness meter with peak indicator.
    /// </summary>
    public sealed class LoudnessMeterRenderer : BaseSpectrumRenderer
    {
        #region Singleton Pattern
        private static readonly Lazy<LoudnessMeterRenderer> _instance = new(() => new LoudnessMeterRenderer());
        private LoudnessMeterRenderer() { } // Приватный конструктор
        public static LoudnessMeterRenderer GetInstance() => _instance.Value;
        #endregion

        #region Constants
        private static class Constants
        {
            // Logging
            public const string LOG_PREFIX = "LoudnessMeterRenderer";

            // Loudness calculation constants
            public const float MIN_LOUDNESS_THRESHOLD = 0.001f; // Minimum loudness to trigger rendering
            public const float SMOOTHING_FACTOR_NORMAL = 0.3f;   // Smoothing factor for normal mode
            public const float SMOOTHING_FACTOR_OVERLAY = 0.5f;   // Smoothing factor for overlay mode
            public const float PEAK_DECAY_RATE = 0.05f;  // Rate at which peak loudness decays

            // Rendering constants
            public const float GLOW_INTENSITY = 0.4f;   // Intensity factor for glow effect
            public const float HIGH_LOUDNESS_THRESHOLD = 0.7f;   // Threshold for high loudness (red)
            public const float MEDIUM_LOUDNESS_THRESHOLD = 0.4f;   // Threshold for medium loudness (yellow)
            public const float BORDER_WIDTH = 1.5f;   // Width of the border stroke
            public const float BLUR_SIGMA = 10f;    // Sigma value for blur mask filter
            public const float PEAK_RECT_HEIGHT = 4f;     // Height of the peak indicator rectangle
            public const float GLOW_HEIGHT_FACTOR = 1f / 3f;// Factor for glow height relative to meter
            public const int MARKER_COUNT = 10;     // Number of divisions for markers

            // Gradient constants
            public const float GRADIENT_ALPHA_FACTOR = 0.8f;   // Alpha transparency factor for gradient colors

            // Quality settings
            public static class Quality
            {
                // Low quality settings
                public const bool LOW_USE_ADVANCED_EFFECTS = false;

                // Medium quality settings
                public const bool MEDIUM_USE_ADVANCED_EFFECTS = true;

                // High quality settings
                public const bool HIGH_USE_ADVANCED_EFFECTS = true;
            }
        }
        #endregion

        #region Fields
        // Rendering resources
        private readonly ObjectPool<SKPaint> _paintPool = new(() => new SKPaint(), paint => paint.Reset(), 5);
        private SKPaint? _backgroundPaint;
        private SKPaint? _markerPaint;
        private SKPaint? _fillPaint;
        private SKPaint? _glowPaint;
        private SKPaint? _peakPaint;
        private SKPicture? _staticPicture;

        // Loudness state
        private float _previousLoudness;
        private float _peakLoudness;
        private float? _cachedLoudness;

        // Canvas dimensions
        private int _currentWidth;
        private int _currentHeight;

        // Quality-dependent settings
        private new bool _useAntiAlias = true;
        private new SKSamplingOptions _samplingOptions = new(SKFilterMode.Linear, SKMipmapMode.Linear);
        private new bool _useAdvancedEffects = true;

        // Synchronization and state
        private readonly SemaphoreSlim _renderSemaphore = new(1, 1);
        private readonly object _loudnessLock = new();
        private new bool _disposed;
        #endregion

        #region Initialization and Configuration
        /// <summary>
        /// Initializes the loudness meter renderer and prepares resources for rendering.
        /// </summary>
        public override void Initialize()
        {
            SmartLogger.Safe(() =>
            {
                base.Initialize();

                // Initialize state
                _previousLoudness = 0f;
                _peakLoudness = 0f;

                // Create required paints
                _backgroundPaint = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = Constants.BORDER_WIDTH,
                    Color = SKColors.White.WithAlpha(100),
                    IsAntialias = _useAntiAlias
                };

                _markerPaint = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 1,
                    Color = SKColors.White.WithAlpha(150),
                    IsAntialias = _useAntiAlias
                };

                _fillPaint = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    IsAntialias = _useAntiAlias
                };

                _glowPaint = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    IsAntialias = _useAntiAlias,
                    MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Constants.BLUR_SIGMA)
                };

                _peakPaint = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    IsAntialias = _useAntiAlias
                };

                SmartLogger.Log(LogLevel.Debug, Constants.LOG_PREFIX, "Initialized");
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.Initialize",
                ErrorMessage = "Failed to initialize renderer"
            });
        }

        /// <summary>
        /// Configures the renderer with overlay status and quality settings.
        /// </summary>
        /// <param name="isOverlayActive">Indicates if the renderer is used in overlay mode.</param>
        /// <param name="quality">The rendering quality level.</param>
        public override void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium)
        {
            SmartLogger.Safe(() =>
            {
                base.Configure(isOverlayActive, quality);

                // Update smoothing factor based on overlay mode
                _smoothingFactor = isOverlayActive ?
                    Constants.SMOOTHING_FACTOR_OVERLAY :
                    Constants.SMOOTHING_FACTOR_NORMAL;

                // Update quality if needed
                if (_quality != quality)
                {
                    _quality = quality;
                    ApplyQualitySettings();
                }
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.Configure",
                ErrorMessage = "Failed to configure renderer"
            });
        }

        /// <summary>
        /// Applies quality settings based on the current quality level.
        /// </summary>
        protected override void ApplyQualitySettings()
        {
            SmartLogger.Safe(() =>
            {
                base.ApplyQualitySettings();

                switch (_quality)
                {
                    case RenderQuality.Low:
                        _useAntiAlias = false;
                        _samplingOptions = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);
                        _useAdvancedEffects = Constants.Quality.LOW_USE_ADVANCED_EFFECTS;
                        break;

                    case RenderQuality.Medium:
                        _useAntiAlias = true;
                        _samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                        _useAdvancedEffects = Constants.Quality.MEDIUM_USE_ADVANCED_EFFECTS;
                        break;

                    case RenderQuality.High:
                        _useAntiAlias = true;
                        _samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                        _useAdvancedEffects = Constants.Quality.HIGH_USE_ADVANCED_EFFECTS;
                        break;
                }

                // Обновляем только IsAntialias, не используя устаревшее FilterQuality
                if (_backgroundPaint != null) _backgroundPaint.IsAntialias = _useAntiAlias;
                if (_markerPaint != null) _markerPaint.IsAntialias = _useAntiAlias;
                if (_fillPaint != null) _fillPaint.IsAntialias = _useAntiAlias;
                if (_glowPaint != null) _glowPaint.IsAntialias = _useAntiAlias;
                if (_peakPaint != null) _peakPaint.IsAntialias = _useAntiAlias;

                // Сбрасываем статический кэш для пересоздания
                _staticPicture?.Dispose();
                _staticPicture = null;
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.ApplyQualitySettings",
                ErrorMessage = "Failed to apply quality settings"
            });
        }
        #endregion

        #region Rendering
        /// <summary>
        /// Renders the loudness meter visualization on the canvas using spectrum data.
        /// </summary>
        public override void Render(
            SKCanvas? canvas,
            float[]? spectrum,
            SKImageInfo info,
            float barWidth,
            float barSpacing,
            int barCount,
            SKPaint? paint,
            Action<SKCanvas, SKImageInfo>? drawPerformanceInfo)
        {
            // Validate rendering parameters
            if (!ValidateRenderParameters(canvas, spectrum, info, paint))
            {
                drawPerformanceInfo?.Invoke(canvas!, info);
                return;
            }

            // Quick reject if canvas area is not visible
            if (canvas!.QuickReject(new SKRect(0, 0, info.Width, info.Height)))
            {
                drawPerformanceInfo?.Invoke(canvas, info);
                return;
            }

            SmartLogger.Safe(() =>
            {
                float loudness = 0f;
                bool semaphoreAcquired = false;

                try
                {
                    // Try to acquire semaphore for updating state
                    semaphoreAcquired = _renderSemaphore.Wait(0);

                    if (semaphoreAcquired)
                    {
                        // Process new loudness value
                        loudness = CalculateAndSmoothLoudness(spectrum!);
                        _cachedLoudness = loudness;

                        // Update peak loudness
                        if (loudness > _peakLoudness)
                        {
                            _peakLoudness = loudness;
                        }
                        else
                        {
                            _peakLoudness = Math.Max(0, _peakLoudness - Constants.PEAK_DECAY_RATE);
                        }
                    }
                    else
                    {
                        // Use cached loudness if semaphore not available
                        lock (_loudnessLock)
                        {
                            loudness = _cachedLoudness ?? CalculateAndSmoothLoudness(spectrum!);
                        }
                    }
                }
                finally
                {
                    // Release semaphore if acquired
                    if (semaphoreAcquired)
                    {
                        _renderSemaphore.Release();
                    }
                }

                // Update static elements if canvas size changed or if static picture doesn't exist
                if (info.Width != _currentWidth || info.Height != _currentHeight || _staticPicture == null)
                {
                    _currentWidth = info.Width;
                    _currentHeight = info.Height;
                    UpdateStaticElements();
                }

                // Render the meter with current loudness values
                RenderEnhancedMeter(canvas!, info, loudness, _peakLoudness);
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.Render",
                ErrorMessage = "Error during rendering"
            });

            // Draw performance info
            drawPerformanceInfo?.Invoke(canvas!, info);
        }

        /// <summary>
        /// Validates all render parameters before processing.
        /// </summary>
        private bool ValidateRenderParameters(SKCanvas? canvas, float[]? spectrum, SKImageInfo info, SKPaint? paint)
        {
            if (canvas == null)
            {
                SmartLogger.Log(LogLevel.Error, Constants.LOG_PREFIX, "Canvas is null");
                return false;
            }

            if (spectrum == null || spectrum.Length == 0)
            {
                SmartLogger.Log(LogLevel.Error, Constants.LOG_PREFIX, "Spectrum is null or empty");
                return false;
            }

            if (paint == null)
            {
                SmartLogger.Log(LogLevel.Error, Constants.LOG_PREFIX, "Paint is null");
                return false;
            }

            if (info.Width <= 0 || info.Height <= 0)
            {
                SmartLogger.Log(LogLevel.Error, Constants.LOG_PREFIX, "Invalid canvas dimensions");
                return false;
            }

            if (_disposed)
            {
                SmartLogger.Log(LogLevel.Error, Constants.LOG_PREFIX, "Renderer is disposed");
                return false;
            }

            return true;
        }
        #endregion

        #region Rendering Implementation
        /// <summary>
        /// Updates static elements of the meter (background and markers).
        /// </summary>
        private void UpdateStaticElements()
        {
            SmartLogger.Safe(() =>
            {
                // Проверка на валидность размеров
                if (_currentWidth <= 0 || _currentHeight <= 0)
                {
                    SmartLogger.Log(LogLevel.Warning, Constants.LOG_PREFIX,
                        $"Invalid dimensions for static elements: {_currentWidth}x{_currentHeight}");
                    return;
                }

                // Create gradient shader for meter fill
                if (_fillPaint != null)
                {
                    var gradientShader = SKShader.CreateLinearGradient(
                        new SKPoint(0, _currentHeight),
                        new SKPoint(0, 0),
                        new[]
                        {
                            SKColors.Green.WithAlpha((byte)(255 * Constants.GRADIENT_ALPHA_FACTOR)),
                            SKColors.Yellow.WithAlpha((byte)(255 * Constants.GRADIENT_ALPHA_FACTOR)),
                            SKColors.Red.WithAlpha((byte)(255 * Constants.GRADIENT_ALPHA_FACTOR))
                        },
                        new[] { 0f, 0.5f, 1.0f },
                        SKShaderTileMode.Clamp);

                    _fillPaint.Shader = gradientShader;
                }

                // Create SKPicture with static elements to improve performance
                using var recorder = new SKPictureRecorder();
                var rect = new SKRect(0, 0, _currentWidth, _currentHeight);
                using var canvas = recorder.BeginRecording(rect);

                // Draw border
                if (_backgroundPaint != null)
                {
                    canvas.DrawRect(0, 0, _currentWidth, _currentHeight, _backgroundPaint);
                }

                // Draw markers
                if (_markerPaint != null)
                {
                    for (int i = 1; i < Constants.MARKER_COUNT; i++)
                    {
                        float y = _currentHeight - (_currentHeight * i / (float)Constants.MARKER_COUNT);
                        canvas.DrawLine(0, y, _currentWidth, y, _markerPaint);
                    }
                }

                // Dispose previous picture and store new one
                _staticPicture?.Dispose();
                _staticPicture = recorder.EndRecording();
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.UpdateStaticElements",
                ErrorMessage = "Failed to update static elements"
            });
        }

        /// <summary>
        /// Renders the meter with current loudness and peak values.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RenderEnhancedMeter(
            SKCanvas canvas,
            SKImageInfo info,
            float loudness,
            float peakLoudness)
        {
            if (loudness < Constants.MIN_LOUDNESS_THRESHOLD) return;

            canvas.Save();

            try
            {
                // Рисуем статические элементы (фон и деления)
                if (_staticPicture != null)
                {
                    // Используем кэшированную картинку, если она есть
                    canvas.DrawPicture(_staticPicture);
                }
                else
                {
                    // Если картинки нет, рисуем напрямую
                    if (_backgroundPaint != null)
                    {
                        canvas.DrawRect(0, 0, info.Width, info.Height, _backgroundPaint);
                    }

                    if (_markerPaint != null)
                    {
                        for (int i = 1; i < Constants.MARKER_COUNT; i++)
                        {
                            float y = info.Height - (info.Height * i / (float)Constants.MARKER_COUNT);
                            canvas.DrawLine(0, y, info.Width, y, _markerPaint);
                        }
                    }

                    // Попробуем создать _staticPicture для следующего кадра
                    if (_currentWidth <= 0 || _currentHeight <= 0)
                    {
                        _currentWidth = info.Width;
                        _currentHeight = info.Height;
                        UpdateStaticElements();
                    }
                }

                // Calculate heights
                float meterHeight = info.Height * loudness;
                float peakHeight = info.Height * peakLoudness;

                // Draw meter fill with gradient
                if (_fillPaint != null)
                {
                    canvas.DrawRect(0, info.Height - meterHeight, info.Width, meterHeight, _fillPaint);
                }

                // Draw glow effect for high loudness levels
                if (_useAdvancedEffects && loudness > Constants.HIGH_LOUDNESS_THRESHOLD && _glowPaint != null)
                {
                    byte alpha = (byte)(255 * Constants.GLOW_INTENSITY *
                        (loudness - Constants.HIGH_LOUDNESS_THRESHOLD) /
                        (1 - Constants.HIGH_LOUDNESS_THRESHOLD));

                    _glowPaint.Color = SKColors.Red.WithAlpha(alpha);
                    canvas.DrawRect(0, info.Height - meterHeight,
                        info.Width, meterHeight * Constants.GLOW_HEIGHT_FACTOR, _glowPaint);
                }

                // Draw peak indicator
                if (_peakPaint != null)
                {
                    float peakLineY = info.Height - peakHeight;

                    // Set peak color based on loudness level
                    _peakPaint.Color = loudness > Constants.HIGH_LOUDNESS_THRESHOLD ? SKColors.Red :
                                    loudness > Constants.MEDIUM_LOUDNESS_THRESHOLD ? SKColors.Yellow :
                                    SKColors.Green;

                    canvas.DrawRect(0, peakLineY - Constants.PEAK_RECT_HEIGHT / 2,
                        info.Width, Constants.PEAK_RECT_HEIGHT, _peakPaint);
                }
            }
            finally
            {
                canvas.Restore();
            }
        }

        /// <summary>
        /// Calculates and smooths loudness value from spectrum data.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private float CalculateAndSmoothLoudness(float[] spectrum)
        {
            float rawLoudness = CalculateLoudness(spectrum.AsSpan());
            float smoothedLoudness = _previousLoudness + (rawLoudness - _previousLoudness) * _smoothingFactor;
            smoothedLoudness = Math.Clamp(smoothedLoudness, Constants.MIN_LOUDNESS_THRESHOLD, 1f);
            _previousLoudness = smoothedLoudness;
            return smoothedLoudness;
        }

        /// <summary>
        /// Calculates loudness level from spectrum data.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float CalculateLoudness(ReadOnlySpan<float> spectrum)
        {
            if (spectrum.IsEmpty) return 0f;

            float sum = 0f;

            // Use SIMD if possible for better performance
            if (Vector.IsHardwareAccelerated && spectrum.Length >= Vector<float>.Count)
            {
                int vectorSize = Vector<float>.Count;
                int vectorizedLength = spectrum.Length - (spectrum.Length % vectorSize);
                int i = 0;

                // Process vectors
                Vector<float> sumVector = Vector<float>.Zero;
                for (; i < vectorizedLength; i += vectorSize)
                {
                    Vector<float> values = new Vector<float>(spectrum.Slice(i, vectorSize));
                    sumVector += Vector.Abs(values);
                }

                // Sum vector elements
                for (int j = 0; j < vectorSize; j++)
                {
                    sum += sumVector[j];
                }

                // Process remaining elements
                for (; i < spectrum.Length; i++)
                {
                    sum += Math.Abs(spectrum[i]);
                }
            }
            else
            {
                // Standard processing
                for (int i = 0; i < spectrum.Length; i++)
                {
                    sum += Math.Abs(spectrum[i]);
                }
            }

            return Math.Clamp(sum / spectrum.Length, 0f, 1f);
        }
        #endregion

        #region Disposal
        /// <summary>
        /// Disposes of resources used by the renderer.
        /// </summary>
        public override void Dispose()
        {
            if (!_disposed)
            {
                SmartLogger.Safe(() =>
                {
                    // Dispose synchronization primitives
                    _renderSemaphore?.Dispose();

                    // Dispose rendering resources
                    _staticPicture?.Dispose();
                    _backgroundPaint?.Dispose();
                    _markerPaint?.Dispose();
                    _fillPaint?.Dispose();
                    _glowPaint?.Dispose();
                    _peakPaint?.Dispose();

                    // Dispose object pools
                    _paintPool?.Dispose();

                    // Clean up cached data
                    _cachedLoudness = null;

                    // Call base implementation
                    base.Dispose();
                }, new SmartLogger.ErrorHandlingOptions
                {
                    Source = $"{Constants.LOG_PREFIX}.Dispose",
                    ErrorMessage = "Error during disposal"
                });

                _disposed = true;
                SmartLogger.Log(LogLevel.Debug, Constants.LOG_PREFIX, "Disposed");
            }
        }
        #endregion
    }
}