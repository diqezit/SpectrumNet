#nullable enable

namespace SpectrumNet
{
    public partial class MainWindow : Window, IAudioVisualizationController
    {
        private const string LogPrefix = "MainWindow";

        private SemaphoreSlim _transitionSemaphore = new(1, 1);
        private CancellationTokenSource _cleanupCts = new();
        private DispatcherTimer _saveSettingsTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
        private Dictionary<string, Action> _buttonActions;

        private bool _isOverlayActive, _isPopupOpen, _isOverlayTopmost = true,
                     _isTransitioning, _isDisposed, _showPerformanceInfo = true;
        private RenderStyle _selectedDrawingType = RenderStyle.Bars;
        private FftWindowType _selectedFftWindowType = FftWindowType.Hann;
        private SpectrumScale _selectedScaleType = SpectrumScale.Linear;

        private OverlayWindow? _overlayWindow;
        private ControlPanelWindow? _controlPanelWindow;
        private Renderer? _renderer;
        private SpectrumBrushes? _spectrumStyles;
        private SpectrumAnalyzer? _analyzer;
        private AudioCapture? _captureManager;
        private CompositeDisposable? _disposables;
        private GainParameters? _gainParameters;
        private SKElement? _renderElement;
        private PropertyChangedEventHandler? _themePropertyChangedHandler;

        private record struct InitializationContext(SynchronizationContext SyncContext, GainParameters GainParams);

        #region IAudioVisualizationController Properties
        public SpectrumAnalyzer Analyzer
        {
            get => _analyzer ?? throw new InvalidOperationException("Analyzer not initialized");
            set => _analyzer = value ?? throw new ArgumentNullException(nameof(Analyzer));
        }

        public int BarCount
        {
            get => Settings.Instance.UIBarCount;
            set => UpdateSetting(value, v => Settings.Instance.UIBarCount = v, nameof(BarCount),
                v => v > 0, DefaultSettings.UIBarCount, "invalid bar count");
        }

        public double BarSpacing
        {
            get => Settings.Instance.UIBarSpacing;
            set => UpdateSetting(value, v => Settings.Instance.UIBarSpacing = v, nameof(BarSpacing),
                v => v >= 0, DefaultSettings.UIBarSpacing, "negative spacing");
        }

        public bool CanStartCapture => _captureManager != null && !IsRecording;

        public new Dispatcher Dispatcher =>
            Application.Current?.Dispatcher ?? throw new InvalidOperationException("Application.Current is null");

        public GainParameters GainParameters =>
            _gainParameters ?? throw new InvalidOperationException("Gain parameters not initialized");

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

        public bool IsRecording
        {
            get => _captureManager?.IsRecording ?? false;
            set
            {
                if (_captureManager?.IsRecording == value) return;

                if (_captureManager != null)
                    _ = _captureManager.ToggleCaptureAsync();
                else
                    SmartLogger.Log(LogLevel.Error, LogPrefix, "Cannot toggle recording: CaptureManager is null");
            }
        }

        public bool ShowPerformanceInfo
        {
            get => _showPerformanceInfo;
            set => SetField(ref _showPerformanceInfo, value);
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

                Settings.Instance.SelectedScaleType = value;
                _saveSettingsTimer.Stop();
                _saveSettingsTimer.Start();
            }
        }

        public RenderStyle SelectedDrawingType
        {
            get => _selectedDrawingType;
            set => UpdateEnumProperty(ref _selectedDrawingType, value,
                v => Settings.Instance.SelectedRenderStyle = v, nameof(SelectedDrawingType));
        }

        public RenderQuality RenderQuality
        {
            get => Settings.Instance.SelectedRenderQuality;
            set
            {
                if (Settings.Instance.SelectedRenderQuality == value) return;
                Settings.Instance.SelectedRenderQuality = value;
                OnPropertyChanged(nameof(RenderQuality));

                SmartLogger.Log(LogLevel.Information, LogPrefix, $"Render quality set to {value}");
                _saveSettingsTimer.Stop();
                _saveSettingsTimer.Start();
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

                SmartLogger.Log(LogLevel.Information, LogPrefix, $"Selected style changed to {value}");
                _saveSettingsTimer.Stop();
                _saveSettingsTimer.Start();
            }
        }

        public SKElement SpectrumCanvas =>
            _renderElement ?? throw new InvalidOperationException("Render element not initialized");

        public SpectrumBrushes SpectrumStyles =>
            _spectrumStyles ?? throw new InvalidOperationException("Spectrum styles not initialized");

        public FftWindowType WindowType
        {
            get => _selectedFftWindowType;
            set
            {
                if (_selectedFftWindowType == value) return;
                _selectedFftWindowType = value;
                OnPropertyChanged(nameof(WindowType));

                Settings.Instance.SelectedFftWindowType = value;
                _saveSettingsTimer.Stop();
                _saveSettingsTimer.Start();
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
            _spectrumStyles?.RegisteredPalettes.OrderBy(kvp => kvp.Key).ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
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
                    _saveSettingsTimer.Stop();
                    _saveSettingsTimer.Start();
                }
            }
        }

        public Palette? SelectedPalette
        {
            get => AvailablePalettes.TryGetValue(SelectedStyle, out var palette) ? palette : null;
            set
            {
                if (value == null) return;
                SelectedStyle = value.Name;
                OnPropertyChanged(nameof(SelectedPalette));
            }
        }

        public bool IsControlPanelOpen => _controlPanelWindow != null && _controlPanelWindow.IsVisible;

        public float MinDbLevel
        {
            get => _gainParameters!.MinDbValue;
            set => UpdateDbLevel(value, v => v < _gainParameters!.MaxDbValue, v => _gainParameters!.MinDbValue = v,
                _gainParameters!.MaxDbValue - 1, v => Settings.Instance.UIMinDbLevel = v,
                $"Min dB level ({value}) must be less than max ({_gainParameters!.MaxDbValue})", nameof(MinDbLevel));
        }

        public float MaxDbLevel
        {
            get => _gainParameters!.MaxDbValue;
            set => UpdateDbLevel(value, v => v > _gainParameters!.MinDbValue, v => _gainParameters!.MaxDbValue = v,
                _gainParameters!.MinDbValue + 1, v => Settings.Instance.UIMaxDbLevel = v,
                $"Max dB level ({value}) must be greater than min ({_gainParameters!.MinDbValue})", nameof(MaxDbLevel));
        }

        public float AmplificationFactor
        {
            get => _gainParameters!.AmplificationFactor;
            set => UpdateDbLevel(value, v => v >= 0, v => _gainParameters!.AmplificationFactor = v, 0,
                v => Settings.Instance.UIAmplificationFactor = v, $"Amplification factor cannot be negative: {value}", nameof(AmplificationFactor));
        }
        #endregion

        public event PropertyChangedEventHandler? PropertyChanged;

        public MainWindow()
        {
            InitializeComponent();
            var syncContext = SynchronizationContext.Current ??
                throw new InvalidOperationException("No synchronization context. Window must be created in UI thread.");

            _buttonActions = new Dictionary<string, Action>
            {
                ["MinimizeButton"] = MinimizeWindow,
                ["MaximizeButton"] = MaximizeWindow,
                ["CloseButton"] = CloseWindow,
                ["OpenControlPanelButton"] = ToggleControlPanel
            };

            LoadAndApplySettings();

            _gainParameters = new GainParameters(
                syncContext,
                Settings.Instance.UIMinDbLevel,
                Settings.Instance.UIMaxDbLevel,
                Settings.Instance.UIAmplificationFactor
            ) ?? throw new InvalidOperationException("Failed to create gain parameters");

            _saveSettingsTimer.Tick += (s, e) => {
                _saveSettingsTimer.Stop();
                SettingsWindow.Instance.SaveSettings();
            };

            DataContext = this;
            InitComponents(new InitializationContext(syncContext, _gainParameters));
            SetupPaletteConverter();
            InitEventHandlers();
            ConfigureTheme();
            UpdateProps();
            CompositionTarget.Rendering += OnRendering;
        }

        #region Initialization
        private void InitComponents(InitializationContext context)
        {
            _renderElement = spectrumCanvas ??
                throw new InvalidOperationException("Canvas not found in window template");

            _spectrumStyles = new SpectrumBrushes() ??
                throw new InvalidOperationException("Failed to create SpectrumBrushes");

            _disposables = new CompositeDisposable() ??
                throw new InvalidOperationException("Failed to create CompositeDisposable");

            _analyzer = new SpectrumAnalyzer(
                new FftProcessor { WindowType = WindowType },
                new SpectrumConverter(_gainParameters),
                context.SyncContext
            ) ?? throw new InvalidOperationException("Failed to create SpectrumAnalyzer");

            _disposables.Add(_analyzer);

            _captureManager = new AudioCapture(this) ??
                throw new InvalidOperationException("Failed to create AudioCaptureManager");

            _disposables.Add(_captureManager);

            _renderer = new Renderer(_spectrumStyles, this, _analyzer, _renderElement) ??
                throw new InvalidOperationException("Failed to create Renderer");

            _disposables.Add(_renderer);
        }

        private void SetupPaletteConverter() =>
            SafeExecute(() => {
                if (Application.Current.Resources["PaletteNameToBrushConverter"] is PaletteNameToBrushConverter converter)
                    converter.BrushesProvider = SpectrumStyles;
                else
                    SmartLogger.Log(LogLevel.Error, LogPrefix, "PaletteNameToBrushConverter not found in application resources");
            }, "Error setting up palette converter");

        private void InitEventHandlers()
        {
            SizeChanged += OnWindowSizeChanged;
            StateChanged += OnStateChanged;
            MouseDoubleClick += OnWindowMouseDoubleClick;
            Closed += OnWindowClosed;
            KeyDown += OnKeyDown;
            LocationChanged += OnWindowLocationChanged;
            PropertyChanged += OnPropertyChangedInternal;
        }

        private void OnPropertyChangedInternal(object? sender, PropertyChangedEventArgs args) { }

        private void ConfigureTheme()
        {
            var tm = ThemeManager.Instance;
            if (tm != null)
            {
                tm.RegisterWindow(this);
                _themePropertyChangedHandler = OnThemePropertyChanged;
                tm.PropertyChanged += _themePropertyChangedHandler;
                UpdateThemeToggleButtonState();
            }
        }

        private void UpdateThemeToggleButtonState()
        {
            if (ThemeToggleButton != null)
            {
                ThemeToggleButton.Checked -= OnThemeToggleButtonChanged;
                ThemeToggleButton.Unchecked -= OnThemeToggleButtonChanged;
                ThemeToggleButton.IsChecked = IsDarkTheme;
                ThemeToggleButton.Checked += OnThemeToggleButtonChanged;
                ThemeToggleButton.Unchecked += OnThemeToggleButtonChanged;
            }
        }

        private void OnThemePropertyChanged(object? sender, PropertyChangedEventArgs? e)
        {
            if (e?.PropertyName == nameof(ThemeManager.IsDarkTheme) && sender is ThemeManager tm)
            {
                OnPropertyChanged(nameof(IsDarkTheme));
                Settings.Instance.IsDarkTheme = tm.IsDarkTheme;
                UpdateThemeToggleButtonState();
            }
        }
        #endregion

        #region Settings Management
        private void LoadAndApplySettings() =>
            SafeExecute(() => {
                SettingsWindow.Instance.LoadSettings();
                ApplyWindowSettings();
                EnsureWindowIsVisible();
                SmartLogger.Log(LogLevel.Information, LogPrefix, "Settings loaded and applied successfully");
            }, "Error loading and applying settings");

        private void ApplyWindowSettings() =>
            SafeExecute(() => {
                Left = Settings.Instance.WindowLeft;
                Top = Settings.Instance.WindowTop;
                Width = Settings.Instance.WindowWidth;
                Height = Settings.Instance.WindowHeight;
                WindowState = Settings.Instance.WindowState;

                IsOverlayTopmost = Settings.Instance.IsOverlayTopmost;
                SelectedDrawingType = Settings.Instance.SelectedRenderStyle;
                WindowType = Settings.Instance.SelectedFftWindowType;
                ScaleType = Settings.Instance.SelectedScaleType;
                RenderQuality = Settings.Instance.SelectedRenderQuality;
                SelectedStyle = Settings.Instance.SelectedPalette;
                ThemeManager.Instance?.SetTheme(Settings.Instance.IsDarkTheme);
            }, "Error applying window settings");

        private void EnsureWindowIsVisible() =>
            SafeExecute(() => {
                bool isVisible = System.Windows.Forms.Screen.AllScreens.Any(screen => {
                    var screenBounds = new System.Drawing.Rectangle(
                        screen.WorkingArea.X, screen.WorkingArea.Y,
                        screen.WorkingArea.Width, screen.WorkingArea.Height);

                    var windowRect = new System.Drawing.Rectangle(
                                    (int)Left, (int)Top, (int)Width, (int)Height);

                    return screenBounds.IntersectsWith(windowRect);
                });

                if (!isVisible)
                {
                    WindowStartupLocation = WindowStartupLocation.CenterScreen;
                    SmartLogger.Log(LogLevel.Warning, LogPrefix, "Window position reset to center (was outside visible area)");
                }
            }, "Error ensuring window visibility");

        private void OnWindowLocationChanged(object? sender, EventArgs e) =>
            SafeExecute(() => {
                if (WindowState == WindowState.Normal)
                    SaveWindowPosition();
            }, "Error updating window location");

        private void SaveWindowPosition() =>
            SafeExecute(() => {
                Settings.Instance.WindowLeft = Left;
                Settings.Instance.WindowTop = Top;
            }, "Error saving window position");
        #endregion

        #region Capture Management
        public async Task StartCaptureAsync()
        {
            if (_captureManager == null)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Attempt to start capture with no CaptureManager");
                return;
            }

            await _captureManager.StartCaptureAsync();
        }

        public async Task StopCaptureAsync()
        {
            if (_captureManager == null)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Attempt to stop capture with no CaptureManager");
                return;
            }

            await _captureManager.StopCaptureAsync();
        }

        public async Task ToggleCaptureAsync()
        {
            if (_captureManager == null) return;

            if (IsRecording)
                await StopCaptureAsync();
            else
                await StartCaptureAsync();
        }
        #endregion

        #region Overlay Management
        public void OpenOverlay() =>
            SafeExecute(() => {
                if (_overlayWindow?.IsInitialized == true)
                {
                    _overlayWindow.Show();
                    _overlayWindow.Topmost = IsOverlayTopmost;
                }
                else
                {
                    var config = new OverlayConfiguration(
                        RenderInterval: 16,
                        IsTopmost: IsOverlayTopmost,
                        ShowInTaskbar: false,
                        EnableHardwareAcceleration: true
                    );

                    _overlayWindow = new OverlayWindow(this, config);
                    if (_overlayWindow == null)
                    {
                        SmartLogger.Log(LogLevel.Error, LogPrefix, "Failed to create overlay window");
                        return;
                    }
                    _overlayWindow.Closed += (_, _) => OnOverlayClosed();
                    _overlayWindow.Show();
                }

                IsOverlayActive = true;

                if (_renderer != null)
                    _renderer.UpdateRenderDimensions(
                        (int)SystemParameters.PrimaryScreenWidth,
                        (int)SystemParameters.PrimaryScreenHeight);
            }, "Error opening overlay window");

        public void CloseOverlay() =>
            SafeExecute(() => _overlayWindow?.Close(), "Error closing overlay");

        private void OnOverlayClosed() =>
            SafeExecute(() => {
                if (_overlayWindow != null)
                {
                    SmartLogger.SafeDispose(_overlayWindow, "overlay window", GetLoggerOptions("Error disposing overlay window"));
                    _overlayWindow = null;
                }

                IsOverlayActive = false;
                Activate();
                Focus();
            }, "Error handling overlay closed");
        #endregion

        #region Control Panel Management
        public void OpenControlPanel() =>
            SafeExecute(() => {
                if (_controlPanelWindow?.IsVisible == true)
                {
                    _controlPanelWindow.Activate();
                    if (_controlPanelWindow.WindowState == WindowState.Minimized)
                        _controlPanelWindow.WindowState = WindowState.Normal;
                    return;
                }

                _controlPanelWindow = new ControlPanelWindow(this);
                _controlPanelWindow.Owner = this;
                _controlPanelWindow.WindowStartupLocation = WindowStartupLocation.Manual;
                double left = this.Left + (this.ActualWidth - _controlPanelWindow.Width) / 2;
                double top = this.Top + this.ActualHeight - 250; // Появление границ окна конфига
                _controlPanelWindow.Left = left;
                _controlPanelWindow.Top = top;
                _controlPanelWindow.Show();
                _controlPanelWindow.Closed += (s, e) => {
                    _controlPanelWindow = null;
                    OnPropertyChanged(nameof(IsControlPanelOpen));
                    Activate();
                    Focus();
                };

                OnPropertyChanged(nameof(IsControlPanelOpen));
            }, "Error opening control panel window");

        public void CloseControlPanel() =>
            SafeExecute(() => {
                if (_controlPanelWindow != null)
                {
                    _controlPanelWindow.Close();
                    _controlPanelWindow = null;
                    OnPropertyChanged(nameof(IsControlPanelOpen));
                    Activate();
                    Focus();
                }
            }, "Error closing control panel window");

        public void MinimizeControlPanel() =>
            SafeExecute(() => {
                if (_controlPanelWindow?.IsVisible == true)
                {
                    _controlPanelWindow.WindowState = WindowState.Minimized;
                }
            }, "Error minimizing control panel window");

        public void ToggleControlPanel() =>
            ToggleWindow(OpenControlPanel, CloseControlPanel, () => _controlPanelWindow?.IsVisible == true, "Error toggling control panel window");

        private void ToggleWindow(Action openAction, Action closeAction, Func<bool> isOpenChecker, string errorMessage) =>
            SafeExecute(() => {
                if (isOpenChecker())
                    closeAction();
                else
                    openAction();
            }, errorMessage);
        #endregion

        #region Event Handlers
        public void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs? e)
        {
            if (e == null || _renderer == null)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "PaintSurface called with null arguments");
                return;
            }

            _renderer.RenderFrame(sender, e);
        }

        private void OnRendering(object? sender, EventArgs? e) =>
            SafeExecute(() => _renderer?.RequestRender(), "Error requesting render");

        private void OnStateChanged(object? sender, EventArgs? e) =>
            SafeExecute(() => {
                if (MaximizeButton != null && MaximizeIcon != null)
                {
                    MaximizeIcon.Data = Geometry.Parse(WindowState == WindowState.Maximized
                        ? "M0,0 L20,0 L20,20 L0,20 Z"
                        : "M2,2 H18 V18 H2 Z");

                    Settings.Instance.WindowState = WindowState;
                }
            }, "Error changing icon");

        private void OnThemeToggleButtonChanged(object? sender, RoutedEventArgs? e) =>
            SafeExecute(() => ThemeManager.Instance?.ToggleTheme(), "Error toggling theme");

        private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs? e) =>
            SafeExecute(() => {
                if (e == null) return;

                if (_renderer != null)
                    _renderer.UpdateRenderDimensions((int)e.NewSize.Width, (int)e.NewSize.Height);

                if (WindowState == WindowState.Normal)
                {
                    Settings.Instance.WindowWidth = Width;
                    Settings.Instance.WindowHeight = Height;
                }
            }, "Error updating dimensions");

        public void OnButtonClick(object sender, RoutedEventArgs e) =>
            SafeExecute(() => {
                if (sender is Button btn && _buttonActions.TryGetValue(btn.Name, out var action))
                    action();
            }, "Error handling button click");

        private void OnWindowMouseDoubleClick(object? sender, System.Windows.Input.MouseButtonEventArgs? e) =>
            SafeExecute(() => {
                if (e == null) return;

                if (IsCheckBoxOrChild(e.OriginalSource as DependencyObject))
                {
                    e.Handled = true;
                    return;
                }

                if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
                {
                    e.Handled = true;
                    MaximizeWindow();
                }
            }, "Error handling double click");

        private bool IsCheckBoxOrChild(DependencyObject? element)
        {
            while (element != null)
            {
                if (element is CheckBox) return true;
                element = VisualTreeHelper.GetParent(element);
            }
            return false;
        }

        public void OnSliderValueChanged(object? sender, RoutedPropertyChangedEventArgs<double>? e) { }
        public void OnComboBoxSelectionChanged(object? sender, SelectionChangedEventArgs? e) { }

        public void OnKeyDown(object sender, KeyEventArgs e)
        {
            SafeExecute(() => {
                if (!IsActive) return;

                switch (e.Key)
                {
                    case Key.Space:
                        if (!(Keyboard.FocusedElement is TextBox ||
                              Keyboard.FocusedElement is PasswordBox ||
                              Keyboard.FocusedElement is ComboBox))
                        {
                            _ = ToggleCaptureAsync();
                            e.Handled = true;
                        }
                        break;
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
                    case Key.Escape:
                        if (WindowState == WindowState.Maximized)
                        {
                            WindowState = WindowState.Normal;
                            e.Handled = true;
                        }
                        break;
                }
            }, "Error handling key down");
        }

        private void OnWindowDrag(object? sender, System.Windows.Input.MouseButtonEventArgs? e) =>
            SafeExecute(() => {
                if (e?.ChangedButton == System.Windows.Input.MouseButton.Left)
                {
                    DragMove();

                    if (WindowState == WindowState.Normal)
                        SaveWindowPosition();
                }
            }, "Error moving window");

        private async void OnWindowClosed(object? sender, EventArgs? e)
        {
            _saveSettingsTimer.Stop();
            SafeExecute(() => SettingsWindow.Instance.SaveSettings(), "Error saving settings on window close");
            _cleanupCts.Cancel();

            UnsubscribeFromEvents();
            await CleanupResourcesAsync();

            _isDisposed = true;
            Application.Current?.Shutdown();
        }

        private void UnsubscribeFromEvents()
        {
            CompositionTarget.Rendering -= OnRendering;
            SizeChanged -= OnWindowSizeChanged;
            StateChanged -= OnStateChanged;
            MouseDoubleClick -= OnWindowMouseDoubleClick;
            Closed -= OnWindowClosed;
            LocationChanged -= OnWindowLocationChanged;
            KeyDown -= OnKeyDown;

            if (_themePropertyChangedHandler != null && ThemeManager.Instance != null)
                ThemeManager.Instance.PropertyChanged -= _themePropertyChangedHandler;
        }

        private async Task CleanupResourcesAsync()
        {
            _saveSettingsTimer.Stop();

            if (_captureManager != null)
            {
                await DisposeResourceAsync(_captureManager,
                    async cm => await cm.StopCaptureAsync(),
                    "capture manager",
                    () => _captureManager = null);
            }

            if (_analyzer != null)
            {
                await DisposeResourceAsync(_analyzer, null, "analyzer",
                    () => _analyzer = null);
            }

            if (_renderer != null)
            {
                await DisposeResourceAsync(_renderer, null, "renderer",
                    () => _renderer = null);
            }

            _spectrumStyles = null;

            if (_disposables != null)
            {
                await DisposeResourceAsync(_disposables, null, "disposables",
                    () => _disposables = null);
            }

            if (_transitionSemaphore != null)
            {
                await DisposeResourceAsync(_transitionSemaphore, null, "transition semaphore",
                    () => _transitionSemaphore = null);
            }

            if (_cleanupCts != null)
            {
                await DisposeResourceAsync(_cleanupCts, null, "cleanup token source",
                    () => _cleanupCts = null);
            }

            if (_overlayWindow != null)
            {
                _overlayWindow.Close();
                await DisposeResourceAsync(_overlayWindow, null, "overlay window",
                    () => _overlayWindow = null);
            }

            if (_controlPanelWindow != null)
            {
                _controlPanelWindow.Close();
                _controlPanelWindow = null;
            }
        }
        #endregion

        #region Helper Methods
        private SmartLogger.ErrorHandlingOptions GetLoggerOptions(string errorMessage) =>
            new SmartLogger.ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = errorMessage
            };

        private void SafeExecute(Action action, string errorMessage) =>
            SmartLogger.Safe(action, GetLoggerOptions(errorMessage));

        private async Task DisposeResourceAsync<T>(
            T resource,
            Func<T, Task>? asyncCleanup = null,
            string resourceName = "resource",
            Action? afterDispose = null)
            where T : IDisposable
        {
            if (resource == null) return;

            if (asyncCleanup != null)
                await SmartLogger.SafeAsync(async () => await asyncCleanup(resource),
                    GetLoggerOptions($"Error stopping {resourceName}"));

            SmartLogger.SafeDispose(resource, resourceName, GetLoggerOptions($"Error disposing {resourceName}"));

            afterDispose?.Invoke();
        }

        private void UpdateOverlayTopmostState() =>
            SafeExecute(() => {
                if (_overlayWindow?.IsInitialized == true)
                    _overlayWindow.Topmost = IsOverlayTopmost;
            }, "Error updating topmost state");

        private void CloseWindow() => Close();

        private void MinimizeWindow() => WindowState = WindowState.Minimized;

        private void MaximizeWindow() =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void UpdateProps() =>
            OnPropertyChanged(nameof(IsRecording), nameof(CanStartCapture));

        private void UpdateGainParameter(float newValue, Action<float> setter, string propertyName) =>
            SafeExecute(() => {
                if (setter == null)
                {
                    SmartLogger.Log(LogLevel.Error, LogPrefix, "Null delegate passed for parameter update");
                    return;
                }

                setter(newValue);
                OnPropertyChanged(propertyName);
            }, "Error updating gain parameter");

        private void UpdateSetting<T>(T value, Action<T> setter, string propertyName,
                                    Func<T, bool> validator, T defaultValue, string errorMessage)
        {
            if (!validator(value))
            {
                SmartLogger.Log(LogLevel.Warning, LogPrefix, $"Attempt to set {errorMessage}: {value}");
                value = defaultValue;
            }

            setter(value);
            OnPropertyChanged(propertyName);
            _saveSettingsTimer.Stop();
            _saveSettingsTimer.Start();
        }

        private void UpdateEnumProperty<T>(ref T field, T value, Action<T> settingUpdater,
                                                 [CallerMemberName] string propertyName = "")
                                                 where T : struct, Enum
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;

            field = value;
            OnPropertyChanged(propertyName);
            settingUpdater(value);
            _saveSettingsTimer.Stop();
            _saveSettingsTimer.Start();
        }

        private void UpdateDbLevel(float value, Func<float, bool> validator, Action<float> setter,
                                                 float fallbackValue, Action<float> settingUpdater, string errorMessage,
                                                 string propertyName)
        {
            if (_gainParameters == null)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Gain parameters not initialized in UpdateDbLevel");
                return;
            }

            if (!validator(value))
            {
                SmartLogger.Log(LogLevel.Warning, LogPrefix, errorMessage);
                value = fallbackValue;
            }

            UpdateGainParameter(value, setter, propertyName);
            settingUpdater(value);
            _saveSettingsTimer.Stop();
            _saveSettingsTimer.Start();
        }

        protected bool SetField<T>(ref T field, T value, Action? callback = null, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName ?? string.Empty);

            if (callback != null)
                SafeExecute(() => callback(), $"Error executing callback for {propertyName}");

            return true;
        }

        public void OnPropertyChanged(params string[] propertyNames) =>
            SafeExecute(() => {
                foreach (var name in propertyNames)
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }, "Error notifying property change");
        #endregion
    }
}