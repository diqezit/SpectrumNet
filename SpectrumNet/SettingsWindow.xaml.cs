#nullable enable

using TabControl = System.Windows.Controls.TabControl;
using TabItem = System.Windows.Controls.TabItem;

namespace SpectrumNet;

public partial class SettingsWindow : Window
{
    private const string LogPrefix = nameof(SettingsWindow);
    private readonly ISmartLogger _logger = SmartLogger.Instance;

    private static readonly Lazy<SettingsWindow> _instance = new(() => new SettingsWindow());
    public static SettingsWindow Instance => _instance.Value;

    private readonly ISettings _settings = Settings.Instance;
    private readonly IThemes _themeManager = ThemeManager.Instance;
    private readonly IRendererFactory _rendererFactory = RendererFactory.Instance;
    private readonly IKeyBindingManager _keyBindingManager;

    private Dictionary<string, object?>? _originalValues;
    private readonly Dictionary<KeyBindingControl, bool> _initializedControls = new();

    private bool _hasUnsavedChanges;
    private bool _isUpdatingBindings;
    private bool _isClosing;

    public SettingsWindow()
    {
        CommonResources.InitialiseResources();
        InitializeComponent();

        _themeManager.RegisterWindow(this);
        _keyBindingManager = new KeyBindingManager(_settings);

        BackupCurrentSettings();
        DataContext = _settings;

        SubscribeToEvents();
    }

    private void SubscribeToEvents()
    {
        _settings.SettingsChanged += OnSettingsChanged;
        Loaded += OnWindowLoaded;
    }

    private void UnsubscribeFromEvents()
    {
        _settings.SettingsChanged -= OnSettingsChanged;

        foreach (var kvp in _initializedControls)
        {
            kvp.Key.KeyChanged -= OnKeyBindingChanged;
            kvp.Key.RequestInitialization -= OnControlRequestInitialization;
        }

        _initializedControls.Clear();
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        _logger.Log(LogLevel.Debug, LogPrefix, "Window loaded");
    }

    private void OnWindowDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (_isClosing) return;

        e.Cancel = !HandleWindowClosing();
    }

    private bool HandleWindowClosing()
    {
        if (_hasUnsavedChanges)
        {
            var result = ShowUnsavedChangesDialog();

            switch (result)
            {
                case MessageBoxResult.Yes:
                    ApplyAndSaveSettings();
                    break;
                case MessageBoxResult.No:
                    RestoreBackupSettings();
                    break;
                case MessageBoxResult.Cancel:
                    return false;
            }
        }

        _isClosing = true;
        CleanupOnClosing();
        return true;
    }

    private void CleanupOnClosing()
    {
        _themeManager.UnregisterWindow(this);
        UnsubscribeFromEvents();
    }

    private void OnCloseButton_Click(object sender, RoutedEventArgs e) =>
        Close();

    private void OnApplyButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyAndSaveSettings();
        ShowSettingsAppliedConfirmation();
    }

    private void OnResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (ConfirmSettingsReset()) ResetToDefaults();
    }

    private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != sender) return;

        if (IsKeyBindingsTabSelected(sender))
            ScheduleKeyBindingsInitialization();
    }

    private static bool IsKeyBindingsTabSelected(object sender)
    {
        return sender is TabControl tabControl &&
               tabControl.SelectedItem is TabItem selectedTab &&
               selectedTab.Header?.ToString() == "Key Bindings";
    }

    private void ScheduleKeyBindingsInitialization()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (MainTabControl?.SelectedItem is TabItem selectedTab)
                ConnectLazyInitialization(selectedTab);

        }), DispatcherPriority.Background);
    }

    private void KeyBindingControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is KeyBindingControl control)
            EnsureControlInitialized(control);
    }

    private void OnControlRequestInitialization(object? sender, EventArgs e)
    {
        if (sender is KeyBindingControl control)
            EnsureControlInitialized(control);

    }

    private void OnKeyBindingChanged(object? sender, Key newKey)
    {
        if (sender is not KeyBindingControl control || _isUpdatingBindings)
            return;

        if (string.IsNullOrEmpty(control.ActionName))
            return;

        var currentKey = _keyBindingManager.GetKeyForAction(control.ActionName);
        if (currentKey == newKey)
            return;

        if (newKey == Key.None)
        {
            ClearKeyBinding(control);
        }
        else
        {
            AssignKeyBinding(control, newKey, currentKey);
        }

        _hasUnsavedChanges = true;
    }

    private void ConnectLazyInitialization(DependencyObject parent)
    {
        if (parent == null) return;

        foreach (var child in LogicalTreeHelper.GetChildren(parent))
        {
            if (child is KeyBindingControl keyBindingControl)
            {
                ConnectControlEvents(keyBindingControl);
            }
            else if (child is DependencyObject depObj)
            {
                ConnectLazyInitialization(depObj);
            }
        }
    }

    private void ConnectControlEvents(KeyBindingControl control)
    {
        control.RequestInitialization -= OnControlRequestInitialization;
        control.RequestInitialization += OnControlRequestInitialization;
    }

    private void EnsureControlInitialized(KeyBindingControl control)
    {
        if (_initializedControls.TryGetValue(control, out bool value) && value)
            return;

        if (control.Tag is string actionName && !string.IsNullOrEmpty(actionName))
        {
            InitializeControl(control, actionName);
            _initializedControls[control] = true;
        }
    }

    private void InitializeControl(KeyBindingControl control, string actionName)
    {
        control.ActionName = actionName;

        if (string.IsNullOrEmpty(control.Description))
        {
            var descriptions = _keyBindingManager.GetActionDescriptions();
            control.Description = descriptions.GetValueOrDefault(actionName, actionName);
        }

        var currentKey = _keyBindingManager.GetKeyForAction(actionName);
        control.CurrentKey = currentKey;

        control.KeyChanged -= OnKeyBindingChanged;
        control.KeyChanged += OnKeyBindingChanged;
    }

    private void ClearKeyBinding(KeyBindingControl control)
    {
        _isUpdatingBindings = true;

        try
        {
            if (_keyBindingManager.SetKeyForAction(control.ActionName, Key.None))
                control.CurrentKey = Key.None;
        }
        finally
        {
            _isUpdatingBindings = false;
        }

        RefreshOtherControls(control.ActionName);
    }

    private void AssignKeyBinding(KeyBindingControl control, Key newKey, Key currentKey)
    {
        var existingAction = _keyBindingManager.GetActionForKey(newKey);

        if (existingAction != null && existingAction != control.ActionName)
        {
            if (!ConfirmKeyReassignment(control, newKey, existingAction))
                return;
        }

        _isUpdatingBindings = true;

        try
        {
            if (_keyBindingManager.SetKeyForAction(
                control.ActionName,
                newKey,
                force: existingAction != null))
                control.CurrentKey = newKey;
        }
        finally
        {
            _isUpdatingBindings = false;
        }

        RefreshOtherControls(control.ActionName);
    }

    private void RefreshOtherControls(string excludeAction)
    {
        if (_isUpdatingBindings) return;

        _isUpdatingBindings = true;

        try
        {
            foreach (var kvp in _initializedControls)
            {
                var control = kvp.Key;
                if (control.ActionName != null && control.ActionName != excludeAction)
                {
                    var currentKey = _keyBindingManager.GetKeyForAction(control.ActionName);
                    if (control.CurrentKey != currentKey)
                        control.CurrentKey = currentKey;
                }
            }
        }
        finally
        {
            _isUpdatingBindings = false;
        }
    }

    private void RefreshAllKeyBindings()
    {
        _keyBindingManager.ValidateAndFixConflicts();

        _isUpdatingBindings = true;
        try
        {
            foreach (var kvp in _initializedControls)
            {
                var control = kvp.Key;
                if (!string.IsNullOrEmpty(control.ActionName))
                {
                    var currentKey = _keyBindingManager.GetKeyForAction(control.ActionName);
                    control.CurrentKey = currentKey;
                }
            }
        }
        finally
        {
            _isUpdatingBindings = false;
        }
    }

    private void OnSettingsChanged(object? sender, string action)
    {
        if (_isUpdatingBindings) return;

        if (action is "LoadSettings" or "ResetToDefaults")
        {
            ApplyLoadedSettings();
            RefreshAllKeyBindings();
        }
    }

    private void ApplyLoadedSettings()
    {
        _themeManager.SetTheme(_settings.General.IsDarkTheme);
        UpdateAllRenderers();
    }

    private void ApplyAndSaveSettings()
    {
        _keyBindingManager.ValidateAndFixConflicts();
        SaveSettings();
        BackupCurrentSettings();
        _hasUnsavedChanges = false;
    }

    public void LoadSettings() => _settings.LoadSettings();

    public void SaveSettings() => _settings.SaveSettings();

    public void ResetToDefaults()
    {
        _settings.ResetToDefaults();
        _hasUnsavedChanges = false;
        ShowSettingsResetConfirmation();
        RefreshAllKeyBindings();
    }

    private void BackupCurrentSettings() =>
        _originalValues = CreateBackupDictionary(GetSettingsToBackup());

    private (string, object?)[] GetSettingsToBackup() =>
    [
        ("Particles", _settings.Particles),
        ("Raindrops", _settings.Raindrops),
        ("Window", _settings.Window),
        ("Visualization", _settings.Visualization),
        ("Audio", _settings.Audio),
        ("General", _settings.General),
        ("KeyBindings", _settings.KeyBindings)
    ];

    private static Dictionary<string, object?> CreateBackupDictionary(
        (string, object?)[] settingsToBackup) =>
        settingsToBackup.ToDictionary(
            item => item.Item1,
            item => DeserializeSettingsItem(item.Item2));

    private static object? DeserializeSettingsItem(object? item)
    {
        if (item == null) return null;

        var json = JsonConvert.SerializeObject(item);
        return JsonConvert.DeserializeObject(json, item.GetType());
    }

    private void RestoreBackupSettings()
    {
        if (_originalValues == null) return;

        _settings.Particles = (ParticleSettings)_originalValues["Particles"]!;
        _settings.Raindrops = (RaindropSettings)_originalValues["Raindrops"]!;
        _settings.Window = (WindowSettings)_originalValues["Window"]!;
        _settings.Visualization = (VisualizationSettings)_originalValues["Visualization"]!;
        _settings.Audio = (AudioSettings)_originalValues["Audio"]!;
        _settings.General = (GeneralSettings)_originalValues["General"]!;
        _settings.KeyBindings = (KeyBindingSettings)_originalValues["KeyBindings"]!;

        ApplyRestoredSettings();
    }

    private void ApplyRestoredSettings()
    {
        _themeManager.SetTheme(_settings.General.IsDarkTheme);
        UpdateAllRenderers();
        RefreshAllKeyBindings();
    }

    private void UpdateAllRenderers()
    {
        foreach (var renderer in _rendererFactory.GetAllRenderers())
            UpdateRenderer(renderer);
    }

    private void UpdateRenderer(ISpectrumRenderer renderer)
    {
        var isOverlayActive = renderer.IsOverlayActive;
        renderer.Configure(isOverlayActive, _settings.Visualization.SelectedRenderQuality);
    }

    public void EnsureWindowVisible()
    {
        if (!IsWindowOnScreen())
            ResetWindowPosition();
    }

    private bool IsWindowOnScreen() =>
        Screen.AllScreens.Any(screen =>
            screen.Bounds.IntersectsWith(
                new System.Drawing.Rectangle(
                    (int)Left,
                    (int)Top,
                    (int)Width,
                    (int)Height)));

    private void ResetWindowPosition() =>
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

    private static MessageBoxResult ShowUnsavedChangesDialog() =>
        MessageBox.Show(
            "You have unsaved changes. Do you want to save them before closing?",
            "Unsaved Changes",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

    private bool ConfirmKeyReassignment(KeyBindingControl control, Key newKey, string existingAction)
    {
        var descriptions = _keyBindingManager.GetActionDescriptions();
        var existingDescription = descriptions.GetValueOrDefault(existingAction, existingAction);

        var message = $"Key '{newKey}' is already used for '{existingDescription}'.\n\n" +
                     $"Do you want to reassign it to '{control.Description}'?\n" +
                     $"('{existingDescription}' will be left without a key binding)";

        var result = MessageBox.Show(message, "Key Conflict", MessageBoxButton.YesNo, MessageBoxImage.Question);

        return result == MessageBoxResult.Yes;
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