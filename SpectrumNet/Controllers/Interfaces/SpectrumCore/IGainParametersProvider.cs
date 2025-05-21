#nullable enable

namespace SpectrumNet.Controllers.Interfaces.SpectrumCore;

public interface IGainParametersProvider
{
    float AmplificationFactor { get; set; } 
    float MaxDbValue { get; set; }
    float MinDbValue { get; set; }
}
