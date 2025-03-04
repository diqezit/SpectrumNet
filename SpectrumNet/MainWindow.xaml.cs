using System.Windows.Media;
using System.Windows.Media.Animation;

#nullable enable

namespace SpectrumNet
{
    public static class MwConstants
    {
        public const int RenderIntervalMs = 16,
                         MonitorDelay = 16,
                         BarCount = 60;
        public const double BarSpacing = 4;
        public const string DefaultStyle = "Gradient",
                            ReadyStatus = "Ready",
                            RecordingStatus = "Recording...";
    }

    public partial class MainWindow : Window, IAudioVisualizationController
    {
        private const string LogPrefix = "[MainWindow] ";

        private record MainWindowSettings(
            string Style = "Gradient",
            double BarSpacing = 4,
            int BarCount = 60,
            string StatusText = "Ready"
        );

        private readonly SemaphoreSlim _transitionSemaphore = new(1, 1);
        private MainWindowSettings _state = new();
        private bool _isOverlayActive, _isPopupOpen,
                     _isOverlayTopmost = true,
                     _isControlPanelVisible = true,
                     _isTransitioning;
        private RenderStyle _selectedDrawingType = RenderStyle.Bars;
        private FftWindowType _selectedFftWindowType = FftWindowType.Hann;
        private SpectrumScale _selectedScaleType = SpectrumScale.Linear;
        private OverlayWindow? _overlayWindow;
        private Renderer? _renderer;
        private SpectrumBrushes _spectrumStyles = null!;
        private SpectrumAnalyzer? _analyzer;
        private AudioCaptureManager? _captureManager;
        private CompositeDisposable? _disposables;
        private GainParameters _gainParameters = null!;
        private SKElement _renderElement = null!;
        private PropertyChangedEventHandler? _themePropertyChangedHandler;

        #region IAudioVisualizationController Properties

        public SpectrumAnalyzer Analyzer
        {
            get => _analyzer!;
            set => _analyzer = value ?? throw new ArgumentNullException(nameof(Analyzer));
        }

        public int BarCount
        {
            get => _state.BarCount;
            set => UpdateState(s => s with { BarCount = value }, nameof(BarCount));
        }

        public double BarSpacing
        {
            get => _state.BarSpacing;
            set => UpdateState(s => s with { BarSpacing = value }, nameof(BarSpacing));
        }

        public bool CanStartCapture => _captureManager != null && !IsRecording;

        public GainParameters GainParameters => _gainParameters;

        public bool IsOverlayActive
        {
            get => _isOverlayActive;
            set
            {
                if (_isOverlayActive == value) return;
                _isOverlayActive = value;
                OnPropertyChanged(nameof(IsOverlayActive));
                SpectrumRendererFactory.ConfigureAllRenderers(value);
                _renderElement?.InvalidateVisual();
            }
        }

        public bool IsRecording
        {
            get => _captureManager?.IsRecording ?? false;
            set
            {
                if (_captureManager?.IsRecording == value) return;
                if (value)
                    _ = StartCaptureAsync();
                else
                    _ = StopCaptureAsync();
                OnPropertyChanged(nameof(IsRecording), nameof(CanStartCapture));
            }
        }

        public bool IsTransitioning => _isTransitioning;

        public Renderer Renderer
        {
            get => _renderer!;
            set => _renderer = value ?? throw new ArgumentNullException(nameof(Renderer));
        }

        public SpectrumScale ScaleType
        {
            get => _selectedScaleType;
            set
            {
                if (_selectedScaleType == value) return;
                _selectedScaleType = value;
                OnPropertyChanged(nameof(ScaleType));
                if (_analyzer != null)
                {
                    _analyzer.SetScaleType(value);
                    _renderer?.RequestRender();
                    _renderElement?.InvalidateVisual();
                }
            }
        }

        public RenderStyle SelectedDrawingType
        {
            get => _selectedDrawingType;
            set
            {
                if (_selectedDrawingType == value) return;
                _selectedDrawingType = value;
                OnPropertyChanged(nameof(SelectedDrawingType));
            }
        }

        public string SelectedStyle
        {
            get => _state.Style;
            set => UpdateState(s => s with { Style = value }, nameof(SelectedStyle));
        }

        public SKElement SpectrumCanvas => _renderElement;

        public SpectrumBrushes SpectrumStyles => _spectrumStyles;

        public string StatusText
        {
            get => _state.StatusText;
            set => UpdateState(s => s with { StatusText = value }, nameof(StatusText));
        }

        public FftWindowType WindowType
        {
            get => _selectedFftWindowType;
            set
            {
                if (_selectedFftWindowType == value) return;
                _selectedFftWindowType = value;
                OnPropertyChanged(nameof(WindowType));
                if (_analyzer != null)
                {
                    _analyzer.SetWindowType(value);
                    _renderer?.RequestRender();
                    _renderElement?.InvalidateVisual();
                }
            }
        }

        #endregion

        #region Additional Public Properties

        public static bool IsDarkTheme => ThemeManager.Instance.IsDarkTheme;

        public static IEnumerable<RenderStyle> AvailableDrawingTypes =>
            Enum.GetValues<RenderStyle>().OrderBy(s => s.ToString());

        public static IEnumerable<FftWindowType> AvailableFftWindowTypes =>
            Enum.GetValues<FftWindowType>().OrderBy(wt => wt.ToString());

        public static IEnumerable<SpectrumScale> AvailableScaleTypes =>
            Enum.GetValues<SpectrumScale>().OrderBy(s => s.ToString());

        public IReadOnlyDictionary<string, StyleDefinition> AvailableStyles =>
            _spectrumStyles?.Styles?
                .OrderBy(kvp => kvp.Key)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value) ?? new Dictionary<string, StyleDefinition>();

        public bool IsPopupOpen
        {
            get => _isPopupOpen;
            set => SetField(ref _isPopupOpen, value);
        }

        public bool IsOverlayTopmost
        {
            get => _isOverlayTopmost;
            set => SetField(ref _isOverlayTopmost, value, UpdateOverlayTopmostState);
        }

        public bool IsControlPanelVisible
        {
            get => _isControlPanelVisible;
            set
            {
                if (SetField(ref _isControlPanelVisible, value))
                    UpdateToggleButtonContent();
            }
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

        #endregion

        public event PropertyChangedEventHandler? PropertyChanged;

        public MainWindow()
        {
            try
            {
                InitializeComponent();
                _gainParameters = new GainParameters(
                    SynchronizationContext.Current ?? throw new InvalidOperationException("No synchronization context"),
                    SharedConstants.DefaultMinDb,
                    SharedConstants.DefaultMaxDb,
                    SharedConstants.DefaultAmplificationFactor
                );
                DataContext = this;
                InitComponents();
                InitEventHandlers();
                ConfigureTheme();
                UpdateProps();
                CompositionTarget.Rendering += OnRendering;
                UpdateToggleButtonContent();
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Failed to initialize main window: {ex}");
                throw;
            }
        }

        #region Initialization

        private void InitComponents()
        {
            try
            {
                _renderElement = spectrumCanvas ?? throw new InvalidOperationException("spectrumCanvas is null");
                _spectrumStyles = new SpectrumBrushes();
                _disposables = new CompositeDisposable();
                var syncContext = SynchronizationContext.Current ?? throw new InvalidOperationException("SynchronizationContext.Current is null");
                _analyzer = new SpectrumAnalyzer(
                    new FftProcessor { WindowType = WindowType },
                    new SpectrumConverter(_gainParameters),
                    syncContext
                );
                _disposables.Add(_analyzer);
                _captureManager = new AudioCaptureManager(this);
                _disposables.Add(_captureManager);
                _renderer = new Renderer(_spectrumStyles, this, _analyzer, _renderElement);
                _disposables.Add(_renderer);
                SelectedDrawingType = RenderStyle.Bars;
                UpdateState(s => s with { Style = MwConstants.DefaultStyle }, nameof(SelectedStyle));
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Failed to initialize components: {ex}");
                throw;
            }
        }

        private void InitEventHandlers()
        {
            _renderElement.PaintSurface += OnPaintSurface;
            SizeChanged += OnWindowSizeChanged;
            StateChanged += OnStateChanged;
            MouseDoubleClick += OnWindowMouseDoubleClick;
            Closed += OnWindowClosed;
            PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(IsRecording))
                    StatusText = IsRecording ? MwConstants.RecordingStatus : MwConstants.ReadyStatus;
            };
        }

        private void ConfigureTheme()
        {
            var tm = ThemeManager.Instance;
            tm.RegisterWindow(this);
            _themePropertyChangedHandler = (_, e) =>
            {
                if (e.PropertyName == nameof(ThemeManager.IsDarkTheme))
                    OnPropertyChanged(nameof(IsDarkTheme));
            };
            tm.PropertyChanged += _themePropertyChangedHandler;
        }

        #endregion

        #region Capture Management

        public async Task StartCaptureAsync()
        {
            if (_captureManager == null) return;
            try
            {
                await _captureManager.StartCaptureAsync();
                UpdateProps();
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Failed to start audio capture: {ex}");
            }
        }

        public async Task StopCaptureAsync()
        {
            if (_captureManager == null) return;
            try
            {
                await _captureManager.StopCaptureAsync();
                UpdateProps();
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Failed to stop audio capture: {ex}");
            }
        }

        #endregion

        #region Overlay Management

        private void OpenOverlay()
        {
            try
            {
                if (_overlayWindow?.IsInitialized == true)
                {
                    _overlayWindow.Show();
                    _overlayWindow.Topmost = IsOverlayTopmost;
                }
                else
                {
                    _overlayWindow = new OverlayWindow(
                        this,
                        new OverlayConfiguration(
                            RenderInterval: MwConstants.RenderIntervalMs,
                            IsTopmost: IsOverlayTopmost,
                            ShowInTaskbar: false,
                            EnableHardwareAcceleration: true
                        )
                    );
                    _overlayWindow.Closed += (_, _) => OnOverlayClosed();
                    _overlayWindow.Show();
                }
                IsOverlayActive = true;
                SpectrumRendererFactory.ConfigureAllRenderers(true);
                _renderElement?.InvalidateVisual();
                UpdateRendererDimensions((int)SystemParameters.PrimaryScreenWidth, (int)SystemParameters.PrimaryScreenHeight);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Failed to open overlay window: {ex}");
            }
        }

        private void CloseOverlay() => _overlayWindow?.Close();

        private void OnOverlayClosed()
        {
            try
            {
                _overlayWindow?.Dispose();
                _overlayWindow = null;
                IsOverlayActive = false;
                SpectrumRendererFactory.ConfigureAllRenderers(false);
                _renderElement?.InvalidateVisual();
                Activate();
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error closing overlay: {ex}");
            }
        }

        #endregion

        #region Event Handlers

        public void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
        {
            if (IsOverlayActive && sender == _renderElement)
            {
                e.Surface.Canvas.Clear(SKColors.Transparent);
                return;
            }
            _renderer?.RenderFrame(sender, e);
        }

        private void OnRendering(object? sender, EventArgs e) => _renderElement?.InvalidateVisual();

        private void OnStateChanged(object? sender, EventArgs e)
        {
            if (MaximizeButton == null || MaximizeIcon == null) return;
            MaximizeIcon.Data = Geometry.Parse(WindowState == WindowState.Maximized
                ? "M0,0 L20,0 L20,20 L0,20 Z"
                : "M2,2 H18 V18 H2 Z");
        }

        private void OnThemeToggleButtonChanged(object sender, RoutedEventArgs e) =>
            ThemeManager.Instance.ToggleTheme();

        private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs e) =>
            _renderer?.UpdateRenderDimensions((int)e.NewSize.Width, (int)e.NewSize.Height);

        private void OnComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ComboBox cb) return;
            try
            {
                switch (cb.Name)
                {
                    case "RenderStyleComboBox" when cb.SelectedItem is RenderStyle rs:
                        SelectedDrawingType = rs;
                        break;
                    case "FftWindowTypeComboBox" when cb.SelectedItem is FftWindowType wt:
                        WindowType = wt;
                        break;
                    case "ScaleTypeComboBox" when cb.SelectedItem is SpectrumScale scale:
                        ScaleType = scale;
                        break;
                }
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error in combo box selection change: {ex}");
            }
        }

        private async void OnWindowClosed(object? sender, EventArgs e)
        {
            CompositionTarget.Rendering -= OnRendering;
            _renderElement.PaintSurface -= OnPaintSurface;
            SizeChanged -= OnWindowSizeChanged;
            StateChanged -= OnStateChanged;
            MouseDoubleClick -= OnWindowMouseDoubleClick;

            if (_captureManager != null)
            {
                try { await _captureManager.StopCaptureAsync(); }
                catch (Exception ex) { SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error stopping capture: {ex}"); }
                DisposeSafe(_captureManager, "capture manager");
                _captureManager = null;
            }

            DisposeSafe(_analyzer, "analyzer");
            _analyzer = null;
            DisposeSafe(_renderer, "renderer");
            _renderer = null;
            DisposeSafe(_disposables, "disposables");
            _disposables = null;
            DisposeSafe(_transitionSemaphore, "transition semaphore");
        }

        private void OnWindowDrag(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        private void OnWindowMouseDoubleClick(object? sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject originalElement)
            {
                DependencyObject? element = originalElement;
                while (element != null)
                {
                    if (element is CheckBox)
                    {
                        e.Handled = true;
                        return;
                    }
                    element = VisualTreeHelper.GetParent(element);
                }
            }
            if (e.ChangedButton == MouseButton.Left)
            {
                e.Handled = true;
                MaximizeWindow();
            }
        }

        private void OnSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (sender is not Slider slider) return;
            var sliderActions = new Dictionary<string, Action<double>>
            {
                ["barSpacingSlider"] = value => UpdateState(s => s with { BarSpacing = value }, nameof(BarSpacing)),
                ["barCountSlider"] = value => UpdateState(s => s with { BarCount = (int)value }, nameof(BarCount)),
                ["minDbLevelSlider"] = value => MinDbLevel = (float)value,
                ["maxDbLevelSlider"] = value => MaxDbLevel = (float)value,
                ["amplificationFactorSlider"] = value => AmplificationFactor = (float)value
            };
            if (sliderActions.TryGetValue(slider.Name, out var act))
                act(slider.Value);
        }

        private void OnButtonClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            try
            {
                var actions = new Dictionary<string, Action>
                {
                    ["StartCaptureButton"] = async () => await StartCaptureAsync(),
                    ["StopCaptureButton"] = async () => await StopCaptureAsync(),
                    ["OverlayButton"] = () => OnOverlayButtonClick(sender, e),
                    ["OpenSettingsButton"] = () => new SettingsWindow().ShowDialog(),
                    ["OpenPopupButton"] = () => IsPopupOpen = !IsPopupOpen,
                    ["MinimizeButton"] = MinimizeWindow,
                    ["MaximizeButton"] = MaximizeWindow,
                    ["CloseButton"] = CloseWindow
                };
                if (actions.TryGetValue(btn.Name, out var act))
                    act();
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error handling button click: {btn.Name} - {ex}");
            }
        }

        private void ToggleButtonContainer_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                ToggleControlPanelButton_Click(ToggleControlPanelButton, new RoutedEventArgs());
        }

        private void ToggleControlPanelButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (IsControlPanelVisible)
                {
                    if (FindResource("HidePanelAnimation") is Storyboard hidePanelSB)
                    {
                        hidePanelSB.Completed += (s, ev) =>
                        {
                            ControlPanel.Visibility = Visibility.Collapsed;
                            IsControlPanelVisible = false;
                            if (FindResource("HidePanelAndButtonAnimation") is Storyboard hidePanelAndButtonSB)
                                hidePanelAndButtonSB.Begin(this);
                        };
                        hidePanelSB.Begin(ControlPanel);
                    }
                }
                else
                {
                    ControlPanel.Visibility = Visibility.Visible;
                    if (FindResource("ShowPanelAndButtonAnimation") is Storyboard showPanelAndButtonSB)
                        showPanelAndButtonSB.Begin(this);
                    if (FindResource("ShowPanelAnimation") is Storyboard showPanelSB)
                        showPanelSB.Begin(ControlPanel);
                    IsControlPanelVisible = true;
                }
                var pulseTransform = new ScaleTransform(1.0, 1.0);
                var originalTransform = ToggleControlPanelButton.RenderTransform;
                ToggleControlPanelButton.RenderTransform = pulseTransform;
                var pulseAnimation = new DoubleAnimation
                {
                    From = 1.0,
                    To = 1.1,
                    Duration = TimeSpan.FromSeconds(0.15),
                    AutoReverse = true,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                pulseTransform.BeginAnimation(ScaleTransform.ScaleXProperty, pulseAnimation);
                pulseTransform.BeginAnimation(ScaleTransform.ScaleYProperty, pulseAnimation);
                pulseAnimation.Completed += (s, args) =>
                    ToggleControlPanelButton.RenderTransform = originalTransform;
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error toggling control panel: {ex}");
            }
        }

        private void OnOverlayButtonClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (IsOverlayActive)
                    CloseOverlay();
                else
                    OpenOverlay();
                SpectrumRendererFactory.ConfigureAllRenderers(IsOverlayActive);
                _renderElement?.InvalidateVisual();
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error toggling overlay: {ex}");
            }
        }

        #endregion

        #region Helper Methods

        private void UpdateOverlayTopmostState()
        {
            if (_overlayWindow?.IsInitialized == true)
                _overlayWindow.Topmost = IsOverlayTopmost;
        }

        private void UpdateRendererDimensions(int width, int height) =>
            _renderer?.UpdateRenderDimensions(width, height);

        private void CloseWindow() => Close();
        private void MinimizeWindow() => WindowState = WindowState.Minimized;
        private void MaximizeWindow() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void UpdateProps() =>
            OnPropertyChanged(nameof(IsRecording), nameof(CanStartCapture), nameof(StatusText));

        private void UpdateGainParameter(float newValue, Action<float> setter, string propertyName)
        {
            setter(newValue);
            OnPropertyChanged(propertyName);
            _renderElement?.InvalidateVisual();
        }

        private void UpdateState(Func<MainWindowSettings, MainWindowSettings> updater, string propertyName)
        {
            _state = updater(_state);
            OnPropertyChanged(propertyName);
        }

        private void UpdateToggleButtonContent()
        {
            if (ToggleButtonIcon?.RenderTransform is RotateTransform rt)
            {
                rt.BeginAnimation(
                    RotateTransform.AngleProperty,
                    new DoubleAnimation
                    {
                        To = IsControlPanelVisible ? 0 : 180,
                        Duration = TimeSpan.FromSeconds(0.3),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    }
                );
            }
        }

        protected bool SetField<T>(ref T field, T value, Action? callback = null, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;
            field = value;
            OnPropertyChanged(propertyName ?? string.Empty);
            callback?.Invoke();
            return true;
        }

        public void OnPropertyChanged(params string[] propertyNames)
        {
            foreach (var name in propertyNames)
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private void DisposeSafe(IDisposable? disposable, string name)
        {
            try
            {
                disposable?.Dispose();
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error disposing {name}: {ex}");
            }
        }

        #endregion
    }
}