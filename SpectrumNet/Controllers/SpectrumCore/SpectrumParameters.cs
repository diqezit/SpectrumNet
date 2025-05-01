#nullable enable

namespace SpectrumNet.Controllers.SpectrumCore;
public record SpectrumParameters(
    float MinDb,
    float DbRange,
    float AmplificationFactor
)
{
    public static SpectrumParameters FromProvider(IGainParametersProvider? provider) =>
        provider is null
            ? throw new ArgumentNullException(nameof(provider))
            : new SpectrumParameters(
                provider.MinDbValue,
                Max(provider.MaxDbValue - provider.MinDbValue, Constants.Epsilon),
                provider.AmplificationFactor
            );
}
