namespace SpectrumNet
{
    #region Renderers Implementations

    /// <summary>
    /// **BarsRenderer** - реализация рендерера спектра в виде вертикальных столбцов (баров).
    /// <br/>
    /// **BarsRenderer** - spectrum renderer visualizing audio spectrum as vertical bars.
    /// <para>
    /// **Основные характеристики / Key Features:**
    /// </para>
    /// <list type="bullet">
    ///     <item> **Столбчатая диаграмма / Bar Chart:**
    ///          Отображает интенсивность частот в виде вертикальных полос.
    ///          Высота столбца = амплитуде спектра.
    ///          <br/>
    ///          **Bar Chart:**
    ///          Displays frequency intensity via vertical bars.
    ///          Bar height = spectrum amplitude.
    ///     </item>
    ///     <item> **Сглаживание / Smoothing:**
    ///          Уменьшает резкие скачки для плавной визуализации.
    ///          Настройка сглаживания: <see cref="Configure(bool)"/>.
    ///          <br/>
    ///          **Smoothing:**
    ///          Reduces abrupt changes for smooth visuals.
    ///          Smoothing config: <see cref="Configure(bool)"/>.
    ///     </item>
    ///     <item> **Стилизация / Stylization:**
    ///          Настройка: цвет, альфа, радиус углов.
    ///          Эффект белого "блика" на вершинах.
    ///          <br/>
    ///          **Stylization:**
    ///          Configurable: color, alpha, corner radius.
    ///          White "highlight" effect on bar tops.
    ///     </item>
    ///     <item> **Производительность / Performance:**
    ///          Оптимизирован для реального времени.
    ///          <br/>
    ///          **Performance:**
    ///          Optimized for real-time use.
    ///     </item>
    ///     <item> **Singleton:**
    ///          Единственный экземпляр в приложении для оптимизации ресурсов.
    ///           <br/>
    ///          **Singleton:**
    ///          Single instance per app for resource optimization.
    ///     </item>
    /// </list>
    /// <para>
    /// **Создание своих рендереров / Custom Renderer Creation:**
    /// </para>
    /// <para>
    /// Для новых стилей `SpectrumNet`: интерфейс <see cref="ISpectrumRenderer"/> и подходы из:
    /// <see cref="BarsRenderer"/>, <see cref="FireRenderer"/>, <see cref="DotsRenderer"/>, <see cref="CubesRenderer"/>.
    /// <br/>
    /// For new `SpectrumNet` styles: follow <see cref="ISpectrumRenderer"/> and patterns from:
    /// <see cref="BarsRenderer"/>, <see cref="FireRenderer"/>, <see cref="DotsRenderer"/>, <see cref="CubesRenderer"/>.
    /// </para>
    /// <para>
    /// **Основные шаги и рекомендации / Key Steps and Recommendations:**
    /// </para>
    /// <list type="number">
    ///     <item> **Реализация ISpectrumRenderer / Implement ISpectrumRenderer:**
    ///         Реализуйте интерфейс <see cref="ISpectrumRenderer"/> и методы:
    ///         <br/>
    ///         Implement <see cref="ISpectrumRenderer"/> interface and methods:
    ///         <list type="bullet">
    ///             <item> <see cref="Initialize"/>: Инициализация ресурсов (<see cref="SKPaint"/>, <see cref="SKPath"/>, буферы). Однократно при создании.
    ///                  <br/>
    ///                  <see cref="Initialize"/>: Resource init (<see cref="SKPaint"/>, <see cref="SKPath"/>, buffers). Once on creation.</item>
    ///             <item> <see cref="Configure(bool)"/>: Настройка параметров (сглаживание, цвет) в зависимости от overlay. Многократно.
    ///                   <br/>
    ///                   <see cref="Configure(bool)"/>: Parameter config (smoothing, color) based on overlay. Multiple calls.</item>
    ///             <item> <see cref="Render(SKCanvas, float[], SKImageInfo, float, float, int, SKPaint, Action{SKCanvas, SKImageInfo})"/>:
    ///                   **Основной метод**. Визуализация спектра на <see cref="SKCanvas"/>.
    ///                   Параметры: спектр. данные, инфо канваса, параметры отрисовки, базовая кисть, делегат производительности.
    ///                   <br/>
    ///                   <see cref="Render(SKCanvas, float[], SKImageInfo, float, float, int, SKPaint, Action{SKCanvas, SKImageInfo})"/>:
    ///                   **Main method**. Spectrum visualization on <see cref="SKCanvas"/>.
    ///                   Params: spectrum data, canvas info, render params, base paint, performance delegate.</item>
    ///             <item> <see cref="Dispose"/>: Освобождение ресурсов (<see cref="SKPaint"/>, <see cref="SKPath"/>). Dispose pattern.
    ///                  <br/>
    ///                  <see cref="Dispose"/>: Release resources (<see cref="SKPaint"/>, <see cref="SKPath"/>). Dispose pattern.</item>
    ///         </list>
    ///     </item>
    ///     <item> **Singleton Pattern (Рекомендовано) / Singleton Pattern (Recommended):**
    ///         Рендереры как Singleton для оптимизации (как в <see cref="BarsRenderer"/>, <see cref="FireRenderer"/>).
    ///         <br/>
    ///         Singleton renderers for optimization (like <see cref="BarsRenderer"/>, <see cref="FireRenderer"/>).
    ///     </item>
    ///     <item> **Кэширование и Переиспользование / Caching and Reuse:**
    ///         <see cref="SpectrumRendererFactory"/> кэширует рендереры. Обновление состояния при переиспользовании (<see cref="Configure(bool)"/>).
    ///         <br/>
    ///         <see cref="SpectrumRendererFactory"/> caches renderers. Update state on reuse (<see cref="Configure(bool)"/>).
    ///     </item>
    ///     <item> **Параметры Render / Render Parameters:**
    ///         Параметры <see cref="Render(SKCanvas, float[], SKImageInfo, float, float, int, SKPaint, Action{SKCanvas, SKImageInfo})"/>: `spectrum`, `info`, `barWidth`, `barSpacing`, `barCount`, `paint`.
    ///         <br/>
    ///         <see cref="Render(SKCanvas, float[], SKImageInfo, float, float, int, SKPaint, Action{SKCanvas, SKImageInfo})"/> parameters: `spectrum`, `info`, `barWidth`, `barSpacing`, `barCount`, `paint`.
    ///         <list type="bullet">
    ///             <item> `spectrum`: Спектральные данные (амплитуды частот). / Spectral data (frequency amplitudes).</item>
    ///             <item> `info`: Инфо канваса (ширина, высота). / Canvas info (width, height).</item>
    ///             <item> `barWidth`, `barSpacing`, `barCount`: Размер и кол-во элементов. / Size and count of elements.</item>
    ///             <item> `paint`: Базовая кисть для стилизации. / Base paint for styling.</item>
    ///         </list>
    ///     </item>
    ///     <item> **Сглаживание спектра / Spectrum Smoothing:**
    ///         Применение сглаживания для плавности. Пример: экспоненциальное сглаживание.
    ///         <br/>
    ///         Apply smoothing for visual smoothness. Example: exponential smoothing.
    ///     </item>
    ///     <item> **Оптимизация / Optimization:**
    ///         Производительность рендеринга важна. Эффективные методы SkiaSharp, избегать лишних объектов, параллелизм (как в <see cref="FireRenderer"/>).
    ///         <br/>
    ///         Rendering performance is key. Efficient SkiaSharp, avoid extra objects, parallelism (like <see cref="FireRenderer"/>).</item>
    ///     <item> **Визуальные эффекты / Visual Effects:**
    ///         Эксперименты: градиенты, прозрачность, анимации.
    ///         <br/>
    ///         Experiment: gradients, transparency, animations.</item>
    ///     <item> **Конфигурация / Configuration:**
    ///         Настройка через <see cref="Configure(bool)"/> для адаптации пользователем.
    ///         <br/>
    ///         Configuration via <see cref="Configure(bool)"/> for user adaptation.</item>
    ///     <item> **Валидация / Validation:**
    ///         Проверка параметров в начале <see cref="Render(SKCanvas, float[], SKImageInfo, float, float, int, SKPaint, Action{SKCanvas, SKImageInfo})"/> для избежания ошибок.
    ///         <br/>
    ///         Parameter validation at <see cref="Render(SKCanvas, float[], SKImageInfo, float, float, int, SKPaint, Action{SKCanvas, SKImageInfo})"/> start to prevent errors.</item>
    /// </list>
    /// <para>
    /// Изучение <see cref="BarsRenderer"/>, <see cref="FireRenderer"/>, <see cref="DotsRenderer"/>, <see cref="CubesRenderer"/> - лучший способ понять реализацию и лучшие практики.
    /// <br/>
    /// Studying <see cref="BarsRenderer"/>, <see cref="FireRenderer"/>, <see cref="DotsRenderer"/>, <see cref="CubesRenderer"/> is best way to learn implementation and best practices.
    /// </para>
    /// </summary>
    public class BarsRenderer : ISpectrumRenderer, IDisposable
    {
        private static BarsRenderer? _instance;
        private bool _isInitialized;
        private readonly SKPath _path = new();
        private volatile bool _disposed;

        private const float MaxCornerRadius = 10f,
                            HighlightWidthProportion = 0.6f,
                            HighlightHeightProportion = 0.1f,
                            MaxHighlightHeight = 5f,
                            AlphaMultiplier = 1.5f,
                            HighlightAlphaDivisor = 3f,
                            DefaultCornerRadiusFactor = 5.0f;

        private float[]? _previousSpectrum;
        private float _smoothingFactor = 0.3f;

        private BarsRenderer() { }

        /// <summary>
        /// Возвращает единственный экземпляр BarsRenderer (Singleton).
        /// <br/>
        /// Returns the singleton instance of BarsRenderer.
        /// </summary>
        /// <returns>Экземпляр BarsRenderer. / BarsRenderer instance.</returns>
        public static BarsRenderer GetInstance() => _instance ??= new BarsRenderer();

        /// <inheritdoc />
        public void Initialize()
        {
            if (!_isInitialized)
            {
                _isInitialized = true;
                Log.Debug("BarsRenderer initialized");
            }
        }

        /// <inheritdoc />
        public void Configure(bool isOverlayActive)
        {
            _smoothingFactor = isOverlayActive ? 0.5f : 0.3f;
        }

        /// <inheritdoc />
        public void Render(SKCanvas canvas, float[] spectrum, SKImageInfo info,
                            float barWidth, float barSpacing, int barCount, SKPaint paint,
                            Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
        {
            if (!_isInitialized || canvas == null || spectrum == null || spectrum.Length < 2 ||
                paint == null || info.Width <= 0 || info.Height <= 0)
            {
                Log.Warning("Invalid render parameters or renderer not initialized.");
                return;
            }

            int halfSpectrumLength = spectrum.Length / 2;
            int actualBarCount = Math.Min(halfSpectrumLength, barCount);
            float totalBarWidth = barWidth + barSpacing;
            float canvasHeight = info.Height;

            using var barPaint = paint.Clone();
            barPaint.IsAntialias = true;

            using var highlightPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = SKColors.White
            };

            float[] scaledSpectrum = ScaleSpectrum(spectrum, actualBarCount, halfSpectrumLength);
            float[] smoothedSpectrum = SmoothSpectrum(scaledSpectrum, actualBarCount);

            for (int i = 0; i < actualBarCount; i++)
            {
                float barHeight = MathF.Max(smoothedSpectrum[i] * canvasHeight, 1f);
                byte alpha = (byte)MathF.Min(smoothedSpectrum[i] * AlphaMultiplier * 255f, 255f);
                barPaint.Color = barPaint.Color.WithAlpha(alpha);

                float x = i * totalBarWidth;
                float cornerRadius = MathF.Min(barWidth * DefaultCornerRadiusFactor, MaxCornerRadius);
                RenderBar(canvas, x, barWidth, barHeight, canvasHeight, cornerRadius, barPaint);

                if (barHeight > cornerRadius * 2)
                {
                    float highlightWidth = barWidth * HighlightWidthProportion;
                    float highlightHeight = MathF.Min(barHeight * HighlightHeightProportion, MaxHighlightHeight);
                    byte highlightAlpha = (byte)(alpha / HighlightAlphaDivisor);
                    highlightPaint.Color = highlightPaint.Color.WithAlpha(highlightAlpha);

                    RenderHighlight(canvas, x, barWidth, barHeight, canvasHeight, highlightWidth, highlightHeight, highlightPaint);
                }
            }

            drawPerformanceInfo?.Invoke(canvas, info);
        }

        private static float[] ScaleSpectrum(float[] spectrum, int barCount, int halfSpectrumLength)
        {
            float[] scaledSpectrum = new float[barCount];
            float blockSize = (float)halfSpectrumLength / barCount;

            for (int i = 0; i < barCount; i++)
            {
                float sum = 0;
                int start = (int)(i * blockSize);
                int end = (int)((i + 1) * blockSize);
                end = Math.Min(end, halfSpectrumLength);
                for (int j = start; j < end; j++)
                {
                    sum += spectrum[j];
                }
                scaledSpectrum[i] = sum / (end - start);
            }

            return scaledSpectrum;
        }

        private float[] SmoothSpectrum(float[] scaledSpectrum, int actualBarCount)
        {
            if (_previousSpectrum == null || _previousSpectrum.Length != actualBarCount)
            {
                _previousSpectrum = new float[actualBarCount];
            }

            float[] smoothedSpectrum = new float[actualBarCount];

            for (int i = 0; i < actualBarCount; i++)
            {
                smoothedSpectrum[i] = _previousSpectrum[i] * (1 - _smoothingFactor) + scaledSpectrum[i] * _smoothingFactor;
                _previousSpectrum[i] = smoothedSpectrum[i];
            }

            return smoothedSpectrum;
        }

        private void RenderBar(SKCanvas canvas, float x, float barWidth, float barHeight,
                                        float canvasHeight, float cornerRadius, SKPaint barPaint)
        {
            _path.Reset();
            _path.AddRoundRect(new SKRoundRect(new SKRect(x, canvasHeight - barHeight, x + barWidth, canvasHeight), cornerRadius, cornerRadius));
            canvas.DrawPath(_path, barPaint);
        }

        private static void RenderHighlight(SKCanvas canvas, float x, float barWidth, float barHeight,
                                                float canvasHeight, float highlightWidth, float highlightHeight, SKPaint highlightPaint)
        {
            float highlightX = x + (barWidth - highlightWidth) / 2;
            canvas.DrawRect(highlightX, canvasHeight - barHeight, highlightWidth, highlightHeight, highlightPaint);
        }

        /// <inheritdoc />
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing)
            {
                _path.Dispose();
                _previousSpectrum = null;
            }
            _disposed = true;
            Log.Debug("BarsRenderer disposed");
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }

    #endregion

}