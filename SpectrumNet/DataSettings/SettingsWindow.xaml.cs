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

    private bool _changesApplied;

    public SettingsWindow()
    {
        CommonResources.InitialiseResources();
        InitializeComponent();
        ThemeManager.Instance.RegisterWindow(this);
        BackupCurrentSettings();
        DataContext = _settings;
    }

    public void EnsureWindowVisible() => Safe(
        () =>
        {
            if (!IsWindowOnScreen())
                ResetWindowPosition();
        },
        new()
        {
            Source = LogPrefix,
            ErrorMessage = "Error ensuring window visibility"
        });

    public void LoadSettings() => Safe(
        () =>
        {
            var settingsPath = GetSettingsFilePath();

            if (!File.Exists(settingsPath))
            {
                Log(
                    LogLevel.Information,
                    LogPrefix,
                    "Settings file not found, using defaults");
                return;
            }

            try
            {
                var loadedSettings = DeserializeSettings(settingsPath);
                if (loadedSettings == null)
                    return;

                ApplyLoadedSettings(loadedSettings);
                Log(
                    LogLevel.Information,
                    LogPrefix,
                    $"Settings loaded from {settingsPath}");
            }
            catch (Exception ex)
            {
                Log(
                    LogLevel.Error,
                    LogPrefix,
                    $"Error loading settings: {ex.Message}. Using defaults.");
            }
        },
        new()
        {
            Source = nameof(LoadSettings),
            ErrorMessage = "Error loading settings"
        });

    public void SaveSettings() => Safe(
        () =>
        {
            UpdateRenderersWithCurrentSettings();

            var settingsPath = EnsureSettingsDirectory();
            var json = SerializeSettings();

            WriteSettingsToFiles(settingsPath, json);

            Log(
                LogLevel.Information,
                LogPrefix,
                $"Settings saved to {settingsPath}");
        },
        new()
        {
            Source = nameof(SaveSettings),
            ErrorMessage = "Error saving settings"
        });

    public void ResetToDefaults() => Safe(
        () =>
        {
            if (!ConfirmSettingsReset())
                return;

            _settings.ResetToDefaults();
            ApplyAndSaveSettings();
            _changesApplied = true;

            ShowSettingsResetConfirmation();
        },
        new()
        {
            Source = nameof(ResetToDefaults),
            ErrorMessage = "Error resetting settings",
            ExceptionHandler = ShowErrorMessage
        });

    private void OnWindowDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void Window_Closing(object sender, CancelEventArgs e) =>
        Safe(
            () =>
            {
                CleanupOnClosing();
                ThemeManager.Instance.UnregisterWindow(this);
            },
            nameof(Window_Closing),
            "Error closing window");

    private void OnCloseButton_Click(object sender, RoutedEventArgs e)
    {
        _changesApplied = false;
        Close();
    }

    private void OnApplyButton_Click(object sender, RoutedEventArgs e) => Safe(
        () =>
        {
            ApplyAndSaveSettings();
            _changesApplied = true;
            ShowSettingsAppliedConfirmation();
        },
        new()
        {
            Source = nameof(OnApplyButton_Click),
            ErrorMessage = "Error applying settings",
            ExceptionHandler = ShowErrorMessage
        });

    private void OnResetButton_Click(object sender, RoutedEventArgs e) => ResetToDefaults();

    private void BackupCurrentSettings() =>
        _originalValues = _settings.GetType()
            .GetProperties()
            .Where(p => p.CanRead && p.CanWrite)
            .ToDictionary(p => p.Name, p => p.GetValue(_settings));

    private void RestoreBackupSettings()
    {
        if (_originalValues == null)
            return;

        foreach (var (key, value) in _originalValues)
            _settings.GetType().GetProperty(key)?.SetValue(_settings, value);

        ThemeManager.Instance.SetTheme(_settings.IsDarkTheme);
        UpdateAllRenderers();
    }

    private void ApplyLoadedSettings(Settings loadedSettings)
    {
        CopyPropertiesFrom(loadedSettings);
        BackupCurrentSettings();
        ThemeManager.Instance.SetTheme(_settings.IsDarkTheme);
        UpdateAllRenderers();
    }

    private static void CopyPropertiesFrom(object source, object target) => Safe(
        () =>
        {
            var properties = source.GetType()
                .GetProperties()
                .Where(p => p.CanRead &&
                       p.GetCustomAttributes(typeof(JsonIgnoreAttribute), true).Length == 0);

            foreach (var prop in properties)
            {
                var targetProp = target.GetType().GetProperty(prop.Name);
                if (targetProp?.CanWrite == true)
                    targetProp.SetValue(target, prop.GetValue(source));
            }
        },
        new()
        {
            Source = LogPrefix,
            ErrorMessage = "Error copying properties"
        });

    private void CopyPropertiesFrom(object source) => CopyPropertiesFrom(source, _settings);

    private void ApplyAndSaveSettings()
    {
        SaveSettings();
        BackupCurrentSettings();
    }

    private void CleanupOnClosing()
    {
        if (_changesApplied)
            SaveSettings();
        else
            RestoreBackupSettings();
    }

    private void UpdateRenderersWithCurrentSettings() => Safe(
        () =>
        {
            foreach (var renderer in GetRenderers())
            {
                var isOverlayActive = renderer.IsOverlayActive;
                renderer.Configure(isOverlayActive);
            }
        },
        new()
        {
            Source = nameof(UpdateRenderersWithCurrentSettings),
            ErrorMessage = "Error updating renderer"
        });

    private void UpdateAllRenderers() => Safe(
        () =>
        {
            foreach (var renderer in GetRenderers())
            {
                var isOverlayActive = renderer.IsOverlayActive;
                renderer.Configure(isOverlayActive, _settings.SelectedRenderQuality);
            }
        },
        new()
        {
            Source = nameof(UpdateAllRenderers),
            ErrorMessage = "Error updating renderers"
        });

    private IEnumerable<ISpectrumRenderer> GetRenderers() => _rendererFactory.GetAllRenderers();

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

    private static Settings? DeserializeSettings(string filePath)
    {
        var content = File.ReadAllText(filePath);
        var loadedSettings = JsonConvert.DeserializeObject<Settings>(content);

        if (loadedSettings == null)
            Log(
                LogLevel.Warning,
                LogPrefix,
                "Failed to deserialize settings. Using defaults.");

        return loadedSettings;
    }

    private string SerializeSettings()
    {
        return JsonConvert.SerializeObject(
            _settings,
            new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                ContractResolver = new DefaultContractResolver
                {
                    NamingStrategy = new CamelCaseNamingStrategy()
                }
            });
    }

    private static void WriteSettingsToFiles(string settingsPath, string json)
    {
        File.WriteAllText(settingsPath, json);
        File.WriteAllText("settings.json", json);
    }

    private bool IsWindowOnScreen()
    {
        return Screen.AllScreens.Any(screen =>
            screen.Bounds.IntersectsWith(
                new System.Drawing.Rectangle(
                    (int)Left,
                    (int)Top,
                    (int)Width,
                    (int)Height)));
    }

    private void ResetWindowPosition()
    {
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Log(
            LogLevel.Warning,
            LogPrefix,
            "Window position reset to center");
    }

    private static bool ConfirmSettingsReset()
    {
        return MessageBox.Show(
            "Are you sure you want to reset all settings to defaults?",
            "Reset Settings",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    private static void ShowSettingsResetConfirmation()
    {
        MessageBox.Show(
            "Settings have been reset to defaults.",
            "Settings Reset",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private static void ShowSettingsAppliedConfirmation()
    {
        MessageBox.Show(
            "Settings applied successfully!",
            "Settings",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private static void ShowErrorMessage(Exception ex)
    {
        MessageBox.Show(
            $"Error: {ex.Message}",
            "Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}