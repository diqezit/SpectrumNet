#nullable enable

using System.Collections.Specialized;
using static System.Windows.Input.Key;
using static SpectrumNet.SmartLogger;

namespace SpectrumNet
{
    /// <summary>
    /// Centralized keyboard event handler
    /// </summary>
    public class KeyboardManager
    {
        private const string LogPrefix = "KeyboardManager";
        private readonly IAudioVisualizationController _controller;
        private readonly Dictionary<Key, Action> _globalKeyActions;
        private readonly Dictionary<(Key Key, ModifierKeys Modifiers), Action> _modifiedKeyActions;

        public KeyboardManager(IAudioVisualizationController controller)
        {
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));

            _globalKeyActions = new Dictionary<Key, Action>
            {
                { Space, () => _ = _controller.ToggleCaptureAsync() },
                { F10, () => _controller.RenderQuality = RenderQuality.Low },
                { F11, () => _controller.RenderQuality = RenderQuality.Medium },
                { F12, () => _controller.RenderQuality = RenderQuality.High },
                { Escape, () =>
                    {
                        if (_controller.IsPopupOpen) _controller.IsPopupOpen = false;
                        else if (_controller.IsOverlayActive) _controller.CloseOverlay();
                    }
                }
            };

            _modifiedKeyActions = new Dictionary<(Key, ModifierKeys), Action>
            {
                { (O, ModifierKeys.Control), () =>
                    {
                        if (_controller.IsOverlayActive) _controller.CloseOverlay();
                        else _controller.OpenOverlay();
                    }
                },
                { (P, ModifierKeys.Control), () => _controller.ToggleControlPanel() }
            };
        }

        public bool HandleKeyDown(KeyEventArgs e, IInputElement? focusedElement)
        {
            if (focusedElement is TextBox or PasswordBox or ComboBox)
                return false;

            if (Keyboard.Modifiers != ModifierKeys.None &&
                _modifiedKeyActions.TryGetValue((e.Key, Keyboard.Modifiers), out var modAction))
            {
                modAction();
                return true;
            }

            if (_globalKeyActions.TryGetValue(e.Key, out var action))
            {
                action();
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Audio visualization controller that separates logic from views
    /// </summary>
    public class AudioVisualizationController : INotifyPropertyChanged, IDisposable, IAudioVisualizationController
    {
        private const string LogPrefix = "AudioVisualizationController";

        private SemaphoreSlim _transitionSemaphore = new(1, 1);
        private CancellationTokenSource _cleanupCts = new();
        private DispatcherTimer _saveSettingsTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };

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
        private SKGLElement? _renderElement;
        private Window? _ownerWindow;
        private KeyboardManager? _keyboardManager;

        private readonly IEnumerable<RenderStyle> _availableDrawingTypes;
        private readonly IEnumerable<FftWindowType> _availableFftWindowTypes;
        private readonly IEnumerable<SpectrumScale> _availableScaleTypes;
        private readonly IEnumerable<RenderQuality> _availableRenderQualities;

        public event PropertyChangedEventHandler? PropertyChanged;

        private static SpectrumNet.Settings AppSettings => SpectrumNet.Settings.Instance;

        public AudioVisualizationController(Window ownerWindow, SKGLElement renderElement)
        {
            ArgumentNullException.ThrowIfNull(ownerWindow);
            ArgumentNullException.ThrowIfNull(renderElement);

            _ownerWindow = ownerWindow;
            _renderElement = renderElement;

            _availableDrawingTypes = Enum.GetValues<RenderStyle>().OrderBy(s => s.ToString()).ToList();
            _availableFftWindowTypes = Enum.GetValues<FftWindowType>().OrderBy(wt => wt.ToString()).ToList();
            _availableScaleTypes = Enum.GetValues<SpectrumScale>().OrderBy(s => s.ToString()).ToList();
            _availableRenderQualities = Enum.GetValues<RenderQuality>().OrderBy(q => (int)q).ToList();

            var syncContext = SynchronizationContext.Current ??
                throw new InvalidOperationException("No synchronization context. Controller must be created in UI thread.");

            LoadAndApplySettings();

            _gainParameters = new GainParameters(
                syncContext,
                AppSettings.UIMinDbLevel,
                AppSettings.UIMaxDbLevel,
                AppSettings.UIAmplificationFactor
            ) ?? throw new InvalidOperationException("Failed to create gain parameters");

            _saveSettingsTimer.Tick += OnSaveSettingsTimerTick;

            InitComponents(syncContext);
            SetupPaletteConverter();

            _keyboardManager = new KeyboardManager(this);

            AppSettings.FavoriteRenderers.CollectionChanged += OnFavoriteRenderersChanged;
        }

        #region Initialization
        private void InitComponents(SynchronizationContext syncContext)
        {
            _spectrumStyles = new SpectrumBrushes() ??
                throw new InvalidOperationException("Failed to create SpectrumBrushes");

            _disposables = new CompositeDisposable() ??
                throw new InvalidOperationException("Failed to create CompositeDisposable");

            _analyzer = new SpectrumAnalyzer(
                new FftProcessor { WindowType = WindowType },
                new SpectrumConverter(_gainParameters),
                syncContext
            ) ?? throw new InvalidOperationException("Failed to create SpectrumAnalyzer");

            _disposables.Add(_analyzer);

            _captureManager = new AudioCapture(this) ??
                throw new InvalidOperationException("Failed to create AudioCaptureManager");

            _disposables.Add(_captureManager);

            _renderer = new Renderer(_spectrumStyles, this, _analyzer,
                _renderElement ?? throw new InvalidOperationException("Render element is null")
            ) ?? throw new InvalidOperationException("Failed to create Renderer");

            _disposables.Add(_renderer);
        }

        private void SetupPaletteConverter() =>
            Safe(() =>
            {
                if (Application.Current.Resources["PaletteNameToBrushConverter"] is PaletteNameToBrushConverter converter)
                    converter.BrushesProvider = SpectrumStyles;
                else
                    Log(LogLevel.Error, LogPrefix, "PaletteNameToBrushConverter not found in application resources");
            }, new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error setting up palette converter" });

        private void LoadAndApplySettings() =>
            Safe(() =>
            {
                SettingsWindow.Instance.LoadSettings();

                ShowPerformanceInfo = AppSettings.ShowPerformanceInfo;
                IsOverlayTopmost = AppSettings.IsOverlayTopmost;
                SelectedDrawingType = AppSettings.SelectedRenderStyle;
                WindowType = AppSettings.SelectedFftWindowType;
                ScaleType = AppSettings.SelectedScaleType;
                RenderQuality = AppSettings.SelectedRenderQuality;
                SelectedStyle = AppSettings.SelectedPalette;

                ThemeManager.Instance.SetTheme(AppSettings.IsDarkTheme);

                Log(LogLevel.Information, LogPrefix, "Settings loaded and applied successfully");
            }, new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error loading and applying settings" });

        private void OnSaveSettingsTimerTick(object? sender, EventArgs e)
        {
            _saveSettingsTimer.Stop();
            SettingsWindow.Instance.SaveSettings();
        }
        #endregion

        #region IAudioVisualizationController Properties
        public IEnumerable<RenderStyle> AvailableDrawingTypes => _availableDrawingTypes;
        public IEnumerable<FftWindowType> AvailableFftWindowTypes => _availableFftWindowTypes;
        public IEnumerable<SpectrumScale> AvailableScaleTypes => _availableScaleTypes;
        public IEnumerable<RenderQuality> AvailableRenderQualities => _availableRenderQualities;

        public IEnumerable<RenderStyle> OrderedDrawingTypes =>
            _availableDrawingTypes.OrderBy(x =>
                AppSettings.FavoriteRenderers.Contains(x) ? 0 : 1).ThenBy(x => x.ToString());

        public SpectrumAnalyzer Analyzer
        {
            get => _analyzer ?? throw new InvalidOperationException("Analyzer not initialized");
            set => _analyzer = value ?? throw new ArgumentNullException(nameof(value));
        }

        public int BarCount
        {
            get => AppSettings.UIBarCount;
            set => UpdateSetting(value, v => AppSettings.UIBarCount = v, nameof(BarCount),
                v => v > 0, DefaultSettings.UIBarCount, "invalid bar count");
        }

        public double BarSpacing
        {
            get => AppSettings.UIBarSpacing;
            set => UpdateSetting(value, v => AppSettings.UIBarSpacing = v, nameof(BarSpacing),
                v => v >= 0, DefaultSettings.UIBarSpacing, "negative spacing");
        }

        public bool CanStartCapture => _captureManager is not null && !IsRecording;

        public Dispatcher Dispatcher =>
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

                if (_captureManager is not null)
                    _ = _captureManager.ToggleCaptureAsync();
                else
                    Log(LogLevel.Error, LogPrefix, "Cannot toggle recording: CaptureManager is null");
            }
        }

        public bool ShowPerformanceInfo
        {
            get => _showPerformanceInfo;
            set
            {
                if (_showPerformanceInfo == value) return;

                _showPerformanceInfo = value;
                AppSettings.ShowPerformanceInfo = value;
                RestartSettingsTimer();
                OnPropertyChanged(nameof(ShowPerformanceInfo));
            }
        }

        public bool IsTransitioning
        {
            get => _isTransitioning;
            set => SetField(ref _isTransitioning, value);
        }

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
                AppSettings.SelectedScaleType = value;
                RestartSettingsTimer();
                OnPropertyChanged(nameof(ScaleType));
            }
        }

        public RenderStyle SelectedDrawingType
        {
            get => _selectedDrawingType;
            set => UpdateEnumProperty(ref _selectedDrawingType, value,
                v => AppSettings.SelectedRenderStyle = v, nameof(SelectedDrawingType));
        }

        public RenderQuality RenderQuality
        {
            get => AppSettings.SelectedRenderQuality;
            set
            {
                if (AppSettings.SelectedRenderQuality == value) return;

                AppSettings.SelectedRenderQuality = value;
                RestartSettingsTimer();
                OnPropertyChanged(nameof(RenderQuality));
                Log(LogLevel.Information, LogPrefix, $"Render quality set to {value}");
            }
        }

        public string SelectedStyle
        {
            get => AppSettings.SelectedPalette;
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    Log(LogLevel.Warning, LogPrefix, "Attempt to set empty style name");
                    value = DefaultSettings.SelectedPalette;
                }

                if (AppSettings.SelectedPalette == value) return;

                AppSettings.SelectedPalette = value;
                RestartSettingsTimer();
                OnPropertyChanged(nameof(SelectedStyle));
                Log(LogLevel.Information, LogPrefix, $"Selected style changed to {value}");
            }
        }

        public SKGLElement SpectrumCanvas => _renderElement ?? throw new InvalidOperationException("Render element not initialized");

        public SpectrumBrushes SpectrumStyles =>
            _spectrumStyles ?? throw new InvalidOperationException("Spectrum styles not initialized");

        public FftWindowType WindowType
        {
            get => _selectedFftWindowType;
            set
            {
                if (_selectedFftWindowType == value) return;

                _selectedFftWindowType = value;
                AppSettings.SelectedFftWindowType = value;
                RestartSettingsTimer();
                OnPropertyChanged(nameof(WindowType));
            }
        }

        public bool IsOverlayTopmost
        {
            get => _isOverlayTopmost;
            set
            {
                if (_isOverlayTopmost == value) return;

                _isOverlayTopmost = value;
                AppSettings.IsOverlayTopmost = value;
                RestartSettingsTimer();
                OnPropertyChanged(nameof(IsOverlayTopmost));
                UpdateOverlayTopmostState();
            }
        }

        public Palette? SelectedPalette
        {
            get => AvailablePalettes.TryGetValue(SelectedStyle, out var palette) ? palette : null;
            set
            {
                if (value is null) return;
                SelectedStyle = value.Name;
                OnPropertyChanged(nameof(SelectedPalette));
            }
        }

        public bool IsControlPanelOpen => _controlPanelWindow is { IsVisible: true };

        public float MinDbLevel
        {
            get => _gainParameters!.MinDbValue;
            set => UpdateDbLevel(value, v => v < _gainParameters!.MaxDbValue, v => _gainParameters!.MinDbValue = v,
                _gainParameters!.MaxDbValue - 1, v => AppSettings.UIMinDbLevel = v,
                $"Min dB level ({value}) must be less than max ({_gainParameters!.MaxDbValue})", nameof(MinDbLevel));
        }

        public float MaxDbLevel
        {
            get => _gainParameters!.MaxDbValue;
            set => UpdateDbLevel(value, v => v > _gainParameters!.MinDbValue, v => _gainParameters!.MaxDbValue = v,
                _gainParameters!.MinDbValue + 1, v => AppSettings.UIMaxDbLevel = v,
                $"Max dB level ({value}) must be greater than min ({_gainParameters!.MinDbValue})", nameof(MaxDbLevel));
        }

        public float AmplificationFactor
        {
            get => _gainParameters!.AmplificationFactor;
            set => UpdateDbLevel(value, v => v >= 0, v => _gainParameters!.AmplificationFactor = v, 0,
                v => AppSettings.UIAmplificationFactor = v, $"Amplification factor cannot be negative: {value}", nameof(AmplificationFactor));
        }

        public IReadOnlyDictionary<string, Palette> AvailablePalettes =>
            _spectrumStyles?.RegisteredPalettes.OrderBy(kvp => kvp.Key)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
            ?? new Dictionary<string, Palette>();

        public bool IsPopupOpen
        {
            get => _isPopupOpen;
            set => SetField(ref _isPopupOpen, value);
        }

        public bool IsMaximized => _ownerWindow?.WindowState is WindowState.Maximized;
        #endregion

        #region IAudioVisualizationController Methods
        public bool HandleKeyDown(KeyEventArgs e, IInputElement? focusedElement)
        {
            if (_keyboardManager is not { } keyManager) return false;

            try
            {
                return keyManager.HandleKeyDown(e, focusedElement);
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, LogPrefix, $"Error handling key down: {ex.Message}");
                return false;
            }
        }

        #region Window Control
        public void MinimizeWindow() =>
            _ownerWindow?.Apply(w => w.WindowState = WindowState.Minimized);

        public void MaximizeWindow() =>
            _ownerWindow?.Apply(w => w.WindowState = w.WindowState switch
            {
                WindowState.Maximized => WindowState.Normal,
                _ => WindowState.Maximized
            });

        public void CloseWindow() =>
            _ownerWindow?.Close();

        public void ToggleTheme() =>
            Safe(() => ThemeManager.Instance?.ToggleTheme(),
                new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error toggling theme" });
        #endregion

        #region Control Panel
        public void OpenControlPanel() =>
            SafeExecute(() =>
            {
                if (_controlPanelWindow is { IsVisible: true } panel)
                {
                    panel.Activate();
                    if (panel.WindowState == WindowState.Minimized)
                        panel.WindowState = WindowState.Normal;
                    return;
                }

                _controlPanelWindow = new ControlPanelWindow(this)
                {
                    Owner = _ownerWindow,
                    WindowStartupLocation = WindowStartupLocation.Manual
                };

                if (_ownerWindow is not null)
                {
                    _controlPanelWindow.Left = _ownerWindow.Left + (_ownerWindow.ActualWidth - _controlPanelWindow.Width) / 2;
                    _controlPanelWindow.Top = _ownerWindow.Top + _ownerWindow.ActualHeight - 250;
                }

                _controlPanelWindow.Show();
                _controlPanelWindow.Closed += OnControlPanelClosed;
                OnPropertyChanged(nameof(IsControlPanelOpen));
            }, "Error opening control panel window");

        private void OnControlPanelClosed(object? sender, EventArgs e)
        {
            _controlPanelWindow = null;
            OnPropertyChanged(nameof(IsControlPanelOpen));
            _ownerWindow?.Activate();
            _ownerWindow?.Focus();
        }

        public void CloseControlPanel() =>
            SafeExecute(() =>
            {
                if (_controlPanelWindow is not null)
                {
                    _controlPanelWindow.Closed -= OnControlPanelClosed;
                    _controlPanelWindow.Close();
                    _controlPanelWindow = null;
                    OnPropertyChanged(nameof(IsControlPanelOpen));
                    _ownerWindow?.Activate();
                    _ownerWindow?.Focus();
                }
            }, "Error closing control panel window");

        public void ToggleControlPanel() =>
            ToggleWindow(OpenControlPanel, CloseControlPanel, () => _controlPanelWindow is { IsVisible: true },
                "Error toggling control panel window");

        private void OnFavoriteRenderersChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(OrderedDrawingTypes));
            RestartSettingsTimer();
        }
        #endregion

        #region Audio Capture
        public async Task StartCaptureAsync()
        {
            if (_captureManager is not { } captureManager)
            {
                Log(LogLevel.Error, LogPrefix, "Attempt to start capture with no CaptureManager");
                return;
            }

            await captureManager.StartCaptureAsync();
        }

        public async Task StopCaptureAsync()
        {
            if (_captureManager is not { } captureManager)
            {
                Log(LogLevel.Error, LogPrefix, "Attempt to stop capture with no CaptureManager");
                return;
            }

            await captureManager.StopCaptureAsync();
        }

        public async Task ToggleCaptureAsync()
        {
            if (_captureManager is null) return;

            if (IsRecording)
                await StopCaptureAsync();
            else
                await StartCaptureAsync();
        }
        #endregion

        #region Visualization
        public void RequestRender() =>
            SafeExecute(() => _renderer?.RequestRender(), "Error requesting render");

        public void UpdateRenderDimensions(int width, int height) =>
            SafeExecute(() => _renderer?.UpdateRenderDimensions(width, height), "Error updating render dimensions");

        public void SynchronizeVisualization() =>
            SafeExecute(() => _renderer?.SynchronizeWithController(), "Error synchronizing visualization");

        public void OnPaintSurface(object? sender, SKPaintGLSurfaceEventArgs? e)
        {
            if (e is null || _renderer is null)
            {
                Log(LogLevel.Error, LogPrefix, "PaintSurface called with null arguments");
                return;
            }

            _renderer.RenderFrame(sender, e);
        }

        public SpectrumAnalyzer? GetCurrentAnalyzer() =>
            _captureManager is { } manager ? manager.GetAnalyzer() : null;
        #endregion

        #region Overlay
        public void OpenOverlay() =>
            SafeExecute(() =>
            {
                if (_overlayWindow is { IsInitialized: true } overlay)
                {
                    overlay.Show();
                    overlay.Topmost = IsOverlayTopmost;
                    return;
                }

                var config = new OverlayConfiguration(
                    RenderInterval: 16,
                    IsTopmost: IsOverlayTopmost,
                    ShowInTaskbar: false,
                    EnableHardwareAcceleration: true
                );

                _overlayWindow = new OverlayWindow(this, config)
                    ?? throw new InvalidOperationException("Failed to create overlay window");

                _overlayWindow.Closed += (_, _) => OnOverlayClosed();
                _overlayWindow.Show();

                IsOverlayActive = true;
                SpectrumCanvas.InvalidateVisual();

                _renderer?.UpdateRenderDimensions(
                    (int)SystemParameters.PrimaryScreenWidth,
                    (int)SystemParameters.PrimaryScreenHeight);
            }, "Error opening overlay window");

        public void CloseOverlay() =>
            SafeExecute(() => _overlayWindow?.Close(), "Error closing overlay");

        private void OnOverlayClosed() =>
            SafeExecute(() =>
            {
                if (_overlayWindow is not null)
                {
                    SafeDispose(_overlayWindow, "overlay window",
                        new ErrorHandlingOptions { Source = LogPrefix, ErrorMessage = "Error disposing overlay window" });
                    _overlayWindow = null;
                }

                IsOverlayActive = false;
                _ownerWindow?.Activate();
                _ownerWindow?.Focus();
            }, "Error handling overlay closed");
        #endregion
        #endregion

        #region IDisposable Implementation
        public void DisposeResources()
        {
            if (_isDisposed) return;

            SafeExecute(() => CleanupResourcesAsync().GetAwaiter().GetResult(),
                "Error disposing resources");
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            DisposeResources();
            _isDisposed = true;
        }

        private async Task CleanupResourcesAsync()
        {
            Log(LogLevel.Information, LogPrefix, "Starting cleanup of resources");

            try
            {
                _saveSettingsTimer.Stop();

                // Asynchronous resource disposal
                await DisposeResourceSafelyAsync(async () =>
                {
                    if (_captureManager is not null)
                    {
                        await _captureManager.StopCaptureAsync();
                        _captureManager.Dispose();
                    }
                }, "Audio capture manager");

                // Synchronous resource disposal
                DisposeResourceSafely(() => _overlayWindow?.Dispose(), "Overlay window");
                DisposeResourceSafely(() => _controlPanelWindow?.Dispose(), "Control panel window");
                DisposeResourceSafely(() => _renderer?.Dispose(), "Renderer");
                DisposeResourceSafely(() => _analyzer?.Dispose(), "Analyzer");
                DisposeResourceSafely(() => _disposables?.Dispose(), "Disposables");
                DisposeResourceSafely(() => _transitionSemaphore?.Dispose(), "Transition semaphore");
                DisposeResourceSafely(() => _cleanupCts?.Dispose(), "Cleanup CTS");
                DisposeResourceSafely(() => SpectrumRendererFactory.DisposeAllRenderers(), "Renderer factory");

                // Clear references
                _keyboardManager = null;
                _spectrumStyles = null;
                _ownerWindow = null;

                Log(LogLevel.Information, LogPrefix, "Resource cleanup completed successfully");
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, LogPrefix, $"Error during resource cleanup: {ex.Message}");
            }
        }

        private void DisposeResourceSafely(Action action, string resourceName)
        {
            try
            {
                action();
                Log(LogLevel.Information, LogPrefix, $"{resourceName} disposed");
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, LogPrefix, $"Error disposing {resourceName}: {ex.Message}");
            }
        }

        private async Task DisposeResourceSafelyAsync(Func<Task> asyncAction, string resourceName)
        {
            try
            {
                await asyncAction();
                Log(LogLevel.Information, LogPrefix, $"{resourceName} disposed");
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, LogPrefix, $"Error disposing {resourceName}: {ex.Message}");
            }
        }
        #endregion

        #region Helper Methods
        private ErrorHandlingOptions GetLoggerOptions(string errorMessage) =>
            new() { Source = LogPrefix, ErrorMessage = errorMessage };

        private void SafeExecute(Action action, string errorMessage) =>
            Safe(action, GetLoggerOptions(errorMessage));

        private void ToggleWindow(Action openAction, Action closeAction, Func<bool> isOpenChecker, string errorMessage) =>
            SafeExecute(() => (isOpenChecker() ? closeAction : openAction)(), errorMessage);

        private void UpdateOverlayTopmostState() =>
            SafeExecute(() =>
            {
                if (_overlayWindow is { IsInitialized: true } overlay)
                    overlay.Topmost = IsOverlayTopmost;
            }, "Error updating topmost state");

        private void UpdateGainParameter(float newValue, Action<float> setter, string propertyName) =>
            SafeExecute(() =>
            {
                if (setter is null)
                {
                    Log(LogLevel.Error, LogPrefix, "Null delegate passed for parameter update");
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
                Log(LogLevel.Warning, LogPrefix, $"Attempt to set {errorMessage}: {value}");
                value = defaultValue;
            }

            setter(value);
            OnPropertyChanged(propertyName);
            RestartSettingsTimer();
        }

        private void UpdateEnumProperty<T>(ref T field, T value, Action<T> settingUpdater,
                                         [CallerMemberName] string propertyName = "")
                                         where T : struct, Enum
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;

            field = value;
            OnPropertyChanged(propertyName);
            settingUpdater(value);
            RestartSettingsTimer();
        }

        private void UpdateDbLevel(float value, Func<float, bool> validator, Action<float> setter,
                                 float fallbackValue, Action<float> settingUpdater, string errorMessage,
                                 string propertyName)
        {
            if (_gainParameters is null)
            {
                Log(LogLevel.Error, LogPrefix, "Gain parameters not initialized in UpdateDbLevel");
                return;
            }

            if (!validator(value))
            {
                Log(LogLevel.Warning, LogPrefix, errorMessage);
                value = fallbackValue;
            }

            UpdateGainParameter(value, setter, propertyName);
            settingUpdater(value);
            RestartSettingsTimer();
        }

        protected bool SetField<T>(ref T field, T value, Action? callback = null, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName ?? string.Empty);

            callback?.Apply(c => SafeExecute(c, $"Error executing callback for {propertyName}"));

            return true;
        }

        private void RestartSettingsTimer()
        {
            _saveSettingsTimer.Stop();
            _saveSettingsTimer.Start();
        }

        public void OnPropertyChanged(params string[] propertyNames) =>
            SafeExecute(() =>
            {
                foreach (var name in propertyNames)
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }, "Error notifying property change");
        #endregion
    }
}