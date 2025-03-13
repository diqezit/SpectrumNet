#nullable enable

namespace SpectrumNet
{
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
        SpectrumAnalyzer Analyzer { get; set; }
        int BarCount { get; set; }
        double BarSpacing { get; set; }
        bool CanStartCapture { get; }
        Dispatcher Dispatcher { get; }
        GainParameters GainParameters { get; }
        bool IsOverlayActive { get; }
        bool IsRecording { get; set; }
        bool IsTransitioning { get; } 
        RenderQuality RenderQuality { get; set; }
        void OnPropertyChanged(params string[] propertyNames);
        Renderer? Renderer { get; set; }
        SpectrumScale ScaleType { get; set; }
        RenderStyle SelectedDrawingType { get; set; }
        string SelectedStyle { get; set; }
        SKElement SpectrumCanvas { get; }
        SpectrumBrushes SpectrumStyles { get; }
        FftWindowType WindowType { get; set; }
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
        void Initialize();
        void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info, float barWidth,
                    float barSpacing, int barCount, SKPaint? paint,
                    Action<SKCanvas, SKImageInfo> drawPerformanceInfo);
        void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium);
        RenderQuality Quality { get; set; }
    }
}