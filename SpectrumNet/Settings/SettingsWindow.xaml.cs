#nullable enable

using System.Windows.Forms;
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
    public partial class SettingsWindow : Window
    {
        #region Константы
        private const string LogPrefix = "[SettingsWindow] ";
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
            try
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
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error ensuring window visibility: {ex.Message}");
            }
        }

        private void CopyPropertiesFrom(object source)
        {
            try
            {
                // Получаем все свойства, которые можно скопировать
                var properties = source.GetType().GetProperties()
                    .Where(p => p.CanRead &&
                           !p.GetCustomAttributes(typeof(JsonIgnoreAttribute), true).Any());

                foreach (var prop in properties)
                {
                    try
                    {
                        // Находим свойство с таким же именем в _settings
                        var targetProp = _settings.GetType().GetProperty(prop.Name);
                        if (targetProp != null && targetProp.CanWrite)
                        {
                            var value = prop.GetValue(source);
                            targetProp.SetValue(_settings, value);
                        }
                    }
                    catch (Exception ex)
                    {
                        SmartLogger.Log(LogLevel.Warning, LogPrefix, $"Failed to copy property {prop.Name}: {ex.Message}");
                    }
                }

                SmartLogger.Log(LogLevel.Debug, LogPrefix, "Settings copied successfully");
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error copying properties: {ex.Message}");
            }
        }

        public void LoadSettings()
        {
            try
            {
                string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string settingsPath = Path.Combine(appDataPath, DefaultSettings.APP_FOLDER, DefaultSettings.SETTINGS_FILE);

                // Если файла нет в AppData, проверяем текущую директорию (для обратной совместимости)
                if (!File.Exists(settingsPath) && File.Exists("settings.json"))
                {
                    settingsPath = "settings.json";
                    SmartLogger.Log(LogLevel.Information, LogPrefix, "Using settings from current directory");
                }

                if (!File.Exists(settingsPath))
                {
                    SmartLogger.Log(LogLevel.Information, LogPrefix, "Settings file not found, using defaults");
                    return;
                }

                string json = File.ReadAllText(settingsPath);
                var loadedSettings = JsonConvert.DeserializeObject<Settings>(json);

                if (loadedSettings == null)
                {
                    SmartLogger.Log(LogLevel.Warning, LogPrefix, "Failed to deserialize settings, using defaults");
                    return;
                }

                CopyPropertiesFrom(loadedSettings);
                SaveOriginalSettings();
                UpdateAllRenderers();

                SmartLogger.Log(LogLevel.Information, LogPrefix, $"Settings loaded from {settingsPath}");
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error loading settings: {ex.Message}");
            }
        }

        public void SaveSettings()
        {
            try
            {
                UpdateRenderers();
                string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string appFolder = Path.Combine(appDataPath, DefaultSettings.APP_FOLDER);

                if (!Directory.Exists(appFolder))
                    Directory.CreateDirectory(appFolder);

                string settingsPath = Path.Combine(appFolder, DefaultSettings.SETTINGS_FILE);

                var jsonSettings = new JsonSerializerSettings
                {
                    Formatting = Formatting.Indented,
                    ContractResolver = new DefaultContractResolver
                    {
                        NamingStrategy = new CamelCaseNamingStrategy()
                    }
                };

                string json = JsonConvert.SerializeObject(_settings, jsonSettings);
                File.WriteAllText(settingsPath, json);
                File.WriteAllText("settings.json", json);

                SmartLogger.Log(LogLevel.Information, LogPrefix, $"Settings saved successfully to {settingsPath}");
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error saving settings: {ex.Message}");
            }
        }

        public void ResetToDefaults()
        {
            try
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
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error resetting settings: {ex.Message}");
                MessageBox.Show($"Error resetting settings: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
            if (_originalValues == null)
            {
                SmartLogger.Log(LogLevel.Warning, LogPrefix, "Original settings not found");
                return;
            }

            foreach (var (key, value) in _originalValues)
                _settings.GetType().GetProperty(key)?.SetValue(_settings, value);

            SmartLogger.Log(LogLevel.Debug, LogPrefix, "Settings restored from original");
        }

        private void UpdateRenderers()
        {
            SmartLogger.Log(LogLevel.Information, LogPrefix, "Applying settings to renderers");

            try
            {
                var renderer = RaindropsRenderer.GetInstance();
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
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error updating renderer: {ex.Message}");
            }
        }

        private void UpdateAllRenderers()
        {
            try
            {
                foreach (var renderer in SpectrumRendererFactory.GetAllRenderers())
                {
                    try
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
                    }
                    catch (Exception ex)
                    {
                        SmartLogger.Log(LogLevel.Warning, LogPrefix, $"Failed to update renderer {renderer.GetType().Name}: {ex.Message}");
                    }
                }

                SmartLogger.Log(LogLevel.Debug, LogPrefix, "All renderers updated successfully");
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error updating renderers: {ex.Message}");
            }
        }
        #endregion

        #region Обработчики событий
        private void OnWindowDrag(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            try
            {
                SmartLogger.Log(LogLevel.Information, LogPrefix, "Closing settings window");
                SaveSettings();
                ThemeManager.Instance.UnregisterWindow(this);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error closing window: {ex.Message}");
            }
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
            try
            {
                SmartLogger.Log(LogLevel.Debug, LogPrefix, "Applying settings");
                SaveSettings();
                SaveOriginalSettings();

                MessageBox.Show("Settings applied successfully!", "Settings",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                SmartLogger.Log(LogLevel.Information, LogPrefix, "Settings applied successfully");
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error applying settings: {ex.Message}");
                MessageBox.Show($"Error applying settings: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnResetButton_Click(object sender, RoutedEventArgs e)
        {
            ResetToDefaults();
        }
        #endregion
    }
}