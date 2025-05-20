#nullable enable

namespace SpectrumNet.Controllers.Interfaces.ViewCore;

public interface IVisualizationSettingsManager
{
    int BarCount { get; set; }
    double BarSpacing { get; set; }
    RenderQuality RenderQuality { get; set; }
    RenderStyle SelectedDrawingType { get; set; }
    SpectrumScale ScaleType { get; set; }
    string SelectedStyle { get; set; }
    bool ShowPerformanceInfo { get; set; }
    void InitializeAfterRendererCreated();
}