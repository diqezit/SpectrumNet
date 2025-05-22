#nullable enable

namespace SpectrumNet.SN.Themes;

public sealed class ThemeManager : IThemes
{
    private const string LogPrefix = nameof(ThemeManager);
    private readonly ISmartLogger _logger = SmartLogger.Instance;
    private readonly ISettings _settings = Settings.Settings.Instance;
    private static readonly Lazy<ThemeManager> _instance = new(() => new ThemeManager());
    private readonly List<WeakReference<Window>> _registeredWindows = [];
    private bool _isDarkTheme = DefaultSettings.IsDarkTheme;

    public static ThemeManager Instance => _instance.Value;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<bool>? ThemeChanged;

    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        private set
        {
            if (_isDarkTheme == value) return;
            _isDarkTheme = value;
            OnPropertyChanged();
            ThemeChanged?.Invoke(this, value);
        }
    }

    private ThemeManager() { }

    public void RegisterWindow(Window window)
    {
        if (window == null) return;

        CleanupWeakReferences();

        if (_registeredWindows.Any(wr => wr.TryGetTarget(out var existingWindow) && existingWindow == window))
            return;

        _registeredWindows.Add(new WeakReference<Window>(window));
        ApplyThemeToWindow(window);
    }

    public void UnregisterWindow(Window window)
    {
        if (window == null) return;

        for (int i = _registeredWindows.Count - 1; i >= 0; i--)
        {
            if (_registeredWindows[i].TryGetTarget(out var registeredWindow) && registeredWindow == window)
            {
                _registeredWindows.RemoveAt(i);
                break;
            }
        }
    }

    public void ToggleTheme() => SetTheme(!_isDarkTheme);

    public void SetTheme(bool isDark) =>
        _logger.Safe(() =>
        {
            if (_isDarkTheme == isDark) return;

            IsDarkTheme = isDark;
            ApplyThemeToAllWindows();
            _settings.IsDarkTheme = isDark;
        },
        LogPrefix,
        "Error setting theme");

    public void ApplyThemeToCurrentWindow() =>
        _logger.Safe(() =>
        {
            if (Application.Current?.MainWindow != null)
            {
                ApplyThemeToWindow(Application.Current.MainWindow);
            }
        },
        LogPrefix,
        "Error applying theme to current window");

    private void CleanupWeakReferences()
    {
        for (int i = _registeredWindows.Count - 1; i >= 0; i--)
        {
            if (!_registeredWindows[i].TryGetTarget(out _))
            {
                _registeredWindows.RemoveAt(i);
            }
        }
    }

    private void ApplyThemeToAllWindows() =>
        _logger.Safe(() =>
        {
            CleanupWeakReferences();
            foreach (var windowRef in _registeredWindows)
            {
                if (windowRef.TryGetTarget(out var window))
                {
                    ApplyThemeToWindow(window);
                }
            }
        },
        LogPrefix,
        "Error applying theme to all windows");

    private void ApplyThemeToWindow(Window window) =>
        _logger.Safe(() =>
        {
            if (window == null) return;

            var resources = window.Resources.MergedDictionaries;
            Application.Current.Dispatcher.Invoke(() =>
            {
                var oldThemeDict = resources.FirstOrDefault(d =>
                    d.Source?.ToString().Contains("LightTheme.xaml") == true ||
                    d.Source?.ToString().Contains("DarkTheme.xaml") == true);

                if (oldThemeDict != null)
                    resources.Remove(oldThemeDict);

                var newThemeDict = new ResourceDictionary
                {
                    Source = new Uri(_isDarkTheme ?
                        "/SN.Themes/Resources/DarkTheme.xaml" :
                        "/SN.Themes/Resources/LightTheme.xaml",
                        UriKind.RelativeOrAbsolute)
                };

                resources.Add(newThemeDict);
            });
        },
        LogPrefix,
        "Error applying theme to window");

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}