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
/// <summary>
/// Единый интерфейс для контроллера визуализации аудио спектра.
/// </summary>
public interface IAudioVisualizationController : INotifyPropertyChanged, IDisposable
{
    // Базовые свойства
    Dispatcher Dispatcher { get; }
    SpectrumAnalyzer Analyzer { get; set; }
    Renderer? Renderer { get; set; }
    SKElement SpectrumCanvas { get; }
    SpectrumBrushes SpectrumStyles { get; }
    GainParameters GainParameters { get; }

    // Настройки визуализации
    int BarCount { get; set; }
    double BarSpacing { get; set; }
    RenderQuality RenderQuality { get; set; }
    RenderStyle SelectedDrawingType { get; set; }
    SpectrumScale ScaleType { get; set; }
    FftWindowType WindowType { get; set; }
    float MinDbLevel { get; set; }
    float MaxDbLevel { get; set; }
    float AmplificationFactor { get; set; }
    bool ShowPerformanceInfo { get; set; }

    // Управление захватом звука
    bool IsRecording { get; set; }
    bool CanStartCapture { get; }

    // Управление оверлеем
    bool IsOverlayActive { get; set; }
    bool IsOverlayTopmost { get; set; }

    // Управление окном
    bool IsMaximized { get; }

    // Управление всплывающими окнами
    bool IsPopupOpen { get; set; }
    bool IsControlPanelOpen { get; }

    // Управление стилями
    string SelectedStyle { get; set; }
    IReadOnlyDictionary<string, Palette> AvailablePalettes { get; }
    Palette? SelectedPalette { get; set; }

    // Управление состоянием
    bool IsTransitioning { get; set; }

    // Свойства для ComboBox
    IEnumerable<RenderStyle> AvailableDrawingTypes { get; }
    IEnumerable<FftWindowType> AvailableFftWindowTypes { get; }
    IEnumerable<SpectrumScale> AvailableScaleTypes { get; }
    IEnumerable<RenderQuality> AvailableRenderQualities { get; }

    // Методы визуализации
    void RequestRender();
    void UpdateRenderDimensions(int width, int height);
    void SynchronizeVisualization();
    void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs? e);
    SpectrumAnalyzer? GetCurrentAnalyzer();

    // Методы управления захватом
    Task StartCaptureAsync();
    Task StopCaptureAsync();
    Task ToggleCaptureAsync();

    // Методы управления оверлеем
    void OpenOverlay();
    void CloseOverlay();

    // Методы управления окном
    void MinimizeWindow();
    void MaximizeWindow();
    void CloseWindow();

    // Методы управления панелью
    void OpenControlPanel();
    void CloseControlPanel();
    void ToggleControlPanel();

    // Методы UI
    void ToggleTheme();
    bool HandleKeyDown(KeyEventArgs e, IInputElement? focusedElement);

    // Управление ресурсами
    void DisposeResources();
    void OnPropertyChanged(params string[] propertyNames);
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
    void Initialize();
    void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info, float barWidth,
                float barSpacing, int barCount, SKPaint? paint,
                Action<SKCanvas, SKImageInfo> drawPerformanceInfo);
    void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium);
    RenderQuality Quality { get; set; }
}