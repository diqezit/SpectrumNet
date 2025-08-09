#nullable enable

using SpectrumNet.SN.Settings.Constants;

namespace SpectrumNet.SN.Settings;

public class Settings : ISettings
{
    private static readonly Lazy<Settings> _instance = new(() => new());
    public static Settings Instance => _instance.Value;

    private ParticleSettings _particles = new();
    private RaindropSettings _raindrops = new();
    private WindowSettings _window = new();
    private VisualizationSettings _visualization = new();
    private AudioSettings _audio = new();
    private GeneralSettings _general = new();
    private KeyBindingSettings _keyBindings = new();

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

    public KeyBindingSettings KeyBindings
    {
        get => _keyBindings;
        set => SetProperty(ref _keyBindings, value);
    }

    [JsonIgnore]
    private PropertyChangedEventHandler? _propertyChanged;

    public event PropertyChangedEventHandler? PropertyChanged
    {
        add => _propertyChanged += value;
        remove => _propertyChanged -= value;
    }

    public event EventHandler<string>? SettingsChanged;

    public void LoadSettings(string? filePath = null)
    {
        string settingsPath = filePath ?? GetSettingsFilePath();

        if (!File.Exists(settingsPath))
            return;

        var content = File.ReadAllText(settingsPath);
        var jsonSettings = new JsonSerializerSettings
        {
            Converters = new JsonConverter[] { new StringEnumConverter() }
        };

        var loadedSettings = JsonConvert.DeserializeObject<Settings>(content, jsonSettings);
        if (loadedSettings != null)
        {
            ApplySettings(loadedSettings);
            ValidateKeyBindings();
            SettingsChanged?.Invoke(this, "LoadSettings");
        }
    }

    public void SaveSettings(string? filePath = null)
    {
        string settingsPath = filePath ?? EnsureSettingsDirectory();

        var json = JsonConvert.SerializeObject(this, new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy()
            },
            Converters = new JsonConverter[] { new StringEnumConverter() }
        });

        File.WriteAllText(settingsPath, json);
        File.WriteAllText("settings.json", json);

        SettingsChanged?.Invoke(this, "SaveSettings");
    }

    public void ResetToDefaults()
    {
        Particles = new ParticleSettings();
        Raindrops = new RaindropSettings();
        Window = new WindowSettings();
        Visualization = new VisualizationSettings();
        Audio = new AudioSettings();
        General = new GeneralSettings();
        KeyBindings = new KeyBindingSettings();

        SettingsChanged?.Invoke(this, "ResetToDefaults");
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

    private void ApplySettings(Settings source)
    {
        Particles = source.Particles ?? new ParticleSettings();
        Raindrops = source.Raindrops ?? new RaindropSettings();
        Window = source.Window ?? new WindowSettings();
        Visualization = source.Visualization ?? new VisualizationSettings();
        Audio = source.Audio ?? new AudioSettings();
        General = source.General ?? new GeneralSettings();
        KeyBindings = source.KeyBindings ?? new KeyBindingSettings();
    }

    private void ValidateKeyBindings()
    {
        var keyBindingManager = new KeyBindingManager(this);
        keyBindingManager.ValidateAndFixConflicts();
    }

    private static string GetSettingsFilePath()
    {
        string appDataPath = GetFolderPath(SpecialFolder.ApplicationData);
        string settingsPath = Path.Combine(appDataPath, DefaultSettings.APP_FOLDER, DefaultSettings.SETTINGS_FILE);

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