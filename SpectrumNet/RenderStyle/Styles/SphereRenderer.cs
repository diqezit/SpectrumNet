#nullable enable

namespace SpectrumNet
{
    #region Renderers Implementations

    /// <summary>
    /// **SphereRenderer** - реализация рендерера спектра в виде круговой диаграммы из сфер.
    /// <br/>
    /// **SphereRenderer** - spectrum renderer visualizing audio spectrum as a circular arrangement of spheres.
    /// <para>
    /// **Основные характеристики / Key Features:**
    /// </para>
    /// <list type="bullet">
    ///     <item> **Круговая диаграмма / Circular Diagram:**
    ///          Отображает интенсивность частот в виде сфер, расположенных по кругу.
    ///          Размер сферы = амплитуде спектра.
    ///          <br/>
    ///          **Circular Diagram:**
    ///          Displays frequency intensity via spheres arranged in a circle.
    ///          Sphere size = spectrum amplitude.
    ///     </item>
    ///     <item> **Сглаживание / Smoothing:**
    ///          Плавное изменение прозрачности для естественной визуализации.
    ///          Настройка через <see cref="AlphaSmoothingFactor"/>.
    ///          <br/>
    ///          **Smoothing:**
    ///          Smooth alpha transitions for natural visualization.
    ///          Configured via <see cref="AlphaSmoothingFactor"/>.
    ///     </item>
    ///     <item> **Адаптивность / Adaptivity:**
    ///          Автоматическая настройка размеров и расположения в зависимости от размера канваса.
    ///          <br/>
    ///          **Adaptivity:**
    ///          Automatic size and position adjustment based on canvas dimensions.
    ///     </item>
    ///     <item> **Производительность / Performance:**
    ///          Предварительный расчет тригонометрических значений.
    ///          Оптимизированная обработка спектра.
    ///          <br/>
    ///          **Performance:**
    ///          Precomputed trigonometric values.
    ///          Optimized spectrum processing.
    ///     </item>
    ///     <item> **Singleton:**
    ///          Единственный экземпляр в приложении для оптимизации ресурсов.
    ///          <br/>
    ///          **Singleton:**
    ///          Single instance per app for resource optimization.
    ///     </item>
    /// </list>
    /// </summary>
    public sealed class SphereRenderer : ISpectrumRenderer, IDisposable
    {
        #region Fields and Properties
        private static SphereRenderer? _instance;
        private bool _isInitialized;
        private bool _isOverlayActive;
        private volatile bool _disposed;
        private float _sphereRadius, _sphereSpacing;
        private int _sphereCount;
        private float[]? _cosValues, _sinValues, _currentAlphas;
        private float[]? _processedSpectrum;
        private readonly SemaphoreSlim _spectrumSemaphore = new(1, 1);
        #endregion

        #region Constants
        private const float PiOver180 = (float)(Math.PI / 180);
        private const float MinMagnitude = 0.01f;
        private const float MaxIntensityMultiplier = 3f;
        private const float AlphaSmoothingFactor = 0.2f;
        private const float MinAlpha = 0.1f;
        private const float DefaultRadius = 40f;
        private const float DefaultSpacing = 10f;
        private const int DefaultCount = 8;

        private static readonly (float Radius, float Spacing, int Count) DefaultConfig =
            (DefaultRadius, DefaultSpacing, DefaultCount);

        private static readonly (float Radius, float Spacing, int Count) OverlayConfig =
            (20f, 5f, 16);
        #endregion

        #region Constructor and Instance Management
        private SphereRenderer() { }

        public static SphereRenderer GetInstance() => _instance ??= new SphereRenderer();
        #endregion

        #region ISpectrumRenderer Implementation
        public void Initialize()
        {
            if (_isInitialized) return;

            UpdateConfiguration(DefaultConfig);
            _isInitialized = true;
            Log.Debug("SphereRenderer initialized");
        }

        public void Configure(bool isOverlayActive)
        {
            if (_isOverlayActive == isOverlayActive) return;

            _isOverlayActive = isOverlayActive;
            UpdateConfiguration(isOverlayActive ? OverlayConfig : DefaultConfig);
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
            if (!ValidateRenderParams(canvas, spectrum, info, paint, drawPerformanceInfo))
                return;

            bool semaphoreAcquired = false;
            try
            {
                semaphoreAcquired = _spectrumSemaphore.Wait(0);

                if (semaphoreAcquired)
                {
                    ProcessSpectrum(spectrum!, info, barWidth, barSpacing, barCount);
                }

                // Если семафор не был получен, используем ранее обработанный спектр
                if (_processedSpectrum != null)
                {
                    int sphereCount = Math.Min(spectrum!.Length / 2, _sphereCount);
                    float centerRadius = info.Height / 2f - (_sphereRadius + _sphereSpacing);

                    RenderSpheres(
                        canvas!,
                        _processedSpectrum.AsSpan(0, sphereCount),
                        info.Width / 2f,
                        info.Height / 2f,
                        centerRadius,
                        paint!);
                }

                drawPerformanceInfo?.Invoke(canvas!, info);
            }
            catch (Exception ex)
            {
                Log.Error($"Error in SphereRenderer.Render: {ex.Message}");
            }
            finally
            {
                if (semaphoreAcquired)
                    _spectrumSemaphore.Release();
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                _spectrumSemaphore.Dispose();

                if (_processedSpectrum != null)
                    ArrayPool<float>.Shared.Return(_processedSpectrum);

                _cosValues = _sinValues = _currentAlphas = _processedSpectrum = null;
            }

            _disposed = true;
            _isInitialized = false;
            Log.Debug("SphereRenderer disposed");
        }
        #endregion

        #region Processing Methods
        private void ProcessSpectrum(float[] spectrum, SKImageInfo info, float barWidth, float barSpacing, int barCount)
        {
            AdjustConfiguration(barCount, barSpacing, info.Width, info.Height);
            int sphereCount = Math.Min(spectrum.Length / 2, _sphereCount);

            EnsureProcessedSpectrumCapacity(sphereCount);

            ScaleSpectrum(spectrum, _processedSpectrum!, sphereCount);
            UpdateAlphas(_processedSpectrum!.AsSpan(0, sphereCount));
        }

        private void EnsureProcessedSpectrumCapacity(int requiredSize)
        {
            if (_processedSpectrum != null && _processedSpectrum.Length >= requiredSize)
                return;

            if (_processedSpectrum != null)
                ArrayPool<float>.Shared.Return(_processedSpectrum);

            _processedSpectrum = ArrayPool<float>.Shared.Rent(requiredSize);
        }

        private void UpdateConfiguration((float Radius, float Spacing, int Count) config)
        {
            (_sphereRadius, _sphereSpacing, _sphereCount) = config;

            EnsureArrayCapacity(ref _currentAlphas, _sphereCount);
            PrecomputeTrigValues();
        }

        private void AdjustConfiguration(int barCount, float barSpacing, int canvasWidth, int canvasHeight)
        {
            // Адаптируем радиус сфер в зависимости от количества и расстояния баров
            _sphereRadius = Math.Max(5f, DefaultRadius - barCount * 0.2f + barSpacing * 0.5f);
            _sphereSpacing = Math.Max(2f, DefaultSpacing - barCount * 0.1f + barSpacing * 0.3f);
            _sphereCount = Math.Clamp(barCount / 2, 4, 64);

            // Ограничиваем максимальный радиус размерами канваса
            float maxRadius = Math.Min(canvasWidth, canvasHeight) / 2f - (_sphereRadius + _sphereSpacing);
            if (_sphereRadius > maxRadius)
                _sphereRadius = maxRadius;

            EnsureArrayCapacity(ref _currentAlphas, _sphereCount);
            PrecomputeTrigValues();
        }
        #endregion

        #region Rendering Methods
        private void RenderSpheres(
            SKCanvas canvas,
            ReadOnlySpan<float> spectrum,
            float centerX,
            float centerY,
            float maxRadius,
            SKPaint paint)
        {
            if (!AreArraysValid(spectrum.Length))
                return;

            using var spherePaint = paint.Clone();
            spherePaint.Style = SKPaintStyle.Fill;
            spherePaint.IsAntialias = true;

            for (int i = 0; i < spectrum.Length; i++)
            {
                float magnitude = spectrum[i];
                if (magnitude < MinMagnitude)
                    continue;

                float x = centerX + _cosValues![i] * maxRadius;
                float y = centerY + _sinValues![i] * maxRadius;
                float alpha = MathF.Min(_currentAlphas![i], 1.0f);

                // Применяем альфа-канал к цвету
                spherePaint.Color = paint.Color.WithAlpha((byte)(255 * alpha));

                // Размер сферы зависит от интенсивности спектра
                float circleSize = MathF.Max(magnitude * _sphereRadius, 2f) + _sphereSpacing * 0.2f;
                canvas.DrawCircle(x, y, circleSize, spherePaint);
            }
        }
        #endregion

        #region Helper Methods
        private void PrecomputeTrigValues()
        {
            EnsureArrayCapacity(ref _cosValues, _sphereCount);
            EnsureArrayCapacity(ref _sinValues, _sphereCount);

            float angleStepRad = 360f / _sphereCount * PiOver180;

            for (int i = 0; i < _sphereCount; i++)
            {
                float angle = i * angleStepRad;
                _cosValues![i] = MathF.Cos(angle);
                _sinValues![i] = MathF.Sin(angle);
            }
        }

        private static void ScaleSpectrum(float[] source, float[] target, int targetCount)
        {
            float blockSize = source.Length / (2f * targetCount);

            for (int i = 0; i < targetCount; i++)
            {
                int start = (int)(i * blockSize);
                int end = (int)((i + 1) * blockSize);
                end = Math.Min(end, source.Length / 2);

                if (start >= end)
                {
                    target[i] = 0;
                    continue;
                }

                float sum = 0;
                for (int j = start; j < end; j++)
                    sum += source[j];

                target[i] = sum / (end - start);
            }
        }

        private void UpdateAlphas(ReadOnlySpan<float> spectrum)
        {
            if (_currentAlphas == null || _currentAlphas.Length < spectrum.Length)
                return;

            for (int i = 0; i < spectrum.Length; i++)
            {
                float targetAlpha = MathF.Max(MinAlpha, spectrum[i] * MaxIntensityMultiplier);
                _currentAlphas[i] = _currentAlphas[i] +
                                   (targetAlpha - _currentAlphas[i]) * AlphaSmoothingFactor;
            }
        }

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
                Log.Error("SphereRenderer not initialized before rendering");
                return false;
            }

            if (canvas == null ||
                spectrum == null || spectrum.Length < 2 ||
                paint == null ||
                drawPerformanceInfo == null ||
                info.Width <= 0 || info.Height <= 0)
            {
                Log.Error("Invalid render parameters for SphereRenderer");
                return false;
            }

            return true;
        }

        private bool AreArraysValid(int requiredLength) =>
            _cosValues != null && _sinValues != null && _currentAlphas != null &&
            _cosValues.Length >= requiredLength &&
            _sinValues.Length >= requiredLength &&
            _currentAlphas.Length >= requiredLength;

        private static void EnsureArrayCapacity<T>(ref T[]? array, int requiredSize) where T : struct
        {
            if (array == null || array.Length < requiredSize)
                array = new T[requiredSize];
        }
        #endregion
    }
    #endregion
}