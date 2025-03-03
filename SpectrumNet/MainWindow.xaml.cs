using System.Windows.Media;
using System.Windows.Media.Animation;

#nullable enable

namespace SpectrumNet;

public static class MwConstants
{
    public const int RenderIntervalMs = 16,
                     MonitorDelay = 16,
                     BarCount = 60;
    public const double BarSpacing = 4;
    public const string DefaultStyle = "Gradient",
                        ReadyStatus = "Ready",
                        RecordingStatus = "Recording...";
}

public partial class MainWindow : Window, INotifyPropertyChanged
{
    const string LogPrefix = "[MainWindow] ";
    record MainWindowSettings(
        string Style = MwConstants.DefaultStyle,
        double BarSpacing = MwConstants.BarSpacing,
        int BarCount = MwConstants.BarCount,
        string StatusText = MwConstants.ReadyStatus
    );

    MainWindowSettings _state = new();
    bool _isOverlayActive, _isPopupOpen,
         _isOverlayTopmost = true,
         _isControlPanelVisible = true,
         _isTransitioning;
    RenderStyle _selectedDrawingType = RenderStyle.Bars;
    FftWindowType _selectedFftWindowType = FftWindowType.Hann;
    SpectrumScale _selectedScaleType = SpectrumScale.Linear;
    OverlayWindow? _overlayWindow;
    internal SpectrumBrushes? _spectrumStyles;
    internal Renderer? _renderer;
    internal SpectrumAnalyzer? _analyzer;
    internal AudioCaptureManager? _captureManager;
    internal GainParameters _gainParameters;
    internal CompositeDisposable? _disposables;

    public static bool IsDarkTheme => ThemeManager.Instance.IsDarkTheme;
    public static IEnumerable<RenderStyle> AvailableDrawingTypes =>
        Enum.GetValues<RenderStyle>().OrderBy(s => s.ToString());
    public static IEnumerable<FftWindowType> AvailableFftWindowTypes =>
        Enum.GetValues<FftWindowType>().OrderBy(wt => wt.ToString());
    public static IEnumerable<SpectrumScale> AvailableScaleTypes =>
        Enum.GetValues<SpectrumScale>().OrderBy(s => s.ToString());
    public IReadOnlyDictionary<string, StyleDefinition> AvailableStyles =>
        _spectrumStyles!.Styles
            .OrderBy(kvp => kvp.Key)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    public SKElement? RenderElement { get; private set; }
    public bool CanStartCapture => _captureManager is not null && !IsRecording;
    public bool IsPopupOpen
    {
        get => _isPopupOpen;
        set => SetField(ref _isPopupOpen, value);
    }
    public bool IsOverlayTopmost
    {
        get => _isOverlayTopmost;
        set => SetField(ref _isOverlayTopmost, value, UpdateOverlayTopmostState);
    }
    public bool IsOverlayActive
    {
        get => _isOverlayActive;
        set
        {
            if (_isOverlayActive == value)
                return;
            _isOverlayActive = value;
            OnPropertyChanged(nameof(IsOverlayActive));
            SpectrumRendererFactory.ConfigureAllRenderers(value);
            spectrumCanvas?.InvalidateVisual();
        }
    }
    public bool IsControlPanelVisible
    {
        get => _isControlPanelVisible;
        set
        {
            _isControlPanelVisible = value;
            OnPropertyChanged(nameof(IsControlPanelVisible));
            UpdateToggleButtonContent();
        }
    }
    public bool IsRecording
    {
        get => _captureManager?.IsRecording ?? false;
        set
        {
            if (_captureManager?.IsRecording == value)
                return;
            _ = value ? StartCaptureAsync() : StopCaptureAsync();
            OnPropertyChanged(nameof(IsRecording), nameof(CanStartCapture));
        }
    }
    public bool IsTransitioning
    {
        get => _isTransitioning;
        private set
        {
            if (_isTransitioning != value)
            {
                _isTransitioning = value;
                SmartLogger.IsAnyTransitionActive = value;
            }
        }
    }
    public double BarSpacing
    {
        get => _state.BarSpacing;
        set => UpdateState(
            s => s with { BarSpacing = value },
            nameof(BarSpacing),
            InvalidateVisuals
        );
    }
    public int BarCount
    {
        get => _state.BarCount;
        set => UpdateState(
            s => s with { BarCount = value },
            nameof(BarCount),
            InvalidateVisuals
        );
    }
    public RenderStyle SelectedDrawingType
    {
        get => _selectedDrawingType;
        set => SetField(
            ref _selectedDrawingType,
            value,
            () => _renderer?.UpdateRenderStyle(value)
        );
    }
    public SpectrumScale SelectedScaleType
    {
        get => _selectedScaleType;
        set
        {
            if (_selectedScaleType == value)
                return;
            _selectedScaleType = value;
            OnPropertyChanged(nameof(SelectedScaleType));
            UpdateSpectrumScale(value);
        }
    }
    public string SelectedStyle
    {
        get => _state.Style;
        set => UpdateState(
            s => s with { Style = value },
            nameof(SelectedStyle),
            () =>
            {
                if (_spectrumStyles != null)
                {
                    var (startColor, endColor, _) = _spectrumStyles.GetColorsAndBrush(value);
                    _renderer?.UpdateSpectrumStyle(value, startColor, endColor);
                }
                InvalidateVisuals();
            }
        );
    }
    public string StatusText
    {
        get => _state.StatusText;
        set => UpdateState(
            s => s with { StatusText = value },
            nameof(StatusText)
        );
    }
    public float MinDbLevel
    {
        get => _gainParameters.MinDbValue;
        set => UpdateGainParameter(
            value,
            v => _gainParameters.MinDbValue = v,
            nameof(MinDbLevel)
        );
    }
    public float MaxDbLevel
    {
        get => _gainParameters.MaxDbValue;
        set => UpdateGainParameter(
            value,
            v => _gainParameters.MaxDbValue = v,
            nameof(MaxDbLevel)
        );
    }
    public float AmplificationFactor
    {
        get => _gainParameters.AmplificationFactor;
        set => UpdateGainParameter(
            value,
            v => _gainParameters.AmplificationFactor = v,
            nameof(AmplificationFactor)
        );
    }
    public FftWindowType SelectedFftWindowType
    {
        get => _selectedFftWindowType;
        set
        {
            if (_selectedFftWindowType == value)
                return;
            _selectedFftWindowType = value;
            OnPropertyChanged(nameof(SelectedFftWindowType));
            UpdateFftWindowType(value);
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindow()
    {
        try
        {
            InitializeComponent();
            _gainParameters = new GainParameters(
                SynchronizationContext.Current,
                SharedConstants.DefaultMinDb,
                SharedConstants.DefaultMaxDb,
                SharedConstants.DefaultAmplificationFactor
            );
            DataContext = this;
            InitComponents();
            InitEventHandlers();
            ConfigureTheme();
            UpdateProps();
            CompositionTarget.Rendering += OnRendering;
            UpdateToggleButtonContent();
        }
        catch (Exception ex)
        {
            SmartLogger.Log(
                LogLevel.Error,
                LogPrefix,
                $"Failed to initialize main window: {ex}"
            );
            throw;
        }
    }

    void InitComponents()
    {
        try
        {
            RenderElement = spectrumCanvas
                ?? throw new InvalidOperationException("spectrumCanvas is null");
            _spectrumStyles = new SpectrumBrushes();
            _disposables = new CompositeDisposable();
            var syncContext = SynchronizationContext.Current
                ?? throw new InvalidOperationException("SynchronizationContext.Current is null");
            _analyzer = new SpectrumAnalyzer(
                new FftProcessor { WindowType = SelectedFftWindowType },
                new SpectrumConverter(_gainParameters),
                syncContext
            );
            _analyzer.ScaleType = SelectedScaleType;
            _captureManager = new AudioCaptureManager(this);
            _renderer = new Renderer(_spectrumStyles, this, _analyzer, RenderElement);
            SelectedStyle = MwConstants.DefaultStyle;
            SelectedDrawingType = RenderStyle.Bars;
        }
        catch (Exception ex)
        {
            SmartLogger.Log(
                LogLevel.Error,
                LogPrefix,
                $"Failed to initialize components: {ex}"
            );
            throw;
        }
    }

    void InitEventHandlers()
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
                StatusText = IsRecording ? MwConstants.RecordingStatus : MwConstants.ReadyStatus;
        };
    }

    void ConfigureTheme()
    {
        var tm = ThemeManager.Instance;
        tm.RegisterWindow(this);
        tm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ThemeManager.IsDarkTheme))
                OnPropertyChanged(nameof(IsDarkTheme));
        };
    }

    public Task StartCaptureAsync()
    {
        try
        {
            var task = _captureManager?.StartCaptureAsync() ?? Task.CompletedTask;
            UpdateProps();
            return task;
        }
        catch (Exception ex)
        {
            SmartLogger.Log(
                LogLevel.Error,
                LogPrefix,
                $"Failed to start audio capture: {ex}"
            );
            return Task.CompletedTask;
        }
    }

    public Task StopCaptureAsync()
    {
        try
        {
            var task = _captureManager?.StopCaptureAsync() ?? Task.CompletedTask;
            UpdateProps();
            return task;
        }
        catch (Exception ex)
        {
            SmartLogger.Log(
                LogLevel.Error,
                LogPrefix,
                $"Failed to stop audio capture: {ex}"
            );
            return Task.CompletedTask;
        }
    }

    void OpenOverlay()
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
                        RenderInterval: MwConstants.RenderIntervalMs,
                        IsTopmost: IsOverlayTopmost,
                        ShowInTaskbar: false,
                        EnableHardwareAcceleration: true
                    )
                );
                _overlayWindow.Closed += (_, _) => OnOverlayClosed();
                _overlayWindow.Show();
            }
            IsOverlayActive = true;
            SpectrumRendererFactory.ConfigureAllRenderers(true);
            spectrumCanvas?.InvalidateVisual();
            UpdateRendererDimensions(
                (int)SystemParameters.PrimaryScreenWidth,
                (int)SystemParameters.PrimaryScreenHeight
            );
        }
        catch (Exception ex)
        {
            SmartLogger.Log(
                LogLevel.Error,
                LogPrefix,
                $"Failed to open overlay window: {ex}"
            );
        }
    }

    void CloseOverlay()
    {
        if (_overlayWindow == null)
            return;
        _overlayWindow.Close();
    }

    void OnOverlayClosed()
    {
        try
        {
            _overlayWindow?.Dispose();
            _overlayWindow = null;
            IsOverlayActive = false;
            SpectrumRendererFactory.ConfigureAllRenderers(false);
            spectrumCanvas?.InvalidateVisual();
            Activate();
        }
        catch (Exception ex)
        {
            SmartLogger.Log(
                LogLevel.Error,
                LogPrefix,
                $"Error closing overlay: {ex}"
            );
        }
    }

    public void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        if (IsOverlayActive && sender == spectrumCanvas)
        {
            e.Surface.Canvas.Clear(SKColors.Transparent);
            return;
        }
        _renderer?.RenderFrame(sender, e);
    }

    void OnRendering(object? sender, EventArgs e) =>
        RenderElement?.InvalidateVisual();

    void OnStateChanged(object? sender, EventArgs e)
    {
        if (MaximizeButton is null || MaximizeIcon is null)
            return;
        MaximizeIcon.Data = Geometry.Parse(
            WindowState == WindowState.Maximized
                ? "M0,0 L20,0 L20,20 L0,20 Z"
                : "M2,2 H18 V18 H2 Z"
        );
    }

    void OnThemeToggleButtonChanged(object sender, RoutedEventArgs e) =>
        ThemeManager.Instance.ToggleTheme();

    void OnWindowSizeChanged(object? sender, SizeChangedEventArgs e) =>
        _renderer?.UpdateRenderDimensions(
            (int)e.NewSize.Width,
            (int)e.NewSize.Height
        );

    void OnComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox cb)
            return;
        try
        {
            switch (cb.Name)
            {
                case nameof(StyleComboBox)
                    when SelectedStyle != null && _spectrumStyles != null:
                    {
                        var (startColor, endColor, _) =
                            _spectrumStyles.GetColorsAndBrush(SelectedStyle);
                        _renderer?.UpdateSpectrumStyle(
                            SelectedStyle,
                            startColor,
                            endColor
                        );
                        break;
                    }
                case nameof(RenderStyleComboBox)
                    when RenderStyleComboBox?.SelectedValue is RenderStyle rs:
                    _selectedDrawingType = rs;
                    _renderer?.UpdateRenderStyle(rs);
                    break;
                case nameof(FftWindowTypeComboBox)
                    when FftWindowTypeComboBox?.SelectedValue is FftWindowType wt:
                    SelectedFftWindowType = wt;
                    break;
                case nameof(ScaleTypeComboBox)
                    when ScaleTypeComboBox?.SelectedValue is SpectrumScale scale:
                    SelectedScaleType = scale;
                    break;
            }
            InvalidateVisuals();
        }
        catch (Exception ex)
        {
            SmartLogger.Log(
                LogLevel.Error,
                LogPrefix,
                $"Error in combo box selection change: {ex}"
            );
        }
    }

    void OnWindowClosed(object? sender, EventArgs e)
    {
        try
        {
            if (IsOverlayActive)
                CloseOverlay();
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
        catch (Exception ex)
        {
            SmartLogger.Log(
                LogLevel.Error,
                LogPrefix,
                $"Error during window cleanup: {ex}"
            );
        }
    }

    void OnWindowDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    void OnWindowMouseDoubleClick(object? sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject originalElement)
        {
            DependencyObject element = originalElement;
            while (element != null)
            {
                if (element is CheckBox)
                {
                    e.Handled = true;
                    return;
                }
                element = VisualTreeHelper.GetParent(element);
            }
        }
        if (e.ChangedButton == MouseButton.Left)
        {
            e.Handled = true;
            MaximizeWindow();
        }
    }

    void OnSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is not Slider slider)
            return;
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

    void OnButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn)
            return;
        try
        {
            var actions = new Dictionary<string, Action>
            {
                ["StartCaptureButton"] = async () => await StartCaptureAsync(),
                ["StopCaptureButton"] = async () => await StopCaptureAsync(),
                ["OverlayButton"] = () => OnOverlayButtonClick(sender, e),
                ["OpenSettingsButton"] = () =>
                    Dispatcher.Invoke(() => new SettingsWindow().ShowDialog()),
                ["OpenPopupButton"] = () => IsPopupOpen = !IsPopupOpen,
                ["MinimizeButton"] = () => Dispatcher.Invoke(MinimizeWindow),
                ["MaximizeButton"] = () => Dispatcher.Invoke(MaximizeWindow),
                ["CloseButton"] = () => Dispatcher.Invoke(CloseWindow)
            };
            if (actions.TryGetValue(btn.Name, out var act))
                act();
        }
        catch (Exception ex)
        {
            SmartLogger.Log(
                LogLevel.Error,
                LogPrefix,
                $"Error handling button click: {btn.Name} - {ex}"
            );
        }
    }

    void ToggleButtonContainer_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            ToggleControlPanelButton_Click(ToggleControlPanelButton, new RoutedEventArgs());
    }

    void ToggleControlPanelButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (IsControlPanelVisible)
            {
                var hidePanelSB = (Storyboard)FindResource("HidePanelAnimation");
                hidePanelSB.Completed += (s, ev) =>
                {
                    ControlPanel.Visibility = Visibility.Collapsed;
                    IsControlPanelVisible = false;
                    ((Storyboard)FindResource("HidePanelAndButtonAnimation")).Begin(this);
                };
                hidePanelSB.Begin(ControlPanel);
            }
            else
            {
                ControlPanel.Visibility = Visibility.Visible;
                ((Storyboard)FindResource("ShowPanelAndButtonAnimation")).Begin(this);
                ((Storyboard)FindResource("ShowPanelAnimation")).Begin(ControlPanel);
                IsControlPanelVisible = true;
            }
            var pulseTransform = new ScaleTransform(1.0, 1.0);
            var originalTransform = ToggleControlPanelButton.RenderTransform;
            ToggleControlPanelButton.RenderTransform = pulseTransform;
            var pulseAnimation = new DoubleAnimation
            {
                From = 1.0,
                To = 1.1,
                Duration = TimeSpan.FromSeconds(0.15),
                AutoReverse = true,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            pulseTransform.BeginAnimation(ScaleTransform.ScaleXProperty, pulseAnimation);
            pulseTransform.BeginAnimation(ScaleTransform.ScaleYProperty, pulseAnimation);
            pulseAnimation.Completed += (s, args) =>
                ToggleControlPanelButton.RenderTransform = originalTransform;
        }
        catch (Exception ex)
        {
            SmartLogger.Log(
                LogLevel.Error,
                LogPrefix,
                $"Error toggling control panel: {ex}"
            );
        }
    }

    void OnOverlayButtonClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (IsOverlayActive)
                CloseOverlay();
            else
                Dispatcher.Invoke(OpenOverlay);
            SpectrumRendererFactory.ConfigureAllRenderers(IsOverlayActive);
            spectrumCanvas?.InvalidateVisual();
        }
        catch (Exception ex)
        {
            SmartLogger.Log(
                LogLevel.Error,
                LogPrefix,
                $"Error toggling overlay: {ex}"
            );
        }
    }

    void UpdateToggleButtonContent()
    {
        if (ToggleButtonIcon?.RenderTransform is RotateTransform rt)
        {
            rt.BeginAnimation(
                RotateTransform.AngleProperty,
                new DoubleAnimation
                {
                    To = IsControlPanelVisible ? 0 : 180,
                    Duration = TimeSpan.FromSeconds(0.3),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                }
            );
        }
    }

    void CloseWindow() => Close();
    void MinimizeWindow() => WindowState = WindowState.Minimized;
    void MaximizeWindow() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void UpdateOverlayTopmostState()
    {
        if (_overlayWindow?.IsInitialized == true)
            _overlayWindow.Topmost = IsOverlayTopmost;
    }
    void UpdateRendererDimensions(int width, int height) =>
        _renderer?.UpdateRenderDimensions(width, height);

    async void UpdateFftWindowType(FftWindowType windowType)
    {
        if (_analyzer?.FftProcessor == null || _captureManager == null)
            return;

        try
        {
            var currentType = _analyzer.FftProcessor.WindowType;
            SmartLogger.Log(
                LogLevel.Debug,
                LogPrefix,
                $"Changing FFT window type from {currentType} to {windowType}",
                forceLog: true
            );

            IsTransitioning = true;
            InvalidateVisuals();

            bool wasRecording = _captureManager.IsRecording;
            if (wasRecording)
            {
                SmartLogger.Log(
                    LogLevel.Debug,
                    LogPrefix,
                    "Pausing capture for window type change",
                    forceLog: true
                );
                await _captureManager.StopCaptureAsync(false);
            }

            _analyzer.FftProcessor.WindowType = windowType;

            if (wasRecording)
            {
                SmartLogger.Log(
                    LogLevel.Debug,
                    LogPrefix,
                    "Resuming capture after window type change",
                    forceLog: true
                );
                await _captureManager.StartCaptureAsync();
            }

            _renderer?.RequestRender();
            InvalidateVisuals();
            SynchronizeRendererWithSettings();
        }
        catch (Exception ex)
        {
            SmartLogger.Log(
                LogLevel.Error,
                LogPrefix,
                $"Error changing FFT window type: {ex}"
            );
        }
        finally
        {
            IsTransitioning = false;
            InvalidateVisuals();
        }
    }

    async void UpdateSpectrumScale(SpectrumScale scale)
    {
        if (_analyzer == null || _captureManager == null)
            return;

        try
        {
            SmartLogger.Log(
                LogLevel.Debug,
                LogPrefix,
                $"Changing spectrum scale from {_analyzer.ScaleType} to {scale}",
                forceLog: true
            );

            IsTransitioning = true;
            InvalidateVisuals();

            bool wasRecording = _captureManager.IsRecording;
            if (wasRecording)
            {
                SmartLogger.Log(
                    LogLevel.Debug,
                    LogPrefix,
                    "Pausing capture for scale change",
                    forceLog: true
                );
                await _captureManager.StopCaptureAsync(false);
            }

            _analyzer.ScaleType = scale;

            if (wasRecording)
            {
                SmartLogger.Log(
                    LogLevel.Debug,
                    LogPrefix,
                    "Resuming capture after scale change",
                    forceLog: true
                );
                await _captureManager.StartCaptureAsync();
            }

            _renderer?.RequestRender();
            InvalidateVisuals();
            SynchronizeRendererWithSettings();
        }
        catch (Exception ex)
        {
            SmartLogger.Log(
                LogLevel.Error,
                LogPrefix,
                $"Error changing spectrum scale: {ex}"
            );
        }
        finally
        {
            IsTransitioning = false;
            InvalidateVisuals();
        }
    }

    void SynchronizeRendererWithSettings()
    {
        if (_renderer == null)
            return;
        try
        {
            _renderer.UpdateRenderStyle(SelectedDrawingType);
            if (_spectrumStyles != null)
            {
                var (startColor, endColor, _) = _spectrumStyles.GetColorsAndBrush(SelectedStyle);
                _renderer.UpdateSpectrumStyle(SelectedStyle, startColor, endColor);
            }
            SmartLogger.Log(
                LogLevel.Debug,
                LogPrefix,
                "Renderer synchronized with current settings",
                forceLog: true
            );
        }
        catch (Exception ex)
        {
            SmartLogger.Log(
                LogLevel.Error,
                LogPrefix,
                $"Failed to synchronize renderer: {ex}"
            );
        }
    }

    void UpdateProps() =>
        OnPropertyChanged(nameof(IsRecording), nameof(CanStartCapture), nameof(StatusText));

    void UpdateGainParameter(float newValue, Action<float> setter, string propertyName)
    {
        setter(newValue);
        OnPropertyChanged(propertyName);
        InvalidateVisuals();
    }

    void InvalidateVisuals() => RenderElement?.InvalidateVisual();

    void UpdateState(
        Func<MainWindowSettings, MainWindowSettings> updater,
        string propertyName,
        Action? callback = null
    )
    {
        _state = updater(_state);
        OnPropertyChanged(propertyName);
        callback?.Invoke();
    }

    protected bool SetField<T>(
        ref T field,
        T value,
        Action? callback = null,
        [CallerMemberName] string? propertyName = null
    )
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName ?? string.Empty);
        callback?.Invoke();
        return true;
    }

    public virtual void OnPropertyChanged(params string[] propertyNames)
    {
        foreach (var name in propertyNames)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}