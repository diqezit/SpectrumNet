namespace SpectrumNet.SN.Themes;

public interface IThemes : INotifyPropertyChanged
{
    bool IsDarkTheme { get; }
    void RegisterWindow(Window window);
    void UnregisterWindow(Window window);
    void ToggleTheme();
    void SetTheme(bool isDark);
}

public sealed class Theme : IThemes
{
    private const string Log = nameof(Theme);
    private const string DarkUri = "/SN.Themes/Resources/DarkTheme.xaml";
    private const string LightUri = "/SN.Themes/Resources/LightTheme.xaml";

    private static readonly Lazy<Theme> _lazy = new(() => new Theme());
    public static Theme Instance => _lazy.Value;

    private readonly ISmartLogger _log = SmartLogger.Instance;
    private readonly ISettingsService _settings = SettingsService.Instance;
    private readonly List<WeakReference<Window>> _windows = [];
    private bool _isDark;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsDarkTheme
    {
        get => _isDark;
        private set
        {
            if (_isDark == value) return;
            _isDark = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDarkTheme)));
        }
    }

    private Theme() => _isDark = _settings.Current.General.IsDarkTheme;

    public void RegisterWindow(Window w)
    {
        if (w is null) return;
        Cleanup();
        if (!_windows.Any(wr => wr.TryGetTarget(out Window? x) && x == w))
            _windows.Add(new WeakReference<Window>(w));
        ApplyTo(w);
    }

    public void UnregisterWindow(Window w)
    {
        if (w is null) return;
        for (int i = _windows.Count - 1; i >= 0; i--)
            if (_windows[i].TryGetTarget(out Window? x) && x == w)
            {
                _windows.RemoveAt(i);
                break;
            }
    }

    public void ToggleTheme() => SetTheme(!_isDark);

    public void SetTheme(bool isDark)
    {
        if (_isDark == isDark) return;
        IsDarkTheme = isDark;
        ApplyAll();
        _settings.UpdateGeneral(g => g with { IsDarkTheme = isDark });
    }

    private void Cleanup() => _windows.RemoveAll(wr => !wr.TryGetTarget(out _));

    private void ApplyAll()
    {
        Cleanup();
        foreach (WeakReference<Window> wr in _windows)
            if (wr.TryGetTarget(out Window? w)) ApplyTo(w);
    }

    private void ApplyTo(Window w)
    {
        if (w is null) return;
        _log.Safe(() => Application.Current.Dispatcher.Invoke(() =>
        {
            Collection<ResourceDictionary> dicts = w.Resources.MergedDictionaries;
            ResourceDictionary? old = dicts.FirstOrDefault(d => d.Source?.ToString().EndsWith("Theme.xaml") == true);

            if (old is not null) dicts.Remove(old);
            dicts.Add(new ResourceDictionary
            {
                Source = new Uri(_isDark ? DarkUri : LightUri, UriKind.Relative)
            });
        }), Log, "Apply theme error");
    }
}

public static class CommonResources
{
    public static void InitialiseResources() =>
        Theme.Instance.RegisterWindow(Application.Current.MainWindow!);
}
