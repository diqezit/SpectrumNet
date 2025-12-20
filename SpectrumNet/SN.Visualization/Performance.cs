namespace SpectrumNet.SN.Visualization;

public enum PerformanceLevel { Excellent, Good, Fair, Poor }

public readonly record struct PerformanceMetrics(double FrameTimeMs, double Fps);

public readonly record struct PerformanceSnapshot(
    float Fps,
    double CpuPct,
    double RamMb,
    PerformanceLevel Level,
    bool Limited);

public sealed class PerformanceMetricsManager : IPerformanceMetricsManager
{
    private const int MaxFrames = 120;
    private const double CpuSmooth = 0.2;
    private const double HiCpu = 80;
    private const double MedCpu = 60;
    private const double HiMem = 1000;
    private const double MedMem = 500;
    private const double MinDtFps = 0.001;
    private const double MinDtCpu = 0.01;
    private const double DefFps = 60;
    private const float GoodFps = 50f;
    private const float FairFps = 30f;

    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(250);
    private static readonly int Cores = Math.Max(Environment.ProcessorCount, 1);

    private static readonly Lazy<PerformanceMetricsManager> _lazy = new(() => new());
    public static PerformanceMetricsManager Instance => _lazy.Value;

    public event EventHandler? MetricsUpdated;
    public event EventHandler? LevelChanged;
    public event EventHandler? FpsLimitChanged;

    private readonly object _gate = new();
    private readonly double[] _times = new double[MaxFrames];
    private readonly Stopwatch _sw = new();
    private readonly Process _proc;

    private PeriodicTimer? _timer;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private TimeSpan _lastCpu;
    private double _lastWall;
    private long _frameN;
    private float _fps = (float)DefFps;
    private double _cpu;
    private double _ram;
    private PerformanceLevel _level = PerformanceLevel.Good;
    private long _lastTick;
    private bool _limited;
    private SynchronizationContext? _ctx;
    private bool _disposed;

    private PerformanceMetricsManager()
    {
        _proc = Process.GetCurrentProcess();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Dispose();
    }

    public bool IsFpsLimited
    {
        get { lock (_gate) return _limited; }
    }

    public void SetFpsLimit(bool on)
    {
        if (_disposed) return;

        lock (_gate)
        {
            if (_limited == on) return;
            _limited = on;

            if (on)
            {
                if (!_sw.IsRunning) _sw.Start();
                _lastTick = _sw.ElapsedMilliseconds;
            }
        }

        Raise(FpsLimitChanged);
    }

    public bool ShouldRenderFrame()
    {
        if (_disposed) return true;
        EnsureInit();

        lock (_gate)
        {
            if (!_limited) return true;

            if (!_sw.IsRunning)
            {
                _sw.Start();
                _lastTick = 0;
                return true;
            }

            long now = _sw.ElapsedMilliseconds;
            if (now - _lastTick < 1000.0 / DefFps)
                return false;

            _lastTick = now;
            return true;
        }
    }

    public void Initialize(SynchronizationContext? ctx = null)
    {
        if (_cts != null || _disposed) return;

        lock (_gate)
        {
            if (_cts != null || _disposed) return;

            _ctx = ctx ?? SynchronizationContext.Current;

            if (!_sw.IsRunning) _sw.Start();

            _lastWall = _sw.Elapsed.TotalSeconds;
            _lastCpu = _proc.TotalProcessorTime;
            _lastTick = _sw.ElapsedMilliseconds;

            _cts = new();
            _timer = new(Interval);
            _loop = Task.Run(() => Loop(_timer, _cts.Token), _cts.Token);
        }
    }

    public void RecordFrameTime()
    {
        if (_disposed) return;
        EnsureInit();

        lock (_gate)
        {
            _times[(int)(_frameN % MaxFrames)] = _sw.Elapsed.TotalSeconds;
            _frameN++;
        }
    }

    public float GetFps() { lock (_gate) return _fps; }
    public double GetCpu() { lock (_gate) return _cpu; }
    public double GetRam() { lock (_gate) return _ram; }
    public PerformanceLevel GetLevel() { lock (_gate) return _level; }

    public PerformanceSnapshot GetSnapshot()
    {
        lock (_gate) return new(_fps, _cpu, _ram, _level, _limited);
    }

    public void Dispose()
    {
        CancellationTokenSource? cts;
        Task? loop;
        PeriodicTimer? timer;

        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;

            cts = _cts;
            loop = _loop;
            timer = _timer;

            _cts = null;
            _loop = null;
            _timer = null;

            MetricsUpdated = null;
            LevelChanged = null;
            FpsLimitChanged = null;

            _ctx = null;
            _sw.Stop();
        }

        if (cts != null)
        {
            cts.Cancel();
            try { loop?.Wait(500); } catch { }
            cts.Dispose();
        }

        timer?.Dispose();
        try { _proc.Dispose(); } catch { }
    }

    private void EnsureInit()
    {
        if (!_disposed && _cts == null) Initialize();
    }

    private async Task Loop(PeriodicTimer t, CancellationToken ct)
    {
        try
        {
            while (await t.WaitForNextTickAsync(ct).ConfigureAwait(false))
                Update();
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private void Update()
    {
        if (_disposed) return;

        float fps;
        double cpu, ram;
        PerformanceLevel lvl;
        bool ch;

        lock (_gate)
        {
            fps = CalcFps();
            cpu = CalcCpu();
            ram = GetRamVal();
            lvl = DetLevel(fps, cpu, ram);

            _fps = fps;
            _cpu = cpu;
            _ram = ram;

            ch = lvl != _level;
            if (ch) _level = lvl;
        }

        Raise(MetricsUpdated);

        if (ch) Raise(LevelChanged);
    }

    private void Raise(EventHandler? h)
    {
        if (h == null || _disposed) return;

        var c = _ctx;
        if (c != null && SynchronizationContext.Current != c)
            c.Post(_ => { if (!_disposed) h(this, EventArgs.Empty); }, null);
        else
            h(this, EventArgs.Empty);
    }

    private float CalcFps()
    {
        int n = (int)Min(_frameN, MaxFrames);
        if (n < 2) return _fps;

        double dt = _times[(int)((_frameN - 1) % MaxFrames)]
                  - _times[(int)((_frameN - n) % MaxFrames)];

        return dt <= MinDtFps ? _fps : (float)((n - 1) / dt);
    }

    private double CalcCpu()
    {
        if (_lastCpu == Zero) return _cpu;

        var now = _proc.TotalProcessorTime;
        double wall = _sw.Elapsed.TotalSeconds;
        double cd = (now - _lastCpu).TotalSeconds;
        double wd = wall - _lastWall;

        _lastCpu = now;
        _lastWall = wall;

        return wd <= MinDtCpu
            ? _cpu
            : Clamp(
            _cpu * (1 - CpuSmooth) + cd / wd / Cores * 100 * CpuSmooth,
            0,
            100);
    }

    private double GetRamVal()
    {
        try { return _proc.WorkingSet64 / 1024.0 / 1024.0; }
        catch { return _ram; }
    }

    private static PerformanceLevel DetLevel(float fps, double cpu, double ram) =>
        fps >= GoodFps && cpu <= MedCpu && ram <= MedMem
            ? PerformanceLevel.Excellent
            : fps >= GoodFps && cpu <= HiCpu && ram <= HiMem
                ? PerformanceLevel.Good
                : fps >= FairFps && cpu <= HiCpu
                    ? PerformanceLevel.Fair
                    : PerformanceLevel.Poor;
}

internal sealed class PerformanceOverlay(IPerformanceMetricsManager mm) : IDisposable
{
    private readonly IPerformanceMetricsManager _mm = mm
        ?? throw new ArgumentNullException(nameof(mm));

    private readonly SKFont _font = new()
    {
        Size = 12,
        Edging = SKFontEdging.SubpixelAntialias
    };

    private readonly SKPaint _paint = new() { IsAntialias = true };

    private readonly SKPaint _bg = new()
    {
        Color = new(0, 0, 0, 180),
        IsAntialias = true
    };

    private bool _disposed;

    public void Render(SKCanvas c, SKImageInfo i)
    {
        if (_disposed) return;

        var s = _mm.GetSnapshot();
        string lim = s.Limited ? " [60]" : "";
        string txt = $"RAM:{s.RamMb:F0}MB CPU:{s.CpuPct:F0}% FPS:{s.Fps:F0}{lim} | {s.Level}";

        float pad = 6f;
        float w = _font.MeasureText(txt);
        float h = _font.Size;

        var r = new SKRect(
            8,
            i.Height - h - pad * 2 - 8,
            w + pad * 2 + 8,
            i.Height - 8);

        c.DrawRoundRect(r, 4, 4, _bg);

        _paint.Color = s.Level switch
        {
            PerformanceLevel.Excellent => SKColors.LimeGreen,
            PerformanceLevel.Good => SKColors.DodgerBlue,
            PerformanceLevel.Fair => SKColors.Orange,
            _ => SKColors.Red
        };

        c.DrawText(txt, r.Left + pad, r.Bottom - pad, _font, _paint);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _font.Dispose();
        _paint.Dispose();
        _bg.Dispose();
    }
}
