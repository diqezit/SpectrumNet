#nullable enable

namespace SpectrumNet
{
    /// <summary>
    /// Interaction logic for SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private readonly Settings _settings;
        private Dictionary<string, object>? _originalValues;

        /// <summary>
        /// Initializes a new instance of the SettingsWindow class.
        /// </summary>
        public SettingsWindow()
        {
            Log.Debug($"[{nameof(SettingsWindow)}] Инициализация окна ресурсов");
            InitaliseRecources();
            Log.Debug($"[{nameof(SettingsWindow)}] Инициализация окна");
            InitializeComponent();

            Log.Debug($"[{nameof(SettingsWindow)}] Инициализация темы");
            ThemeManager.Instance.RegisterWindow(this);

            _settings = Settings.Instance;
            SaveOriginalSettings();
            DataContext = _settings;
        }

        private static void InitaliseRecources()
        {
            CommonResources.InitaliseRecources();
        }

        #region Private Methods

        /// <summary>
        /// Saves the original settings values to a dictionary for later restoration.
        /// </summary>
        private void SaveOriginalSettings()
        {
            var settings = _settings.GetType().GetProperties()
                .Where(p => p.CanRead && p.CanWrite)
                .ToDictionary(p => p.Name, p => p.GetValue(_settings));
            _originalValues = settings;

            Log.Debug($"[{nameof(SettingsWindow)}] Оригинальные настройки сохранены");
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

            foreach (var setting in _originalValues)
            {
                var property = _settings.GetType().GetProperty(setting.Key);
                property?.SetValue(_settings, setting.Value);
            }

            Log.Debug($"[{nameof(SettingsWindow)}] Настройки восстановлены из оригинала");
        }

        /// <summary>
        /// Handles the window closing event, saving and persisting the settings.
        /// </summary>
        /// <param name="sender">The sender of the event.</param>
        /// <param name="e">The CancelEventArgs containing the event data.</param>
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
        /// Persists the current settings to a JSON file.
        /// </summary>
        private void PersistSettings()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_settings, Formatting.Indented);
                File.WriteAllText("settings.json", json);
                Log.Information($"[{nameof(SettingsWindow)}] Настройки успешно сохранены");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"[{nameof(SettingsWindow)}] Ошибка при сохранении настроек в файл");
            }
        }

        /// <summary>
        /// Saves the current settings.
        /// </summary>
        private void SaveSettings()
        {
            try
            {
                Log.Information($"[{nameof(SettingsWindow)}] Настройки сохранены успешно");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"[{nameof(SettingsWindow)}] Ошибка при сохранении настроек");
                throw;
            }
        }

        #endregion

        #region Event Handlers

        /// <summary>
        /// Handles the close button click event, restoring original settings and closing the window.
        /// </summary>
        /// <param name="sender">The sender of the event.</param>
        /// <param name="e">The RoutedEventArgs containing the event data.</param>
        private void OnCloseButton_Click(object sender, RoutedEventArgs e)
        {
            Log.Debug($"[{nameof(SettingsWindow)}] Кнопка закрытия нажата");
            RestoreOriginalSettings();
            ThemeManager.Instance.UnregisterWindow(this);
            Close();
        }

        /// <summary>
        /// Handles the apply button click event, saving and persisting the settings.
        /// </summary>
        /// <param name="sender">The sender of the event.</param>
        /// <param name="e">The RoutedEventArgs containing the event data.</param>
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
        /// Handles the reset button click event, resetting the settings to their default values.
        /// </summary>
        /// <param name="sender">The sender of the event.</param>
        /// <param name="e">The RoutedEventArgs containing the event data.</param>
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