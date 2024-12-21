#nullable enable

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
        #region Fields
        /// <summary>
        /// The application settings instance.
        /// </summary>
        private readonly Settings _settings;

        /// <summary>
        /// Dictionary to store original setting values.
        /// </summary>
        private Dictionary<string, object?>? _originalValues;
        #endregion

        #region Constructor
        /// <summary>
        /// Initializes a new instance of the <see cref="SettingsWindow"/> class.
        /// </summary>
        public SettingsWindow()
        {
            Log.Debug($"[{nameof(SettingsWindow)}] Инициализация окна ресурсов");
            InitialiseResources();

            Log.Debug($"[{nameof(SettingsWindow)}] Инициализация окна");
            InitializeComponent();

            Log.Debug($"[{nameof(SettingsWindow)}] Инициализация темы");
            ThemeManager.Instance.RegisterWindow(this);

            _settings = Settings.Instance;
            SaveOriginalSettings();
            DataContext = _settings;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Initializes the window resources.
        /// </summary>
        private static void InitialiseResources() =>
            CommonResources.InitialiseResources();

        /// <summary>
        /// Saves the current settings to use as original values.
        /// </summary>
        private void SaveOriginalSettings()
        {
            _originalValues = _settings.GetType().GetProperties()
                .Where(p => p.CanRead && p.CanWrite)
                .ToDictionary(p => p.Name, p => p.GetValue(_settings));
        }

        /// <summary>
        /// Restores the settings to their original values.
        /// </summary>
        private void RestoreOriginalSettings()
        {
            if (_originalValues == null)
            {
                Log.Warning($"[{nameof(SettingsWindow)}] Оригинальные настройки не найдены");
                return;
            }

            foreach (var (key, value) in _originalValues)
                _settings.GetType().GetProperty(key)?.SetValue(_settings, value);

            Log.Debug($"[{nameof(SettingsWindow)}] Настройки восстановлены из оригинала");
        }

        /// <summary>
        /// Saves the current settings.
        /// </summary>
        private void SaveSettings() =>
            Log.Information($"[{nameof(SettingsWindow)}] Настройки сохранены успешно");

        /// <summary>
        /// Persists the current settings to a file.
        /// </summary>
        private void PersistSettings()
        {
            try
            {
                File.WriteAllText("settings.json", JsonConvert.SerializeObject(_settings, Formatting.Indented));
                Log.Information($"[{nameof(SettingsWindow)}] Настройки успешно сохранены");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"[{nameof(SettingsWindow)}] Ошибка при сохранении настроек в файл");
            }
        }
        #endregion

        #region Event Handlers
        /// <summary>
        /// Handles the window closing event.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The event arguments.</param>
        private void Window_Closing(object sender, CancelEventArgs e)
        {
            try
            {
                Log.Information($"[{nameof(SettingsWindow)}] Закрытие окна настроек");
                SaveSettings();
                PersistSettings();
                ThemeManager.Instance.UnregisterWindow(this);
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"[{nameof(SettingsWindow)}] Ошибка при закрытии окна");
            }
        }

        /// <summary>
        /// Handles the close button click event.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The event arguments.</param>
        private void OnCloseButton_Click(object sender, RoutedEventArgs e)
        {
            Log.Debug($"[{nameof(SettingsWindow)}] Кнопка закрытия нажата");
            RestoreOriginalSettings();
            ThemeManager.Instance.UnregisterWindow(this);
            Close();
        }

        /// <summary>
        /// Handles the apply button click event.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The event arguments.</param>
        private void OnApplyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Log.Debug($"[{nameof(SettingsWindow)}] Применение настроек");
                SaveSettings();
                PersistSettings();
                SaveOriginalSettings();
                Log.Information($"[{nameof(SettingsWindow)}] Настройки успешно применены");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"[{nameof(SettingsWindow)}] Ошибка при применении настроек");
            }
        }

        /// <summary>
        /// Handles the reset button click event.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The event arguments.</param>
        private void OnResetButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _settings.ResetToDefaults();
                Log.Debug($"[{nameof(SettingsWindow)}] Настройки сброшены на значения по умолчанию");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"[{nameof(SettingsWindow)}] Ошибка при сбросе настроек");
            }
        }
        #endregion
    }
}