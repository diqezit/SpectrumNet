#nullable enable

using SpectrumNet;

namespace SpectrumNet.SN.Settings.Models;

public class VisualizationSettings
{
    public RenderStyle SelectedRenderStyle { get; set; } = DefaultSettings.SelectedRenderStyle;
    public FftWindowType SelectedFftWindowType { get; set; } = DefaultSettings.SelectedFftWindowType;
    public SpectrumScale SelectedScaleType { get; set; } = DefaultSettings.SelectedScaleType;
    public RenderQuality SelectedRenderQuality { get; set; } = DefaultSettings.SelectedRenderQuality;
    public int BarCount { get; set; } = DefaultSettings.UIBarCount;
    public double BarSpacing { get; set; } = DefaultSettings.UIBarSpacing;
    public string SelectedPalette { get; set; } = DefaultSettings.SelectedPalette;
    public bool ShowPerformanceInfo { get; set; } = DefaultSettings.ShowPerformanceInfo;
}
