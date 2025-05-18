#nullable enable

namespace SpectrumNet.Service.Colors;

public class ColorDefinitionProvider : IColorDefinitionProvider
{
    public IReadOnlyDictionary<string, SKColor> GetColorDefinitions() => Colors.ColorDefinitions;
}