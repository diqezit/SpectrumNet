#nullable enable

namespace SpectrumNet
{
    public sealed class RainbowRenderer : ISpectrumRenderer, IDisposable
    {
        #region Fields
        private static RainbowRenderer? _instance;
        private bool _isInitialized;
        private readonly SKPath _path = new();
        private volatile bool _disposed;
        private readonly SemaphoreSlim _spectrumSemaphore = new(1, 1);
        private readonly object _spectrumLock = new();

        private const float MinMagnitudeThreshold = 0.008f;
        private const float AlphaMultiplier = 1.7f;
        private const float HighlightAlpha = 0.8f;
        private const float GlowIntensity = 0.45f;
        private const float GlowRadius = 6f;
        private const float CornerRadius = 8f;
        private const float HighlightHeightProportion = 0.08f;
        private const float HighlightWidthProportion = 0.7f;
        private const float ReflectionOpacity = 0.3f;
        private const float ReflectionHeight = 0.15f;

        private float _smoothingFactor = 0.3f;
        private float[]? _previousSpectrum;
        private float[]? _processedSpectrum;
        private readonly SKPaint _barPaint;
        private readonly SKPaint _highlightPaint;
        private readonly SKPaint _reflectionPaint;
        private SKPaint? _glowPaint;
        private SKColor[]? _colorCache;
        #endregion

        #region Constructor and Initialization
        private RainbowRenderer()
        {
            _barPaint = new SKPaint
            {
                IsAntialias = true,
                FilterQuality = SKFilterQuality.High,
                Style = SKPaintStyle.Fill
            };

            _highlightPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = SKColors.White
            };

            _reflectionPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                BlendMode = SKBlendMode.SrcOver
            };
        }

        public static RainbowRenderer GetInstance() => _instance ??= new RainbowRenderer();

        public void Initialize()
        {
            if (!_isInitialized)
            {
                _isInitialized = true;
                _glowPaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Fill,
                    ImageFilter = SKImageFilter.CreateBlur(GlowRadius, GlowRadius)
                };

                _colorCache = new SKColor[256];
                for (int i = 0; i < 256; i++)
                {
                    float normalizedValue = i / 255f;
                    _colorCache[i] = GetRainbowColor(normalizedValue);
                }

                Log.Debug("RainbowRenderer initialized");
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
            int halfSpectrumLength = spectrum!.Length / 2;
            int actualBarCount = Math.Min(halfSpectrumLength, barCount);

            try
            {
                semaphoreAcquired = _spectrumSemaphore.Wait(0);

                if (semaphoreAcquired)
                {
                    float[] scaledSpectrum = ScaleSpectrum(spectrum, actualBarCount, halfSpectrumLength);
                    _processedSpectrum = SmoothSpectrum(scaledSpectrum, actualBarCount);
                }

                lock (_spectrumLock)
                {
                    renderSpectrum = _processedSpectrum ??
                                     ProcessSynchronously(spectrum!, actualBarCount, halfSpectrumLength);
                }
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

            using var _ = new SKAutoCanvasRestore(canvas!, true);
            RenderBars(canvas!, renderSpectrum, info, barWidth, barSpacing, basePaint!);
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
                Log.Error("RainbowRenderer not initialized before rendering");
                return false;
            }

            if (canvas == null ||
                spectrum == null || spectrum.Length < 2 ||
                basePaint == null ||
                info.Width <= 0 || info.Height <= 0)
            {
                Log.Error("Invalid render parameters for RainbowRenderer");
                return false;
            }

            return true;
        }

        private float[] ProcessSynchronously(float[] spectrum, int targetCount, int halfSpectrumLength)
        {
            var scaledSpectrum = ScaleSpectrum(spectrum, targetCount, halfSpectrumLength);
            return SmoothSpectrum(scaledSpectrum, targetCount);
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
            float reflectionHeight = canvasHeight * ReflectionHeight;

            for (int i = 0; i < spectrum.Length; i++)
            {
                float magnitude = Math.Clamp(spectrum[i], 0f, 1f);
                if (magnitude < MinMagnitudeThreshold)
                    continue;

                float barHeight = magnitude * canvasHeight;
                float x = startX + i * totalBarWidth;
                float y = canvasHeight - barHeight;

                SKColor barColor = GetBarColor(magnitude);
                byte baseAlpha = (byte)Math.Clamp(magnitude * 255f, 0, 255);

                _barPaint.Color = barColor.WithAlpha(baseAlpha);
                _barPaint.Shader = null;

                _path.Reset();
                _path.AddRoundRect(new SKRoundRect(
                    new SKRect(x, y, x + barWidth, canvasHeight),
                    CornerRadius, CornerRadius));

                if (_glowPaint != null && magnitude > 0.3f && magnitude <= 0.95f)
                {
                    float adjustedGlowRadius = GlowRadius * (1 + loudness * 0.3f);
                    if (Math.Abs(adjustedGlowRadius - GlowRadius) > 0.1f)
                    {
                        _glowPaint.ImageFilter = SKImageFilter.CreateBlur(adjustedGlowRadius, adjustedGlowRadius);
                    }

                    byte glowAlpha = (byte)Math.Clamp(magnitude * 255f * GlowIntensity, 0, 255);
                    _glowPaint.Color = barColor.WithAlpha(glowAlpha);
                    canvas.DrawPath(_path, _glowPaint);
                }

                try
                {
                    using var shader = SKShader.CreateLinearGradient(
                        new SKPoint(x, y),
                        new SKPoint(x + barWidth, y),
                        new[] { barColor, barColor.WithAlpha((byte)Math.Clamp(255 * 0.7f, 0, 255)) },
                        new[] { 0.0f, 1.0f },
                        SKShaderTileMode.Clamp);

                    _barPaint.Shader = shader;
                    byte barAlpha = (byte)Math.Clamp(magnitude * AlphaMultiplier * 255f, 0, 255);
                    _barPaint.Color = barColor.WithAlpha(barAlpha);
                    canvas.DrawPath(_path, _barPaint);
                }
                catch
                {
                    _barPaint.Shader = null;
                    canvas.DrawPath(_path, _barPaint);
                }

                _barPaint.Shader = null;

                if (barHeight > CornerRadius * 2)
                {
                    float highlightWidth = barWidth * HighlightWidthProportion;
                    float highlightHeight = Math.Min(barHeight * HighlightHeightProportion, CornerRadius);
                    byte highlightAlpha = (byte)Math.Clamp(magnitude * 255f * HighlightAlpha, 0, 255);
                    _highlightPaint.Color = SKColors.White.WithAlpha(highlightAlpha);

                    float highlightX = x + (barWidth - highlightWidth) / 2;
                    canvas.DrawRect(
                        highlightX,
                        y,
                        highlightWidth,
                        highlightHeight,
                        _highlightPaint);

                    if (magnitude > 0.2f)
                    {
                        byte reflectionAlpha = (byte)Math.Clamp(magnitude * 255 * ReflectionOpacity, 0, 255);
                        _reflectionPaint.Color = barColor.WithAlpha(reflectionAlpha);
                        float reflectY = canvasHeight;
                        float reflectHeight = Math.Min(barHeight * 0.4f, reflectionHeight);

                        canvas.DrawRect(
                            x,
                            reflectY,
                            barWidth,
                            reflectHeight,
                            _reflectionPaint);
                    }
                }
            }
        }

        private SKColor GetBarColor(float magnitude)
        {
            if (_colorCache != null)
            {
                int colorIndex = Math.Clamp((int)(magnitude * 255), 0, 255);
                return _colorCache[colorIndex];
            }

            return GetRainbowColor(magnitude);
        }
        #endregion

        #region Spectrum Processing
        private static float[] ScaleSpectrum(float[] spectrum, int targetCount, int halfSpectrumLength)
        {
            float[] scaledSpectrum = new float[targetCount];
            float blockSize = (float)halfSpectrumLength / targetCount;

            for (int i = 0; i < targetCount; i++)
            {
                int start = (int)(i * blockSize);
                int end = (int)((i + 1) * blockSize);
                end = Math.Min(end, halfSpectrumLength);

                if (end <= start)
                {
                    end = start + 1;
                }

                ReadOnlySpan<float> block = spectrum.AsSpan(start, end - start);
                float sum = 0;

                for (int j = 0; j < block.Length; j++)
                {
                    sum += block[j];
                }

                scaledSpectrum[i] = sum / block.Length;
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
            float adaptiveFactor = _smoothingFactor * (1f + MathF.Pow(CalculateLoudness(spectrum), 2) * 0.5f);

            for (int i = 0; i < targetCount; i++)
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
                float weight = i < subBass ? 1.7f : i < bass ? 1.4f : i < mid ? 1.1f : 0.6f;
                sum += MathF.Abs(spectrum[i]) * weight;
            }

            return Math.Clamp(sum / length * 4.0f, 0f, 1f);
        }

        private static SKColor GetRainbowColor(float normalizedValue)
        {
            normalizedValue = Math.Clamp(normalizedValue, 0f, 1f);
            float hue = 240 - 240 * normalizedValue;
            if (hue < 0) hue += 360;
            float saturation = 100f;
            float brightness = 90f + normalizedValue * 10f;
            return SKColor.FromHsv(hue, saturation, brightness);
        }
        #endregion

        #region Disposal
        private void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _spectrumSemaphore?.Dispose();
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
                Log.Debug("RainbowRenderer disposed");
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