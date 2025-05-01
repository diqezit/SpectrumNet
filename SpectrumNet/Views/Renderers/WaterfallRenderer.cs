#nullable enable

using SpectrumNet.Service.Enums;

namespace SpectrumNet.Views.Renderers;

/// <summary>
/// Renderer that visualizes spectrum data as a waterfall spectrogram display.
/// </summary>
public sealed class WaterfallRenderer : BaseSpectrumRenderer
{
    #region Singleton Pattern
    private static readonly Lazy<WaterfallRenderer> _instance = new(() => new WaterfallRenderer());
    private WaterfallRenderer() { } // Приватный конструктор
    public static WaterfallRenderer GetInstance() => _instance.Value;
    #endregion

    #region Constants
    private static class Constants
    {
        // Logging
        public const string LOG_PREFIX = "WaterfallRenderer";

        // Spectrum processing constants
        public const float MIN_SIGNAL = 1e-6f;                  // Minimum signal value to avoid log(0)
        public const int DEFAULT_BUFFER_HEIGHT = 256;           // Default spectrogram buffer height
        public const int OVERLAY_BUFFER_HEIGHT = 128;           // Buffer height when overlay is active
        public const int DEFAULT_SPECTRUM_WIDTH = 1024;         // Default spectrum width
        public const int MIN_BAR_COUNT = 10;                    // Minimum number of bars

        // Color palette
        public const int COLOR_PALETTE_SIZE = 256;              // Size of the color palette array
        public const float BLUE_THRESHOLD = 0.25f;              // Threshold for deep blue to light blue
        public const float CYAN_THRESHOLD = 0.5f;               // Threshold for cyan to yellow
        public const float YELLOW_THRESHOLD = 0.75f;            // Threshold for yellow to red

        // Rendering settings
        public const float DETAIL_SCALE_FACTOR = 0.8f;          // Factor for detail scaling based on bar width
        public const float ZOOM_THRESHOLD = 2.0f;               // Threshold for applying zoom-based enhancement
        public const int LOW_BAR_COUNT_THRESHOLD = 64;          // Threshold for low detail rendering
        public const int HIGH_BAR_COUNT_THRESHOLD = 256;        // Threshold for high detail rendering

        // Synchronization
        public const int SEMAPHORE_TIMEOUT_MS = 5;              // Timeout for semaphore wait in ms

        // Quality settings
        public static class Quality
        {
            // Low quality settings
            public const bool LOW_USE_ADVANCED_EFFECTS = false;

            // Medium quality settings
            public const bool MEDIUM_USE_ADVANCED_EFFECTS = true;

            // High quality settings
            public const bool HIGH_USE_ADVANCED_EFFECTS = true;

            // Color enhancement factors
            public const float LOW_COLOR_ENHANCEMENT = 0.9f;
            public const float MEDIUM_COLOR_ENHANCEMENT = 1.0f;
            public const float HIGH_COLOR_ENHANCEMENT = 1.2f;
        }
    }
    #endregion

    #region Fields
    // Overlay mode flag
    private readonly bool _isOverlayMode;

    // Quality settings
    private new bool _useAntiAlias = true;
    private new bool _useAdvancedEffects = true;
    private new SKSamplingOptions _samplingOptions = new(SKFilterMode.Linear, SKMipmapMode.Linear);
    private float _colorEnhancement = 1.0f;

    // Cached spectrogram data
    private float[]? _currentSpectrum;
    private float[][]? _spectrogramBuffer;
    private int _bufferHead;
    private bool _needsBufferResize;

    // Waterfall bitmap used for final rendering
    private SKBitmap? _waterfallBitmap;

    // Bar layout parameters caching
    private float _lastBarWidth;
    private int _lastBarCount;
    private float _lastBarSpacing;

    // Color palette (cached as int array)
    private static readonly int[] _colorPalette = InitColorPalette();

    // Synchronization
    private readonly SemaphoreSlim _renderSemaphore = new(1, 1);
    private new bool _disposed;

    // Object pool for spectrum arrays
    private readonly ObjectPool<float[]> _spectrumPool = new(
        () => new float[Constants.DEFAULT_SPECTRUM_WIDTH],
        array => Array.Clear(array, 0, array.Length),
        initialCount: 2);
    #endregion

    #region Initialization and Configuration
    /// <summary>
    /// Initializes the waterfall renderer and prepares resources for rendering.
    /// </summary>
    public override void Initialize()
    {
        Safe(() =>
        {
            base.Initialize();

            int bufferHeight = _isOverlayMode ?
                Constants.OVERLAY_BUFFER_HEIGHT :
                Constants.DEFAULT_BUFFER_HEIGHT;

            InitializeSpectrogramBuffer(bufferHeight, Constants.DEFAULT_SPECTRUM_WIDTH);

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

            bool configChanged = _isOverlayMode != isOverlayActive || _quality != quality;

            // Update overlay mode - since it's readonly, we'll use a local variable for comparison
            bool isCurrentlyOverlayActive = isOverlayActive;

            if (_quality != quality)
            {
                ApplyQualitySettings();
            }

            // Update buffer height based on overlay mode if configuration changed
            if (configChanged && _spectrogramBuffer != null)
            {
                int bufferHeight = isCurrentlyOverlayActive ?
                    Constants.OVERLAY_BUFFER_HEIGHT :
                    Constants.DEFAULT_BUFFER_HEIGHT;

                int currentWidth = _spectrogramBuffer[0].Length;
                InitializeSpectrogramBuffer(bufferHeight, currentWidth);
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
                    _colorEnhancement = Constants.Quality.LOW_COLOR_ENHANCEMENT;
                    break;

                case RenderQuality.Medium:
                    _useAntiAlias = true;
                    _samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                    _useAdvancedEffects = Constants.Quality.MEDIUM_USE_ADVANCED_EFFECTS;
                    _colorEnhancement = Constants.Quality.MEDIUM_COLOR_ENHANCEMENT;
                    break;

                case RenderQuality.High:
                    _useAntiAlias = true;
                    _samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                    _useAdvancedEffects = Constants.Quality.HIGH_USE_ADVANCED_EFFECTS;
                    _colorEnhancement = Constants.Quality.HIGH_COLOR_ENHANCEMENT;
                    break;
            }

            // Invalidate bitmap cache when quality changes
            _waterfallBitmap?.Dispose();
            _waterfallBitmap = null;
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.ApplyQualitySettings",
            ErrorMessage = "Failed to apply quality settings"
        });
    }
    #endregion

    #region Rendering
    /// <summary>
    /// Renders the waterfall visualization on the canvas using spectrum data.
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
        if (!ValidateRenderParameters(canvas, info, barWidth, barCount, barSpacing))
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
            bool parametersChanged = CheckParametersChanged(barWidth, barSpacing, barCount);
            bool semaphoreAcquired = false;

            try
            {
                // Try to acquire semaphore for updating spectrum data
                semaphoreAcquired = _renderSemaphore.Wait(Constants.SEMAPHORE_TIMEOUT_MS);
                if (semaphoreAcquired)
                {
                    // Process spectrum data
                    float[]? adjustedSpectrum = spectrum;
                    if (spectrum != null && parametersChanged)
                    {
                        adjustedSpectrum = AdjustSpectrumResolution(spectrum, barCount, barWidth);
                    }

                    // Update spectrogram buffer with new data
                    UpdateSpectrogramBuffer(adjustedSpectrum);

                    // Resize buffer if needed
                    if (_needsBufferResize && parametersChanged)
                    {
                        ResizeBufferForBarParameters(barCount);
                        _needsBufferResize = false;
                    }
                }

                // Check if buffer is ready for rendering
                if (_spectrogramBuffer == null || _disposed)
                {
                    Log(LogLevel.Warning, Constants.LOG_PREFIX,
                        "Spectrogram buffer is null or renderer is disposed");
                    return;
                }

                // Render the waterfall visualization
                RenderWaterfall(canvas, info, barWidth, barSpacing, barCount);
            }
            finally
            {
                // Release semaphore if acquired
                if (semaphoreAcquired)
                    _renderSemaphore.Release();
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
    private bool ValidateRenderParameters(
        SKCanvas? canvas,
        SKImageInfo info,
        float barWidth,
        int barCount,
        float barSpacing)
    {
        if (canvas == null)
        {
            Log(LogLevel.Warning, Constants.LOG_PREFIX, "Canvas cannot be null");
            return false;
        }

        if (_disposed)
        {
            Log(LogLevel.Warning, Constants.LOG_PREFIX, "Cannot render: instance is disposed");
            return false;
        }

        if (info.Width <= 0 || info.Height <= 0)
        {
            Log(LogLevel.Warning, Constants.LOG_PREFIX, "Invalid canvas dimensions");
            return false;
        }

        if (barCount <= 0)
        {
            Log(LogLevel.Warning, Constants.LOG_PREFIX, "Bar count must be positive");
            return false;
        }

        if (barWidth <= 0)
        {
            Log(LogLevel.Warning, Constants.LOG_PREFIX, "Bar width must be positive");
            return false;
        }

        if (barSpacing < 0)
        {
            Log(LogLevel.Warning, Constants.LOG_PREFIX, "Bar spacing cannot be negative");
            return false;
        }

        return true;
    }
    #endregion

    #region Rendering Implementation
    /// <summary>
    /// Renders the waterfall visualization with current spectrogram data.
    /// </summary>
    private void RenderWaterfall(
        SKCanvas canvas,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount)
    {
        if (_spectrogramBuffer == null) return;

        int bufferHeight = _spectrogramBuffer.Length;
        int spectrumWidth = _spectrogramBuffer[0].Length;

        // Ensure bitmap is created with correct dimensions
        if (_waterfallBitmap == null ||
            _waterfallBitmap.Width != spectrumWidth ||
            _waterfallBitmap.Height != bufferHeight)
        {
            _waterfallBitmap?.Dispose();
            _waterfallBitmap = new SKBitmap(spectrumWidth, bufferHeight);
        }

        // Determine optimal render quality based on bar count
        RenderQuality renderQuality = CalculateRenderQuality(barCount);

        // Update bitmap with current spectrogram data
        UpdateBitmapSafe(_waterfallBitmap, _spectrogramBuffer, _bufferHead, spectrumWidth, bufferHeight, renderQuality);

        // Calculate destination rectangle
        float totalBarsWidth = barCount * barWidth + (barCount - 1) * barSpacing;
        float startX = (info.Width - totalBarsWidth) / 2;
        SKRect destRect = new SKRect(
            left: Max(startX, 0),
            top: 0,
            right: Min(startX + totalBarsWidth, info.Width),
            bottom: info.Height);

        // Quick reject if destination is not visible
        if (canvas.QuickReject(destRect))
            return;

        // Draw the waterfall visualization
        using var renderPaint = new SKPaint
        {
            IsAntialias = _useAntiAlias && barCount > Constants.HIGH_BAR_COUNT_THRESHOLD
        };

        canvas.Clear(SKColors.Black);
        // Используем правильную перегрузку DrawBitmap
        canvas.DrawBitmap(_waterfallBitmap, destRect, renderPaint);

        // Apply zoom enhancement for detailed view
        if (barWidth > Constants.ZOOM_THRESHOLD && _useAdvancedEffects)
        {
            ApplyZoomEnhancement(canvas, destRect, barWidth, barCount, barSpacing);
        }
    }

    /// <summary>
    /// Applies visual enhancements for zoomed-in view.
    /// </summary>
    private void ApplyZoomEnhancement(
        SKCanvas canvas,
        SKRect destRect,
        float barWidth,
        int barCount,
        float barSpacing)
    {
        if (canvas == null || _disposed) return;

        using var paint = new SKPaint
        {
            Color = new SKColor(255, 255, 255, 40),
            StrokeWidth = 1,
            IsAntialias = _useAntiAlias,
            Style = Stroke
        };

        // Draw grid lines
        int gridStep = Max(1, barCount / 10);
        using (var path = new SKPath())
        {
            for (int i = 0; i < barCount; i += gridStep)
            {
                float x = destRect.Left + i * (barWidth + barSpacing) + barWidth / 2;
                path.MoveTo(x, destRect.Top);
                path.LineTo(x, destRect.Bottom);
            }
            canvas.DrawPath(path, paint);
        }

        // Draw frequency markers for very zoomed in view
        if (barWidth > Constants.ZOOM_THRESHOLD * 1.5f)
        {
            // Use SKFont for text rendering (modern approach)
            using var font = new SKFont { Size = 10 };
            using var textPaint = new SKPaint
            {
                Color = SKColors.White,
                IsAntialias = _useAntiAlias
            };

            for (int i = 0; i < barCount; i += gridStep * 2)
            {
                float x = destRect.Left + i * (barWidth + barSpacing) + barWidth / 2;
                string marker = $"{i}";
                float textWidth = font.MeasureText(marker, textPaint);
                canvas.DrawText(marker, x - textWidth / 2, destRect.Bottom - 5, font, textPaint);
            }
        }
    }

    /// <summary>
    /// Updates the bitmap with current spectrogram data.
    /// </summary>
    private void UpdateBitmapSafe(
        SKBitmap bitmap,
        float[][] spectrogramBuffer,
        int bufferHead,
        int spectrumWidth,
        int bufferHeight,
        RenderQuality quality)
    {
        if (bitmap == null || spectrogramBuffer == null || _disposed)
            return;

        nint pixelsPtr = bitmap.GetPixels();
        if (pixelsPtr == nint.Zero)
        {
            Log(LogLevel.Error, Constants.LOG_PREFIX, "Invalid bitmap pixels pointer");
            return;
        }

        Safe(() =>
        {
            // Rent array from pool to avoid allocation
            int[] pixels = ArrayPool<int>.Shared.Rent(spectrumWidth * bufferHeight);
            try
            {
                // Apply color enhancement based on quality
                float colorEnhancementFactor = _colorEnhancement;

                // Use SIMD for faster processing if available
                if (IsHardwareAccelerated && spectrumWidth >= Vector<float>.Count)
                {
                    Parallel.For(0, bufferHeight, y =>
                    {
                        int bufferIndex = (bufferHead + 1 + y) % bufferHeight;
                        int rowOffset = y * spectrumWidth;
                        int vectorizedWidth = spectrumWidth - spectrumWidth % Vector<float>.Count;

                        // Process spectrum data in vector chunks
                        for (int x = 0; x < vectorizedWidth; x += Vector<float>.Count)
                        {
                            Vector<float> values = new Vector<float>(spectrogramBuffer[bufferIndex], x);

                            // Apply color enhancement
                            if (Abs(colorEnhancementFactor - 1.0f) > float.Epsilon)
                                values = Multiply(values, colorEnhancementFactor);

                            // Convert values to colors
                            for (int i = 0; i < Vector<float>.Count; i++)
                            {
                                float normalized = Clamp(values[i], 0f, 1f);
                                int paletteIndex = (int)(normalized * (Constants.COLOR_PALETTE_SIZE - 1));
                                pixels[rowOffset + x + i] = _colorPalette[paletteIndex];
                            }
                        }

                        // Process remaining elements
                        for (int x = vectorizedWidth; x < spectrumWidth; x++)
                        {
                            float value = spectrogramBuffer[bufferIndex][x];
                            if (Abs(colorEnhancementFactor - 1.0f) > float.Epsilon)
                                value *= colorEnhancementFactor;
                            float normalized = Clamp(value, 0f, 1f);
                            int paletteIndex = (int)(normalized * (Constants.COLOR_PALETTE_SIZE - 1));
                            pixels[rowOffset + x] = _colorPalette[paletteIndex];
                        }
                    });
                }
                else
                {
                    // Standard processing for small arrays or without SIMD
                    Parallel.For(0, bufferHeight, y =>
                    {
                        int bufferIndex = (bufferHead + 1 + y) % bufferHeight;
                        int rowOffset = y * spectrumWidth;
                        for (int x = 0; x < spectrumWidth; x++)
                        {
                            float value = spectrogramBuffer[bufferIndex][x];
                            if (Abs(colorEnhancementFactor - 1.0f) > float.Epsilon)
                                value *= colorEnhancementFactor;
                            float normalized = Clamp(value, 0f, 1f);
                            int paletteIndex = (int)(normalized * (Constants.COLOR_PALETTE_SIZE - 1));
                            pixels[rowOffset + x] = _colorPalette[paletteIndex];
                        }
                    });
                }

                // Copy pixels to bitmap
                Marshal.Copy(pixels, 0, pixelsPtr, spectrumWidth * bufferHeight);
            }
            finally
            {
                // Return rented array to pool
                ArrayPool<int>.Shared.Return(pixels);
            }
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.UpdateBitmapSafe",
            ErrorMessage = "Bitmap update error"
        });
    }
    #endregion

    #region Spectrum Processing
    /// <summary>
    /// Adjusts spectrum resolution to match the target bar count.
    /// </summary>
    [MethodImpl(AggressiveOptimization)]
    private float[] AdjustSpectrumResolution(float[] spectrum, int barCount, float barWidth)
    {
        if (spectrum == null || barCount <= 0)
            return spectrum ?? Array.Empty<float>();

        // Skip processing if spectrum already matches target size
        if (spectrum.Length == barCount) return spectrum;

        // Get spectrum array from pool
        float[] result = _spectrumPool.Get();
        if (result.Length < barCount)
        {
            // If pool returned too small array, create a new one
            _spectrumPool.Return(result);
            result = new float[barCount];
        }

        // Calculate detail level based on bar width
        float detailLevel = Max(0.1f, Min(1.0f, barWidth * Constants.DETAIL_SCALE_FACTOR));
        float ratio = (float)spectrum.Length / barCount;

        if (ratio > 1.0f)
        {
            // Downscaling (more source points than target points)
            Parallel.For(0, barCount, i =>
            {
                int startIdx = (int)(i * ratio);
                int endIdx = Min((int)((i + 1) * ratio), spectrum.Length);
                float sum = 0;
                float peak = 0;
                int count = endIdx - startIdx;

                if (count <= 0) return;

                // Use SIMD for faster processing if available
                if (IsHardwareAccelerated && count >= Vector<float>.Count)
                {
                    int vectorSize = Vector<float>.Count;
                    int vectorCount = count / vectorSize;

                    for (int v = 0; v < vectorCount; v++)
                    {
                        Vector<float> values = new Vector<float>(spectrum, startIdx + v * vectorSize);
                        sum += VectorSum(values);
                        peak = Max(peak, VectorMax(values));
                    }

                    // Process remaining elements
                    for (int j = startIdx + vectorCount * vectorSize; j < endIdx; j++)
                    {
                        sum += spectrum[j];
                        peak = Max(peak, spectrum[j]);
                    }
                }
                else
                {
                    // Standard processing
                    for (int j = startIdx; j < endIdx; j++)
                    {
                        sum += spectrum[j];
                        peak = Max(peak, spectrum[j]);
                    }
                }

                // Blend average and peak values based on detail level
                result[i] = sum / count * (1 - detailLevel) + peak * detailLevel;
            });
        }
        else
        {
            // Upscaling (fewer source points than target points)
            for (int i = 0; i < barCount; i++)
            {
                float exactIdx = i * ratio;
                int idx1 = (int)exactIdx;
                int idx2 = Min(idx1 + 1, spectrum.Length - 1);
                float frac = exactIdx - idx1;

                // Linear interpolation
                result[i] = spectrum[idx1] * (1 - frac) + spectrum[idx2] * frac;
            }
        }

        return result;
    }

    /// <summary>
    /// Updates the spectrogram buffer with new spectrum data.
    /// </summary>
    private void UpdateSpectrogramBuffer(float[]? spectrum)
    {
        if (_spectrogramBuffer == null || _disposed)
            return;

        int bufferHeight = _spectrogramBuffer.Length;
        int spectrumWidth = _spectrogramBuffer[0].Length;

        if (spectrum == null)
        {
            // Fill with minimum value if no spectrum data
            Array.Fill(_spectrogramBuffer[_bufferHead], Constants.MIN_SIGNAL);
        }
        else
        {
            // Cache current spectrum
            if (_currentSpectrum == null || _currentSpectrum.Length != spectrum.Length)
                _currentSpectrum = new float[spectrum.Length];

            Array.Copy(spectrum, _currentSpectrum, spectrum.Length);

            // Direct copy if sizes match
            if (spectrum.Length == spectrumWidth)
            {
                Array.Copy(spectrum, _spectrogramBuffer[_bufferHead], spectrumWidth);
            }
            else if (spectrum.Length > spectrumWidth)
            {
                // Downscaling
                ProcessSpectrumDownscaling(spectrum, spectrumWidth);
            }
            else
            {
                // Upscaling
                ProcessSpectrumUpscaling(spectrum, spectrumWidth);
            }
        }

        // Advance buffer head for next update
        _bufferHead = (_bufferHead + 1) % bufferHeight;
    }

    /// <summary>
    /// Processes spectrum downscaling (more source points than target).
    /// </summary>
    private void ProcessSpectrumDownscaling(float[] spectrum, int spectrumWidth)
    {
        if (_spectrogramBuffer == null) return;

        float ratio = (float)spectrum.Length / spectrumWidth;

        if (IsHardwareAccelerated && spectrum.Length >= Vector<float>.Count)
        {
            Parallel.For(0, spectrumWidth, i =>
            {
                int startIdx = (int)(i * ratio);
                int endIdx = Min((int)((i + 1) * ratio), spectrum.Length);

                if (endIdx - startIdx >= Vector<float>.Count)
                {
                    int vectorSize = Vector<float>.Count;
                    int vectorizedLength = (endIdx - startIdx) / vectorSize * vectorSize;
                    float maxVal = Constants.MIN_SIGNAL;

                    // Find maximum using SIMD
                    for (int j = startIdx; j < startIdx + vectorizedLength; j += vectorSize)
                    {
                        Vector<float> values = new Vector<float>(spectrum, j);
                        maxVal = Max(maxVal, VectorMax(values));
                    }

                    // Process remaining elements
                    for (int j = startIdx + vectorizedLength; j < endIdx; j++)
                        maxVal = Max(maxVal, spectrum[j]);

                    _spectrogramBuffer[_bufferHead][i] = maxVal;
                }
                else
                {
                    // Standard processing for small segments
                    float maxVal = Constants.MIN_SIGNAL;
                    for (int j = startIdx; j < endIdx; j++)
                        maxVal = Max(maxVal, spectrum[j]);

                    _spectrogramBuffer[_bufferHead][i] = maxVal;
                }
            });
        }
        else
        {
            // Sequential processing for small arrays
            for (int i = 0; i < spectrumWidth; i++)
            {
                int startIdx = (int)(i * ratio);
                int endIdx = Min((int)((i + 1) * ratio), spectrum.Length);
                float maxVal = Constants.MIN_SIGNAL;

                for (int j = startIdx; j < endIdx; j++)
                    maxVal = Max(maxVal, spectrum[j]);

                _spectrogramBuffer[_bufferHead][i] = maxVal;
            }
        }
    }

    /// <summary>
    /// Processes spectrum upscaling (fewer source points than target).
    /// </summary>
    private void ProcessSpectrumUpscaling(float[] spectrum, int spectrumWidth)
    {
        if (_spectrogramBuffer == null) return;

        float ratio = (float)spectrum.Length / spectrumWidth;

        for (int i = 0; i < spectrumWidth; i++)
        {
            float exactIdx = i * ratio;
            int idx1 = (int)exactIdx;
            int idx2 = Min(idx1 + 1, spectrum.Length - 1);
            float frac = exactIdx - idx1;

            // Linear interpolation
            _spectrogramBuffer[_bufferHead][i] = spectrum[idx1] * (1 - frac) + spectrum[idx2] * frac;
        }
    }

    /// <summary>
    /// Initializes the spectrogram buffer with given dimensions.
    /// </summary>
    private void InitializeSpectrogramBuffer(int bufferHeight, int spectrumWidth)
    {
        if (bufferHeight <= 0 || spectrumWidth <= 0)
            throw new ArgumentException("Buffer dimensions must be positive");

        if (_spectrogramBuffer == null ||
            _spectrogramBuffer.Length != bufferHeight ||
            _spectrogramBuffer[0].Length != spectrumWidth)
        {
            // Clean up existing buffer
            if (_spectrogramBuffer != null)
            {
                for (int i = 0; i < _spectrogramBuffer.Length; i++)
                    _spectrogramBuffer[i] = null!;
            }

            // Create new buffer
            _spectrogramBuffer = new float[bufferHeight][];

            // Initialize buffer rows
            if (bufferHeight > 32)
            {
                // Parallel initialization for large buffers
                Parallel.For(0, bufferHeight, i =>
                {
                    _spectrogramBuffer[i] = new float[spectrumWidth];
                    Array.Fill(_spectrogramBuffer[i], Constants.MIN_SIGNAL);
                });
            }
            else
            {
                // Sequential initialization for small buffers
                for (int i = 0; i < bufferHeight; i++)
                {
                    _spectrogramBuffer[i] = new float[spectrumWidth];
                    Array.Fill(_spectrogramBuffer[i], Constants.MIN_SIGNAL);
                }
            }

            // Reset buffer head
            _bufferHead = 0;
        }
    }

    /// <summary>
    /// Checks if rendering parameters have changed.
    /// </summary>
    private bool CheckParametersChanged(float barWidth, float barSpacing, int barCount)
    {
        bool changed = Abs(_lastBarWidth - barWidth) > 0.1f ||
                       Abs(_lastBarSpacing - barSpacing) > 0.1f ||
                       _lastBarCount != barCount;

        if (changed)
        {
            _lastBarWidth = barWidth;
            _lastBarSpacing = barSpacing;
            _lastBarCount = barCount;
            _needsBufferResize = true;
        }

        return changed;
    }

    /// <summary>
    /// Resizes the buffer to match the bar parameters.
    /// </summary>
    private void ResizeBufferForBarParameters(int barCount)
    {
        if (_spectrogramBuffer == null) return;

        int bufferHeight = _spectrogramBuffer.Length;
        int newWidth = CalculateOptimalSpectrumWidth(barCount);

        if (_spectrogramBuffer[0].Length != newWidth)
        {
            InitializeSpectrogramBuffer(bufferHeight, newWidth);
        }
    }

    /// <summary>
    /// Calculates the optimal spectrum width based on bar count.
    /// </summary>
    private int CalculateOptimalSpectrumWidth(int barCount)
    {
        int baseWidth = Max(barCount, Constants.MIN_BAR_COUNT);

        // Find next power of 2 greater than the base width
        int width = 1;
        while (width < baseWidth)
        {
            width *= 2;
        }

        return Max(width, Constants.DEFAULT_SPECTRUM_WIDTH / 4);
    }

    /// <summary>
    /// Calculates the render quality based on bar count.
    /// </summary>
    private RenderQuality CalculateRenderQuality(int barCount)
    {
        if (barCount > Constants.HIGH_BAR_COUNT_THRESHOLD)
            return RenderQuality.High;

        if (barCount < Constants.LOW_BAR_COUNT_THRESHOLD)
            return RenderQuality.Low;

        return RenderQuality.Medium;
    }
    #endregion

    #region Helper Methods
    /// <summary>
    /// Calculates the sum of all values in a vector.
    /// </summary>
    [MethodImpl(AggressiveInlining)]
    private static float VectorSum(Vector<float> vector)
    {
        float sum = 0;
        for (int i = 0; i < Vector<float>.Count; i++)
            sum += vector[i];
        return sum;
    }

    /// <summary>
    /// Finds the maximum value in a vector.
    /// </summary>
    [MethodImpl(AggressiveInlining)]
    private static float VectorMax(Vector<float> vector)
    {
        float max = float.MinValue;
        for (int i = 0; i < Vector<float>.Count; i++)
            max = Max(max, vector[i]);
        return max;
    }

    /// <summary>
    /// Initializes the color palette for the spectrogram.
    /// </summary>
    private static int[] InitColorPalette()
    {
        int[] palette = new int[Constants.COLOR_PALETTE_SIZE];

        for (int i = 0; i < Constants.COLOR_PALETTE_SIZE; i++)
        {
            float normalized = i / (float)(Constants.COLOR_PALETTE_SIZE - 1);
            SKColor color = GetSpectrogramColor(normalized);
            palette[i] = color.Alpha << 24 | color.Red << 16 | color.Green << 8 | color.Blue;
        }

        return palette;
    }

    /// <summary>
    /// Gets a color for the spectrogram based on normalized value.
    /// </summary>
    private static SKColor GetSpectrogramColor(float normalized)
    {
        normalized = Clamp(normalized, 0f, 1f);

        if (normalized < Constants.BLUE_THRESHOLD)
        {
            // Deep blue to light blue
            float t = normalized / Constants.BLUE_THRESHOLD;
            return new SKColor(0, (byte)(t * 50), (byte)(50 + t * 205), 255);
        }
        else if (normalized < Constants.CYAN_THRESHOLD)
        {
            // Light blue to cyan
            float t = (normalized - Constants.BLUE_THRESHOLD) /
                      (Constants.CYAN_THRESHOLD - Constants.BLUE_THRESHOLD);
            return new SKColor(0, (byte)(50 + t * 205), 255, 255);
        }
        else if (normalized < Constants.YELLOW_THRESHOLD)
        {
            // Cyan to yellow
            float t = (normalized - Constants.CYAN_THRESHOLD) /
                      (Constants.YELLOW_THRESHOLD - Constants.CYAN_THRESHOLD);
            return new SKColor((byte)(t * 255), 255, (byte)(255 - t * 255), 255);
        }
        else
        {
            // Yellow to red
            float t = (normalized - Constants.YELLOW_THRESHOLD) /
                      (1f - Constants.YELLOW_THRESHOLD);
            return new SKColor(255, (byte)(255 - t * 255), 0, 255);
        }
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

                // Dispose bitmap resources
                _waterfallBitmap?.Dispose();
                _waterfallBitmap = null;

                // Clean up cached data
                _spectrogramBuffer = null;
                _currentSpectrum = null;

                // Dispose object pools
                _spectrumPool?.Dispose();

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