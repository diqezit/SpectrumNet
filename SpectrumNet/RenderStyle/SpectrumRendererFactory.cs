namespace SpectrumNet
{
    #region Structs

    /// <summary>
    /// Структура для хранения кэшированных значений, используемых при рендеринге спектра.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct RenderCache
    {
        /// <summary>
        /// Ширина области рендеринга.
        /// </summary>
        public float Width;
        /// <summary>
        /// Высота области рендеринга.
        /// </summary>
        public float Height;
        /// <summary>
        /// Нижняя граница отображения на екране (для динамических частиц , корректируеться при оверлее).
        /// </summary>
        public float LowerBound;
        /// <summary>
        /// Верхняя граница спектрального диапазона (аналогично).
        /// </summary>
        public float UpperBound;
        /// <summary>
        /// Размер шага для дискретизации спектра.
        /// </summary>
        public float StepSize;
        /// <summary>
        /// Высота наложения для оврелея.
        /// </summary>
        public float OverlayHeight;
    }

    #endregion

    #region Enums

    /// <summary>
    /// Перечисление, определяющее различные стили рендеринга спектра.
    /// </summary>
    public enum RenderStyle
    {
        AsciiDonut,
        Bars,
        CircularBars,
        CircularWave,
        Cubes,
        Fire,
        Gauge,
        GradientWave,
        Heartbeat,
        Loudness,
        Particles,
        Raindrops,
        Rainbow,
        SphereRenderer,
        TextParticles,
        Waveform,
    }

    #endregion

    #region Interfaces

    /// <summary>
    /// Интерфейс, определяющий контракт для классов, выполняющих рендеринг спектра.
    /// </summary>
    public interface ISpectrumRenderer : IDisposable
    {
        /// <summary>
        /// Инициализирует рендерер.
        /// </summary>
        void Initialize();
        /// <summary>
        /// Выполняет рендеринг спектра на заданном канвасе.
        /// </summary>
        /// <param name="canvas">Канвас SkiaSharp для рисования.</param>
        /// <param name="spectrum">Массив float значений спектра.</param>
        /// <param name="info">Информация о изображении SkiaSharp.</param>
        /// <param name="barWidth">Ширина столбцов спектра.</param>
        /// <param name="barSpacing">Расстояние между столбцами спектра.</param>
        /// <param name="barCount">Количество столбцов спектра.</param>
        /// <param name="paint">Объект SKPaint для стилизации рендеринга через public sealed class SpectrumBrushes.</param>
        /// <param name="drawPerformanceInfo">Рисования информации о производительности на канве.</param>
        void Render(SKCanvas canvas, float[] spectrum, SKImageInfo info, float barWidth,
                    float barSpacing, int barCount, SKPaint paint, Action<SKCanvas, SKImageInfo> drawPerformanceInfo);
        /// <summary>
        /// Конфигурирует рендерер, например, для активации или деактивации режима наложения при включенном оверлее на весь екран.
        /// </summary>
        /// <param name="isOverlayActive">Указывает, активен ли режим наложения.</param>
        void Configure(bool isOverlayActive);
    }

    #endregion

    #region Factory Classes

    /// <summary>
    /// Фабрика для создания экземпляров рендереров спектра в зависимости от выбранного стиля выбраного из главного окна.
    /// Обеспечивает кэширование и переиспользование экземпляров рендереров.
    /// </summary>
    public static class SpectrumRendererFactory
    {
        private static readonly object _lock = new();
        private static readonly Dictionary<RenderStyle, ISpectrumRenderer> _rendererCache = new();
        private static readonly HashSet<RenderStyle> _initializedRenderers = new();

        /// <summary>
        /// Создает или получает экземпляр рендерера спектра заданного стиля.
        /// Использует кэширование для переиспользования существующих экземпляров.
        /// </summary>
        /// <param name="style">Стиль рендеринга спектра.</param>
        /// <param name="isOverlayActive">Указывает, активен ли режим наложения для рендерера.</param>
        /// <returns>Экземпляр ISpectrumRenderer для заданного стиля.</returns>
        public static ISpectrumRenderer CreateRenderer(RenderStyle style, bool isOverlayActive)
        {
            if (_rendererCache.TryGetValue(style, out var cachedRenderer))
            {
                cachedRenderer.Configure(isOverlayActive);
                return cachedRenderer;
            }

            lock (_lock)
            {
                if (_rendererCache.TryGetValue(style, out cachedRenderer))
                {
                    cachedRenderer.Configure(isOverlayActive);
                    return cachedRenderer;
                }

                var renderer = (ISpectrumRenderer)(style switch
                {
                    RenderStyle.AsciiDonut => AsciiDonutRenderer.GetInstance(),
                    RenderStyle.Bars => BarsRenderer.GetInstance(),
                    RenderStyle.CircularBars => CircularBarsRenderer.GetInstance(),
                    RenderStyle.CircularWave => CircularWaveRenderer.GetInstance(),
                    RenderStyle.Cubes => CubesRenderer.GetInstance(),
                    RenderStyle.Fire => FireRenderer.GetInstance(),
                    RenderStyle.Gauge => GaugeRenderer.GetInstance(),
                    RenderStyle.GradientWave => GradientWaveRenderer.GetInstance(),
                    RenderStyle.Heartbeat => HeartbeatRenderer.GetInstance(),
                    RenderStyle.Loudness => LoudnessMeterRenderer.GetInstance(),
                    RenderStyle.Particles => ParticlesRenderer.GetInstance(),
                    RenderStyle.Raindrops => RaindropsRenderer.GetInstance(),
                    RenderStyle.Rainbow => RainbowRenderer.GetInstance(),
                    RenderStyle.SphereRenderer => SphereRenderer.GetInstance(),
                    RenderStyle.TextParticles => TextParticlesRenderer.GetInstance(),
                    RenderStyle.Waveform => WaveformRenderer.GetInstance(),
                    _ => throw new ArgumentException($"Unknown render style: {style}")
                });

                if (!_initializedRenderers.Contains(style))
                {
                    renderer.Initialize();
                    _initializedRenderers.Add(style);
                }

                renderer.Configure(isOverlayActive);
                _rendererCache[style] = renderer;

                return renderer;
            }
        }
    }
    #endregion
}