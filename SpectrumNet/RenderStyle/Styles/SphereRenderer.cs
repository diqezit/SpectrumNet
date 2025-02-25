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
        private float _sphereRadius, _sphereSpacing;
        private int _sphereCount;
        private float[]? _cosValues, _sinValues, _currentAlphas;
        private SKPaint? _spherePaint;

        #endregion

        #region Constants

        /// <summary>
        /// Константа для преобразования градусов в радианы
        /// </summary>
        private const float PiOver180 = (float)(Math.PI / 180);

        /// <summary>
        /// Минимальная величина спектра для отображения сферы
        /// </summary>
        private const float MinMagnitude = 0.01f;

        /// <summary>
        /// Максимальный множитель интенсивности для усиления визуального эффекта
        /// </summary>
        private const float MaxIntensityMultiplier = 3f;

        /// <summary>
        /// Фактор сглаживания альфа-канала для плавного изменения прозрачности
        /// </summary>
        private const float AlphaSmoothingFactor = 0.2f;

        /// <summary>
        /// Минимальное значение альфа-канала для видимости сфер
        /// </summary>
        private const float MinAlpha = 0.1f;

        /// <summary>
        /// Стандартный радиус сферы
        /// </summary>
        private const float DefaultRadius = 40f;

        /// <summary>
        /// Стандартное расстояние между сферами
        /// </summary>
        private const float DefaultSpacing = 10f;

        /// <summary>
        /// Стандартное количество сфер
        /// </summary>
        private const int DefaultCount = 8;

        #endregion

        #region Static Configurations

        /// <summary>
        /// Конфигурация по умолчанию (радиус, расстояние, количество)
        /// </summary>
        private static readonly (float Radius, float Spacing, int Count) DefaultConfig = (DefaultRadius, DefaultSpacing, DefaultCount);

        /// <summary>
        /// Конфигурация для оверлея (радиус, расстояние, количество)
        /// </summary>
        private static readonly (float Radius, float Spacing, int Count) OverlayConfig = (20f, 5f, 16);

        #endregion

        #region Constructor and Instance Management

        /// <summary>
        /// Приватный конструктор для реализации паттерна Singleton
        /// </summary>
        private SphereRenderer() { }

        /// <summary>
        /// Получает единственный экземпляр рендерера сфер
        /// </summary>
        /// <returns>Экземпляр SphereRenderer</returns>
        public static SphereRenderer GetInstance() => _instance ??= new SphereRenderer();

        #endregion

        #region ISpectrumRenderer Implementation

        /// <summary>
        /// Инициализирует рендерер, создавая необходимые ресурсы
        /// </summary>
        public void Initialize()
        {
            if (_isInitialized) return;

            _spherePaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };

            UpdateConfiguration(DefaultConfig);
            _isInitialized = true;
        }

        /// <summary>
        /// Настраивает рендерер в зависимости от режима оверлея
        /// </summary>
        /// <param name="isOverlayActive">Флаг активности оверлея</param>
        public void Configure(bool isOverlayActive)
        {
            if (_isOverlayActive == isOverlayActive) return;

            _isOverlayActive = isOverlayActive;
            UpdateConfiguration(isOverlayActive ? OverlayConfig : DefaultConfig);
        }

        /// <summary>
        /// Отрисовывает спектр в виде сфер на канвасе
        /// </summary>
        /// <param name="canvas">Канвас для рисования</param>
        /// <param name="spectrum">Массив данных спектра</param>
        /// <param name="info">Информация о размерах канваса</param>
        /// <param name="barWidth">Ширина бара (используется для адаптации)</param>
        /// <param name="barSpacing">Расстояние между барами (используется для адаптации)</param>
        /// <param name="barCount">Количество баров (используется для адаптации)</param>
        /// <param name="paint">Базовая кисть для рисования</param>
        /// <param name="drawPerformanceInfo">Делегат для отображения информации о производительности</param>
        public void Render(SKCanvas canvas, float[]? spectrum, SKImageInfo info, float barWidth, float barSpacing, int barCount, SKPaint paint, Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
        {
            if (!_isInitialized || !AreRenderParamsValid(canvas, spectrum, info, paint))
                return;

            AdjustConfiguration(barCount, barSpacing, info.Width, info.Height);
            int sphereCount = Math.Min(spectrum!.Length / 2, _sphereCount);
            float[] scaledSpectrum = ArrayPool<float>.Shared.Rent(sphereCount);

            try
            {
                ScaleSpectrum(spectrum, scaledSpectrum, sphereCount);
                UpdateAlphas(scaledSpectrum.AsSpan(0, sphereCount));
                RenderSpheres(canvas, scaledSpectrum.AsSpan(0, sphereCount), info.Width / 2f, info.Height / 2f,
                              info.Height / 2f - (_sphereRadius + _sphereSpacing), paint);

                drawPerformanceInfo(canvas, info);
            }
            finally
            {
                ArrayPool<float>.Shared.Return(scaledSpectrum);
            }
        }

        /// <summary>
        /// Освобождает ресурсы, используемые рендерером
        /// </summary>
        public void Dispose()
        {
            _spherePaint?.Dispose();
            _spherePaint = null;
            _cosValues = _sinValues = _currentAlphas = null;
            _isInitialized = false;
        }

        #endregion

        #region Private Helper Methods

        /// <summary>
        /// Обновляет конфигурацию рендерера
        /// </summary>
        /// <param name="config">Новая конфигурация (радиус, расстояние, количество)</param>
        private void UpdateConfiguration((float Radius, float Spacing, int Count) config)
        {
            (_sphereRadius, _sphereSpacing, _sphereCount) = config;
            _currentAlphas = new float[_sphereCount];
            PrecomputeTrigValues();
        }

        /// <summary>
        /// Адаптирует конфигурацию под текущие параметры отображения
        /// </summary>
        /// <param name="barCount">Количество баров</param>
        /// <param name="barSpacing">Расстояние между барами</param>
        /// <param name="canvasWidth">Ширина канваса</param>
        /// <param name="canvasHeight">Высота канваса</param>
        private void AdjustConfiguration(int barCount, float barSpacing, int canvasWidth, int canvasHeight)
        {
            // Адаптируем радиус сфер в зависимости от количества и расстояния баров
            _sphereRadius = Math.Max(5f, DefaultRadius - barCount * 0.2f + barSpacing * 0.5f);
            _sphereSpacing = Math.Max(2f, DefaultSpacing - barCount * 0.1f + barSpacing * 0.3f);
            _sphereCount = Math.Clamp(barCount / 2, 4, 64);

            // Ограничиваем максимальный радиус размерами канваса
            float maxRadius = Math.Min(canvasWidth, canvasHeight) / 2f - (_sphereRadius + _sphereSpacing);
            if (_sphereRadius > maxRadius) _sphereRadius = maxRadius;

            _currentAlphas = new float[_sphereCount];
            PrecomputeTrigValues();
        }

        /// <summary>
        /// Предварительно вычисляет тригонометрические значения для оптимизации
        /// </summary>
        private void PrecomputeTrigValues()
        {
            _cosValues = new float[_sphereCount];
            _sinValues = new float[_sphereCount];
            float angleStepRad = 360f / _sphereCount * PiOver180;

            for (int i = 0; i < _sphereCount; i++)
            {
                float angle = i * angleStepRad;
                _cosValues[i] = MathF.Cos(angle);
                _sinValues[i] = MathF.Sin(angle);
            }
        }

        /// <summary>
        /// Масштабирует спектр для отображения нужного количества сфер
        /// </summary>
        /// <param name="source">Исходный спектр</param>
        /// <param name="target">Целевой массив для масштабированного спектра</param>
        /// <param name="targetCount">Целевое количество элементов</param>
        private static void ScaleSpectrum(float[] source, float[] target, int targetCount)
        {
            float blockSize = source.Length / (2f * targetCount);
            for (int i = 0; i < targetCount; i++)
            {
                float sum = 0;
                int start = (int)(i * blockSize), end = (int)((i + 1) * blockSize);
                for (int j = start; j < end; j++) sum += source[j];
                target[i] = sum / blockSize;
            }
        }

        /// <summary>
        /// Обновляет значения альфа-канала для плавного изменения прозрачности
        /// </summary>
        /// <param name="spectrum">Данные спектра</param>
        private void UpdateAlphas(ReadOnlySpan<float> spectrum)
        {
            for (int i = 0; i < spectrum.Length; i++)
            {
                float targetAlpha = MathF.Max(MinAlpha, spectrum[i] * MaxIntensityMultiplier);
                _currentAlphas![i] = _currentAlphas[i] + (targetAlpha - _currentAlphas[i]) * AlphaSmoothingFactor;
            }
        }

        /// <summary>
        /// Отрисовывает сферы на канвасе
        /// </summary>
        /// <param name="canvas">Канвас для рисования</param>
        /// <param name="spectrum">Данные спектра</param>
        /// <param name="centerX">Координата X центра</param>
        /// <param name="centerY">Координата Y центра</param>
        /// <param name="maxRadius">Максимальный радиус расположения сфер</param>
        /// <param name="paint">Кисть для рисования</param>
        private void RenderSpheres(SKCanvas canvas, ReadOnlySpan<float> spectrum, float centerX, float centerY, float maxRadius, SKPaint paint)
        {
            for (int i = 0; i < spectrum.Length; i++)
            {
                float magnitude = spectrum[i];
                if (magnitude < MinMagnitude) continue;

                float x = centerX + _cosValues![i] * maxRadius;
                float y = centerY + _sinValues![i] * maxRadius;
                float alpha = MathF.Min(_currentAlphas![i], 1.0f);

                // Применяем альфа-канал к цвету
                paint.Color = paint.Color.WithAlpha((byte)(255 * alpha));

                // Размер сферы зависит от интенсивности спектра
                float circleSize = MathF.Max(magnitude * _sphereRadius, 2f) + _sphereSpacing * 0.2f;
                canvas.DrawCircle(x, y, circleSize, paint);
            }
        }

        /// <summary>
        /// Проверяет валидность параметров рендеринга
        /// </summary>
        /// <param name="canvas">Канвас для рисования</param>
        /// <param name="spectrum">Данные спектра</param>
        /// <param name="info">Информация о размерах канваса</param>
        /// <param name="paint">Кисть для рисования</param>
        /// <returns>true, если все параметры валидны, иначе false</returns>
        private static bool AreRenderParamsValid(SKCanvas? canvas, float[]? spectrum, SKImageInfo info, SKPaint? paint) =>
            canvas != null && spectrum != null && spectrum.Length > 0 && paint != null && info.Width > 0 && info.Height > 0;
        #endregion
    }

    #endregion
}