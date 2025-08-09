#nullable enable

namespace SpectrumNet.SN.Shared.Colors.Interfaces;

public interface IBrushProvider
{
    (SKColor Color, SKPaint Brush) GetColorAndBrush(string paletteName);
    IReadOnlyDictionary<string, Palette> RegisteredPalettes { get; }
}