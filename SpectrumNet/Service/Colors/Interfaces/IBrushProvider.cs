#nullable enable

namespace SpectrumNet.Service.Colors.Interfaces;

public interface IBrushProvider
{
    (SKColor Color, SKPaint Brush) GetColorAndBrush(string paletteName);
    IReadOnlyDictionary<string, Palette> RegisteredPalettes { get; }
}