#nullable enable

namespace SpectrumNet
{
    public sealed class GlitchRenderer : BaseSpectrumRenderer
    {
        #region Constants
        private static class Constants
        {
            public const string LOG_PREFIX = "GlitchRenderer";
            public const float GLITCH_THRESHOLD = 0.5f;
            public const float MAX_GLITCH_OFFSET = 30f;
            public const int MAX_GLITCH_SEGMENTS = 10;
            public const float SCANLINE_SPEED = 1.5f;
            public const byte SCANLINE_ALPHA = 40;
            public const float SCANLINE_HEIGHT = 3f;
            public const float RGB_SPLIT_MAX = 10f;
            public const float TIME_STEP = 0.016f;
            public const float MIN_GLITCH_DURATION = 0.05f;
            public const float MAX_GLITCH_DURATION = 0.3f;
            public const int BAND_COUNT = 3;
            public const int PROCESSED_SPECTRUM_SIZE = 128;
            public const float SPECTRUM_SCALE = 2.0f;
            public const float SPECTRUM_CLAMP = 1.0f;
            public const float SENSITIVITY = 3.0f;
            public const float SMOOTHING_FACTOR = 0.2f;
        }
        #endregion

        #region Fields
        private static readonly Lazy<GlitchRenderer> _instance = new(() => new GlitchRenderer());
        private readonly SemaphoreSlim _glitchSemaphore = new(1, 1);
        private float[]? _glitchProcessedSpectrum;
        private SKBitmap? _bufferBitmap;
        private SKPaint? _bitmapPaint;
        private List<GlitchSegment>? _glitchSegments;
        private Random _random = new();
        private float _timeAccumulator;
        private int _scanlinePosition;
        private SKPaint? _redPaint;
        private SKPaint? _bluePaint;
        private SKPaint? _scanlinePaint;
        private SKPaint? _noisePaint;
        #endregion

        #region Singleton Constructor
        private GlitchRenderer() { }

        public static GlitchRenderer GetInstance() => _instance.Value;
        #endregion

        #region Initialization
        public override void Initialize() => SmartLogger.Safe(() =>
        {
            base.Initialize();
            _bitmapPaint = new SKPaint { IsAntialias = _useAntiAlias, FilterQuality = _filterQuality };
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
            _scanlinePaint = new SKPaint { Color = SKColors.White.WithAlpha(Constants.SCANLINE_ALPHA), Style = SKPaintStyle.Fill };
            _noisePaint = new SKPaint { Style = SKPaintStyle.Fill };
            _glitchSegments = new List<GlitchSegment>();
            SmartLogger.Log(LogLevel.Debug, Constants.LOG_PREFIX, "Initialized");
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.Initialize",
            ErrorMessage = "Failed to initialize GlitchRenderer"
        });
        #endregion

        #region Configuration
        public override void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium) => SmartLogger.Safe(() =>
        {
            base.Configure(isOverlayActive, quality);
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.Configure",
            ErrorMessage = "Failed to configure GlitchRenderer"
        });

        protected override void ApplyQualitySettings() => SmartLogger.Safe(() =>
        {
            base.ApplyQualitySettings();
            if (_bitmapPaint != null) { _bitmapPaint.IsAntialias = _useAntiAlias; _bitmapPaint.FilterQuality = _filterQuality; }
            if (_redPaint != null) { _redPaint.IsAntialias = _useAntiAlias; _redPaint.FilterQuality = _filterQuality; }
            if (_bluePaint != null) { _bluePaint.IsAntialias = _useAntiAlias; _bluePaint.FilterQuality = _filterQuality; }
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = $"{Constants.LOG_PREFIX}.ApplyQualitySettings",
            ErrorMessage = "Failed to apply quality settings"
        });
        #endregion

        #region Rendering
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
                SmartLogger.Log(LogLevel.Error, Constants.LOG_PREFIX, $"Error in Render: {ex.Message}");
            }
            finally
            {
                if (semaphoreAcquired) _glitchSemaphore.Release();
            }
        }

        private void RenderGlitchEffect(SKCanvas canvas, SKImageInfo info, SKPaint basePaint)
        {
            if (_bufferBitmap == null || _bitmapPaint == null || _glitchSegments == null || _glitchProcessedSpectrum == null) return;

            using (var bufferCanvas = new SKCanvas(_bufferBitmap))
            {
                bufferCanvas.Clear(SKColors.Black);
                DrawBaseSpectrum(bufferCanvas, info, basePaint);

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

                bufferCanvas.DrawRect(0, _scanlinePosition, info.Width, Constants.SCANLINE_HEIGHT, _scanlinePaint!);

                if (_useAdvancedEffects && _glitchProcessedSpectrum[1] > Constants.GLITCH_THRESHOLD * 0.3f)
                {
                    DrawDigitalNoise(bufferCanvas, info, _glitchProcessedSpectrum[1]);
                }
            }

            var activeSegments = _glitchSegments.Where(s => s.IsActive).OrderBy(s => s.Y).ToList();
            int currentY = 0;

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

            if (currentY < info.Height)
            {
                SKRect sourceRect = new(0, currentY, info.Width, info.Height);
                SKRect destRect = new(0, currentY, info.Width, info.Height);
                DrawBitmapSegment(canvas, _bufferBitmap, sourceRect, destRect);
            }

            foreach (var segment in activeSegments)
            {
                SKRect sourceRect = new(0, segment.Y, info.Width, segment.Y + segment.Height);
                SKRect destRect = new(segment.XOffset, segment.Y, segment.XOffset + info.Width, segment.Y + segment.Height);
                DrawBitmapSegment(canvas, _bufferBitmap, sourceRect, destRect);
            }
        }

        private void DrawBaseSpectrum(SKCanvas canvas, SKImageInfo info, SKPaint paint)
        {
            if (_glitchProcessedSpectrum == null) return;

            float barCount = Math.Min(Constants.PROCESSED_SPECTRUM_SIZE, _glitchProcessedSpectrum.Length);
            float barWidth = info.Width / barCount;

            for (int i = 0; i < barCount; i++)
            {
                float amplitude = _glitchProcessedSpectrum[i];
                float x = i * barWidth;
                float height = amplitude * info.Height * 0.5f;
                canvas.DrawLine(x, info.Height / 2 - height / 2, x, info.Height / 2 + height / 2, paint);
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

        private void DrawBitmapSegment(SKCanvas canvas, SKBitmap bitmap, SKRect source, SKRect dest)
        {
            canvas.DrawBitmap(bitmap, source, dest, _bitmapPaint);
        }
        #endregion

        #region Glitch Effects
        private void CreateBufferIfNeeded(SKImageInfo info)
        {
            if (_bufferBitmap == null || _bufferBitmap.Width != info.Width || _bufferBitmap.Height != info.Height)
            {
                _bufferBitmap?.Dispose();
                _bufferBitmap = new SKBitmap(info.Width, info.Height);
            }
        }

        private void UpdateGlitchEffects(SKImageInfo info)
        {
            if (_glitchSegments == null || _glitchProcessedSpectrum == null) return;

            _timeAccumulator += Constants.TIME_STEP;
            _scanlinePosition = (int)(_scanlinePosition + Constants.SCANLINE_SPEED);
            if (_scanlinePosition >= info.Height) _scanlinePosition = 0;

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
                    if (_random.NextDouble() < 0.2)
                    {
                        segment.XOffset = (float)(Constants.MAX_GLITCH_OFFSET * (_random.NextDouble() * 2 - 1) * _glitchProcessedSpectrum[0]);
                    }
                    _glitchSegments[i] = segment;
                }
            }

            if (_glitchProcessedSpectrum[0] > Constants.GLITCH_THRESHOLD &&
                _glitchSegments.Count < Constants.MAX_GLITCH_SEGMENTS &&
                _random.NextDouble() < _glitchProcessedSpectrum[0] * 0.4)
            {
                int segmentHeight = (int)(20 + _random.NextDouble() * 50);
                int y = _random.Next(0, info.Height - segmentHeight);
                float duration = Constants.MIN_GLITCH_DURATION +
                                (float)_random.NextDouble() * (Constants.MAX_GLITCH_DURATION - Constants.MIN_GLITCH_DURATION);
                float xOffset = (float)(Constants.MAX_GLITCH_OFFSET * (_random.NextDouble() * 2 - 1) * _glitchProcessedSpectrum[0]);

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

        #region Spectrum Processing
        private void ProcessSpectrum(float[] spectrum)
        {
            if (_glitchProcessedSpectrum == null || _glitchProcessedSpectrum.Length < Constants.PROCESSED_SPECTRUM_SIZE)
            {
                _glitchProcessedSpectrum = new float[Constants.PROCESSED_SPECTRUM_SIZE];
            }

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

                _glitchProcessedSpectrum[i] *= Constants.SPECTRUM_SCALE;
                _glitchProcessedSpectrum[i] = Math.Min(_glitchProcessedSpectrum[i], Constants.SPECTRUM_CLAMP);
            }

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
                                            (avg * Constants.SENSITIVITY - _glitchProcessedSpectrum[band]) * Constants.SMOOTHING_FACTOR;
                    }
                }
            }

            for (int i = 0; i < Constants.BAND_COUNT; i++)
            {
                _glitchProcessedSpectrum[i] = Math.Min(bandActivity[i], Constants.SPECTRUM_CLAMP);
            }
        }

        private float CalculateBandActivity(float[] spectrum, int band, int bandSize)
        {
            int start = band * bandSize;
            int end = Math.Min((band + 1) * bandSize, spectrum.Length / 2);
            float sum = 0;
            for (int i = start; i < end; i++)
            {
                sum += spectrum[i];
            }
            return sum / (end - start);
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

        #region Disposal
        public override void Dispose()
        {
            if (_disposed) return;

            SmartLogger.Safe(() =>
            {
                base.Dispose();
                _glitchSemaphore.Dispose();
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
                _glitchProcessedSpectrum = null;
                SmartLogger.Log(LogLevel.Debug, Constants.LOG_PREFIX, "Disposed");
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = $"{Constants.LOG_PREFIX}.Dispose",
                ErrorMessage = "Error disposing GlitchRenderer"
            });
        }
        #endregion
    }
}