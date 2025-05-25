#nullable enable

namespace SpectrumNet.SN.Settings;

public class Settings : ISettings
{
    private const string LogPrefix = nameof(Settings);
    private readonly ISmartLogger _logger = SmartLogger.Instance;

    private static readonly Lazy<Settings> _instance = new(() => new());
    public static Settings Instance => _instance.Value;

    [JsonIgnore]
    private PropertyChangedEventHandler? _propertyChanged;

    public event PropertyChangedEventHandler? PropertyChanged
    {
        add => _propertyChanged += value;
        remove => _propertyChanged -= value;
    }

    public event EventHandler<string>? SettingsChanged;

    private ParticleSettings _particles = new();
    private RaindropSettings _raindrops = new();
    private WindowSettings _window = new();
    private VisualizationSettings _visualization = new();
    private AudioSettings _audio = new();
    private GeneralSettings _general = new();

    public ParticleSettings Particles
    {
        get => _particles;
        set => SetProperty(ref _particles, value);
    }

    public RaindropSettings Raindrops
    {
        get => _raindrops;
        set => SetProperty(ref _raindrops, value);
    }

    public WindowSettings Window
    {
        get => _window;
        set => SetProperty(ref _window, value);
    }

    public VisualizationSettings Visualization
    {
        get => _visualization;
        set => SetProperty(ref _visualization, value);
    }

    public AudioSettings Audio
    {
        get => _audio;
        set => SetProperty(ref _audio, value);
    }

    public GeneralSettings General
    {
        get => _general;
        set => SetProperty(ref _general, value);
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected virtual void OnPropertyChanged(string propertyName) =>
        _propertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void LoadSettings(string? filePath = null) =>
        _logger.Safe(() =>
        {
            string settingsPath = filePath ?? GetSettingsFilePath();

            if (!File.Exists(settingsPath))
            {
                _logger.Log(LogLevel.Information, LogPrefix, "Settings file not found, using defaults");
                return;
            }

            try
            {
                var content = File.ReadAllText(settingsPath);
                var loadedSettings = JsonConvert.DeserializeObject<Settings>(content);

                if (loadedSettings == null)
                {
                    _logger.Log(LogLevel.Warning, LogPrefix, "Failed to deserialize settings. Using defaults.");
                    return;
                }

                ApplySettings(loadedSettings);
                _logger.Log(LogLevel.Information, LogPrefix, $"Settings loaded from {settingsPath}");
                SettingsChanged?.Invoke(this, "LoadSettings");
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, LogPrefix, $"Error loading settings: {ex.Message}. Using defaults.");
            }
        },
        LogPrefix,
        "Error loading settings");

    public void SaveSettings(string? filePath = null) =>
        _logger.Safe(() =>
        {
            string settingsPath = filePath ?? EnsureSettingsDirectory();

            var json = JsonConvert.SerializeObject(
                this,
                new JsonSerializerSettings
                {
                    Formatting = Formatting.Indented,
                    ContractResolver = new DefaultContractResolver
                    {
                        NamingStrategy = new CamelCaseNamingStrategy()
                    }
                });

            File.WriteAllText(settingsPath, json);
            File.WriteAllText("settings.json", json);

            _logger.Log(LogLevel.Information, LogPrefix, $"Settings saved to {settingsPath}");
            SettingsChanged?.Invoke(this, "SaveSettings");
        },
        LogPrefix,
        "Error saving settings");

    public void ResetToDefaults() =>
        _logger.Safe(() =>
        {
            Particles = new ParticleSettings();
            Raindrops = new RaindropSettings();
            Window = new WindowSettings();
            Visualization = new VisualizationSettings();
            Audio = new AudioSettings();
            General = new GeneralSettings();

            _logger.Log(LogLevel.Information, LogPrefix, "Settings have been reset to defaults");
            SettingsChanged?.Invoke(this, "ResetToDefaults");
        },
        LogPrefix,
        "Error resetting settings to defaults");

    private void ApplySettings(Settings source)
    {
        Particles = source.Particles ?? new ParticleSettings();
        Raindrops = source.Raindrops ?? new RaindropSettings();
        Window = source.Window ?? new WindowSettings();
        Visualization = source.Visualization ?? new VisualizationSettings();
        Audio = source.Audio ?? new AudioSettings();
        General = source.General ?? new GeneralSettings();
    }

    private static string GetSettingsFilePath()
    {
        string appDataPath = GetFolderPath(SpecialFolder.ApplicationData);
        string settingsPath = Path.Combine(
            appDataPath,
            DefaultSettings.APP_FOLDER,
            DefaultSettings.SETTINGS_FILE);

        if (!File.Exists(settingsPath) && File.Exists("settings.json"))
            settingsPath = "settings.json";

        return settingsPath;
    }

    private static string EnsureSettingsDirectory()
    {
        string appDataPath = GetFolderPath(SpecialFolder.ApplicationData);
        string appFolder = Path.Combine(appDataPath, DefaultSettings.APP_FOLDER);

        if (!Directory.Exists(appFolder))
            Directory.CreateDirectory(appFolder);

        return Path.Combine(appFolder, DefaultSettings.SETTINGS_FILE);
    }
}