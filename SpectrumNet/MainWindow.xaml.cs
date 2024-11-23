#nullable enable
namespace SpectrumNet
{
    public class EventHandlersMainWindow
    {
        private readonly MainWindow _w;
        public EventHandlersMainWindow(MainWindow w) => _w = w;
        public void MainWindow_Closed(object? s, EventArgs e)
        {
            if (_w.IsOverlayActive) _w.CloseOverlay();
            _w._initializationManager.CleanupResources();
        }
        public void OnWindowSizeChanged(object s, SizeChangedEventArgs e) =>
            _w._renderer?.UpdateRenderDimensions((int)e.NewSize.Width, (int)e.NewSize.Height);
        public void OnOpenPopupButtonClick(object s, RoutedEventArgs e) =>
            _w.IsPopupOpen = !_w.IsPopupOpen;
        public void OnOpenSettingsButtonClick(object s, RoutedEventArgs e) =>
            new SettingsWindow().ShowDialog();
        public void OnSliderValueChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        {
            if (s is Slider slider) ProcessSliderChange(slider);
        }
        private void ProcessSliderChange(Slider s)
        {
            switch (s.Name)
            {
                case "barWidthSlider": _w.BarWidth = s.Value; break;
                case "barSpacingSlider": _w.BarSpacing = s.Value; break;
                case "barCountSlider": _w.BarCount = (int)s.Value; break;
                case "minDbLevelSlider": _w.MinDbLevel = (float)s.Value; break;
                case "maxDbLevelSlider": _w.MaxDbLevel = (float)s.Value; break;
                case "adaptionRateSlider": _w.AmplificationFactor = (float)s.Value; break;
            }
            UpdateRendererAndInvalidate();
        }
        public void OnRenderStyleComboBoxSelectionChanged(object s, SelectionChangedEventArgs e)
        {
            if (_w.RenderStyleComboBox?.SelectedValue is RenderStyle r)
            {
                _w.SelectedDrawingType = r;
                _w._renderer?.UpdateRenderStyle(r);
                UpdateRendererAndInvalidate();
            }
        }
        public void OnStyleComboBoxSelectionChanged(object s, SelectionChangedEventArgs e)
        {
            if (_w._renderer != null && _w.SelectedStyle != null && _w._spectrumStyles != null)
            {
                var (sC, eC, p) = _w._spectrumStyles.GetColorsAndBrush(_w.SelectedStyle);
                if (p != null)
                {
                    _w._renderer.UpdateSpectrumStyle(_w.SelectedStyle, sC, eC);
                    UpdateRendererAndInvalidate();
                }
            }
        }
        public void OnPaintSurface(object? s, SKPaintSurfaceEventArgs e) =>
            _w._renderer?.RenderFrame(s, e);
        public void UpdateRendererAndInvalidate()
        {
            _w._renderer?.RequestRender();
            _w._skElement?.InvalidateVisual();
        }
        public void InitializeEventHandlers()
        {
            if (_w._skElement != null) _w._skElement.PaintSurface += OnPaintSurface;
            _w.SizeChanged += OnWindowSizeChanged;
            if (_w._renderTimer != null)
            {
                _w._renderTimer.Tick += (_, _) => _w._skElement?.InvalidateVisual();
                _w._renderTimer.Start();
            }
            if (_w.StyleComboBox != null) _w.StyleComboBox.SelectionChanged += OnStyleComboBoxSelectionChanged;
            if (_w.RenderStyleComboBox != null) _w.RenderStyleComboBox.SelectionChanged += OnRenderStyleComboBoxSelectionChanged;
        }
    }

    public class CaptureOperations
    {
        private readonly MainWindow _w;
        public CaptureOperations(MainWindow w) => _w = w ?? throw new ArgumentNullException(nameof(w));
        public async Task StartCaptureAsync() => await HandleCaptureOperationAsync(c => c.StartCaptureAsync(), true);
        public async Task StopCaptureAsync()
        {
            if (_w._captureController == null || !_w.IsRecording) return;
            await HandleCaptureOperationAsync(c => c.StopCaptureAsync(), false);
        }
        public void OnButtonClick(object s, RoutedEventArgs e)
        {
            if (s is Button b)
                _ = b.Name switch
                {
                    "StartCaptureButton" => StartCaptureAsync(),
                    "StopCaptureButton" => StopCaptureAsync(),
                    _ => Task.CompletedTask
                };
        }
        private async Task HandleCaptureOperationAsync(Func<AudioController, Task> o, bool s)
        {
            try
            {
                if (!s) await CleanupRecordingResourcesAsync();
                else
                {
                    ReinitializeComponents(this);
                    await o(_w._captureController!);
                }
                UpdateState(s);
                await Application.Current.Dispatcher.InvokeAsync(() => _w._skElement?.InvalidateVisual());
            }
            catch { if (!s) await CleanupRecordingResourcesAsync(); }
        }
        private void UpdateState(bool s)
        {
            lock (_w._lock)
            {
                _w.IsRecording = s;
                SetStatusText(s ? "Запись..." : "Готово");
                _w.OnPropertyChanged(nameof(_w.IsRecording), nameof(_w.CanStartCapture), nameof(_w.StatusText));
                Application.Current.Dispatcher.Invoke(CommandManager.InvalidateRequerySuggested);
            }
        }
        private void ReinitializeComponents(CaptureOperations c)
        {
            var f = new FftProcessor(2048);
            var sC = new SpectrumConverter(_w._gainParameters);
            _w._analyzer ??= new SpectrumAnalyzer(f, sC, SynchronizationContext.Current);
            _w._captureController = new AudioController(_w._analyzer, _w, c);
            if (_w._skElement?.ActualWidth > 0 && _w._skElement.ActualHeight > 0)
            {
                _w._renderer?.Dispose();
                _w._renderer = new Renderer(_w._spectrumStyles ?? new SpectrumBrushes(), _w, _w._analyzer, _w._skElement);
            }
        }
        private async Task CleanupRecordingResourcesAsync()
        {
            try
            {
                _w._renderTimer?.Stop();
                if (_w.IsOverlayActive) _w._overlayManager?.CloseOverlay();
                if (_w._skElement != null)
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                        _w._skElement.PaintSurface -= _w._eventHandlers!.OnPaintSurface);
                if (_w._captureController != null)
                {
                    await _w._captureController.StopCaptureAsync();
                    _w._captureController.Dispose();
                    _w._captureController = null;
                }
                _w._renderer?.Dispose();
                _w._renderer = null;
                if (_w._analyzer != null && !_w.IsOverlayActive)
                {
                    _w._analyzer.Dispose();
                    _w._analyzer = null;
                }
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _w.IsRecording = false;
                    _w._captureOperations.SetStatusText("Готово");
                    _w.OnPropertyChanged(nameof(_w.IsRecording), nameof(_w.CanStartCapture));
                    CommandManager.InvalidateRequerySuggested();
                });
                _w._isRecording = false;
                _w._canStartCapture = true;
            }
            catch { }
        }
        public void SetStatusText(string s)
        {
            if (_w._statusText != s)
            {
                _w._statusText = s;
                _w.OnPropertyChanged(nameof(_w.StatusText));
            }
        }
        public void SetRecordingStatus(bool s, Dispatcher d)
        {
            if (!d.CheckAccess())
            {
                d.Invoke(() => SetRecordingStatus(s, d));
                return;
            }
            _w.IsRecording = s;
        }
    }

    public class InitializationManager
    {
        private readonly MainWindow _w;
        public InitializationManager(MainWindow w) => _w = w;
        public void InitializeComponents(CaptureOperations c)
        {
            InitializeBasicComponents(c);
            InitializeRenderer();
            _w.SelectedStyle = "Gradient";
            ConfigureRenderTimer();
        }
        private void InitializeBasicComponents(CaptureOperations c)
        {
            _w._spectrumStyles = new SpectrumBrushes();
            _w._skElement = _w.spectrumCanvas;
            var fft = new FftProcessor(2048);
            var conv = new SpectrumConverter(_w._gainParameters);
            _w._analyzer = new SpectrumAnalyzer(fft, conv, SynchronizationContext.Current);
            _w._captureController = new AudioController(_w._analyzer, _w, c);
            _w._spectrumRenderer = new Renderer(_w._spectrumStyles, _w, _w._analyzer, _w._skElement);
            _w._settings = Settings.Instance;
            _w._disposables = new CompositeDisposable();
        }
        private void InitializeRenderer()
        {
            if (_w._spectrumStyles == null || _w._analyzer == null || _w._skElement == null)
                throw new InvalidOperationException("Обязательные поля не инициализированы");
            _w._renderer = new Renderer(_w._spectrumStyles, _w, _w._analyzer, _w._skElement);
        }
        private void ConfigureRenderTimer()
        {
            if (_w._renderTimer == null) return;
            _w._renderTimer.Interval = TimeSpan.FromMilliseconds(MainWindow.RenderIntervalMs);
            _w._renderTimer.Tick += (_, _) => _w._skElement?.InvalidateVisual();
            _w._renderTimer.Start();
        }
        public void CleanupResources()
        {
            CleanupOverlay();
            CleanupTimer();
            CleanupEventHandlers();
            CleanupDisposables();
        }
        private void CleanupOverlay()
        {
            if (_w._overlayWindow != null)
            {
                _w._overlayWindow.Close();
                _w._overlayWindow.Dispose();
                _w._overlayWindow = null;
            }
        }
        private void CleanupTimer()
        {
            _w._renderTimer?.Stop();
            _w._renderTimer = null;
        }
        private void CleanupEventHandlers()
        {
            if (_w._skElement != null && _w._eventHandlers != null)
                _w._skElement.PaintSurface -= _w._eventHandlers.OnPaintSurface;
        }
        private void CleanupDisposables()
        {
            var disposables = new Dictionary<IDisposable, Action>();
            if (_w._renderer != null)
                disposables.Add(_w._renderer, () => _w._renderer = null);
            if (_w._analyzer != null)
                disposables.Add(_w._analyzer, () => _w._analyzer = null);
            if (_w._spectrumStyles != null)
                disposables.Add(_w._spectrumStyles, () => _w._spectrumStyles = null);
            if (_w._captureController != null)
                disposables.Add(_w._captureController, () => _w._captureController = null);
            if (_w._disposables != null)
                disposables.Add(_w._disposables, () => _w._disposables = null);
            foreach (var kvp in disposables)
            {
                kvp.Key.Dispose();
                kvp.Value();
            }
        }
    }

    public class OverlayManager
    {
        private readonly MainWindow _w;
        public OverlayManager(MainWindow w) => _w = w;
        public void ToggleOverlay() =>
            (_w.IsOverlayActive ? (Action)CloseOverlay : OpenOverlay)();
        private void OpenOverlay()
        {
            _w._overlayWindow = new OverlayWindow(_w, new OverlayConfiguration
            {
                RenderInterval = 16,
                IsTopmost = true,
                ShowInTaskbar = false
            });

            _w._overlayWindow.Closed += (_, _) => OnOverlayClosed();
            _w._overlayWindow.Show();
            _w.IsOverlayActive = true;

            UpdateRendererDimensions(
                (int)SystemParameters.PrimaryScreenWidth,
                (int)SystemParameters.PrimaryScreenHeight
            );
        }
        private void OnOverlayClosed()
        {
            _w.IsOverlayActive = false;

            if (_w.RenderElement != null && _w.Renderer != null)
                UpdateRendererDimensions(
                    (int)_w.RenderElement.ActualWidth,
                    (int)_w.RenderElement.ActualHeight
                );
        }
        public void CloseOverlay()
        {
            if (_w._overlayWindow == null) return;

            _w._overlayWindow.Close();
            _w._overlayWindow.Dispose();
            _w._overlayWindow = null;
            _w.IsOverlayActive = false;
        }
        private void UpdateRendererDimensions(int width, int height) =>
            _w.Renderer?.UpdateRenderDimensions(width, height);
        public void OnOverlayButtonClick(object s, RoutedEventArgs e) =>
            ToggleOverlay();
    }

    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public const int RenderIntervalMs = 16;
        internal readonly object _lock = new(), _updateLock = new();
        internal string _style = "Gradient";
        internal double _barSpacing = 2, _barWidth = 8;
        internal int _barCount = 120;
        public string _statusText = "Готово";
        internal bool _isRecording, _canStartCapture;
        private bool _isOverlayActive, _isPopupOpen;
        private RenderStyle _selectedDrawingType;

        internal EventHandlersMainWindow? _eventHandlers;
        internal DispatcherTimer? _renderTimer;
        internal OverlayWindow? _overlayWindow;
        internal SpectrumBrushes? _spectrumStyles;
        internal Renderer? _spectrumRenderer, _renderer;
        internal CompositeDisposable? _disposables;
        internal SKElement? _skElement;
        internal SpectrumAnalyzer? _analyzer;
        internal AudioController? _captureController;
        internal Settings? _settings;

        internal CaptureOperations _captureOperations;
        internal InitializationManager _initializationManager;
        internal OverlayManager _overlayManager;
        internal GainParameters _gainParameters;
        private readonly Dictionary<string, Action> _cleanupActions = new();

        public bool IsDarkTheme =>
            ThemeManager.Instance.IsDarkTheme;
        public IEnumerable<RenderStyle> AvailableDrawingTypes =>
            Enum.GetValues(typeof(RenderStyle)).Cast<RenderStyle>();
        public IReadOnlyDictionary<string, StyleDefinition> AvailableStyles =>
            _spectrumStyles?.Styles ?? new Dictionary<string, StyleDefinition>();
        public SKElement? RenderElement => _skElement;
        public Renderer? Renderer => _renderer;
        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            InitializeManagers();
            SetupEventHandlers();
            ConfigureTheme();
            UpdateProperties();
        }
        private void InitializeManagers()
        {
            _gainParameters = new GainParameters(SynchronizationContext.Current);
            _eventHandlers = new EventHandlersMainWindow(this);
            _renderTimer = new DispatcherTimer();
            _initializationManager = new InitializationManager(this);
            _overlayManager = new OverlayManager(this);
            _captureOperations = new CaptureOperations(this);
            _initializationManager.InitializeComponents(_captureOperations);
        }
        private void SetupEventHandlers()
        {
            _eventHandlers!.InitializeEventHandlers();
            this.Closed += _eventHandlers!.MainWindow_Closed;
        }
        private void ConfigureTheme()
        {
            ThemeManager.Instance.RegisterWindow(this);
            ThemeManager.Instance.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ThemeManager.IsDarkTheme))
                    OnPropertyChanged(nameof(IsDarkTheme));
            };
        }
        private void UpdateProperties() =>
            OnPropertyChanged(nameof(IsRecording), nameof(CanStartCapture), nameof(StatusText));
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
                if (_isOverlayActive == value) return;
                _isOverlayActive = value;
                OnPropertyChanged(nameof(IsOverlayActive));
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
                SetField(ref _isRecording, value, () => OnPropertyChanged(nameof(CanStartCapture)));
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
                if (_selectedDrawingType == value) return;
                _selectedDrawingType = value;
                _renderer?.UpdateRenderStyle(_selectedDrawingType);
                _skElement?.InvalidateVisual();
            }
        }
        public string SelectedStyle
        {
            get => _style;
            set => SetField(ref _style, value, () =>
            {
                var (sC, eC, p) = _spectrumStyles?.GetColorsAndBrush(SelectedStyle) ?? default;
                _renderer?.UpdateSpectrumStyle(SelectedStyle, sC, eC);
                _skElement?.InvalidateVisual();
            });
        }
        public string StatusText
        {
            get => _statusText;
            private set => SetField(ref _statusText, value);
        }
        public float MinDbLevel
        {
            get => _gainParameters.MinDbValue;
            set => UpdateGainParameter(value, v => _gainParameters.MinDbValue = v, nameof(MinDbLevel));
        }
        public float MaxDbLevel
        {
            get => _gainParameters.MaxDbValue;
            set => UpdateGainParameter(value, v => _gainParameters.MaxDbValue = v, nameof(MaxDbLevel));
        }
        public float AmplificationFactor
        {
            get => _gainParameters.AmplificationFactor;
            set => UpdateGainParameter(value, v => _gainParameters.AmplificationFactor = v, nameof(AmplificationFactor));
        }
        private void UpdateGainParameter(float newValue, Action<float> setter, string propertyName)
        {
            setter(newValue);
            OnPropertyChanged(propertyName);
            InvalidateVisuals();
        }
        public async Task StartCaptureAsync() => await _captureOperations.StartCaptureAsync();
        public async Task StopCaptureAsync() => await _captureOperations.StopCaptureAsync();
        private void OnButtonClick(object s, RoutedEventArgs e) => _captureOperations.OnButtonClick(s, e);
        private void OnThemeToggleButtonChanged(object s, RoutedEventArgs e) => ThemeManager.Instance.ToggleTheme();
        private void InvalidateVisuals() => _skElement?.InvalidateVisual();
        private void OnOverlayButtonClick(object s, RoutedEventArgs e) => _overlayManager.OnOverlayButtonClick(s, e);
        public void CloseOverlay() => _overlayManager.CloseOverlay();
        public void RegisterCleanupAction(Action a) => _cleanupActions[a.Method.Name] = a;
        public event PropertyChangedEventHandler? PropertyChanged;
        public virtual void OnPropertyChanged(params string[] properties)
        {
            if (properties?.Length > 0)
                foreach (var prop in properties)
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }
        private bool SetField<T>(ref T field, T value, Action? callback = null, [CallerMemberName] string? propertyName = null)
        {
            if (!Dispatcher.CheckAccess())
            {
                T temp = field;
                bool result = Dispatcher.Invoke(() => SetField(ref temp, value, callback, propertyName));
                if (result) field = temp;
                return result;
            }
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;
            field = value;
            OnPropertyChanged(propertyName ?? string.Empty);
            callback?.Invoke();
            return true;
        }
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _initializationManager.CleanupResources();
        }
        public void OnOpenPopupButtonClick(object s, RoutedEventArgs e) =>
            _eventHandlers?.OnOpenPopupButtonClick(s, e);
        public void OnPaintSurface(object? s, SKPaintSurfaceEventArgs e) =>
            _eventHandlers?.OnPaintSurface(s, e);
        public void OnStyleComboBoxSelectionChanged(object s, SelectionChangedEventArgs e) =>
            _eventHandlers?.OnStyleComboBoxSelectionChanged(s, e);
        public void OnOpenSettingsButtonClick(object s, RoutedEventArgs e) =>
            _eventHandlers?.OnOpenSettingsButtonClick(s, e);
        public void OnSliderValueChanged(object s, RoutedPropertyChangedEventArgs<double> e) =>
            _eventHandlers?.OnSliderValueChanged(s, e);
    }
}