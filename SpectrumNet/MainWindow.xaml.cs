#nullable enable
namespace SpectrumNet
{
    public static class MainWindowConstants
    {
        public static class Defaults
        {
            public const int RenderIntervalMs = 16;
            public const int MonitorDelay = 16;
            public const int FftSize = 2048;
            public const int BarCount = 120;
            public const double BarWidth = 8;
            public const double BarSpacing = 2;
            public const string Style = "Gradient";
            public const string StatusText = "Готово";
        }
    }

    public sealed class AudioCaptureManager : IDisposable
    {
        private readonly MainWindow _mainWindow;
        private readonly object _lock = new();
        private SpectrumAnalyzer? _analyzer;
        private WasapiLoopbackCapture? _capture;
        private CancellationTokenSource? _cts;
        private bool _isDisposed;

        public bool IsRecording { get; private set; }

        public AudioCaptureManager(MainWindow mainWindow) =>
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));

        public async Task StartCaptureAsync()
        {
            lock (_lock)
            {
                if (_isDisposed) throw new ObjectDisposedException(nameof(AudioCaptureManager));
                InitializeCapture();
            }

            await MonitorCaptureAsync(_cts!.Token);
        }

        public async Task StopCaptureAsync()
        {
            lock (_lock)
            {
                _cts?.Cancel();
                _capture?.StopRecording();
                DisposeComponents();
            }

            UpdateStatus(false);
        }

        private void InitializeCapture()
        {
            DisposeComponents();

            _cts = new CancellationTokenSource();
            _capture = new WasapiLoopbackCapture();
            _capture.DataAvailable += OnDataAvailable;

            InitializeAnalyzer();
            UpdateStatus(true);
            _capture.StartRecording();
        }

        private void InitializeAnalyzer() => _mainWindow.Dispatcher.Invoke(() =>
        {
            _analyzer = new SpectrumAnalyzer(
                new FftProcessor(MainWindowConstants.Defaults.FftSize),
                new SpectrumConverter(_mainWindow._gainParameters),
                SynchronizationContext.Current);

            if (_mainWindow.RenderElement is { ActualWidth: > 0, ActualHeight: > 0 })
            {
                _mainWindow._renderer?.Dispose();
                _mainWindow._renderer = new Renderer(
                    _mainWindow._spectrumStyles ?? new SpectrumBrushes(),
                    _mainWindow,
                    _analyzer,
                    _mainWindow.RenderElement);
            }
        });

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded <= 0 || _analyzer == null || _capture == null) return;

            var samples = new float[e.BytesRecorded / 4];
            Buffer.BlockCopy(e.Buffer, 0, samples, 0, e.BytesRecorded);
            _ = _analyzer.AddSamplesAsync(samples, _capture.WaveFormat.SampleRate);
        }

        private async Task MonitorCaptureAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(MainWindowConstants.Defaults.MonitorDelay, token);
                    _mainWindow.Dispatcher.Invoke(() => _mainWindow.RenderElement?.InvalidateVisual());
                }
            }
            catch (TaskCanceledException) { }
        }

        private void UpdateStatus(bool isRecording) => _mainWindow.Dispatcher.Invoke(() =>
        {
            IsRecording = isRecording;
            _mainWindow.IsRecording = isRecording;
            _mainWindow.StatusText = isRecording ? "Запись..." : MainWindowConstants.Defaults.StatusText;
            _mainWindow.OnPropertyChanged(
                nameof(_mainWindow.IsRecording),
                nameof(_mainWindow.CanStartCapture),
                nameof(_mainWindow.StatusText));
        });

        private void DisposeComponents()
        {
            _cts?.Dispose();
            _capture?.Dispose();
            _analyzer?.Dispose();

            _cts = null;
            _capture = null;
            _analyzer = null;
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
        private readonly object _lock = new();
        private string _style = MainWindowConstants.Defaults.Style;
        private double _barSpacing = MainWindowConstants.Defaults.BarSpacing,
                       _barWidth = MainWindowConstants.Defaults.BarWidth;
        private int _barCount = MainWindowConstants.Defaults.BarCount;
        private string _statusText = MainWindowConstants.Defaults.StatusText;
        private bool _isOverlayActive, _isPopupOpen;
        private RenderStyle _selectedDrawingType;
        private OverlayWindow? _overlayWindow;

        internal SpectrumBrushes? _spectrumStyles;
        internal Renderer? _renderer;
        internal SpectrumAnalyzer? _analyzer;
        internal AudioCaptureManager? _captureManager;
        internal GainParameters _gainParameters;
        internal CompositeDisposable? _disposables;
        private DispatcherTimer? _renderTimer;
        
        public bool IsDarkTheme => ThemeManager.Instance.IsDarkTheme;
        public IEnumerable<RenderStyle> AvailableDrawingTypes => Enum.GetValues<RenderStyle>();
        public IReadOnlyDictionary<string, StyleDefinition> AvailableStyles => _spectrumStyles?.Styles ?? new Dictionary<string, StyleDefinition>();
        public SKElement? RenderElement { get; private set; }
        public bool CanStartCapture => _captureManager != null && !IsRecording;

        public MainWindow()
        {
            InitializeComponent();
            _gainParameters = new GainParameters(SynchronizationContext.Current);
            DataContext = this;
            InitializeComponents();
            SetupEventHandlers();
            ConfigureTheme();
            UpdateProperties();
        }

        private void InitializeComponents()
        {
            RenderElement = spectrumCanvas;
            _spectrumStyles = new SpectrumBrushes();
            _disposables = new CompositeDisposable();
            _renderTimer = new DispatcherTimer 
            { 
                Interval = TimeSpan.FromMilliseconds(MainWindowConstants.Defaults.RenderIntervalMs) 
            };

            _analyzer = new SpectrumAnalyzer(
                new FftProcessor(MainWindowConstants.Defaults.FftSize),
                new SpectrumConverter(_gainParameters),
                SynchronizationContext.Current);
            
            _captureManager = new AudioCaptureManager(this);
            _renderer = new Renderer(_spectrumStyles, this, _analyzer, RenderElement);
            SelectedStyle = MainWindowConstants.Defaults.Style;
        }

        private void SetupEventHandlers()
        {
            if (RenderElement == null) return;

            RenderElement.PaintSurface += OnPaintSurface;
            SizeChanged += OnWindowSizeChanged;
            StyleComboBox.SelectionChanged += OnStyleComboBoxSelectionChanged;
            RenderStyleComboBox.SelectionChanged += OnRenderStyleComboBoxSelectionChanged;
            Closed += OnWindowClosed;

            _renderTimer!.Tick += (_, _) => RenderElement.InvalidateVisual();
            _renderTimer.Start();
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

        private void UpdateProperties() =>
            OnPropertyChanged(nameof(IsRecording), nameof(CanStartCapture), nameof(StatusText));

        // Event Handlers
        private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e) =>
            _renderer?.UpdateRenderDimensions((int)e.NewSize.Width, (int)e.NewSize.Height);

        public void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e) =>
            _renderer?.RenderFrame(sender, e);

        private void OnStyleComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_renderer == null || SelectedStyle == null || _spectrumStyles == null) return;

            var (startColor, endColor, _) = _spectrumStyles.GetColorsAndBrush(SelectedStyle);
            _renderer.UpdateSpectrumStyle(SelectedStyle, startColor, endColor);
            InvalidateVisuals();
        }

        private void OnRenderStyleComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RenderStyleComboBox?.SelectedValue is RenderStyle renderStyle)
            {
                _selectedDrawingType = renderStyle;
                _renderer?.UpdateRenderStyle(renderStyle);
                InvalidateVisuals();
            }
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            if (IsOverlayActive)
                CloseOverlay();
                
            _renderer?.Dispose();
            _analyzer?.Dispose();
            _captureManager?.Dispose();
            _disposables?.Dispose();
        }

        public void OnOpenPopupButtonClick(object sender, RoutedEventArgs e) =>
            IsPopupOpen = !IsPopupOpen;

        public void OnOpenSettingsButtonClick(object sender, RoutedEventArgs e) =>
            new SettingsWindow().ShowDialog();

        public void OnSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (sender is not Slider slider) return;

            var value = slider.Value;
            var updates = new Dictionary<string, Action>
            {
                { "barWidthSlider", () => BarWidth = value },
                { "barSpacingSlider", () => BarSpacing = value },
                { "barCountSlider", () => BarCount = (int)value },
                { "minDbLevelSlider", () => MinDbLevel = (float)value },
                { "maxDbLevelSlider", () => MaxDbLevel = (float)value },
                { "adaptionRateSlider", () => AmplificationFactor = (float)value }
            };

            if (updates.TryGetValue(slider.Name, out var action))
            {
                action();
                InvalidateVisuals();
            }
        }

        private void OnButtonClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                switch (button.Name)
                {
                    case "StartCaptureButton":
                        _ = StartCaptureAsync();
                        break;
                    case "StopCaptureButton":
                        _ = StopCaptureAsync();
                        break;
                }
            }
        }

        public void OnOverlayButtonClick(object sender, RoutedEventArgs e)
        {
            if (IsOverlayActive)
                CloseOverlay();
            else
                OpenOverlay();
        }

        private void OnThemeToggleButtonChanged(object sender, RoutedEventArgs e) =>
            ThemeManager.Instance.ToggleTheme();

        // Overlay Management
        private void OpenOverlay()
        {
            _overlayWindow = new OverlayWindow(this, new OverlayConfiguration
            {
                RenderInterval = MainWindowConstants.Defaults.RenderIntervalMs,
                IsTopmost = true,
                ShowInTaskbar = false
            });

            _overlayWindow.Closed += (_, _) => OnOverlayClosed();
            _overlayWindow.Show();
            IsOverlayActive = true;

            UpdateRendererDimensions(
                (int)SystemParameters.PrimaryScreenWidth,
                (int)SystemParameters.PrimaryScreenHeight
            );
        }

        private void OnOverlayClosed()
        {
            IsOverlayActive = false;
            UpdateRendererDimensions(
                (int)RenderElement?.ActualWidth,
                (int)RenderElement?.ActualHeight
            );
        }

        public void CloseOverlay()
        {
            _overlayWindow?.Close();
            _overlayWindow?.Dispose();
            _overlayWindow = null;
            IsOverlayActive = false;
        }

        private void UpdateRendererDimensions(int? width, int? height)
        {
            if (width.HasValue && height.HasValue)
                _renderer?.UpdateRenderDimensions(width.Value, height.Value);
        }

        // Properties
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

        public double BarWidth
        {
            get => _barWidth;
            set => SetField(ref _barWidth, value, InvalidateVisuals);
        }

        public double BarSpacing
        {
            get => _barSpacing;
            set => SetField(ref _barSpacing, value, InvalidateVisuals);
        }

        public int BarCount
        {
            get => _barCount;
            set => SetField(ref _barCount, value, InvalidateVisuals);
        }

        public RenderStyle SelectedDrawingType
        {
            get => _selectedDrawingType;
            set => SetField(ref _selectedDrawingType, value, () =>
            {
                _renderer?.UpdateRenderStyle(_selectedDrawingType);
                InvalidateVisuals();
            });
        }

        public string SelectedStyle
        {
            get => _style;
            set => SetField(ref _style, value, () =>
            {
                var (sC, eC, _) = _spectrumStyles?.GetColorsAndBrush(SelectedStyle) ?? default;
                _renderer?.UpdateSpectrumStyle(SelectedStyle, sC, eC);
                InvalidateVisuals();
            });
        }

        public string StatusText
        {
            get => _statusText;
            set => SetField(ref _statusText, value);
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

        // Helper Methods
        private void UpdateGainParameter(float newValue, Action<float> setter, string propertyName)
        {
            setter(newValue);
            OnPropertyChanged(propertyName);
            InvalidateVisuals();
        }

        private void InvalidateVisuals() => RenderElement?.InvalidateVisual();

        public async Task StartCaptureAsync()
        {
            if (_captureManager != null)
                await _captureManager.StartCaptureAsync();
        }

        public async Task StopCaptureAsync()
        {
            if (_captureManager != null)
                await _captureManager.StopCaptureAsync();
        }

        // Property Changed Implementation
        private bool SetField<T>(ref T field, T value, Action? callback = null, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            
            field = value;
            OnPropertyChanged(propertyName ?? string.Empty);
            callback?.Invoke();
            return true;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        
        public void OnPropertyChanged(params string[] properties)
        {
            foreach (var prop in properties)
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }
    }
}