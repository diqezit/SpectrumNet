namespace SpectrumNet;

public partial class MainWindow : Window
{
    private readonly ISettingsService _cfg;
    private readonly IThemes _themes;
    private readonly ISmartLogger _log;
    private readonly IPerformanceMetricsManager _perf;

    private AppController? _ctrl;
    private bool _closing;

    public SKElement SpectrumCanvas => spectrumCanvas;

    private new bool IsActive => _ctrl != null && !_closing;

    private AppController Ctrl =>
        _ctrl ?? throw new InvalidOperationException();

    public MainWindow()
    {
        InitializeComponent();

        _cfg = App.Get<ISettingsService>();
        _themes = App.Get<IThemes>();
        _log = App.Get<ISmartLogger>();
        _perf = App.Get<IPerformanceMetricsManager>();
    }

    public void Initialize(AppController c)
    {
        _ctrl = c ?? throw new ArgumentNullException(nameof(c));
        DataContext = _ctrl;

        CompositionTarget.Rendering += OnRender;
        _themes.PropertyChanged += OnTheme;

        _themes.RegisterWindow(this);
        UpdateTheme();

        _ctrl.Input.RegisterWindow(this);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo i)
    {
        base.OnRenderSizeChanged(i);

        if (!IsActive) return;

        Ctrl.View.UpdateRenderDimensions(
            (int)i.NewSize.Width,
            (int)i.NewSize.Height);

        if (WindowState == WindowState.Normal)
        {
            _cfg.UpdateWindow(w => w with
            {
                Width = Width,
                Height = Height
            });
        }
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);

        if (MaximizeIcon != null)
        {
            MaximizeIcon.Data = Geometry.Parse(
                WindowState == WindowState.Maximized
                    ? "M0,0 L20,0 L20,20 L0,20 Z"
                    : "M2,2 H18 V18 H2 Z");
        }

        _cfg.UpdateWindow(w => w with { State = WindowState });
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);

        if (IsActive && WindowState == WindowState.Normal)
        {
            _cfg.UpdateWindow(w => w with
            {
                Left = Left,
                Top = Top
            });
        }
    }

    public void OnPaintSurface(object? s, SKPaintSurfaceEventArgs? e)
    {
        if (IsActive && e != null)
            Ctrl.View.OnPaintSurface(s, e);
    }

    private void OnRender(object? s, EventArgs e)
    {
        if (!IsActive) return;

        if (!FpsLimiter.Instance.IsEnabled ||
            FpsLimiter.Instance.ShouldRenderFrame())
        {
            Ctrl.View.RequestRender();
            _perf.RecordFrameTime();
        }
    }

    private void OnButtonClick(object s, RoutedEventArgs e)
    {
        if (_closing || s is not Button { Name: var n }) return;

        switch (n)
        {
            case nameof(MinimizeButton):
                WindowState = WindowState.Minimized;
                break;

            case nameof(MaximizeButton):
                WindowState = WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
                break;

            case nameof(CloseButton):
                Close();
                break;

            case nameof(OpenControlPanelButton):
                _ctrl?.UI.ToggleControlPanel();
                break;
        }
    }

    private void OnDrag(object? s, MouseButtonEventArgs e)
    {
        if (!IsActive ||
            e.ChangedButton != MouseButton.Left ||
            Mouse.LeftButton != MouseButtonState.Pressed)
            return;

        try
        {
            DragMove();

            if (WindowState == WindowState.Normal)
            {
                _cfg.UpdateWindow(w => w with
                {
                    Left = Left,
                    Top = Top
                });
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void OnTheme(object? s, PropertyChangedEventArgs e)
    {
        if (IsActive && e.PropertyName == nameof(IThemes.IsDarkTheme))
        {
            _cfg.UpdateGeneral(g => g with
            {
                IsDarkTheme = _themes.IsDarkTheme
            });

            UpdateTheme();
        }
    }

    private void UpdateTheme()
    {
        if (ThemeToggleButton == null) return;

        ThemeToggleButton.Checked -= OnToggle;
        ThemeToggleButton.Unchecked -= OnToggle;

        ThemeToggleButton.IsChecked = _themes.IsDarkTheme;

        ThemeToggleButton.Checked += OnToggle;
        ThemeToggleButton.Unchecked += OnToggle;
    }

    private void OnToggle(object? s, RoutedEventArgs e) =>
        _ctrl?.UI.ToggleTheme();

    private void Hyperlink_RequestNavigate(
        object s,
        RequestNavigateEventArgs e)
    {
        if (!IsActive) return;

        e.Handled = true;

        Process.Start(new ProcessStartInfo
        {
            FileName = e.Uri.AbsoluteUri,
            UseShellExecute = true
        });
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_closing)
        {
            base.OnClosing(e);
            return;
        }

        _closing = true;
        e.Cancel = true;

        CompositionTarget.Rendering -= OnRender;
        _themes.PropertyChanged -= OnTheme;

        _cfg.Apply(_cfg.Current);

        _ = Dispatcher.BeginInvoke(() =>
        {
            try
            {
                (_ctrl as IDisposable)?.Dispose();
            }
            catch (Exception ex)
            {
                _log.Log(LogLevel.Error, nameof(MainWindow), ex.Message);
            }

            Close();
        }, DispatcherPriority.Background);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Application.Current?.Shutdown();
    }
}
