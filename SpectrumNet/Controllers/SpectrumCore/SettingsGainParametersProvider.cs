#nullable enable

namespace SpectrumNet.Controllers.SpectrumCore;

public class SettingsGainParametersProvider(
    ISettings settings) : IGainParametersProvider
{
    private readonly ISettings _settings = settings ?? 
        throw new ArgumentNullException(nameof(settings));

    public float AmplificationFactor => _settings.UIAmplificationFactor;
    public float MaxDbValue => _settings.UIMaxDbLevel;
    public float MinDbValue => _settings.UIMinDbLevel;
}