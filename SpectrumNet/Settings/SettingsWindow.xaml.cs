#nullable enable

using Newtonsoft.Json.Serialization;
using MessageBox = System.Windows.MessageBox;

namespace SpectrumNet
{
    /// <summary>
    /// Represents the settings window for the SpectrumNet application.
    /// This window allows users to view and modify application settings.
    /// </summary>
    /// <remarks>
    /// The SettingsWindow class handles the initialization of resources,
    /// manages the application's theme, and provides functionality to
    /// save, apply, reset, and restore settings.
    /// </remarks>
    public partial class SettingsWindow : System.Windows.Window
    {
        #region Константы
        private const string LogPrefix = "SettingsWindow";

        #endregion

        #region Singleton
        private static readonly Lazy<SettingsWindow> _instance = new(() => new SettingsWindow());
        public static SettingsWindow Instance => _instance.Value;
        #endregion

        #region Поля
        /// <summary>
        /// The application settings instance.
        /// </summary>
        private readonly Settings _settings;

        /// <summary>
        /// Dictionary to store original setting values.
        /// </summary>
        private Dictionary<string, object?>? _originalValues;
        #endregion

        #region Конструктор

        public SettingsWindow()
        {
            SmartLogger.Log(LogLevel.Debug, LogPrefix, "Initializing resources window");
            InitialiseResources();

            SmartLogger.Log(LogLevel.Debug, LogPrefix, "Initializing window");
            InitializeComponent();

            SmartLogger.Log(LogLevel.Debug, LogPrefix, "Initializing theme");
            ThemeManager.Instance.RegisterWindow(this);

            _settings = Settings.Instance;
            SaveOriginalSettings();
            DataContext = _settings;
        }
        #endregion

        #region Settings

        public void EnsureWindowVisible()
        {
            SmartLogger.Safe(() =>
            {
                bool isVisible = false;

                // Проверяем видимость хотя бы на одном из экранов
                foreach (var screen in Screen.AllScreens)
                {
                    var screenBounds = new System.Drawing.Rectangle(
                        screen.Bounds.X,
                        screen.Bounds.Y,
                        screen.Bounds.Width,
                        screen.Bounds.Height
                    );

                    var windowRect = new System.Drawing.Rectangle(
                        (int)Left,
                        (int)Top,
                        (int)Width,
                        (int)Height
                    );

                    if (screenBounds.IntersectsWith(windowRect))
                    {
                        isVisible = true;
                        break;
                    }
                }

                if (!isVisible)
                {
                    // Центрируем окно на экране
                    WindowStartupLocation = WindowStartupLocation.CenterScreen;
                    SmartLogger.Log(LogLevel.Warning, LogPrefix, "Window position reset to center (was outside visible area)");
                }
            }, "SettingsWindow", "Error ensuring window visibility");
        }

        private void CopyPropertiesFrom(object source)
        {
            SmartLogger.Safe(() =>
            {
                // Получаем все свойства, которые можно скопировать
                var properties = source.GetType().GetProperties()
                    .Where(p => p.CanRead &&
                           !p.GetCustomAttributes(typeof(JsonIgnoreAttribute), true).Any());

                foreach (var prop in properties)
                {
                    SmartLogger.Safe(() =>
                    {
                        // Находим свойство с таким же именем в _settings
                        var targetProp = _settings.GetType().GetProperty(prop.Name);
                        if (targetProp != null && targetProp.CanWrite)
                        {
                            var value = prop.GetValue(source);
                            targetProp.SetValue(_settings, value);
                        }
                    }, "SettingsWindow", $"Failed to copy property {prop.Name}");
                }

                SmartLogger.Log(LogLevel.Debug, LogPrefix, "Settings copied successfully");
            }, "SettingsWindow", "Error copying properties");
        }

        public void LoadSettings()
        {
            SmartLogger.Safe(() =>
            {
                string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string settingsPath = Path.Combine(appDataPath, DefaultSettings.APP_FOLDER, DefaultSettings.SETTINGS_FILE);
                bool needsCorrection = false;

                // Проверка существования файла настроек
                if (!File.Exists(settingsPath) && File.Exists("settings.json"))
                {
                    settingsPath = "settings.json";
                    SmartLogger.Log(LogLevel.Information, LogPrefix, "Using settings from current directory");
                }

                if (!File.Exists(settingsPath))
                {
                    SmartLogger.Log(LogLevel.Information, LogPrefix, "Settings file not found, using defaults");
                    ResetToDefaults();
                    return;
                }

                // Десериализация настроек
                string json = File.ReadAllText(settingsPath);
                var loadedSettings = JsonConvert.DeserializeObject<Settings>(json);

                if (loadedSettings == null)
                {
                    SmartLogger.Log(LogLevel.Warning, LogPrefix, "Failed to deserialize settings, resetting to defaults");
                    ResetToDefaults();
                    return;
                }

                // Валидация SelectedPalette
                if (IsInvalidPalette(loadedSettings.SelectedPalette))
                {
                    SmartLogger.Log(LogLevel.Warning, LogPrefix,
                        $"Invalid palette '{loadedSettings.SelectedPalette}' detected. Resetting to default.");
                    loadedSettings.SelectedPalette = DefaultSettings.SelectedPalette;
                    needsCorrection = true;
                }

                // Применение настроек
                CopyPropertiesFrom(loadedSettings);
                SaveOriginalSettings();
                UpdateAllRenderers();

                // Принудительное сохранение исправленных настроек
                if (needsCorrection)
                {
                    SaveSettings();
                    SmartLogger.Log(LogLevel.Warning, LogPrefix, "Corrected settings were saved");
                }

                SmartLogger.Log(LogLevel.Information, LogPrefix, $"Settings loaded from {settingsPath}");
            }, "SettingsWindow", "Critical error loading settings");

            // Восстановление в случае ошибки
            SmartLogger.Safe(() => RecoverFromCorruptedSettings(), "SettingsWindow", "Failed to recover from corrupted settings");
        }

        public void SaveSettings()
        {
            SmartLogger.Safe(() =>
            {
                // Предварительная валидация перед сохранением
                if (IsInvalidPalette(_settings.SelectedPalette))
                {
                    SmartLogger.Log(LogLevel.Warning, LogPrefix,
                        "Attempt to save invalid palette detected. Resetting to default.");
                    _settings.SelectedPalette = DefaultSettings.SelectedPalette;
                }

                UpdateRenderers();

                string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string appFolder = Path.Combine(appDataPath, DefaultSettings.APP_FOLDER);

                Directory.CreateDirectory(appFolder); // Гарантированное создание папки

                string settingsPath = Path.Combine(appFolder, DefaultSettings.SETTINGS_FILE);

                var jsonSettings = new JsonSerializerSettings
                {
                    Formatting = Formatting.Indented,
                    ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() },
                    DefaultValueHandling = DefaultValueHandling.Ignore // Игнорировать значения по умолчанию
                };

                string json = JsonConvert.SerializeObject(_settings, jsonSettings);

                // Атомарная запись с временным файлом
                string tempPath = Path.Combine(appFolder, "temp_settings.json");
                File.WriteAllText(tempPath, json);
                File.Replace(tempPath, settingsPath, null);

                SmartLogger.Log(LogLevel.Information, LogPrefix, $"Settings saved successfully to {settingsPath}");
            }, "SettingsWindow", "Error saving settings");
        }

        private bool IsInvalidPalette(string paletteName)
        {
            return string.IsNullOrWhiteSpace(paletteName)
                   || int.TryParse(paletteName, out _)
                   || !SpectrumBrushes.Instance.RegisteredPalettes.ContainsKey(paletteName);
        }

        public void RecoverFromCorruptedSettings()
        {
            SmartLogger.Safe(() =>
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Initiating settings recovery...");

                string corruptPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    DefaultSettings.APP_FOLDER,
                    "corrupt_settings_backup.json"
                );

                if (File.Exists(DefaultSettings.SETTINGS_FILE))
                {
                    File.Copy(DefaultSettings.SETTINGS_FILE, corruptPath, true);
                    SmartLogger.Log(LogLevel.Warning, LogPrefix, $"Corrupted settings backed up to {corruptPath}");
                }

                _settings.ResetToDefaults();
                SaveSettings();

                // ThemeManager.Instance.ReloadTheme(); // Удалено до реализации метода

                UpdateAllRenderers();

                SmartLogger.Log(LogLevel.Information, LogPrefix, "Settings recovery completed successfully");
            }, "SettingsWindow", "Fatal error during settings recovery");
        }

        public void ResetToDefaults()
        {
            SmartLogger.Safe(() =>
            {
                var result = MessageBox.Show(
                    "Are you sure you want to reset all settings to defaults?",
                    "Reset Settings",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question
                );

                if (result != MessageBoxResult.Yes)
                    return;

                _settings.ResetToDefaults();
                UpdateAllRenderers();
                SaveSettings();
                SaveOriginalSettings();

                MessageBox.Show("Settings have been reset to defaults.", "Settings Reset",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                SmartLogger.Log(LogLevel.Information, LogPrefix, "Settings reset to defaults");
            }, "SettingsWindow", "Error resetting settings");
        }
        #endregion

        #region Приватные методы

        private static void InitialiseResources() =>
            CommonResources.InitialiseResources();

        private void SaveOriginalSettings()
        {
            _originalValues = _settings.GetType().GetProperties()
                .Where(p => p.CanRead && p.CanWrite)
                .ToDictionary(p => p.Name, p => p.GetValue(_settings));
        }

        private void RestoreOriginalSettings()
        {
            SmartLogger.Safe(() =>
            {
                if (_originalValues == null)
                {
                    SmartLogger.Log(LogLevel.Warning, LogPrefix, "Original settings not found");
                    return;
                }

                foreach (var (key, value) in _originalValues)
                    _settings.GetType().GetProperty(key)?.SetValue(_settings, value);

                SmartLogger.Log(LogLevel.Debug, LogPrefix, "Settings restored from original");
            }, "SettingsWindow", "Error restoring original settings");
        }

        private void UpdateRenderers()
        {
            SmartLogger.Log(LogLevel.Information, LogPrefix, "Applying settings to renderers");

            SmartLogger.Safe(() =>
            {
                var renderer = RainParticleRenderer.GetInstance();
                if (renderer != null)
                {
                    var field = renderer.GetType().GetField("_isOverlayActive",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

                    bool isOverlayActive = false;
                    if (field != null)
                    {
                        object? value = field.GetValue(renderer);
                        if (value is bool boolValue)
                        {
                            isOverlayActive = boolValue;
                        }
                    }

                    renderer.Configure(isOverlayActive);
                    SmartLogger.Log(LogLevel.Debug, LogPrefix, "Renderer updated successfully");
                }
            }, "SettingsWindow", "Error updating renderer");
        }

        private void UpdateAllRenderers()
        {
            SmartLogger.Safe(() =>
            {
                foreach (var renderer in SpectrumRendererFactory.GetAllRenderers())
                {
                    SmartLogger.Safe(() =>
                    {
                        bool isOverlayActive = false;
                        var field = renderer.GetType().GetField("_isOverlayActive",
                            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

                        if (field != null)
                        {
                            object? value = field.GetValue(renderer);
                            if (value is bool boolValue)
                                isOverlayActive = boolValue;
                        }

                        renderer.Configure(isOverlayActive, _settings.SelectedRenderQuality);
                    }, "SettingsWindow", $"Failed to update renderer {renderer.GetType().Name}");
                }

                SmartLogger.Log(LogLevel.Debug, LogPrefix, "All renderers updated successfully");
            }, "SettingsWindow", "Error updating renderers");
        }
        #endregion

        #region Обработчики событий
        private void OnWindowDrag(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
                DragMove();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            SmartLogger.Safe(() =>
            {
                SmartLogger.Log(LogLevel.Information, LogPrefix, "Closing settings window");
                SaveSettings();
                ThemeManager.Instance.UnregisterWindow(this);
            }, "SettingsWindow", "Error closing window");
        }

        private void OnCloseButton_Click(object sender, RoutedEventArgs e)
        {
            SmartLogger.Log(LogLevel.Debug, LogPrefix, "Close button clicked");
            RestoreOriginalSettings();
            ThemeManager.Instance.UnregisterWindow(this);
            Close();
        }

        private void OnApplyButton_Click(object sender, RoutedEventArgs e)
        {
            SmartLogger.Safe(() =>
            {
                SmartLogger.Log(LogLevel.Debug, LogPrefix, "Applying settings");
                SaveSettings();
                SaveOriginalSettings();

                MessageBox.Show("Settings applied successfully!", "Settings",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                SmartLogger.Log(LogLevel.Information, LogPrefix, "Settings applied successfully");
            }, "SettingsWindow", "Error applying settings");
        }

        private void OnResetButton_Click(object sender, RoutedEventArgs e)
        {
            ResetToDefaults();
        }
        #endregion
    }
}