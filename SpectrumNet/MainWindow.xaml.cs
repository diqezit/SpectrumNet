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
        private readonly CancellationTokenSource _cleanupCts = new();
        private MainWindowSettings _state = new();
        private bool _isOverlayActive, _isPopupOpen,
                     _isOverlayTopmost = true,
                     _isControlPanelVisible = true,
                     _isTransitioning,
                     _isDisposed;
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
            get => _analyzer ?? throw new InvalidOperationException("Analyzer not initialized");
            set => _analyzer = value ?? throw new ArgumentNullException(nameof(Analyzer));
        }

        public int BarCount
        {
            get => _state.BarCount;
            set
            {
                if (value <= 0)
                {
                    SmartLogger.Log(LogLevel.Warning, LogPrefix, $"Attempt to set invalid bar count: {value}");
                    value = MwConstants.BarCount;
                }
                UpdateState(s => s with { BarCount = value }, nameof(BarCount));
            }
        }

        public double BarSpacing
        {
            get => _state.BarSpacing;
            set
            {
                if (value < 0)
                {
                    SmartLogger.Log(LogLevel.Warning, LogPrefix, $"Attempt to set negative spacing: {value}");
                    value = MwConstants.BarSpacing;
                }
                UpdateState(s => s with { BarSpacing = value }, nameof(BarSpacing));
            }
        }

        public bool CanStartCapture => _captureManager != null && !IsRecording;

        public new Dispatcher Dispatcher => Application.Current.Dispatcher;

        public GainParameters GainParameters => _gainParameters ??
            throw new InvalidOperationException("Gain parameters not initialized");

        public bool IsOverlayActive
        {
            get => _isOverlayActive;
            set
            {
                if (_isOverlayActive == value) return;
                _isOverlayActive = value;
                OnPropertyChanged(nameof(IsOverlayActive));
                SpectrumRendererFactory.ConfigureAllRenderers(value);

                try
                {
                    _renderElement?.InvalidateVisual();
                }
                catch (Exception ex)
                {
                    SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error updating visualization: {ex}");
                }
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

        public Renderer? Renderer
        {
            get => _renderer;
            set => _renderer = value;
        }

        public SpectrumScale ScaleType
        {
            get => _selectedScaleType;
            set
            {
                if (_selectedScaleType == value) return;
                _selectedScaleType = value;
                OnPropertyChanged(nameof(ScaleType));

                try
                {
                    _analyzer?.SetScaleType(value);
                    _renderer?.RequestRender();
                    _renderElement?.InvalidateVisual();
                }
                catch (Exception ex)
                {
                    SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error changing scale type: {ex}");
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
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    SmartLogger.Log(LogLevel.Warning, LogPrefix, "Attempt to set empty style name");
                    value = MwConstants.DefaultStyle;
                }
                UpdateState(s => s with { Style = value }, nameof(SelectedStyle));
            }
        }

        public SKElement SpectrumCanvas => _renderElement ??
            throw new InvalidOperationException("Render element not initialized");

        public SpectrumBrushes SpectrumStyles => _spectrumStyles ??
            throw new InvalidOperationException("Spectrum styles not initialized");

        public string StatusText
        {
            get => _state.StatusText;
            set => UpdateState(s => s with { StatusText = value ?? string.Empty }, nameof(StatusText));
        }

        public FftWindowType WindowType
        {
            get => _selectedFftWindowType;
            set
            {
                if (_selectedFftWindowType == value) return;
                _selectedFftWindowType = value;
                OnPropertyChanged(nameof(WindowType));

                try
                {
                    if (_analyzer != null)
                    {
                        _analyzer.SetWindowType(value);
                        _renderer?.RequestRender();
                        _renderElement?.InvalidateVisual();
                    }
                }
                catch (Exception ex)
                {
                    SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error changing FFT window type: {ex}");
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
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value) ??
                new Dictionary<string, StyleDefinition>();

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
            get => _gainParameters?.MinDbValue ?? SharedConstants.DefaultMinDb;
            set
            {
                if (_gainParameters == null) return;

                if (value >= _gainParameters.MaxDbValue)
                {
                    SmartLogger.Log(LogLevel.Warning, LogPrefix,
                        $"Min dB level ({value}) must be less than max ({_gainParameters.MaxDbValue})");
                    value = _gainParameters.MaxDbValue - 1;
                }

                UpdateGainParameter(value, v => _gainParameters.MinDbValue = v, nameof(MinDbLevel));
            }
        }

        public float MaxDbLevel
        {
            get => _gainParameters?.MaxDbValue ?? SharedConstants.DefaultMaxDb;
            set
            {
                if (_gainParameters == null) return;

                if (value <= _gainParameters.MinDbValue)
                {
                    SmartLogger.Log(LogLevel.Warning, LogPrefix,
                        $"Max dB level ({value}) must be greater than min ({_gainParameters.MinDbValue})");
                    value = _gainParameters.MinDbValue + 1;
                }

                UpdateGainParameter(value, v => _gainParameters.MaxDbValue = v, nameof(MaxDbLevel));
            }
        }

        public float AmplificationFactor
        {
            get => _gainParameters?.AmplificationFactor ?? SharedConstants.DefaultAmplificationFactor;
            set
            {
                if (_gainParameters == null) return;

                if (value < 0)
                {
                    SmartLogger.Log(LogLevel.Warning, LogPrefix, $"Amplification factor cannot be negative: {value}");
                    value = 0;
                }

                UpdateGainParameter(value, v => _gainParameters.AmplificationFactor = v, nameof(AmplificationFactor));
            }
        }

        #endregion

        public event PropertyChangedEventHandler? PropertyChanged;

        public MainWindow()
        {
            try
            {
                InitializeComponent();
                var syncContext = SynchronizationContext.Current ??
                    throw new InvalidOperationException("No synchronization context. Window must be created in UI thread.");

                _gainParameters = new GainParameters(
                    syncContext,
                    SharedConstants.DefaultMinDb,
                    SharedConstants.DefaultMaxDb,
                    SharedConstants.DefaultAmplificationFactor
                );

                if (_gainParameters == null)
                {
                    throw new InvalidOperationException("Failed to create gain parameters");
                }

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
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Critical error during window initialization: {ex}");
                throw;
            }
        }

        #region Initialization

        private void InitComponents()
        {
            try
            {
                _renderElement = spectrumCanvas ??
                    throw new InvalidOperationException("Canvas not found in window template");

                _spectrumStyles = new SpectrumBrushes();
                if (_spectrumStyles == null)
                {
                    throw new InvalidOperationException("Failed to create SpectrumBrushes");
                }

                _disposables = new CompositeDisposable();
                if (_disposables == null)
                {
                    throw new InvalidOperationException("Failed to create CompositeDisposable");
                }

                var syncContext = SynchronizationContext.Current ??
                    throw new InvalidOperationException("SynchronizationContext.Current is null");

                _analyzer = new SpectrumAnalyzer(
                    new FftProcessor { WindowType = WindowType },
                    new SpectrumConverter(_gainParameters),
                    syncContext
                );

                if (_analyzer == null)
                {
                    throw new InvalidOperationException("Failed to create SpectrumAnalyzer");
                }

                _disposables.Add(_analyzer);

                _captureManager = new AudioCaptureManager(this);
                if (_captureManager == null)
                {
                    throw new InvalidOperationException("Failed to create AudioCaptureManager");
                }

                _disposables.Add(_captureManager);

                _renderer = new Renderer(_spectrumStyles, this, _analyzer, _renderElement);
                if (_renderer == null)
                {
                    throw new InvalidOperationException("Failed to create Renderer");
                }

                _disposables.Add(_renderer);

                SelectedDrawingType = RenderStyle.Bars;
                UpdateState(s => s with { Style = MwConstants.DefaultStyle }, nameof(SelectedStyle));
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Critical error initializing components: {ex}");
                throw new InvalidOperationException("Failed to initialize components", ex);
            }
        }

        private void InitEventHandlers()
        {
            if (_renderElement != null)
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
            if (_captureManager == null)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Attempt to start capture with no CaptureManager");
                return;
            }

            try
            {
                await _captureManager.StartCaptureAsync();
                UpdateProps();
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error starting audio capture: {ex}");
            }
        }

        public async Task StopCaptureAsync()
        {
            if (_captureManager == null)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Attempt to stop capture with no CaptureManager");
                return;
            }

            try
            {
                await _captureManager.StopCaptureAsync();
                UpdateProps();
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error stopping audio capture: {ex}");
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

                    if (_overlayWindow == null)
                    {
                        SmartLogger.Log(LogLevel.Error, LogPrefix, "Failed to create overlay window");
                        return;
                    }

                    _overlayWindow.Closed += (_, _) => OnOverlayClosed();
                    _overlayWindow.Show();
                }

                IsOverlayActive = true;
                SpectrumRendererFactory.ConfigureAllRenderers(true);
                _renderElement?.InvalidateVisual();
                UpdateRendererDimensions((int)SystemParameters.PrimaryScreenWidth,
                                         (int)SystemParameters.PrimaryScreenHeight);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error opening overlay window: {ex}");
            }
        }

        private void CloseOverlay()
        {
            try
            {
                _overlayWindow?.Close();
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error closing overlay: {ex}");
                if (_overlayWindow != null)
                {
                    DisposeSafe(_overlayWindow, "overlay window");
                    _overlayWindow = null;
                    IsOverlayActive = false;
                }
            }
        }

        private void OnOverlayClosed()
        {
            try
            {
                if (_overlayWindow != null)
                {
                    DisposeSafe(_overlayWindow, "overlay window");
                    _overlayWindow = null;
                }

                IsOverlayActive = false;
                SpectrumRendererFactory.ConfigureAllRenderers(false);
                _renderElement?.InvalidateVisual();
                Activate();
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error handling overlay closed: {ex}");
            }
        }

        #endregion

        #region Event Handlers

        public void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
        {
            if (e == null)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "PaintSurface called with null arguments");
                return;
            }

            if (IsOverlayActive && sender == _renderElement)
            {
                e.Surface.Canvas.Clear(SKColors.Transparent);
                return;
            }

            try
            {
                _renderer?.RenderFrame(sender, e);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error rendering frame: {ex}");
                try { e.Surface.Canvas.Clear(SKColors.Transparent); }
                catch { /* ignore cleanup errors */ }
            }
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            try
            {
                _renderElement?.InvalidateVisual();
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error updating visualization: {ex}");
            }
        }

        private void OnStateChanged(object? sender, EventArgs e)
        {
            if (MaximizeButton == null || MaximizeIcon == null) return;

            try
            {
                MaximizeIcon.Data = Geometry.Parse(WindowState == WindowState.Maximized
                    ? "M0,0 L20,0 L20,20 L0,20 Z"
                    : "M2,2 H18 V18 H2 Z");
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error changing icon: {ex}");
            }
        }

        private void OnThemeToggleButtonChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                ThemeManager.Instance.ToggleTheme();
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error toggling theme: {ex}");
            }
        }

        private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs e)
        {
            if (e == null)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "SizeChanged called with null arguments");
                return;
            }

            try
            {
                _renderer?.UpdateRenderDimensions((int)e.NewSize.Width, (int)e.NewSize.Height);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error updating dimensions: {ex}");
            }
        }

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
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error handling selection change: {ex}");
            }
        }

        private async void OnWindowClosed(object? sender, EventArgs e)
        {
            try
            {
                _cleanupCts.Cancel();

                CompositionTarget.Rendering -= OnRendering;

                if (_renderElement != null)
                    _renderElement.PaintSurface -= OnPaintSurface;

                SizeChanged -= OnWindowSizeChanged;
                StateChanged -= OnStateChanged;
                MouseDoubleClick -= OnWindowMouseDoubleClick;
                Closed -= OnWindowClosed;

                if (_captureManager != null)
                {
                    try
                    {
                        await _captureManager.StopCaptureAsync();
                    }
                    catch (Exception ex)
                    {
                        SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error stopping capture: {ex}");
                    }
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
                DisposeSafe(_cleanupCts, "cleanup token source");

                if (_themePropertyChangedHandler != null && ThemeManager.Instance != null)
                {
                    ThemeManager.Instance.PropertyChanged -= _themePropertyChangedHandler;
                }

                if (_overlayWindow != null)
                {
                    _overlayWindow.Close();
                    _overlayWindow.Dispose(); 
                    _overlayWindow = null;
                }

                _isDisposed = true;

                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error releasing resources: {ex}");
            }
        }

        private void OnWindowDrag(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try
                {
                    DragMove();
                }
                catch (Exception ex)
                {
                    SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error moving window: {ex}");
                }
            }
        }

        private void OnWindowMouseDoubleClick(object? sender, MouseButtonEventArgs e)
        {
            if (e == null) return;

            try
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
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error handling double click: {ex}");
            }
        }

        private void OnSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (sender is not Slider slider) return;

            try
            {
                var sliderActions = new Dictionary<string, Action<double>>
                {
                    ["barSpacingSlider"] = value => BarSpacing = value,
                    ["barCountSlider"] = value => BarCount = (int)value,
                    ["minDbLevelSlider"] = value => MinDbLevel = (float)value,
                    ["maxDbLevelSlider"] = value => MaxDbLevel = (float)value,
                    ["amplificationFactorSlider"] = value => AmplificationFactor = (float)value
                };

                if (sliderActions.TryGetValue(slider.Name, out var action))
                    action(slider.Value);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error handling slider change: {ex}");
            }
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
                if (ControlPanel == null || ToggleControlPanelButton == null)
                {
                    SmartLogger.Log(LogLevel.Error, LogPrefix, "UI elements not found");
                    return;
                }

                if (IsControlPanelVisible)
                {
                    if (FindResource("HidePanelAnimation") is Storyboard hidePanelSB)
                    {
                        hidePanelSB.Completed += (s, ev) =>
                        {
                            try
                            {
                                ControlPanel.Visibility = Visibility.Collapsed;
                                IsControlPanelVisible = false;
                                if (FindResource("HidePanelAndButtonAnimation") is Storyboard hidePanelAndButtonSB)
                                    hidePanelAndButtonSB.Begin(this);
                            }
                            catch (Exception ex)
                            {
                                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error in hide panel animation: {ex}");
                            }
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
            try
            {
                if (_overlayWindow?.IsInitialized == true)
                    _overlayWindow.Topmost = IsOverlayTopmost;
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error updating topmost state: {ex}");
            }
        }

        private void UpdateRendererDimensions(int width, int height)
        {
            try
            {
                if (width <= 0 || height <= 0)
                {
                    SmartLogger.Log(LogLevel.Warning, LogPrefix, $"Invalid dimensions for renderer: {width}x{height}");
                    return;
                }

                _renderer?.UpdateRenderDimensions(width, height);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error updating renderer dimensions: {ex}");
            }
        }

        private void CloseWindow() => Close();

        private void MinimizeWindow() => WindowState = WindowState.Minimized;

        private void MaximizeWindow() =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void UpdateProps() =>
                    OnPropertyChanged(nameof(IsRecording), nameof(CanStartCapture), nameof(StatusText));

        private void UpdateGainParameter(float newValue, Action<float> setter, string propertyName)
        {
            if (setter == null)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Null delegate passed for parameter update");
                return;
            }

            try
            {
                setter(newValue);
                OnPropertyChanged(propertyName);
                _renderElement?.InvalidateVisual();
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error updating gain parameter: {ex}");
            }
        }

        private void UpdateState(Func<MainWindowSettings, MainWindowSettings> updater, string propertyName)
        {
            if (updater == null)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Null delegate passed for state update");
                return;
            }

            try
            {
                _state = updater(_state);
                OnPropertyChanged(propertyName);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error updating state: {ex}");
            }
        }

        private void UpdateToggleButtonContent()
        {
            try
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
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error updating toggle button: {ex}");
            }
        }

        protected bool SetField<T>(ref T field, T value, Action? callback = null, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName ?? string.Empty);

            try
            {
                callback?.Invoke();
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error executing callback in SetField: {ex}");
            }

            return true;
        }

        public void OnPropertyChanged(params string[] propertyNames)
        {
            try
            {
                foreach (var name in propertyNames)
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error notifying property change: {ex}");
            }
        }

        private void DisposeSafe(IDisposable? disposable, string name)
        {
            if (disposable == null) return;

            try
            {
                disposable.Dispose();
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error disposing resource {name}: {ex}");
            }
        }

        #endregion
    }
}