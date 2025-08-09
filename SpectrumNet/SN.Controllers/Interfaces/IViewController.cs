#nullable enable

using SpectrumNet.SN.Spectrum.Core;
using SpectrumNet.SN.Visualization.Core;

namespace SpectrumNet.SN.Controllers.Interfaces;

public interface IViewController
{
    // Методы визуализации
    void RequestRender();
    void UpdateRenderDimensions(int width, int height);
    void SynchronizeVisualization();
    void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs? e);

    // Свойства визуализации
    Renderer? Renderer { get; set; }
    SpectrumAnalyzer Analyzer { get; set; }
    SKElement SpectrumCanvas { get; }
    SpectrumBrushes SpectrumStyles { get; }

    // Настройки визуализации
    int BarCount { get; set; }
    double BarSpacing { get; set; }
    RenderQuality RenderQuality { get; set; }
    RenderStyle SelectedDrawingType { get; set; }
    SpectrumScale ScaleType { get; set; }
    string SelectedStyle { get; set; }
    Palette? SelectedPalette { get; set; }
    bool ShowPerformanceInfo { get; set; }

    // Доступные стили
    IReadOnlyDictionary<string, Palette> AvailablePalettes { get; }
    IEnumerable<RenderStyle> AvailableDrawingTypes { get; }
    IEnumerable<FftWindowType> AvailableFftWindowTypes { get; }
    IEnumerable<SpectrumScale> AvailableScaleTypes { get; }
    IEnumerable<RenderQuality> AvailableRenderQualities { get; }
    IEnumerable<RenderStyle> OrderedDrawingTypes { get; }

    //  для переключения рендеров
    void SelectNextRenderer();
    void SelectPreviousRenderer();
}