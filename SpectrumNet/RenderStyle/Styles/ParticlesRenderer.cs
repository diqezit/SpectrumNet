#nullable enable

namespace SpectrumNet
{
    /// <summary>
    /// Реализует рендерер спектра, который визуализирует аудиоданные в виде частиц.
    /// Рендерер использует кольцевой буфер для управления частицами и оптимизирован для производительности.
    /// </summary>
    public sealed class ParticlesRenderer : ISpectrumRenderer, IDisposable
    {
        #region Instance
        /// <summary>
        /// Возвращает синглтон экземпляр класса ParticlesRenderer.
        /// Реализована ленивый способ инициализации для потокобезопасного создания экземпляра.
        /// </summary>
        private static readonly Lazy<ParticlesRenderer> _lazyInstance = new(() =>
            new ParticlesRenderer(), System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>
        /// Получает синглтон экземпляр ParticlesRenderer.
        /// </summary>
        public static ParticlesRenderer GetInstance() => _lazyInstance.Value;
        #endregion

        #region Fields
        // Теперь вместо поля _random будем использовать thread-local генератор, что повышает производительность.
        [System.ThreadStatic]
        private static System.Random? _threadLocalRandom;
        private const int VelocityLookupSize = 1024;
        private CircularParticleBuffer? _particleBuffer;
        private bool _isOverlayActive, _isInitialized, _isDisposed;
        private RenderCache _renderCache = new();
        private readonly float _velocityRange, _particleLife, _particleLifeDecay,
            _alphaDecayExponent, _spawnThresholdOverlay, _spawnThresholdNormal,
            _spawnProbability, _particleSizeOverlay, _particleSizeNormal,
            _velocityMultiplier;
        private float[]? _spectrumBuffer, _velocityLookup, _alphaCurve;
        #endregion

        #region Constructor
        /// <summary>
        /// Инициализирует новый экземпляр ParticlesRenderer.
        /// Закрытый конструктор для реализации паттерна синглтон.
        /// Инициализирует свойства частиц из глобальных настроек.
        /// </summary>
        private ParticlesRenderer()
        {
            var s = Settings.Instance;
            _velocityRange = s.ParticleVelocityMax - s.ParticleVelocityMin;
            _particleLife = s.ParticleLife;
            _particleLifeDecay = s.ParticleLifeDecay;
            _alphaDecayExponent = s.AlphaDecayExponent;
            _spawnThresholdOverlay = s.SpawnThresholdOverlay;
            _spawnThresholdNormal = s.SpawnThresholdNormal;
            _spawnProbability = s.SpawnProbability;
            _particleSizeOverlay = s.ParticleSizeOverlay;
            _particleSizeNormal = s.ParticleSizeNormal;
            _velocityMultiplier = s.VelocityMultiplier;
            PrecomputeAlphaCurve();
            InitializeVelocityLookup(s.ParticleVelocityMin);
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Инициализирует рендерер частиц. Метод должен быть вызван перед началом рендеринга.
        /// Выделяет и настраивает кольцевой буфер частиц.
        /// </summary>
        /// <exception cref="System.ObjectDisposedException">Выбрасывается, если рендерер уже освобождён.</exception>
        public void Initialize()
        {
            if (_isDisposed) throw new System.ObjectDisposedException(nameof(ParticlesRenderer));
            if (_isInitialized) return;
            _particleBuffer = new CircularParticleBuffer((int)Settings.Instance.MaxParticles, _particleLife, _particleLifeDecay, _velocityMultiplier);
            _isInitialized = true;
        }

        /// <summary>
        /// Конфигурирует рендерер для режима overlay или обычного режима.
        /// Обновляет размер частиц в зависимости от режима.
        /// </summary>
        /// <param name="isOverlayActive">True для активного overlay режима, false для обычного режима.</param>
        public void Configure(bool isOverlayActive)
        {
            if (_isOverlayActive != isOverlayActive)
            {
                _isOverlayActive = isOverlayActive;
                UpdateParticleSizes();
            }
        }

        /// <summary>
        /// Рендерит частицы на основе переданных спектральных данных.
        /// Обновляет позиции частиц, порождает новые и отрисовывает их на канве.
        /// </summary>
        /// <param name="canvas">Канва SkiaSharp для отрисовки.</param>
        /// <param name="spectrum">Аудиоспектр в виде массива float.</param>
        /// <param name="info">Информация об изображении SkiaSharp.</param>
        /// <param name="barWidth">Ширина каждого столбца спектра (не используется напрямую в данном рендерере).</param>
        /// <param name="barSpacing">Интервал между столбцами спектра (не используется напрямую в данном рендерере).</param>
        /// <param name="barCount">Количество столбцов спектра (используется для расчёта шага).</param>
        /// <param name="paint">Объект SKPaint для стилизации частиц.</param>
        /// <param name="drawPerformanceInfo">Делегат для отрисовки информации о производительности (опционально).</param>
        public void Render(
            SKCanvas? canvas,
            float[]? spectrum,
            SKImageInfo info,
            float barWidth,
            float barSpacing,
            int barCount,
            SKPaint? paint,
            System.Action<SKCanvas, SKImageInfo>? drawPerformanceInfo)
        {
            if (!ValidateRenderParameters(canvas, spectrum, info, paint, drawPerformanceInfo))
                return;

            int width = info.Width;
            int height = info.Height;
            _renderCache.Width = width;
            _renderCache.Height = height;
            UpdateRenderCacheBounds(height);
            _renderCache.StepSize = barCount > 0 ? width / barCount : 0f;

            float upperBound = _renderCache.UpperBound;
            float lowerBound = _renderCache.LowerBound;

            UpdateParticles(upperBound);
            SpawnNewParticles(spectrum.AsSpan(0, System.Math.Min(spectrum!.Length, 2048)), lowerBound, width, barWidth);
            RenderParticles(canvas!, paint!, upperBound, lowerBound);

            drawPerformanceInfo!(canvas!, info);
        }

        /// <summary>
        /// Освобождает ресурсы, используемые ParticlesRenderer.
        /// Освобождает арендованные массивы и сбрасывает поля.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            System.GC.SuppressFinalize(this);
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Предварительно вычисляет кривую прозрачности для эффекта затухания частиц.
        /// Сохраняет значения альфа-канала в массиве _alphaCurve на основе заданного экспоненциального закона.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void PrecomputeAlphaCurve()
        {
            if (_alphaCurve == null)
                _alphaCurve = System.Buffers.ArrayPool<float>.Shared.Rent(101);

            float step = 1f / (_alphaCurve.Length - 1);
            for (int i = 0; i < _alphaCurve.Length; i++)
                _alphaCurve[i] = (float)System.Math.Pow(i * step, _alphaDecayExponent);
        }

        /// <summary>
        /// Инициализирует таблицу поиска для скоростей частиц.
        /// Заполняет массив _velocityLookup значениями скоростей в заданном диапазоне.
        /// </summary>
        /// <param name="minVelocity">Минимальная скорость частиц.</param>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void InitializeVelocityLookup(float minVelocity)
        {
            if (_velocityLookup == null)
                _velocityLookup = System.Buffers.ArrayPool<float>.Shared.Rent(VelocityLookupSize);

            for (int i = 0; i < VelocityLookupSize; i++)
            {
                _velocityLookup[i] = minVelocity + _velocityRange * i / VelocityLookupSize;
            }
        }

        /// <summary>
        /// Обновляет позиции и значения альфа у частиц в буфере.
        /// Удаляет частицы, достигшие верхней границы или полностью затухшие.
        /// </summary>
        /// <param name="upperBound">Верхняя вертикальная граница движения частиц.</param>
        private void UpdateParticles(float upperBound)
        {
            if (_particleBuffer == null) return;
            _particleBuffer.Update(upperBound, AlphaCurve);
        }

        /// <summary>
        /// Порождает новые частицы на основе спектральных данных.
        /// Частицы порождаются, если значение спектра превышает порог и с определённой вероятностью.
        /// </summary>
        /// <param name="spectrum">Спектральные данные.</param>
        /// <param name="spawnY">Вертикальная позиция для порождения частиц.</param>
        /// <param name="canvasWidth">Ширина канвы.</param>
        /// <param name="barWidth">Ширина каждого столбца спектра.</param>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void SpawnNewParticles(System.ReadOnlySpan<float> spectrum, float spawnY, int canvasWidth, float barWidth)
        {
            if (_particleBuffer == null) return;
            float threshold = _isOverlayActive ? _spawnThresholdOverlay : _spawnThresholdNormal;
            float baseSize = _isOverlayActive ? _particleSizeOverlay : _particleSizeNormal;
            int targetCount = System.Math.Min(spectrum.Length / 2, 2048);
            var spectrumBufferSpan = SpectrumBuffer.AsSpan(0, targetCount);
            ScaleSpectrum(spectrum, spectrumBufferSpan);
            float xStep = _renderCache.StepSize;

            // Используем thread-local генератор случайных чисел вместо общего поля _random.
            var rnd = _threadLocalRandom ??= new System.Random();
            for (int i = 0; i < targetCount; i++)
            {
                float spectrumValue = spectrumBufferSpan[i];
                if (spectrumValue <= threshold) continue;

                float densityFactor = System.MathF.Min(spectrumValue / threshold, 3f);
                if (rnd.NextDouble() >= densityFactor * _spawnProbability) continue;

                _particleBuffer.Add(new Particle
                {
                    X = i * xStep + (float)rnd.NextDouble() * barWidth,
                    Y = spawnY,
                    Velocity = GetRandomVelocity() * densityFactor,
                    Size = baseSize * densityFactor,
                    Life = _particleLife,
                    Alpha = 1f,
                    IsActive = true
                });
            }
        }

        /// <summary>
        /// Обновляет верхнюю и нижнюю границы кеша отрисовки на основе активности overlay и настроек.
        /// Эти границы определяют видимую область для частиц.
        /// </summary>
        /// <param name="height">Высота области отрисовки.</param>
        private void UpdateRenderCacheBounds(float height)
        {
            var settings = Settings.Instance;
            float overlayHeight = height * settings.OverlayHeightMultiplier;

            if (_isOverlayActive)
            {
                _renderCache.UpperBound = height - overlayHeight;
                _renderCache.LowerBound = height;
                _renderCache.OverlayHeight = overlayHeight;
            }
            else
            {
                _renderCache.UpperBound = 0f;
                _renderCache.LowerBound = height;
                _renderCache.OverlayHeight = 0f;
            }
        }

        /// <summary>
        /// Валидирует параметры, передаваемые в метод Render.
        /// Проверяет, что рендерер инициализирован, не освобождён и что обязательные параметры заданы корректно.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private bool ValidateRenderParameters(
            SKCanvas? canvas,
            float[]? spectrum,
            SKImageInfo info,
            SKPaint? paint,
            System.Action<SKCanvas, SKImageInfo>? drawPerformanceInfo)
            => !_isDisposed && _isInitialized && canvas != null && spectrum != null && spectrum.Length >= 2
                && paint != null && drawPerformanceInfo != null && info.Width > 0 && info.Height > 0;

        /// <summary>
        /// Возвращает случайную скорость для движения частиц из таблицы поиска.
        /// Использует thread-local генератор случайных чисел для повышения производительности.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private float GetRandomVelocity()
        {
            var rnd = _threadLocalRandom ??= new System.Random();
            return VelocityLookup[rnd.Next(VelocityLookupSize)] * _velocityMultiplier;
        }

        /// <summary>
        /// Масштабирует исходные спектральные данные для подгона под размер целевого span.
        /// Используется для уменьшения объёма данных спектра при порождении частиц.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static void ScaleSpectrum(System.ReadOnlySpan<float> source, System.Span<float> dest)
        {
            int srcLen = source.Length / 2, destLen = dest.Length;
            if (srcLen == 0 || destLen == 0) return;

            float scale = srcLen / (float)destLen;
            for (int i = 0; i < destLen; i++)
                dest[i] = source[(int)(i * scale)];
        }

        /// <summary>
        /// Обновляет размер всех активных частиц в буфере в зависимости от текущего режима overlay.
        /// Сохраняет относительный коэффициент изменения размера при переключении режимов.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void UpdateParticleSizes()
        {
            if (_particleBuffer == null) return;
            float baseSize = _isOverlayActive ? _particleSizeOverlay : _particleSizeNormal;
            float oldBaseSize = _isOverlayActive ? _particleSizeNormal : _particleSizeOverlay;
            foreach (ref var particle in _particleBuffer.GetActiveParticles())
            {
                float relativeSizeFactor = particle.Size / oldBaseSize;
                particle.Size = baseSize * relativeSizeFactor;
            }
        }

        /// <summary>
        /// Рендерит активные частицы из буфера на канву.
        /// Отрисовываются только частицы, находящиеся в пределах заданных верхней и нижней границ.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void RenderParticles(SKCanvas canvas, SKPaint paint, float upperBound, float lowerBound)
        {
            if (_particleBuffer == null) return;
            var activeParticles = _particleBuffer.GetActiveParticles();
            int count = activeParticles.Length;
            if (count == 0) return;
            paint.Style = SKPaintStyle.Fill;
            paint.StrokeCap = SKStrokeCap.Round;
            paint.StrokeWidth = 0;

            for (int i = 0; i < count; i++)
            {
                ref readonly var particle = ref activeParticles[i];
                if (particle.Y < upperBound || particle.Y > lowerBound) continue;

                paint.Color = paint.Color.WithAlpha((byte)(particle.Alpha * 255));
                canvas.DrawCircle(particle.X, particle.Y, particle.Size / 2, paint);
            }
        }
        #endregion

        #region Dispose Helpers
        /// <summary>
        /// Освобождает управляемые ресурсы, используемые ParticlesRenderer.
        /// Возвращает арендованные массивы и сбрасывает состояние.
        /// </summary>
        /// <param name="disposing">True, если вызван из метода Dispose(), false – из финализатора.</param>
        private void Dispose(bool disposing)
        {
            if (_isDisposed)
                return;

            if (disposing)
            {
                if (_spectrumBuffer != null)
                {
                    System.Buffers.ArrayPool<float>.Shared.Return(_spectrumBuffer);
                    _spectrumBuffer = null;
                }
                if (_velocityLookup != null)
                {
                    System.Buffers.ArrayPool<float>.Shared.Return(_velocityLookup);
                    _velocityLookup = null;
                }
                if (_alphaCurve != null)
                {
                    System.Buffers.ArrayPool<float>.Shared.Return(_alphaCurve);
                    _alphaCurve = null;
                }
                _particleBuffer = null;
                _renderCache = new RenderCache();
            }

            _isInitialized = false;
            _isDisposed = true;
        }
        #endregion

        #region Nested Classes
        /// <summary>
        /// Реализует кольцевой буфер для эффективного управления и обновления частиц.
        /// Переиспользует ячейки для минимизации сборки мусора и повышения производительности.
        /// </summary>
        private class CircularParticleBuffer
        {
            private Particle[] _buffer;
            private int _head;
            private int _tail;
            private int _count;
            private readonly int _capacity;
            private readonly float _particleLife;
            private readonly float _particleLifeDecay;
            private readonly float _velocityMultiplier;

            /// <summary>
            /// Инициализирует новый экземпляр класса CircularParticleBuffer.
            /// </summary>
            /// <param name="capacity">Максимальное число частиц, которое может содержать буфер.</param>
            /// <param name="particleLife">Начальная продолжительность жизни частицы.</param>
            /// <param name="particleLifeDecay">Скорость уменьшения жизни частицы за кадр.</param>
            /// <param name="velocityMultiplier">Множитель скорости частицы.</param>
            public CircularParticleBuffer(int capacity, float particleLife, float particleLifeDecay, float velocityMultiplier)
            {
                _capacity = capacity;
                _buffer = new Particle[capacity];
                _particleLife = particleLife;
                _particleLifeDecay = particleLifeDecay;
                _velocityMultiplier = velocityMultiplier;
            }

            /// <summary>
            /// Добавляет новую частицу в буфер. Если буфер заполнен, новая частица не добавляется.
            /// </summary>
            /// <param name="particle">Добавляемая частица.</param>
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
            public void Add(Particle particle)
            {
                if (_count < _capacity)
                {
                    _buffer[_tail] = particle;
                    _tail = (_tail + 1) % _capacity;
                    _count++;
                }
            }

            /// <summary>
            /// Возвращает span активных частиц в буфере.
            /// Активными считаются частицы, которые должны отрисовываться.
            /// </summary>
            public System.Span<Particle> GetActiveParticles() => _buffer.AsSpan(_head, _count);

            /// <summary>
            /// Обновляет состояние частиц в буфере.
            /// Перемещает частицы, обновляет их жизнь и альфа-канал, а также управляет индексами буфера.
            /// Оптимизация: цикл разделён на непрерывные и разрывные блоки, чтобы избежать лишних операций взятия по модулю.
            /// </summary>
            /// <param name="upperBound">Верхняя граница для движения частиц.</param>
            /// <param name="alphaCurve">Предвычисленная кривая альфа для затухания частиц.</param>
            public void Update(float upperBound, float[] alphaCurve)
            {
                int writeIndex = 0;
                int curveLength = alphaCurve.Length;
                int maxCurveIndex = curveLength - 1;
                float invParticleLife = 1f / _particleLife;
                float velocityMultiplier = _velocityMultiplier;
                float particleLifeDecay = _particleLifeDecay;
                float sizeMultiplier = 0.99f;

                if (_count == 0) return;

                int endIndex = (_head + _count) % _capacity;

                if (_head < endIndex)
                {
                    // Непрерывный блок
                    for (int i = _head; i < endIndex; i++)
                    {
                        ref var particle = ref _buffer[i];
                        if (particle.IsActive && particle.Y >= upperBound)
                        {
                            particle.Y -= particle.Velocity * velocityMultiplier;
                            particle.Life -= particleLifeDecay;

                            float lifeRatio = particle.Life * invParticleLife;
                            int index = (int)(lifeRatio * maxCurveIndex);
                            index = index < 0 ? 0 : index >= curveLength ? maxCurveIndex : index;
                            particle.Alpha = alphaCurve[index];

                            particle.Size *= sizeMultiplier;
                            _buffer[writeIndex++] = particle;
                        }
                    }
                }
                else
                {
                    // Разрывный блок: от _head до конца массива...
                    for (int i = _head; i < _capacity; i++)
                    {
                        ref var particle = ref _buffer[i];
                        if (particle.IsActive && particle.Y >= upperBound)
                        {
                            particle.Y -= particle.Velocity * velocityMultiplier;
                            particle.Life -= particleLifeDecay;

                            float lifeRatio = particle.Life * invParticleLife;
                            int index = (int)(lifeRatio * maxCurveIndex);
                            index = index < 0 ? 0 : index >= curveLength ? maxCurveIndex : index;
                            particle.Alpha = alphaCurve[index];

                            particle.Size *= sizeMultiplier;
                            _buffer[writeIndex++] = particle;
                        }
                    }
                    // ...и затем от начала массива до endIndex
                    for (int i = 0; i < endIndex; i++)
                    {
                        ref var particle = ref _buffer[i];
                        if (particle.IsActive && particle.Y >= upperBound)
                        {
                            particle.Y -= particle.Velocity * velocityMultiplier;
                            particle.Life -= particleLifeDecay;

                            float lifeRatio = particle.Life * invParticleLife;
                            int index = (int)(lifeRatio * maxCurveIndex);
                            index = index < 0 ? 0 : index >= curveLength ? maxCurveIndex : index;
                            particle.Alpha = alphaCurve[index];

                            particle.Size *= sizeMultiplier;
                            _buffer[writeIndex++] = particle;
                        }
                    }
                }

                _count = writeIndex;
                _head = 0;
                _tail = writeIndex;
            }

            /// <summary>
            /// Возвращает вместимость буфера частиц.
            /// </summary>
            public int Capacity => _capacity;
        }
        #endregion

        #region Particle Struct
        /// <summary>
        /// Представляет одну частицу с её свойствами.
        /// Структура используется для повышения производительности за счёт отсутствия размещения в куче для каждой частицы.
        /// </summary>
        private struct Particle
        {
            /// <summary>
            /// Горизонтальная позиция частицы.
            /// </summary>
            public float X { get; set; }
            /// <summary>
            /// Вертикальная позиция частицы.
            /// </summary>
            public float Y { get; set; }
            /// <summary>
            /// Скорость частицы.
            /// </summary>
            public float Velocity { get; set; }
            /// <summary>
            /// Размер частицы.
            /// </summary>
            public float Size { get; set; }
            /// <summary>
            /// Оставшаяся жизнь частицы.
            /// </summary>
            public float Life { get; set; }
            /// <summary>
            /// Значение альфа (непрозрачность) частицы.
            /// </summary>
            public float Alpha { get; set; }
            /// <summary>
            /// Флаг активности частицы (если true – частица отрисовывается).
            /// </summary>
            public bool IsActive { get; set; }
        }
        #endregion

        #region Properties for Lazy Initialization
        /// <summary>
        /// Свойство для получения буфера спектра с ленивой инициализацией.
        /// Использует ArrayPool для эффективного управления памятью.
        /// </summary>
        private float[] SpectrumBuffer => _spectrumBuffer ??= System.Buffers.ArrayPool<float>.Shared.Rent(2048);

        /// <summary>
        /// Свойство для получения таблицы скоростей с ленивой инициализацией.
        /// Использует ArrayPool для эффективного управления памятью.
        /// </summary>
        private float[] VelocityLookup => _velocityLookup ??= System.Buffers.ArrayPool<float>.Shared.Rent(VelocityLookupSize);

        /// <summary>
        /// Свойство для получения массива кривой альфа с ленивой инициализацией.
        /// Использует ArrayPool для эффективного управления памятью.
        /// </summary>
        private float[] AlphaCurve => _alphaCurve ??= System.Buffers.ArrayPool<float>.Shared.Rent(101);
        #endregion

        #region RenderCache Struct
        /// <summary>
        /// Структура для хранения данных кеша отрисовки.
        /// </summary>
        private struct RenderCache
        {
            /// <summary>
            /// Ширина области отрисовки.
            /// </summary>
            public int Width { get; set; }
            /// <summary>
            /// Высота области отрисовки.
            /// </summary>
            public int Height { get; set; }
            /// <summary>
            /// Верхняя граница отрисовки частиц.
            /// </summary>
            public float UpperBound { get; set; }
            /// <summary>
            /// Нижняя граница отрисовки частиц.
            /// </summary>
            public float LowerBound { get; set; }
            /// <summary>
            /// Высота области overlay.
            /// </summary>
            public float OverlayHeight { get; set; }
            /// <summary>
            /// Шаг для порождения частиц.
            /// </summary>
            public float StepSize { get; set; }
        }
        #endregion
    }
}
