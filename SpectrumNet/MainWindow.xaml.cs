#nullable enable

namespace SpectrumNet;

public partial class MainWindow : System.Windows.Window, IAudioVisualizationController
{
    private const string LogPrefix = "MainWindow";

    // Core components
    private GLWpfControl? _renderElement;
    private SpectrumAnalyzer? _analyzer;
    private SpectrumBrushes? _spectrumStyles;
    private AudioCaptureManager? _captureManager;
    private GainParameters? _gainParameters;
    private VisualizationManager? _visualizationManager;
    private Renderer? _renderer;
    private OverlayWindow? _overlayWindow;
    private ControlPanelWindow? _controlPanelWindow;
    private CompositeDisposable? _disposables;
    private PropertyChangedEventHandler? _themePropertyChangedHandler;

    // State flags
    private bool _isOverlayActive, _isPopupOpen, _isOverlayTopmost = true,
                _isControlPanelVisible = true, _isTransitioning, _isDisposed,
                _showPerformanceInfo = true, _isOpenGLInitialized, _isControlPanelOpen;

    // Configuration state
    private RenderStyle _selectedDrawingType = RenderStyle.Bars;
    private FftWindowType _selectedFftWindowType = FftWindowType.Hann;
    private SpectrumScale _selectedScaleType = SpectrumScale.Linear;
    private string _statusText = "Ready";
    private GLWpfControlSettings _glSettings = new();

    // Synchronization
    private readonly SemaphoreSlim _transitionSemaphore = new(1, 1);
    private readonly CancellationTokenSource _cleanupCts = new();
    private readonly object _openGLLock = new();

    // Core properties
    public GLWpfControlSettings GlSettings => _glSettings;
    public GLWpfControl SpectrumCanvas => _renderElement ??
        throw new InvalidOperationException("Render element not initialized");

    public Renderer? Renderer
    {
        get => _renderer;
        set
        {
            if (_renderer == value) return;
            var oldRenderer = _renderer;
            _renderer = value;
            SmartLogger.SafeDispose(oldRenderer as IDisposable, "old renderer");
        }
    }

    public bool IsControlPanelOpen
    {
        get => _isControlPanelOpen;
        set
        {
            _isControlPanelOpen = value;
            OnPropertyChanged(nameof(IsControlPanelOpen));
        }
    }

    public bool IsTransitioning => _isTransitioning;
    public new Dispatcher Dispatcher => System.Windows.Application.Current?.Dispatcher ??
        throw new InvalidOperationException("Application.Current is null");

    #region IAudioVisualizationController Properties
    public SpectrumAnalyzer Analyzer
    {
        get => _analyzer ?? throw new InvalidOperationException("Analyzer not initialized");
        set => _analyzer = value ?? throw new ArgumentNullException(nameof(Analyzer));
    }

    public SpectrumBrushes SpectrumStyles => _spectrumStyles ??
        throw new InvalidOperationException("Spectrum styles not initialized");

    public GainParameters GainParameters => _gainParameters ??
        throw new InvalidOperationException("Gain parameters not initialized");

    public bool CanStartCapture => _captureManager != null && !IsRecording;

    public int BarCount
    {
        get => Settings.Instance?.UIBarCount ?? DefaultSettings.UIBarCount;
        set
        {
            if (Settings.Instance == null)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Settings.Instance is null, cannot set BarCount");
                return;
            }

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
        get => Settings.Instance?.UIBarSpacing ?? DefaultSettings.UIBarSpacing;
        set
        {
            if (Settings.Instance == null)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Settings.Instance is null, cannot set BarSpacing");
                return;
            }

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

            SmartLogger.Safe(() =>
            {
                SpectrumRendererFactory.ConfigureAllRenderers(value);
                InvalidateRenderElement();
            }, source: LogPrefix, errorMessage: "Error updating visualization");
        }
    }

    public bool IsRecording
    {
        get => _captureManager?.IsRecording ?? false;
        set
        {
            if (_captureManager?.IsRecording == value) return;
            Dispatcher.InvokeAsync(() =>
            {
                if (value) _ = StartCaptureAsync();
                else _ = StopCaptureAsync();
                OnPropertyChanged(nameof(IsRecording), nameof(CanStartCapture));
            });
        }
    }

    public bool ShowPerformanceInfo
    {
        get => _showPerformanceInfo;
        set
        {
            if (_showPerformanceInfo == value) return;
            _showPerformanceInfo = value;
            OnPropertyChanged(nameof(ShowPerformanceInfo));
            _visualizationManager?.RequestRender();
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

            SmartLogger.Safe(() =>
            {
                _analyzer?.SetScaleType(value);
                _visualizationManager?.RequestRender();
                InvalidateRenderElement();
                Settings.Instance.SelectedScaleType = value;
            }, source: LogPrefix, errorMessage: "Error changing scale type");
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

            SmartLogger.Safe(() =>
            {
                _visualizationManager?.HandleRenderStyleChanged(value);
                InvalidateRenderElement();
            }, source: LogPrefix, errorMessage: "Error changing render style");
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

            SmartLogger.Safe(() =>
            {
                SpectrumRendererFactory.GlobalQuality = value;
                _visualizationManager?.RequestRender();
                InvalidateRenderElement();
            }, source: LogPrefix, errorMessage: "Error updating render quality");
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

            SmartLogger.Safe(() =>
            {
                _analyzer?.SetWindowType(value);
                _visualizationManager?.RequestRender();
                InvalidateRenderElement();
                Settings.Instance.SelectedFftWindowType = value;
            }, source: LogPrefix, errorMessage: "Error changing FFT window type");
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
        get => _gainParameters?.MinDbValue ?? DefaultSettings.UIMinDbLevel;
        set
        {
            if (Settings.Instance == null)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Settings.Instance is null, cannot set MinDbLevel");
                return;
            }

            ArgumentNullException.ThrowIfNull(_gainParameters, nameof(_gainParameters));

            if (value >= _gainParameters.MaxDbValue)
            {
                SmartLogger.Log(LogLevel.Warning, LogPrefix, $"Min dB level ({value}) must be less than max ({_gainParameters.MaxDbValue})");
                value = _gainParameters.MaxDbValue - 1;
            }

            UpdateGainParameter(value, v => _gainParameters.MinDbValue = v, nameof(MinDbLevel));
            Settings.Instance.UIMinDbLevel = value;
        }
    }

    public float MaxDbLevel
    {
        get => _gainParameters?.MaxDbValue ?? DefaultSettings.UIMaxDbLevel;
        set
        {
            if (Settings.Instance == null)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Settings.Instance is null, cannot set MaxDbLevel");
                return;
            }

            ArgumentNullException.ThrowIfNull(_gainParameters, nameof(_gainParameters));

            if (value <= _gainParameters.MinDbValue)
            {
                SmartLogger.Log(LogLevel.Warning, LogPrefix, $"Max dB level ({value}) must be greater than min ({_gainParameters.MinDbValue})");
                value = _gainParameters.MinDbValue + 1;
            }

            UpdateGainParameter(value, v => _gainParameters.MaxDbValue = v, nameof(MaxDbLevel));
            Settings.Instance.UIMaxDbLevel = value;
        }
    }

    public float AmplificationFactor
    {
        get => _gainParameters?.AmplificationFactor ?? DefaultSettings.UIAmplificationFactor;
        set
        {
            if (Settings.Instance == null)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Settings.Instance is null, cannot set AmplificationFactor");
                return;
            }

            ArgumentNullException.ThrowIfNull(_gainParameters, nameof(_gainParameters));

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
        InitializeComponent();

        _renderElement = SmartLogger.Safe(() => FindName("OpenTkControl") as GLWpfControl,
            defaultValue: null,
            source: LogPrefix,
            errorMessage: "Failed to find GLWpfControl in XAML")
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

    #region Initialization
    private void OnWindowLoaded(object sender, RoutedEventArgs e) =>
        SmartLogger.Safe(() =>
        {
        InitializeOpenGL();
        InitComponents();
        InitializeRenderers();
        OnPropertyChanged(nameof(AvailablePalettes));
            if (Resources["PaletteNameToBrushConverter"] is PaletteNameToBrushConverter converter)
            {
                converter.BrushesProvider = SpectrumStyles;
            }
        }, source: LogPrefix, errorMessage: "Failed to initialize window");

    private void InitializeOpenGL()
    {
        lock (_openGLLock)
        {
            if (_isOpenGLInitialized) return;

            ArgumentNullException.ThrowIfNull(_renderElement, nameof(_renderElement));

            SmartLogger.Log(LogLevel.Information, LogPrefix, "Initializing OpenGL context");

            _glSettings = new GLWpfControlSettings
            {
                MajorVersion = 4,
                MinorVersion = 0,
                RenderContinuously = false
            };

            _renderElement.Start(_glSettings);

            if (!_renderElement.IsInitialized)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Failed to initialize OpenGL context");
                return;
            }

            SmartLogger.Log(LogLevel.Information, LogPrefix, "OpenGL context initialized successfully");
            _renderElement.Render += OpenTkControl_OnRender;
            _isOpenGLInitialized = true;
        }
    }

    private void OpenTkControl_OnRender(TimeSpan delta)
    {
        if (_isDisposed) return;

        if (!_isOpenGLInitialized || _renderElement?.IsInitialized != true || _renderer == null) return;

        SmartLogger.Safe(() => _renderer.OnGlControlRender(delta),
            source: LogPrefix,
            errorMessage: "Error during render");
    }

    private void InitComponents()
    {
        ArgumentNullException.ThrowIfNull(_renderElement, nameof(_renderElement));

        var syncContext = SynchronizationContext.Current ??
            throw new InvalidOperationException("SynchronizationContext.Current is null");

        _spectrumStyles = new SpectrumBrushes();
        _disposables = new CompositeDisposable();
        _analyzer = new SpectrumAnalyzer(
            new FftProcessor { WindowType = _selectedFftWindowType },
            new SpectrumConverter(_gainParameters ?? throw new InvalidOperationException("Gain parameters not initialized")),
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

        Dispatcher.InvokeAsync(() =>
        {
            if (!_isDisposed && _visualizationManager?.CameraController != null)
            {
                _visualizationManager.CameraController.ActivateCamera();
            }
        }, DispatcherPriority.Background);

        OnPropertyChanged(nameof(CanStartCapture), nameof(IsRecording));
    }

    private void InitializeRenderers()
    {
        if (SpectrumRendererFactory.IsInitialized) return;

        SmartLogger.Log(LogLevel.Information, LogPrefix, "Initializing renderers");

        foreach (RenderStyle style in Enum.GetValues<RenderStyle>())
        {
            SpectrumRendererFactory.CreateRenderer(style, false);
        }

        SpectrumRendererFactory.IsInitialized = true;
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

    private void OnRenderElementSizeChanged(object sender, SizeChangedEventArgs e) =>
        SmartLogger.Safe(() => _visualizationManager?.UpdateRenderDimensions((int)e.NewSize.Width, (int)e.NewSize.Height),
            source: LogPrefix, errorMessage: "Error updating render dimensions");

    private void ConfigureTheme()
    {
        var tm = ThemeManager.Instance;
        if (tm == null) return;

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
    #endregion

    #region Settings Management
    private void LoadAndApplySettings() =>
        SmartLogger.Safe(() =>
        {
            SettingsWindow.Instance.LoadSettings();
            ApplyWindowSettings();
            EnsureWindowIsVisible();
        }, source: LogPrefix, errorMessage: "Error loading settings");

    private void ApplyWindowSettings()
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

    private void EnsureWindowIsVisible()
    {
        var screenRect = new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
        var windowRect = new Rect(Left, Top, Width, Height);

        if (!screenRect.IntersectsWith(windowRect))
        {
            Left = (screenRect.Width - Width) / 2;
            Top = (screenRect.Height - Height) / 2;
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

        if (_captureManager.IsRecording)
        {
            SmartLogger.Log(LogLevel.Warning, LogPrefix, "Capture already in progress");
            return;
        }

        await SmartLogger.SafeAsync(async () =>
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                await _captureManager.StartCaptureAsync();
                UpdateProps();
                InvalidateRenderElement();
            }).Task;
        }, source: LogPrefix, errorMessage: "Error starting capture");
    }

    public async Task StopCaptureAsync()
    {
        if (_captureManager == null)
        {
            SmartLogger.Log(LogLevel.Error, LogPrefix, "Capture manager not initialized");
            return;
        }

        if (!_captureManager.IsRecording)
        {
            SmartLogger.Log(LogLevel.Warning, LogPrefix, "No capture in progress");
            return;
        }

        await SmartLogger.SafeAsync(async () =>
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                await _captureManager.StopCaptureAsync();
                UpdateProps();
                InvalidateRenderElement();
            }).Task;
        }, source: LogPrefix, errorMessage: "Error stopping capture");
    }
    #endregion

    #region Overlay Management
    private void OpenOverlay()
    {
        if (_overlayWindow?.IsInitialized == true)
        {
            _overlayWindow.Show();
            _overlayWindow.Topmost = IsOverlayTopmost;
            return;
        }

        CloseOverlayInternal();

        _overlayWindow = SmartLogger.Safe(() => new OverlayWindow(
            this,
            new OverlayConfiguration(
                RenderInterval: 16,
                IsTopmost: IsOverlayTopmost,
                ShowInTaskbar: false,
                Style: System.Windows.WindowStyle.None,
                State: System.Windows.WindowState.Normal,
                EnableEscapeToClose: true,
                EnableHardwareAcceleration: true
            )
        ), defaultValue: null, source: LogPrefix, errorMessage: "Failed to create overlay window");

        if (_overlayWindow == null) return;

        _overlayWindow.Closed += (_, _) => OnOverlayClosed();
        _overlayWindow.Show();
        IsOverlayActive = true;
    }

    private void CloseOverlay() =>
        SmartLogger.Safe(() =>
        {
            CloseOverlayInternal();
            IsOverlayActive = false;
        }, source: LogPrefix, errorMessage: "Error closing overlay");

    private void CloseOverlayInternal()
    {
        if (_overlayWindow == null) return;

        _overlayWindow.Closed -= (_, _) => OnOverlayClosed();
        _overlayWindow.Close();
        SmartLogger.SafeDispose(_overlayWindow, "overlay window");
        _overlayWindow = null;
    }

    private void OnOverlayClosed() =>
        SmartLogger.Safe(() =>
        {
            if (_overlayWindow != null)
            {
                SmartLogger.SafeDispose(_overlayWindow, "overlay window");
                _overlayWindow = null;
            }

            Dispatcher.InvokeAsync(() =>
            {
                IsOverlayActive = false;
                SpectrumRendererFactory.ConfigureAllRenderers(false);
                InvalidateRenderElement();
                Activate();
            });
        }, source: LogPrefix, errorMessage: "Error handling overlay closed");

    private void OnOverlayButtonClick(object? sender, RoutedEventArgs e) =>
        SmartLogger.Safe(() =>
        {
            if (IsOverlayActive)
                CloseOverlay();
            else
                OpenOverlay();

            SpectrumRendererFactory.ConfigureAllRenderers(IsOverlayActive);
            InvalidateRenderElement();
        }, source: LogPrefix, errorMessage: "Error toggling overlay");
    #endregion

    #region Event Handlers
    private void ToggleButtonContainer_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;

        SmartLogger.Safe(() =>
        {
            if (_controlPanelWindow == null || !_controlPanelWindow.IsVisible)
            {
                _controlPanelWindow = new ControlPanelWindow(this);
                _controlPanelWindow.Closed += (s, args) =>
                {
                    _controlPanelWindow = null;
                    IsControlPanelOpen = false;
                };
                _controlPanelWindow.Show();
                IsControlPanelOpen = true;
            }
            else
            {
                _controlPanelWindow.Close();
                _controlPanelWindow = null;
                IsControlPanelOpen = false;
            }
        }, source: LogPrefix, errorMessage: "Error toggling control panel window");
    }

    private void OnRendering(object? sender, EventArgs? e)
    {
        if (_isDisposed) return;

        SmartLogger.Safe(() =>
        {
            lock (_openGLLock)
            {
                if (!_isOpenGLInitialized) return;

                _visualizationManager?.CameraController?.Update();

                if (_visualizationManager?.NeedsRender == true)
                {
                    InvalidateRenderElement();
                    _visualizationManager.NeedsRender = false;
                }
            }
        }, source: LogPrefix, errorMessage: "Error updating visualization");
    }

    private void OnStateChanged(object? sender, EventArgs? e)
    {
        if (MaximizeButton == null || MaximizeIcon == null) return;

        SmartLogger.Safe(() =>
        {
            MaximizeIcon.Data = Geometry.Parse(WindowState == System.Windows.WindowState.Maximized
                ? "M0,0 L20,0 L20,20 L0,20 Z"
                : "M2,2 H18 V18 H2 Z");
            Settings.Instance.WindowState = WindowState;
        }, source: LogPrefix, errorMessage: "Error changing icon");
    }

    private void OnThemeToggleButtonChanged(object? sender, RoutedEventArgs? e) =>
        SmartLogger.Safe(() => ThemeManager.Instance?.ToggleTheme(),
            source: LogPrefix, errorMessage: "Error toggling theme");

    private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs? e)
    {
        if (e == null) return;

        SmartLogger.Safe(() =>
        {
            if (WindowState == System.Windows.WindowState.Normal)
            {
                Settings.Instance.WindowWidth = Width;
                Settings.Instance.WindowHeight = Height;
            }

            if (_renderElement != null)
            {
                UpdateRendererDimensions((int)_renderElement.ActualWidth, (int)_renderElement.ActualHeight);
            }
        }, source: LogPrefix, errorMessage: "Error updating window size settings");
    }

    internal void OnComboBoxSelectionChanged(object? sender, SelectionChangedEventArgs? e)
    {
        if (sender is not System.Windows.Controls.ComboBox cb || e == null) return;

        SmartLogger.Safe(() =>
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
        }, source: LogPrefix, errorMessage: "Error handling selection change");
    }

    private async void OnWindowClosed(object? sender, EventArgs? e)
    {
        _isDisposed = true;
        await SmartLogger.SafeAsync(async () =>
        {
            SettingsWindow.Instance.SaveSettings();

            if (_controlPanelWindow != null)
            {
                _controlPanelWindow.Close();
                _controlPanelWindow = null;
            }

            _cleanupCts.Cancel();

            CompositionTarget.Rendering -= OnRendering;
            SizeChanged -= OnWindowSizeChanged;
            StateChanged -= OnStateChanged;
            MouseDoubleClick -= OnWindowMouseDoubleClick;
            Closed -= OnWindowClosed;
            LocationChanged -= OnWindowLocationChanged;
            MouseWheel -= Window_MouseWheel;

            if (_renderElement != null)
            {
                _renderElement.SizeChanged -= OnRenderElementSizeChanged;
                _renderElement.Render -= OpenTkControl_OnRender;
            }

            if (_captureManager != null)
            {
                await Dispatcher.InvokeAsync(async () =>
                    await _captureManager.StopCaptureAsync()).Task;
                SmartLogger.SafeDispose(_captureManager, "capture manager");
                _captureManager = null;
            }

            SmartLogger.SafeDispose(_analyzer, "analyzer");
            _analyzer = null;

            SmartLogger.SafeDispose(_visualizationManager, "visualization manager");
            _visualizationManager = null;

            SmartLogger.SafeDispose(_disposables, "disposables");
            _disposables = null;

            SmartLogger.SafeDispose(_transitionSemaphore, "transition semaphore");
            SmartLogger.SafeDispose(_cleanupCts, "cleanup token source");

            if (_themePropertyChangedHandler != null && ThemeManager.Instance != null)
                ThemeManager.Instance.PropertyChanged -= _themePropertyChangedHandler;

            if (_overlayWindow != null)
            {
                _overlayWindow.Close();
                SmartLogger.SafeDispose(_overlayWindow, "overlay window");
                _overlayWindow = null;
            }

            System.Windows.Application.Current?.Shutdown();
        }, source: LogPrefix, errorMessage: "Error releasing resources");
    }

    private void OnWindowDrag(object? sender, System.Windows.Input.MouseButtonEventArgs? e)
    {
        if (e?.ChangedButton != System.Windows.Input.MouseButton.Left) return;

        SmartLogger.Safe(() =>
        {
            DragMove();
            if (WindowState == System.Windows.WindowState.Normal)
            {
                Settings.Instance.WindowLeft = Left;
                Settings.Instance.WindowTop = Top;
            }
        }, source: LogPrefix, errorMessage: "Error moving window");
    }

    private void OnWindowMouseDoubleClick(object? sender, System.Windows.Input.MouseButtonEventArgs? e)
    {
        if (e == null) return;

        SmartLogger.Safe(() =>
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
        }, source: LogPrefix, errorMessage: "Error handling double click");
    }

    internal void OnSliderValueChanged(object? sender, RoutedPropertyChangedEventArgs<double>? e)
    {
        if (sender is not Slider slider || e == null) return;

        SmartLogger.Safe(() =>
        {
            switch (slider.Name)
            {
                case "barSpacingSlider":
                    BarSpacing = slider.Value;
                    break;
                case "barCountSlider":
                    BarCount = (int)slider.Value;
                    break;
                case "minDbLevelSlider":
                    MinDbLevel = (float)slider.Value;
                    break;
                case "maxDbLevelSlider":
                    MaxDbLevel = (float)slider.Value;
                    break;
                case "amplificationFactorSlider":
                    AmplificationFactor = (float)slider.Value;
                    break;
            }
        }, source: LogPrefix, errorMessage: "Error handling slider change");
    }

    internal void OnButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn) return;

        SmartLogger.Safe(() =>
        {
            switch (btn.Name)
            {
                case "StartCaptureButton":
                    _ = StartCaptureAsync();
                    break;
                case "StopCaptureButton":
                    _ = StopCaptureAsync();
                    break;
                case "OverlayButton":
                    OnOverlayButtonClick(sender, e);
                    break;
                case "OpenSettingsButton":
                    OpenSettingsDialog(btn);
                    break;
                case "OpenPopupButton":
                    IsPopupOpen = !IsPopupOpen;
                    break;
                case "MinimizeButton":
                    MinimizeWindow();
                    break;
                case "MaximizeButton":
                    MaximizeWindow();
                    break;
                case "CloseButton":
                    CloseWindow();
                    break;
                case "ResetCameraButton":
                    _visualizationManager?.CameraController?.ResetCamera();
                    break;
            }
        }, source: LogPrefix, errorMessage: $"Error handling button click: {btn.Name}");
    }

    private void OpenSettingsDialog(System.Windows.Controls.Button button)
    {
        button.IsEnabled = false;
        try
        {
            new SettingsWindow().ShowDialog();
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private void Window_MouseMove(object sender, MouseEventArgs e) =>
        SmartLogger.Safe(() => _visualizationManager?.CameraController?.HandleMouseMove(e),
            source: LogPrefix, errorMessage: "Error handling mouse move");

    private void Window_MouseWheel(object sender, MouseWheelEventArgs e) =>
        SmartLogger.Safe(() => _visualizationManager?.CameraController?.HandleMouseWheel(e),
            source: LogPrefix, errorMessage: "Error handling mouse wheel");

    private void MainWindow_KeyDown(object sender, KeyEventArgs e) =>
        SmartLogger.Safe(() =>
        {
            _visualizationManager?.CameraController?.HandleKeyDown(e);
            if (e.Handled) return;

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
        }, source: LogPrefix, errorMessage: "Error handling key down");

    private void MainWindow_KeyUp(object sender, KeyEventArgs e) =>
        SmartLogger.Safe(() => _visualizationManager?.CameraController?.HandleKeyUp(e),
            source: LogPrefix, errorMessage: "Error handling key up");
    #endregion

    #region Helper Methods
    private void UpdateOverlayTopmostState() =>
        SmartLogger.Safe(() =>
        {
            if (_overlayWindow?.IsInitialized == true)
                _overlayWindow.Topmost = IsOverlayTopmost;
        }, source: LogPrefix, errorMessage: "Error updating topmost state");

    private void UpdateRendererDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            SmartLogger.Log(LogLevel.Warning, LogPrefix, $"Invalid dimensions: {width}x{height}");
            return;
        }

        SmartLogger.Safe(() =>
        {
            lock (_openGLLock)
            {
                if (!_isOpenGLInitialized) return;
                _visualizationManager?.UpdateRenderDimensions(width, height);
            }
        }, source: LogPrefix, errorMessage: "Error updating renderer dimensions");
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
        ArgumentNullException.ThrowIfNull(setter, nameof(setter));

        SmartLogger.Safe(() =>
        {
            setter(newValue);
            OnPropertyChanged(propertyName);
            Dispatcher.InvokeAsync(() => InvalidateRenderElement(), DispatcherPriority.Render);
        }, source: LogPrefix, errorMessage: "Error updating gain parameter");
    }

    protected bool SetField<T>(ref T field, T value, Action? callback = null, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;

        field = value;
        OnPropertyChanged(propertyName ?? string.Empty);

        SmartLogger.Safe(() => callback?.Invoke(),
            source: LogPrefix,
            errorMessage: "Error executing callback in SetField");

        return true;
    }

    public void OnPropertyChanged(params string[] propertyNames)
    {
        if (_isDisposed) return;

        foreach (var name in propertyNames)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    private void InvalidateRenderElement()
    {
        if (_renderElement == null || !_isOpenGLInitialized) return;
        _renderElement.InvalidateVisual();
    }
    #endregion
}