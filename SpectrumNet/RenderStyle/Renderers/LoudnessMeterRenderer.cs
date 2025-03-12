#nullable enable

namespace SpectrumNet
{
    public class LoudnessMeterRenderer : ISpectrumRenderer, IDisposable
    {
        #region Fields

        private static LoudnessMeterRenderer? _instance;
        private bool _isInitialized;
        private bool _disposed = false;
        private readonly SemaphoreSlim _renderSemaphore = new(1, 1);
        private readonly object _loudnessLock = new();

        // Loudness calculation constants
        private const float MIN_LOUDNESS_THRESHOLD = 0.001f; // Minimum loudness to trigger rendering
        private const float SMOOTHING_FACTOR_NORMAL = 0.3f;   // Smoothing factor for normal mode
        private const float SMOOTHING_FACTOR_OVERLAY = 0.5f;   // Smoothing factor for overlay mode
        private const float PEAK_DECAY_RATE = 0.05f;  // Rate at which peak loudness decays

        // Rendering constants
        private const float GLOW_INTENSITY = 0.4f;   // Intensity factor for glow effect
        private const float HIGH_LOUDNESS_THRESHOLD = 0.7f;   // Threshold for high loudness (red)
        private const float MEDIUM_LOUDNESS_THRESHOLD = 0.4f;   // Threshold for medium loudness (yellow)
        private const float BORDER_WIDTH = 1.5f;   // Width of the border stroke
        private const float BLUR_SIGMA = 10f;    // Sigma value for blur mask filter
        private const float PEAK_RECT_HEIGHT = 4f;     // Height of the peak indicator rectangle
        private const float GLOW_HEIGHT_FACTOR = 1f / 3f;// Factor for glow height relative to meter
        private const int MARKER_COUNT = 10;     // Number of divisions for markers

        // Gradient constants
        private const float GRADIENT_ALPHA_FACTOR = 0.8f;   // Alpha transparency factor for gradient colors

        // Quality settings
        private RenderQuality _quality = RenderQuality.Medium;
        private bool _useAntiAlias = true;
        private SKFilterQuality _filterQuality = SKFilterQuality.Medium;
        private bool _useAdvancedEffects = true;

        // Rendering fields
        private float _smoothingFactor = SMOOTHING_FACTOR_NORMAL;
        private float _previousLoudness = 0f;
        private float _peakLoudness = 0f;
        private float? _cachedLoudness;
        private SKPicture? _staticPicture;
        private SKPaint? _backgroundPaint;
        private SKPaint? _markerPaint;
        private SKPaint? _fillPaint;
        private SKPaint? _glowPaint;
        private SKPaint? _peakPaint;
        private int _currentWidth;
        private int _currentHeight;

        // Logging
        private const string LOG_PREFIX = "[LoudnessMeterRenderer] ";

        #endregion

        #region Initialization

        public LoudnessMeterRenderer() { }

        public static LoudnessMeterRenderer GetInstance() => _instance ??= new LoudnessMeterRenderer();

        public void Initialize()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(LoudnessMeterRenderer));
            }

            if (_isInitialized) return;
            _isInitialized = true;
            _previousLoudness = 0f;
            _peakLoudness = 0f;

            _backgroundPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = BORDER_WIDTH,
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
                IsAntialias = _useAntiAlias,
                FilterQuality = _filterQuality
            };

            _glowPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                IsAntialias = _useAntiAlias,
                FilterQuality = _filterQuality,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, BLUR_SIGMA)
            };

            _peakPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                IsAntialias = _useAntiAlias
            };

            SmartLogger.Log(LogLevel.Debug, LOG_PREFIX, "Initialized");
        }

        public void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(LoudnessMeterRenderer));
            }

            _smoothingFactor = isOverlayActive ? SMOOTHING_FACTOR_OVERLAY : SMOOTHING_FACTOR_NORMAL;
            Quality = quality;
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
            Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
        {
            try
            {
                if (!ValidateRenderParameters(canvas, spectrum, info, paint))
                    return;

                float loudness = 0f;
                bool semaphoreAcquired = false;

                try
                {
                    semaphoreAcquired = _renderSemaphore.Wait(0);

                    if (semaphoreAcquired)
                    {
                        loudness = CalculateAndSmoothLoudness(spectrum!);
                        _cachedLoudness = loudness;

                        if (loudness > _peakLoudness)
                        {
                            _peakLoudness = loudness;
                        }
                        else
                        {
                            _peakLoudness = Math.Max(0, _peakLoudness - PEAK_DECAY_RATE);
                        }
                    }
                    else
                    {
                        lock (_loudnessLock)
                        {
                            loudness = _cachedLoudness ?? CalculateAndSmoothLoudness(spectrum!);
                        }
                    }
                }
                finally
                {
                    if (semaphoreAcquired)
                    {
                        _renderSemaphore.Release();
                    }
                }

                if (info.Width != _currentWidth || info.Height != _currentHeight)
                {
                    _currentWidth = info.Width;
                    _currentHeight = info.Height;
                    UpdateStaticElements();
                }

                RenderEnhancedMeter(canvas!, info, loudness, _peakLoudness);
                drawPerformanceInfo(canvas!, info);
            }
            catch (ObjectDisposedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LOG_PREFIX, $"Rendering failed: {ex.Message}");
            }
        }

        private bool ValidateRenderParameters(
            SKCanvas? canvas,
            float[]? spectrum,
            SKImageInfo info,
            SKPaint? paint)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(LoudnessMeterRenderer));
            }

            if (!_isInitialized)
            {
                SmartLogger.Log(LogLevel.Error, LOG_PREFIX, "Not initialized");
                return false;
            }

            if (canvas == null)
            {
                SmartLogger.Log(LogLevel.Error, LOG_PREFIX, "Canvas is null");
                return false;
            }

            if (spectrum == null || spectrum.Length == 0)
            {
                SmartLogger.Log(LogLevel.Error, LOG_PREFIX, "Spectrum is null or empty");
                return false;
            }

            if (paint == null)
            {
                SmartLogger.Log(LogLevel.Error, LOG_PREFIX, "Paint is null");
                return false;
            }

            if (info.Width <= 0 || info.Height <= 0)
            {
                SmartLogger.Log(LogLevel.Error, LOG_PREFIX, "Invalid canvas dimensions");
                return false;
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float CalculateAndSmoothLoudness(float[] spectrum)
        {
            float rawLoudness = CalculateLoudness(spectrum.AsSpan());
            float smoothedLoudness = _previousLoudness + (rawLoudness - _previousLoudness) * _smoothingFactor;
            smoothedLoudness = Math.Clamp(smoothedLoudness, MIN_LOUDNESS_THRESHOLD, 1f);
            _previousLoudness = smoothedLoudness;
            return smoothedLoudness;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float CalculateLoudness(ReadOnlySpan<float> spectrum)
        {
            if (spectrum.IsEmpty) return 0f;
            float sum = 0f;
            for (int i = 0; i < spectrum.Length; i++)
                sum += Math.Abs(spectrum[i]);
            return Math.Clamp(sum / spectrum.Length, 0f, 1f);
        }

        private void UpdateStaticElements()
        {
            var gradientShader = SKShader.CreateLinearGradient(
                new SKPoint(0, _currentHeight),
                new SKPoint(0, 0),
                new[]
                {
                    SKColors.Green.WithAlpha((byte)(255 * GRADIENT_ALPHA_FACTOR)),
                    SKColors.Yellow.WithAlpha((byte)(255 * GRADIENT_ALPHA_FACTOR)),
                    SKColors.Red.WithAlpha((byte)(255 * GRADIENT_ALPHA_FACTOR))
                },
                new[] { 0f, 0.5f, 1.0f },
                SKShaderTileMode.Clamp);
            _fillPaint!.Shader = gradientShader;

            using var recorder = new SKPictureRecorder();
            using var canvas = recorder.BeginRecording(new SKRect(0, 0, _currentWidth, _currentHeight));
            canvas.DrawRect(0, 0, _currentWidth, _currentHeight, _backgroundPaint);
            for (int i = 1; i < MARKER_COUNT; i++)
            {
                float y = _currentHeight - (_currentHeight * i / (float)MARKER_COUNT);
                canvas.DrawLine(0, y, _currentWidth, y, _markerPaint);
            }
            _staticPicture?.Dispose();
            _staticPicture = recorder.EndRecording();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RenderEnhancedMeter(
            SKCanvas canvas,
            SKImageInfo info,
            float loudness,
            float peakLoudness)
        {
            if (loudness < MIN_LOUDNESS_THRESHOLD) return;

            float meterHeight = info.Height * loudness;
            float peakHeight = info.Height * peakLoudness;

            canvas.Save();
            canvas.DrawPicture(_staticPicture);
            canvas.DrawRect(0, info.Height - meterHeight, info.Width, meterHeight, _fillPaint);

            if (_useAdvancedEffects && loudness > HIGH_LOUDNESS_THRESHOLD)
            {
                byte alpha = (byte)(255 * GLOW_INTENSITY * (loudness - HIGH_LOUDNESS_THRESHOLD) / (1 - HIGH_LOUDNESS_THRESHOLD));
                _glowPaint!.Color = SKColors.Red.WithAlpha(alpha);
                canvas.DrawRect(0, info.Height - meterHeight, info.Width, meterHeight * GLOW_HEIGHT_FACTOR, _glowPaint);
            }

            float peakLineY = info.Height - peakHeight;
            _peakPaint!.Color = loudness > HIGH_LOUDNESS_THRESHOLD ? SKColors.Red :
                                loudness > MEDIUM_LOUDNESS_THRESHOLD ? SKColors.Yellow : SKColors.Green;
            canvas.DrawRect(0, peakLineY - PEAK_RECT_HEIGHT / 2, info.Width, PEAK_RECT_HEIGHT, _peakPaint);

            canvas.Restore();
        }

        #endregion

        #region Quality Settings

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

            if (_backgroundPaint != null) _backgroundPaint.IsAntialias = _useAntiAlias;
            if (_markerPaint != null) _markerPaint.IsAntialias = _useAntiAlias;
            if (_fillPaint != null)
            {
                _fillPaint.IsAntialias = _useAntiAlias;
                _fillPaint.FilterQuality = _filterQuality;
            }
            if (_glowPaint != null)
            {
                _glowPaint.IsAntialias = _useAntiAlias;
                _glowPaint.FilterQuality = _filterQuality;
            }
            if (_peakPaint != null) _peakPaint.IsAntialias = _useAntiAlias;
        }

        #endregion

        #region Disposal

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                _renderSemaphore?.Dispose();
                _staticPicture?.Dispose();
                _backgroundPaint?.Dispose();
                _markerPaint?.Dispose();
                _fillPaint?.Dispose();
                _glowPaint?.Dispose();
                _peakPaint?.Dispose();
            }

            _disposed = true;
            SmartLogger.Log(LogLevel.Debug, LOG_PREFIX, "Disposed");
        }

        ~LoudnessMeterRenderer()
        {
            Dispose(false);
        }

        #endregion
    }
}