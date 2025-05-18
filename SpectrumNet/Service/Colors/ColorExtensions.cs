#nullable enable

namespace SpectrumNet.Service.Colors;

public static class ColorExtensions
{
    public static IPalette ToPalette(this KeyValuePair<string, SKColor> colorEntry)
    {
        return new Palette(colorEntry.Key, colorEntry.Value);
    }
}