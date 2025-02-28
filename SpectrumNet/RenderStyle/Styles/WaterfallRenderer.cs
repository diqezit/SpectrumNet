#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;

namespace SpectrumNet
{
    public sealed class WaterfallRenderer : ISpectrumRenderer, IDisposable
    {
        private static WaterfallRenderer? _instance;
        private bool _isInitialized;
        private bool _isOverlayActive;
        private volatile bool _disposed;
        private readonly SemaphoreSlim _spectrumSemaphore = new(1, 1);

        private float[]? _currentSpectrum;
        private float[][]? _spectrogramBuffer;
        private int _bufferHead = 0;

        private SKBitmap? _waterfallBitmap; // Переиспользуемый битмап

        private const int DefaultBufferHeight = 256;
        private const int OverlayBufferHeight = 128;

        // Исходные сигнальные значения: минимальное (не равное нулю, чтобы избежать log(0)) и максимальное
        private const float MinimumSignal = 1e-6f;
        private const float MaximumSignal = 5000f;

        // Диапазон децибел: от -100 дБ (минимум) до 0 дБ (максимум)
        private const float minDB = -100f;
        private const float maxDB = 0f;

        // Предвычисленная палитра из 256 цветов для SDR waterfall (ARGB)
        private static int[]? _colorPalette;

        private WaterfallRenderer() { }

        public static WaterfallRenderer GetInstance() => _instance ??= new WaterfallRenderer();

        public void Initialize()
        {
            if (_isInitialized) return;
            int bufferHeight = _isOverlayActive ? OverlayBufferHeight : DefaultBufferHeight;
            InitializeSpectrogramBuffer(bufferHeight);
            InitializeColorPalette();
            _isInitialized = true;
            Log.Debug("WaterfallRenderer инициализирован для SDR waterplot визуализации");
        }

        public void Configure(bool isOverlayActive)
        {
            if (_isOverlayActive == isOverlayActive) return;
            _isOverlayActive = isOverlayActive;
            int bufferHeight = _isOverlayActive ? OverlayBufferHeight : DefaultBufferHeight;
            InitializeSpectrogramBuffer(bufferHeight);
        }

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
            if (!ValidateRenderParams(canvas, spectrum, info, paint))
                return;

            bool semaphoreAcquired = false;
            try
            {
                semaphoreAcquired = _spectrumSemaphore.Wait(5);
                if (semaphoreAcquired && spectrum != null)
                {
                    UpdateSpectrogramBuffer(spectrum);
                }

                if (_spectrogramBuffer == null)
                    return;

                int bufferHeight = _spectrogramBuffer.Length;
                int spectrumWidth = _spectrogramBuffer[0].Length;

                // Переиспользование битмапа: создаём его один раз или при изменении размеров
                if (_waterfallBitmap == null || _waterfallBitmap.Width != spectrumWidth || _waterfallBitmap.Height != bufferHeight)
                {
                    _waterfallBitmap?.Dispose();
                    _waterfallBitmap = new SKBitmap(spectrumWidth, bufferHeight);
                }

                // Обновляем пиксели битмапа с использованием предвычисленной палитры и параллельной обработки
                unsafe
                {
                    int* basePtr = (int*)_waterfallBitmap.GetPixels();
                    Parallel.For(0, bufferHeight, y =>
                    {
                        int bufferIndex = (_bufferHead + 1 + y) % bufferHeight;
                        int rowOffset = y * spectrumWidth;
                        for (int x = 0; x < spectrumWidth; x++)
                        {
                            float value = _spectrogramBuffer[bufferIndex][x];
                            // Инвертируем нормализацию: таким образом фон становится темным, а сигнал – ярким.
                            float normalized = 1 - NormalizeValue(value);
                            int paletteIndex = (int)(normalized * 255f);
                            paletteIndex = paletteIndex < 0 ? 0 : (paletteIndex > 255 ? 255 : paletteIndex);
                            basePtr[rowOffset + x] = _colorPalette![paletteIndex];
                        }
                    });
                }

                // Очищаем канву (фон – черный, как принято в SDR)
                canvas!.Clear(SKColors.Black);
                SKRect destRect = new SKRect(0, 0, info.Width, info.Height);
                canvas.DrawBitmap(_waterfallBitmap, destRect);

                // drawPerformanceInfo?.Invoke(canvas, info);
            }
            catch (Exception ex)
            {
                Log.Error($"Error in WaterfallRenderer.Render: {ex.Message}");
            }
            finally
            {
                if (semaphoreAcquired)
                    _spectrumSemaphore.Release();
            }
        }

        /// <summary>
        /// Предвычисление палитры для спектрограммы.
        /// Градиент: от темно-синего через синий, голубой, зеленый, желтый к красному.
        /// </summary>
        private static void InitializeColorPalette()
        {
            if (_colorPalette != null) return;
            _colorPalette = new int[256];
            for (int i = 0; i < 256; i++)
            {
                float normalized = i / 255f;
                SKColor color = GetSpectrogramColor(normalized);
                _colorPalette[i] = (color.Alpha << 24) | (color.Red << 16) | (color.Green << 8) | color.Blue;
            }
        }

        /// <summary>
        /// Функция расчета цвета спектрограммы для заданного нормализованного значения.
        /// Градиент: от темно-синего (не чистый черный) к синему, голубому, зеленому, желтому и красному.
        /// </summary>
        private static SKColor GetSpectrogramColor(float normalized)
        {
            normalized = Math.Clamp(normalized, 0f, 1f);
            if (normalized < 0.25f)
            {
                // От темно-синего к синему
                float t = normalized / 0.25f;
                byte r = 0;
                byte g = (byte)(t * 50);
                byte b = (byte)(50 + t * 205);
                return new SKColor(r, g, b, 255);
            }
            else if (normalized < 0.5f)
            {
                // От синего к голубому
                float t = (normalized - 0.25f) / 0.25f;
                byte r = 0;
                byte g = (byte)(50 + t * 205);
                byte b = 255;
                return new SKColor(r, g, b, 255);
            }
            else if (normalized < 0.75f)
            {
                // От голубого к зеленому/желтому
                float t = (normalized - 0.5f) / 0.25f;
                byte r = (byte)(t * 255);
                byte g = 255;
                byte b = (byte)(255 - t * 255);
                return new SKColor(r, g, b, 255);
            }
            else
            {
                // От желтого к красному
                float t = (normalized - 0.75f) / 0.25f;
                byte r = 255;
                byte g = (byte)(255 - t * 255);
                byte b = 0;
                return new SKColor(r, g, b, 255);
            }
        }

        /// <summary>
        /// Нормализует значение сигнала, переводя его в децибелы с логарифмическим масштабом.
        /// Значения нормализуются в диапазоне от minDB до maxDB.
        /// </summary>
        private float NormalizeValue(float value)
        {
            const float epsilon = 1e-6f;
            float dB = 20f * (float)Math.Log10(value + epsilon);
            return Math.Clamp((dB - minDB) / (maxDB - minDB), 0f, 1f);
        }

        private void InitializeSpectrogramBuffer(int bufferHeight)
        {
            int spectrumWidth = _currentSpectrum?.Length ?? 1024;
            _spectrogramBuffer = new float[bufferHeight][];
            for (int i = 0; i < bufferHeight; i++)
            {
                _spectrogramBuffer[i] = new float[spectrumWidth];
                for (int j = 0; j < spectrumWidth; j++)
                {
                    _spectrogramBuffer[i][j] = MinimumSignal;
                }
            }
            _bufferHead = 0;
        }

        private void UpdateSpectrogramBuffer(float[] spectrum)
        {
            if (_spectrogramBuffer == null)
                return;
            int bufferHeight = _spectrogramBuffer.Length;
            if (_currentSpectrum == null || _currentSpectrum.Length != spectrum.Length)
            {
                _currentSpectrum = new float[spectrum.Length];
                InitializeSpectrogramBuffer(bufferHeight);
            }
            Array.Copy(spectrum, _currentSpectrum, spectrum.Length);
            _bufferHead = (_bufferHead + 1) % bufferHeight;
            Array.Copy(spectrum, _spectrogramBuffer[_bufferHead], Math.Min(spectrum.Length, _spectrogramBuffer[_bufferHead].Length));
        }

        private bool ValidateRenderParams(SKCanvas? canvas, float[]? spectrum, SKImageInfo info, SKPaint? paint)
        {
            if (_disposed)
                return false;
            if (!_isInitialized)
            {
                Initialize();
                return false;
            }
            if (canvas == null || info.Width <= 0 || info.Height <= 0)
                return false;
            return true;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _spectrumSemaphore.Dispose();
            _waterfallBitmap?.Dispose();
            _spectrogramBuffer = null;
            _currentSpectrum = null;
            _disposed = true;
            _isInitialized = false;
        }
    }
}