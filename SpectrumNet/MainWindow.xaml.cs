#nullable enable

using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using MouseWheelEventArgs = System.Windows.Input.MouseWheelEventArgs;

namespace SpectrumNet
{
    public partial class MainWindow : System.Windows.Window, IAudioVisualizationController
    {
        private const string LogPrefix = "[MainWindow] ";

        // Core components
        private GLWpfControl? _renderElement;
        private SpectrumAnalyzer? _analyzer;
        private SpectrumBrushes? _spectrumStyles;
        private AudioCaptureManager? _captureManager;
        private GainParameters? _gainParameters;
        private VisualizationManager? _visualizationManager;

        // State flags
        private bool _isOverlayActive, _isPopupOpen, _isOverlayTopmost = true,
                    _isControlPanelVisible = true, _isTransitioning, _isDisposed,
                    _showPerformanceInfo = true;

        // Configuration state
        private RenderStyle _selectedDrawingType = RenderStyle.Bars;
        private FftWindowType _selectedFftWindowType = FftWindowType.Hann;
        private SpectrumScale _selectedScaleType = SpectrumScale.Linear;
        private string _statusText = "Ready";
        private GLWpfControlSettings _glSettings = new();

        // Synchronization
        private readonly SemaphoreSlim _transitionSemaphore = new(1, 1);
        private readonly CancellationTokenSource _cleanupCts = new();
        private CompositeDisposable? _disposables;
        private PropertyChangedEventHandler? _themePropertyChangedHandler;
        private OverlayWindow? _overlayWindow;

        // Core properties
        public GLWpfControlSettings GlSettings => _glSettings;
        public GLWpfControl SpectrumCanvas => _renderElement ?? throw new InvalidOperationException("Render element not initialized");
        public Renderer? Renderer { get => _visualizationManager?.Renderer; set => throw new InvalidOperationException("Renderer cannot be set directly when using VisualizationManager"); }
        public bool IsTransitioning => _isTransitioning;
        public new Dispatcher Dispatcher => System.Windows.Application.Current?.Dispatcher ?? throw new InvalidOperationException("Application.Current is null");

        #region IAudioVisualizationController Properties

        // Property implementations with validation and error handling
        public SpectrumAnalyzer Analyzer
        {
            get => _analyzer ?? throw new InvalidOperationException("Analyzer not initialized");
            set => _analyzer = value ?? throw new ArgumentNullException(nameof(Analyzer));
        }

        public SpectrumBrushes SpectrumStyles => _spectrumStyles ?? throw new InvalidOperationException("Spectrum styles not initialized");
        public GainParameters GainParameters => _gainParameters ?? throw new InvalidOperationException("Gain parameters not initialized");
        public bool CanStartCapture => _captureManager != null && !IsRecording;

        public int BarCount
        {
            get => Settings.Instance.UIBarCount;
            set
            {
                if (value <= 0)
                {
                    SmartLogger.Log(LogLevel.Warning, LogPrefix, $"Attempt to set invalid bar count: {value}");
                    value = DefaultSettings.UIBarCount;
                }
                Settings.Instance.UIBarCount = value;
                OnPropertyChanged(nameof(BarCount));
            }
        }

        public double BarSpacing
        {
            get => Settings.Instance.UIBarSpacing;
            set
            {
                if (value < 0)
                {
                    SmartLogger.Log(LogLevel.Warning, LogPrefix, $"Attempt to set negative spacing: {value}");
                    value = DefaultSettings.UIBarSpacing;
                }
                Settings.Instance.UIBarSpacing = value;
                OnPropertyChanged(nameof(BarSpacing));
            }
        }

        public bool IsOverlayActive
        {
            get => _isOverlayActive;
            set
            {
                if (_isOverlayActive == value) return;
                _isOverlayActive = value;
                OnPropertyChanged(nameof(IsOverlayActive));
                SpectrumRendererFactory.ConfigureAllRenderers(value);

                try { _renderElement?.InvalidateVisual(); }
                catch (Exception ex) { SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error updating visualization: {ex}"); }
            }
        }

        public bool IsRecording
        {
            get => _captureManager?.IsRecording ?? false;
            set
            {
                if (_captureManager?.IsRecording == value) return;
                if (value) _ = StartCaptureAsync();
                else _ = StopCaptureAsync();
                OnPropertyChanged(nameof(IsRecording), nameof(CanStartCapture));
            }
        }

        public bool ShowPerformanceInfo
        {
            get => _showPerformanceInfo;
            set
            {
                if (_showPerformanceInfo != value)
                {
                    _showPerformanceInfo = value;
                    OnPropertyChanged(nameof(ShowPerformanceInfo));
                    _visualizationManager?.RequestRender();
                }
            }
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
                    _visualizationManager?.RequestRender();
                    _renderElement?.InvalidateVisual();
                    Settings.Instance.SelectedScaleType = value;
                }
                catch (Exception ex) { SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error changing scale type: {ex}"); }
            }
        }

        public RenderStyle SelectedDrawingType
        {
            get => _selectedDrawingType;
            set
            {
                if (_selectedDrawingType == value) return;

                var oldStyle = _selectedDrawingType;
                _selectedDrawingType = value;
                OnPropertyChanged(nameof(SelectedDrawingType));
                Settings.Instance.SelectedRenderStyle = value;
                _visualizationManager?.HandleRenderStyleChanged(value);
                _renderElement?.InvalidateVisual();
            }
        }

        public RenderQuality RenderQuality
        {
            get => Settings.Instance.SelectedRenderQuality;
            set
            {
                if (Settings.Instance.SelectedRenderQuality == value) return;
                Settings.Instance.SelectedRenderQuality = value;
                OnPropertyChanged(nameof(RenderQuality));

                try
                {
                    SpectrumRendererFactory.GlobalQuality = value;
                    _visualizationManager?.RequestRender();
                    _renderElement?.InvalidateVisual();
                }
                catch (Exception ex) { SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error updating render quality: {ex}"); }
            }
        }

        public string SelectedStyle
        {
            get => Settings.Instance.SelectedPalette;
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    SmartLogger.Log(LogLevel.Warning, LogPrefix, "Attempt to set empty style name");
                    value = DefaultSettings.SelectedPalette;
                }

                if (Settings.Instance.SelectedPalette == value) return;
                Settings.Instance.SelectedPalette = value;
                OnPropertyChanged(nameof(SelectedStyle));
            }
        }

        public string StatusText
        {
            get => _statusText;
            set
            {
                if (_statusText == value) return;
                _statusText = value ?? string.Empty;
                OnPropertyChanged(nameof(StatusText));
            }
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
                    _analyzer?.SetWindowType(value);
                    _visualizationManager?.RequestRender();
                    _renderElement?.InvalidateVisual();
                    Settings.Instance.SelectedFftWindowType = value;
                }
                catch (Exception ex) { SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error changing FFT window type: {ex}"); }
            }
        }
        #endregion

        #region Additional Public Properties
        public static bool IsDarkTheme => ThemeManager.Instance?.IsDarkTheme ?? false;

        public static IEnumerable<RenderStyle> AvailableDrawingTypes =>
            Enum.GetValues<RenderStyle>().OrderBy(s => s.ToString());

        public static IEnumerable<FftWindowType> AvailableFftWindowTypes =>
            Enum.GetValues<FftWindowType>().OrderBy(wt => wt.ToString());

        public static IEnumerable<SpectrumScale> AvailableScaleTypes =>
            Enum.GetValues<SpectrumScale>().OrderBy(s => s.ToString());

        public static IEnumerable<RenderQuality> AvailableRenderQualities =>
            Enum.GetValues<RenderQuality>().OrderBy(q => (int)q);

        public IReadOnlyDictionary<string, Color4> AvailablePalettes =>
            _spectrumStyles?.RegisteredPalettes.OrderBy(kvp => kvp.Key).ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
            ?? new Dictionary<string, Color4>();

        public bool IsPopupOpen
        {
            get => _isPopupOpen;
            set => SetField(ref _isPopupOpen, value);
        }

        public bool IsOverlayTopmost
        {
            get => _isOverlayTopmost;
            set
            {
                if (SetField(ref _isOverlayTopmost, value, UpdateOverlayTopmostState))
                    Settings.Instance.IsOverlayTopmost = value;
            }
        }

        public Color4? SelectedPalette
        {
            get => AvailablePalettes.TryGetValue(SelectedStyle, out var color) ? color : null;
            set
            {
                if (value == null) return;
                var paletteName = AvailablePalettes.FirstOrDefault(p => p.Value.Equals(value)).Key;
                if (!string.IsNullOrEmpty(paletteName))
                {
                    SelectedStyle = paletteName;
                    OnPropertyChanged(nameof(SelectedPalette));
                }
            }
        }

        public bool IsControlPanelVisible
        {
            get => _isControlPanelVisible;
            set
            {
                if (SetField(ref _isControlPanelVisible, value))
                    Settings.Instance.IsControlPanelVisible = value;
            }
        }

        public float MinDbLevel
        {
            get => _gainParameters!.MinDbValue;
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
                Settings.Instance.UIMinDbLevel = value;
            }
        }

        public float MaxDbLevel
        {
            get => _gainParameters!.MaxDbValue;
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
                Settings.Instance.UIMaxDbLevel = value;
            }
        }

        public float AmplificationFactor
        {
            get => _gainParameters!.AmplificationFactor;
            set
            {
                if (_gainParameters == null) return;
                if (value < 0)
                {
                    SmartLogger.Log(LogLevel.Warning, LogPrefix, $"Amplification factor cannot be negative: {value}");
                    value = 0;
                }
                UpdateGainParameter(value, v => _gainParameters.AmplificationFactor = v, nameof(AmplificationFactor));
                Settings.Instance.UIAmplificationFactor = value;
            }
        }
        #endregion

        public event PropertyChangedEventHandler? PropertyChanged;

        public MainWindow()
        {
            try
            {
                InitializeComponent();

                _renderElement = FindName("OpenTkControl") as GLWpfControl
                    ?? throw new InvalidOperationException("Failed to find GLWpfControl in XAML");

                Loaded += OnWindowLoaded;

                var syncContext = SynchronizationContext.Current ??
                    throw new InvalidOperationException("No synchronization context. Window must be created in UI thread.");

                LoadAndApplySettings();
                _gainParameters = new GainParameters(
                    syncContext,
                    Settings.Instance.UIMinDbLevel,
                    Settings.Instance.UIMaxDbLevel,
                    Settings.Instance.UIAmplificationFactor
                );

                DataContext = this;
                InitEventHandlers();
                ConfigureTheme();
                UpdateProps();
                CompositionTarget.Rendering += OnRendering;
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Critical error during window initialization: {ex}");
                throw;
            }
        }

        #region Initialization
        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                InitializeOpenGL();
                InitComponents();
                InitializeRenderers();
                OnPropertyChanged(nameof(AvailablePalettes));

                ((PaletteNameToBrushConverter)Resources["PaletteNameToBrushConverter"]).BrushesProvider = SpectrumStyles;
                SmartLogger.Log(LogLevel.Information, LogPrefix, "Window loaded and initialized");
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Failed to initialize: {ex}");
                throw;
            }
        }

        private void InitializeOpenGL()
        {
            _glSettings = new GLWpfControlSettings
            {
                MajorVersion = 4,
                MinorVersion = 0
            };

            _renderElement!.Start(_glSettings);
        }

        private void OpenTkControl_OnRender(TimeSpan delta)
        {
            if (_renderElement != null && _renderElement.IsInitialized)
            {
                _visualizationManager?.Renderer?.OnGlControlRender(delta);
            }
        }

        private void InitComponents()
        {
            try
            {
                if (_renderElement == null)
                    throw new InvalidOperationException("OpenGL control not initialized");

                var syncContext = SynchronizationContext.Current ??
                    throw new InvalidOperationException("SynchronizationContext.Current is null");

                _spectrumStyles = new SpectrumBrushes();
                _disposables = new CompositeDisposable();
                _analyzer = new SpectrumAnalyzer(
                    new FftProcessor { WindowType = _selectedFftWindowType },
                    new SpectrumConverter(_gainParameters),
                    syncContext
                );
                _disposables.Add(_analyzer);

                IAudioDeviceService deviceService = new DefaultAudioDeviceService();
                IOpenGLService glService = new OpenGLService();

                _captureManager = new AudioCaptureManager(this, deviceService, glService);
                _disposables.Add(_captureManager);
                _visualizationManager = new VisualizationManager(this, _renderElement, glService);
                _visualizationManager.Initialize(_analyzer, _spectrumStyles);
                _disposables.Add(_visualizationManager);

                Task.Delay(200).ContinueWith(_ =>
                {
                    _visualizationManager.CameraController?.ActivateCamera();
                }, TaskScheduler.FromCurrentSynchronizationContext());

                OnPropertyChanged(nameof(CanStartCapture), nameof(IsRecording));
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Component initialization failed: {ex}");
                throw;
            }
        }

        private void InitializeRenderers()
        {
            try
            {
                foreach (RenderStyle style in Enum.GetValues(typeof(RenderStyle)))
                {
                    var renderer = SpectrumRendererFactory.CreateRenderer(style, false);
                }
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error initializing renderers: {ex}");
            }
        }

        private void InitEventHandlers()
        {
            SizeChanged += OnWindowSizeChanged;
            StateChanged += OnStateChanged;
            MouseDoubleClick += OnWindowMouseDoubleClick;
            Closed += OnWindowClosed;
            KeyDown += MainWindow_KeyDown;
            KeyUp += MainWindow_KeyUp;
            LocationChanged += OnWindowLocationChanged;
            MouseMove += Window_MouseMove;
            MouseWheel += Window_MouseWheel;

            if (_renderElement != null)
            {
                _renderElement.SizeChanged += OnRenderElementSizeChanged;
            }
            else
            {
                throw new InvalidOperationException("_renderElement not initialized");
            }

            PropertyChanged += (_, args) =>
            {
                if (args?.PropertyName == nameof(IsRecording))
                    StatusText = IsRecording ? "Recording..." : "Ready";
            };
        }

        private void OnRenderElementSizeChanged(object sender, SizeChangedEventArgs e)
        {
            try
            {
                _visualizationManager?.UpdateRenderDimensions((int)e.NewSize.Width, (int)e.NewSize.Height);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error updating render dimensions: {ex}");
            }
        }

        private void ConfigureTheme()
        {
            var tm = ThemeManager.Instance;
            if (tm != null)
            {
                tm.RegisterWindow(this);
                _themePropertyChangedHandler = (_, e) =>
                {
                    if (e?.PropertyName == nameof(ThemeManager.IsDarkTheme))
                    {
                        OnPropertyChanged(nameof(IsDarkTheme));
                        Settings.Instance.IsDarkTheme = tm.IsDarkTheme;
                    }
                };
                tm.PropertyChanged += _themePropertyChangedHandler;
            }
        }
        #endregion

        #region Settings Management
        private void LoadAndApplySettings()
        {
            try
            {
                SettingsWindow.Instance.LoadSettings();
                ApplyWindowSettings();
                EnsureWindowIsVisible();
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error loading settings: {ex.Message}");
            }
        }

        private void ApplyWindowSettings()
        {
            try
            {
                // Window positioning
                Left = Settings.Instance.WindowLeft;
                Top = Settings.Instance.WindowTop;
                Width = Settings.Instance.WindowWidth;
                Height = Settings.Instance.WindowHeight;
                WindowState = Settings.Instance.WindowState;

                // Application settings
                IsControlPanelVisible = Settings.Instance.IsControlPanelVisible;
                IsOverlayTopmost = Settings.Instance.IsOverlayTopmost;
                SelectedDrawingType = Settings.Instance.SelectedRenderStyle;
                WindowType = Settings.Instance.SelectedFftWindowType;
                ScaleType = Settings.Instance.SelectedScaleType;
                RenderQuality = Settings.Instance.SelectedRenderQuality;
                SelectedStyle = Settings.Instance.SelectedPalette;
                ThemeManager.Instance?.SetTheme(Settings.Instance.IsDarkTheme);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error applying settings: {ex.Message}");
            }
        }

        private void EnsureWindowIsVisible()
        {
            try
            {
                var screenRect = new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
                var windowRect = new Rect(Left, Top, Width, Height);

                if (!screenRect.IntersectsWith(windowRect))
                {
                    Left = (screenRect.Width - Width) / 2;
                    Top = (screenRect.Height - Height) / 2;
                }
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error ensuring window visibility: {ex.Message}");
            }
        }

        private void OnWindowLocationChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Normal)
            {
                Settings.Instance.WindowLeft = Left;
                Settings.Instance.WindowTop = Top;
            }
        }
        #endregion

        #region Capture Management
        public async Task StartCaptureAsync()
        {
            if (_captureManager == null)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Capture manager not initialized");
                return;
            }

            try
            {
                await _captureManager.StartCaptureAsync();
                _visualizationManager?.RequestRender();
                UpdateProps();
                _renderElement?.InvalidateVisual();
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error starting capture: {ex}");
                StatusText = $"Error: {ex.Message.Substring(0, Math.Min(50, ex.Message.Length))}...";
                OnPropertyChanged(nameof(CanStartCapture));
            }
        }

        public async Task StopCaptureAsync()
        {
            if (_captureManager == null)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Capture manager not initialized");
                return;
            }

            try
            {
                await _captureManager.StopCaptureAsync();
                UpdateProps();
                _renderElement?.InvalidateVisual();
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error stopping capture: {ex}");
                StatusText = $"Error: {ex.Message.Substring(0, Math.Min(50, ex.Message.Length))}...";
                OnPropertyChanged(nameof(CanStartCapture));
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
                            RenderInterval: 16,
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
            try { _overlayWindow?.Close(); }
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

        private void OnOverlayButtonClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (IsOverlayActive) CloseOverlay(); else OpenOverlay();
                SpectrumRendererFactory.ConfigureAllRenderers(IsOverlayActive);
                _renderElement?.InvalidateVisual();
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error toggling overlay: {ex}");
            }
        }

        #endregion

        #region Event Handlers

        private void OnRendering(object? sender, EventArgs? e)
        {
            try { _renderElement?.InvalidateVisual(); }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error updating visualization: {ex}");
            }
        }

        private void OnStateChanged(object? sender, EventArgs? e)
        {
            if (MaximizeButton == null || MaximizeIcon == null) return;

            try
            {
                MaximizeIcon.Data = Geometry.Parse(WindowState == System.Windows.WindowState.Maximized
                    ? "M0,0 L20,0 L20,20 L0,20 Z"
                    : "M2,2 H18 V18 H2 Z");

                Settings.Instance.WindowState = WindowState;
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error changing icon: {ex}");
            }
        }

        private void OnThemeToggleButtonChanged(object? sender, RoutedEventArgs? e)
        {
            try { ThemeManager.Instance?.ToggleTheme(); }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error toggling theme: {ex}");
            }
        }

        private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs? e)
        {
            if (e == null) return;

            try
            {
                if (WindowState == System.Windows.WindowState.Normal)
                {
                    Settings.Instance.WindowWidth = Width;
                    Settings.Instance.WindowHeight = Height;
                }
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error updating window size settings: {ex}");
            }
        }

        private void OnComboBoxSelectionChanged(object? sender, SelectionChangedEventArgs? e)
        {
            if (sender is not System.Windows.Controls.ComboBox cb || e == null) return;

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
                    case "RenderQualityComboBox" when cb.SelectedItem is RenderQuality quality:
                        RenderQuality = quality;
                        break;
                }
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error handling selection change: {ex}");
            }
        }

        private async void OnWindowClosed(object? sender, EventArgs? e)
        {
            try
            {
                SettingsWindow.Instance.SaveSettings();
                _cleanupCts.Cancel();
                CompositionTarget.Rendering -= OnRendering;

                SizeChanged -= OnWindowSizeChanged;
                StateChanged -= OnStateChanged;
                MouseDoubleClick -= OnWindowMouseDoubleClick;
                Closed -= OnWindowClosed;
                LocationChanged -= OnWindowLocationChanged;
                MouseWheel -= Window_MouseWheel;

                if (_captureManager != null)
                {
                    try { await _captureManager.StopCaptureAsync(); }
                    catch (Exception ex) { SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error stopping capture: {ex}"); }
                    DisposeSafe(_captureManager, "capture manager");
                    _captureManager = null;
                }

                DisposeSafe(_analyzer, "analyzer");
                _analyzer = null;
                DisposeSafe(_visualizationManager, "visualization manager");
                _visualizationManager = null;
                DisposeSafe(_disposables, "disposables");
                _disposables = null;
                DisposeSafe(_transitionSemaphore, "transition semaphore");
                DisposeSafe(_cleanupCts, "cleanup token source");

                if (_themePropertyChangedHandler != null && ThemeManager.Instance != null)
                    ThemeManager.Instance.PropertyChanged -= _themePropertyChangedHandler;

                if (_overlayWindow != null)
                {
                    _overlayWindow.Close();
                    DisposeSafe(_overlayWindow, "overlay window");
                    _overlayWindow = null;
                }

                _isDisposed = true;
                System.Windows.Application.Current?.Shutdown();
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error releasing resources: {ex}");
            }
        }

        private void OnWindowDrag(object? sender, System.Windows.Input.MouseButtonEventArgs? e)
        {
            if (e?.ChangedButton == System.Windows.Input.MouseButton.Left)
            {
                try
                {
                    DragMove();
                    if (WindowState == System.Windows.WindowState.Normal)
                    {
                        Settings.Instance.WindowLeft = Left;
                        Settings.Instance.WindowTop = Top;
                    }
                }
                catch (Exception ex) { SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error moving window: {ex}"); }
            }
        }

        private void OnWindowMouseDoubleClick(object? sender, System.Windows.Input.MouseButtonEventArgs? e)
        {
            if (e == null) return;

            try
            {
                if (e.OriginalSource is DependencyObject originalElement)
                {
                    DependencyObject? element = originalElement;
                    while (element != null)
                    {
                        if (element is System.Windows.Controls.CheckBox)
                        {
                            e.Handled = true;
                            return;
                        }
                        element = VisualTreeHelper.GetParent(element);
                    }
                }

                if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
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

        private void OnSliderValueChanged(object? sender, RoutedPropertyChangedEventArgs<double>? e)
        {
            if (sender is not Slider slider || e == null) return;

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
            if (sender is not System.Windows.Controls.Button btn) return;

            try
            {
                var actions = new Dictionary<string, Action>
                {
                    ["StartCaptureButton"] = async () => await StartCaptureAsync(),
                    ["StopCaptureButton"] = async () => await StopCaptureAsync(),
                    ["OverlayButton"] = () => OnOverlayButtonClick(sender, e),
                    ["OpenSettingsButton"] = () =>
                    {
                        if (this.OpenSettingsButton != null)
                        {
                            this.OpenSettingsButton.IsEnabled = false;
                            try { new SettingsWindow().ShowDialog(); }
                            finally { this.OpenSettingsButton.IsEnabled = true; }
                        }
                    },
                    ["OpenPopupButton"] = () => IsPopupOpen = !IsPopupOpen,
                    ["MinimizeButton"] = MinimizeWindow,
                    ["MaximizeButton"] = MaximizeWindow,
                    ["CloseButton"] = CloseWindow,
                    ["ResetCameraButton"] = () => {
                        _visualizationManager?.CameraController?.ResetCamera();
                    }
                };

                if (actions.TryGetValue(btn.Name, out var act))
                    act();
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error handling button click: {btn.Name} - {ex}");
            }
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            try
            {
                _visualizationManager?.CameraController?.HandleMouseMove(e);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error handling mouse move: {ex}");
            }
        }

        private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            try
            {
                _visualizationManager?.CameraController?.HandleMouseWheel(e);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error handling mouse wheel: {ex}");
            }
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                _visualizationManager?.CameraController?.HandleKeyDown(e);

                if (!e.Handled)
                {
                    switch (e.Key)
                    {
                        case Key.F10:
                            RenderQuality = RenderQuality.Low;
                            e.Handled = true;
                            break;
                        case Key.F11:
                            RenderQuality = RenderQuality.Medium;
                            e.Handled = true;
                            break;
                        case Key.F12:
                            RenderQuality = RenderQuality.High;
                            e.Handled = true;
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error handling key down: {ex}");
            }
        }

        private void MainWindow_KeyUp(object sender, KeyEventArgs e)
        {
            try
            {
                _visualizationManager?.CameraController?.HandleKeyUp(e);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error handling key up: {ex}");
            }
        }

        private void ToggleButtonContainer_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                IsControlPanelVisible = !IsControlPanelVisible;
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
                _visualizationManager?.UpdateRenderDimensions(width, height);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error updating renderer dimensions: {ex}");
            }
        }

        private void CloseWindow() => Close();

        private void MinimizeWindow() => WindowState = System.Windows.WindowState.Minimized;

        private void MaximizeWindow() =>
            WindowState = WindowState == System.Windows.WindowState.Maximized
                ? System.Windows.WindowState.Normal
                : System.Windows.WindowState.Maximized;

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

        protected bool SetField<T>(ref T field, T value, Action? callback = null, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName ?? string.Empty);

            try { callback?.Invoke(); }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error executing callback in SetField: {ex}");
            }

            return true;
        }

        public void OnPropertyChanged(params string[] propertyNames)
        {
            foreach (var name in propertyNames)
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private void DisposeSafe(IDisposable? disposable, string name)
        {
            if (disposable == null) return;

            try { disposable.Dispose(); }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error disposing resource {name}: {ex}");
            }
        }

        #endregion
    }
}