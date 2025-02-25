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
            Log.Debug($"[{nameof(SettingsWindow)}] Initializing resources window");
            InitialiseResources();

            Log.Debug($"[{nameof(SettingsWindow)}] Initializing window");
            InitializeComponent();

            Log.Debug($"[{nameof(SettingsWindow)}] Initializing theme");
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
                Log.Warning($"[{nameof(SettingsWindow)}] Original settings not found");
                return;
            }

            foreach (var (key, value) in _originalValues)
                _settings.GetType().GetProperty(key)?.SetValue(_settings, value);

            Log.Debug($"[{nameof(SettingsWindow)}] Settings restored from original");
        }

        /// <summary>
        /// Saves the current settings.
        /// </summary>
        private void SaveSettings()
        {
            Log.Information($"[{nameof(SettingsWindow)}] Settings saved successfully");

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
                    Log.Debug($"[{nameof(SettingsWindow)}] Renderer updated successfully");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"[{nameof(SettingsWindow)}] Error updating renderer");
            }
        }

        /// <summary>
        /// Persists the current settings to a file.
        /// </summary>
        private void PersistSettings()
        {
            try
            {
                File.WriteAllText("settings.json", JsonConvert.SerializeObject(_settings, Formatting.Indented));
                Log.Information($"[{nameof(SettingsWindow)}] Settings saved successfully");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"[{nameof(SettingsWindow)}] Error saving settings to file");
            }
        }
        #endregion

        #region Event Handlers
        /// <summary>
        /// Handles the window drag event.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The event arguments.</param>
        private void OnWindowDrag(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        /// <summary>
        /// Handles the window closing event.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The event arguments.</param>
        private void Window_Closing(object sender, CancelEventArgs e)
        {
            try
            {
                Log.Information($"[{nameof(SettingsWindow)}] Closing settings window");
                SaveSettings();
                PersistSettings();
                ThemeManager.Instance.UnregisterWindow(this);
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"[{nameof(SettingsWindow)}] Error closing window");
            }
        }

        /// <summary>
        /// Handles the close button click event.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The event arguments.</param>
        private void OnCloseButton_Click(object sender, RoutedEventArgs e)
        {
            Log.Debug($"[{nameof(SettingsWindow)}] Close button clicked");
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
                Log.Debug($"[{nameof(SettingsWindow)}] Applying settings");
                SaveSettings();
                PersistSettings();
                SaveOriginalSettings();

                MessageBox.Show("Settings applied successfully!", "Settings",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                Log.Information($"[{nameof(SettingsWindow)}] Settings applied successfully");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"[{nameof(SettingsWindow)}] Error applying settings");
                MessageBox.Show($"Error applying settings: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
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
                SaveSettings();

                MessageBox.Show("Settings have been reset to defaults.", "Settings Reset",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                Log.Debug($"[{nameof(SettingsWindow)}] Settings reset to defaults");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"[{nameof(SettingsWindow)}] Error resetting settings");
                MessageBox.Show($"Error resetting settings: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        #endregion
    }
}