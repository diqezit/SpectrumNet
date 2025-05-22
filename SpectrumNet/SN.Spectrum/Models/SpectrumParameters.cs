#nullable enable

using Constants = SpectrumNet.SN.Spectrum.Utils.Constants;

namespace SpectrumNet.SN.Spectrum.Models;

public record SpectrumParameters(
    float MinDb,
    float DbRange,
    float AmplificationFactor)
{
    public static SpectrumParameters FromProvider(IGainParametersProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        return new(
            provider.MinDbValue,
            Math.Max(provider.MaxDbValue - provider.MinDbValue, Constants.EPSILON),
            provider.AmplificationFactor);
    }
}