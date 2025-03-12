#nullable enable

using System.Windows.Forms;
using Newtonsoft.Json.Serialization;
using MessageBox = System.Windows.MessageBox;

namespace SpectrumNet
{
    /// <summary>
    /// Represents the settings window for the SpectrumNet application.
    /// </summary>
    public partial class SettingsWindow : Window
    {
        #region Константы
        private const string LogPrefix = "SettingsWindow";
        #endregion

        #region Singleton
        private static readonly Lazy<SettingsWindow> _instance = new(() => new SettingsWindow());
        public static SettingsWindow Instance => _instance.Value;
        #endregion

        #region Поля
        private readonly Settings _settings;
        private Dictionary<string, object?>? _originalValues;
        #endregion

        #region Конструктор
        public SettingsWindow()
        {
            InitialiseResources();
            InitializeComponent();
            ThemeManager.Instance.RegisterWindow(this);

            _settings = Settings.Instance;
            SaveOriginalSettings();
            DataContext = _settings;
        }
        #endregion

        #region Settings
        public void EnsureWindowVisible() => SmartLogger.Safe(() =>
        {
            bool isVisible = Screen.AllScreens.Any(screen =>
            {
                var screenBounds = new System.Drawing.Rectangle(
                    screen.Bounds.X, screen.Bounds.Y,
                    screen.Bounds.Width, screen.Bounds.Height
                );

                var windowRect = new System.Drawing.Rectangle(
                    (int)Left, (int)Top, (int)Width, (int)Height
                );

                return screenBounds.IntersectsWith(windowRect);
            });

            if (!isVisible)
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
                SmartLogger.Log(LogLevel.Warning, LogPrefix, "Window position reset to center (was outside visible area)");
            }
        }, new SmartLogger.ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error ensuring window visibility" });

        private void CopyPropertiesFrom(object source) => SmartLogger.Safe(() =>
        {
            source.GetType().GetProperties()
                .Where(p => p.CanRead && !p.GetCustomAttributes(typeof(JsonIgnoreAttribute), true).Any())
                .ToList()
                .ForEach(prop => SmartLogger.Safe(() =>
                {
                    var targetProp = _settings.GetType().GetProperty(prop.Name);
                    if (targetProp?.CanWrite == true)
                    {
                        targetProp.SetValue(_settings, prop.GetValue(source));
                    }
                }, new SmartLogger.ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = $"Failed to copy property {prop.Name}" }));
        }, new SmartLogger.ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error copying properties" });

        public void LoadSettings() => SmartLogger.Safe(() =>
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string settingsPath = Path.Combine(appDataPath, DefaultSettings.APP_FOLDER, DefaultSettings.SETTINGS_FILE);

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
        }, new SmartLogger.ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error loading settings" });

        public void SaveSettings() => SmartLogger.Safe(() =>
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

            SmartLogger.Log(LogLevel.Information, LogPrefix, $"Settings saved to {settingsPath}");
        }, new SmartLogger.ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error saving settings" });

        public void ResetToDefaults() => SmartLogger.Safe(() =>
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
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = LogPrefix,
            ErrorMessage = "Error resetting settings",
            ExceptionHandler = ex => MessageBox.Show($"Error resetting settings: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error)
        });
        #endregion

        #region Приватные методы
        private static void InitialiseResources() =>
            CommonResources.InitialiseResources();

        private void SaveOriginalSettings() =>
            _originalValues = _settings.GetType().GetProperties()
                .Where(p => p.CanRead && p.CanWrite)
                .ToDictionary(p => p.Name, p => p.GetValue(_settings));

        private void RestoreOriginalSettings()
        {
            if (_originalValues == null)
            {
                SmartLogger.Log(LogLevel.Warning, LogPrefix, "Original settings not found");
                return;
            }

            foreach (var (key, value) in _originalValues)
                _settings.GetType().GetProperty(key)?.SetValue(_settings, value);
        }

        private void UpdateRenderers() => SmartLogger.Safe(() =>
        {
            var renderer = RaindropsRenderer.GetInstance();
            if (renderer == null) return;

            var field = renderer.GetType().GetField("_isOverlayActive",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            bool isOverlayActive = false;
            if (field?.GetValue(renderer) is bool boolValue)
            {
                isOverlayActive = boolValue;
            }

            renderer.Configure(isOverlayActive);
        }, new SmartLogger.ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error updating renderer" });

        private void UpdateAllRenderers() => SmartLogger.Safe(() =>
        {
            foreach (var renderer in SpectrumRendererFactory.GetAllRenderers())
            {
                SmartLogger.Safe(() =>
                {
                    bool isOverlayActive = false;
                    var field = renderer.GetType().GetField("_isOverlayActive",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

                    if (field?.GetValue(renderer) is bool boolValue)
                        isOverlayActive = boolValue;

                    renderer.Configure(isOverlayActive, _settings.SelectedRenderQuality);
                }, new SmartLogger.ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = $"Failed to update renderer {renderer.GetType().Name}" });
            }
        }, new SmartLogger.ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error updating renderers" });
        #endregion

        #region Обработчики событий
        private void OnWindowDrag(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void Window_Closing(object sender, CancelEventArgs e) => SmartLogger.Safe(() =>
        {
            SaveSettings();
            ThemeManager.Instance.UnregisterWindow(this);
        }, new SmartLogger.ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error closing window" });

        private void OnCloseButton_Click(object sender, RoutedEventArgs e)
        {
            RestoreOriginalSettings();
            ThemeManager.Instance.UnregisterWindow(this);
            Close();
        }

        private void OnApplyButton_Click(object sender, RoutedEventArgs e) => SmartLogger.Safe(() =>
        {
            SaveSettings();
            SaveOriginalSettings();

            MessageBox.Show("Settings applied successfully!", "Settings",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = LogPrefix,
            ErrorMessage = "Error applying settings",
            ExceptionHandler = ex => MessageBox.Show($"Error applying settings: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error)
        });

        private void OnResetButton_Click(object sender, RoutedEventArgs e) =>
            ResetToDefaults();
        #endregion
    }
}