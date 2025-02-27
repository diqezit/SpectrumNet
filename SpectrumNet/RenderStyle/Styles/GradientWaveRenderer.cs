#nullable enable

namespace SpectrumNet
{
    public class GradientWaveRenderer : ISpectrumRenderer, IDisposable
    {
        #region Fields
        private static readonly Lazy<GradientWaveRenderer> _instance =
            new Lazy<GradientWaveRenderer>(() => new GradientWaveRenderer());
        private bool _isInitialized;
        private bool _disposed = false;
        private bool _isOverlayActive;
        private float[]? _previousSpectrum;
        private float[]? _processedSpectrum;
        private List<SKPoint>? _cachedPoints;
        private readonly SemaphoreSlim _spectrumSemaphore = new(1, 1);
        private readonly object _spectrumLock = new();
        private readonly SKPath _wavePath = new();
        private readonly SKPath _fillPath = new();

        private const float Offset = 10f;
        private const float SmoothingFactorNormal = 0.3f;
        private const float SmoothingFactorOverlay = 0.5f;
        private const float MinMagnitudeThreshold = 0.01f;
        private const float MaxSpectrumValue = 1.5f;
        private const float DefaultLineWidth = 3f;
        private const float GlowIntensity = 0.3f;
        private const float HighMagnitudeThreshold = 0.7f;
        private const float FillOpacity = 0.2f;
        private const float LineGradientSaturation = 100f;
        private const float LineGradientLightness = 50f;
        private const float OverlayGradientSaturation = 100f;
        private const float OverlayGradientLightness = 55f;
        private const float MaxBlurRadius = 6f;
        private const float BaselineOffset = 2f;
        private const int ExtraPointsCount = 4;

        private float _smoothingFactor = SmoothingFactorNormal;
        #endregion

        #region Constructor and Initialization
        private GradientWaveRenderer() { }

        public static GradientWaveRenderer GetInstance() => _instance.Value;

        public void Initialize()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(GradientWaveRenderer));
            }

            if (_isInitialized) return;

            Log.Debug("GradientWaveRenderer initialized");
            _isInitialized = true;
        }
        #endregion

        #region Configuration
        public void Configure(bool isOverlayActive)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(GradientWaveRenderer));
            }

            _isOverlayActive = isOverlayActive;
            _smoothingFactor = isOverlayActive ? SmoothingFactorOverlay : SmoothingFactorNormal;
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
            try
            {
                if (!ValidateRenderParameters(canvas, spectrum, paint))
                    return;

                float[] renderSpectrum;
                List<SKPoint> renderPoints;
                bool semaphoreAcquired = false;
                int actualBarCount = Math.Min(spectrum!.Length / 2, barCount);

                try
                {
                    semaphoreAcquired = _spectrumSemaphore.Wait(0);

                    if (semaphoreAcquired)
                    {
                        float[] scaledSpectrum = ScaleSpectrum(spectrum, actualBarCount);
                        _processedSpectrum = SmoothSpectrum(scaledSpectrum, actualBarCount);
                        _cachedPoints = GetSpectrumPoints(_processedSpectrum, info);
                    }

                    lock (_spectrumLock)
                    {
                        if (_processedSpectrum != null && _cachedPoints != null)
                        {
                            renderSpectrum = _processedSpectrum;
                            renderPoints = _cachedPoints;
                        }
                        else
                        {
                            float[] scaledSpectrum = ScaleSpectrum(spectrum, actualBarCount);
                            renderSpectrum = SmoothSpectrum(scaledSpectrum, actualBarCount);
                            renderPoints = GetSpectrumPoints(renderSpectrum, info);
                        }
                    }
                }
                finally
                {
                    if (semaphoreAcquired)
                    {
                        _spectrumSemaphore.Release();
                    }
                }

                RenderGradientWave(canvas!, renderPoints, renderSpectrum, info, paint!, barCount);

                drawPerformanceInfo?.Invoke(canvas!, info);
            }
            catch (ObjectDisposedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Error($"Error rendering gradient wave: {ex.Message}");
            }
        }

        private bool ValidateRenderParameters(SKCanvas? canvas, float[]? spectrum, SKPaint? paint)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(GradientWaveRenderer));
            }

            if (!_isInitialized)
            {
                Log.Error("GradientWaveRenderer is not initialized");
                return false;
            }

            if (canvas == null)
            {
                Log.Error("Canvas is null");
                return false;
            }

            if (spectrum == null || spectrum.Length == 0)
            {
                Log.Error("Spectrum is null or empty");
                return false;
            }

            if (paint == null)
            {
                Log.Error("Paint is null");
                return false;
            }

            return true;
        }

        private void RenderGradientWave(
            SKCanvas canvas,
            List<SKPoint> points,
            float[] spectrum,
            SKImageInfo info,
            SKPaint basePaint,
            int barCount)
        {
            if (points.Count < 2)
                return;

            float maxMagnitude = 0f;
            for (int i = 0; i < spectrum.Length; i++)
            {
                if (spectrum[i] > maxMagnitude)
                    maxMagnitude = spectrum[i];
            }

            float blurRadius = Math.Min(MaxBlurRadius, 10f / (float)Math.Sqrt(barCount));
            float saturation = _isOverlayActive ? OverlayGradientSaturation : LineGradientSaturation;
            float lightness = _isOverlayActive ? OverlayGradientLightness : LineGradientLightness;
            float yBaseline = info.Height - Offset + BaselineOffset;

            _wavePath.Reset();
            _fillPath.Reset();
            _wavePath.MoveTo(points[0]);

            for (int i = 1; i < points.Count - 2; i += 1)
            {
                float x1 = points[i].X;
                float y1 = points[i].Y;
                float x2 = points[i + 1].X;
                float y2 = points[i + 1].Y;
                float xMid = (x1 + x2) / 2;
                float yMid = (y1 + y2) / 2;

                _wavePath.QuadTo(x1, y1, xMid, yMid);
            }

            if (points.Count >= 2)
            {
                _wavePath.LineTo(points[points.Count - 1]);
            }

            _fillPath.AddPath(_wavePath);
            _fillPath.LineTo(info.Width, yBaseline);
            _fillPath.LineTo(0, yBaseline);
            _fillPath.Close();

            canvas.Save();

            using (var fillPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            })
            {
                SKColor[] colors = new SKColor[3]; 
                colors[0] = basePaint.Color.WithAlpha((byte)(255 * FillOpacity * maxMagnitude));
                colors[1] = basePaint.Color.WithAlpha((byte)(255 * FillOpacity * maxMagnitude * 0.5f));
                colors[2] = SKColors.Transparent;

                float[] colorPositions = { 0.0f, 0.7f, 1.0f };

                fillPaint.Shader = SKShader.CreateLinearGradient(
                    new SKPoint(0, Offset),
                    new SKPoint(0, yBaseline),
                    colors,
                    colorPositions,
                    SKShaderTileMode.Clamp);

                canvas.SaveLayer(null);
                canvas.DrawPath(_fillPath, fillPaint);
                canvas.Restore();
            }

            if (maxMagnitude > HighMagnitudeThreshold)
            {
                using (var glowPaint = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = DefaultLineWidth * 2.0f,
                    IsAntialias = true,
                    MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, blurRadius),
                    Color = basePaint.Color.WithAlpha((byte)(255 * GlowIntensity * maxMagnitude))
                })
                {
                    canvas.SaveLayer(null);
                    canvas.DrawPath(_wavePath, glowPaint);
                    canvas.Restore();
                }
            }

            SKColor[] lineColors = new SKColor[points.Count];
            float[] positions = new float[points.Count];

            for (int i = 0; i < points.Count; i++)
            {
                float normalizedValue = points[i].X / info.Width;
                float segmentMagnitude = 1.0f - (points[i].Y - Offset) / (info.Height - 2 * Offset);

                lineColors[i] = SKColor.FromHsl(
                    normalizedValue * 360,
                    saturation,
                    lightness,
                    (byte)(255 * Math.Min(0.6f + segmentMagnitude * 0.4f, 1.0f)));

                positions[i] = normalizedValue;
            }

            using (var gradientPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = DefaultLineWidth,
                IsAntialias = true,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round
            })
            {
                gradientPaint.Shader = SKShader.CreateLinearGradient(
                    new SKPoint(0, 0),
                    new SKPoint(info.Width, 0),
                    lineColors,
                    positions,
                    SKShaderTileMode.Clamp);

                canvas.DrawPath(_wavePath, gradientPaint);
            }

            canvas.Restore();
        }
        #endregion

        #region Spectrum Processing
        private static float[] ScaleSpectrum(float[] spectrum, int targetCount)
        {
            int spectrumLength = spectrum.Length / 2;
            float[] scaledSpectrum = new float[targetCount];
            float blockSize = (float)spectrumLength / targetCount;

            for (int i = 0; i < targetCount; i++)
            {
                float sum = 0;
                int start = (int)(i * blockSize);
                int end = (int)((i + 1) * blockSize);

                for (int j = start; j < end && j < spectrumLength; j++)
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
                _previousSpectrum = new float[targetCount];

            var smoothedSpectrum = new float[targetCount];

            for (int i = 0; i < targetCount; i++)
            {
                float currentValue = spectrum[i];
                float smoothedValue = _previousSpectrum[i] + (currentValue - _previousSpectrum[i]) * _smoothingFactor;
                smoothedSpectrum[i] = Math.Clamp(smoothedValue, MinMagnitudeThreshold, MaxSpectrumValue);
                _previousSpectrum[i] = smoothedSpectrum[i];
            }

            float[] extraSmoothedSpectrum = new float[targetCount];
            for (int i = 0; i < targetCount; i++)
            {
                float sum = smoothedSpectrum[i];
                int count = 1;

                if (i > 0) { sum += smoothedSpectrum[i - 1]; count++; }
                if (i < targetCount - 1) { sum += smoothedSpectrum[i + 1]; count++; }

                extraSmoothedSpectrum[i] = sum / count;
            }

            return extraSmoothedSpectrum;
        }

        private List<SKPoint> GetSpectrumPoints(float[] spectrum, SKImageInfo info)
        {
            List<SKPoint> points = new List<SKPoint>();
            float min_y = Offset;
            float max_y = info.Height - Offset;
            int spectrumLength = spectrum.Length;

            if (spectrumLength < 1)
            {
                return points;
            }

            points.Add(new SKPoint(-Offset, max_y));
            points.Add(new SKPoint(0, max_y));

            for (int i = 0; i < spectrumLength; i++)
            {
                float x = (i / (spectrumLength - 1f)) * info.Width;
                float y = max_y - (spectrum[i] * (max_y - min_y));
                points.Add(new SKPoint(x, y));
            }

            points.Add(new SKPoint(info.Width, max_y));
            points.Add(new SKPoint(info.Width + Offset, max_y));

            return points;
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
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                _spectrumSemaphore?.Dispose();
                _wavePath?.Dispose();
                _fillPath?.Dispose();
                _previousSpectrum = null;
                _processedSpectrum = null;
                _cachedPoints = null;
            }

            _disposed = true;
            Log.Debug("GradientWaveRenderer disposed");
        }

        ~GradientWaveRenderer()
        {
            Dispose(false);
        }
        #endregion
    }
}