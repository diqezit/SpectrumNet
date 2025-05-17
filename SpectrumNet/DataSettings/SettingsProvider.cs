#nullable enable

namespace SpectrumNet.DataSettings;

public interface ISettingsProvider
{
    ISettings Settings { get; }
}

public class SettingsProvider : ISettingsProvider
{
    private static readonly Lazy<SettingsProvider> _instance = new(() => new SettingsProvider());
    public static SettingsProvider Instance => _instance.Value;

    private readonly ISettings _settings;
    public ISettings Settings => _settings;

    private SettingsProvider()
    {
        _settings = DataSettings.Settings.Instance;
    }
}