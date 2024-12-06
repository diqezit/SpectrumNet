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
        public const string ReadyStatus = "Ready";
        public const string RecordingStatus = "Record...";
    }

    public sealed class AudioCaptureManager : IDisposable
    {
        private readonly MainWindow _mainWindow;
        private readonly object _lock = new();
        private record CaptureState(SpectrumAnalyzer Analyzer, WasapiLoopbackCapture Capture, CancellationTokenSource Cts);

        private CaptureState? _state;
        private bool _isDisposed;

        public bool IsRecording { get; private set; }

        #region Constructor
        public AudioCaptureManager(MainWindow mainWindow) => _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
        #endregion

        #region Public Methods
        public async Task StartCaptureAsync()
        {
            lock (_lock)
            {
                if (_isDisposed) throw new ObjectDisposedException(nameof(AudioCaptureManager));
                InitializeCapture();
            }

            await MonitorCaptureAsync(_state!.Cts.Token);
        }

        public Task StopCaptureAsync()
        {
            lock (_lock)
            {
                _state?.Cts.Cancel();
                _state?.Capture.StopRecording();
                DisposeState();
            }
            UpdateStatus(false);
            return Task.CompletedTask;
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
        #endregion

        #region Private Methods
        private void InitializeCapture()
        {
            DisposeState();
            var capture = new WasapiLoopbackCapture();
            var analyzer = InitializeAnalyzer();

            _state = new CaptureState(analyzer, capture, new CancellationTokenSource());
            _state.Capture.DataAvailable += OnDataAvailable;
            UpdateStatus(true);
            _state.Capture.StartRecording();
        }

        private SpectrumAnalyzer InitializeAnalyzer() => _mainWindow.Dispatcher.Invoke(() =>
        {
            var analyzer = new SpectrumAnalyzer(new FftProcessor(), new SpectrumConverter(_mainWindow._gainParameters), SynchronizationContext.Current);
            if (_mainWindow.RenderElement is { ActualWidth: > 0, ActualHeight: > 0 })
            {
                _mainWindow._renderer?.Dispose();
                _mainWindow._renderer = new Renderer(_mainWindow._spectrumStyles ?? new SpectrumBrushes(), _mainWindow, analyzer, _mainWindow.RenderElement);
            }
            return analyzer;
        });

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded <= 0 || _state?.Analyzer == null) return;

            var samples = new float[e.BytesRecorded / 4];
            try { Buffer.BlockCopy(e.Buffer, 0, samples, 0, e.BytesRecorded); } catch { }
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
        }

        private void UpdateStatus(bool isRecording) => _mainWindow.Dispatcher.Invoke(() =>
        {
            IsRecording = isRecording;
            _mainWindow.IsRecording = isRecording;
            _mainWindow.StatusText = isRecording ? MwConstants.RecordingStatus : MwConstants.ReadyStatus;
            _mainWindow.OnPropertyChanged(nameof(_mainWindow.IsRecording), nameof(_mainWindow.CanStartCapture), nameof(_mainWindow.StatusText));
        });

        private void DisposeState()
        {
            _state?.Cts.Dispose();
            _state?.Capture.Dispose();
            _state?.Analyzer.Dispose();
            _state = null;
        }
        #endregion
    }

    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        #region Private Fields
        private record MainWindowSettings(
            string Style = MwConstants.DefaultStyle,
            double BarSpacing = MwConstants.BarSpacing,
            int BarCount = MwConstants.BarCount,
            string StatusText = MwConstants.ReadyStatus);

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
        #endregion

        #region Public Properties
        public static bool IsDarkTheme => ThemeManager.Instance.IsDarkTheme;
        public static IEnumerable<RenderStyle> AvailableDrawingTypes => Enum.GetValues<RenderStyle>()
            .Cast<RenderStyle>()
            .OrderBy(style => style.ToString());

        public IReadOnlyDictionary<string, StyleDefinition> AvailableStyles => _spectrumStyles!.Styles
            .OrderBy(kvp => kvp.Key)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        public static IEnumerable<FftWindowType> AvailableFftWindowTypes => Enum.GetValues(typeof(FftWindowType))
            .Cast<FftWindowType>()
            .OrderBy(windowType => windowType.ToString());

        public SKElement? RenderElement { get; private set; }
        public bool CanStartCapture => _captureManager is not null && !IsRecording;
        #endregion

        #region Constructor
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
        #endregion

        #region Initialization Methods
        private void InitializeComponents()
        {
            if (spectrumCanvas != null)
            {
                RenderElement = spectrumCanvas;
            }

            _spectrumStyles = new SpectrumBrushes();
            _disposables = new CompositeDisposable();

            var renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(MwConstants.RenderIntervalMs) };
            renderTimer.Tick += (_, _) => RenderElement?.InvalidateVisual();
            renderTimer.Start();

            if (_gainParameters != null)
            {
                _analyzer = new SpectrumAnalyzer(new FftProcessor(), new SpectrumConverter(_gainParameters), SynchronizationContext.Current);
            }

            if (this != null)
            {
                _captureManager = new AudioCaptureManager(this);
            }

            if (_spectrumStyles != null && RenderElement != null)
            {
                _renderer = new Renderer(_spectrumStyles, this, _analyzer ?? throw new ArgumentNullException(nameof(_analyzer)), RenderElement);
            }

            SelectedStyle = MwConstants.DefaultStyle;
        }

        private void SetupEventHandlers()
        {
            RenderElement!.PaintSurface += OnPaintSurface;
            SizeChanged += OnWindowSizeChanged;
            StateChanged += OnStateChanged;
            MouseDoubleClick += OnWindowMouseDoubleClick;
            Closed += OnWindowClosed;

            RegisterComboBoxHandler(StyleComboBox);
            RegisterComboBoxHandler(RenderStyleComboBox);
        }

        private void RegisterComboBoxHandler(ComboBox? comboBox)
        {
            if (comboBox != null)
                comboBox.SelectionChanged += OnComboBoxSelectionChanged;
        }
        #endregion

        #region Event Handlers
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

        private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e) => _renderer?.UpdateRenderDimensions((int)e.NewSize.Width, (int)e.NewSize.Height);

        public void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e) => _renderer?.RenderFrame(sender, e);

        private void OnComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            switch (sender)
            {
                case ComboBox { Name: nameof(StyleComboBox) } when SelectedStyle != null:
                    var (startColor, endColor, _) = _spectrumStyles.GetColorsAndBrush(SelectedStyle);
                    _renderer.UpdateSpectrumStyle(SelectedStyle, startColor, endColor);
                    break;

                case ComboBox { Name: nameof(RenderStyleComboBox) } when RenderStyleComboBox.SelectedValue is RenderStyle renderStyle:
                    _selectedDrawingType = renderStyle;
                    _renderer?.UpdateRenderStyle(renderStyle);
                    break;

                case ComboBox { Name: nameof(FftWindowTypeComboBox) } when FftWindowTypeComboBox.SelectedValue is FftWindowType windowType:
                    SelectedFftWindowType = windowType;
                    break;
            }
            InvalidateVisuals();
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            if (IsOverlayActive) CloseOverlay();
            _renderer?.Dispose();
            _analyzer?.Dispose();
            _captureManager?.Dispose();
            _disposables?.Dispose();
        }

        private void CloseWindow() => this.Close();

        private void OnOpenPopupButtonClick(object sender, RoutedEventArgs e) => IsPopupOpen = !IsPopupOpen;

        private void OnOpenSettingsButtonClick(object sender, RoutedEventArgs e) => new SettingsWindow().ShowDialog();

        private void OnWindowDrag(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void MinimizeWindow() => this.WindowState = WindowState.Minimized;

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

        private void ToggleWindowState() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void OnWindowMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                e.Handled = true;
                ToggleWindowState();
            }
        }

        private void OnSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (sender is not Slider slider) return;

            var sliderActions = new Dictionary<string, Action<double>>
            {
                ["barSpacingSlider"] = value => BarSpacing = value,
                ["barCountSlider"] = value => BarCount = (int)value,
                ["minDbLevelSlider"] = value => MinDbLevel = (float)value,
                ["maxDbLevelSlider"] = value => MaxDbLevel = (float)value,
                ["adaptionRateSlider"] = value => AmplificationFactor = (float)value
            };

            if (sliderActions.TryGetValue(slider.Name, out var action))
            {
                action(slider.Value);
                InvalidateVisuals();
            }
        }

        private async void OnButtonClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button) return;

            var actions = new Dictionary<string, Func<Task>>
        {
            { "StartCaptureButton", StartCaptureAsync },
            { "StopCaptureButton", StopCaptureAsync },
            { "OverlayButton", () => { OnOverlayButtonClick(sender, e); return Task.CompletedTask; } },
            { "OpenSettingsButton", () => { OnOpenSettingsButtonClick(sender, e); return Task.CompletedTask; } },
            { "OpenPopupButton", () => { OnOpenPopupButtonClick(sender, e); return Task.CompletedTask; } },
            { "MinimizeButton", () => { MinimizeWindow(); return Task.CompletedTask; } },
            { "MaximizeButton", () => { MaximizeWindow(); return Task.CompletedTask; } },
            { "CloseButton", () => { CloseWindow(); return Task.CompletedTask; } }
        };

            if (actions.TryGetValue(button.Name, out var action))
            {
                await action();
            }
        }

        private void OnOverlayButtonClick(object sender, RoutedEventArgs e) =>
            (IsOverlayActive ? (Action)CloseOverlay : OpenOverlay)();

        private void OnThemeToggleButtonChanged(object sender, RoutedEventArgs e) =>
            ThemeManager.Instance.ToggleTheme();

        private void OpenOverlay()
        {
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
            }

            UpdateRendererDimensions((int)SystemParameters.PrimaryScreenWidth, (int)SystemParameters.PrimaryScreenHeight);
        }

        private void OnOverlayClosed() => IsOverlayActive = false;

        public void CloseOverlay()
        {
            if (_overlayWindow != null)
            {
                _overlayWindow.Close();
                _overlayWindow.Dispose();
                _overlayWindow = null;
            }

            IsOverlayActive = false;
        }

        private void UpdateRendererDimensions(int? width, int? height) => _renderer?.UpdateRenderDimensions(width ?? 0, height ?? 0);
        #endregion

        #region Property Handlers
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
        #endregion

        #region Helper Methods
        private void UpdateGainParameter(float newValue, Action<float> setter, string propertyName)
        {
            setter(newValue);
            OnPropertyChanged(propertyName);
            InvalidateVisuals();
        }

        private void UpdateState(Func<MainWindowSettings, MainWindowSettings> updater, string propertyName, Action? callback = null)
        {
            _state = updater(_state);
            OnPropertyChanged(propertyName);
            callback?.Invoke();
        }

        private void InvalidateVisuals() => RenderElement?.InvalidateVisual();

        public Task StartCaptureAsync()
        {
            if (_captureManager != null)
            {
                return _captureManager.StartCaptureAsync();
            }
            return Task.CompletedTask;
        }

        public Task StopCaptureAsync()
        {
            if (_captureManager != null)
            {
                return _captureManager.StopCaptureAsync();
            }
            return Task.CompletedTask;
        }
        #endregion

        #region INotifyPropertyChanged Implementation
        public event PropertyChangedEventHandler? PropertyChanged;

        public virtual void OnPropertyChanged(params string[] propertyNames)
        {
            foreach (var name in propertyNames)
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        protected bool SetField<T>(ref T field, T value, Action? callback = null, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName ?? string.Empty);
            callback?.Invoke();
            return true;
        }
        #endregion
    }
}