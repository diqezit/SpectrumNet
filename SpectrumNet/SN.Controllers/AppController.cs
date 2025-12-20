namespace SpectrumNet.SN.Controllers;

public sealed class AppController : ObservableObject, IDisposable
{
    private static readonly IReadOnlyDictionary<string, string[]> PropMap =
        new Dictionary<string, string[]>
        {
            [nameof(IAudioController.IsRecording)] =
                [nameof(IsRecording), nameof(CanStartCapture)],
            [nameof(IAudioController.CanStartCapture)] =
                [nameof(CanStartCapture)],
            [nameof(IAudioController.WindowType)] =
                [nameof(WindowType)],
            [nameof(IAudioController.StereoMode)] =
                [nameof(StereoMode)],
            [nameof(View.ScaleType)] =
                [nameof(ScaleType)]
        };

    private readonly ISmartLogger _log;
    private readonly IPerformanceMetricsManager _perf;
    private readonly IBrushProvider _br;
    private readonly IRendererFactory _rf;
    private readonly ISettingsService _cfg;
    private readonly CancellationTokenSource _cts = new();

    private bool _disposed;

    public IAudioController Audio { get; }
    public View View { get; }
    public UIManager UI { get; }
    public IOverlayManager Overlay { get; }
    public InputHandler Input { get; }

    public static Dispatcher Dispatcher =>
        Application.Current?.Dispatcher
        ?? throw new InvalidOperationException();

    public bool LimitFpsTo60
    {
        get => _perf.IsFpsLimited;
        set
        {
            _perf.SetFpsLimit(value);
            OnPropertyChanged();
        }
    }

    public bool IsRecording
    {
        get => Audio.IsRecording;
        set => Audio.IsRecording = value;
    }

    public bool CanStartCapture => !_disposed && Audio.CanStartCapture;

    public FftWindowType WindowType
    {
        get => Audio.WindowType;
        set
        {
            Audio.WindowType = value;
            OnPropertyChanged();
        }
    }

    public SpectrumScale ScaleType
    {
        get => View.ScaleType;
        set => View.ScaleType = value;
    }

    public StereoMode StereoMode
    {
        get => Audio.StereoMode;
        set => Audio.StereoMode = value;
    }

    public AppController(
        Window o,
        SKElement c,
        ISettingsService cfg,
        ISmartLogger log,
        IRendererFactory rf,
        IBrushProvider br,
        IThemes th,
        ITransparencyManager tm,
        IPerformanceMetricsManager perf)
    {
        ArgumentNullException.ThrowIfNull(o);
        ArgumentNullException.ThrowIfNull(c);

        _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _rf = rf ?? throw new ArgumentNullException(nameof(rf));
        _br = br ?? throw new ArgumentNullException(nameof(br));
        _perf = perf ?? throw new ArgumentNullException(nameof(perf));

        SynchronizationContext ctx = SynchronizationContext.Current
            ?? throw new InvalidOperationException();

        Overlay = new OverlayManager(this, tm, _cfg);
        UI = new UIManager(this, th);

        View = new View(
            c,
            _cfg,
            _rf,
            _br,
            () => Audio?.IsRecording ?? false,
            () => UI.IsOverlayActive);

        Audio = new AudioController(
            this,
            ctx,
            _cfg.GainParameters,
            _cfg,
            _rf,
            _log,
            _perf,
            _br);

        Input = new InputHandler(this, _cfg, tm);

        _perf.FpsLimitChanged += OnFpsLimitChanged;
        Audio.PropertyChanged += OnSub;
        View.PropertyChanged += OnSub;

        InitRender();
        Input.RegisterWindow(o);
    }

    private void OnFpsLimitChanged(object? s, EventArgs e) =>
        OnPropertyChanged(nameof(LimitFpsTo60));

    private void OnSub(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == null) return;

        if (PropMap.TryGetValue(e.PropertyName, out string[]? ps))
        {
            foreach (string p in ps)
                OnPropertyChanged(p);
        }

        if (e.PropertyName == nameof(IAudioController.IsRecording) && IsRecording)
            View.RequestRender();
    }

    private void InitRender()
    {
        View.Analyzer = new SpectrumAnalyzer(Audio.GainParameters)
        {
            WindowType = Audio.WindowType,
            ScaleType = View.ScaleType,
            StereoMode = Audio.StereoMode
        };

        View.Renderer = new Renderer(View, _br, _rf, _perf, _log);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _perf.FpsLimitChanged -= OnFpsLimitChanged;
        Audio.PropertyChanged -= OnSub;
        View.PropertyChanged -= OnSub;

        Try(_cts.Cancel);

        if (IsRecording)
            Try(() => Audio.StopCaptureAsync().Wait(2000));

        UI.CloseControlPanel();
        View.Dispose();

        Try(() => (Overlay as IDisposable)?.Dispose());

        Audio.Dispose();
        UI.Dispose();
        Input.Dispose();

        Try(_cts.Dispose);
    }

    private static void Try(Action a)
    {
        try { a(); } catch { }
    }
}
