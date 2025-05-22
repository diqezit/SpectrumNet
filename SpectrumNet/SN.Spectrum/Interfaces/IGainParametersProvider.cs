#nullable enable

namespace SpectrumNet.SN.Spectrum.Interfaces;

public interface IGainParametersProvider
{
    float AmplificationFactor { get; set; }
    float MaxDbValue { get; set; }
    float MinDbValue { get; set; }
}
