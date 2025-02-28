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
        #endregion

        #region Constants
        private const float GlitchThreshold = 0.5f;
        private const float MaxGlitchOffset = 30f;
        private const float MinGlitchDuration = 0.05f;
        private const float MaxGlitchDuration = 0.3f;
        private const float ScanlineSpeed = 1.5f;
        private const float ScanlineAlpha = 40;
        private const float ScanlineHeight = 3f;
        private const float RgbSplitMax = 10f;
        private const float TimeStep = 0.016f;
        private const float SmoothingFactor = 0.2f;
        private const int MaxGlitchSegments = 10;
        #endregion

        #region Constructor and Initialization
        private GlitchRenderer() { }

        public static GlitchRenderer GetInstance() => _instance ??= new GlitchRenderer();

        public void Initialize()
        {
            if (_isInitialized) return;

            _bitmapPaint = new SKPaint
            {
                IsAntialias = true,
                FilterQuality = SKFilterQuality.Medium
            };

            _glitchSegments = new List<GlitchSegment>();

            _isInitialized = true;
            Log.Debug("GlitchRenderer initialized");
        }

        public void Configure(bool isOverlayActive)
        {
            _isOverlayActive = isOverlayActive;
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
                Log.Error($"Error in GlitchRenderer.Render: {ex.Message}");
            }
            finally
            {
                if (semaphoreAcquired)
                    _spectrumSemaphore.Release();
            }
        }

        private void RenderGlitchEffect(SKCanvas canvas, SKImageInfo info, SKPaint basePaint)
        {
            if (_bufferBitmap == null || _bitmapPaint == null || _glitchSegments == null)
                return;

            // Очищаем буфер для нового кадра
            using (var bufferCanvas = new SKCanvas(_bufferBitmap))
            {
                bufferCanvas.Clear(SKColors.Black);

                // Рисуем базовые элементы (например, спектральные линии)
                DrawBaseSpectrum(bufferCanvas, info, basePaint);

                // Добавляем RGB-сдвиг, если спектр активен
                if (_processedSpectrum != null && _processedSpectrum[2] > GlitchThreshold * 0.5f)
                {
                    float rgbSplit = _processedSpectrum[2] * RgbSplitMax;

                    using var redPaint = new SKPaint
                    {
                        Color = SKColors.Red.WithAlpha(100),
                        BlendMode = SKBlendMode.SrcOver
                    };

                    using var bluePaint = new SKPaint
                    {
                        Color = SKColors.Blue.WithAlpha(100),
                        BlendMode = SKBlendMode.SrcOver
                    };

                    // Рисуем сдвинутые копии в красном и синем каналах
                    bufferCanvas.Save();
                    bufferCanvas.Translate(-rgbSplit, 0);
                    DrawBaseSpectrum(bufferCanvas, info, redPaint);
                    bufferCanvas.Restore();

                    bufferCanvas.Save();
                    bufferCanvas.Translate(rgbSplit, 0);
                    DrawBaseSpectrum(bufferCanvas, info, bluePaint);
                    bufferCanvas.Restore();
                }

                // Рисуем сканлайн
                using (var scanlinePaint = new SKPaint
                {
                    Color = SKColors.White.WithAlpha((byte)ScanlineAlpha),
                    Style = SKPaintStyle.Fill
                })
                {
                    bufferCanvas.DrawRect(
                        0,
                        _scanlinePosition,
                        info.Width,
                        ScanlineHeight,
                        scanlinePaint);
                }

                // Применяем цифровой шум, если спектр активен
                if (_processedSpectrum != null && _processedSpectrum[1] > GlitchThreshold * 0.3f)
                {
                    DrawDigitalNoise(bufferCanvas, info, _processedSpectrum[1]);
                }
            }

            // Рисуем сегменты глитча
            foreach (var segment in _glitchSegments)
            {
                if (!segment.IsActive) continue;

                // Копируем части буфера со смещением для создания эффекта глитча
                SKRect sourceRect = new SKRect(
                    0,
                    segment.Y,
                    info.Width,
                    segment.Y + segment.Height);

                SKRect destRect = new SKRect(
                    segment.XOffset,
                    segment.Y,
                    segment.XOffset + info.Width,
                    segment.Y + segment.Height);

                canvas.DrawBitmap(_bufferBitmap, sourceRect, destRect);
            }

            // Рисуем неискаженные части
            for (int y = 0; y < info.Height;)
            {
                bool isGlitched = false;
                int segmentHeight = 0;

                // Проверяем, находится ли текущая строка в глитч-сегменте
                foreach (var segment in _glitchSegments)
                {
                    if (segment.IsActive && y >= segment.Y && y < segment.Y + segment.Height)
                    {
                        isGlitched = true;
                        segmentHeight = segment.Y + segment.Height - y;
                        break;
                    }
                }

                if (!isGlitched)
                {
                    // Находим следующий глитч-сегмент (если есть)
                    int nextGlitchY = info.Height;
                    foreach (var segment in _glitchSegments)
                    {
                        if (segment.IsActive && segment.Y > y && segment.Y < nextGlitchY)
                        {
                            nextGlitchY = segment.Y;
                        }
                    }

                    // Рисуем неискаженную часть до следующего глитча
                    int height = nextGlitchY - y;

                    SKRect sourceRect = new SKRect(0, y, info.Width, y + height);
                    SKRect destRect = new SKRect(0, y, info.Width, y + height);

                    canvas.DrawBitmap(_bufferBitmap, sourceRect, destRect);

                    y += height;
                }
                else
                {
                    // Пропускаем глитч-сегмент
                    y += segmentHeight;
                }
            }
        }

        private void DrawBaseSpectrum(SKCanvas canvas, SKImageInfo info, SKPaint paint)
        {
            if (_processedSpectrum == null) return;

            // Рисуем простые линии спектра
            float barCount = Math.Min(128, _processedSpectrum.Length);
            float barWidth = info.Width / barCount;

            for (int i = 0; i < barCount; i++)
            {
                float amplitude = _processedSpectrum[i];
                float x = i * barWidth;
                float height = amplitude * info.Height * 0.5f;

                // Рисуем линию
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
            // Рисуем цифровой шум (случайные пиксели)
            int noiseCount = (int)(intensity * 1000);

            using (var noisePaint = new SKPaint
            {
                Color = SKColors.White.WithAlpha((byte)(intensity * 150)),
                Style = SKPaintStyle.Fill
            })
            {
                for (int i = 0; i < noiseCount; i++)
                {
                    float x = _random.Next(0, info.Width);
                    float y = _random.Next(0, info.Height);
                    float size = 1 + _random.Next(0, 3);

                    canvas.DrawRect(x, y, size, size, noisePaint);
                }
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

            _timeAccumulator += TimeStep;

            // Обновляем позицию сканлайна
            _scanlinePosition = (int)(_scanlinePosition + ScanlineSpeed);
            if (_scanlinePosition >= info.Height)
                _scanlinePosition = 0;

            // Обновляем существующие глитч-сегменты
            for (int i = _glitchSegments.Count - 1; i >= 0; i--)
            {
                var segment = _glitchSegments[i];

                segment.Duration -= TimeStep;
                if (segment.Duration <= 0)
                {
                    segment.IsActive = false;
                    _glitchSegments.RemoveAt(i);
                }
                else
                {
                    // Случайно меняем смещение для активных сегментов
                    if (_random.NextDouble() < 0.2)
                    {
                        segment.XOffset = (float)(MaxGlitchOffset * (_random.NextDouble() * 2 - 1) *
                                                _processedSpectrum[0]);
                    }

                    _glitchSegments[i] = segment;
                }
            }

            // Создаем новые глитч-сегменты, если активность спектра выше порога
            if (_processedSpectrum[0] > GlitchThreshold && _glitchSegments.Count < MaxGlitchSegments)
            {
                if (_random.NextDouble() < _processedSpectrum[0] * 0.4)
                {
                    int segmentHeight = (int)(20 + _random.NextDouble() * 50);
                    int y = _random.Next(0, info.Height - segmentHeight);

                    float duration = MinGlitchDuration +
                                   (float)_random.NextDouble() *
                                   (MaxGlitchDuration - MinGlitchDuration);

                    float xOffset = (float)(MaxGlitchOffset * (_random.NextDouble() * 2 - 1) *
                                          _processedSpectrum[0]);

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
            int bands = 3; // Низкие, средние, высокие частоты

            if (_processedSpectrum == null || _processedSpectrum.Length < 128)
            {
                _processedSpectrum = new float[128];
            }

            // Масштабируем спектр до 128 точек для визуализации
            for (int i = 0; i < 128; i++)
            {
                float index = (float)i * spectrum.Length / (2f * 128);
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

                // Усиливаем для лучшей видимости
                _processedSpectrum[i] *= 2.0f;
                _processedSpectrum[i] = Math.Min(_processedSpectrum[i], 1.0f);
            }

            // Вычисляем активность по диапазонам для эффектов глитча
            float[] bandActivity = new float[bands];
            int bandSize = spectrum.Length / (2 * bands);

            for (int band = 0; band < bands; band++)
            {
                float sum = 0;
                int start = band * bandSize;
                int end = (band + 1) * bandSize;
                end = Math.Min(end, spectrum.Length / 2);

                for (int i = start; i < end; i++)
                {
                    sum += spectrum[i];
                }

                float avg = sum / (end - start);

                // Применяем сглаживание
                if (band < _processedSpectrum.Length)
                {
                    if (_processedSpectrum[band] == 0) // Первая инициализация
                    {
                        bandActivity[band] = avg * 3; // Увеличиваем чувствительность
                    }
                    else
                    {
                        bandActivity[band] = _processedSpectrum[band] +
                                           (avg * 3 - _processedSpectrum[band]) * SmoothingFactor;
                    }
                }
            }

            // Обновляем первые 3 значения обработанного спектра с активностью диапазонов
            for (int i = 0; i < bands; i++)
            {
                _processedSpectrum[i] = Math.Min(bandActivity[i], 1.0f);
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
                Log.Error("GlitchRenderer not initialized before rendering");
                return false;
            }

            if (canvas == null ||
                spectrum == null || spectrum.Length < 2 ||
                paint == null ||
                drawPerformanceInfo == null ||
                info.Width <= 0 || info.Height <= 0)
            {
                Log.Error("Invalid render parameters for GlitchRenderer");
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

            _bitmapPaint = null;
            _bufferBitmap = null;
            _glitchSegments = null;
            _processedSpectrum = null;

            _disposed = true;
            _isInitialized = false;
            Log.Debug("GlitchRenderer disposed");
        }
        #endregion
    }
}