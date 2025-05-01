#nullable enable

using SpectrumNet.Service.Enums;

namespace SpectrumNet.Views.Renderers;

/// <summary>
/// Renderer that visualizes spectrum data as a mirrored waveform with fill and glow effects.
/// </summary>
public sealed class WaveformRenderer : BaseSpectrumRenderer
{
    #region Singleton Pattern
    private static readonly Lazy<WaveformRenderer> _instance = new(() => new WaveformRenderer());
    private WaveformRenderer() { } // Приватный конструктор
    public static WaveformRenderer GetInstance() => _instance.Value;
    #endregion

    #region Constants
    private static class Constants
    {
        // Logging
        public const string LOG_PREFIX = "WaveformRenderer";

        // Spectrum processing constants
        public const float SMOOTHING_FACTOR_NORMAL = 0.3f;  // Smoothing factor for normal mode
        public const float SMOOTHING_FACTOR_OVERLAY = 0.5f;  // Smoothing factor for overlay mode
        public const float MIN_MAGNITUDE_THRESHOLD = 0.01f; // Minimum magnitude threshold for rendering
        public const float MAX_SPECTRUM_VALUE = 1.5f;  // Maximum spectrum value for clamping

        // Rendering constants
        public const float MIN_STROKE_WIDTH = 2.0f;  // Minimum stroke width for waveform lines
        public const byte FILL_ALPHA = 64;    // Alpha value for waveform fill
        public const float GLOW_INTENSITY = 0.4f;  // Intensity of glow effect
        public const float GLOW_RADIUS = 3.0f;  // Blur radius for glow effect
        public const float HIGHLIGHT_ALPHA = 0.7f;  // Alpha value for highlight effect
        public const float HIGH_AMPLITUDE_THRESHOLD = 0.6f;  // Threshold for high amplitude effects

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
    private readonly ObjectPool<SKPath> _pathPool = new(() => new SKPath(), path => path.Reset(), 3);
    private readonly ObjectPool<SKPaint> _paintPool = new(() => new SKPaint(), paint => paint.Reset(), 4);
    private readonly SKPath _topPath = new();
    private readonly SKPath _bottomPath = new();
    private readonly SKPath _fillPath = new();

    // Quality-dependent settings
    private new bool _useAntiAlias = true;
    private new SKSamplingOptions _samplingOptions = new(SKFilterMode.Linear, SKMipmapMode.Linear);
    private new bool _useAdvancedEffects = true;

    // Synchronization and state
    private readonly SemaphoreSlim _renderSemaphore = new(1, 1);
    private new bool _disposed;
    #endregion

    #region Initialization and Configuration
    /// <summary>
    /// Initializes the waveform renderer and prepares resources for rendering.
    /// </summary>
    public override void Initialize()
    {
        Safe(() =>
        {
            base.Initialize();

            // Apply initial quality settings
            ApplyQualitySettings();

            Log(LogLevel.Debug, Constants.LOG_PREFIX, "Initialized");
        }, new ErrorHandlingOptions
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
        Safe(() =>
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
        }, new ErrorHandlingOptions
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
        Safe(() =>
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
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.ApplyQualitySettings",
            ErrorMessage = "Failed to apply quality settings"
        });
    }
    #endregion

    #region Rendering
    /// <summary>
    /// Renders the waveform visualization on the canvas using spectrum data.
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

        Safe(() =>
        {
            float[] renderSpectrum;
            bool semaphoreAcquired = false;
            try
            {
                // Try to acquire semaphore for updating spectrum data
                semaphoreAcquired = _renderSemaphore.Wait(0);
                if (semaphoreAcquired)
                {
                    // Process spectrum data in background
                    ProcessSpectrumData(spectrum!, barCount);
                }

                // Get spectrum data for rendering
                lock (_spectrumLock)
                {
                    renderSpectrum = _processedSpectrum ??
                                     ProcessSpectrumSynchronously(spectrum!, barCount);
                }

                // Render waveform using processed spectrum data
                RenderWaveform(canvas, renderSpectrum, info, paint!);
            }
            finally
            {
                // Release semaphore if acquired
                if (semaphoreAcquired)
                {
                    _renderSemaphore.Release();
                }
            }
        }, new ErrorHandlingOptions
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
        if (canvas == null || spectrum == null || paint == null)
        {
            Log(LogLevel.Error, Constants.LOG_PREFIX, "Invalid render parameters: null values");
            return false;
        }

        if (info.Width <= 0 || info.Height <= 0)
        {
            Log(LogLevel.Error, Constants.LOG_PREFIX, $"Invalid image dimensions: {info.Width}x{info.Height}");
            return false;
        }

        if (spectrum.Length < 2)
        {
            Log(LogLevel.Warning, Constants.LOG_PREFIX, "Spectrum must have at least 2 elements");
            return false;
        }

        if (_disposed)
        {
            Log(LogLevel.Error, Constants.LOG_PREFIX, "Renderer is disposed");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Processes spectrum data for visualization.
    /// </summary>
    private void ProcessSpectrumData(float[] spectrum, int barCount)
    {
        Safe(() =>
        {
            // Ensure spectrum buffer is initialized
            EnsureSpectrumBuffer(spectrum.Length);

            int spectrumLength = spectrum.Length;
            int actualBarCount = Min(spectrumLength, barCount);

            // Scale spectrum data to target bar count
            float[] scaledSpectrum = ScaleSpectrum(spectrum, actualBarCount, spectrumLength);

            // Apply smoothing for transitions
            _processedSpectrum = SmoothSpectrum(scaledSpectrum, actualBarCount);
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.ProcessSpectrumData",
            ErrorMessage = "Error processing spectrum data"
        });
    }

    /// <summary>
    /// Processes spectrum data synchronously when async processing isn't available.
    /// </summary>
    private float[] ProcessSpectrumSynchronously(float[] spectrum, int barCount)
    {
        int spectrumLength = spectrum.Length;
        int actualBarCount = Min(spectrumLength, barCount);
        float[] scaledSpectrum = ScaleSpectrum(spectrum, actualBarCount, spectrumLength);
        return SmoothSpectrum(scaledSpectrum, actualBarCount);
    }

    /// <summary>
    /// Ensures the spectrum buffer is of the correct size.
    /// </summary>
    private void EnsureSpectrumBuffer(int length)
    {
        Safe(() =>
        {
            if (_previousSpectrum == null || _previousSpectrum.Length != length)
            {
                _previousSpectrum = new float[length];
            }
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.EnsureSpectrumBuffer",
            ErrorMessage = "Error ensuring spectrum buffer"
        });
    }
    #endregion

    #region Rendering Implementation
    /// <summary>
    /// Renders the waveform visualization using processed spectrum data.
    /// </summary>
    private void RenderWaveform(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        SKPaint basePaint)
    {
        Safe(() =>
        {
            float midY = info.Height / 2;
            float xStep = (float)info.Width / spectrum.Length;

            // Create waveform paths
            CreateWavePaths(spectrum, midY, xStep);
            CreateFillPath(spectrum, midY, xStep, info.Width);

            // Setup paints from pool
            using var waveformPaint = _paintPool.Get();
            using var fillPaint = _paintPool.Get();
            using var glowPaint = _useAdvancedEffects ? _paintPool.Get() : null;
            using var highlightPaint = _useAdvancedEffects ? _paintPool.Get() : null;

            // Configure waveform paint
            waveformPaint.Style = Stroke;
            waveformPaint.StrokeWidth = Max(Constants.MIN_STROKE_WIDTH, 50f / spectrum.Length);
            waveformPaint.IsAntialias = _useAntiAlias;
            waveformPaint.StrokeCap = SKStrokeCap.Round;
            waveformPaint.StrokeJoin = SKStrokeJoin.Round;
            waveformPaint.Color = basePaint.Color;

            // Configure fill paint
            fillPaint.Style = Fill;
            fillPaint.Color = basePaint.Color.WithAlpha(Constants.FILL_ALPHA);
            fillPaint.IsAntialias = _useAntiAlias;

            // Configure glow paint if advanced effects are enabled
            if (glowPaint != null && _useAdvancedEffects)
            {
                glowPaint.Style = Stroke;
                glowPaint.StrokeWidth = waveformPaint.StrokeWidth * 1.5f;
                glowPaint.Color = basePaint.Color;
                glowPaint.IsAntialias = _useAntiAlias;
                glowPaint.MaskFilter = SKMaskFilter.CreateBlur(Normal, Constants.GLOW_RADIUS);
            }

            // Configure highlight paint if advanced effects are enabled
            if (highlightPaint != null && _useAdvancedEffects)
            {
                highlightPaint.Style = Stroke;
                highlightPaint.StrokeWidth = waveformPaint.StrokeWidth * 0.6f;
                highlightPaint.Color = SKColors.White.WithAlpha((byte)(255 * Constants.HIGHLIGHT_ALPHA));
                highlightPaint.IsAntialias = _useAntiAlias;
            }

            // Render glow effect for high amplitude sections
            if (glowPaint != null && _useAdvancedEffects)
            {
                bool hasHighAmplitude = HasHighAmplitude(spectrum);
                if (hasHighAmplitude)
                {
                    glowPaint.Color = glowPaint.Color.WithAlpha((byte)(255 * Constants.GLOW_INTENSITY));
                    canvas.DrawPath(_topPath, glowPaint);
                    canvas.DrawPath(_bottomPath, glowPaint);
                }
            }

            // Render waveform fill and outlines
            canvas.DrawPath(_fillPath, fillPaint);
            canvas.DrawPath(_topPath, waveformPaint);
            canvas.DrawPath(_bottomPath, waveformPaint);

            // Render highlights for high amplitude points
            if (highlightPaint != null && _useAdvancedEffects)
            {
                RenderHighlights(canvas, spectrum, midY, xStep, highlightPaint);
            }
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.RenderWaveform",
            ErrorMessage = "Failed to render waveform"
        });
    }

    /// <summary>
    /// Checks if spectrum contains high amplitude values that require special effects.
    /// </summary>
    [MethodImpl(AggressiveInlining)]
    private bool HasHighAmplitude(float[] spectrum)
    {
        if (IsHardwareAccelerated && spectrum.Length >= Vector<float>.Count)
        {
            // Use vectorization for faster checking
            Vector<float> threshold = new Vector<float>(Constants.HIGH_AMPLITUDE_THRESHOLD);
            int vectorSize = Vector<float>.Count;
            int vectorizedLength = spectrum.Length - spectrum.Length % vectorSize;

            for (int i = 0; i < vectorizedLength; i += vectorSize)
            {
                Vector<float> values = new Vector<float>(spectrum, i);
                if (GreaterThanAny(values, threshold))
                {
                    return true;
                }
            }

            // Check remaining elements
            for (int i = vectorizedLength; i < spectrum.Length; i++)
            {
                if (spectrum[i] > Constants.HIGH_AMPLITUDE_THRESHOLD)
                {
                    return true;
                }
            }

            return false;
        }
        else
        {
            // Standard approach for smaller arrays
            for (int i = 0; i < spectrum.Length; i++)
            {
                if (spectrum[i] > Constants.HIGH_AMPLITUDE_THRESHOLD)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Renders highlight points at high amplitude locations.
    /// </summary>
    private void RenderHighlights(SKCanvas canvas, float[] spectrum, float midY, float xStep, SKPaint highlightPaint)
    {
        for (int i = 0; i < spectrum.Length; i++)
        {
            if (spectrum[i] > Constants.HIGH_AMPLITUDE_THRESHOLD)
            {
                float x = i * xStep;
                float topY = midY - spectrum[i] * midY;
                float bottomY = midY + spectrum[i] * midY;

                canvas.DrawPoint(x, topY, highlightPaint);
                canvas.DrawPoint(x, bottomY, highlightPaint);
            }
        }
    }

    /// <summary>
    /// Creates the top and bottom wave paths for the waveform visualization.
    /// </summary>
    private void CreateWavePaths(float[] spectrum, float midY, float xStep)
    {
        _topPath.Reset();
        _bottomPath.Reset();

        float x = 0;
        float topY = midY - spectrum[0] * midY;
        float bottomY = midY + spectrum[0] * midY;

        _topPath.MoveTo(x, topY);
        _bottomPath.MoveTo(x, bottomY);

        for (int i = 1; i < spectrum.Length; i++)
        {
            float prevX = (i - 1) * xStep;
            float prevTopY = midY - spectrum[i - 1] * midY;
            float prevBottomY = midY + spectrum[i - 1] * midY;

            x = i * xStep;
            topY = midY - spectrum[i] * midY;
            bottomY = midY + spectrum[i] * midY;

            float controlX = (prevX + x) / 2;
            _topPath.CubicTo(controlX, prevTopY, controlX, topY, x, topY);
            _bottomPath.CubicTo(controlX, prevBottomY, controlX, bottomY, x, bottomY);
        }
    }

    /// <summary>
    /// Creates the fill path for the waveform visualization.
    /// </summary>
    private void CreateFillPath(float[] spectrum, float midY, float xStep, float width)
    {
        _fillPath.Reset();

        float startX = 0;
        float startTopY = midY - spectrum[0] * midY;
        _fillPath.MoveTo(startX, startTopY);

        for (int i = 1; i < spectrum.Length; i++)
        {
            float prevX = (i - 1) * xStep;
            float prevTopY = midY - spectrum[i - 1] * midY;

            float x = i * xStep;
            float topY = midY - spectrum[i] * midY;

            float controlX = (prevX + x) / 2;
            _fillPath.CubicTo(controlX, prevTopY, controlX, topY, x, topY);
        }

        float endX = (spectrum.Length - 1) * xStep;
        float endBottomY = midY + spectrum[spectrum.Length - 1] * midY;
        _fillPath.LineTo(endX, endBottomY);

        for (int i = spectrum.Length - 2; i >= 0; i--)
        {
            float prevX = (i + 1) * xStep;
            float prevBottomY = midY + spectrum[i + 1] * midY;

            float x = i * xStep;
            float bottomY = midY + spectrum[i] * midY;

            float controlX = (prevX + x) / 2;
            _fillPath.CubicTo(controlX, prevBottomY, controlX, bottomY, x, bottomY);
        }

        _fillPath.Close();
    }
    #endregion

    #region Spectrum Processing
    /// <summary>
    /// Scales the spectrum data to the target count of bars.
    /// </summary>
    [MethodImpl(AggressiveOptimization)]
    private float[] ScaleSpectrum(float[] spectrum, int targetCount, int spectrumLength)
    {
        float[] scaledSpectrum = new float[targetCount];
        float blockSize = (float)spectrumLength / targetCount;

        if (IsHardwareAccelerated && spectrumLength >= Vector<float>.Count)
        {
            int chunkSize = Min(128, targetCount);

            // Parallel processing for large arrays
            Parallel.For(0, (targetCount + chunkSize - 1) / chunkSize, chunkIndex =>
            {
                int startIdx = chunkIndex * chunkSize;
                int endIdx = Min(startIdx + chunkSize, targetCount);

                for (int i = startIdx; i < endIdx; i++)
                {
                    int start = (int)(i * blockSize);
                    int end = (int)((i + 1) * blockSize);
                    int actualEnd = Min(end, spectrumLength);
                    int count = actualEnd - start;

                    if (count <= 0)
                    {
                        scaledSpectrum[i] = 0;
                        continue;
                    }

                    // Vectorized sum calculation
                    float sum = 0;
                    if (count >= Vector<float>.Count)
                    {
                        Vector<float> sumVector = Vector<float>.Zero;
                        int vectorizedCount = count - count % Vector<float>.Count;

                        for (int j = 0; j < vectorizedCount; j += Vector<float>.Count)
                        {
                            Vector<float> values = new Vector<float>(spectrum, start + j);
                            sumVector += values;
                        }

                        for (int j = 0; j < Vector<float>.Count; j++)
                        {
                            sum += sumVector[j];
                        }

                        for (int j = start + vectorizedCount; j < actualEnd; j++)
                        {
                            sum += spectrum[j];
                        }
                    }
                    else
                    {
                        for (int j = start; j < actualEnd; j++)
                        {
                            sum += spectrum[j];
                        }
                    }

                    scaledSpectrum[i] = sum / count;
                }
            });
        }
        else
        {
            // Sequential processing for small arrays
            for (int i = 0; i < targetCount; i++)
            {
                float sum = 0;
                int start = (int)(i * blockSize);
                int end = (int)((i + 1) * blockSize);
                int actualEnd = Min(end, spectrumLength);
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
        }

        return scaledSpectrum;
    }

    /// <summary>
    /// Applies smoothing to the spectrum data.
    /// </summary>
    [MethodImpl(AggressiveOptimization)]
    private float[] SmoothSpectrum(float[] spectrum, int targetCount)
    {
        if (_previousSpectrum == null || _previousSpectrum.Length != targetCount)
        {
            _previousSpectrum = new float[targetCount];
            Array.Copy(spectrum, _previousSpectrum, targetCount);
            return spectrum;
        }

        var smoothedSpectrum = new float[targetCount];

        if (IsHardwareAccelerated && targetCount >= Vector<float>.Count)
        {
            // Vectorized smoothing
            Vector<float> smoothingFactor = new Vector<float>(_smoothingFactor);
            int vectorizedLength = targetCount - targetCount % Vector<float>.Count;

            for (int i = 0; i < vectorizedLength; i += Vector<float>.Count)
            {
                Vector<float> current = new Vector<float>(spectrum, i);
                Vector<float> previous = new Vector<float>(_previousSpectrum, i);
                Vector<float> diff = current - previous;
                Vector<float> smoothed = previous + diff * smoothingFactor;

                // Apply clamping
                Vector<float> minThreshold = new Vector<float>(Constants.MIN_MAGNITUDE_THRESHOLD);
                Vector<float> maxThreshold = new Vector<float>(Constants.MAX_SPECTRUM_VALUE);
                smoothed = Max(smoothed, minThreshold);
                smoothed = Min(smoothed, maxThreshold);

                smoothed.CopyTo(smoothedSpectrum, i);
                smoothed.CopyTo(_previousSpectrum, i);
            }

            // Process remaining elements
            for (int i = vectorizedLength; i < targetCount; i++)
            {
                float currentValue = spectrum[i];
                float previousValue = _previousSpectrum[i];
                float smoothedValue = previousValue + (currentValue - previousValue) * _smoothingFactor;
                smoothedSpectrum[i] = Clamp(smoothedValue, Constants.MIN_MAGNITUDE_THRESHOLD, Constants.MAX_SPECTRUM_VALUE);
                _previousSpectrum[i] = smoothedSpectrum[i];
            }
        }
        else
        {
            // Standard smoothing
            for (int i = 0; i < targetCount; i++)
            {
                float currentValue = spectrum[i];
                float previousValue = _previousSpectrum[i];
                float smoothedValue = previousValue + (currentValue - previousValue) * _smoothingFactor;
                smoothedSpectrum[i] = Clamp(smoothedValue, Constants.MIN_MAGNITUDE_THRESHOLD, Constants.MAX_SPECTRUM_VALUE);
                _previousSpectrum[i] = smoothedSpectrum[i];
            }
        }

        return smoothedSpectrum;
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
            Safe(() =>
            {
                // Dispose synchronization primitives
                _renderSemaphore?.Dispose();

                // Dispose paths
                _topPath?.Dispose();
                _bottomPath?.Dispose();
                _fillPath?.Dispose();

                // Dispose object pools
                _pathPool?.Dispose();
                _paintPool?.Dispose();

                // Clean up cached data
                _previousSpectrum = null;
                _processedSpectrum = null;

                // Call base implementation
                base.Dispose();
            }, new ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.Dispose",
                ErrorMessage = "Error during disposal"
            });

            _disposed = true;
            Log(LogLevel.Debug, Constants.LOG_PREFIX, "Disposed");
        }
    }
    #endregion
}