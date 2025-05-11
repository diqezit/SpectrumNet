#nullable enable

namespace SpectrumNet;

public partial class SettingsWindow : Window
{
    private const string LogPrefix = "SettingsWindow";
    private static readonly Lazy<SettingsWindow> _instance = new(() => new SettingsWindow());
    public static SettingsWindow Instance => _instance.Value;

    private readonly Settings _settings = Settings.Instance;
    private Dictionary<string, object?>? _originalValues;
    private readonly IRendererFactory _rendererFactory = RendererFactory.Instance;

    public SettingsWindow()
    {
        CommonResources.InitialiseResources();
        InitializeComponent();
        ThemeManager.Instance.RegisterWindow(this);
        SaveOriginalSettings();
        DataContext = _settings;
    }

    public void EnsureWindowVisible() => Safe(() =>
    {
        if (!Screen.AllScreens.Any(screen => screen.Bounds.IntersectsWith(new System.Drawing.Rectangle(
            (int)Left, (int)Top, (int)Width, (int)Height))))
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Log(LogLevel.Warning, LogPrefix, "Window position reset to center");
        }
    }, new()
    {
        Source = LogPrefix,
        ErrorMessage = "Error ensuring window visibility"
    });

    private void CopyPropertiesFrom(object source) => Safe(() =>
    {
        source.GetType().GetProperties()
            .Where(p => p.CanRead && p.GetCustomAttributes(typeof(JsonIgnoreAttribute), true).Length == 0)
            .ToList()
            .ForEach(prop =>
            {
                var targetProp = _settings.GetType().GetProperty(prop.Name);
                if (targetProp?.CanWrite == true)
                    targetProp.SetValue(_settings, prop.GetValue(source));
            });
    }, new() { Source = LogPrefix, ErrorMessage = "Error copying properties" });

    public void LoadSettings() => Safe(() =>
    {
        string appDataPath = GetFolderPath(SpecialFolder.ApplicationData);

        string settingsPath = Path.Combine(
            appDataPath,
            DefaultSettings.APP_FOLDER,
            DefaultSettings.SETTINGS_FILE);

        if (!File.Exists(settingsPath) && File.Exists("settings.json"))
            settingsPath = "settings.json";

        if (!File.Exists(settingsPath))
        {
            Log(LogLevel.Information, LogPrefix, "Settings file not found, using defaults");
            return;
        }

        try
        {
            var loadedSettings = JsonConvert.DeserializeObject<Settings>(File.ReadAllText(settingsPath));
            if (loadedSettings == null)
            {
                Log(LogLevel.Warning, LogPrefix, "Failed to deserialize settings. Using defaults.");
                return;
            }

            CopyPropertiesFrom(loadedSettings);
            SaveOriginalSettings();

            ThemeManager.Instance.SetTheme(_settings.IsDarkTheme);

            UpdateAllRenderers();
            Log(LogLevel.Information, LogPrefix, $"Settings loaded from {settingsPath}");
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, LogPrefix, $"Error loading settings: {ex.Message}. Using defaults.");
        }
    }, new() { Source = nameof(LoadSettings), ErrorMessage = "Error loading settings" });

    public void SaveSettings() => Safe(() =>
    {
        UpdateRenderers();

        string appDataPath = GetFolderPath(SpecialFolder.ApplicationData);
        string appFolder = Path.Combine(appDataPath, DefaultSettings.APP_FOLDER);

        if (!Directory.Exists(appFolder))
            Directory.CreateDirectory(appFolder);

        string settingsPath = Path.Combine(appFolder, DefaultSettings.SETTINGS_FILE);

        string json = JsonConvert.SerializeObject(_settings, new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() }
        });

        File.WriteAllText(settingsPath, json);
        File.WriteAllText("settings.json", json);

        Log(
            LogLevel.Information,
            LogPrefix,
            $"Settings saved to {settingsPath}");

    }, new() { Source = nameof(SaveSettings), ErrorMessage = "Error saving settings" });

    public void ResetToDefaults() => Safe(() =>
    {
        if (MessageBox.Show(
            "Are you sure you want to reset all settings to defaults?",
            "Reset Settings",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) != MessageBoxResult.Yes)

            return;

        _settings.ResetToDefaults();
        UpdateAllRenderers();
        SaveSettings();
        SaveOriginalSettings();

        MessageBox.Show(
            "Settings have been reset to defaults.",
            "Settings Reset",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

    }, new()
    {
        Source = nameof(ResetToDefaults),
        ErrorMessage = "Error resetting settings",

        ExceptionHandler = ex => MessageBox.Show(
            $"Error resetting settings: {ex.Message}",
            "Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error)
    });

    private void SaveOriginalSettings() =>
        _originalValues = _settings.GetType().GetProperties()
            .Where(p => p.CanRead && p.CanWrite)
            .ToDictionary(p => p.Name, p => p.GetValue(_settings));

    private void RestoreOriginalSettings()
    {
        if (_originalValues == null)
            return;

        foreach (var (key, value) in _originalValues)
            _settings.GetType().GetProperty(key)?.SetValue(_settings, value);

        ThemeManager.Instance.SetTheme(_settings.IsDarkTheme);
        UpdateAllRenderers();
    }

    private void UpdateRenderers() => Safe(() =>
    {
        var renderers = _rendererFactory.GetAllRenderers();
        foreach (var renderer in renderers)
        {
            bool isOverlayActive = renderer.IsOverlayActive;
            renderer.Configure(isOverlayActive);
        }
    }, new() { Source = nameof(UpdateRenderers), ErrorMessage = "Error updating renderer" });

    private void UpdateAllRenderers() => Safe(() =>
    {
        var renderers = _rendererFactory.GetAllRenderers();
        foreach (var renderer in renderers)
        {
            bool isOverlayActive = renderer.IsOverlayActive;
            renderer.Configure(isOverlayActive, _settings.SelectedRenderQuality);
        }
    }, new() { Source = nameof(UpdateAllRenderers), ErrorMessage = "Error updating renderers" });

    private void OnWindowDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void Window_Closing(object sender, CancelEventArgs e) =>
        Safe(() =>
        {
            SaveSettings();
            ThemeManager.Instance.UnregisterWindow(this);
        },
        nameof(Window_Closing),
        "Error closing window");

    private void OnCloseButton_Click(object sender, RoutedEventArgs e)
    {
        RestoreOriginalSettings();
        ThemeManager.Instance.UnregisterWindow(this);
        Close();
    }

    private void OnApplyButton_Click(object sender, RoutedEventArgs e) => Safe(() =>
    {
        SaveSettings();
        SaveOriginalSettings();

        MessageBox.Show(
            "Settings applied successfully!",
            "Settings",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

    }, new()
    {
        Source = nameof(OnApplyButton_Click),
        ErrorMessage = "Error applying settings",
        ExceptionHandler = ex => MessageBox.Show(
            $"Error applying settings: {ex.Message}",
            "Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error)
    });

    private void OnResetButton_Click(object sender, RoutedEventArgs e) => ResetToDefaults();
}