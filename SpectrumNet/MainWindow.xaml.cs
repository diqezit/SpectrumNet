#nullable enable
namespace SpectrumNet
{
    public static class MwConstants
    {
        public const int RenderIntervalMs = 16;
        public const int MonitorDelay = 16;
        public const int FftSize = 2048;
        public const int BarCount = 120;
        public const double BarWidth = 8;
        public const double BarSpacing = 2;
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
                new FftProcessor(MwConstants.FftSize),
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
        private record WindowState(
            string Style = MwConstants.DefaultStyle,
            double BarSpacing = MwConstants.BarSpacing,
            double BarWidth = MwConstants.BarWidth,
            int BarCount = MwConstants.BarCount,
            string StatusText = MwConstants.ReadyStatus);

        private readonly object _lock = new();
        private WindowState _state = new();
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
        public SKElement? RenderElement { get; private set; }
        public bool CanStartCapture => _captureManager is not null && !IsRecording;

        public MainWindow()
        {
            InitializeComponent();

            try
            {
                _gainParameters = new GainParameters(SynchronizationContext.Current);
                DataContext = this;
                InitializeComponents();
                SetupEventHandlers();
                ConfigureTheme();
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
            RenderElement = spectrumCanvas;
            _spectrumStyles = new SpectrumBrushes();
            _disposables = new CompositeDisposable();

            var renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(MwConstants.RenderIntervalMs) };
            renderTimer.Tick += (_, _) => RenderElement?.InvalidateVisual();
            renderTimer.Start();

            _analyzer = new SpectrumAnalyzer(new FftProcessor(MwConstants.FftSize),
                new SpectrumConverter(_gainParameters), SynchronizationContext.Current);

            if (_analyzer == null)
            {
                Log.Fatal("[MainWindow] SpectrumAnalyzer initialization failed.");
                throw new InvalidOperationException("Failed to initialize SpectrumAnalyzer.");
            }

            _captureManager = new AudioCaptureManager(this);
            if (_captureManager == null)
            {
                Log.Fatal("[MainWindow] AudioCaptureManager initialization failed.");
                throw new InvalidOperationException("Failed to initialize AudioCaptureManager.");
            }

            _renderer = new Renderer(_spectrumStyles, this, _analyzer, RenderElement);
            if (_renderer == null)
            {
                Log.Fatal("[MainWindow] Renderer initialization failed.");
                throw new InvalidOperationException("Failed to initialize Renderer.");
            }

            SelectedStyle = MwConstants.DefaultStyle;
        }

        private void SetupEventHandlers()
        {
            RenderElement.PaintSurface += OnPaintSurface;
            SizeChanged += OnWindowSizeChanged;
            StyleComboBox.SelectionChanged += OnComboBoxSelectionChanged;
            RenderStyleComboBox.SelectionChanged += OnComboBoxSelectionChanged;
            Closed += OnWindowClosed;
        }

        private void ConfigureTheme()
        {
            try
            {
                ThemeManager.Instance.RegisterWindow(this);
                ThemeManager.Instance.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(ThemeManager.IsDarkTheme))
                        OnPropertyChanged(nameof(IsDarkTheme));
                };
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "[MainWindow] Error while configuring theme.");
            }
        }

        private void UpdateProperties() => OnPropertyChanged(nameof(IsRecording), nameof(CanStartCapture), nameof(StatusText));

        private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
        {
            try
            {
                _renderer?.UpdateRenderDimensions((int)e.NewSize.Width, (int)e.NewSize.Height);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "[MainWindow] Error during window size change.");
            }
        }

        public void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
        {
            try
            {
                _renderer?.RenderFrame(sender, e);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "[MainWindow] Error during paint surface rendering.");
            }
        }

        private void OnComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (sender == StyleComboBox && _renderer != null && SelectedStyle != null && _spectrumStyles != null)
                {
                    var (startColor, endColor, _) = _spectrumStyles.GetColorsAndBrush(SelectedStyle);
                    _renderer.UpdateSpectrumStyle(SelectedStyle, startColor, endColor);
                }
                else if (sender == RenderStyleComboBox && RenderStyleComboBox.SelectedValue is RenderStyle renderStyle)
                {
                    _selectedDrawingType = renderStyle;
                    _renderer?.UpdateRenderStyle(renderStyle);
                }
                InvalidateVisuals();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "[MainWindow] Error during combo box selection change.");
            }
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            try
            {
                if (IsOverlayActive) CloseOverlay();
                _renderer?.Dispose();
                _analyzer?.Dispose();
                _captureManager?.Dispose();
                _disposables?.Dispose();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "[MainWindow] Error during window close event.");
            }
        }

        private void OnOpenPopupButtonClick(object sender, RoutedEventArgs e) => IsPopupOpen = !IsPopupOpen;

        private void OnOpenSettingsButtonClick(object sender, RoutedEventArgs e)
        {
            try
            {
                new SettingsWindow().ShowDialog();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "[MainWindow] Error opening settings window.");
            }
        }

        private void OnSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                if (sender is Slider slider)
                {
                    var value = slider.Value;
                    var updates = new Dictionary<string, Action>
                    {
                        ["barWidthSlider"] = () => BarWidth = value,
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
            catch (Exception ex)
            {
                Log.Fatal(ex, "[MainWindow] Error during slider value change.");
            }
        }

        private async void OnButtonClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button button)
                {
                    switch (button.Name)
                    {
                        case "StartCaptureButton": await StartCaptureAsync(); break;
                        case "StopCaptureButton": await StopCaptureAsync(); break;
                        case "OverlayButton": OnOverlayButtonClick(sender, e); break;
                        case "OpenSettingsButton": OnOpenSettingsButtonClick(sender, e); break;
                        case "OpenPopupButton": OnOpenPopupButtonClick(sender, e); break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "[MainWindow] Error during button click event.");
            }
        }

        private void OnOverlayButtonClick(object sender, RoutedEventArgs e) => (IsOverlayActive ? (Action)CloseOverlay : OpenOverlay)();

        private void OnThemeToggleButtonChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                ThemeManager.Instance.ToggleTheme();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "[MainWindow] Error toggling theme.");
            }
        }

        private void OpenOverlay()
        {
            try
            {
                _overlayWindow = new OverlayWindow(this, new OverlayConfiguration { RenderInterval = MwConstants.RenderIntervalMs, IsTopmost = true, ShowInTaskbar = false });
                _overlayWindow.Closed += (_, _) => OnOverlayClosed();
                _overlayWindow.Show();
                IsOverlayActive = true;
                UpdateRendererDimensions((int)SystemParameters.PrimaryScreenWidth, (int)SystemParameters.PrimaryScreenHeight);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "[MainWindow] Error opening overlay.");
            }
        }

        private void OnOverlayClosed()
        {
            try
            {
                IsOverlayActive = false;
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "[MainWindow] Error while closing overlay.");
            }
        }

        public void CloseOverlay()
        {
            try
            {
                if (_overlayWindow != null)
                {
                    _overlayWindow.Close();
                    _overlayWindow.Dispose();
                    _overlayWindow = null;
                }
                IsOverlayActive = false;
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "[MainWindow] Error during overlay close.");
            }
        }

        private void UpdateRendererDimensions(int? width, int? height)
        {
            try
            {
                _renderer?.UpdateRenderDimensions(width ?? 0, height ?? 0);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "[MainWindow] Error updating renderer dimensions.");
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
                try
                {
                    if (_captureManager?.IsRecording != value)
                    {
                        _ = value ? StartCaptureAsync() : StopCaptureAsync();
                        OnPropertyChanged(nameof(IsRecording), nameof(CanStartCapture));
                    }
                }
                catch (Exception ex)
                {
                    Log.Fatal(ex, "[MainWindow] Error setting IsRecording property.");
                }
            }
        }

        public double BarWidth
        {
            get => _state.BarWidth;
            set => UpdateState(s => s with { BarWidth = value }, nameof(BarWidth), InvalidateVisuals);
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
            try
            {
                setter(newValue);
                OnPropertyChanged(propertyName);
                InvalidateVisuals();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, $"[MainWindow] Error updating gain parameter {propertyName}.");
            }
        }

        private void UpdateState(Func<WindowState, WindowState> updater, string propertyName, Action? callback = null)
        {
            try
            {
                _state = updater(_state);
                OnPropertyChanged(propertyName);
                callback?.Invoke();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, $"[MainWindow] Error updating state for property {propertyName}.");
            }
        }

        private void InvalidateVisuals()
        {
            try
            {
                RenderElement?.InvalidateVisual();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "[MainWindow] Error invalidating visuals.");
            }
        }

        public Task StartCaptureAsync()
        {
            try
            {
                return _captureManager?.StartCaptureAsync() ?? Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "[MainWindow] Error starting capture.");
                return Task.FromException(ex);
            }
        }

        public Task StopCaptureAsync()
        {
            try
            {
                return _captureManager?.StopCaptureAsync() ?? Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "[MainWindow] Error stopping capture.");
                return Task.FromException(ex);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public virtual void OnPropertyChanged(params string[] propertyNames)
        {
            try
            {
                foreach (var name in propertyNames)
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "[MainWindow] Error raising PropertyChanged event.");
            }
        }

        protected bool SetField<T>(ref T field, T value, Action? callback = null, [CallerMemberName] string? propertyName = null)
        {
            try
            {
                if (EqualityComparer<T>.Default.Equals(field, value)) return false;
                field = value;
                OnPropertyChanged(propertyName ?? string.Empty);
                callback?.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, $"[MainWindow] Error setting field for property {propertyName}.");
                return false;
            }
        }
    }
}