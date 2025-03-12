#nullable enable

namespace SpectrumNet;

/// <summary>
/// Interface defining the contract for an audio spectrum visualization controller.
/// This interface provides properties and methods for managing spectrum rendering,
/// analysis settings, and audio capture state.
/// <br/>
/// Интерфейс, определяющий контракт для контроллера визуализации аудио спектра.
/// Этот интерфейс предоставляет свойства и методы для управления рендерингом спектра,
/// настройками анализа и состоянием захвата аудио.
/// </summary>
public interface IAudioVisualizationController : INotifyPropertyChanged
{
    /// <summary>
    /// Gets or sets the spectrum analyzer used for processing audio data.
    /// <br/>
    /// Получает или задает анализатор спектра, используемый для обработки аудиоданных.
    /// </summary>
    SpectrumAnalyzer Analyzer { get; set; }

    /// <summary>
    /// Gets or sets the number of bars for spectrum visualization.
    /// <br/>
    /// Получает или задает количество баров для визуализации спектра.
    /// </summary>
    int BarCount { get; set; }

    /// <summary>
    /// Gets or sets the spacing between bars for spectrum visualization.
    /// <br/>
    /// Получает или задает расстояние между барами для визуализации спектра.
    /// </summary>
    double BarSpacing { get; set; }

    /// <summary>
    /// Gets a value indicating whether audio capture can be started (e.g., if a capture device is available and not in use).
    /// <br/>
    /// Получает значение, указывающее, может ли быть начат захват аудио (например, если устройство захвата доступно и не используется).
    /// </summary>
    bool CanStartCapture { get; }

    /// <summary>
    /// Gets the UI thread dispatcher used to execute actions on the UI thread.
    /// <br/>
    /// Получает диспетчер потока пользовательского интерфейса, используемый для выполнения действий в UI потоке.
    /// </summary>
    Dispatcher Dispatcher { get; }

    /// <summary>
    /// Gets the gain parameters used for spectrum normalization and scaling.
    /// <br/>
    /// Получает параметры усиления, используемые для нормализации и масштабирования спектра.
    /// </summary>
    GainParameters GainParameters { get; }

    /// <summary>
    /// Gets a value indicating whether the overlay mode (transparent window) is active.
    /// <br/>
    /// Получает значение, указывающее, активен ли режим оверлея (прозрачного окна).
    /// </summary>
    bool IsOverlayActive { get; }

    /// <summary>
    /// Gets or sets a value indicating whether audio recording is currently in progress.
    /// <br/>
    /// Получает или задает значение, указывающее, выполняется ли в данный момент запись аудио.
    /// </summary>
    bool IsRecording { get; set; }

    /// <summary>
    /// Gets a value indicating whether the controller is in a transition state (e.g., when changing FFT window type or spectrum scale).
    /// <br/>
    /// Получает значение, указывающее, находится ли контроллер в состоянии перехода (например, при изменении типа FFT окна или шкалы спектра).
    /// </summary>
    bool IsTransitioning { get; }

    /// <summary>
    /// Gets or sets the quality level for spectrum rendering.
    /// <br/>
    /// Получает или задает уровень качества отрисовки спектра.
    /// </summary>
    RenderQuality RenderQuality { get; set; }

    /// <summary>
    /// Notifies listeners that a property value has changed, implementing the INotifyPropertyChanged interface.
    /// <br/>
    /// Уведомляет слушателей об изменении значения свойства, реализуя интерфейс INotifyPropertyChanged.
    /// <param name="propertyNames">The names of the properties that have changed. - Имена измененных свойств.</param>
    /// </summary>
    void OnPropertyChanged(params string[] propertyNames);

    /// <summary>
    /// Gets or sets the renderer used for drawing the spectrum.
    /// <br/>
    /// Получает или задает рендерер, используемый для отрисовки спектра.
    /// </summary>
    Renderer? Renderer { get; set; }

    /// <summary>
    /// Gets or sets the spectrum scale type (e.g., linear, logarithmic).
    /// <br/>
    /// Получает или задает тип шкалы спектра (например, линейная, логарифмическая).
    /// </summary>
    SpectrumScale ScaleType { get; set; }

    /// <summary>
    /// Gets or sets the selected drawing type of the spectrum (e.g., bars, lines).
    /// <br/>
    /// Получает или задает выбранный тип отрисовки спектра (например, бары, линии).
    /// </summary>
    RenderStyle SelectedDrawingType { get; set; }

    /// <summary>
    /// Gets or sets the selected visualization style of the spectrum (e.g., gradient, solid color).
    /// <br/>
    /// Получает или задает выбранный стиль визуализации спектра (например, градиент, сплошной цвет).
    /// </summary>
    string SelectedStyle { get; set; }

    /// <summary>
    /// Gets the SkiaSharp canvas element on which the spectrum is rendered.
    /// <br/>
    /// Получает SkiaSharp элемент холста, на котором отрисовывается спектр.
    /// </summary>
    SKElement SpectrumCanvas { get; }

    /// <summary>
    /// Gets the collection of brushes and styles available for spectrum visualization.
    /// <br/>
    /// Получает коллекцию кистей и стилей, доступных для визуализации спектра.
    /// </summary>
    SpectrumBrushes SpectrumStyles { get; }

    /// <summary>
    /// Gets or sets the FFT (Fast Fourier Transform) window type used for spectrum analysis.
    /// <br/>
    /// Получает или задает тип окна FFT (Fast Fourier Transform), используемый для анализа спектра.
    /// </summary>
    FftWindowType WindowType { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether performance information should be displayed.
    /// <br/>
    /// Получает или задает значение, указывающее, следует ли отображать информацию о производительности.
    /// </summary>
    bool ShowPerformanceInfo { get; set; }
}


/// <summary>
/// Enumeration of rendering quality levels.
/// </summary>
public enum RenderQuality
{
    Low,
    Medium,
    High
}

/// <summary>
/// Interface for classes that perform spectrum rendering.
/// </summary>
public interface ISpectrumRenderer : IDisposable
{
    /// <summary>Initializes the renderer.</summary>
    void Initialize();

    /// <summary>Renders the spectrum on the given canvas.</summary>
    /// <param name="canvas">Canvas for drawing.</param>
    /// <param name="spectrum">Array of spectrum values.</param>
    /// <param name="info">Image information.</param>
    /// <param name="barWidth">Width of bars.</param>
    /// <param name="barSpacing">Spacing between bars.</param>
    /// <param name="barCount">Number of bars.</param>
    /// <param name="paint">Object for styling the rendering.</param>
    /// <param name="drawPerformanceInfo">Method for drawing performance information.</param>
    void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info, float barWidth,
                float barSpacing, int barCount, SKPaint? paint,
                Action<SKCanvas, SKImageInfo> drawPerformanceInfo);

    /// <summary>Configures the renderer for overlay mode and quality settings.</summary>
    /// <param name="isOverlayActive">Whether overlay mode is active.</param>
    /// <param name="quality">Rendering quality level.</param>
    void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium);

    /// <summary>Gets or sets the current rendering quality.</summary>
    RenderQuality Quality { get; set; }
}