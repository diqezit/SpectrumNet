#nullable enable

namespace SpectrumNet
{
    // Добавляем псевдонимы для устранения неоднозначностей
    using WpfApplication = System.Windows.Application;
    using WpfWindow = System.Windows.Window;
    using WpfMouseWheelEventArgs = System.Windows.Input.MouseWheelEventArgs;

    #region CommonResources
    /// <summary>
    /// Предоставляет общие ресурсы и утилитные методы для приложения.
    /// </summary>
    public partial class CommonResources
    {
        /// <summary>
        /// Инициализирует ресурсы приложения, загружая и объединяя словари ресурсов.
        /// </summary>
        public static void InitialiseResources()
        {
            WpfApplication.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/SpectrumNet;component/Themes/CommonResources.xaml",
                                UriKind.Absolute)
            });

            ThemeManager.Instance.ApplyThemeToWindow(WpfApplication.Current.MainWindow);
        }

        /// <summary>
        /// Обрабатывает прокрутку колесика мыши для ползунков.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие.</param>
        /// <param name="e">Аргументы события колесика мыши.</param>
        public void Slider_MouseWheelScroll(object sender, WpfMouseWheelEventArgs e)
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
    /// Управляет темой приложения, позволяя переключаться между светлой и темной темами.
    /// </summary>
    public class ThemeManager : INotifyPropertyChanged
    {
        #region Fields
        private static ThemeManager? _instance;
        private bool _isDarkTheme = true;
        private readonly Dictionary<bool, ResourceDictionary> _themeDictionaries;
        private readonly List<WeakReference<WpfWindow>> _registeredWindows = new();
        #endregion

        #region Properties
        /// <summary>
        /// Получает единственный экземпляр ThemeManager.
        /// </summary>
        public static ThemeManager Instance => _instance ??= new ThemeManager();

        /// <summary>
        /// Получает или устанавливает значение, указывающее, активна ли темная тема.
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
        /// Происходит при изменении значения свойства.
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Происходит при смене темы.
        /// </summary>
        public event EventHandler<ThemeChangedEventArgs>? ThemeChanged;
        #endregion

        #region Constructor
        /// <summary>
        /// Инициализирует новый экземпляр класса ThemeManager.
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
        /// Регистрирует окно в ThemeManager.
        /// </summary>
        /// <param name="window">Окно для регистрации.</param>
        public void RegisterWindow(WpfWindow window)
        {
            CleanupUnusedReferences();

            if (!_registeredWindows.Any(wr => wr.TryGetTarget(out var w) && w == window))
            {
                _registeredWindows.Add(new WeakReference<WpfWindow>(window));
                window.Closed += OnWindowClosed;
                ApplyThemeToWindow(window);
            }
        }

        public void SetTheme(bool isDarkTheme)
        {
            IsDarkTheme = isDarkTheme;
        }

        /// <summary>
        /// Снимает регистрацию окна из ThemeManager.
        /// </summary>
        /// <param name="window">Окно для снятия регистрации.</param>
        public void UnregisterWindow(WpfWindow window)
        {
            CleanupUnusedReferences();

            _registeredWindows.RemoveAll(wr => wr.TryGetTarget(out var w) && w == window);
            window.Closed -= OnWindowClosed;
        }

        /// <summary>
        /// Переключает между светлой и темной темами.
        /// </summary>
        public void ToggleTheme()
        {
            IsDarkTheme = !IsDarkTheme;
            UpdateAllWindows();
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Загружает словарь ресурсов темы по указанному пути.
        /// </summary>
        /// <param name="path">Путь к ресурсу темы.</param>
        /// <returns>Загруженный ResourceDictionary.</returns>
        private static ResourceDictionary LoadThemeResource(string path) => new()
        {
            Source = new Uri($"pack://application:,,,/SpectrumNet;component/{path}", UriKind.Absolute)
        };

        /// <summary>
        /// Удаляет ссылки на закрытые окна из списка зарегистрированных окон.
        /// </summary>
        private void CleanupUnusedReferences() =>
            _registeredWindows.RemoveAll(wr => !wr.TryGetTarget(out _));

        /// <summary>
        /// Обрабатывает событие закрытия окна.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие.</param>
        /// <param name="e">Аргументы события.</param>
        private void OnWindowClosed(object? sender, EventArgs e)
        {
            if (sender is WpfWindow window)
            {
                _registeredWindows.RemoveAll(wr => wr.TryGetTarget(out var w) && w == window);
                window.Closed -= OnWindowClosed;
            }
        }

        /// <summary>
        /// Обновляет тему для всех зарегистрированных окон.
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
        /// Применяет текущую тему к указанному окну.
        /// </summary>
        /// <param name="window">Окно, к которому применяется тема.</param>
        public void ApplyThemeToWindow(WpfWindow window)
        {
            if (window == null) return;

            var themeDict = _themeDictionaries[IsDarkTheme];
            WpfApplication.Current.Dispatcher.Invoke(() =>
            {
                foreach (var key in themeDict.Keys)
                {
                    WpfApplication.Current.Resources[key] = themeDict[key];
                }
            });
        }
        #endregion

        #region Event Triggers
        /// <summary>
        /// Вызывает событие PropertyChanged.
        /// </summary>
        /// <param name="propertyName">Имя измененного свойства.</param>
        protected virtual void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        /// <summary>
        /// Вызывает событие ThemeChanged.
        /// </summary>
        /// <param name="isDarkTheme">Значение, указывающее, активна ли темная тема.</param>
        protected virtual void OnThemeChanged(bool isDarkTheme) =>
            ThemeChanged?.Invoke(this, new ThemeChangedEventArgs(isDarkTheme));
        #endregion
    }
    #endregion

    #region ThemeChangedEventArgs
    /// <summary>
    /// Предоставляет данные для события ThemeChanged.
    /// </summary>
    public class ThemeChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Получает значение, указывающее, активна ли темная тема.
        /// </summary>
        public bool IsDarkTheme { get; }

        /// <summary>
        /// Инициализирует новый экземпляр класса ThemeChangedEventArgs.
        /// </summary>
        /// <param name="isDarkTheme">Значение, указывающее, активна ли темная тема.</param>
        public ThemeChangedEventArgs(bool isDarkTheme) => IsDarkTheme = isDarkTheme;
    }
    #endregion
}