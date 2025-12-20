namespace SpectrumNet.SN.Controllers;

public static class Limits
{
    public const int MinBars = 10;
    public const int MaxBars = 200;
    public const int BarStep = 5;
    public const double MinSpacing = 0;
    public const double MaxSpacing = 20;
    public const double SpacingStep = 0.5;
}

public sealed class View : ObservableObject, IRendererContext, IDisposable
{
    private static readonly RenderStyle[] AllStyles = Enum.GetValues<RenderStyle>();

    private readonly ISettingsService _cfg;
    private readonly IRendererFactory _rf;
    private readonly IBrushProvider _br;
    private readonly Func<bool> _isRec;
    private readonly Func<bool> _isOv;

    private SpectrumAnalyzer? _an;
    private Renderer? _rnd;
    private string _style = "Rainbow";
    private RenderStyle _draw;
    private SpectrumScale _scale;
    private RenderQuality _q;
    private bool _perf;
    private int _bars;
    private double _spacing;
    private bool _disposed;

    public View(
        SKElement c,
        ISettingsService s,
        IRendererFactory rf,
        IBrushProvider b,
        Func<bool> isRec,
        Func<bool> isOv)
    {
        SpectrumCanvas = c ?? throw new ArgumentNullException(nameof(c));
        _cfg = s ?? throw new ArgumentNullException(nameof(s));
        _rf = rf ?? throw new ArgumentNullException(nameof(rf));
        _br = b ?? throw new ArgumentNullException(nameof(b));
        _isRec = isRec ?? throw new ArgumentNullException(nameof(isRec));
        _isOv = isOv ?? throw new ArgumentNullException(nameof(isOv));

        VisualizationConfig v = _cfg.Current.Visualization;
        _style = v.SelectedPalette ?? "Rainbow";
        _draw = v.SelectedRenderStyle;
        _scale = v.SelectedScaleType;
        _q = v.SelectedRenderQuality;
        _perf = v.ShowPerformanceInfo;
        _bars = v.BarCount;
        _spacing = v.BarSpacing;
    }

    public SKElement SpectrumCanvas { get; }
    public SpectrumBrushes Brushes => (SpectrumBrushes)_br;

    bool IRendererContext.IsRecording => _isRec();
    bool IRendererContext.IsOverlayActive => _isOv();
    bool IRendererContext.ShowPerformanceInfo => _perf;
    RenderStyle IRendererContext.Style => _draw;
    RenderQuality IRendererContext.Quality => _q;
    int IRendererContext.BarCount => _bars;
    double IRendererContext.BarSpacing => _spacing;
    ISpectralDataProvider? IRendererContext.DataProvider => _an;

    public SpectrumAnalyzer Analyzer
    {
        get => _an ?? throw new InvalidOperationException();
        set => _an = value ?? throw new ArgumentNullException(nameof(value));
    }

    public Renderer? Renderer
    {
        get => _rnd;
        set
        {
            _rnd = value;
            if (_rnd != null)
            {
                Apply(_style);
                _rnd.RequestRender();
            }
        }
    }

    private bool SetV<T>(
        ref T f,
        T v,
        Func<VisualizationConfig, T, VisualizationConfig> u,
        Action? a = null)
    {
        if (!SetProperty(ref f, v)) return false;

        _cfg.UpdateVisualization(c => u(c, v));
        a?.Invoke();
        _rnd?.RequestRender();
        return true;
    }

    public string SelectedStyle
    {
        get => _style;
        set
        {
            value = string.IsNullOrEmpty(value) ? "Rainbow" : value;
            if (SetV(ref _style, value, (c, v) => c with { SelectedPalette = v }))
                Apply(value);
        }
    }

    public RenderStyle SelectedDrawingType
    {
        get => _draw;
        set => SetV(
            ref _draw,
            value,
            (c, v) => c with { SelectedRenderStyle = v });
    }

    public SpectrumScale ScaleType
    {
        get => _scale;
        set => SetV(
            ref _scale,
            value,
            (c, v) => c with { SelectedScaleType = v },
            () => { if (_an != null) _an.ScaleType = value; });
    }

    public RenderQuality RenderQuality
    {
        get => _q;
        set => SetV(
            ref _q,
            value,
            (c, v) => c with { SelectedRenderQuality = v },
            () => _rf.GlobalQuality = value);
    }

    public bool ShowPerformanceInfo
    {
        get => _perf;
        set => SetV(
            ref _perf,
            value,
            (c, v) => c with { ShowPerformanceInfo = v });
    }

    public int BarCount
    {
        get => _bars;
        set => SetV(
            ref _bars,
            Clamp(value <= 0 ? 32 : value, Limits.MinBars, Limits.MaxBars),
            (c, v) => c with { BarCount = v });
    }

    public double BarSpacing
    {
        get => _spacing;
        set => SetV(
            ref _spacing,
            Clamp(value < 0 ? 0 : value, Limits.MinSpacing, Limits.MaxSpacing),
            (c, v) => c with { BarSpacing = v });
    }

    public IReadOnlyDictionary<string, Palette> AvailablePalettes =>
        _br.RegisteredPalettes;

    public static IEnumerable<FftWindowType> AvailableFftWindowTypes =>
        Enum.GetValues<FftWindowType>();

    public static IEnumerable<SpectrumScale> AvailableScaleTypes =>
        Enum.GetValues<SpectrumScale>();

    public static IEnumerable<RenderQuality> AvailableRenderQualities =>
        Enum.GetValues<RenderQuality>();

    public static IEnumerable<StereoMode> AvailableStereoModes =>
        Enum.GetValues<StereoMode>();

    public IEnumerable<RenderStyle> OrderedDrawingTypes
    {
        get
        {
            ImmutableArray<RenderStyle> f = _cfg.Current.General.FavoriteRenderers;

            return f.Length > 0
                ? AllStyles
                    .OrderBy(x => f.Contains(x) ? 0 : 1)
                    .ThenBy(x => x.ToString())
                : AllStyles.OrderBy(x => x.ToString());
        }
    }

    public void RefreshOrderedDrawingTypes() =>
        OnPropertyChanged(nameof(OrderedDrawingTypes));

    public void RequestRender() => _rnd?.RequestRender();

    public void UpdateRenderDimensions(int w, int h) =>
        _rnd?.UpdateRenderDimensions(w, h);

    public void OnPaintSurface(object? s, SKPaintSurfaceEventArgs? e)
    {
        if (!_disposed && e != null)
            _rnd?.RenderFrame(s, e);
    }

    public void SelectNextRenderer() => Cycle(1);
    public void SelectPreviousRenderer() => Cycle(-1);

    private void Cycle(int d)
    {
        if (AllStyles.Length < 2) return;

        int i = Array.IndexOf(AllStyles, _draw);
        SelectedDrawingType = AllStyles[(i + d + AllStyles.Length) % AllStyles.Length];
    }

    private void Apply(string n)
    {
        if (_rnd == null) return;

        (_, SKPaint? b) = _br.GetColorAndBrush(n);
        _rnd.UpdateSpectrumStyle(n, b);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _rnd?.Dispose();
        _rnd = null;
        _an = null;
    }
}
