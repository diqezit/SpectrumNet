#nullable enable

namespace SpectrumNet.SN.Spectrum;

public class SettingsGainParametersProvider(
    ISettings settings) : IGainParametersProvider
{
    private readonly ISettings _settings = settings ??
        throw new ArgumentNullException(nameof(settings));

    public float AmplificationFactor
    {
        get => _settings.Audio.AmplificationFactor;
        set => _settings.Audio.AmplificationFactor = value;
    }

    public float MaxDbValue
    {
        get => _settings.Audio.MaxDbLevel;
        set => _settings.Audio.MaxDbLevel = value;
    }

    public float MinDbValue
    {
        get => _settings.Audio.MinDbLevel;
        set => _settings.Audio.MinDbLevel = value;
    }
}