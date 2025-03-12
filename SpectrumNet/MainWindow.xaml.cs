#nullable enable

namespace SpectrumNet
{
    public partial class MainWindow : Window, IAudioVisualizationController
    {
        private const string LogPrefix = "MainWindow";

        private readonly SemaphoreSlim _transitionSemaphore = new(1, 1);
        private readonly CancellationTokenSource _cleanupCts = new();

        private bool _isOverlayActive, _isPopupOpen, _isOverlayTopmost = true,
                     _isTransitioning, _isDisposed,
                     _showPerformanceInfo = true;

        private RenderStyle _selectedDrawingType = RenderStyle.Bars;
        private FftWindowType _selectedFftWindowType = FftWindowType.Hann;
        private SpectrumScale _selectedScaleType = SpectrumScale.Linear;

        private OverlayWindow? _overlayWindow;
        private ControlPanelWindow? _controlPanelWindow;
        private Renderer? _renderer;
        private SpectrumBrushes? _spectrumStyles;
        private SpectrumAnalyzer? _analyzer;
        private AudioCaptureManager? _captureManager;
        private CompositeDisposable? _disposables;
        private GainParameters? _gainParameters;
        private SKElement? _renderElement;
        private PropertyChangedEventHandler? _themePropertyChangedHandler;

        private record struct InitializationContext(
            SynchronizationContext SyncContext,
            GainParameters GainParams);

        #region IAudioVisualizationController Properties
        // Свойства интерфейса остались без изменений, так как они не содержат try-catch или проблем с SmartLogger
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
                UpdateVisualization(() =>
                {
                    SpectrumRendererFactory.ConfigureAllRenderers(value);
                    _renderElement?.InvalidateVisual();
                });
            }
        }

        public bool IsRecording
        {
            get => _captureManager?.IsRecording ?? false;
            set
            {
                if (_captureManager?.IsRecording == value) return;
                _ = value ? StartCaptureAsync() : StopCaptureAsync();
                OnPropertyChanged(nameof(IsRecording), nameof(CanStartCapture));
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
                _renderer?.RequestRender();
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

                UpdateVisualization(() =>
                {
                    _analyzer?.SetScaleType(value);
                    _renderer?.RequestRender();
                    _renderElement?.InvalidateVisual();
                    Settings.Instance.SelectedScaleType = value;
                });
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

                UpdateVisualization(() =>
                {
                    SpectrumRendererFactory.GlobalQuality = value;
                    _renderer?.RequestRender();
                    _renderElement?.InvalidateVisual();
                    SmartLogger.Log(LogLevel.Information, LogPrefix, $"Render quality set to {value}");
                });
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

                UpdateVisualization(() =>
                {
                    _analyzer?.SetWindowType(value);
                    _renderer?.RequestRender();
                    _renderElement?.InvalidateVisual();
                    Settings.Instance.SelectedFftWindowType = value;
                });
            }
        }
        #endregion

        #region Additional Public Properties
        // Дополнительные свойства также остались без изменений
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
                    Settings.Instance.IsOverlayTopmost = value;
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

        public bool IsControlPanelOpen
        {
            get => _controlPanelWindow != null && _controlPanelWindow.IsVisible;
        }

        public float MinDbLevel
        {
            get => _gainParameters!.MinDbValue;
            set => UpdateDbLevel(value,
                v => v < _gainParameters!.MaxDbValue,
                v => _gainParameters!.MinDbValue = v,
                _gainParameters!.MaxDbValue - 1,
                v => Settings.Instance.UIMinDbLevel = v,
                $"Min dB level ({value}) must be less than max ({_gainParameters!.MaxDbValue})",
                nameof(MinDbLevel));
        }

        public float MaxDbLevel
        {
            get => _gainParameters!.MaxDbValue;
            set => UpdateDbLevel(value,
                v => v > _gainParameters!.MinDbValue,
                v => _gainParameters!.MaxDbValue = v,
                _gainParameters!.MinDbValue + 1,
                v => Settings.Instance.UIMaxDbLevel = v,
                $"Max dB level ({value}) must be greater than min ({_gainParameters!.MinDbValue})",
                nameof(MaxDbLevel));
        }

        public float AmplificationFactor
        {
            get => _gainParameters!.AmplificationFactor;
            set => UpdateDbLevel(value,
                v => v >= 0,
                v => _gainParameters!.AmplificationFactor = v,
                0,
                v => Settings.Instance.UIAmplificationFactor = v,
                $"Amplification factor cannot be negative: {value}",
                nameof(AmplificationFactor));
        }
        #endregion

        public event PropertyChangedEventHandler? PropertyChanged;

        public MainWindow()
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

            _captureManager = new AudioCaptureManager(this) ??
                throw new InvalidOperationException("Failed to create AudioCaptureManager");

            _disposables.Add(_captureManager);

            _renderer = new Renderer(_spectrumStyles, this, _analyzer, _renderElement) ??
                throw new InvalidOperationException("Failed to create Renderer");

            _disposables.Add(_renderer);
        }

        private void SetupPaletteConverter() =>
            SmartLogger.Safe(() =>
            {
                if (Application.Current.Resources["PaletteNameToBrushConverter"] is PaletteNameToBrushConverter converter)
                    converter.BrushesProvider = SpectrumStyles;
                else
                    SmartLogger.Log(LogLevel.Error, LogPrefix, "PaletteNameToBrushConverter not found in application resources");
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error setting up palette converter"
            });

        private void InitEventHandlers()
        {
            if (_renderElement != null)
                _renderElement.PaintSurface += OnPaintSurface;

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
            }
        }

        private void OnThemePropertyChanged(object? sender, PropertyChangedEventArgs? e)
        {
            if (e?.PropertyName == nameof(ThemeManager.IsDarkTheme) && sender is ThemeManager tm)
            {
                OnPropertyChanged(nameof(IsDarkTheme));
                Settings.Instance.IsDarkTheme = tm.IsDarkTheme;
            }
        }
        #endregion

        #region Settings Management
        private void LoadAndApplySettings() =>
            SmartLogger.Safe(() =>
            {
                SettingsWindow.Instance.LoadSettings();
                ApplyWindowSettings();
                EnsureWindowIsVisible();
                SmartLogger.Log(LogLevel.Information, LogPrefix, "Settings loaded and applied successfully");
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error loading and applying settings"
            });

        private void ApplyWindowSettings() =>
            SmartLogger.Safe(() =>
            {
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
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error applying window settings"
            });

        private void EnsureWindowIsVisible() =>
            SmartLogger.Safe(() =>
            {
                bool isVisible = System.Windows.Forms.Screen.AllScreens.Any(screen =>
                {
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
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error ensuring window visibility"
            });

        private void OnWindowLocationChanged(object? sender, EventArgs e) =>
            SmartLogger.Safe(() =>
            {
                if (WindowState == WindowState.Normal)
                    SaveWindowPosition();
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error updating window location"
            });

        private void SaveWindowPosition() =>
            SmartLogger.Safe(() =>
            {
                Settings.Instance.WindowLeft = Left;
                Settings.Instance.WindowTop = Top;
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error saving window position"
            });
        #endregion

        #region Capture Management
        public async Task StartCaptureAsync()
        {
            if (_captureManager == null)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Attempt to start capture with no CaptureManager");
                return;
            }

            await SmartLogger.SafeAsync(async () =>
            {
                await _captureManager.StartCaptureAsync();
                UpdateProps();
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error starting audio capture"
            });
        }

        public async Task StopCaptureAsync()
        {
            if (_captureManager == null)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Attempt to stop capture with no CaptureManager");
                return;
            }

            await SmartLogger.SafeAsync(async () =>
            {
                await _captureManager.StopCaptureAsync();
                UpdateProps();
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error stopping audio capture"
            });
        }
        #endregion

        #region Overlay Management
        private void OpenOverlay() =>
            SmartLogger.Safe(() =>
            {
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
                SpectrumRendererFactory.ConfigureAllRenderers(true);
                _renderElement?.InvalidateVisual();
                UpdateRendererDimensions((int)SystemParameters.PrimaryScreenWidth,
                                         (int)SystemParameters.PrimaryScreenHeight);
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error opening overlay window"
            });

        private void CloseOverlay() =>
            SmartLogger.Safe(() => _overlayWindow?.Close(), new SmartLogger.ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error closing overlay"
            });

        private void HandleOverlayCloseFailed()
        {
            if (_overlayWindow != null)
            {
                SmartLogger.SafeDispose(_overlayWindow, "overlay window", new SmartLogger.ErrorHandlingOptions
                {
                    Source = LogPrefix,
                    ErrorMessage = "Error disposing overlay window"
                });
                _overlayWindow = null;
                IsOverlayActive = false;
            }
        }

        private void OnOverlayClosed() =>
            SmartLogger.Safe(() =>
            {
                if (_overlayWindow != null)
                {
                    SmartLogger.SafeDispose(_overlayWindow, "overlay window", new SmartLogger.ErrorHandlingOptions
                    {
                        Source = LogPrefix,
                        ErrorMessage = "Error disposing overlay window"
                    });
                    _overlayWindow = null;
                }

                IsOverlayActive = false;
                SpectrumRendererFactory.ConfigureAllRenderers(false);
                _renderElement?.InvalidateVisual();
                Activate();
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error handling overlay closed"
            });

        private void OnOverlayButtonClick(object sender, RoutedEventArgs e) =>
            SmartLogger.Safe(() =>
            {
                if (IsOverlayActive)
                {
                    CloseOverlay();
                    if (_overlayWindow != null && _overlayWindow.IsVisible)
                    {
                        HandleOverlayCloseFailed();
                    }
                }
                else
                    OpenOverlay();

                SpectrumRendererFactory.ConfigureAllRenderers(IsOverlayActive);
                _renderElement?.InvalidateVisual();
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error toggling overlay"
            });
        #endregion

        #region Control Panel Management
        private void OpenControlPanel() =>
            SmartLogger.Safe(() =>
            {
                if (_controlPanelWindow?.IsVisible == true)
                {
                    _controlPanelWindow.Activate();
                    if (_controlPanelWindow.WindowState == WindowState.Minimized)
                        _controlPanelWindow.WindowState = WindowState.Normal;

                    return;
                }

                _controlPanelWindow = new ControlPanelWindow(this);
                _controlPanelWindow.Owner = this;
                _controlPanelWindow.Show();
                _controlPanelWindow.Closed += (s, e) =>
                {
                    _controlPanelWindow = null;
                    OnPropertyChanged(nameof(IsControlPanelOpen));
                };

                OnPropertyChanged(nameof(IsControlPanelOpen));
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error opening control panel window"
            });

        public void CloseControlPanel() =>
            SmartLogger.Safe(() =>
            {
                if (_controlPanelWindow != null)
                {
                    _controlPanelWindow.Close();
                    _controlPanelWindow = null;
                    OnPropertyChanged(nameof(IsControlPanelOpen));
                }
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error closing control panel window"
            });

        public void MinimizeControlPanel() =>
            SmartLogger.Safe(() =>
            {
                if (_controlPanelWindow?.IsVisible == true)
                {
                    _controlPanelWindow.WindowState = WindowState.Minimized;
                }
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error minimizing control panel window"
            });

        public void ToggleControlPanel() =>
            SmartLogger.Safe(() =>
            {
                if (_controlPanelWindow?.IsVisible == true)
                {
                    CloseControlPanel();
                }
                else
                {
                    OpenControlPanel();
                }
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error toggling control panel window"
            });
        #endregion

        #region Event Handlers
        public void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs? e)
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

            var renderResult = SmartLogger.Safe<bool>(() =>
            {
                _renderer?.RenderFrame(sender, e);
                return true;
            }, false, new SmartLogger.ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error rendering frame"
            });

            bool renderSuccess = renderResult.Success && renderResult.Result == true;

            if (!renderSuccess)
            {
                SmartLogger.Safe(() => e.Surface.Canvas.Clear(SKColors.Transparent),
                    new SmartLogger.ErrorHandlingOptions
                    {
                        Source = LogPrefix,
                        ErrorMessage = "Error clearing canvas"
                    });
            }
        }

        private void OnRendering(object? sender, EventArgs? e) =>
            SmartLogger.Safe(() => _renderElement?.InvalidateVisual(), new SmartLogger.ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error updating visualization"
            });

        private void OnStateChanged(object? sender, EventArgs? e) =>
            SmartLogger.Safe(() =>
            {
                if (MaximizeButton != null && MaximizeIcon != null)
                {
                    MaximizeIcon.Data = Geometry.Parse(WindowState == WindowState.Maximized
                        ? "M0,0 L20,0 L20,20 L0,20 Z"
                        : "M2,2 H18 V18 H2 Z");

                    Settings.Instance.WindowState = WindowState;
                }
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error changing icon"
            });

        private void OnThemeToggleButtonChanged(object? sender, RoutedEventArgs? e) =>
            SmartLogger.Safe(() => ThemeManager.Instance?.ToggleTheme(), new SmartLogger.ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error toggling theme"
            });

        private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs? e) =>
            SmartLogger.Safe(() =>
            {
                if (e == null) return;

                _renderer?.UpdateRenderDimensions((int)e.NewSize.Width, (int)e.NewSize.Height);

                if (WindowState == WindowState.Normal)
                {
                    Settings.Instance.WindowWidth = Width;
                    Settings.Instance.WindowHeight = Height;
                }
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error updating dimensions"
            });

        public void OnComboBoxSelectionChanged(object? sender, SelectionChangedEventArgs? e) =>
            SmartLogger.Safe(() =>
            {
                if (sender is not ComboBox cb || e == null) return;

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
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error handling selection change"
            });

        private async void OnWindowClosed(object? sender, EventArgs? e)
        {
            SmartLogger.Safe(() => SettingsWindow.Instance.SaveSettings(), new SmartLogger.ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error saving settings"
            });
            _cleanupCts.Cancel();

            UnsubscribeFromEvents();
            await CleanupResourcesAsync();

            _isDisposed = true;
            Application.Current?.Shutdown();
        }

        private void UnsubscribeFromEvents()
        {
            CompositionTarget.Rendering -= OnRendering;

            if (_renderElement != null)
                _renderElement.PaintSurface -= OnPaintSurface;

            SizeChanged -= OnWindowSizeChanged;
            StateChanged -= OnStateChanged;
            MouseDoubleClick -= OnWindowMouseDoubleClick;
            Closed -= OnWindowClosed;
            LocationChanged -= OnWindowLocationChanged;

            if (_themePropertyChangedHandler != null && ThemeManager.Instance != null)
                ThemeManager.Instance.PropertyChanged -= _themePropertyChangedHandler;
        }

        private async Task CleanupResourcesAsync()
        {
            if (_captureManager != null)
            {
                await SmartLogger.SafeAsync(async () => await _captureManager.StopCaptureAsync(),
                    new SmartLogger.ErrorHandlingOptions
                    {
                        Source = LogPrefix,
                        ErrorMessage = "Error stopping capture"
                    });
                SmartLogger.SafeDispose(_captureManager, "capture manager", new SmartLogger.ErrorHandlingOptions
                {
                    Source = LogPrefix,
                    ErrorMessage = "Error disposing capture manager"
                });
                _captureManager = null;
            }

            SmartLogger.SafeDispose(_analyzer, "analyzer", new SmartLogger.ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error disposing analyzer"
            });
            SmartLogger.SafeDispose(_renderer, "renderer", new SmartLogger.ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error disposing renderer"
            });
            _analyzer = null;
            _renderer = null;
            _spectrumStyles = null;

            SmartLogger.SafeDispose(_disposables, "disposables", new SmartLogger.ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error disposing disposables"
            });
            _disposables = null;

            SmartLogger.SafeDispose(_transitionSemaphore, "transition semaphore", new SmartLogger.ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error disposing transition semaphore"
            });
            SmartLogger.SafeDispose(_cleanupCts, "cleanup token source", new SmartLogger.ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error disposing cleanup token source"
            });

            if (_overlayWindow != null)
            {
                _overlayWindow.Close();
                SmartLogger.SafeDispose(_overlayWindow, "overlay window", new SmartLogger.ErrorHandlingOptions
                {
                    Source = LogPrefix,
                    ErrorMessage = "Error disposing overlay window"
                });
                _overlayWindow = null;
            }

            if (_controlPanelWindow != null)
            {
                _controlPanelWindow.Close();
                _controlPanelWindow = null;
            }
        }

        private void OnWindowDrag(object? sender, System.Windows.Input.MouseButtonEventArgs? e) =>
            SmartLogger.Safe(() =>
            {
                if (e?.ChangedButton == System.Windows.Input.MouseButton.Left)
                {
                    DragMove();

                    if (WindowState == WindowState.Normal)
                        SaveWindowPosition();
                }
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error moving window"
            });

        private void OnWindowMouseDoubleClick(object? sender, System.Windows.Input.MouseButtonEventArgs? e) =>
            SmartLogger.Safe(() =>
            {
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
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error handling double click"
            });

        private bool IsCheckBoxOrChild(DependencyObject? element)
        {
            while (element != null)
            {
                if (element is CheckBox)
                    return true;
                element = VisualTreeHelper.GetParent(element);
            }
            return false;
        }

        public void OnSliderValueChanged(object? sender, RoutedPropertyChangedEventArgs<double>? e) =>
            SmartLogger.Safe(() =>
            {
                if (sender is not Slider slider || e == null) return;

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
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error handling slider change"
            });

        public void OnButtonClick(object sender, RoutedEventArgs e) =>
            SmartLogger.Safe(() =>
            {
                if (sender is not Button btn) return;

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
                        OpenSettings();
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
                    case "OpenControlPanelButton":
                        ToggleControlPanel();
                        break;
                }
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error handling button click"
            });

        private void OpenSettings() =>
            SmartLogger.Safe(() =>
            {
                new SettingsWindow().ShowDialog();
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error opening settings"
            });

        private void OnKeyDown(object sender, KeyEventArgs e) =>
            SmartLogger.Safe(() =>
            {
                switch (e.Key)
                {
                    case Key.F10:
                    case Key.F11:
                    case Key.F12:
                        HandleQualityHotkey(e.Key);
                        e.Handled = true;
                        break;
                }
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error handling key down"
            });

        private void HandleQualityHotkey(Key key)
        {
            var quality = key switch
            {
                Key.F10 => RenderQuality.Low,
                Key.F11 => RenderQuality.Medium,
                Key.F12 => RenderQuality.High,
                _ => RenderQuality.Medium
            };

            RenderQuality = quality;
            SmartLogger.Log(LogLevel.Information, LogPrefix, $"Quality set to {quality} via hotkey");
        }
        #endregion

        #region Helper Methods
        private void UpdateVisualization(Action action) =>
            SmartLogger.Safe(action, new SmartLogger.ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error updating visualization"
            });

        private void UpdateOverlayTopmostState() =>
            SmartLogger.Safe(() =>
            {
                if (_overlayWindow?.IsInitialized == true)
                    _overlayWindow.Topmost = IsOverlayTopmost;
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error updating topmost state"
            });

        private void UpdateRendererDimensions(int width, int height) =>
            SmartLogger.Safe(() =>
            {
                if (width <= 0 || height <= 0)
                {
                    SmartLogger.Log(LogLevel.Warning, LogPrefix, $"Invalid dimensions for renderer: {width}x{height}");
                    return;
                }

                _renderer?.UpdateRenderDimensions(width, height);
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error updating renderer dimensions"
            });

        private void CloseWindow() => Close();
        private void MinimizeWindow() => WindowState = WindowState.Minimized;
        private void MaximizeWindow() =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void UpdateProps() =>
            OnPropertyChanged(nameof(IsRecording), nameof(CanStartCapture));

        private void UpdateGainParameter(float newValue, Action<float> setter, string propertyName) =>
            SmartLogger.Safe(() =>
            {
                if (setter == null)
                {
                    SmartLogger.Log(LogLevel.Error, LogPrefix, "Null delegate passed for parameter update");
                    return;
                }

                setter(newValue);
                OnPropertyChanged(propertyName);
                _renderElement?.InvalidateVisual();
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error updating gain parameter"
            });

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
        }

        private void UpdateEnumProperty<T>(ref T field, T value, Action<T> settingUpdater, [CallerMemberName] string propertyName = "") where T : struct, Enum
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            OnPropertyChanged(propertyName);
            settingUpdater(value);
        }

        private void UpdateDbLevel(float value, Func<float, bool> validator, Action<float> setter,
                                 float fallbackValue, Action<float> settingUpdater, string errorMessage,
                                 string propertyName)
        {
            if (_gainParameters == null) return;

            if (!validator(value))
            {
                SmartLogger.Log(LogLevel.Warning, LogPrefix, errorMessage);
                value = fallbackValue;
            }

            UpdateGainParameter(value, setter, propertyName);
            settingUpdater(value);
            SmartLogger.Safe(() => SettingsWindow.Instance.SaveSettings(), new SmartLogger.ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error saving settings after updating gain parameter"
            });
        }

        protected bool SetField<T>(ref T field, T value, Action? callback = null, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName ?? string.Empty);

            SmartLogger.Safe(() => callback?.Invoke(), new SmartLogger.ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error executing callback in SetField"
            });

            return true;
        }

        public void OnPropertyChanged(params string[] propertyNames) =>
            SmartLogger.Safe(() =>
            {
                foreach (var name in propertyNames)
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }, new SmartLogger.ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error notifying property change"
            });
        #endregion
    }
}