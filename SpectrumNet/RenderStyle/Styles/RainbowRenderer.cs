#nullable enable

namespace SpectrumNet
{
    public sealed class RainbowRenderer : ISpectrumRenderer, IDisposable
    {
        #region Constants
        private static class Constants
        {
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
        }

        private const string LogPrefix = "[RainbowRenderer] ";
        #endregion

        #region Fields
        private static RainbowRenderer? _instance;
        private bool _isInitialized;
        private readonly SKPath _path = new();
        private volatile bool _disposed;
        private volatile float[]? _processedSpectrum;
        private float[]? _previousSpectrum;
        private readonly SKPaint _barPaint;
        private readonly SKPaint _highlightPaint;
        private readonly SKPaint _reflectionPaint;
        private SKPaint? _glowPaint;
        private SKColor[]? _colorCache;

        // Quality-related fields
        private RenderQuality _quality = RenderQuality.Medium;
        private bool _useAntiAlias = true;
        private SKFilterQuality _filterQuality = SKFilterQuality.Medium;
        private bool _useAdvancedEffects = true;
        private float _smoothingFactor = Constants.SMOOTHING_BASE;
        #endregion

        #region Constructor and Initialization
        private RainbowRenderer()
        {
            _barPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill
            };

            _highlightPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = SKColors.White
            };

            _reflectionPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                BlendMode = SKBlendMode.SrcOver
            };

            ApplyQualitySettings();
        }

        public static RainbowRenderer GetInstance() => _instance ??= new RainbowRenderer();

        public void Initialize()
        {
            if (_isInitialized)
                return;

            _glowPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                ImageFilter = SKImageFilter.CreateBlur(Constants.GLOW_RADIUS, Constants.GLOW_RADIUS)
            };

            _colorCache = new SKColor[Constants.MAX_ALPHA + 1];
            for (int i = 0; i <= Constants.MAX_ALPHA; i++)
            {
                float normalizedValue = i / (float)Constants.MAX_ALPHA;
                _colorCache[i] = GetRainbowColor(normalizedValue);
            }

            ApplyQualitySettings();
            _isInitialized = true;
            SmartLogger.Log(LogLevel.Debug, LogPrefix, "Renderer initialized");
        }
        #endregion

        #region Properties
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
        #endregion

        #region Configuration
        public void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium)
        {
            _smoothingFactor = isOverlayActive ? Constants.SMOOTHING_OVERLAY : Constants.SMOOTHING_BASE;
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
            SKPaint? basePaint,
            Action<SKCanvas, SKImageInfo>? drawPerformanceInfo)
        {
            if (!ValidateRenderParameters(canvas, spectrum, info, basePaint))
                return;

            int spectrumLength = spectrum!.Length;
            int actualBarCount = Math.Min(spectrumLength, barCount);
            float[] renderSpectrum = _processedSpectrum ?? ProcessSynchronously(spectrum, actualBarCount, spectrumLength);

            using var _ = new SKAutoCanvasRestore(canvas!, true);
            RenderBars(canvas!, renderSpectrum, info, barWidth, barSpacing, basePaint!);
            drawPerformanceInfo?.Invoke(canvas!, info);

            Task.Run(() =>
            {
                try
                {
                    float[] latestSpectrum = spectrum.ToArray();
                    float[] processed = ProcessSpectrum(latestSpectrum, actualBarCount, spectrumLength);
                    _processedSpectrum = processed;
                }
                catch (Exception ex)
                {
                    SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error processing spectrum: {ex.Message}");
                }
            });
        }

        private bool ValidateRenderParameters(
            SKCanvas? canvas,
            float[]? spectrum,
            SKImageInfo info,
            SKPaint? basePaint)
        {
            if (!_isInitialized)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Renderer not initialized before rendering");
                return false;
            }

            if (canvas == null || spectrum == null || spectrum.Length < 2 || basePaint == null ||
                info.Width <= 0 || info.Height <= 0)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Invalid render parameters");
                return false;
            }

            return true;
        }

        private float[] ProcessSynchronously(float[] spectrum, int targetCount, int spectrumLength)
        {
            float[] scaledSpectrum = ScaleSpectrum(spectrum, targetCount, spectrumLength);
            return SmoothSpectrum(scaledSpectrum, targetCount);
        }

        private float[] ProcessSpectrum(float[] spectrum, int targetCount, int spectrumLength)
        {
            return ProcessSynchronously(spectrum, targetCount, spectrumLength);
        }

        private void RenderBars(
            SKCanvas canvas,
            float[] spectrum,
            SKImageInfo info,
            float barWidth,
            float barSpacing,
            SKPaint basePaint)
        {
            float totalBarWidth = barWidth + barSpacing;
            float canvasHeight = info.Height;
            float startX = (info.Width - (spectrum.Length * totalBarWidth - barSpacing)) / 2f;
            float loudness = CalculateLoudness(spectrum);
            float reflectionHeight = canvasHeight * Constants.REFLECTION_HEIGHT;

            for (int i = 0; i < spectrum.Length; i++)
            {
                float magnitude = Math.Clamp(spectrum[i], 0f, 1f);
                if (magnitude < Constants.MIN_MAGNITUDE_THRESHOLD)
                    continue;

                float barHeight = magnitude * canvasHeight;
                float x = startX + i * totalBarWidth;
                float y = canvasHeight - barHeight;
                var barRect = new SKRect(x, y, x + barWidth, canvasHeight);

                if (canvas.QuickReject(barRect))
                    continue;

                SKColor barColor = GetBarColor(magnitude);
                byte baseAlpha = (byte)Math.Clamp(magnitude * Constants.MAX_ALPHA, 0, Constants.MAX_ALPHA);

                if (_useAdvancedEffects && _glowPaint != null &&
                    magnitude > Constants.GLOW_MIN_MAGNITUDE && magnitude <= Constants.GLOW_MAX_MAGNITUDE)
                {
                    float adjustedGlowRadius = Constants.GLOW_RADIUS * (1 + loudness * Constants.GLOW_LOUDNESS_FACTOR);
                    if (Math.Abs(adjustedGlowRadius - Constants.GLOW_RADIUS) > Constants.GLOW_RADIUS_THRESHOLD)
                    {
                        _glowPaint.ImageFilter = SKImageFilter.CreateBlur(adjustedGlowRadius, adjustedGlowRadius);
                    }

                    byte glowAlpha = (byte)Math.Clamp(magnitude * Constants.MAX_ALPHA * Constants.GLOW_INTENSITY, 0, Constants.MAX_ALPHA);
                    _glowPaint.Color = barColor.WithAlpha(glowAlpha);
                    canvas.DrawRoundRect(barRect, Constants.CORNER_RADIUS, Constants.CORNER_RADIUS, _glowPaint);
                }

                using var shader = SKShader.CreateLinearGradient(
                    new SKPoint(x, y),
                    new SKPoint(x + barWidth, y),
                    new[] { barColor, barColor.WithAlpha((byte)(Constants.MAX_ALPHA * Constants.GRADIENT_ALPHA_FACTOR)) },
                    new[] { 0f, 1f },
                    SKShaderTileMode.Clamp);

                byte barAlpha = (byte)Math.Clamp(magnitude * Constants.ALPHA_MULTIPLIER * Constants.MAX_ALPHA, 0, Constants.MAX_ALPHA);
                _barPaint.Color = barColor.WithAlpha(barAlpha);
                _barPaint.Shader = shader;
                canvas.DrawRoundRect(barRect, Constants.CORNER_RADIUS, Constants.CORNER_RADIUS, _barPaint);

                if (barHeight <= Constants.CORNER_RADIUS * 2)
                    continue;

                float highlightWidth = barWidth * Constants.HIGHLIGHT_WIDTH_PROP;
                float highlightHeight = Math.Min(barHeight * Constants.HIGHLIGHT_HEIGHT_PROP, Constants.CORNER_RADIUS);
                byte highlightAlpha = (byte)Math.Clamp(magnitude * Constants.MAX_ALPHA * Constants.HIGHLIGHT_ALPHA, 0, Constants.MAX_ALPHA);
                _highlightPaint.Color = SKColors.White.WithAlpha(highlightAlpha);
                float highlightX = x + (barWidth - highlightWidth) / 2;
                canvas.DrawRect(highlightX, y, highlightWidth, highlightHeight, _highlightPaint);

                if (_useAdvancedEffects && magnitude > Constants.REFLECTION_MIN_MAGNITUDE)
                {
                    byte reflectionAlpha = (byte)Math.Clamp(magnitude * Constants.MAX_ALPHA * Constants.REFLECTION_OPACITY, 0, Constants.MAX_ALPHA);
                    _reflectionPaint.Color = barColor.WithAlpha(reflectionAlpha);
                    float reflectHeight = Math.Min(barHeight * Constants.REFLECTION_FACTOR, reflectionHeight);
                    canvas.DrawRect(x, canvasHeight, barWidth, reflectHeight, _reflectionPaint);
                }
            }
        }

        private SKColor GetBarColor(float magnitude)
        {
            int colorIndex = Math.Clamp((int)(magnitude * Constants.MAX_ALPHA), 0, Constants.MAX_ALPHA);
            return _colorCache != null ? _colorCache[colorIndex] : GetRainbowColor(magnitude);
        }
        #endregion

        #region Spectrum Processing
        private static float[] ScaleSpectrum(float[] spectrum, int targetCount, int spectrumLength)
        {
            float[] scaledSpectrum = new float[targetCount];
            float blockSize = (float)spectrumLength / targetCount;
            int vectorSize = Vector<float>.Count;

            for (int i = 0; i < targetCount; i++)
            {
                int start = (int)(i * blockSize);
                int end = Math.Min((int)((i + 1) * blockSize), spectrumLength);
                if (end <= start) end = start + 1;

                int length = end - start;
                ReadOnlySpan<float> block = spectrum.AsSpan(start, length);
                float sum = 0f;
                int j = 0;

                for (; j <= length - vectorSize; j += vectorSize)
                {
                    var vector = new Vector<float>(block.Slice(j, vectorSize));
                    sum += Vector.Sum(vector);
                }

                for (; j < length; j++)
                {
                    sum += block[j];
                }

                scaledSpectrum[i] = sum / length;
            }

            return scaledSpectrum;
        }

        private float[] SmoothSpectrum(float[] spectrum, int targetCount)
        {
            if (_previousSpectrum == null || _previousSpectrum.Length != targetCount)
                _previousSpectrum = new float[targetCount];

            float[] smoothedSpectrum = new float[targetCount];
            float loudness = CalculateLoudness(spectrum);
            float adaptiveFactor = _smoothingFactor * (1f + MathF.Pow(loudness, 2) * Constants.LOUDNESS_SMOOTH_FACTOR);
            int vectorSize = Vector<float>.Count;

            int i = 0;
            for (; i <= targetCount - vectorSize; i += vectorSize)
            {
                var current = new Vector<float>(spectrum, i);
                var previous = new Vector<float>(_previousSpectrum, i);
                var delta = current - previous;
                var smoothed = previous + delta * adaptiveFactor;
                smoothed = Vector.Max(Vector.Min(smoothed, Vector<float>.One), Vector<float>.Zero);
                smoothed.CopyTo(smoothedSpectrum, i);
                smoothed.CopyTo(_previousSpectrum, i);
            }

            for (; i < targetCount; i++)
            {
                float delta = spectrum[i] - _previousSpectrum[i];
                smoothedSpectrum[i] = _previousSpectrum[i] + delta * adaptiveFactor;
                smoothedSpectrum[i] = Math.Clamp(smoothedSpectrum[i], 0f, 1f);
                _previousSpectrum[i] = smoothedSpectrum[i];
            }

            return smoothedSpectrum;
        }

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

            return Math.Clamp(sum / length * Constants.LOUDNESS_SCALE, 0f, 1f);
        }

        private static SKColor GetRainbowColor(float normalizedValue)
        {
            normalizedValue = Math.Clamp(normalizedValue, 0f, 1f);
            float hue = Constants.HUE_START - Constants.HUE_RANGE * normalizedValue;
            if (hue < 0) hue += 360;
            float brightness = Constants.BRIGHTNESS_BASE + normalizedValue * Constants.BRIGHTNESS_RANGE;
            return SKColor.FromHsv(hue, Constants.SATURATION, brightness);
        }
        #endregion

        #region Quality Settings
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

            _barPaint.IsAntialias = _useAntiAlias;
            _barPaint.FilterQuality = _filterQuality;
            _highlightPaint.IsAntialias = _useAntiAlias;
            _reflectionPaint.IsAntialias = _useAntiAlias;

            if (_glowPaint != null)
            {
                _glowPaint.IsAntialias = _useAntiAlias;
                _glowPaint.FilterQuality = _filterQuality;
            }
        }
        #endregion

        #region Disposal
        private void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                _path?.Dispose();
                _glowPaint?.Dispose();
                _barPaint?.Dispose();
                _highlightPaint?.Dispose();
                _reflectionPaint?.Dispose();
                _previousSpectrum = null;
                _processedSpectrum = null;
                _colorCache = null;
            }

            _disposed = true;
            SmartLogger.Log(LogLevel.Debug, LogPrefix, "Renderer disposed");
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}