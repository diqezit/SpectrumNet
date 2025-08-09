// SpectrumNet/Controllers/Interfaces/ViewCore/IStylesProvider.cs
#nullable enable

namespace SpectrumNet.SN.Controllers.View.Interfaces;

public interface IStylesProvider
{
    IReadOnlyDictionary<string, Palette> AvailablePalettes { get; }
    IEnumerable<RenderStyle> AvailableDrawingTypes { get; }
    IEnumerable<FftWindowType> AvailableFftWindowTypes { get; }
    IEnumerable<SpectrumScale> AvailableScaleTypes { get; }
    IEnumerable<RenderQuality> AvailableRenderQualities { get; }
    IEnumerable<RenderStyle> OrderedDrawingTypes { get; }
    Palette? SelectedPalette { get; set; }
}