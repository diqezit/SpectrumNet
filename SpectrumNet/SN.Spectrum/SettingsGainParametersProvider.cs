#nullable enable

namespace SpectrumNet.SN.Spectrum;

public class SettingsGainParametersProvider(
    ISettings settings) : IGainParametersProvider
{
    private readonly ISettings _settings = settings ??
        throw new ArgumentNullException(nameof(settings));

    public float AmplificationFactor
    {
        get => _settings.UIAmplificationFactor;
        set => _settings.UIAmplificationFactor = value;
    }

    public float MaxDbValue
    {
        get => _settings.UIMaxDbLevel;
        set => _settings.UIMaxDbLevel = value;
    }

    public float MinDbValue
    {
        get => _settings.UIMinDbLevel;
        set => _settings.UIMinDbLevel = value;
    }
}