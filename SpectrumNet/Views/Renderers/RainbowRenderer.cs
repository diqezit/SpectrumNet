#nullable enable

using SpectrumNet.Service.Enums;

namespace SpectrumNet.Views.Renderers;

/// <summary>
/// Renderer that visualizes spectrum data as rainbow-colored bars with dynamic lighting effects.
/// </summary>
public sealed class RainbowRenderer : BaseSpectrumRenderer
{
    #region Singleton Pattern
    private static readonly Lazy<RainbowRenderer> _instance = new(() => new RainbowRenderer());
    private RainbowRenderer() { } // Приватный конструктор
    public static RainbowRenderer GetInstance() => _instance.Value;
    #endregion

    #region Constants
    private static class Constants
    {
        // Logging
        public const string LOG_PREFIX = "RainbowRenderer";

        // Spectrum processing constants
        public const float MIN_MAGNITUDE_THRESHOLD = 0.008f; // Minimum magnitude to render a bar
        public const float ALPHA_MULTIPLIER = 1.7f;   // Multiplier for bar alpha calculation
        public const float SMOOTHING_BASE = 0.3f;   // Base smoothing factor for normal mode
        public const float SMOOTHING_OVERLAY = 0.5f;   // Smoothing factor for overlay mode
        public const int MAX_ALPHA = 255;    // Maximum alpha value for color calculations

        // Bar rendering constants
        public const float CORNER_RADIUS = 8f;     // Radius for rounded corners of bars
        public const float GRADIENT_ALPHA_FACTOR = 0.7f;   // Alpha factor for bar gradient end

        // Effect constants
        public const float GLOW_INTENSITY = 0.45f;  // Intensity of glow effect
        public const float GLOW_RADIUS = 6f;     // Base radius for glow blur
        public const float GLOW_LOUDNESS_FACTOR = 0.3f;   // Loudness influence on glow radius
        public const float GLOW_RADIUS_THRESHOLD = 0.1f;   // Threshold for updating glow radius
        public const float GLOW_MIN_MAGNITUDE = 0.3f;   // Minimum magnitude for glow effect
        public const float GLOW_MAX_MAGNITUDE = 0.95f;  // Maximum magnitude for glow effect
        public const float HIGHLIGHT_ALPHA = 0.8f;   // Alpha value for highlight effect
        public const float HIGHLIGHT_HEIGHT_PROP = 0.08f;  // Proportion of bar height for highlight
        public const float HIGHLIGHT_WIDTH_PROP = 0.7f;   // Proportion of bar width for highlight
        public const float REFLECTION_OPACITY = 0.3f;   // Opacity for reflection effect
        public const float REFLECTION_HEIGHT = 0.15f;  // Proportion of canvas height for reflection
        public const float REFLECTION_FACTOR = 0.4f;   // Factor of bar height for reflection
        public const float REFLECTION_MIN_MAGNITUDE = 0.2f;   // Minimum magnitude for reflection effect

        // Loudness calculation constants
        public const float SUB_BASS_WEIGHT = 1.7f;   // Weight for sub-bass frequencies
        public const float BASS_WEIGHT = 1.4f;   // Weight for bass frequencies
        public const float MID_WEIGHT = 1.1f;   // Weight for mid frequencies
        public const float HIGH_WEIGHT = 0.6f;   // Weight for high frequencies
        public const float LOUDNESS_SCALE = 4.0f;   // Scaling factor for loudness
        public const float LOUDNESS_SMOOTH_FACTOR = 0.5f;   // Factor for adaptive smoothing

        // Rainbow color constants
        public const float HUE_START = 240f;   // Starting hue for rainbow gradient
        public const float HUE_RANGE = 240f;   // Range of hue variation
        public const float SATURATION = 100f;   // Saturation for rainbow colors
        public const float BRIGHTNESS_BASE = 90f;    // Base brightness for rainbow colors
        public const float BRIGHTNESS_RANGE = 10f;    // Range of brightness variation

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
    private readonly ObjectPool<SKPaint> _paintPool = new(() => new SKPaint(), paint => paint.Reset(), 4);
    private readonly SKPath _path = new();
    private readonly SKPaint _barPaint = new() { Style = Fill };
    private readonly SKPaint _highlightPaint = new() { Style = Fill, Color = SKColors.White };
    private readonly SKPaint _reflectionPaint = new() { Style = Fill, BlendMode = SKBlendMode.SrcOver };
    private SKPaint? _glowPaint;

    // Color cache
    private SKColor[]? _colorCache;

    // Quality-dependent settings
    private new bool _useAntiAlias = true;
    private new SKSamplingOptions _samplingOptions = new(SKFilterMode.Linear, SKMipmapMode.Linear);
    private new bool _useAdvancedEffects = true;

    // Synchronization and state
    private readonly SemaphoreSlim _renderSemaphore = new(1, 1);
    private volatile float[]? _processedSpectrum;
    private new bool _disposed;
    #endregion

    #region Initialization and Configuration
    /// <summary>
    /// Initializes the rainbow renderer and prepares resources for rendering.
    /// </summary>
    public override void Initialize()
    {
        Safe(() =>
        {
            base.Initialize();

            _glowPaint = new SKPaint
            {
                Style = Fill,
                IsAntialias = _useAntiAlias,
                ImageFilter = SKImageFilter.CreateBlur(Constants.GLOW_RADIUS, Constants.GLOW_RADIUS)
            };

            // Precompute rainbow colors for performance
            _colorCache = new SKColor[Constants.MAX_ALPHA + 1];
            for (int i = 0; i <= Constants.MAX_ALPHA; i++)
            {
                float normalizedValue = i / (float)Constants.MAX_ALPHA;
                _colorCache[i] = GetRainbowColor(normalizedValue);
            }

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
                Constants.SMOOTHING_OVERLAY :
                Constants.SMOOTHING_BASE;

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

            // Update paint properties based on quality settings
            _barPaint.IsAntialias = _useAntiAlias;
            _highlightPaint.IsAntialias = _useAntiAlias;
            _reflectionPaint.IsAntialias = _useAntiAlias;

            if (_glowPaint != null)
            {
                _glowPaint.IsAntialias = _useAntiAlias;
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
    /// Renders the rainbow bar visualization on the canvas using spectrum data.
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
            int spectrumLength = spectrum!.Length;
            int actualBarCount = Min(spectrumLength, barCount);

            // Get spectrum data for rendering
            float[] renderSpectrum = _processedSpectrum ??
                                    ProcessSpectrumSynchronously(spectrum, actualBarCount, spectrumLength);

            // Save canvas state
            using var _ = new SKAutoCanvasRestore(canvas!, true);

            // Render bars based on spectrum data
            RenderBars(canvas!, renderSpectrum, info, barWidth, barSpacing, paint!);

            // Start background processing for next frame
            Task.Run(() =>
            {
                try
                {
                    float[] latestSpectrum = spectrum.ToArray();
                    float[] processed = ProcessSpectrum(latestSpectrum, actualBarCount, spectrumLength);

                    bool acquired = _renderSemaphore.Wait(0);
                    if (acquired)
                    {
                        try
                        {
                            _processedSpectrum = processed;
                        }
                        finally
                        {
                            _renderSemaphore.Release();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log(LogLevel.Error, Constants.LOG_PREFIX, $"Error processing spectrum: {ex.Message}");
                }
            });
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
    #endregion

    #region Rendering Implementation
    /// <summary>
    /// Renders rainbow bars based on spectrum data.
    /// </summary>
    private void RenderBars(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        SKPaint basePaint)
    {
        Safe(() =>
        {
            float totalBarWidth = barWidth + barSpacing;
            float canvasHeight = info.Height;
            float startX = (info.Width - (spectrum.Length * totalBarWidth - barSpacing)) / 2f;
            float loudness = CalculateLoudness(spectrum);
            float reflectionHeight = canvasHeight * Constants.REFLECTION_HEIGHT;

            for (int i = 0; i < spectrum.Length; i++)
            {
                float magnitude = Clamp(spectrum[i], 0f, 1f);
                if (magnitude < Constants.MIN_MAGNITUDE_THRESHOLD)
                    continue;

                float barHeight = magnitude * canvasHeight;
                float x = startX + i * totalBarWidth;
                float y = canvasHeight - barHeight;
                var barRect = new SKRect(x, y, x + barWidth, canvasHeight);

                if (canvas.QuickReject(barRect))
                    continue;

                SKColor barColor = GetBarColor(magnitude);
                byte baseAlpha = (byte)Clamp(magnitude * Constants.MAX_ALPHA, 0, Constants.MAX_ALPHA);

                // Render glow effect for high-energy bars if advanced effects are enabled
                if (_useAdvancedEffects && _glowPaint != null &&
                    magnitude > Constants.GLOW_MIN_MAGNITUDE && magnitude <= Constants.GLOW_MAX_MAGNITUDE)
                {
                    float adjustedGlowRadius = Constants.GLOW_RADIUS * (1 + loudness * Constants.GLOW_LOUDNESS_FACTOR);
                    if (Abs(adjustedGlowRadius - Constants.GLOW_RADIUS) > Constants.GLOW_RADIUS_THRESHOLD)
                    {
                        _glowPaint.ImageFilter = SKImageFilter.CreateBlur(adjustedGlowRadius, adjustedGlowRadius);
                    }

                    byte glowAlpha = (byte)Clamp(magnitude * Constants.MAX_ALPHA * Constants.GLOW_INTENSITY, 0, Constants.MAX_ALPHA);
                    _glowPaint.Color = barColor.WithAlpha(glowAlpha);
                    canvas.DrawRoundRect(barRect, Constants.CORNER_RADIUS, Constants.CORNER_RADIUS, _glowPaint);
                }

                // Create horizontal gradient shader for bar
                using var shader = SKShader.CreateLinearGradient(
                    new SKPoint(x, y),
                    new SKPoint(x + barWidth, y),
                    new[] { barColor, barColor.WithAlpha((byte)(Constants.MAX_ALPHA * Constants.GRADIENT_ALPHA_FACTOR)) },
                    new[] { 0f, 1f },
                    SKShaderTileMode.Clamp);

                // Render main bar
                byte barAlpha = (byte)Clamp(magnitude * Constants.ALPHA_MULTIPLIER * Constants.MAX_ALPHA, 0, Constants.MAX_ALPHA);
                _barPaint.Color = barColor.WithAlpha(barAlpha);
                _barPaint.Shader = shader;
                canvas.DrawRoundRect(barRect, Constants.CORNER_RADIUS, Constants.CORNER_RADIUS, _barPaint);

                if (barHeight <= Constants.CORNER_RADIUS * 2)
                    continue;

                // Render highlight at top of bar
                float highlightWidth = barWidth * Constants.HIGHLIGHT_WIDTH_PROP;
                float highlightHeight = Min(barHeight * Constants.HIGHLIGHT_HEIGHT_PROP, Constants.CORNER_RADIUS);
                byte highlightAlpha = (byte)Clamp(magnitude * Constants.MAX_ALPHA * Constants.HIGHLIGHT_ALPHA, 0, Constants.MAX_ALPHA);
                _highlightPaint.Color = SKColors.White.WithAlpha(highlightAlpha);
                float highlightX = x + (barWidth - highlightWidth) / 2;
                canvas.DrawRect(highlightX, y, highlightWidth, highlightHeight, _highlightPaint);

                // Render reflection if advanced effects are enabled and magnitude is sufficient
                if (_useAdvancedEffects && magnitude > Constants.REFLECTION_MIN_MAGNITUDE)
                {
                    byte reflectionAlpha = (byte)Clamp(magnitude * Constants.MAX_ALPHA * Constants.REFLECTION_OPACITY, 0, Constants.MAX_ALPHA);
                    _reflectionPaint.Color = barColor.WithAlpha(reflectionAlpha);
                    float reflectHeight = Min(barHeight * Constants.REFLECTION_FACTOR, reflectionHeight);
                    canvas.DrawRect(x, canvasHeight, barWidth, reflectHeight, _reflectionPaint);
                }
            }

            // Reset shader to avoid memory leaks
            _barPaint.Shader = null;
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.RenderBars",
            ErrorMessage = "Failed to render bars"
        });
    }

    /// <summary>
    /// Gets bar color from pre-computed cache or generates it on demand.
    /// </summary>
    [MethodImpl(AggressiveInlining)]
    private SKColor GetBarColor(float magnitude)
    {
        int colorIndex = Clamp((int)(magnitude * Constants.MAX_ALPHA), 0, Constants.MAX_ALPHA);
        return _colorCache != null ? _colorCache[colorIndex] : GetRainbowColor(magnitude);
    }

    /// <summary>
    /// Generates rainbow color based on normalized magnitude.
    /// </summary>
    [MethodImpl(AggressiveInlining)]
    private static SKColor GetRainbowColor(float normalizedValue)
    {
        normalizedValue = Clamp(normalizedValue, 0f, 1f);
        float hue = Constants.HUE_START - Constants.HUE_RANGE * normalizedValue;
        if (hue < 0) hue += 360;
        float brightness = Constants.BRIGHTNESS_BASE + normalizedValue * Constants.BRIGHTNESS_RANGE;
        return SKColor.FromHsv(hue, Constants.SATURATION, brightness);
    }
    #endregion

    #region Spectrum Processing
    /// <summary>
    /// Processes spectrum data synchronously.
    /// </summary>
    private float[] ProcessSpectrumSynchronously(float[] spectrum, int targetCount, int spectrumLength)
    {
        float[] scaledSpectrum = ScaleSpectrum(spectrum, targetCount, spectrumLength);
        return SmoothSpectrum(scaledSpectrum, targetCount);
    }

    /// <summary>
    /// Processes spectrum data for visualization.
    /// </summary>
    private float[] ProcessSpectrum(float[] spectrum, int targetCount, int spectrumLength)
    {
        return ProcessSpectrumSynchronously(spectrum, targetCount, spectrumLength);
    }

    /// <summary>
    /// Scales the spectrum data to the target count of bars with SIMD optimization.
    /// </summary>
    [MethodImpl(AggressiveOptimization)]
    private static float[] ScaleSpectrum(float[] spectrum, int targetCount, int spectrumLength)
    {
        float[] scaledSpectrum = new float[targetCount];
        float blockSize = (float)spectrumLength / targetCount;
        int vectorSize = Vector<float>.Count;

        for (int i = 0; i < targetCount; i++)
        {
            int start = (int)(i * blockSize);
            int end = Min((int)((i + 1) * blockSize), spectrumLength);
            if (end <= start) end = start + 1;

            int length = end - start;
            ReadOnlySpan<float> block = spectrum.AsSpan(start, length);
            float sum = 0f;
            int j = 0;

            // Use SIMD for faster processing if possible
            for (; j <= length - vectorSize; j += vectorSize)
            {
                var vector = new Vector<float>(block.Slice(j, vectorSize));
                sum += Sum(vector);
            }

            // Process remaining elements
            for (; j < length; j++)
            {
                sum += block[j];
            }

            scaledSpectrum[i] = sum / length;
        }

        return scaledSpectrum;
    }

    /// <summary>
    /// Applies smoothing to the spectrum data with adaptive factor based on loudness.
    /// </summary>
    [MethodImpl(AggressiveOptimization)]
    private float[] SmoothSpectrum(float[] spectrum, int targetCount)
    {
        if (_previousSpectrum == null || _previousSpectrum.Length != targetCount)
            _previousSpectrum = new float[targetCount];

        float[] smoothedSpectrum = new float[targetCount];
        float loudness = CalculateLoudness(spectrum);
        float adaptiveFactor = _smoothingFactor * (1f + MathF.Pow(loudness, 2) * Constants.LOUDNESS_SMOOTH_FACTOR);
        int vectorSize = Vector<float>.Count;

        // Use SIMD vectorization for faster processing
        int i = 0;
        for (; i <= targetCount - vectorSize; i += vectorSize)
        {
            var current = new Vector<float>(spectrum, i);
            var previous = new Vector<float>(_previousSpectrum, i);
            var delta = current - previous;
            var smoothed = previous + delta * adaptiveFactor;
            smoothed = Max(Min(smoothed, Vector<float>.One), Vector<float>.Zero);
            smoothed.CopyTo(smoothedSpectrum, i);
            smoothed.CopyTo(_previousSpectrum, i);
        }

        // Process remaining elements
        for (; i < targetCount; i++)
        {
            float delta = spectrum[i] - _previousSpectrum[i];
            smoothedSpectrum[i] = _previousSpectrum[i] + delta * adaptiveFactor;
            smoothedSpectrum[i] = Clamp(smoothedSpectrum[i], 0f, 1f);
            _previousSpectrum[i] = smoothedSpectrum[i];
        }

        return smoothedSpectrum;
    }

    /// <summary>
    /// Calculates loudness factor from spectrum with frequency weighting.
    /// </summary>
    [MethodImpl(AggressiveInlining)]
    private static float CalculateLoudness(ReadOnlySpan<float> spectrum)
    {
        if (spectrum.IsEmpty) return 0f;

        float sum = 0f;
        int length = spectrum.Length;
        int subBass = length >> 4, bass = length >> 3, mid = length >> 2;

        for (int i = 0; i < length; i++)
        {
            float weight = i < subBass ? Constants.SUB_BASS_WEIGHT :
                          i < bass ? Constants.BASS_WEIGHT :
                          i < mid ? Constants.MID_WEIGHT : Constants.HIGH_WEIGHT;
            sum += MathF.Abs(spectrum[i]) * weight;
        }

        return Clamp(sum / length * Constants.LOUDNESS_SCALE, 0f, 1f);
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
                // Dispose rendering resources
                _path?.Dispose();
                _glowPaint?.Dispose();
                _barPaint?.Dispose();
                _highlightPaint?.Dispose();
                _reflectionPaint?.Dispose();

                // Dispose synchronization primitives
                _renderSemaphore?.Dispose();

                // Clean up cached data
                _previousSpectrum = null;
                _processedSpectrum = null;
                _colorCache = null;

                // Dispose object pools
                _paintPool?.Dispose();

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