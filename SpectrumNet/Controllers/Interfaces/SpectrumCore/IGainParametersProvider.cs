#nullable enable

namespace SpectrumNet.Controllers.Interfaces.SpectrumCore;

public interface IGainParametersProvider
{
    float AmplificationFactor { get; }
    float MaxDbValue { get; }
    float MinDbValue { get; }
}
