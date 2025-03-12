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

        /// <summary>
        /// Handles mouse wheel scrolling for sliders.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The mouse wheel event arguments.</param>
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
                if (_isDarkTheme == value) return;

                _isDarkTheme = value;
                OnPropertyChanged(nameof(IsDarkTheme));
                OnThemeChanged(value);
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

        /// <summary>
        /// Sets the theme to either dark or light.
        /// </summary>
        /// <param name="isDarkTheme">Whether to use dark theme.</param>
        public void SetTheme(bool isDarkTheme) => SmartLogger.Safe(() =>
            IsDarkTheme = isDarkTheme,
            new SmartLogger.ErrorHandlingOptions
            {
                Source = nameof(SetTheme),
                ErrorMessage = "Failed to set theme"
            });

        /// <summary>
        /// Unregisters a window from the ThemeManager.
        /// </summary>
        /// <param name="window">The window to unregister.</param>
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

        /// <summary>
        /// Toggles between light and dark themes.
        /// </summary>
        public void ToggleTheme() => SmartLogger.Safe(() =>
        {
            IsDarkTheme = !IsDarkTheme;
            UpdateAllWindows();
        }, new SmartLogger.ErrorHandlingOptions
        {
            Source = nameof(ToggleTheme),
            ErrorMessage = "Failed to toggle theme"
        });
        #endregion

        #region Private Methods
        /// <summary>
        /// Loads a theme resource dictionary from the specified path.
        /// </summary>
        /// <param name="path">The path to the theme resource.</param>
        /// <returns>The loaded ResourceDictionary.</returns>
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

        /// <summary>
        /// Removes references to closed windows from the registered windows list.
        /// </summary>
        private void CleanupUnusedReferences() => SmartLogger.Safe(() =>
            _registeredWindows.RemoveAll(wr => !wr.TryGetTarget(out _)),
            new SmartLogger.ErrorHandlingOptions
            {
                Source = nameof(CleanupUnusedReferences),
                ErrorMessage = "Error cleaning up unused window references"
            });

        /// <summary>
        /// Handles the window closed event.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event arguments.</param>
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

        /// <summary>
        /// Updates the theme for all registered windows.
        /// </summary>
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

        /// <summary>
        /// Applies the current theme to the specified window asynchronously.
        /// </summary>
        /// <param name="window">The window to apply the theme to.</param>
        /// <param name="cancellationToken">Optional token to cancel the operation.</param>
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

        /// <summary>
        /// Applies the current theme to the specified window.
        /// </summary>
        /// <param name="window">The window to apply the theme to.</param>
        /// <param name="cancellationToken">Optional token to cancel the operation.</param>
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
        public ThemeChangedEventArgs(bool isDarkTheme)
        {
            IsDarkTheme = isDarkTheme;
        }
    }
    #endregion
}