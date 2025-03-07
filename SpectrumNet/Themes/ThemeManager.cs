#nullable enable

namespace SpectrumNet
{
    #region CommonResources
    /// <summary>
    /// Provides common resources and utility methods for the application.
    /// </summary>
    public partial class CommonResources
    {
        /// <summary>
        /// Initializes the application resources by loading and merging resource dictionaries.
        /// </summary>
        public static void InitialiseResources()
        {
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/SpectrumNet;component/Themes/CommonResources.xaml",
                                UriKind.Absolute)
            });

            ThemeManager.Instance.ApplyThemeToWindow(Application.Current.MainWindow);
        }

        /// <summary>
        /// Handles mouse wheel scrolling for sliders.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The mouse wheel event arguments.</param>
        public void Slider_MouseWheelScroll(object sender, MouseWheelEventArgs e)
        {
            if (sender is Slider slider)
            {
                var change = slider.SmallChange > 0 ? slider.SmallChange : 1;
                slider.Value = Math.Clamp(slider.Value + (e.Delta > 0 ? change : -change), slider.Minimum, slider.Maximum);
                e.Handled = true;
            }
        }
    }
    #endregion

    #region ThemeManager
    /// <summary>
    /// Manages the application's theme, allowing switching between light and dark themes.
    /// </summary>
    public class ThemeManager : INotifyPropertyChanged
    {
        #region Fields
        private static ThemeManager? _instance;
        private bool _isDarkTheme = true;
        private readonly Dictionary<bool, ResourceDictionary> _themeDictionaries;
        private readonly List<WeakReference<Window>> _registeredWindows = new();
        #endregion

        #region Properties
        /// <summary>
        /// Gets the singleton instance of the ThemeManager.
        /// </summary>
        public static ThemeManager Instance => _instance ??= new ThemeManager();

        /// <summary>
        /// Gets or sets a value indicating whether the dark theme is currently active.
        /// </summary>
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
        #endregion

        #region Events
        /// <summary>
        /// Occurs when a property value changes.
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Occurs when the theme changes.
        /// </summary>
        public event EventHandler<ThemeChangedEventArgs>? ThemeChanged;
        #endregion

        #region Constructor
        /// <summary>
        /// Initializes a new instance of the ThemeManager class.
        /// </summary>
        private ThemeManager()
        {
            _themeDictionaries = new Dictionary<bool, ResourceDictionary>
            {
                { true, LoadThemeResource("/Themes/DarkTheme.xaml") },
                { false, LoadThemeResource("/Themes/LightTheme.xaml") }
            };
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Registers a window with the ThemeManager.
        /// </summary>
        /// <param name="window">The window to register.</param>
        public void RegisterWindow(Window window)
        {
            CleanupUnusedReferences();

            if (!_registeredWindows.Any(wr => wr.TryGetTarget(out var w) && w == window))
            {
                _registeredWindows.Add(new WeakReference<Window>(window));
                window.Closed += OnWindowClosed;
                ApplyThemeToWindow(window);
            }
        }

        public void SetTheme(bool isDarkTheme)
        {
            IsDarkTheme = isDarkTheme;
        }

        /// <summary>
        /// Unregisters a window from the ThemeManager.
        /// </summary>
        /// <param name="window">The window to unregister.</param>
        public void UnregisterWindow(Window window)
        {
            CleanupUnusedReferences();

            _registeredWindows.RemoveAll(wr => wr.TryGetTarget(out var w) && w == window);
            window.Closed -= OnWindowClosed;
        }

        /// <summary>
        /// Toggles between light and dark themes.
        /// </summary>
        public void ToggleTheme()
        {
            IsDarkTheme = !IsDarkTheme;
            UpdateAllWindows();
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Loads a theme resource dictionary from the specified path.
        /// </summary>
        /// <param name="path">The path to the theme resource.</param>
        /// <returns>The loaded ResourceDictionary.</returns>
        private static ResourceDictionary LoadThemeResource(string path) => new()
        {
            Source = new Uri($"pack://application:,,,/SpectrumNet;component/{path}", UriKind.Absolute)
        };

        /// <summary>
        /// Removes references to closed windows from the registered windows list.
        /// </summary>
        private void CleanupUnusedReferences() =>
            _registeredWindows.RemoveAll(wr => !wr.TryGetTarget(out _));

        /// <summary>
        /// Handles the window closed event.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event arguments.</param>
        private void OnWindowClosed(object? sender, EventArgs e)
        {
            if (sender is Window window)
            {
                _registeredWindows.RemoveAll(wr => wr.TryGetTarget(out var w) && w == window);
                window.Closed -= OnWindowClosed;
            }
        }

        /// <summary>
        /// Updates the theme for all registered windows.
        /// </summary>
        private void UpdateAllWindows()
        {
            foreach (var windowRef in _registeredWindows.ToList())
            {
                if (windowRef.TryGetTarget(out var window))
                {
                    ApplyThemeToWindow(window);
                }
            }
        }

        /// <summary>
        /// Applies the current theme to the specified window.
        /// </summary>
        /// <param name="window">The window to apply the theme to.</param>
        public void ApplyThemeToWindow(Window window)
        {
            if (window == null) return;

            var themeDict = _themeDictionaries[IsDarkTheme];
            Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (var key in themeDict.Keys)
                {
                    Application.Current.Resources[key] = themeDict[key]; 
                }
            });
        }
        #endregion

        #region Event Triggers
        /// <summary>
        /// Raises the PropertyChanged event.
        /// </summary>
        /// <param name="propertyName">The name of the property that changed.</param>
        protected virtual void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        /// <summary>
        /// Raises the ThemeChanged event.
        /// </summary>
        /// <param name="isDarkTheme">A value indicating whether the dark theme is active.</param>
        protected virtual void OnThemeChanged(bool isDarkTheme) =>
            ThemeChanged?.Invoke(this, new ThemeChangedEventArgs(isDarkTheme));
        #endregion
    }
    #endregion

    #region ThemeChangedEventArgs
    /// <summary>
    /// Provides data for the ThemeChanged event.
    /// </summary>
    public class ThemeChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Gets a value indicating whether the dark theme is active.
        /// </summary>
        public bool IsDarkTheme { get; }

        /// <summary>
        /// Initializes a new instance of the ThemeChangedEventArgs class.
        /// </summary>
        /// <param name="isDarkTheme">A value indicating whether the dark theme is active.</param>
        public ThemeChangedEventArgs(bool isDarkTheme) => IsDarkTheme = isDarkTheme;
    }
    #endregion
}