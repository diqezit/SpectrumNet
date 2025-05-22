#nullable enable

namespace SpectrumNet.SN.Shared.Colors;

public class ColorDefinitionProvider : IColorDefinitionProvider
{
    public IReadOnlyDictionary<string, SKColor> GetColorDefinitions() => Colors.ColorDefinitions;
}