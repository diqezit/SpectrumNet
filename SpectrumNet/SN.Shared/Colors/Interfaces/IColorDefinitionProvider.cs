#nullable enable

namespace SpectrumNet.SN.Shared.Colors.Interfaces;

public interface IColorDefinitionProvider
{
    IReadOnlyDictionary<string, SKColor> GetColorDefinitions();
}