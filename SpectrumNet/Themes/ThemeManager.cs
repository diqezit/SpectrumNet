using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Serilog;

#nullable enable

namespace SpectrumNet
{
    /// <summary>
    /// Ресурс для управления обработчиков слайдеров и кнопок для стилей темы в CommonRecources.xml.
    /// </summary>
    public partial class CommonResources
    {

        public static void InitaliseRecources()
        {
            // Загрузка ресурсов для xaml
            var commonResources = new ResourceDictionary { Source = new Uri("/Themes/CommonResources.xaml", UriKind.Relative) };
            var lightTheme = new ResourceDictionary { Source = new Uri("/Themes/LightTheme.xaml", UriKind.Relative) };
            var darkTheme = new ResourceDictionary { Source = new Uri("/Themes/DarkTheme.xaml", UriKind.Relative) };

            Application.Current.Resources.MergedDictionaries.Add(commonResources);
            Application.Current.Resources.MergedDictionaries.Add(lightTheme);
            Application.Current.Resources.MergedDictionaries.Add(darkTheme);
        }

        public void Slider_MouseWheelScroll(object sender, MouseWheelEventArgs e)
        {
            if (sender is Slider slider)
            {
                double change = slider.SmallChange > 0 ? slider.SmallChange : 1;
                slider.Value += (e.Delta > 0 ? change : -change);
                slider.Value = Math.Max(slider.Minimum, Math.Min(slider.Maximum, slider.Value));
                e.Handled = true;
            }
        }
    }

    /// <summary>
    /// Управляет темами приложения, позволяя переключаться между светлой и темной темой.
    /// </summary>
    public class ThemeManager : INotifyPropertyChanged
    {
        private static ThemeManager? _instance;
        private bool _isDarkTheme = true;
        private readonly Dictionary<bool, ResourceDictionary> _themeDictionaries;
        private readonly List<WeakReference<Window>> _registeredWindows;

        public static ThemeManager Instance => _instance ??= new ThemeManager();

        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<ThemeChangedEventArgs>? ThemeChanged;

        public bool IsDarkTheme
        {
            get => _isDarkTheme;
            private set
            {
                if (_isDarkTheme != value)
                {
                    _isDarkTheme = value;
                    OnPropertyChanged(nameof(IsDarkTheme));
                    OnThemeChanged(value);
                }
            }
        }

        private ThemeManager()
        {
            _registeredWindows = new List<WeakReference<Window>>();
            _themeDictionaries = new Dictionary<bool, ResourceDictionary>
            {
                { true, LoadThemeResource("Themes/DarkTheme.xaml") },
                { false, LoadThemeResource("Themes/LightTheme.xaml") }
            };
        }

        private ResourceDictionary LoadThemeResource(string path)
        {
            return new ResourceDictionary { Source = new Uri(path, UriKind.Relative) };
        }

        #region Регистрация и управление окнами

        public void RegisterWindow(Window window)
        {
            try
            {
                CleanupUnusedReferences();

                if (!_registeredWindows.Any(wr => wr.TryGetTarget(out var w) && w == window))
                {
                    _registeredWindows.Add(new WeakReference<Window>(window));
                    window.Closed += OnWindowClosed;
                    ApplyThemeToWindow(window);
                    Log.Debug($"[ThemeManager] Окно {window.GetType().Name} зарегистрировано");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[ThemeManager] Ошибка при регистрации окна");
            }
        }

        private void CleanupUnusedReferences()
        {
            _registeredWindows.RemoveAll(wr => !wr.TryGetTarget(out _));
        }

        public void UnregisterWindow(Window window)
        {
            try
            {
                CleanupUnusedReferences();

                if (_registeredWindows.Any(wr => wr.TryGetTarget(out var w) && w == window))
                {
                    _registeredWindows.RemoveAll(wr => wr.TryGetTarget(out var w) && w == window);
                    window.Closed -= OnWindowClosed;
                    Log.Debug($"[ThemeManager] Окно {window.GetType().Name} разорегистрировано");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[ThemeManager] Ошибка при разорегистрации окна");
            }
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            if (sender is Window window)
            {
                window.Loaded -= OnWindowLoaded;
                window.Unloaded -= OnWindowUnloaded;
                _registeredWindows.RemoveAll(wr => wr.TryGetTarget(out var w) && w == window);
                window.Closed -= OnWindowClosed;
            }
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is Window window)
            {
                ApplyThemeToWindow(window);
            }
        }

        private void OnWindowUnloaded(object sender, RoutedEventArgs e)
        {
            // Ничего не делаем, чтобы избежать перезагрузки ресурсов
        }

        #endregion

        #region Переключение темы

        public void ToggleTheme()
        {
            try
            {
                IsDarkTheme = !IsDarkTheme;
                UpdateAllWindows();
                Log.Information($"[ThemeManager] Тема изменена на {(IsDarkTheme ? "темную" : "светлую")}");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[ThemeManager] Ошибка при переключении темы");
            }
        }

        private void UpdateAllWindows()
        {
            var windows = _registeredWindows.ToList();
            foreach (var windowRef in windows)
            {
                if (windowRef.TryGetTarget(out var window))
                {
                    ApplyThemeToWindow(window);
                }
            }
        }

        public void ApplyThemeToWindow(Window window)
        {
            if (window == null) return;

            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var themeDict = _themeDictionaries[IsDarkTheme];

                    // Обновляем только измененные ресурсы
                    foreach (var key in themeDict.Keys)
                    {
                        if (window.Resources.Contains(key))
                        {
                            window.Resources[key] = themeDict[key];
                        }
                        else
                        {
                            window.Resources.Add(key, themeDict[key]);
                        }
                    }

                    // Если это главное окно, обновляем ресурсы приложения
                    if (window is MainWindow)
                    {
                        var app = Application.Current;
                        if (app != null)
                        {
                            foreach (var key in themeDict.Keys)
                            {
                                if (app.Resources.Contains(key))
                                {
                                    app.Resources[key] = themeDict[key];
                                }
                                else
                                {
                                    app.Resources.Add(key, themeDict[key]);
                                }
                            }
                        }
                    }

                    Log.Debug($"[ThemeManager] Тема применена к окну {window.GetType().Name}");
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"[ThemeManager] Ошибка при применении темы к окну {window.GetType().Name}");
            }
        }

        #endregion

        #region События и уведомления

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected virtual void OnThemeChanged(bool isDarkTheme)
        {
            ThemeChanged?.Invoke(this, new ThemeChangedEventArgs(isDarkTheme));
        }

        #endregion
    }

    /// <summary>
    /// Аргументы события, указывающие на изменение темы.
    /// </summary>
    public class ThemeChangedEventArgs : EventArgs
    {
        public bool IsDarkTheme { get; }

        public ThemeChangedEventArgs(bool isDarkTheme)
        {
            IsDarkTheme = isDarkTheme;
        }
    }
}