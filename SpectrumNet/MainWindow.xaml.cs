using System.Windows.Media;
using System.Windows.Media.Animation;

#nullable enable

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
        public const string RecordingStatus = "Recording...";
    }

    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        #region Private Fields and Records
        private record MainWindowSettings(
            string Style = MwConstants.DefaultStyle,
            double BarSpacing = MwConstants.BarSpacing,
            int BarCount = MwConstants.BarCount,
            string StatusText = MwConstants.ReadyStatus);

        private MainWindowSettings _state = new();
        private bool _isOverlayActive, _isPopupOpen;
        private RenderStyle _selectedDrawingType = RenderStyle.Bars;
        private FftWindowType _selectedFftWindowType = FftWindowType.Hann;
        private OverlayWindow? _overlayWindow;

        internal SpectrumBrushes? _spectrumStyles;
        internal Renderer? _renderer;
        internal SpectrumAnalyzer? _analyzer;
        internal AudioCaptureManager? _captureManager;
        internal GainParameters _gainParameters;
        internal CompositeDisposable? _disposables;
        #endregion

        #region Properties
        public static bool IsDarkTheme => ThemeManager.Instance.IsDarkTheme;

        public static IEnumerable<RenderStyle> AvailableDrawingTypes =>
            Enum.GetValues<RenderStyle>().OrderBy(s => s.ToString());

        public IReadOnlyDictionary<string, StyleDefinition> AvailableStyles =>
            _spectrumStyles!.Styles.OrderBy(kvp => kvp.Key)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        public static IEnumerable<FftWindowType> AvailableFftWindowTypes =>
            Enum.GetValues<FftWindowType>().OrderBy(wt => wt.ToString());

        public SKElement? RenderElement { get; private set; }

        public bool CanStartCapture => _captureManager is not null && !IsRecording;

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
                if (_spectrumStyles != null)
                {
                    var (startColor, endColor, _) = _spectrumStyles.GetColorsAndBrush(value);
                    _renderer?.UpdateSpectrumStyle(value, startColor, endColor);
                }
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
        #endregion

        #region Constructor and Initialization
        public MainWindow()
        {
            InitializeComponent();

            _gainParameters = new GainParameters(
                SynchronizationContext.Current,
                SharedConstants.DefaultMinDb,
                SharedConstants.DefaultMaxDb,
                SharedConstants.DefaultAmplificationFactor);

            DataContext = this;

            InitComponents();
            InitEventHandlers();
            ConfigureTheme();
            UpdateProps();

            CompositionTarget.Rendering += OnRendering;
        }

        private void InitComponents()
        {
            RenderElement = spectrumCanvas ?? throw new InvalidOperationException("spectrumCanvas is null");
            _spectrumStyles = new SpectrumBrushes();
            _disposables = new CompositeDisposable();

            var syncContext = SynchronizationContext.Current ??
                              throw new InvalidOperationException("SynchronizationContext.Current is null");

            _analyzer = new SpectrumAnalyzer(
                new FftProcessor { WindowType = SelectedFftWindowType },
                new SpectrumConverter(_gainParameters),
                syncContext);

            _captureManager = new AudioCaptureManager(this);
            _renderer = new Renderer(_spectrumStyles, this, _analyzer, RenderElement);

            SelectedStyle = MwConstants.DefaultStyle;
            SelectedDrawingType = RenderStyle.Bars;
        }

        private void InitEventHandlers()
        {
            if (RenderElement is null)
                throw new InvalidOperationException("RenderElement is null");

            RenderElement.PaintSurface += OnPaintSurface;
            SizeChanged += OnWindowSizeChanged;
            StateChanged += OnStateChanged;
            MouseDoubleClick += OnWindowMouseDoubleClick;
            Closed += OnWindowClosed;

            PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(IsRecording))
                {
                    StatusText = IsRecording ? MwConstants.RecordingStatus : MwConstants.ReadyStatus;
                }
            };
        }

        private void ConfigureTheme()
        {
            var tm = ThemeManager.Instance;
            tm.RegisterWindow(this);
            tm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ThemeManager.IsDarkTheme))
                    OnPropertyChanged(nameof(IsDarkTheme));
            };
        }
        #endregion

        #region Event Handlers
        private void OnRendering(object sender, EventArgs e) => RenderElement?.InvalidateVisual();

        private void OnStateChanged(object? sender, EventArgs e)
        {
            if (MaximizeButton is null || MaximizeIcon is null) return;
            MaximizeIcon.Data = Geometry.Parse(WindowState == WindowState.Maximized
                ? "M0,0 L20,0 L20,20 L0,20 Z"
                : "M2,2 H18 V18 H2 Z");
        }

        private void OnThemeToggleButtonChanged(object sender, RoutedEventArgs e) =>
            ThemeManager.Instance.ToggleTheme();

        private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs e) =>
            _renderer?.UpdateRenderDimensions((int)e.NewSize.Width, (int)e.NewSize.Height);

        public void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e) =>
            _renderer?.RenderFrame(sender, e);

        private void OnComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ComboBox cb) return;
            switch (cb.Name)
            {
                case nameof(StyleComboBox):
                    if (SelectedStyle != null && _spectrumStyles != null)
                    {
                        var (startColor, endColor, _) = _spectrumStyles.GetColorsAndBrush(SelectedStyle);
                        _renderer?.UpdateSpectrumStyle(SelectedStyle, startColor, endColor);
                    }
                    break;
                case nameof(RenderStyleComboBox):
                    if (RenderStyleComboBox?.SelectedValue is RenderStyle rs)
                    {
                        _selectedDrawingType = rs;
                        _renderer?.UpdateRenderStyle(rs);
                    }
                    break;
                case nameof(FftWindowTypeComboBox):
                    if (FftWindowTypeComboBox?.SelectedValue is FftWindowType wt)
                        SelectedFftWindowType = wt;
                    break;
            }
            InvalidateVisuals();
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            if (IsOverlayActive) CloseOverlay();

            CompositionTarget.Rendering -= OnRendering;
            RenderElement!.PaintSurface -= OnPaintSurface;
            SizeChanged -= OnWindowSizeChanged;
            StateChanged -= OnStateChanged;
            MouseDoubleClick -= OnWindowMouseDoubleClick;

            _renderer?.Dispose();
            _analyzer?.Dispose();
            _captureManager?.Dispose();
            _disposables?.Dispose();
        }

        private void OnWindowDrag(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        private void OnWindowMouseDoubleClick(object? sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                e.Handled = true;
                MaximizeWindow();
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
                ["amplificationFactorSlider"] = value => AmplificationFactor = (float)value
            };
            if (sliderActions.TryGetValue(slider.Name, out var act))
            {
                act(slider.Value);
                InvalidateVisuals();
            }
        }

        private void OnButtonClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            var actions = new Dictionary<string, Action>
            {
                ["StartCaptureButton"] = async () => await StartCaptureAsync(),
                ["StopCaptureButton"] = async () => await StopCaptureAsync(),
                ["OverlayButton"] = () => OnOverlayButtonClick(sender, e),
                ["OpenSettingsButton"] = () => Dispatcher.Invoke(() => new SettingsWindow().ShowDialog()),
                ["OpenPopupButton"] = () => IsPopupOpen = !IsPopupOpen,
                ["MinimizeButton"] = () => Dispatcher.Invoke(MinimizeWindow),
                ["MaximizeButton"] = () => Dispatcher.Invoke(MaximizeWindow),
                ["CloseButton"] = () => Dispatcher.Invoke(CloseWindow)
            };
            if (actions.TryGetValue(btn.Name, out var act))
                act();
        }

        private void ToggleControlPanelButton_Click(object sender, RoutedEventArgs e)
        {
            if (ControlPanel.Visibility == Visibility.Visible)
            {
                var hidePanelSB = (Storyboard)FindResource("HidePanelAnimation");
                hidePanelSB.Completed += (s, ev) => ControlPanel.Visibility = Visibility.Collapsed;
                hidePanelSB.Begin();
            }
            else
            {
                ControlPanel.Visibility = Visibility.Visible;
                var showPanelSB = (Storyboard)FindResource("ShowPanelAnimation");
                showPanelSB.Begin();
            }
        }

        private void CloseWindow() => Close();
        private void MinimizeWindow() => WindowState = WindowState.Minimized;
        private void MaximizeWindow() =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void OnOverlayButtonClick(object sender, RoutedEventArgs e)
        {
            if (IsOverlayActive)
                CloseOverlay();
            else
                Dispatcher.Invoke(OpenOverlay);
        }

        private void OnOverlayClosed() => IsOverlayActive = false;

        private void OpenOverlay()
        {
            if (_overlayWindow != null) return;

            _overlayWindow = new OverlayWindow(this, new OverlayConfiguration
            {
                RenderInterval = MwConstants.RenderIntervalMs,
                IsTopmost = true,
                ShowInTaskbar = false
            });

            _overlayWindow.Closed += (_, _) => OnOverlayClosed();
            _overlayWindow.Show();
            IsOverlayActive = true;

            UpdateRendererDimensions(
                (int)SystemParameters.PrimaryScreenWidth,
                (int)SystemParameters.PrimaryScreenHeight);
        }

        private void CloseOverlay()
        {
            if (_overlayWindow == null) return;

            _overlayWindow.Close();
            _overlayWindow.Dispose();
            _overlayWindow = null;
            IsOverlayActive = false;
        }

        private void UpdateRendererDimensions(int width, int height) =>
            _renderer?.UpdateRenderDimensions(width, height);

        private void UpdateFftWindowType(FftWindowType windowType)
        {
            if (_analyzer?.FftProcessor != null)
                _analyzer.FftProcessor.WindowType = windowType;
        }

        private void UpdateProps() =>
            OnPropertyChanged(nameof(IsRecording), nameof(CanStartCapture), nameof(StatusText));

        private void UpdateGainParameter(float newValue, Action<float> setter, string propertyName)
        {
            setter(newValue);
            OnPropertyChanged(propertyName);
            InvalidateVisuals();
        }

        private void InvalidateVisuals() => RenderElement?.InvalidateVisual();

        public Task StartCaptureAsync()
        {
            var task = _captureManager?.StartCaptureAsync() ?? Task.CompletedTask;
            UpdateProps();
            return task;
        }

        public Task StopCaptureAsync()
        {
            var task = _captureManager?.StopCaptureAsync() ?? Task.CompletedTask;
            UpdateProps();
            return task;
        }

        private void UpdateState(Func<MainWindowSettings, MainWindowSettings> updater, string propertyName, Action? callback = null)
        {
            _state = updater(_state);
            OnPropertyChanged(propertyName);
            callback?.Invoke();
        }

        protected bool SetField<T>(ref T field, T value, Action? callback = null, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName ?? string.Empty);
            callback?.Invoke();
            return true;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public virtual void OnPropertyChanged(params string[] propertyNames)
        {
            foreach (var name in propertyNames)
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
    #endregion
}