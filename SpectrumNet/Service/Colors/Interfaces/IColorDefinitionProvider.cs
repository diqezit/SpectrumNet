#nullable enable

namespace SpectrumNet.Service.Colors.Interfaces;

public interface IColorDefinitionProvider
{
    IReadOnlyDictionary<string, SKColor> GetColorDefinitions();
}