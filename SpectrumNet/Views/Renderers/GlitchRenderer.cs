#nullable enable

using SpectrumNet.Service.Enums;

namespace SpectrumNet.Views.Renderers;

/// <summary>
/// Renderer that visualizes spectrum data with digital glitch effects.
/// </summary>
public sealed class GlitchRenderer : BaseSpectrumRenderer
{
    #region Constants
    private static class Constants
    {
        // Logging
        public const string LOG_PREFIX = "GlitchRenderer";

        // Glitch effect parameters
        public const float GLITCH_THRESHOLD = 0.5f;           // Threshold to trigger glitch effects
        public const float MAX_GLITCH_OFFSET = 30f;           // Maximum horizontal offset for glitch segments
        public const int MAX_GLITCH_SEGMENTS = 10;            // Maximum number of glitch segments
        public const float MIN_GLITCH_DURATION = 0.05f;       // Minimum duration of a glitch segment
        public const float MAX_GLITCH_DURATION = 0.3f;        // Maximum duration of a glitch segment

        // Scanline effect
        public const float SCANLINE_SPEED = 1.5f;             // Scanline movement speed in pixels per frame
        public const byte SCANLINE_ALPHA = 40;                // Scanline transparency
        public const float SCANLINE_HEIGHT = 3f;              // Scanline height in pixels

        // Color effects
        public const float RGB_SPLIT_MAX = 10f;               // Maximum RGB split distance

        // Animation and timing
        public const float TIME_STEP = 0.016f;                // Time increment per frame (~60 FPS)

        // Spectrum processing
        public const int BAND_COUNT = 3;                      // Number of frequency bands to analyze
        public const int PROCESSED_SPECTRUM_SIZE = 128;       // Size of processed spectrum array
        public const float SPECTRUM_SCALE = 2.0f;             // Scaling factor for spectrum values
        public const float SPECTRUM_CLAMP = 1.0f;             // Maximum value for processed spectrum
        public const float SENSITIVITY = 3.0f;                // Sensitivity multiplier for band activity
        public const float SMOOTHING_FACTOR = 0.2f;           // Smoothing factor for spectrum changes
    }
    #endregion

    #region Structures
    /// <summary>
    /// Represents a horizontal segment of the display that will be glitched.
    /// </summary>
    private struct GlitchSegment
    {
        public int Y;              // Vertical position
        public int Height;         // Height of segment
        public float XOffset;      // Horizontal offset
        public float Duration;     // Remaining duration in seconds
        public bool IsActive;      // Whether the segment is active
    }
    #endregion

    #region Fields
    private static readonly Lazy<GlitchRenderer> _instance = new(() => new GlitchRenderer());

    // Thread synchronization
    private readonly SemaphoreSlim _glitchSemaphore = new(1, 1);

    // Rendering resources
    private SKBitmap? _bufferBitmap;
    private readonly ObjectPool<SKPaint> _paintPool = new(() => new SKPaint(), paint => paint.Reset(), 5);

    // Cached paints
    private SKPaint? _bitmapPaint;
    private SKPaint? _redPaint;
    private SKPaint? _bluePaint;
    private SKPaint? _scanlinePaint;
    private SKPaint? _noisePaint;

    // Glitch state
    private float[]? _glitchProcessedSpectrum;
    private List<GlitchSegment>? _glitchSegments;
    private readonly Random _random = new();
    private float _timeAccumulator;
    private int _scanlinePosition;

    // Quality settings
    private new bool _useAntiAlias = true;
    private SKSamplingOptions _samplingOptions = new(SKFilterMode.Linear, SKMipmapMode.Linear);
    private new bool _useAdvancedEffects = true;

    // Disposal
    private new bool _disposed;
    #endregion

    #region Singleton Pattern
    /// <summary>
    /// Private constructor to enforce Singleton pattern.
    /// </summary>
    private GlitchRenderer() { }

    /// <summary>
    /// Gets the singleton instance of the glitch renderer.
    /// </summary>
    public static GlitchRenderer GetInstance() => _instance.Value;
    #endregion

    #region Initialization and Configuration
    /// <summary>
    /// Initializes the glitch renderer and prepares rendering resources.
    /// </summary>
    public override void Initialize()
    {
        Safe(() =>
        {
            base.Initialize();

            // Initialize paint objects
            _bitmapPaint = new SKPaint { IsAntialias = _useAntiAlias };

            _redPaint = new SKPaint
            {
                Color = SKColors.Red.WithAlpha(100),
                BlendMode = SKBlendMode.SrcOver,
                IsAntialias = _useAntiAlias,
            };

            _bluePaint = new SKPaint
            {
                Color = SKColors.Blue.WithAlpha(100),
                BlendMode = SKBlendMode.SrcOver,
                IsAntialias = _useAntiAlias,
            };

            _scanlinePaint = new SKPaint
            {
                Color = SKColors.White.WithAlpha(Constants.SCANLINE_ALPHA),
                Style = Fill
            };

            _noisePaint = new SKPaint
            {
                Style = Fill
            };

            // Initialize glitch segments
            _glitchSegments = new List<GlitchSegment>();

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

            // Apply quality settings if changed
            if (_quality != quality)
            {
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
                    _useAdvancedEffects = false;
                    break;

                case RenderQuality.Medium:
                    _useAntiAlias = true;
                    _samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                    _useAdvancedEffects = true;
                    break;

                case RenderQuality.High:
                    _useAntiAlias = true;
                    _samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                    _useAdvancedEffects = true;
                    break;
            }

            // Update paint settings
            if (_bitmapPaint != null) { _bitmapPaint.IsAntialias = _useAntiAlias; }
            if (_redPaint != null) { _redPaint.IsAntialias = _useAntiAlias; }
            if (_bluePaint != null) { _bluePaint.IsAntialias = _useAntiAlias; }
        }, new ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.ApplyQualitySettings",
            ErrorMessage = "Failed to apply quality settings"
        });
    }
    #endregion

    #region Rendering
    /// <summary>
    /// Renders the glitch visualization on the canvas using spectrum data.
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
        if (!QuickValidate(canvas, spectrum, info, paint))
        {
            drawPerformanceInfo?.Invoke(canvas!, info);
            return;
        }

        bool semaphoreAcquired = false;
        try
        {
            semaphoreAcquired = _glitchSemaphore.Wait(0);

            if (semaphoreAcquired)
            {
                CreateBufferIfNeeded(info);
                ProcessSpectrum(spectrum!);
                UpdateGlitchEffects(info);
            }

            RenderGlitchEffect(canvas!, info, paint!);
            drawPerformanceInfo?.Invoke(canvas!, info);
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, Constants.LOG_PREFIX, $"Error in Render: {ex.Message}");
        }
        finally
        {
            if (semaphoreAcquired) _glitchSemaphore.Release();
        }
    }

    /// <summary>
    /// Renders the glitch effect using the buffer and glitch segments.
    /// </summary>
    [MethodImpl(AggressiveOptimization)]
    private void RenderGlitchEffect(SKCanvas canvas, SKImageInfo info, SKPaint basePaint)
    {
        if (_bufferBitmap == null || _bitmapPaint == null || _glitchSegments == null ||
            _glitchProcessedSpectrum == null) return;

        // Step 1: Render to buffer bitmap
        using (var bufferCanvas = new SKCanvas(_bufferBitmap))
        {
            bufferCanvas.Clear(SKColors.Black);

            // Draw base spectrum visualization
            DrawBaseSpectrum(bufferCanvas, info, basePaint);

            // Apply RGB split effect if enabled and triggered
            if (_useAdvancedEffects && _glitchProcessedSpectrum[2] > Constants.GLITCH_THRESHOLD * 0.5f)
            {
                float rgbSplit = _glitchProcessedSpectrum[2] * Constants.RGB_SPLIT_MAX;
                var rgbEffects = new[]
                {
                    (-rgbSplit, 0f, _redPaint!),
                    (rgbSplit, 0f, _bluePaint!)
                };

                foreach (var (dx, dy, effectPaint) in rgbEffects)
                {
                    bufferCanvas.Save();
                    bufferCanvas.Translate(dx, dy);
                    DrawBaseSpectrum(bufferCanvas, info, effectPaint);
                    bufferCanvas.Restore();
                }
            }

            // Draw scanline
            bufferCanvas.DrawRect(0, _scanlinePosition, info.Width, Constants.SCANLINE_HEIGHT, _scanlinePaint!);

            // Add digital noise if enabled and triggered
            if (_useAdvancedEffects && _glitchProcessedSpectrum[1] > Constants.GLITCH_THRESHOLD * 0.3f)
            {
                DrawDigitalNoise(bufferCanvas, info, _glitchProcessedSpectrum[1]);
            }
        }

        // Step 2: Render buffer to canvas with glitch segments
        var activeSegments = _glitchSegments.Where(s => s.IsActive).OrderBy(s => s.Y).ToList();
        int currentY = 0;

        // Draw unaffected areas before and between glitch segments
        foreach (var segment in activeSegments)
        {
            if (currentY < segment.Y)
            {
                SKRect sourceRect = new(0, currentY, info.Width, segment.Y);
                SKRect destRect = new(0, currentY, info.Width, segment.Y);
                DrawBitmapSegment(canvas, _bufferBitmap, sourceRect, destRect);
            }
            currentY = segment.Y + segment.Height;
        }

        // Draw remaining unaffected area after all segments
        if (currentY < info.Height)
        {
            SKRect sourceRect = new(0, currentY, info.Width, info.Height);
            SKRect destRect = new(0, currentY, info.Width, info.Height);
            DrawBitmapSegment(canvas, _bufferBitmap, sourceRect, destRect);
        }

        // Draw all glitch segments with offsets
        foreach (var segment in activeSegments)
        {
            SKRect sourceRect = new(0, segment.Y, info.Width, segment.Y + segment.Height);
            SKRect destRect = new(segment.XOffset, segment.Y, segment.XOffset + info.Width, segment.Y + segment.Height);
            DrawBitmapSegment(canvas, _bufferBitmap, sourceRect, destRect);
        }
    }

    /// <summary>
    /// Draws the base spectrum visualization in the form of vertical lines.
    /// </summary>
    private void DrawBaseSpectrum(SKCanvas canvas, SKImageInfo info, SKPaint paint)
    {
        if (_glitchProcessedSpectrum == null) return;

        float barCount = Min(Constants.PROCESSED_SPECTRUM_SIZE, _glitchProcessedSpectrum.Length);
        float barWidth = info.Width / barCount;

        for (int i = 0; i < barCount; i++)
        {
            float amplitude = _glitchProcessedSpectrum[i];
            float x = i * barWidth;
            float height = amplitude * info.Height * 0.5f;
            canvas.DrawLine(x, info.Height / 2 - height / 2, x, info.Height / 2 + height / 2, paint);
        }
    }

    /// <summary>
    /// Draws random digital noise dots on the canvas.
    /// </summary>
    private void DrawDigitalNoise(SKCanvas canvas, SKImageInfo info, float intensity)
    {
        int noiseCount = (int)(intensity * 1000);
        _noisePaint!.Color = SKColors.White.WithAlpha((byte)(intensity * 150));

        for (int i = 0; i < noiseCount; i++)
        {
            float x = _random.Next(0, info.Width);
            float y = _random.Next(0, info.Height);
            float size = 1 + _random.Next(0, 3);
            canvas.DrawRect(x, y, size, size, _noisePaint);
        }
    }

    /// <summary>
    /// Draws a segment of the bitmap to the canvas.
    /// </summary>
    [MethodImpl(AggressiveInlining)]
    private void DrawBitmapSegment(SKCanvas canvas, SKBitmap bitmap, SKRect source, SKRect dest)
    {
        canvas.DrawBitmap(bitmap, source, dest, _bitmapPaint);
    }
    #endregion

    #region Spectrum Processing
    /// <summary>
    /// Processes the spectrum data for glitch effects.
    /// </summary>
    [MethodImpl(AggressiveOptimization)]
    private void ProcessSpectrum(float[] spectrum)
    {
        // Initialize or resize processed spectrum array if needed
        if (_glitchProcessedSpectrum == null || _glitchProcessedSpectrum.Length < Constants.PROCESSED_SPECTRUM_SIZE)
        {
            _glitchProcessedSpectrum = new float[Constants.PROCESSED_SPECTRUM_SIZE];
        }

        // Resample spectrum to target size
        for (int i = 0; i < Constants.PROCESSED_SPECTRUM_SIZE; i++)
        {
            float index = (float)i * spectrum.Length / (2f * Constants.PROCESSED_SPECTRUM_SIZE);
            int baseIndex = (int)index;
            float lerp = index - baseIndex;

            if (baseIndex < spectrum.Length / 2 - 1)
            {
                _glitchProcessedSpectrum[i] = spectrum[baseIndex] * (1 - lerp) + spectrum[baseIndex + 1] * lerp;
            }
            else
            {
                _glitchProcessedSpectrum[i] = spectrum[spectrum.Length / 2 - 1];
            }

            // Apply scaling and clamping
            _glitchProcessedSpectrum[i] *= Constants.SPECTRUM_SCALE;
            _glitchProcessedSpectrum[i] = Min(_glitchProcessedSpectrum[i], Constants.SPECTRUM_CLAMP);
        }

        // Process specific frequency bands for glitch effects
        float[] bandActivity = new float[Constants.BAND_COUNT];
        int bandSize = spectrum.Length / (2 * Constants.BAND_COUNT);

        for (int band = 0; band < Constants.BAND_COUNT; band++)
        {
            float avg = CalculateBandActivity(spectrum, band, bandSize);

            if (band < _glitchProcessedSpectrum.Length)
            {
                if (_glitchProcessedSpectrum[band] == 0)
                {
                    bandActivity[band] = avg * Constants.SENSITIVITY;
                }
                else
                {
                    bandActivity[band] = _glitchProcessedSpectrum[band] +
                                        (avg * Constants.SENSITIVITY - _glitchProcessedSpectrum[band]) *
                                        Constants.SMOOTHING_FACTOR;
                }
            }
        }

        // Update the first few elements with band activity for effects control
        for (int i = 0; i < Constants.BAND_COUNT; i++)
        {
            _glitchProcessedSpectrum[i] = Min(bandActivity[i], Constants.SPECTRUM_CLAMP);
        }
    }

    /// <summary>
    /// Calculates the average activity for a specific frequency band.
    /// </summary>
    [MethodImpl(AggressiveInlining)]
    private float CalculateBandActivity(float[] spectrum, int band, int bandSize)
    {
        int start = band * bandSize;
        int end = Min((band + 1) * bandSize, spectrum.Length / 2);
        float sum = 0;

        for (int i = start; i < end; i++)
        {
            sum += spectrum[i];
        }

        return sum / (end - start);
    }
    #endregion

    #region Glitch Effects
    /// <summary>
    /// Creates or recreates the buffer bitmap if needed.
    /// </summary>
    private void CreateBufferIfNeeded(SKImageInfo info)
    {
        if (_bufferBitmap == null || _bufferBitmap.Width != info.Width || _bufferBitmap.Height != info.Height)
        {
            _bufferBitmap?.Dispose();
            _bufferBitmap = new SKBitmap(info.Width, info.Height);
        }
    }

    /// <summary>
    /// Updates glitch effects state based on spectrum data.
    /// </summary>
    private void UpdateGlitchEffects(SKImageInfo info)
    {
        if (_glitchSegments == null || _glitchProcessedSpectrum == null) return;

        // Update time and scanline position
        _timeAccumulator += Constants.TIME_STEP;
        _scanlinePosition = (int)(_scanlinePosition + Constants.SCANLINE_SPEED);
        if (_scanlinePosition >= info.Height) _scanlinePosition = 0;

        // Update existing glitch segments
        for (int i = _glitchSegments.Count - 1; i >= 0; i--)
        {
            var segment = _glitchSegments[i];
            segment.Duration -= Constants.TIME_STEP;

            if (segment.Duration <= 0)
            {
                segment.IsActive = false;
                _glitchSegments.RemoveAt(i);
            }
            else
            {
                // Randomly update segment offset
                if (_random.NextDouble() < 0.2)
                {
                    segment.XOffset = (float)(Constants.MAX_GLITCH_OFFSET *
                                           (_random.NextDouble() * 2 - 1) *
                                           _glitchProcessedSpectrum[0]);
                }
                _glitchSegments[i] = segment;
            }
        }

        // Create new glitch segments based on spectrum intensity
        if (_glitchProcessedSpectrum[0] > Constants.GLITCH_THRESHOLD &&
            _glitchSegments.Count < Constants.MAX_GLITCH_SEGMENTS &&
            _random.NextDouble() < _glitchProcessedSpectrum[0] * 0.4)
        {
            int segmentHeight = (int)(20 + _random.NextDouble() * 50);
            int y = _random.Next(0, info.Height - segmentHeight);
            float duration = Constants.MIN_GLITCH_DURATION +
                            (float)_random.NextDouble() *
                            (Constants.MAX_GLITCH_DURATION - Constants.MIN_GLITCH_DURATION);
            float xOffset = (float)(Constants.MAX_GLITCH_OFFSET *
                                 (_random.NextDouble() * 2 - 1) *
                                 _glitchProcessedSpectrum[0]);

            _glitchSegments.Add(new GlitchSegment
            {
                Y = y,
                Height = segmentHeight,
                XOffset = xOffset,
                Duration = duration,
                IsActive = true
            });
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
                _glitchSemaphore?.Dispose();
                _bufferBitmap?.Dispose();
                _bitmapPaint?.Dispose();
                _redPaint?.Dispose();
                _bluePaint?.Dispose();
                _scanlinePaint?.Dispose();
                _noisePaint?.Dispose();
                _paintPool?.Dispose();

                _bufferBitmap = null;
                _bitmapPaint = null;
                _redPaint = null;
                _bluePaint = null;
                _scanlinePaint = null;
                _noisePaint = null;
                _glitchSegments = null;
                _glitchProcessedSpectrum = null;

                base.Dispose();

                _disposed = true;
                Log(LogLevel.Debug, Constants.LOG_PREFIX, "Disposed");
            }, new ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.Dispose",
                ErrorMessage = "Error during disposal"
            });
        }
    }
    #endregion
}