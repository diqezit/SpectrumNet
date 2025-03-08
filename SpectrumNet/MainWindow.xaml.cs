﻿#nullable enable

using OpenTK.Windowing.Common;
using System.Runtime;

namespace SpectrumNet
{
    public partial class MainWindow : System.Windows.Window, IAudioVisualizationController
    {
        private const string LogPrefix = "[MainWindow] ";

        private GLWpfControl? _renderElement;
        private readonly SemaphoreSlim _transitionSemaphore = new(1, 1);
        private readonly CancellationTokenSource _cleanupCts = new();
        private bool _isOverlayActive, _isPopupOpen,
                     _isOverlayTopmost = true,
                     _isControlPanelVisible = true,
                     _isTransitioning,
                     _isDisposed,
                     _showPerformanceInfo = true;
        private RenderStyle _selectedDrawingType = RenderStyle.Bars;
        private FftWindowType _selectedFftWindowType = FftWindowType.Hann;
        private SpectrumScale _selectedScaleType = SpectrumScale.Linear;
        private OverlayWindow? _overlayWindow;
        private Renderer? _renderer;
        private SpectrumBrushes? _spectrumStyles;
        private SpectrumAnalyzer? _analyzer;
        private AudioCaptureManager? _captureManager;
        private CompositeDisposable? _disposables;
        private GainParameters? _gainParameters;
        private PropertyChangedEventHandler? _themePropertyChangedHandler;
        private string _statusText = "Ready";

        private GLWpfControlSettings _glSettings = new GLWpfControlSettings();
        public GLWpfControlSettings GlSettings => _glSettings;

        #region IAudioVisualizationController Properties

        public SpectrumAnalyzer Analyzer
        {
            get => _analyzer ?? throw new InvalidOperationException("Analyzer not initialized");
            set => _analyzer = value ?? throw new ArgumentNullException(nameof(Analyzer));
        }

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

        public bool CanStartCapture => _captureManager != null && !IsRecording;

        public new Dispatcher Dispatcher => System.Windows.Application.Current?.Dispatcher ?? throw new InvalidOperationException("Application.Current is null");

        public GainParameters GainParameters => _gainParameters ?? throw new InvalidOperationException("Gain parameters not initialized");

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

        public bool ShowPerformanceInfo
        {
            get => _showPerformanceInfo;
            set
            {
                if (_showPerformanceInfo != value)
                {
                    _showPerformanceInfo = value;
                    OnPropertyChanged(nameof(ShowPerformanceInfo));
                    _renderer?.RequestRender();
                }
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
                    Settings.Instance.SelectedScaleType = value;
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
                Settings.Instance.SelectedRenderStyle = value;
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
                    _renderer?.RequestRender();
                    _renderElement?.InvalidateVisual();

                    SmartLogger.Log(LogLevel.Information, LogPrefix, $"Render quality set to {value}");
                }
                catch (Exception ex)
                {
                    SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error updating render quality: {ex}");
                }
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

        // Изменено для работы с GLWpfControl
        public GLWpfControl SpectrumCanvas => _renderElement ?? throw new InvalidOperationException("Render element not initialized");

        public SpectrumBrushes SpectrumStyles => _spectrumStyles ?? throw new InvalidOperationException("Spectrum styles not initialized");

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
                    _renderer?.RequestRender();
                    _renderElement?.InvalidateVisual();
                    Settings.Instance.SelectedFftWindowType = value;
                }
                catch (Exception ex)
                {
                    SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error changing FFT window type: {ex}");
                }
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

        public IReadOnlyDictionary<string, Palette> AvailablePalettes =>
            _spectrumStyles?.RegisteredPalettes
                .OrderBy(kvp => kvp.Key)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
            ?? new Dictionary<string, Palette>();

        public static IEnumerable<RenderQuality> AvailableRenderQualities =>
            Enum.GetValues<RenderQuality>().OrderBy(q => (int)q);

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
                {
                    Settings.Instance.IsOverlayTopmost = value;
                }
            }
        }

        public Palette? SelectedPalette
        {
            get
            {
                if (AvailablePalettes.TryGetValue(SelectedStyle, out var palette))
                    return palette;
                return null;
            }
            set
            {
                if (value == null)
                    return;
                SelectedStyle = value.Name;
                OnPropertyChanged(nameof(SelectedPalette));
            }
        }

        public bool IsControlPanelVisible
        {
            get => _isControlPanelVisible;
            set
            {
                if (SetField(ref _isControlPanelVisible, value))
                {
                    Settings.Instance.IsControlPanelVisible = value;
                }
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
                var syncContext = SynchronizationContext.Current ??
                    throw new InvalidOperationException("No synchronization context. Window must be created in UI thread.");

                LoadAndApplySettings();

                _gainParameters = new GainParameters(
                    syncContext,
                    Settings.Instance.UIMinDbLevel,
                    Settings.Instance.UIMaxDbLevel,
                    Settings.Instance.UIAmplificationFactor
                ) ?? throw new InvalidOperationException("Failed to create gain parameters");

                DataContext = this;

                InitializeOpenGL();

                InitComponents();

                var converter = (PaletteNameToBrushConverter)this.Resources["PaletteNameToBrushConverter"];
                converter.BrushesProvider = SpectrumStyles;

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

        private void InitializeOpenGL()
        {
            try
            {
                _renderElement = openGLControl;
                if (_renderElement == null)
                {
                    throw new InvalidOperationException("OpenGL control not found in XAML");
                }

                _renderElement.Start(_glSettings);

                SmartLogger.Log(LogLevel.Information, LogPrefix, "OpenGL initialized successfully", forceLog: true);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Failed to initialize OpenGL: {ex}");
                throw;
            }
        }

        private void OnOpenGLControlReady()
        {
            SmartLogger.Log(LogLevel.Information, LogPrefix, "OpenGL context ready", forceLog: true);
        }

        private void OnRender(TimeSpan delta)
        {
            _renderer?.RenderFrame(delta);
        }

        private void InitComponents()
        {
            try
            {
                if (_renderElement == null)
                    throw new InvalidOperationException("OpenGL control not initialized");

                _spectrumStyles = new SpectrumBrushes();
                if (_spectrumStyles == null)
                    throw new InvalidOperationException("Failed to create SpectrumBrushes");

                _disposables = new CompositeDisposable();
                if (_disposables == null)
                    throw new InvalidOperationException("Failed to create CompositeDisposable");

                var syncContext = SynchronizationContext.Current ??
                    throw new InvalidOperationException("SynchronizationContext.Current is null");

                _analyzer = new SpectrumAnalyzer(
                    new FftProcessor { WindowType = WindowType },
                    new SpectrumConverter(_gainParameters),
                    syncContext
                );
                if (_analyzer == null)
                    throw new InvalidOperationException("Failed to create SpectrumAnalyzer");

                _disposables.Add(_analyzer);

                _captureManager = new AudioCaptureManager(this);
                if (_captureManager == null)
                    throw new InvalidOperationException("Failed to create AudioCaptureManager");

                _disposables.Add(_captureManager);

                _renderer = new Renderer(_spectrumStyles, this, _analyzer, _renderElement);
                if (_renderer == null)
                    throw new InvalidOperationException("Failed to create Renderer");

                _disposables.Add(_renderer);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Critical error initializing components: {ex}");
                throw new InvalidOperationException("Failed to initialize components", ex);
            }
        }

        private void InitEventHandlers()
        {
            SizeChanged += OnWindowSizeChanged;
            StateChanged += OnStateChanged;
            MouseDoubleClick += OnWindowMouseDoubleClick;
            Closed += OnWindowClosed;
            KeyDown += MainWindow_KeyDown;
            LocationChanged += OnWindowLocationChanged;

            PropertyChanged += (_, args) =>
            {
                if (args?.PropertyName == nameof(IsRecording))
                    StatusText = IsRecording ? "Recording..." : "Ready";
            };
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

                SmartLogger.Log(LogLevel.Information, LogPrefix, "Settings loaded and applied successfully");
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error loading and applying settings: {ex.Message}");
            }
        }

        private void ApplyWindowSettings()
        {
            try
            {
                Left = Settings.Instance.WindowLeft;
                Top = Settings.Instance.WindowTop;
                Width = Settings.Instance.WindowWidth;
                Height = Settings.Instance.WindowHeight;
                WindowState = Settings.Instance.WindowState;

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
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error applying window settings: {ex.Message}");
            }
        }

        private void EnsureWindowIsVisible()
        {
            try
            {
                bool isVisible = false;
                foreach (var screen in System.Windows.Forms.Screen.AllScreens)
                {
                    var screenBounds = new System.Drawing.Rectangle(
                        screen.WorkingArea.X,
                        screen.WorkingArea.Y,
                        screen.WorkingArea.Width,
                        screen.WorkingArea.Height);

                    var windowRect = new System.Drawing.Rectangle(
                        (int)Left,
                        (int)Top,
                        (int)Width,
                        (int)Height);

                    if (screenBounds.IntersectsWith(windowRect))
                    {
                        isVisible = true;
                        break;
                    }
                }

                if (!isVisible)
                {
                    WindowStartupLocation = WindowStartupLocation.CenterScreen;
                    SmartLogger.Log(LogLevel.Warning, LogPrefix, "Window position reset to center (was outside visible area)");
                }
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error ensuring window visibility: {ex.Message}");
            }
        }

        private void OnWindowLocationChanged(object? sender, EventArgs e)
        {
            try
            {
                if (WindowState == System.Windows.WindowState.Normal)
                {
                    Settings.Instance.WindowLeft = Left;
                    Settings.Instance.WindowTop = Top;
                }
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error updating window location: {ex.Message}");
            }
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

        private void OnOverlayButtonClick(object? sender, RoutedEventArgs e)
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

        #region Event Handlers

        private void OnRendering(object? sender, EventArgs? e)
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
            try
            {
                ThemeManager.Instance?.ToggleTheme();
            }
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
                if (_renderElement != null)
                {
                    // Уведомляем рендерер об изменении размеров
                    _renderer?.UpdateRenderDimensions((int)e.NewSize.Width, (int)e.NewSize.Height);
                }

                if (WindowState == System.Windows.WindowState.Normal)
                {
                    Settings.Instance.WindowWidth = Width;
                    Settings.Instance.WindowHeight = Height;
                }
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error updating dimensions: {ex}");
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
                            try
                            {
                                new SettingsWindow().ShowDialog();
                            }
                            finally
                            {
                                this.OpenSettingsButton.IsEnabled = true;
                            }
                        }
                    },
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

        private void MainWindow_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            try
            {
                switch (e.Key)
                {
                    case System.Windows.Input.Key.F10:
                        RenderQuality = RenderQuality.Low;
                        e.Handled = true;
                        SmartLogger.Log(LogLevel.Information, LogPrefix, "Quality set to Low via hotkey");
                        break;
                    case System.Windows.Input.Key.F11:
                        RenderQuality = RenderQuality.Medium;
                        e.Handled = true;
                        SmartLogger.Log(LogLevel.Information, LogPrefix, "Quality set to Medium via hotkey");
                        break;
                    case System.Windows.Input.Key.F12:
                        RenderQuality = RenderQuality.High;
                        e.Handled = true;
                        SmartLogger.Log(LogLevel.Information, LogPrefix, "Quality set to High via hotkey");
                        break;
                }
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error handling key down: {ex}");
            }
        }

        private void ToggleButtonContainer_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
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