#nullable enable

namespace SpectrumNet.DataSettings;

public class SettingsProvider : ISettingsProvider
{
    private static readonly Lazy<SettingsProvider> _instance = new(() => new SettingsProvider());
    public static SettingsProvider Instance => _instance.Value;

    private readonly ISettings _settings;
    private readonly IGainParametersProvider _gainParametersProvider;

    public ISettings Settings => _settings;
    public IGainParametersProvider GainParameters => _gainParametersProvider;

    private SettingsProvider()
    {
        _settings = DataSettings.Settings.Instance;
        _gainParametersProvider = new SettingsGainParametersProvider(_settings);
    }
}