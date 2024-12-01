#nullable enable
using System.Windows.Media;

namespace SpectrumNet
{
    public static class MwConstants
    {
        public const int RenderIntervalMs = 16;
        public const int MonitorDelay = 16;
        public const int BarCount = 60;
        public const double BarSpacing = 4;
        public const string DefaultStyle = "Gradient";
        public const string ReadyStatus = "Готово";
        public const string RecordingStatus = "Запись...";
    }

    public sealed class AudioCaptureManager : IDisposable
    {
        private readonly MainWindow _mainWindow;
        private readonly object _lock = new();
        private record CaptureState(
            SpectrumAnalyzer Analyzer,
            WasapiLoopbackCapture Capture,
            CancellationTokenSource Cts);

        private CaptureState? _state;
        private bool _isDisposed;

        public bool IsRecording { get; private set; }

        public AudioCaptureManager(MainWindow mainWindow)
        {
            if (mainWindow == null)
            {
                Log.Fatal("[AudioCaptureManager] MainWindow is null in AudioCaptureManager constructor.");
                throw new ArgumentNullException(nameof(mainWindow));
            }
            _mainWindow = mainWindow;
        }

        public async Task StartCaptureAsync()
        {
            lock (_lock)
            {
                if (_isDisposed)
                {
                    Log.Fatal("[AudioCaptureManager] Attempted to start capture on a disposed AudioCaptureManager.");
                    throw new ObjectDisposedException(nameof(AudioCaptureManager));
                }

                InitializeCapture();
            }

            if (_state?.Cts == null)
            {
                Log.Fatal("[AudioCaptureManager] CancellationTokenSource is null in StartCaptureAsync.");
                throw new InvalidOperationException("Capture state is not properly initialized.");
            }

            await MonitorCaptureAsync(_state.Cts.Token);
        }

        public Task StopCaptureAsync()
        {
            lock (_lock)
            {
                if (_state?.Cts == null)
                {
                    Log.Fatal("[AudioCaptureManager] CancellationTokenSource is null in StopCaptureAsync.");
                }
                else
                {
                    _state.Cts.Cancel();
                    _state.Capture?.StopRecording();
                }
                DisposeState();
            }

            UpdateStatus(false);
            return Task.CompletedTask;
        }

        private void InitializeCapture()
        {
            DisposeState();

            var capture = new WasapiLoopbackCapture();
            var analyzer = InitializeAnalyzer();

            if (capture == null || analyzer == null)
            {
                Log.Fatal("[AudioCaptureManager] Failed to initialize capture or analyzer in InitializeCapture.");
                throw new InvalidOperationException("Failed to initialize capture or analyzer.");
            }

            _state = new CaptureState(
                analyzer,
                capture,
                new CancellationTokenSource());

            if (_state.Capture == null)
            {
                Log.Fatal("[AudioCaptureManager] WasapiLoopbackCapture is null after initialization.");
                throw new InvalidOperationException("Capture initialization failed.");
            }

            if (_state.Analyzer == null)
            {
                Log.Fatal("[AudioCaptureManager] SpectrumAnalyzer is null after initialization.");
                throw new InvalidOperationException("Analyzer initialization failed.");
            }

            _state.Capture.DataAvailable += OnDataAvailable;
            UpdateStatus(true);
            _state.Capture.StartRecording();
        }

        private SpectrumAnalyzer InitializeAnalyzer() => _mainWindow.Dispatcher.Invoke(() =>
        {
            var analyzer = new SpectrumAnalyzer(
                new FftProcessor(),
                new SpectrumConverter(_mainWindow._gainParameters),
                SynchronizationContext.Current);

            if (_mainWindow.RenderElement is { ActualWidth: > 0, ActualHeight: > 0 })
            {
                _mainWindow._renderer?.Dispose();
                _mainWindow._renderer = new Renderer(
                    _mainWindow._spectrumStyles ?? new SpectrumBrushes(),
                    _mainWindow,
                    analyzer,
                    _mainWindow.RenderElement);
            }
            else
            {
                Log.Fatal("[AudioCaptureManager] RenderElement has null dimensions or is not properly initialized.");
            }

            return analyzer;
        });

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded <= 0 || _state?.Analyzer == null || _state.Capture == null)
            {
                Log.Fatal("[AudioCaptureManager] DataAvailable event received with invalid state or no data.");
                return;
            }

            var samples = new float[e.BytesRecorded / 4];
            try
            {
                Buffer.BlockCopy(e.Buffer, 0, samples, 0, e.BytesRecorded);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "[AudioCaptureManager] Error copying buffer in OnDataAvailable.");
            }

            _ = _state.Analyzer.AddSamplesAsync(samples, _state.Capture.WaveFormat.SampleRate);
        }

        private async Task MonitorCaptureAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(MwConstants.MonitorDelay, token);
                    _mainWindow.Dispatcher.Invoke(() => _mainWindow.RenderElement?.InvalidateVisual());
                }
            }
            catch (TaskCanceledException) { }
            catch (Exception ex)
            {
                Log.Fatal(ex, "[AudioCaptureManager] Unexpected error in MonitorCaptureAsync.");
            }
        }

        private void UpdateStatus(bool isRecording) => _mainWindow.Dispatcher.Invoke(() =>
        {
            IsRecording = isRecording;
            _mainWindow.IsRecording = isRecording;
            _mainWindow.StatusText = isRecording ? MwConstants.RecordingStatus : MwConstants.ReadyStatus;
            _mainWindow.OnPropertyChanged(
                nameof(_mainWindow.IsRecording),
                nameof(_mainWindow.CanStartCapture),
                nameof(_mainWindow.StatusText));
        });

        private void DisposeState()
        {
            if (_state == null)
            {
                Log.Fatal("[AudioCaptureManager] DisposeState called with a null state.");
                return;
            }

            _state.Cts?.Dispose();
            _state.Capture?.Dispose();
            _state.Analyzer?.Dispose();
            _state = null;
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            lock (_lock)
            {
                if (_isDisposed) return;
                StopCaptureAsync().GetAwaiter().GetResult();
                _isDisposed = true;
            }
        }
    }

    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private record MainWindowSettings(
            string Style = MwConstants.DefaultStyle,
            double BarSpacing = MwConstants.BarSpacing,
            int BarCount = MwConstants.BarCount,
            string StatusText = MwConstants.ReadyStatus);

        private readonly object _lock = new();
        private MainWindowSettings _state = new();
        private bool _isOverlayActive, _isPopupOpen;
        private RenderStyle _selectedDrawingType;
        private OverlayWindow? _overlayWindow;

        internal SpectrumBrushes? _spectrumStyles;
        internal Renderer? _renderer;
        internal SpectrumAnalyzer? _analyzer;
        internal AudioCaptureManager? _captureManager;
        internal GainParameters _gainParameters;
        internal CompositeDisposable? _disposables;

        public bool IsDarkTheme => ThemeManager.Instance.IsDarkTheme;
        public IEnumerable<RenderStyle> AvailableDrawingTypes => Enum.GetValues<RenderStyle>();
        public IReadOnlyDictionary<string, StyleDefinition> AvailableStyles =>
            _spectrumStyles?.Styles ?? new Dictionary<string, StyleDefinition>();
        public IEnumerable<FftWindowType> AvailableFftWindowTypes =>
            Enum.GetValues(typeof(FftWindowType)).Cast<FftWindowType>();
        public SKElement? RenderElement { get; private set; }
        public bool CanStartCapture => _captureManager is not null && !IsRecording;

        public MainWindow()
        {
            InitializeComponent();

            try
            {
                Log.Information("[MainWindow] Initializing MainWindow.");

                _gainParameters = new GainParameters(SynchronizationContext.Current);
                if (_gainParameters == null)
                {
                    Log.Warning("[MainWindow] _gainParameters is null after initialization.");
                }
                else
                {
                    Log.Information("[MainWindow] _gainParameters initialized successfully.");
                }

                DataContext = this;

                Log.Information("[MainWindow] Initializing components.");
                InitializeComponents();

                Log.Information("[MainWindow] Setting up event handlers.");
                SetupEventHandlers();

                Log.Information("[MainWindow] Configuring theme.");
                ConfigureTheme();

                Log.Information("[MainWindow] Updating properties.");
                UpdateProperties();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "[MainWindow] Error during MainWindow initialization.");
                throw;
            }
        }

        private void InitializeComponents()
        {
            try
            {
                Log.Information("[MainWindow] Initializing components.");

                if (spectrumCanvas != null)
                {
                    RenderElement = spectrumCanvas;
                    Log.Information("[MainWindow] RenderElement set to spectrumCanvas.");
                }
                else
                {
                    Log.Warning("[MainWindow] spectrumCanvas is null. RenderElement not set.");
                }

                _spectrumStyles = new SpectrumBrushes();
                Log.Information("[MainWindow] SpectrumBrushes initialized.");

                _disposables = new CompositeDisposable();
                Log.Information("[MainWindow] CompositeDisposable initialized.");

                var renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(MwConstants.RenderIntervalMs) };
                renderTimer.Tick += (_, _) => RenderElement?.InvalidateVisual();
                renderTimer.Start();
                Log.Information("[MainWindow] Render timer started.");

                if (_gainParameters != null)
                {
                    _analyzer = new SpectrumAnalyzer(new FftProcessor(),
                        new SpectrumConverter(_gainParameters), SynchronizationContext.Current);
                    Log.Information("[MainWindow] SpectrumAnalyzer initialized.");
                }
                else
                {
                    Log.Warning("[MainWindow] _gainParameters is null. SpectrumAnalyzer not initialized.");
                }

                if (this != null)
                {
                    _captureManager = new AudioCaptureManager(this);
                    Log.Information("[MainWindow] AudioCaptureManager initialized.");
                }
                else
                {
                    Log.Warning("[MainWindow] MainWindow is null. AudioCaptureManager not initialized.");
                }

                if (_spectrumStyles != null && RenderElement != null)
                {
                    _renderer = new Renderer(_spectrumStyles, this, _analyzer 
                        ?? throw new ArgumentNullException(nameof(_analyzer)), RenderElement);
                    Log.Information("[MainWindow] Renderer initialized.");
                }
                else
                {
                    Log.Warning("[MainWindow] _spectrumStyles or RenderElement is null. Renderer not initialized.");
                }

                SelectedStyle = MwConstants.DefaultStyle;
                Log.Information("[MainWindow] Default style set.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[MainWindow] Error during InitializeComponents.");
            }
        }

        private void SetupEventHandlers()
        {
            try
            {
                Log.Information("[MainWindow] Setting up event handlers.");

                if (RenderElement != null)
                {
                    Log.Information("[MainWindow] Subscribing to PaintSurface event.");
                    RenderElement.PaintSurface += OnPaintSurface;
                }
                else
                {
                    Log.Warning("[MainWindow] RenderElement is null. Cannot subscribe to PaintSurface event.");
                }

                SizeChanged += OnWindowSizeChanged;
                Log.Information("[MainWindow] Subscribed to SizeChanged event.");

                if (StyleComboBox != null)
                {
                    Log.Information("[MainWindow] Subscribing to StyleComboBox.SelectionChanged event.");
                    StyleComboBox.SelectionChanged += OnComboBoxSelectionChanged;
                }
                else
                {
                    Log.Warning("[MainWindow] StyleComboBox is null. Cannot subscribe to SelectionChanged event.");
                }

                if (RenderStyleComboBox != null)
                {
                    Log.Information("[MainWindow] Subscribing to RenderStyleComboBox.SelectionChanged event.");
                    RenderStyleComboBox.SelectionChanged += OnComboBoxSelectionChanged;
                }
                else
                {
                    Log.Warning("[MainWindow] RenderStyleComboBox is null. Cannot subscribe to SelectionChanged event.");
                }

                this.StateChanged += OnStateChanged;
                Log.Information("[MainWindow] Subscribed to StateChanged event.");

                Closed += OnWindowClosed;
                Log.Information("[MainWindow] Subscribed to Closed event.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[MainWindow] Error during SetupEventHandlers.");
            }
        }

        private void OnStateChanged(object sender, EventArgs e)
        {
            if (MaximizeButton != null && MaximizeIcon != null)
            {
                if (this.WindowState == WindowState.Maximized)
                {
                    MaximizeIcon.Data = Geometry.Parse("M0,0 L20,0 L20,20 L0,20 Z");
                }
                else
                {
                    MaximizeIcon.Data = Geometry.Parse("M2,2 H18 V18 H2 Z");
                }
            }
        }

        private void ConfigureTheme()
        {
            ThemeManager.Instance.RegisterWindow(this);
            ThemeManager.Instance.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ThemeManager.IsDarkTheme))
                    OnPropertyChanged(nameof(IsDarkTheme));
            };
        }

        private void UpdateProperties() => OnPropertyChanged(nameof(IsRecording), nameof(CanStartCapture), nameof(StatusText));

        private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e) =>
            _renderer?.UpdateRenderDimensions((int)e.NewSize.Width, (int)e.NewSize.Height);

        public void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e) =>
            _renderer?.RenderFrame(sender, e);

        private void OnComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                Log.Debug("[MainWindow] ComboBox selection changed. Sender: {SenderType}", sender?.GetType().Name);

                if (sender == StyleComboBox && _renderer != null && SelectedStyle != null && _spectrumStyles != null)
                {
                    Log.Debug("[MainWindow] Selected StyleComboBox. SelectedStyle: {SelectedStyle}", SelectedStyle);

                    var (startColor, endColor, _) = _spectrumStyles.GetColorsAndBrush(SelectedStyle);

                    Log.Debug("[MainWindow] Updating spectrum style. StartColor: {StartColor}, EndColor: {EndColor}", startColor, endColor);
                    _renderer.UpdateSpectrumStyle(SelectedStyle, startColor, endColor);
                }
                else if (sender == RenderStyleComboBox && RenderStyleComboBox.SelectedValue is RenderStyle renderStyle)
                {
                    Log.Debug("[MainWindow] Selected RenderStyleComboBox. RenderStyle: {RenderStyle}", renderStyle);

                    _selectedDrawingType = renderStyle;

                    Log.Debug("[MainWindow] Updating render style: {RenderStyle}", renderStyle);
                    _renderer?.UpdateRenderStyle(renderStyle);
                }
                else if (sender == FftWindowTypeComboBox && FftWindowTypeComboBox.SelectedValue is FftWindowType windowType)
                {
                    Log.Debug("[MainWindow] Selected FftWindowTypeComboBox. FftWindowType: {WindowType}", windowType);

                    SelectedFftWindowType = windowType;
                }

                Log.Debug("[MainWindow] Invalidating visuals...");
                InvalidateVisuals();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[MainWindow] Error during ComboBox selection change.");
            }
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            if (IsOverlayActive) CloseOverlay();
            _renderer?.Dispose();
            _analyzer?.Dispose();
            _captureManager?.Dispose();
            _disposables?.Dispose();
        }

        private void CloseWindow()
        {
            this.Close();
        }

        private void OnOpenPopupButtonClick(object sender, RoutedEventArgs e) => IsPopupOpen = !IsPopupOpen;

        private void OnOpenSettingsButtonClick(object sender, RoutedEventArgs e) =>
            new SettingsWindow().ShowDialog();

        private void OnWindowDrag(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void MinimizeWindow()
        {
            this.WindowState = WindowState.Minimized;
        }

        private void MaximizeWindow()
        {
            if (this.WindowState == WindowState.Maximized)
            {
                this.WindowState = WindowState.Normal;
            }
            else
            {
                this.WindowState = WindowState.Maximized;
            }
        }

        private void OnSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (sender is Slider slider)
            {
                var value = slider.Value;
                var updates = new Dictionary<string, Action>
                {
                    ["barSpacingSlider"] = () => BarSpacing = value,
                    ["barCountSlider"] = () => BarCount = (int)value,
                    ["minDbLevelSlider"] = () => MinDbLevel = (float)value,
                    ["maxDbLevelSlider"] = () => MaxDbLevel = (float)value,
                    ["adaptionRateSlider"] = () => AmplificationFactor = (float)value
                };

                if (updates.TryGetValue(slider.Name, out var action))
                {
                    action();
                    InvalidateVisuals();
                }
            }
        }

        private async void OnButtonClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                switch (button.Name)
                {
                    case "StartCaptureButton":
                        await StartCaptureAsync();
                        break;
                    case "StopCaptureButton":
                        await StopCaptureAsync();
                        break;
                    case "OverlayButton":
                        OnOverlayButtonClick(sender, e);
                        break;
                    case "OpenSettingsButton":
                        OnOpenSettingsButtonClick(sender, e);
                        break;
                    case "OpenPopupButton":
                        OnOpenPopupButtonClick(sender, e);
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
                }
            }
        }

        private void OnOverlayButtonClick(object sender, RoutedEventArgs e) =>
            (IsOverlayActive ? (Action)CloseOverlay : OpenOverlay)();

        private void OnThemeToggleButtonChanged(object sender, RoutedEventArgs e) =>
            ThemeManager.Instance.ToggleTheme();

        private void OpenOverlay()
        {
            try
            {
                Log.Information("[MainWindow] Attempting to open overlay.");

                if (_overlayWindow == null)
                {
                    _overlayWindow = new OverlayWindow(this, new OverlayConfiguration
                    {
                        RenderInterval = MwConstants.RenderIntervalMs,
                        IsTopmost = true,
                        ShowInTaskbar = false
                    });

                    _overlayWindow.Closed += (_, _) => OnOverlayClosed();
                    _overlayWindow.Show();
                    IsOverlayActive = true;
                    Log.Information("[MainWindow] Overlay window opened and set to active.");
                }
                else
                {
                    Log.Warning("[MainWindow] Overlay window is already open.");
                }

                UpdateRendererDimensions((int)SystemParameters.PrimaryScreenWidth, (int)SystemParameters.PrimaryScreenHeight);
                Log.Information("[MainWindow] Renderer dimensions updated.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[MainWindow] Error during OpenOverlay.");
            }
        }

        private void OnOverlayClosed()
        {
            Log.Information("[MainWindow] Overlay closed.");
            IsOverlayActive = false;
        }

        public void CloseOverlay()
        {
            try
            {
                Log.Information("[MainWindow] Attempting to close overlay.");

                if (_overlayWindow != null)
                {
                    _overlayWindow.Close();
                    _overlayWindow.Dispose();
                    _overlayWindow = null;
                    Log.Information("[MainWindow] Overlay window closed and disposed.");
                }
                else
                {
                    Log.Warning("[MainWindow] Overlay window is already null.");
                }

                IsOverlayActive = false;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[MainWindow] Error during CloseOverlay.");
            }
        }

        private void UpdateRendererDimensions(int? width, int? height) =>
            _renderer?.UpdateRenderDimensions(width ?? 0, height ?? 0);

        private FftWindowType _selectedFftWindowType;
        public FftWindowType SelectedFftWindowType
        {
            get => _selectedFftWindowType;
            set
            {
                if (_selectedFftWindowType != value)
                {
                    _selectedFftWindowType = value;
                    OnPropertyChanged(nameof(SelectedFftWindowType));
                    UpdateFftWindowType(value);
                }
            }
        }

        private void UpdateFftWindowType(FftWindowType windowType)
        {
            if (_analyzer?.FftProcessor != null)
            {
                _analyzer.FftProcessor.WindowType = windowType;
            }
        }

        public bool IsPopupOpen
        {
            get => _isPopupOpen;
            set => SetField(ref _isPopupOpen, value);
        }

        public bool IsOverlayActive
        {
            get => _isOverlayActive;
            set => SetField(ref _isOverlayActive, value);
        }

        public bool IsRecording
        {
            get => _captureManager?.IsRecording ?? false;
            set
            {
                if (_captureManager?.IsRecording != value)
                {
                    _ = value ? StartCaptureAsync() : StopCaptureAsync();
                    OnPropertyChanged(nameof(IsRecording), nameof(CanStartCapture));
                }
            }
        }

        public double BarSpacing
        {
            get => _state.BarSpacing;
            set => UpdateState(s => s with { BarSpacing = value }, nameof(BarSpacing), InvalidateVisuals);
        }

        public int BarCount
        {
            get => _state.BarCount;
            set => UpdateState(s => s with { BarCount = value }, nameof(BarCount), InvalidateVisuals);
        }

        public RenderStyle SelectedDrawingType
        {
            get => _selectedDrawingType;
            set => SetField(ref _selectedDrawingType, value, () => _renderer?.UpdateRenderStyle(value));
        }

        public string SelectedStyle
        {
            get => _state.Style;
            set => UpdateState(s => s with { Style = value }, nameof(SelectedStyle), () =>
            {
                var (sC, eC, _) = _spectrumStyles?.GetColorsAndBrush(value) ?? default;
                _renderer?.UpdateSpectrumStyle(value, sC, eC);
                InvalidateVisuals();
            });
        }

        public string StatusText
        {
            get => _state.StatusText;
            set => UpdateState(s => s with { StatusText = value }, nameof(StatusText));
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

        private void UpdateState(Func<MainWindowSettings, 
            MainWindowSettings> updater, string propertyName,
            Action? callback = null)
        {
            _state = updater(_state);
            OnPropertyChanged(propertyName);
            callback?.Invoke();
        }

        private void InvalidateVisuals() => RenderElement?.InvalidateVisual();

        public Task StartCaptureAsync()
        {
            try
            {
                Log.Information("[MainWindow] Attempting to start capture.");

                if (_captureManager != null)
                {
                    Log.Information("[MainWindow] Starting capture.");
                    return _captureManager.StartCaptureAsync();
                }
                else
                {
                    Log.Warning("[MainWindow] _captureManager is null, cannot start capture.");
                    return Task.CompletedTask;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[MainWindow] Error during StartCaptureAsync.");
                return Task.CompletedTask;
            }
        }

        public Task StopCaptureAsync()
        {
            try
            {
                Log.Information("[MainWindow] Attempting to stop capture.");

                if (_captureManager != null)
                {
                    Log.Information("[MainWindow] Stopping capture.");
                    return _captureManager.StopCaptureAsync();
                }
                else
                {
                    Log.Warning("[MainWindow] _captureManager is null, cannot stop capture.");
                    return Task.CompletedTask;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[MainWindow] Error during StopCaptureAsync.");
                return Task.CompletedTask;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public virtual void OnPropertyChanged(params string[] propertyNames)
        {
            foreach (var name in propertyNames)
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        protected bool SetField<T>(ref T field, T value, Action? callback = null,
            [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName ?? string.Empty);
            callback?.Invoke();
            return true;
        }
    }
}