#nullable enable

namespace SpectrumNet
{

    public class EventHandlersMainWindow
    {
        private readonly MainWindow _mainWindow;

        public EventHandlersMainWindow(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
        }

        #region Event Handlers

        #region Window and Resource Management

        public void MainWindow_Closed(object? sender, EventArgs e)
        {
            if (_mainWindow.IsOverlayActive)
            {
                _mainWindow.CloseOverlay();
            }
            _mainWindow._initializationManager.CleanupResources();
        }

        public void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
        {
            _mainWindow._renderer?.UpdateRenderDimensions((int)e.NewSize.Width, (int)e.NewSize.Height);
        }

        #endregion

        #region UI Control Handlers
        public void OnOpenPopupButtonClick(object sender, RoutedEventArgs e)
        {
            _mainWindow.IsPopupOpen = !_mainWindow.IsPopupOpen;
        }

        public void OnOpenSettingsButtonClick(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow();
            settingsWindow.ShowDialog();
        }

        public void OnSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (sender is Slider slider)
            {
                ProcessSliderChange(slider);
            }
        }

        private void ProcessSliderChange(Slider slider)
        {
            switch (slider.Name)
            {
                case "barWidthSlider":
                    _mainWindow.BarWidth = slider.Value;
                    break;
                case "barSpacingSlider":
                    _mainWindow.BarSpacing = slider.Value;
                    break;
                case "barCountSlider":
                    _mainWindow.BarCount = (int)slider.Value;
                    break;
                case "minDbLevelSlider":
                    _mainWindow.MinDbLevel = (float)slider.Value;
                    break;
                case "maxDbLevelSlider":
                    _mainWindow.MaxDbLevel = (float)slider.Value;
                    break;
                case "adaptionRateSlider":
                    _mainWindow.AmplificationFactor = (float)slider.Value;
                    break;
            }

            UpdateRendererAndInvalidate();
        }

        #endregion

        #region Rendering Style Change Handlers
        public void OnRenderStyleComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_mainWindow.RenderStyleComboBox?.SelectedValue is RenderStyle newRenderStyle)
            {
                try
                {
                    _mainWindow.SelectedDrawingType = newRenderStyle;

                    if (_mainWindow._renderer == null)
                    {
                        Log.Error("[MainWindow] Рендерер не инициализирован. Обновление стиля рендеринга не может быть выполнено.");
                        return;
                    }

                    _mainWindow._renderer.UpdateRenderStyle(_mainWindow.SelectedDrawingType);
                    UpdateRendererAndInvalidate();
                    Log.Information("[MainWindow] Новый стиль рендеринга установлен: {newRenderStyle}.", newRenderStyle);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[MainWindow] Произошла ошибка при изменении стиля рендеринга.");
                }
            }
            else
            {
                Log.Warning("[MainWindow] Выбранный стиль рендеринга недоступен или равен null.");
            }
        }

        public void OnStyleComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_mainWindow._renderer == null || _mainWindow.SelectedStyle == null || _mainWindow._spectrumStyles == null)
            {
                Log.Warning("[MainWindow] Рендерер, SelectedStyle или SpectrumStyles не инициализированы");
                return;
            }

            try
            {
                Log.Information($"[MainWindow] Обновление стиля на {_mainWindow.SelectedStyle}");

                var (startColor, endColor, paint) = _mainWindow._spectrumStyles.GetColorsAndBrush(_mainWindow.SelectedStyle);

                if (paint == null)
                {
                    Log.Error("Кисть не была установлена для стиля {SelectedStyle}", _mainWindow.SelectedStyle);
                    return;
                }

                _mainWindow._renderer.UpdateSpectrumStyle(_mainWindow.SelectedStyle, startColor, endColor);
                UpdateRendererAndInvalidate();

                Log.Information("[MainWindow] Стиль успешно обновлен");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[MainWindow] Ошибка при обновлении стиля");
            }
        }
        #endregion

        #region Rendering and UI Update
        public void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
        {
            try
            {
                _mainWindow._renderer?.RenderFrame(sender, e);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[MainWindow] Ошибка при вызове рендерера.");
            }
        }

        public void UpdateRendererAndInvalidate()
        {
            _mainWindow._renderer?.RequestRender();
            _mainWindow._skElement?.InvalidateVisual();
        }
        #endregion

        #endregion

        #region Initialization Methods
        public void InitializeEventHandlers()
        {
            if (_mainWindow._skElement != null)
            {
                _mainWindow._skElement.PaintSurface += OnPaintSurface;
            }
            else
            {
                Log.Error("[MainWindow] _skElement is null при подписке на PaintSurface.");
            }

            _mainWindow.SizeChanged += OnWindowSizeChanged;

            if (_mainWindow._renderTimer != null)
            {
                _mainWindow._renderTimer.Tick += (_, _) =>
                {
                    if (_mainWindow._skElement != null)
                    {
                        _mainWindow._skElement.InvalidateVisual();
                    }
                    else
                    {
                        Log.Error("[MainWindow] _skElement is null при попытке вызова InvalidateVisual из InitializeEventHandlers.");
                    }
                };
                _mainWindow._renderTimer.Start();
            }
            else
            {
                Log.Warning("[MainWindow] _renderTimer is null при инициализации обработчиков событий.");
            }

            if (_mainWindow.StyleComboBox != null)
            {
                _mainWindow.StyleComboBox.SelectionChanged += OnStyleComboBoxSelectionChanged;
            }
            else
            {
                Log.Error("[MainWindow] StyleComboBox is null при подписке на SelectionChanged.");
            }

            if (_mainWindow.RenderStyleComboBox != null)
            {
                _mainWindow.RenderStyleComboBox.SelectionChanged += OnRenderStyleComboBoxSelectionChanged;
            }
            else
            {
                Log.Error("[MainWindow] RenderStyleComboBox is null при подписке на SelectionChanged.");
            }
        }
        #endregion
    }

    public class CaptureOperations
    {
        private readonly MainWindow _mainWindow;

        public CaptureOperations(MainWindow mainWindow)
        {
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
        }

        // Начальные методы захвата
        public async Task StartCaptureAsync()
        {
            await HandleCaptureOperationAsync(controller => controller.StartCaptureAsync(), true);
        }

        public async Task StopCaptureAsync()
        {
            if (_mainWindow._captureController == null || !_mainWindow.IsRecording)
            {
                Log.Warning("[MainWindow] Попытка остановить запись в некорректном состоянии.");
                return;
            }

            await HandleCaptureOperationAsync(controller => controller.StopCaptureAsync(), false);
        }

        // Метод для обработки нажатия кнопок
        public void OnButtonClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                Log.Information("[MainWindow] Нажата кнопка: {ButtonName}", button.Name);

                switch (button.Name)
                {
                    case "StartCaptureButton":
                        _ = StartCaptureAsync();
                        break;
                    case "StopCaptureButton":
                        _ = StopCaptureAsync();
                        break;
                    default:
                        Log.Warning("[MainWindow] Неизвестная кнопка: {ButtonName}", button.Name);
                        break;
                }
            }
        }

        // Вспомогательные методы обработки захвата
        private async Task HandleCaptureOperationAsync(Func<AudioController, Task> operation, bool isStarting)
        {
            try
            {
                Log.Debug("[MainWindow] Начало операции захвата. Статус: {IsStarting}", isStarting);

                if (!isStarting)
                {
                    await CleanupRecordingResourcesAsync();
                }
                else
                {
                    ReinitializeComponents(this);
                    await operation(_mainWindow._captureController!);
                }

                UpdateState(isStarting);

                Log.Information("[MainWindow] Запись {RecordingState}.", isStarting ? "начата" : "остановлена");

                await Application.Current.Dispatcher.InvokeAsync(() => _mainWindow._skElement?.InvalidateVisual());
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[MainWindow] Произошла ошибка во время операции захвата.");
                await ShowErrorMessageBoxAsync(ex.Message);

                if (!isStarting)
                {
                    await CleanupRecordingResourcesAsync();
                }
            }
        }

        // Обновление состояния UI и внутренних свойств
        private void UpdateState(bool isStarting)
        {
            lock (_mainWindow._lock)
            {
                _mainWindow.IsRecording = isStarting;
                SetStatusText(isStarting ? "Запись..." : "Готово");

                _mainWindow.OnPropertyChanged(
                    nameof(_mainWindow.IsRecording),
                    nameof(_mainWindow.CanStartCapture),
                    nameof(_mainWindow.StatusText)
                );

                Application.Current.Dispatcher.Invoke(() => CommandManager.InvalidateRequerySuggested());
            }
        }

        // Реинициализация компонентов перед началом записи
        private void ReinitializeComponents(CaptureOperations captureOperations)
        {
            try
            {
                Log.Information("[MainWindow] Начало реинициализации компонентов");

                _mainWindow._analyzer ??= new SpectrumAnalyzer();
                _mainWindow._captureController = new AudioController(_mainWindow._analyzer, _mainWindow, captureOperations);

                if (_mainWindow._skElement == null || _mainWindow._skElement.ActualWidth <= 0 || _mainWindow._skElement.ActualHeight <= 0)
                {
                    Log.Warning("[MainWindow] Неверные размеры _skElement: Width={Width}, Height={Height}",
                        _mainWindow._skElement?.ActualWidth, _mainWindow._skElement?.ActualHeight);
                    return;
                }

                _mainWindow._renderer?.Dispose();
                _mainWindow._renderer = new Renderer(_mainWindow._spectrumStyles ?? new SpectrumBrushes(), _mainWindow, _mainWindow._analyzer, _mainWindow._skElement);

                Log.Information("[MainWindow] Компоненты успешно реинициализированы");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[MainWindow] Ошибка при реинициализации компонентов");
                throw;
            }
        }

        // Очистка ресурсов записи после завершения захвата
        private async Task CleanupRecordingResourcesAsync()
        {
            try
            {
                Log.Information("[MainWindow] Начало очистки ресурсов записи");

                // Останавливаем таймер рендеринга
                _mainWindow._renderTimer?.Stop();

                // Если оверлей активен, закрываем его
                if (_mainWindow.IsOverlayActive)
                {
                    _mainWindow._overlayManager?.CloseOverlay();
                }

                // Останавливаем рендеринг перед освобождением ресурсов
                if (_mainWindow._skElement != null)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        _mainWindow._skElement.PaintSurface -= _mainWindow._eventHandlers!.OnPaintSurface;
                    });
                }

                // Освобождаем ресурсы в правильном порядке
                if (_mainWindow._captureController != null)
                {
                    Log.Debug("[MainWindow] Остановка захвата и освобождение контроллера");
                    await _mainWindow._captureController.StopCaptureAsync();
                    _mainWindow._captureController.Dispose();
                    _mainWindow._captureController = null;
                }

                // Освобождаем рендерер
                if (_mainWindow._renderer != null)
                {
                    _mainWindow._renderer.Dispose();
                    _mainWindow._renderer = null;
                }

                // Освобождаем анализатор только если он не будет больше использоваться
                if (_mainWindow._analyzer != null && !_mainWindow.IsOverlayActive)
                {
                    _mainWindow._analyzer.Dispose();
                    _mainWindow._analyzer = null;
                }

                // Обновляем состояние UI
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _mainWindow.IsRecording = false;
                    _mainWindow._captureOperations.SetStatusText("Готово");
                    _mainWindow.OnPropertyChanged(nameof(_mainWindow.IsRecording), nameof(_mainWindow.CanStartCapture));
                    CommandManager.InvalidateRequerySuggested();
                });

                _mainWindow._isRecording = false;
                _mainWindow._canStartCapture = true;

                Log.Information("[MainWindow] Ресурсы записи успешно очищены");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[MainWindow] Ошибка при очистке ресурсов записи");
                throw;
            }
        }

        // Показ сообщения об ошибке
        private static async Task ShowErrorMessageBoxAsync(string message)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                MessageBox.Show($"Произошла ошибка: {message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }

        // Метод для установки текста статуса
        public void SetStatusText(string statusText)
        {
            if (_mainWindow._statusText != statusText)
            {
                _mainWindow._statusText = statusText;
                _mainWindow.OnPropertyChanged(nameof(_mainWindow.StatusText));
            }
        }

        // Метод для установки статуса записи
        public void SetRecordingStatus(bool isRecording, Dispatcher dispatcher)
        {
            if (!dispatcher.CheckAccess())
            {
                dispatcher.Invoke(() => SetRecordingStatus(isRecording, dispatcher));
                return;
            }

            _mainWindow.IsRecording = isRecording;
        }
    }

    public class InitializationManager
    {
        private readonly MainWindow _mainWindow;

        public InitializationManager(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
        }

        public void InitializeComponents(CaptureOperations captureOperations)
        {
            try
            {
                Log.Debug("[MainWindow] Начало инициализации компонентов.");

                InitializeBasicComponents(captureOperations);
                InitializeRenderer();
                InitializeOverlay();
                ConfigureRenderTimer();

                Log.Information("[MainWindow] Компоненты инициализированы.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"[{nameof(MainWindow)}] Ошибка инициализации компонентов.");
                throw;
            }
        }

        private void InitializeBasicComponents(CaptureOperations captureOperations)
        {
            _mainWindow._spectrumStyles = new SpectrumBrushes() ??
                throw new InvalidOperationException($"[{nameof(MainWindow)}] _spectrumStyles не был инициализирован.");

            _mainWindow._skElement = _mainWindow.spectrumCanvas ??
                throw new ArgumentNullException(nameof(_mainWindow.spectrumCanvas), $"[{nameof(MainWindow)}] spectrumCanvas не был инициализирован.");

            _mainWindow._analyzer = new SpectrumAnalyzer() ??
                throw new InvalidOperationException($"[{nameof(MainWindow)}] _analyzer не был инициализирован.");

            _mainWindow._captureController = new AudioController(_mainWindow._analyzer, _mainWindow, captureOperations);

            _mainWindow._spectrumRenderer = new Renderer(_mainWindow._spectrumStyles, _mainWindow, _mainWindow._analyzer, _mainWindow._skElement) ??
                throw new InvalidOperationException($"[{nameof(MainWindow)}] _spectrumRenderer не был инициализирован.");

            _mainWindow._settings = Settings.Instance ??
                throw new InvalidOperationException($"[{nameof(MainWindow)}] _settings не был инициализирован.");

            _mainWindow._disposables = new CompositeDisposable();
        }

        private void InitializeRenderer()
        {
            Log.Debug("[MainWindow] Инициализация рендера.");

            if (_mainWindow._spectrumStyles == null)
            {
                Log.Error("[MainWindow] _spectrumStyles is null перед инициализацией рендерера.");
                throw new InvalidOperationException("[MainWindow] _spectrumStyles is null.");
            }
            if (_mainWindow._skElement == null)
            {
                Log.Error("[MainWindow] _skElement is null перед инициализацией рендерера.");
                throw new InvalidOperationException("[MainWindow] _skElement is null.");
            }
            if (_mainWindow._analyzer == null)
            {
                Log.Error("[MainWindow] _analyzer is null перед инициализацией рендерера.");
                throw new InvalidOperationException("[MainWindow] _analyzer is null.");
            }

            _mainWindow._renderer = new Renderer(_mainWindow._spectrumStyles, _mainWindow, _mainWindow._analyzer, _mainWindow._skElement);
            Log.Debug("[MainWindow] Рендерер инициализирован.");

            _mainWindow.RegisterCleanupAction(() =>
            {
                Log.Debug("[MainWindow] Освобождение ресурсов рендерера.");
                _mainWindow._renderer?.Dispose();
            });
        }

        private void InitializeOverlay()
        {
            Log.Information("Initializing overlay system");
            _mainWindow.SelectedStyle = "Gradient";
            Log.Debug("Style initialized: {SelectedStyle}", _mainWindow.SelectedStyle);
        }

        private void ConfigureRenderTimer()
        {
            if (_mainWindow._renderTimer == null)
            {
                Log.Warning("[MainWindow] _renderTimer is null, инициализация пропущена.");
                return;
            }

            _mainWindow._renderTimer.Interval = TimeSpan.FromMilliseconds(MainWindow.RenderIntervalMs);
            _mainWindow._renderTimer.Tick += (_, _) =>
            {
                if (_mainWindow._skElement != null)
                {
                    _mainWindow._skElement.InvalidateVisual();
                }
                else
                {
                    Log.Error("[MainWindow] _skElement is null при попытке вызова InvalidateVisual.");
                }
            };
            _mainWindow._renderTimer.Start();
        }

        public void CleanupResources()
        {
            try
            {
                Log.Information("[MainWindow] Начало процесса освобождения ресурсов.");

                if (_mainWindow._overlayWindow != null)
                {
                    _mainWindow._overlayWindow.Close();
                    _mainWindow._overlayWindow.Dispose();
                    _mainWindow._overlayWindow = null;
                    Log.Debug($"[MainWindow] Закрыто и освобождено окно оверлея.");
                }

                if (_mainWindow._renderTimer != null)
                {
                    _mainWindow._renderTimer.Stop();
                    _mainWindow._renderTimer = null;
                    Log.Debug($"[MainWindow] Таймер остановлен и освобожден.");
                }

                if (_mainWindow._skElement != null)
                {
                    _mainWindow._skElement.PaintSurface -= _mainWindow._eventHandlers!.OnPaintSurface;
                    Log.Debug($"[MainWindow] Обработчик PaintSurface удален из: {_mainWindow._skElement.GetType().Name}");
                }

                var disposables = new List<IDisposable?>
            {
                _mainWindow._renderer,
                _mainWindow._analyzer,
                _mainWindow._spectrumStyles,
                _mainWindow._captureController,
                _mainWindow._disposables
            };

                foreach (var disposable in disposables.Where(d => d != null))
                {
                    try
                    {
                        disposable?.Dispose();
                        Log.Information($"[MainWindow] Освобожден: {disposable?.GetType().Name}");

                        if (disposable == _mainWindow._renderer) _mainWindow._renderer = null;
                        else if (disposable == _mainWindow._analyzer) _mainWindow._analyzer = null;
                        else if (disposable == _mainWindow._spectrumStyles) _mainWindow._spectrumStyles = null;
                        else if (disposable == _mainWindow._captureController) _mainWindow._captureController = null;
                        else if (disposable == _mainWindow._disposables) _mainWindow._disposables = null;
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, $"[MainWindow] Ошибка при освобождении {disposable?.GetType().Name}");
                    }
                }

                Log.Information("[MainWindow] Ресурсы успешно освобождены.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[MainWindow] Ошибка в процессе очистки ресурсов.");
            }
        }
    }

    public class OverlayManager
    {
        private readonly MainWindow _mainWindow;

        public OverlayManager(MainWindow mainWindow)
        {
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
        }

        public void ToggleOverlay()
        {
            try
            {
                if (_mainWindow.IsOverlayActive)
                {
                    if (_mainWindow._overlayWindow != null)
                    {
                        CloseOverlay();
                    }
                    else
                    {
                        _mainWindow.IsOverlayActive = false;
                    }
                }
                else
                {
                    OpenOverlay();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[OverlayManager] Не удалось переключить режим оверлея");
                MessageBox.Show(
                    $"Не удалось переключить режим оверлея: {ex.Message}",
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        private void OpenOverlay()
        {
            try
            {
                var config = new OverlayConfiguration
                {
                    RenderInterval = 16, // ~60 FPS
                    IsTopmost = true,
                    ShowInTaskbar = false
                };

                // Проверка на null перед созданием окна оверлея
                if (_mainWindow != null)
                {
                    _mainWindow._overlayWindow = new OverlayWindow(_mainWindow, config);
                    _mainWindow._overlayWindow.Closed += (_, _) => OnOverlayClosed();
                    _mainWindow._overlayWindow.Show();
                    _mainWindow.IsOverlayActive = true;

                    // Обновление размеров рендерера
                    _mainWindow.Renderer?.UpdateRenderDimensions(
                        (int)SystemParameters.PrimaryScreenWidth,
                        (int)SystemParameters.PrimaryScreenHeight
                    );

                    Log.Information("[OverlayManager] Окно оверлея открыто");
                }
                else
                {
                    Log.Warning("[OverlayManager] Главное окно null, не удалось открыть оверлей.");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[OverlayManager] Произошла ошибка при открытии оверлея");
                MessageBox.Show(
                    $"Ошибка при открытии оверлея: {ex.Message}",
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        private void OnOverlayClosed()
        {
            try
            {
                _mainWindow.IsOverlayActive = false;

                // Проверки на null перед обновлением размеров рендерера
                if (_mainWindow.RenderElement != null && _mainWindow.Renderer != null)
                {
                    _mainWindow.Renderer.UpdateRenderDimensions(
                        (int)_mainWindow.RenderElement.ActualWidth,
                        (int)_mainWindow.RenderElement.ActualHeight
                    );
                }

                Log.Debug("[OverlayManager] Обработано событие закрытия оверлея");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[OverlayManager] Произошла ошибка при обработке события закрытия оверлея");
            }
        }

        public void CloseOverlay()
        {
            try
            {
                if (_mainWindow._overlayWindow != null)
                {
                    _mainWindow._overlayWindow.Close();
                    _mainWindow._overlayWindow.Dispose();
                    _mainWindow._overlayWindow = null;
                    _mainWindow.IsOverlayActive = false;

                    Log.Information("[OverlayManager] Окно оверлея закрыто и освобождено");
                }
                else
                {
                    Log.Warning("[OverlayManager] Окно оверлея уже равно null");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[OverlayManager] Произошла ошибка при закрытии оверлея");
            }
        }

        public void OnOverlayButtonClick(object sender, RoutedEventArgs e)
        {
            try
            {
                ToggleOverlay();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[OverlayManager] Произошла ошибка при обработке нажатия кнопки оверлея");
            }
        }
    }

    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        #region Constants

        public const int RenderIntervalMs = 16;

        #endregion

        #region Private Fields

        // Locks
        internal readonly object _lock = new();
        internal readonly object _updateLock = new();

        // Configuration
        internal string _style = "Gradient";
        internal double _barSpacing = 2;
        internal int _barCount = 120;
        internal double _barWidth = 8;

        // State
        public string _statusText = "Готово";
        internal bool _isRecording;
        private bool _isOverlayActive;
        internal bool _canStartCapture;
        private bool _isPopupOpen;
        private RenderStyle _selectedDrawingType;
        public bool IsDarkTheme => ThemeManager.Instance.IsDarkTheme;

        // Objects
        internal EventHandlersMainWindow? _eventHandlers;
        internal DispatcherTimer? _renderTimer;
        internal OverlayWindow? _overlayWindow;
        internal SpectrumBrushes? _spectrumStyles;
        internal Renderer? _spectrumRenderer;
        internal CompositeDisposable? _disposables;
        internal SKElement? _skElement;
        internal SpectrumAnalyzer? _analyzer;
        internal Renderer? _renderer;
        internal AudioController? _captureController;
        internal Settings? _settings;
        internal CaptureOperations _captureOperations;
        internal readonly InitializationManager _initializationManager;
        internal OverlayManager _overlayManager;

        // Cleanup
        private readonly Dictionary<string, Action> _cleanupActions = new();

        #endregion

        #region Constructor
        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            _eventHandlers = new EventHandlersMainWindow(this);
            _renderTimer = new DispatcherTimer();
            _initializationManager = new InitializationManager(this);
            _overlayManager = new OverlayManager(this);
            _captureOperations = new CaptureOperations(this);
            _initializationManager.InitializeComponents(_captureOperations);
            _eventHandlers.InitializeEventHandlers();

            ThemeManager.Instance.RegisterWindow(this);
            ThemeManager.Instance.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ThemeManager.IsDarkTheme))
                {
                    OnPropertyChanged(nameof(IsDarkTheme));
                }
            };

            OnPropertyChanged(nameof(IsRecording), nameof(CanStartCapture), nameof(StatusText));
            this.Closed += _eventHandlers.MainWindow_Closed;
            Log.Information("[MainWindow] MainWindow инициализирован.");
        }

        #endregion

        #region Public Properties
        public IEnumerable<RenderStyle> AvailableDrawingTypes { get; } =
            Enum.GetValues(typeof(RenderStyle)).Cast<RenderStyle>();

        public IReadOnlyDictionary<string, StyleDefinition> AvailableStyles =>
            _spectrumStyles?.Styles ?? new Dictionary<string, StyleDefinition>();

        public SKElement? RenderElement => _skElement;
        public Renderer? Renderer => _renderer;

        public bool IsPopupOpen
        {
            get => _isPopupOpen;
            set => SetField(ref _isPopupOpen, value);
        }

        public bool IsOverlayActive
        {
            get => _isOverlayActive;
            set
            {
                if (_isOverlayActive != value)
                {
                    _isOverlayActive = value;
                    OnPropertyChanged(nameof(IsOverlayActive));
                }
            }
        }

        public bool CanStartCapture
        {
            get => !IsRecording;
            private set => SetField(ref _canStartCapture, value);
        }

        public bool IsRecording
        {
            get => _isRecording;
            set
            {
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.Invoke(() => IsRecording = value);
                    return;
                }
                Log.Debug("[MainWindow] Изменение состояния записи на {State}", value ? "включено" : "выключено");
                SetField(ref _isRecording, value, () =>
                {
                    OnPropertyChanged(nameof(CanStartCapture));
                });
            }
        }

        public double BarWidth
        {
            get => _barWidth;
            set => SetField(ref _barWidth, value, _eventHandlers!.UpdateRendererAndInvalidate);
        }

        public double BarSpacing
        {
            get => _barSpacing;
            set => SetField(ref _barSpacing, value, _eventHandlers!.UpdateRendererAndInvalidate);
        }

        public int BarCount
        {
            get => _barCount;
            set => SetField(ref _barCount, value, _eventHandlers!.UpdateRendererAndInvalidate);
        }

        public RenderStyle SelectedDrawingType
        {
            get => _selectedDrawingType;
            set
            {
                Log.Debug("[MainWindow] Изменение типа рисования на {DrawingType}", value);
                if (_selectedDrawingType != value)
                {
                    _selectedDrawingType = value;
                    _renderer?.UpdateRenderStyle(_selectedDrawingType);
                    _skElement?.InvalidateVisual();
                }
            }
        }

        public string SelectedStyle
        {
            get => _style;
            set
            {
                Log.Debug("[MainWindow] Изменение стиля на {Style}", value);
                SetField(ref _style, value, () =>
                {
                    var (startColor, endColor, paint) = _spectrumStyles?.GetColorsAndBrush(SelectedStyle)
                                                        ?? (default, default, null);

                    _renderer?.UpdateSpectrumStyle(SelectedStyle, startColor, endColor);
                    _skElement?.InvalidateVisual();
                });
            }
        }

        public string StatusText
        {
            get => _statusText;
            private set => SetField(ref _statusText, value);
        }

        public float MinDbLevel
        {
            get => _analyzer?.MinDbValue ?? 0;
            set
            {
                if (_analyzer != null && _analyzer.MinDbValue != value)
                {
                    _analyzer.MinDbValue = value;
                    OnPropertyChanged(nameof(MinDbLevel));
                    UpdateAnalyzerAndInvalidate();
                }
            }
        }

        public float MaxDbLevel
        {
            get => _analyzer?.MaxDbValue ?? 0;
            set
            {
                if (_analyzer != null && _analyzer.MaxDbValue != value)
                {
                    _analyzer.MaxDbValue = value;
                    OnPropertyChanged(nameof(MaxDbLevel));
                    UpdateAnalyzerAndInvalidate();
                }
            }
        }

        public float AmplificationFactor
        {
            get => _analyzer!.AmplificationFactor;
            set
            {
                if (_analyzer != null && _analyzer.AmplificationFactor != value)
                {
                    _analyzer.AmplificationFactor = value;
                    OnPropertyChanged(nameof(AmplificationFactor));
                    UpdateAnalyzerAndInvalidate();
                }
            }
        }
        #endregion

        #region Capture Operations

        private void OnThemeToggleButtonChanged(object sender, RoutedEventArgs e)
        {
            ThemeManager.Instance.ToggleTheme();
        }

        public async Task StartCaptureAsync()
        {
            await _captureOperations.StartCaptureAsync();
        }

        public async Task StopCaptureAsync()
        {
            await _captureOperations.StopCaptureAsync();
        }

        private void OnButtonClick(object sender, RoutedEventArgs e)
        {
            _captureOperations.OnButtonClick(sender, e);
        }

        public void OnOpenPopupButtonClick(object sender, RoutedEventArgs e)
        {
            _eventHandlers?.OnOpenPopupButtonClick(sender, e);
        }

        public void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
        {
            _eventHandlers?.OnPaintSurface(sender, e);
        }

        public void OnStyleComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _eventHandlers?.OnStyleComboBoxSelectionChanged(sender, e);
        }

        public void OnOpenSettingsButtonClick(object sender, RoutedEventArgs e)
        {
            _eventHandlers?.OnOpenSettingsButtonClick(sender, e);
        }

        public void OnSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _eventHandlers?.OnSliderValueChanged(sender, e);
        }

        #endregion

        #region Helper Methods
        private void UpdateAnalyzerAndInvalidate()
        {
            lock (_updateLock)
            {
                _analyzer?.UpdateGainParameters(
                    amplificationFactor: AmplificationFactor,
                    minDbValue: MinDbLevel,
                    maxDbValue: MaxDbLevel
                );
            }
            _skElement?.InvalidateVisual();
        }

        #endregion

        #region Overlay Management

        private void OnOverlayButtonClick(object sender, RoutedEventArgs e)
        {
            _overlayManager.OnOverlayButtonClick(sender, e);
        }

        public void CloseOverlay()
        {
            _overlayManager.CloseOverlay();
        }

        #endregion

        #region Cleanup

        public void RegisterCleanupAction(Action action) =>
            _cleanupActions[action.Method.Name] = action;

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _initializationManager.CleanupResources();
        }

        #endregion

        #region INotifyPropertyChanged Implementation

        public event PropertyChangedEventHandler? PropertyChanged;

        public virtual void OnPropertyChanged(params string[] propertyNames)
        {
            if (propertyNames == null || propertyNames.Length == 0)
            {
                return;
            }

            foreach (var propertyName in propertyNames)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        private bool SetField<T>(ref T field, T value, Action? callback = null,
            [CallerMemberName] string? propertyName = null)
        {
            if (!Dispatcher.CheckAccess())
            {
                T temp = field;
                bool result = Dispatcher.Invoke(() =>
                    SetField(ref temp, value, callback, propertyName));
                if (result)
                {
                    field = temp;
                }
                return result;
            }

            if (!EqualityComparer<T>.Default.Equals(field, value))
            {
                field = value;
                OnPropertyChanged(propertyName ?? string.Empty);
                callback?.Invoke();
                return true;
            }
            return false;
        }

        #endregion
    }
}