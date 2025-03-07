#nullable enable

namespace SpectrumNet
{
    public sealed class GlitchRenderer : ISpectrumRenderer, IDisposable
    {
        #region Fields
        private static GlitchRenderer? _instance;
        private bool _isInitialized;
        private bool _isOverlayActive;
        private volatile bool _disposed;
        private readonly SemaphoreSlim _spectrumSemaphore = new(1, 1);

        private float[]? _processedSpectrum;
        private SKBitmap? _bufferBitmap;
        private SKPaint? _bitmapPaint;
        private List<GlitchSegment>? _glitchSegments;
        private Random _random = new();
        private float _timeAccumulator;
        private int _scanlinePosition;

        private const string LogPrefix = "[GlitchRenderer]";

        // Настройки качества рендеринга
        private RenderQuality _quality = RenderQuality.Medium;
        private bool _useAntiAlias = true;
        private SKFilterQuality _filterQuality = SKFilterQuality.Medium;
        private bool _useAdvancedEffects = true;

        // Переиспользуемые SKPaint для минимизации выделений памяти
        private SKPaint? _redPaint;
        private SKPaint? _bluePaint;
        private SKPaint? _scanlinePaint;
        private SKPaint? _noisePaint;
        #endregion

        #region Constants
        private static class Constants
        {
            // Glitch Effect Parameters
            public const float GlitchThreshold = 0.5f;  // Threshold for glitch activation based on spectrum activity
            public const float MaxGlitchOffset = 30f;   // Maximum horizontal offset for glitch segments
            public const int MaxGlitchSegments = 10;    // Maximum number of concurrent glitch segments

            // Scanline Parameters
            public const float ScanlineSpeed = 1.5f;  // Speed of scanline movement across the screen
            public const byte ScanlineAlpha = 40;    // Alpha value for scanline transparency
            public const float ScanlineHeight = 3f;    // Height of the scanline

            // RGB Split Parameters
            public const float RgbSplitMax = 10f;   // Maximum offset for RGB split effect

            // Timing and Animation
            public const float TimeStep = 0.016f;// Time step for animation updates (approximately 60 FPS)
            public const float MinGlitchDuration = 0.05f; // Minimum duration of a glitch segment
            public const float MaxGlitchDuration = 0.3f;  // Maximum duration of a glitch segment

            // Spectrum Processing
            public const int BandCount = 3;     // Number of frequency bands (low, mid, high)
            public const int ProcessedSpectrumSize = 128;// Size of the processed spectrum array
            public const float SpectrumScale = 2.0f;  // Scaling factor for spectrum visualization
            public const float SpectrumClamp = 1.0f;  // Maximum value for spectrum data
            public const float Sensitivity = 3.0f;  // Sensitivity factor for band activity detection
            public const float SmoothingFactor = 0.2f;  // Smoothing factor for band activity over time
        }
        #endregion

        #region Constructor and Initialization
        private GlitchRenderer() { }

        public static GlitchRenderer GetInstance() => _instance ??= new GlitchRenderer();

        public void Initialize()
        {
            if (_isInitialized) return;

            _bitmapPaint = new SKPaint
            {
                IsAntialias = _useAntiAlias,
                FilterQuality = _filterQuality
            };

            _redPaint = new SKPaint
            {
                Color = SKColors.Red.WithAlpha(100),
                BlendMode = SKBlendMode.SrcOver,
                IsAntialias = _useAntiAlias,
                FilterQuality = _filterQuality
            };

            _bluePaint = new SKPaint
            {
                Color = SKColors.Blue.WithAlpha(100),
                BlendMode = SKBlendMode.SrcOver,
                IsAntialias = _useAntiAlias,
                FilterQuality = _filterQuality
            };

            _scanlinePaint = new SKPaint
            {
                Color = SKColors.White.WithAlpha(Constants.ScanlineAlpha),
                Style = SKPaintStyle.Fill
            };

            _noisePaint = new SKPaint
            {
                Style = SKPaintStyle.Fill
            };

            _glitchSegments = new List<GlitchSegment>();

            _isInitialized = true;
            SmartLogger.Log(LogLevel.Debug, LogPrefix, "GlitchRenderer initialized");
        }

        public void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium)
        {
            _isOverlayActive = isOverlayActive;
            Quality = quality;
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
            if (!ValidateRenderParams(canvas, spectrum, info, paint, drawPerformanceInfo))
                return;

            bool semaphoreAcquired = false;
            try
            {
                semaphoreAcquired = _spectrumSemaphore.Wait(0);

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
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error in GlitchRenderer.Render: {ex.Message}");
            }
            finally
            {
                if (semaphoreAcquired)
                    _spectrumSemaphore.Release();
            }
        }

        private void RenderGlitchEffect(SKCanvas canvas, SKImageInfo info, SKPaint basePaint)
        {
            if (_bufferBitmap == null || _bitmapPaint == null || _glitchSegments == null || _processedSpectrum == null)
                return;

            using (var bufferCanvas = new SKCanvas(_bufferBitmap))
            {
                bufferCanvas.Clear(SKColors.Black);
                DrawBaseSpectrum(bufferCanvas, info, basePaint);

                if (_useAdvancedEffects && _processedSpectrum[2] > Constants.GlitchThreshold * 0.5f)
                {
                    float rgbSplit = _processedSpectrum[2] * Constants.RgbSplitMax;
                    bufferCanvas.Save();
                    bufferCanvas.Translate(-rgbSplit, 0);
                    DrawBaseSpectrum(bufferCanvas, info, _redPaint!);
                    bufferCanvas.Restore();

                    bufferCanvas.Save();
                    bufferCanvas.Translate(rgbSplit, 0);
                    DrawBaseSpectrum(bufferCanvas, info, _bluePaint!);
                    bufferCanvas.Restore();
                }

                bufferCanvas.DrawRect(
                    0,
                    _scanlinePosition,
                    info.Width,
                    Constants.ScanlineHeight,
                    _scanlinePaint!);

                if (_useAdvancedEffects && _processedSpectrum[1] > Constants.GlitchThreshold * 0.3f)
                {
                    DrawDigitalNoise(bufferCanvas, info, _processedSpectrum[1]);
                }
            }

            var activeSegments = _glitchSegments.Where(s => s.IsActive).OrderBy(s => s.Y).ToList();
            int currentY = 0;

            foreach (var segment in activeSegments)
            {
                if (currentY < segment.Y)
                {
                    SKRect sourceRect = new SKRect(0, currentY, info.Width, segment.Y);
                    SKRect destRect = new SKRect(0, currentY, info.Width, segment.Y);
                    canvas.DrawBitmap(_bufferBitmap, sourceRect, destRect);
                }
                currentY = segment.Y + segment.Height;
            }

            if (currentY < info.Height)
            {
                SKRect sourceRect = new SKRect(0, currentY, info.Width, info.Height);
                SKRect destRect = new SKRect(0, currentY, info.Width, info.Height);
                canvas.DrawBitmap(_bufferBitmap, sourceRect, destRect);
            }

            foreach (var segment in activeSegments)
            {
                SKRect sourceRect = new SKRect(0, segment.Y, info.Width, segment.Y + segment.Height);
                SKRect destRect = new SKRect(segment.XOffset, segment.Y, segment.XOffset + info.Width, segment.Y + segment.Height);
                canvas.DrawBitmap(_bufferBitmap, sourceRect, destRect);
            }
        }

        private void DrawBaseSpectrum(SKCanvas canvas, SKImageInfo info, SKPaint paint)
        {
            if (_processedSpectrum == null) return;

            float barCount = Math.Min(Constants.ProcessedSpectrumSize, _processedSpectrum.Length);
            float barWidth = info.Width / barCount;

            for (int i = 0; i < barCount; i++)
            {
                float amplitude = _processedSpectrum[i];
                float x = i * barWidth;
                float height = amplitude * info.Height * 0.5f;

                canvas.DrawLine(
                    x,
                    info.Height / 2 - height / 2,
                    x,
                    info.Height / 2 + height / 2,
                    paint);
            }
        }

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
        #endregion

        #region Glitch Effects
        private void CreateBufferIfNeeded(SKImageInfo info)
        {
            if (_bufferBitmap == null ||
                _bufferBitmap.Width != info.Width ||
                _bufferBitmap.Height != info.Height)
            {
                _bufferBitmap?.Dispose();
                _bufferBitmap = new SKBitmap(info.Width, info.Height);
            }
        }

        private void UpdateGlitchEffects(SKImageInfo info)
        {
            if (_glitchSegments == null || _processedSpectrum == null) return;

            _timeAccumulator += Constants.TimeStep;
            _scanlinePosition = (int)(_scanlinePosition + Constants.ScanlineSpeed);
            if (_scanlinePosition >= info.Height)
                _scanlinePosition = 0;

            for (int i = _glitchSegments.Count - 1; i >= 0; i--)
            {
                var segment = _glitchSegments[i];
                segment.Duration -= Constants.TimeStep;

                if (segment.Duration <= 0)
                {
                    segment.IsActive = false;
                    _glitchSegments.RemoveAt(i);
                }
                else
                {
                    if (_random.NextDouble() < 0.2)
                    {
                        segment.XOffset = (float)(Constants.MaxGlitchOffset * (_random.NextDouble() * 2 - 1) *
                                                _processedSpectrum[0]);
                    }
                    _glitchSegments[i] = segment;
                }
            }

            if (_processedSpectrum[0] > Constants.GlitchThreshold && _glitchSegments.Count < Constants.MaxGlitchSegments)
            {
                if (_random.NextDouble() < _processedSpectrum[0] * 0.4)
                {
                    int segmentHeight = (int)(20 + _random.NextDouble() * 50);
                    int y = _random.Next(0, info.Height - segmentHeight);
                    float duration = Constants.MinGlitchDuration +
                                   (float)_random.NextDouble() * (Constants.MaxGlitchDuration - Constants.MinGlitchDuration);
                    float xOffset = (float)(Constants.MaxGlitchOffset * (_random.NextDouble() * 2 - 1) * _processedSpectrum[0]);

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
        }
        #endregion

        #region Spectrum Processing
        private void ProcessSpectrum(float[] spectrum)
        {
            if (_processedSpectrum == null || _processedSpectrum.Length < Constants.ProcessedSpectrumSize)
            {
                _processedSpectrum = new float[Constants.ProcessedSpectrumSize];
            }

            for (int i = 0; i < Constants.ProcessedSpectrumSize; i++)
            {
                float index = (float)i * spectrum.Length / (2f * Constants.ProcessedSpectrumSize);
                int baseIndex = (int)index;
                float lerp = index - baseIndex;

                if (baseIndex < spectrum.Length / 2 - 1)
                {
                    _processedSpectrum[i] = spectrum[baseIndex] * (1 - lerp) +
                                          spectrum[baseIndex + 1] * lerp;
                }
                else
                {
                    _processedSpectrum[i] = spectrum[spectrum.Length / 2 - 1];
                }

                _processedSpectrum[i] *= Constants.SpectrumScale;
                _processedSpectrum[i] = Math.Min(_processedSpectrum[i], Constants.SpectrumClamp);
            }

            float[] bandActivity = new float[Constants.BandCount];
            int bandSize = spectrum.Length / (2 * Constants.BandCount);

            for (int band = 0; band < Constants.BandCount; band++)
            {
                float sum = 0;
                int start = band * bandSize;
                int end = Math.Min((band + 1) * bandSize, spectrum.Length / 2);

                for (int i = start; i < end; i++)
                {
                    sum += spectrum[i];
                }

                float avg = sum / (end - start);

                if (band < _processedSpectrum.Length)
                {
                    if (_processedSpectrum[band] == 0)
                    {
                        bandActivity[band] = avg * Constants.Sensitivity;
                    }
                    else
                    {
                        bandActivity[band] = _processedSpectrum[band] +
                                           (avg * Constants.Sensitivity - _processedSpectrum[band]) * Constants.SmoothingFactor;
                    }
                }
            }

            for (int i = 0; i < Constants.BandCount; i++)
            {
                _processedSpectrum[i] = Math.Min(bandActivity[i], Constants.SpectrumClamp);
            }
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

            if (_bitmapPaint != null)
            {
                _bitmapPaint.IsAntialias = _useAntiAlias;
                _bitmapPaint.FilterQuality = _filterQuality;
            }
            if (_redPaint != null)
            {
                _redPaint.IsAntialias = _useAntiAlias;
                _redPaint.FilterQuality = _filterQuality;
            }
            if (_bluePaint != null)
            {
                _bluePaint.IsAntialias = _useAntiAlias;
                _bluePaint.FilterQuality = _filterQuality;
            }
        }
        #endregion

        #region Structures
        private struct GlitchSegment
        {
            public int Y;
            public int Height;
            public float XOffset;
            public float Duration;
            public bool IsActive;
        }
        #endregion

        #region Helper Methods
        private bool ValidateRenderParams(
            SKCanvas? canvas,
            float[]? spectrum,
            SKImageInfo info,
            SKPaint? paint,
            Action<SKCanvas, SKImageInfo>? drawPerformanceInfo)
        {
            if (_disposed)
                return false;

            if (!_isInitialized)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "GlitchRenderer not initialized before rendering");
                return false;
            }

            if (canvas == null ||
                spectrum == null || spectrum.Length < 2 ||
                paint == null ||
                drawPerformanceInfo == null ||
                info.Width <= 0 || info.Height <= 0)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Invalid render parameters for GlitchRenderer");
                return false;
            }

            return true;
        }
        #endregion

        #region Disposal
        public void Dispose()
        {
            if (_disposed) return;

            _spectrumSemaphore.Dispose();
            _bufferBitmap?.Dispose();
            _bitmapPaint?.Dispose();
            _redPaint?.Dispose();
            _bluePaint?.Dispose();
            _scanlinePaint?.Dispose();
            _noisePaint?.Dispose();

            _bufferBitmap = null;
            _bitmapPaint = null;
            _redPaint = null;
            _bluePaint = null;
            _scanlinePaint = null;
            _noisePaint = null;
            _glitchSegments = null;
            _processedSpectrum = null;

            _disposed = true;
            _isInitialized = false;
            SmartLogger.Log(LogLevel.Debug, LogPrefix, "GlitchRenderer disposed");
        }
        #endregion
    }
}