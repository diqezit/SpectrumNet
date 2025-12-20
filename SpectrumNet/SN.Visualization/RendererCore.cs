namespace SpectrumNet.SN.Visualization;

public sealed class RendererFactory : IRendererFactory
{
    private const string NS = "SpectrumNet.SN.Visualization.Renderers";

    private static readonly Lazy<RendererFactory> _lazy =
        new(() => new(), LazyThreadSafetyMode.ExecutionAndPublication);

    public static RendererFactory Instance => _lazy.Value;

    private static readonly IReadOnlyDictionary<RenderStyle, string> Overrides =
        new Dictionary<RenderStyle, string>
        {
            [RenderStyle.Glitch] = "MatrixRainRenderer",
            [RenderStyle.Kenwood] = "KenwoodBarsRenderer",
            [RenderStyle.Loudness] = "LoudnessMeterRenderer"
        };

    private readonly object _gate = new();
    private readonly Dictionary<RenderStyle, ISpectrumRenderer> _cache = [];

    private ISmartLogger? _log;
    private ITransparencyManager? _tm;
    private RenderQuality _q = RenderQuality.Medium;
    private bool _init;

    private RendererFactory() { }

    public void Initialize(
        ISmartLogger log,
        ITransparencyManager tm,
        RenderQuality q = RenderQuality.Medium)
    {
        if (_init) return;

        _log = log ?? throw new ArgumentNullException(nameof(log));
        _tm = tm ?? throw new ArgumentNullException(nameof(tm));
        _q = q;
        _tm.TransparencyChanged += OnTransparency;
        _init = true;
    }

    public RenderQuality GlobalQuality
    {
        get => _q;
        set
        {
            if (_q != value)
            {
                _q = value;
                ConfigureAllRenderers(null, value);
            }
        }
    }

    public ISpectrumRenderer CreateRenderer(
        RenderStyle s,
        bool ov,
        RenderQuality? q = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        RenderQuality rq = q ?? _q;

        lock (_gate)
        {
            if (_cache.TryGetValue(s, out ISpectrumRenderer? r))
            {
                Configure(r, ov, rq);
                return r;
            }

            ct.ThrowIfCancellationRequested();

            r = Create(s);
            _log?.Safe(r.Initialize, nameof(RendererFactory), $"Init error: {s}");
            r.Configure(ov, rq);
            _cache[s] = r;

            return r;
        }
    }

    public IEnumerable<ISpectrumRenderer> GetAllRenderers()
    {
        lock (_gate) return _cache.Values.ToArray();
    }

    public void ConfigureAllRenderers(bool? ov, RenderQuality? q = null)
    {
        RenderQuality rq = q ?? _q;

        lock (_gate)
        {
            foreach (ISpectrumRenderer r in _cache.Values)
                Configure(r, ov ?? r.IsOverlayActive, rq);
        }
    }

    public void Dispose()
    {
        if (_tm != null)
            _tm.TransparencyChanged -= OnTransparency;

        lock (_gate)
        {
            foreach (ISpectrumRenderer r in _cache.Values)
                r.Dispose();
            _cache.Clear();
        }
    }

    private void OnTransparency(object? s, TransparencyEventArgs e)
    {
        lock (_gate)
        {
            foreach (ISpectrumRenderer r in _cache.Values)
                r.SetOverlayTransparency(e.Level);
        }
    }

    private static void Configure(ISpectrumRenderer r, bool ov, RenderQuality q)
    {
        if (r.IsOverlayActive != ov || r.Quality != q)
            r.Configure(ov, q);
    }

    private static ISpectrumRenderer Create(RenderStyle s)
    {
        Type type = Type.GetType($"{NS}.{GetTypeName(s)}", true)!;

        return Activator.CreateInstance(type, nonPublic: true) as ISpectrumRenderer
            ?? throw new InvalidOperationException($"Create failed: {s}");
    }

    private static string GetTypeName(RenderStyle s) =>
        Overrides.TryGetValue(s, out string? n)
            ? n
            : s.ToString().EndsWith("Renderer")
                ? s.ToString()
                : $"{s}Renderer";
}

public sealed class Renderer : AsyncDisposableBase
{
    private const int PhDelay = 16;
    private const int PhWarmup = 2;

    private readonly ISmartLogger _log;
    private readonly IRendererContext _ctx;
    private readonly IBrushProvider _bp;
    private readonly IRendererFactory _rf;
    private readonly IPerformanceMetricsManager _mm;
    private readonly SKElement _el;

    private RendererPlaceholder? _ph;
    private FrameCache? _fc;
    private PerformanceOverlay? _perf;
    private string _style = "Solid";
    private SKPaint _paint;
    private volatile bool _updating;
    private bool _phSched;
    private int _frame;

    public Renderer(
        IRendererContext ctx,
        IBrushProvider bp,
        IRendererFactory rf,
        IPerformanceMetricsManager mm,
        ISmartLogger log)
    {
        _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        _bp = bp ?? throw new ArgumentNullException(nameof(bp));
        _rf = rf ?? throw new ArgumentNullException(nameof(rf));
        _mm = mm ?? throw new ArgumentNullException(nameof(mm));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _el = ctx.SpectrumCanvas;

        (_, SKPaint b) = _bp.GetColorAndBrush(_style);
        _paint = b.Clone() ?? throw new InvalidOperationException("Brush init failed");
    }

    private RendererPlaceholder Ph => _ph ??= new();
    private FrameCache Cache => _fc ??= new(_log);
    private PerformanceOverlay Perf => _perf ??= new(_mm);

    public bool ShouldShowPlaceholder => !_ctx.IsRecording;
    public SpectrumAnalyzer? Analyzer => _ctx.DataProvider as SpectrumAnalyzer;

    private bool Active => !_isDisposed;
    private bool PhReady => _frame > PhWarmup;

    public void RequestRender()
    {
        if (Active) _el.InvalidateVisual();
    }

    public void UpdateRenderDimensions(int w, int h)
    {
        if (Active && w > 0 && h > 0) MarkDirty();
    }

    public void UpdateSpectrumStyle(string n, SKPaint b)
    {
        if (!Active || _updating || string.IsNullOrEmpty(n) || n == _style)
            return;

        _updating = true;
        try
        {
            SKPaint old = _paint;
            _paint = b.Clone() ?? throw new InvalidOperationException();
            _style = n;
            old.Dispose();
            MarkDirty();
        }
        finally
        {
            _updating = false;
        }
    }

    public void RenderFrame(object? s, SKPaintSurfaceEventArgs e) =>
        _log.Safe(() =>
        {
            SKCanvas c = e.Surface.Canvas;
            c.Clear(SKColors.Transparent);

            if (Skip(s)) return;

            _frame++;
            Core(c, e.Surface, e.Info);
            _mm.RecordFrameTime();
        }, nameof(Renderer), "Render error");

    private void Core(SKCanvas c, SKSurface sf, SKImageInfo i)
    {
        if (ShouldShowPlaceholder)
        {
            TryPh(c, i);
            return;
        }

        if (CanCache())
        {
            Cache.Draw(c);
            return;
        }

        var sp = _ctx.DataProvider?.GetSpectrum();
        if (!HasSpec(sp))
        {
            TryPh(c, i);
            return;
        }

        int n = _ctx.BarCount;
        if (n <= 0) return;

        float spc = MathF.Min((float)_ctx.BarSpacing, i.Width / (n + 1f));

        ISpectrumRenderer r = _rf.CreateRenderer(_ctx.Style, _ctx.IsOverlayActive, _ctx.Quality);

        r.Render(
            c,
            sp!.Value.Spectrum,
            i,
            0,
            spc,
            n,
            _paint,
            _ctx.ShowPerformanceInfo ? Perf.Render : null);

        Cache.Update(sf, i);
    }

    private bool TryPh(SKCanvas c, SKImageInfo i)
    {
        if (!PhReady) return false;

        Ph.Render(c, i);
        SchedPh();
        return true;
    }

    private void SchedPh()
    {
        if (!Active || _phSched) return;

        _phSched = true;
        _ = SchedPhAsync();
    }

    private async Task SchedPhAsync()
    {
        void R() => _phSched = false;

        try
        {
            await Task.Delay(PhDelay).ConfigureAwait(false);

            if (Active && ShouldShowPlaceholder)
            {
                _ = _el.Dispatcher.BeginInvoke(() =>
                {
                    R();
                    if (Active) RequestRender();
                }, DispatcherPriority.Background);
            }
            else
            {
                R();
            }
        }
        catch
        {
            R();
        }
    }

    public RendererPlaceholder? GetPlaceholder() =>
        ShouldShowPlaceholder ? _ph : null;

    protected override void DisposeManaged() =>
        _log.Safe(() =>
        {
            _paint.Dispose();
            _fc?.Dispose();
            _ph?.Dispose();
            _perf?.Dispose();
        }, nameof(Renderer), "Dispose error");

    private void MarkDirty()
    {
        _fc?.MarkDirty();
        RequestRender();
    }

    private bool Skip(object? s) =>
        !Active || (_ctx.IsOverlayActive && s == _ctx.SpectrumCanvas);

    private bool CanCache() =>
        !_ctx.IsRecording && !Cache.IsDirty && Cache.IsValid;

    private static bool HasSpec(SpectralData? sp) =>
        sp.HasValue && sp.Value.Spectrum.Length > 0;
}
