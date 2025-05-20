#nullable enable

using SpectrumNet.Controllers.Interfaces.FactoryCore;

namespace SpectrumNet;

public partial class SettingsWindow : Window
{
    private const string LogPrefix = nameof(SettingsWindow);
    private readonly ISmartLogger _logger = SmartLogger.Instance;

    private static readonly Lazy<SettingsWindow> _instance = new(() => new SettingsWindow());
    public static SettingsWindow Instance => _instance.Value;

    private readonly ISettings _settings = Settings.Instance;
    private readonly IThemes _themeManager = ThemeManager.Instance;
    private Dictionary<string, object?>? _originalValues;
    private readonly IRendererFactory _rendererFactory = RendererFactory.Instance;

    private bool _changesApplied;

    public SettingsWindow()
    {
        CommonResources.InitialiseResources();
        InitializeComponent();
        _themeManager.RegisterWindow(this);
        BackupCurrentSettings();
        DataContext = _settings;

        _settings.SettingsChanged += OnSettingsChanged;
    }

    private void OnSettingsChanged(object? sender, string action)
    {
        if (action is "LoadSettings" or "ResetToDefaults")
        {
            _themeManager.SetTheme(_settings.IsDarkTheme);
            UpdateAllRenderers();
        }
    }

    public void EnsureWindowVisible() =>
        _logger.Safe(() =>
        {
            if (!IsWindowOnScreen())
                ResetWindowPosition();
        },
        LogPrefix,
        "Error ensuring window visibility");

    public void LoadSettings() => _settings.LoadSettings();
    public void SaveSettings() => _settings.SaveSettings();

    public void ResetToDefaults() =>
        _logger.Safe(() =>
        {
            if (!ConfirmSettingsReset())
                return;

            _settings.ResetToDefaults();
            _changesApplied = true;
            ShowSettingsResetConfirmation();
        },
        LogPrefix,
        "Error resetting settings");

    private void OnWindowDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void Window_Closing(object sender, CancelEventArgs e) =>
        _logger.Safe(() =>
        {
            CleanupOnClosing();
            _themeManager.UnregisterWindow(this);
        },
        LogPrefix,
        "Error closing window");

    private void OnCloseButton_Click(object sender, RoutedEventArgs e)
    {
        _changesApplied = false;
        Close();
    }

    private void OnApplyButton_Click(object sender, RoutedEventArgs e) =>
        _logger.Safe(() =>
        {
            ApplyAndSaveSettings();
            _changesApplied = true;
            ShowSettingsAppliedConfirmation();
        },
        LogPrefix,
        "Error applying settings");

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

        _themeManager.SetTheme(_settings.IsDarkTheme);
        UpdateAllRenderers();
    }

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

    private void UpdateAllRenderers() =>
        _logger.Safe(() =>
        {
            foreach (var renderer in _rendererFactory.GetAllRenderers())
            {
                var isOverlayActive = renderer.IsOverlayActive;
                renderer.Configure(isOverlayActive, _settings.SelectedRenderQuality);
            }
        },
        LogPrefix,
        "Error updating renderers");

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
        _logger.Log(LogLevel.Warning, LogPrefix, "Window position reset to center");
    }

    private static bool ConfirmSettingsReset() =>
        MessageBox.Show(
            "Are you sure you want to reset all settings to defaults?",
            "Reset Settings",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;

    private static void ShowSettingsResetConfirmation() =>
        MessageBox.Show(
            "Settings have been reset to defaults.",
            "Settings Reset",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

    private static void ShowSettingsAppliedConfirmation() =>
        MessageBox.Show(
            "Settings applied successfully!",
            "Settings",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
}