#nullable enable

namespace SpectrumNet;

public partial class CommonResources
{
    public static void InitialiseResources() => SmartLogger.Safe(() =>
    {
        Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/SpectrumNet;component/Themes/CommonResources.xaml",
                            UriKind.Absolute)
        });

        ThemeManager.Instance.ApplyThemeToWindow(Application.Current.MainWindow);
    }, new SmartLogger.ErrorHandlingOptions
    {
        Source = nameof(InitialiseResources),
        ErrorMessage = "Failed to initialize application resources"
    });

    public void Slider_MouseWheelScroll(object sender, MouseWheelEventArgs e) => SmartLogger.Safe(() =>
    {
        if (sender is Slider slider)
        {
            var change = slider.SmallChange > 0 ? slider.SmallChange : 1;
            slider.Value = Math.Clamp(slider.Value + (e.Delta > 0 ? change : -change), slider.Minimum, slider.Maximum);
            e.Handled = true;
        }
    }, new SmartLogger.ErrorHandlingOptions
    {
        Source = nameof(Slider_MouseWheelScroll),
        ErrorMessage = "Error handling mouse wheel scroll for slider"
    });
}

public class ThemeManager : INotifyPropertyChanged
{
    private static ThemeManager? _instance;
    private bool _isDarkTheme = true;
    private readonly Dictionary<bool, ResourceDictionary> _themeDictionaries;
    private readonly List<WeakReference<Window>> _registeredWindows = [];

    public static ThemeManager Instance => _instance ??= new ThemeManager();

    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        private set
        {
            if (_isDarkTheme == value) return;

            _isDarkTheme = value;
            OnPropertyChanged(nameof(IsDarkTheme));
            OnThemeChanged(value);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<ThemeChangedEventArgs>? ThemeChanged;

    private ThemeManager()
    {
        _themeDictionaries = new Dictionary<bool, ResourceDictionary>
        {
            { true, LoadThemeResource("/Themes/DarkTheme.xaml") },
            { false, LoadThemeResource("/Themes/LightTheme.xaml") }
        };
    }

    public void RegisterWindow(Window window) => SmartLogger.Safe(() =>
    {
        if (window == null) return;

        CleanupUnusedReferences();

        if (!_registeredWindows.Any(wr => wr.TryGetTarget(out var w) && w == window))
        {
            _registeredWindows.Add(new WeakReference<Window>(window));
            window.Closed += OnWindowClosed;
            ApplyThemeToWindow(window);
        }
    }, new SmartLogger.ErrorHandlingOptions
    {
        Source = nameof(RegisterWindow),
        ErrorMessage = "Failed to register window with ThemeManager"
    });

    public void SetTheme(bool isDarkTheme) => SmartLogger.Safe(() =>
        IsDarkTheme = isDarkTheme,
        new SmartLogger.ErrorHandlingOptions
        {
            Source = nameof(SetTheme),
            ErrorMessage = "Failed to set theme"
        });

    public void UnregisterWindow(Window window) => SmartLogger.Safe(() =>
    {
        if (window == null) return;

        CleanupUnusedReferences();
        _registeredWindows.RemoveAll(wr => wr.TryGetTarget(out var w) && w == window);
        window.Closed -= OnWindowClosed;
    }, new SmartLogger.ErrorHandlingOptions
    {
        Source = nameof(UnregisterWindow),
        ErrorMessage = "Failed to unregister window from ThemeManager"
    });

    public void ToggleTheme() => SmartLogger.Safe(() =>
    {
        IsDarkTheme = !IsDarkTheme;
        UpdateAllWindows();
    }, new SmartLogger.ErrorHandlingOptions
    {
        Source = nameof(ToggleTheme),
        ErrorMessage = "Failed to toggle theme"
    });

    public static bool ShouldProcessKeyInContext(Window window, KeyEventArgs e, bool checkInputElements = true)
    {
        if (window == null || !window.IsActive)
            return false;

        if (checkInputElements && (e.Key == Key.Space || e.Key == Key.Enter))
        {
            if (Keyboard.FocusedElement is TextBox ||
                Keyboard.FocusedElement is PasswordBox ||
                Keyboard.FocusedElement is ComboBox ||
                Keyboard.FocusedElement is RichTextBox)
            {
                return false;
            }
        }

        return true;
    }

    private static ResourceDictionary LoadThemeResource(string path)
    {
        ResourceDictionary? result = null;
        SmartLogger.Safe(() =>
        {
            result = new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/SpectrumNet;component/{path}", UriKind.Absolute)
            };
        },
        new SmartLogger.ErrorHandlingOptions
        {
            Source = nameof(LoadThemeResource),
            ErrorMessage = $"Failed to load theme resource from {path}"
        });

        return result ?? new ResourceDictionary();
    }

    private void CleanupUnusedReferences() => SmartLogger.Safe(() =>
        _registeredWindows.RemoveAll(wr => !wr.TryGetTarget(out _)),
        new SmartLogger.ErrorHandlingOptions
        {
            Source = nameof(CleanupUnusedReferences),
            ErrorMessage = "Error cleaning up unused window references"
        });

    private void OnWindowClosed(object? sender, EventArgs e) => SmartLogger.Safe(() =>
    {
        if (sender is Window window)
        {
            _registeredWindows.RemoveAll(wr => wr.TryGetTarget(out var w) && w == window);
            window.Closed -= OnWindowClosed;
        }
    }, new SmartLogger.ErrorHandlingOptions
    {
        Source = nameof(OnWindowClosed),
        ErrorMessage = "Error handling window closed event"
    });

    private void UpdateAllWindows() => SmartLogger.Safe(() =>
    {
        foreach (var windowRef in _registeredWindows.ToList())
        {
            if (windowRef.TryGetTarget(out var window))
            {
                ApplyThemeToWindow(window);
            }
        }
    }, new SmartLogger.ErrorHandlingOptions
    {
        Source = nameof(UpdateAllWindows),
        ErrorMessage = "Failed to update all windows with new theme"
    });

    public async Task ApplyThemeToWindowAsync(Window? window, CancellationToken cancellationToken = default)
    {
        await SmartLogger.SafeAsync(async () =>
        {
            if (window == null) return;

            var themeDict = _themeDictionaries[IsDarkTheme];
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var key in themeDict.Keys)
                {
                    Application.Current.Resources[key] = themeDict[key];
                }
            }, System.Windows.Threading.DispatcherPriority.Normal, cancellationToken);
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = nameof(ApplyThemeToWindowAsync),
            ErrorMessage = "Failed to apply theme to window"
        });
    }

    public void ApplyThemeToWindow(Window? window, CancellationToken cancellationToken = default)
    {
        var task = ApplyThemeToWindowAsync(window, cancellationToken);
        task.ContinueWith(t =>
        {
            if (t.Exception != null)
            {
                SmartLogger.Error($"Error in ApplyThemeToWindow: {t.Exception.InnerException?.Message}",
                    nameof(ApplyThemeToWindow));
            }
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    protected virtual void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected virtual void OnThemeChanged(bool isDarkTheme) =>
        ThemeChanged?.Invoke(this, new ThemeChangedEventArgs(isDarkTheme));
}

public class ThemeChangedEventArgs(bool isDarkTheme)
    : EventArgs
{
    public bool IsDarkTheme { get; } = isDarkTheme;
}