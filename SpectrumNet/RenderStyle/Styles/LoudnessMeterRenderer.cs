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

        private const float MinLoudnessThreshold = 0.001f;
        private const float SmoothingFactorNormal = 0.3f;
        private const float SmoothingFactorOverlay = 0.5f;
        private const float PeakDecayRate = 0.05f;
        private const float GlowIntensity = 0.4f;
        private const float HighLoudnessThreshold = 0.7f;
        private const float MediumLoudnessThreshold = 0.4f;
        private const float BorderWidth = 1.5f;

        private float _smoothingFactor = SmoothingFactorNormal;
        private float _previousLoudness = 0f;
        private float _peakLoudness = 0f;
        private float? _cachedLoudness;
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
            Log.Debug("LoudnessMeterRenderer initialized");
        }

        public void Configure(bool isOverlayActive)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(LoudnessMeterRenderer));
            }

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
                            _peakLoudness = Math.Max(0, _peakLoudness - PeakDecayRate);
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

                RenderEnhancedMeter(canvas!, info, loudness, _peakLoudness, paint!);
                drawPerformanceInfo(canvas!, info);
            }
            catch (ObjectDisposedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Error($"Error rendering loudness meter: {ex.Message}");
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
                Log.Error("LoudnessMeterRenderer is not initialized");
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

            if (info.Width <= 0 || info.Height <= 0)
            {
                Log.Error("Invalid canvas dimensions");
                return false;
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float CalculateAndSmoothLoudness(float[] spectrum)
        {
            float rawLoudness = CalculateLoudness(spectrum.AsSpan());

            float smoothedLoudness = _previousLoudness + (rawLoudness - _previousLoudness) * _smoothingFactor;
            smoothedLoudness = Math.Clamp(smoothedLoudness, MinLoudnessThreshold, 1f);
            _previousLoudness = smoothedLoudness;

            return smoothedLoudness;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float CalculateLoudness(ReadOnlySpan<float> spectrum)
        {
            if (spectrum.IsEmpty)
                return 0f;

            float sum = 0f;
            for (int i = 0; i < spectrum.Length; i++)
                sum += Math.Abs(spectrum[i]);

            return Math.Clamp(sum / spectrum.Length, 0f, 1f);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RenderEnhancedMeter(
            SKCanvas canvas,
            SKImageInfo info,
            float loudness,
            float peakLoudness,
            SKPaint basePaint)
        {
            if (loudness < MinLoudnessThreshold)
                return;

            float meterHeight = info.Height * loudness;
            float peakHeight = info.Height * peakLoudness;

            canvas.Save();

            using (var backgroundPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = BorderWidth,
                Color = SKColors.White.WithAlpha(100),
                IsAntialias = true
            })
            {
                canvas.DrawRect(0, 0, info.Width, info.Height, backgroundPaint);
            }

            using (var fillPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            })
            {
                SKColor[] colors = {
                    SKColors.Green.WithAlpha((byte)(255 * 0.8f)),
                    SKColors.Yellow.WithAlpha((byte)(255 * 0.8f)),
                    SKColors.Red.WithAlpha((byte)(255 * 0.8f))
                };

                float[] positions = { 0, 0.5f, 1.0f };

                fillPaint.Shader = SKShader.CreateLinearGradient(
                    new SKPoint(0, info.Height),
                    new SKPoint(0, 0),
                    colors,
                    positions,
                    SKShaderTileMode.Clamp);

                canvas.DrawRect(0, info.Height - meterHeight, info.Width, meterHeight, fillPaint);
            }

            if (loudness > HighLoudnessThreshold)
            {
                using (var glowPaint = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    IsAntialias = true,
                    MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 10),
                    Color = SKColors.Red.WithAlpha((byte)(255 * GlowIntensity * (loudness - HighLoudnessThreshold) / (1 - HighLoudnessThreshold)))
                })
                {
                    canvas.DrawRect(0, info.Height - meterHeight, info.Width, meterHeight / 3, glowPaint);
                }
            }

            using (var markerPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1,
                Color = SKColors.White.WithAlpha(150),
                IsAntialias = true
            })
            {
                for (int i = 1; i < 10; i++)
                {
                    float y = info.Height - (info.Height * i / 10f);
                    canvas.DrawLine(0, y, info.Width, y, markerPaint);
                }
            }

            using (var peakPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = loudness > HighLoudnessThreshold ? SKColors.Red :
                        loudness > MediumLoudnessThreshold ? SKColors.Yellow : SKColors.Green,
                IsAntialias = true
            })
            {
                float peakLineY = info.Height - peakHeight;
                canvas.DrawRect(0, peakLineY - 2, info.Width, 4, peakPaint);
            }

            canvas.Restore();
        }
        #endregion

        #region Disposal
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                _renderSemaphore?.Dispose();
            }

            _disposed = true;
            Log.Debug("LoudnessMeterRenderer disposed");
        }

        ~LoudnessMeterRenderer()
        {
            Dispose(disposing: false);
        }
        #endregion
    }
}