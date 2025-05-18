#nullable enable

namespace SpectrumNet.Controllers.SpectrumCore;

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