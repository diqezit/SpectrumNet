namespace SpectrumNet.SN.Visualization;

public readonly record struct RenderParameters(
    int EffectiveBarCount,
    float BarWidth,
    float BarSpacing,
    float StartOffset);

public readonly record struct Pooled<T>(ObjectPool<T> Pool) : IDisposable where T : class
{
    public T Value { get; } = Pool.Get();
    public void Dispose() => Pool.Return(Value);
}

public record struct PeakTracker
{
    public float Value;
    public float Timer;
    public float Velocity;

    public void Capture(float v, float hold)
    {
        Value = v;
        Timer = hold;
        Velocity = 0f;
    }

    public void Update(float cur, float hold, float dt, float fall)
    {
        if (cur > Value) { Capture(cur, hold); return; }
        if (Timer > 0f) { Timer -= dt; return; }
        Value = MathF.Max(0f, Value - fall * dt);
    }

    public void UpdateWithGravity(float cur, float hold, float dt, float g, float damp, float cap = 0.02f)
    {
        if (cur > Value + cap) { Capture(cur, hold); return; }
        if (Timer > 0f) { Timer -= dt; return; }
        Velocity += g;
        Velocity *= damp;
        Value = MathF.Max(0f, MathF.Max(cur, Value - Velocity));
    }

    public void Reset()
    {
        Value = 0f;
        Timer = 0f;
        Velocity = 0f;
    }
}

public record struct BoundsBuilder
{
    private SKRect _r;

    public readonly SKRect Bounds => _r;
    public bool HasBounds { get; private set; }

    public void Add(SKRect r)
    {
        _r = HasBounds ? SKRect.Union(_r, r) : r;
        HasBounds = true;
    }

    public void Add(float x, float y)
    {
        if (!HasBounds) { _r = new SKRect(x, y, x, y); HasBounds = true; return; }

        if (x < _r.Left) _r.Left = x;
        else if (x > _r.Right) _r.Right = x;

        if (y < _r.Top) _r.Top = y;
        else if (y > _r.Bottom) _r.Bottom = y;
    }

    public void Add(SKPoint p) => Add(p.X, p.Y);

    public void Inflate(float dx, float dy)
    {
        if (HasBounds) _r.Inflate(dx, dy);
    }

    public void Reset()
    {
        _r = SKRect.Empty;
        HasBounds = false;
    }
}

public record struct DimCache
{
    public int W, H;
    public float FW, FH;

    public bool Changed(SKImageInfo info)
    {
        if (W == info.Width && H == info.Height) return false;
        W = info.Width;
        H = info.Height;
        FW = info.Width;
        FH = info.Height;
        return true;
    }

    public bool Changed(int w, int h)
    {
        if (W == w && H == h) return false;
        W = w;
        H = h;
        FW = w;
        FH = h;
        return true;
    }

    public readonly SKPoint Center => new(FW * 0.5f, FH * 0.5f);
    public readonly float MinDim => MathF.Min(FW, FH);
}

public record struct GridCache
{
    public int Cols, Rows;
    public float Cell, X, Y, Px, Mar;
    public bool Ov;

    public readonly bool Changed(int c, int r, float cell, float x, float y, bool ov, float tol = 0.25f) =>
        Cols != c || Rows != r || Ov != ov ||
        Abs(Cell - cell) > tol ||
        Abs(X - x) > tol ||
        Abs(Y - y) > tol;

    public void Set(int c, int r, float px, float mar, float cell, float x, float y, bool ov)
    {
        Cols = c;
        Rows = r;
        Px = px;
        Mar = mar;
        Cell = cell;
        X = x;
        Y = y;
        Ov = ov;
    }
}

public record struct AnimState
{
    public float Phase;
    public float Time;
    public float Accumulator;

    public float UpdatePhase(float spd, float dt = 0.016f)
    {
        Phase += spd * dt;
        if (Phase >= MathF.PI * 2f) Phase -= MathF.PI * 2f;
        else if (Phase < 0f) Phase += MathF.PI * 2f;
        return Phase;
    }

    public void AddTime(float dt) => Time += dt;

    public bool Tick(float interval, float dt)
    {
        Accumulator += dt;
        if (Accumulator < interval) return false;
        Accumulator = 0f;
        return true;
    }
}

public abstract class SpectrumRenderer : ISpectrumRenderer, IDisposable
{
    protected const float DeltaTime = 0.016f;
    protected const float Tau = MathF.PI * 2f;

    protected static readonly Random Rng = Random.Shared;

    private readonly ISmartLogger _log = Instance;
    private readonly ObjectPool<SKPaint> _paints;
    private readonly ObjectPool<SKPath> _paths;
    private readonly object _lock = new();

    private float _smoothing = 0.3f;
    private float[]? _prev;
    private float[]? _work;

    private bool _dirty;
    private bool _disposed;
    private int _fc;

    private static class Lp<T>
    {
        public static readonly ObjectPool<List<T>> Pool =
            new(() => new List<T>(256), l => l.Clear(), maxSize: 64);
    }

    public RenderQuality Quality { get; private set; } = RenderQuality.Medium;
    public bool IsOverlayActive { get; private set; }

    protected float OverlayAlpha { get; private set; } = 0.8f;
    protected bool IsInitialized { get; private set; }
    protected bool UseAntiAlias { get; private set; } = true;
    protected bool UseAdvancedEffects { get; private set; } = true;

    private int MaxBars => Quality switch
    {
        RenderQuality.Low => 64,
        RenderQuality.High => 256,
        _ => 128
    };

    protected virtual int GetMaxBarsForQuality() => MaxBars;

    protected static ISettingsService Settings => SettingsService.Instance;
    protected static AppSettingsConfig CurrentSettings => Settings.Current;
    protected static GeneralConfig GeneralCfg => CurrentSettings.General;
    protected static ParticlesConfig ParticlesCfg => CurrentSettings.Particles;
    protected static RaindropsConfig RaindropsCfg => CurrentSettings.Raindrops;
    protected static VisualizationConfig VisualizationCfg => CurrentSettings.Visualization;
    protected static KeyBindingsConfig KeyBindingsCfg => CurrentSettings.KeyBindings;

    public float SmoothingFactor
    {
        get => _smoothing;
        protected set => _smoothing = Clamp(value, 0f, 1f);
    }

    protected virtual int CleanupEveryFrames => 300;
    private static readonly float[] colorPos = new[] { 0f, 1f };

    protected SpectrumRenderer()
    {
        _paints = new ObjectPool<SKPaint>(() => new SKPaint(), ResetPaint);
        _paths = new ObjectPool<SKPath>(() => new SKPath(), p => p.Reset());
    }

    private static void ResetPaint(SKPaint p)
    {
        p.Shader = null;
        p.MaskFilter = null;
        p.ImageFilter = null;
        p.PathEffect = null;
        p.Reset();
    }

    public virtual void Initialize()
    {
        if (IsInitialized) return;
        IsInitialized = true;
        OnInitialize();
        OnQualitySettingsApplied();
        _log.Log(LogLevel.Debug, GetType().Name, "Initialized");
    }

    public virtual void Configure(bool isOverlay, RenderQuality quality)
    {
        bool ovCh = IsOverlayActive != isOverlay;
        bool qCh = Quality != quality;

        IsOverlayActive = isOverlay;
        Quality = quality;

        if (ovCh || qCh)
            OnQualitySettingsApplied();

        _log.Log(LogLevel.Debug, GetType().Name, $"Configured: Overlay={isOverlay}, Quality={quality}");
    }

    public virtual void SetOverlayTransparency(float level)
    {
        OverlayAlpha = Clamp(level, 0f, 1f);
        _dirty = true;
    }

    public abstract void Render(
        SKCanvas? canvas,
        float[]? spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint? paint,
        Action<SKCanvas, SKImageInfo>? drawPerformanceInfo);

    public virtual bool RequiresRedraw() => _dirty || IsOverlayActive;

    protected void RequestRedraw() => _dirty = true;
    protected void ClearRedrawRequest() => _dirty = false;

    protected virtual void OnInitialize() { }
    protected virtual void CleanupUnusedResources() { }

    protected virtual void OnQualitySettingsApplied()
    {
        UseAntiAlias = Quality != RenderQuality.Low;
        UseAdvancedEffects = Quality != RenderQuality.Low;
        SmoothingFactor = IsOverlayActive ? 0.5f : 0.3f;
        ResetTemporalSmoothing();
        RequestRedraw();
    }

    protected virtual void OnDispose() { }

    protected void ResetTemporalSmoothing()
    {
        lock (_lock) { _prev = null; }
    }

    protected bool CanRender(SKCanvas? c, float[]? s, SKPaint? p, SKImageInfo i) =>
        IsInitialized &&
        c != null &&
        s is { Length: > 0 } &&
        p != null &&
        i.Width > 0 &&
        i.Height > 0;

    protected void TickCleanup()
    {
        int n = CleanupEveryFrames;
        if (n <= 0) return;
        _fc++;
        if (_fc % n == 0) CleanupUnusedResources();
    }

    protected Pooled<SKPaint> RentPaint() => new(_paints);
    protected Pooled<SKPath> RentPath() => new(_paths);
    protected static Pooled<List<T>> RentList<T>() => new(Lp<T>.Pool);

    protected SKPaint GetPaint() => _paints.Get();
    protected void ReturnPaint(SKPaint p) => _paints.Return(p);

    protected SKPaint CreatePaint(SKColor c, SKPaintStyle st = Fill, SKShader? sh = null)
    {
        SKPaint p = GetPaint();
        p.Color = c;
        p.IsAntialias = UseAntiAlias;
        p.Style = st;
        p.Shader = sh;
        p.MaskFilter = null;
        p.ImageFilter = null;
        p.PathEffect = null;
        return p;
    }

    protected SKPaint CreateStrokePaint(SKColor c, float w, SKStrokeCap cap = SKStrokeCap.Round)
    {
        SKPaint p = CreatePaint(c, Stroke);
        p.StrokeWidth = w;
        p.StrokeCap = cap;
        p.StrokeJoin = SKStrokeJoin.Round;
        return p;
    }

    protected void WithPaint(SKColor c, SKPaintStyle st, Action<SKPaint> act)
    {
        SKPaint p = CreatePaint(c, st);
        try { act(p); }
        finally { ReturnPaint(p); }
    }

    protected void WithPaint(SKColor c, SKPaintStyle st, SKShader? sh, Action<SKPaint> act)
    {
        SKPaint p = CreatePaint(c, st, sh);
        try { act(p); }
        finally { ReturnPaint(p); }
    }

    protected void WithStroke(SKColor c, float w, Action<SKPaint> act)
    {
        SKPaint p = CreateStrokePaint(c, w);
        try { act(p); }
        finally { ReturnPaint(p); }
    }

    protected void WithBlur(SKColor c, SKPaintStyle st, float sig, Action<SKPaint> act)
    {
        SKPaint p = CreatePaint(c, st);

        if (sig > 0f)
        {
            using var blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, sig);
            p.MaskFilter = blur;

            try { act(p); }
            finally { p.MaskFilter = null; ReturnPaint(p); }
        }
        else
        {
            try { act(p); }
            finally { ReturnPaint(p); }
        }
    }

    protected void WithMask(SKColor c, SKPaintStyle st, SKMaskFilter? mf, Action<SKPaint> act)
    {
        SKPaint p = CreatePaint(c, st);
        p.MaskFilter = mf;

        try { act(p); }
        finally { p.MaskFilter = null; ReturnPaint(p); }
    }

    protected void WithShader(SKColor c, SKPaintStyle st, Func<SKShader?> mk, Action<SKPaint> act)
    {
        SKPaint p = CreatePaint(c, st);

        SKShader? sh = null;
        try { sh = mk(); p.Shader = sh; act(p); }
        finally { p.Shader = null; sh?.Dispose(); ReturnPaint(p); }
    }

    protected void WithImageFilter(SKColor c, SKPaintStyle st, Func<SKImageFilter?> mk, Action<SKPaint> act)
    {
        SKPaint p = CreatePaint(c, st);

        SKImageFilter? f = null;
        try { f = mk(); p.ImageFilter = f; act(p); }
        finally { p.ImageFilter = null; f?.Dispose(); ReturnPaint(p); }
    }

    protected void WithImageBlur(SKColor c, SKPaintStyle st, float sx, float sy, Action<SKPaint> act)
    {
        if (sx <= 0f && sy <= 0f) { WithPaint(c, st, act); return; }
        WithImageFilter(c, st, () => SKImageFilter.CreateBlur(sx, sy), act);
    }

    protected void WithPathEffect(SKColor c, SKPaintStyle st, Func<SKPathEffect?> mk, Action<SKPaint> act)
    {
        SKPaint p = CreatePaint(c, st);

        SKPathEffect? e = null;
        try { e = mk(); p.PathEffect = e; act(p); }
        finally { p.PathEffect = null; e?.Dispose(); ReturnPaint(p); }
    }

    protected RenderParameters CalcRenderParams(SKImageInfo info, int bc, float sp)
    {
        int n = Clamp(bc, 1, GetMaxBarsForQuality());
        if (info.Width <= 0) return new RenderParameters(n, 0f, 0f, 0f);

        float s = MathF.Max(0f, sp);
        const float MinBw = 1f;

        if (n > 1)
        {
            float maxSp = (info.Width - n * MinBw) / (n - 1);
            if (float.IsFinite(maxSp))
                s = MathF.Min(s, MathF.Max(0f, maxSp));
        }

        float totSp = (n - 1) * s;
        float avail = info.Width - totSp;

        return avail <= 0f
            ? new RenderParameters(n, 0f, s, 0f)
            : new RenderParameters(n, avail / n, s, 0f);
    }

    protected static RenderParameters CalcStandardRenderParams(
        SKImageInfo info,
        int bc,
        float bw,
        float bs,
        int maxBars)
    {
        int req = Clamp(bc, 1, maxBars);

        if (info.Width <= 0 || info.Height <= 0)
            return new RenderParameters(req, 0f, 0f, 0f);

        float sp = MathF.Max(0f, bs);
        const float MinBw = 1f;

        if (bw > 0f && req > 1)
        {
            float maxSp = (info.Width - req * bw) / (req - 1);
            if (float.IsFinite(maxSp))
                sp = MathF.Min(sp, MathF.Max(0f, maxSp));
        }

        if (bw > 0f)
        {
            float stride = bw + sp;
            if (stride <= 0f)
                return new RenderParameters(1, 0f, sp, 0f);

            int fit = (int)MathF.Floor((info.Width + sp) / stride);
            int n = Clamp(Min(req, fit), 1, req);

            float tot = n * bw + (n - 1) * sp;
            float off = tot < info.Width ? (info.Width - tot) * 0.5f : 0f;

            return new RenderParameters(n, bw, sp, off);
        }

        if (req > 1)
        {
            float maxSp = (info.Width - req * MinBw) / (req - 1);
            if (float.IsFinite(maxSp))
                sp = MathF.Min(sp, MathF.Max(0f, maxSp));
        }

        float totSp = (req - 1) * sp;
        float avail = info.Width - totSp;

        return avail <= 0f
            ? new RenderParameters(req, 0f, sp, 0f)
            : new RenderParameters(req, avail / req, sp, 0f);
    }

    protected (bool ok, float[]? data) ProcessSpectrum(float[]? s, int n, float? sf = null, bool smooth = true)
    {
        if (s is not { Length: > 0 } || n <= 0)
            return (false, null);

        float[] buf = GetWorkBuffer(n);

        if (s.Length == n) Array.Copy(s, buf, n);
        else ResampleSpectrumAvg(s, buf);

        if (smooth) ApplySmoothing(buf, sf);

        return (true, buf);
    }

    private float[] GetWorkBuffer(int n) =>
        _work is { Length: var len } && len == n ? _work : (_work = new float[n]);

    protected static void ResampleSpectrumAvg(float[] src, float[] dst)
    {
        int n = dst.Length;
        if (n <= 0) return;

        if (src.Length == 0)
        {
            Array.Clear(dst, 0, dst.Length);
            return;
        }

        float block = src.Length / (float)n;

        for (int i = 0; i < n; i++)
        {
            int s = (int)(i * block);
            int e = i == n - 1 ? src.Length : Min((int)((i + 1) * block), src.Length);
            dst[i] = e > s ? Avg(src, s, e) : 0f;
        }
    }

    protected static void ResampleSpectrumMax(float[] src, float[] dst, float min = 0f)
    {
        int n = dst.Length;
        if (n <= 0) return;

        if (src.Length == 0)
        {
            if (min <= 0f) Array.Clear(dst, 0, dst.Length);
            else Array.Fill(dst, min);
            return;
        }

        float ratio = src.Length / (float)n;

        for (int i = 0; i < n; i++)
        {
            float v;

            if (ratio > 1f)
            {
                int a = (int)(i * ratio);
                int b = i == n - 1 ? src.Length : Min((int)((i + 1) * ratio), src.Length);

                v = min;
                for (int j = a; j < b; j++)
                    if (src[j] > v) v = src[j];
            }
            else
            {
                float x = i * ratio;
                int i1 = (int)x;
                int i2 = Min(i1 + 1, src.Length - 1);
                float t = x - i1;

                v = src[i1] * (1f - t) + src[i2] * t;
                if (v < min) v = min;
            }

            dst[i] = v;
        }
    }

    private static float Avg(float[] a, int s, int e)
    {
        float sum = 0f;
        for (int i = s; i < e; i++) sum += a[i];
        return sum / (e - s);
    }

    private void ApplySmoothing(float[] buf, float? sf)
    {
        lock (_lock)
        {
            int n = buf.Length;

            if (_prev is not { Length: var len } || len != n)
            {
                _prev = new float[n];
                Array.Copy(buf, _prev, n);
                return;
            }

            float f = sf ?? _smoothing;
            float inv = 1f - f;

            for (int i = 0; i < n; i++)
            {
                float v = _prev[i] * inv + buf[i] * f;
                _prev[i] = v;
                buf[i] = v;
            }
        }
    }

    protected void SetProcessingSmoothingFactor(float f) => SmoothingFactor = f;

    protected int GetBarsForQuality(int low, int med, int hi) => Quality switch
    {
        RenderQuality.Low => low,
        RenderQuality.Medium => med,
        RenderQuality.High => hi,
        _ => med
    };

    protected float GetSmoothingForQuality(float low, float med, float hi, float ovMul = 1.2f)
    {
        float f = Quality switch
        {
            RenderQuality.Low => low,
            RenderQuality.Medium => med,
            RenderQuality.High => hi,
            _ => med
        };

        return IsOverlayActive ? f * ovMul : f;
    }

    protected void ApplyStandardQualitySmoothing(float ovMul = 1.2f)
    {
        float f = Quality switch
        {
            RenderQuality.Low => 0.4f,
            RenderQuality.High => 0.25f,
            _ => 0.3f
        };

        SetProcessingSmoothingFactor(IsOverlayActive ? f * ovMul : f);
        RequestRedraw();
    }

    protected float GetOverlayAlphaFactor() => IsOverlayActive ? OverlayAlpha : 1f;

    protected float SelectByOverlay(float n, float ov) => IsOverlayActive ? ov : n;
    protected int SelectByOverlay(int n, int ov) => IsOverlayActive ? ov : n;
    protected byte SelectByOverlay(byte n, byte ov) => IsOverlayActive ? ov : n;

    protected static byte CalculateAlpha(float x, float mul = 255f) =>
        x <= 0f ? (byte)0 :
        x >= 1f ? (byte)255 :
        (byte)MathF.Min(x * mul, 255f);

    protected static float Lerp(float a, float b, float t) =>
        a + (b - a) * Clamp(t, 0f, 1f);

    protected static float Normalize(float v, float a, float b) =>
        b <= a ? 0f : Clamp((v - a) / (b - a), 0f, 1f);

    protected static float SmoothWithAttackRelease(float cur, float trg, float up, float down) =>
        Lerp(cur, trg, trg > cur ? up : down);

    protected static float WrapAngle(float a) =>
        a >= Tau ? a - Tau : a < 0f ? a + Tau : a;

    protected static bool IsAreaVisible(SKCanvas? c, SKRect r) => c == null || !c.QuickReject(r);

    protected static SKPoint GetCenter(SKImageInfo info) => new(info.Width * 0.5f, info.Height * 0.5f);
    protected static float GetMinDimension(SKImageInfo info) => MathF.Min(info.Width, info.Height);

    protected static SKRect GetBarRect(float x, float mag, float w, float h, float minH = 1f) =>
        new(x, h - MathF.Max(mag * h, minH), x + w, h);

    protected static float CalculateAverageSpectrum(float[] s)
    {
        if (s.Length == 0) return 0f;
        float sum = 0f;
        for (int i = 0; i < s.Length; i++) sum += s[i];
        return sum / s.Length;
    }

    protected static float CalculateMaxSpectrum(float[] s)
    {
        if (s.Length == 0) return 0f;
        float m = s[0];
        for (int i = 1; i < s.Length; i++) if (s[i] > m) m = s[i];
        return m;
    }

    protected static float CalculateAverageLoudness(float[] s)
    {
        if (s.Length == 0) return 0f;
        float sum = 0f;
        for (int i = 0; i < s.Length; i++) sum += MathF.Abs(s[i]);
        return Clamp(sum / s.Length, 0f, 1f);
    }

    protected static float CalculateRmsLoudness(float[] s, float minDb = -30f, float maxDb = 5f)
    {
        if (s.Length == 0) return minDb;

        float sum = 0f;
        for (int i = 0; i < s.Length; i++) sum += s[i] * s[i];

        float rms = MathF.Sqrt(sum / s.Length);
        float db = 20f * MathF.Log10(MathF.Max(rms, 1e-10f));

        return Clamp(db, minDb, maxDb);
    }

    protected static T[] EnsureArraySize<T>(ref T[]? a, int n) where T : struct
    {
        if (n <= 0) n = 1;
        if (a == null || a.Length != n) a = new T[n];
        return a;
    }

    protected static T[] EnsureArraySize<T>(ref T[]? a, int n, T def) where T : struct
    {
        if (n <= 0) n = 1;

        if (a == null || a.Length != n)
        {
            a = new T[n];
            Array.Fill(a, def);
        }

        return a;
    }

    protected static PeakTracker[] EnsurePeakTrackers(ref PeakTracker[]? a, int n, bool reset = true)
    {
        if (n <= 0) n = 1;

        if (a == null || a.Length != n)
            a = new PeakTracker[n];

        if (reset)
            for (int i = 0; i < a.Length; i++) a[i].Reset();

        return a;
    }

    protected static T[][] EnsureJaggedArray<T>(ref T[][]? a, int rows, int cols) where T : struct
    {
        if (rows <= 0) rows = 1;
        if (cols <= 0) cols = 1;

        if (a == null || a.Length != rows || (a.Length > 0 && a[0].Length != cols))
        {
            a = new T[rows][];
            for (int i = 0; i < rows; i++) a[i] = new T[cols];
        }

        return a;
    }

    protected void RenderPath(SKCanvas c, Action<SKPath> build, SKPaint p)
    {
        using Pooled<SKPath> sp = RentPath();
        build(sp.Value);
        if (!sp.Value.IsEmpty) c.DrawPath(sp.Value, p);
    }

    protected void RenderRects(SKCanvas c, IEnumerable<SKRect> rects, SKPaint p, float r = 0f) =>
        RenderPath(c, path =>
        {
            foreach (SKRect rect in rects)
            {
                if (rect.IsEmpty) continue;
                if (r > 0f) path.AddRoundRect(rect, r, r);
                else path.AddRect(rect);
            }
        }, p);

    protected static SKColor AdjustBrightness(SKColor c, float k)
    {
        byte r = (byte)MathF.Min(255f, c.Red * k);
        byte g = (byte)MathF.Min(255f, c.Green * k);
        byte b = (byte)MathF.Min(255f, c.Blue * k);
        return new SKColor(r, g, b, c.Alpha);
    }

    protected static SKColor InterpolateColor(SKColor a, SKColor b, float t)
    {
        t = Clamp(t, 0f, 1f);

        return new SKColor(
            (byte)Lerp(a.Red, b.Red, t),
            (byte)Lerp(a.Green, b.Green, t),
            (byte)Lerp(a.Blue, b.Blue, t),
            (byte)Lerp(a.Alpha, b.Alpha, t));
    }

    protected static SKColor InterpolateColorArray(SKColor[] cols, float t)
    {
        if (cols.Length == 0) return SKColors.Transparent;
        if (cols.Length == 1) return cols[0];

        t = Clamp(t, 0f, 1f);
        float x = t * (cols.Length - 1);
        int i = (int)x;

        return i >= cols.Length - 1 ? cols[^1] : InterpolateColor(cols[i], cols[i + 1], x - i);
    }

    protected static SKShader CreateVerticalGradient(SKRect b, SKColor top, SKColor bot) =>
        SKShader.CreateLinearGradient(
            new SKPoint(b.Left, b.Top),
            new SKPoint(b.Left, b.Bottom),
            new[] { top, bot },
            colorPos,
            SKShaderTileMode.Clamp);

    protected static SKShader CreateVerticalGradient(SKRect b, SKColor[] cols, float[]? pos = null)
    {
        if (cols.Length == 0)
            return SKShader.CreateColor(SKColors.Transparent);

        float[] p = pos ?? (cols.Length == 1 ? new[] { 0f } : BuildPos(cols.Length));

        return SKShader.CreateLinearGradient(
            new SKPoint(b.Left, b.Top),
            new SKPoint(b.Left, b.Bottom),
            cols,
            p,
            SKShaderTileMode.Clamp);
    }

    private static float[] BuildPos(int n)
    {
        if (n <= 1) return new[] { 0f };
        float[] p = new float[n];
        float d = 1f / (n - 1);
        for (int i = 0; i < n; i++) p[i] = i * d;
        return p;
    }

    protected static SKPoint Cr(SKPoint p0, SKPoint p1, SKPoint p2, SKPoint p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;

        float x = 0.5f * (
            2f * p1.X +
            (-p0.X + p2.X) * t +
            (2f * p0.X - 5f * p1.X + 4f * p2.X - p3.X) * t2 +
            (-p0.X + 3f * p1.X - 3f * p2.X + p3.X) * t3);

        float y = 0.5f * (
            2f * p1.Y +
            (-p0.Y + p2.Y) * t +
            (2f * p0.Y - 5f * p1.Y + 4f * p2.Y - p3.Y) * t2 +
            (-p0.Y + 3f * p1.Y - 3f * p2.Y + p3.Y) * t3);

        return (!float.IsFinite(x) || !float.IsFinite(y)) ? p1 : new SKPoint(x, y);
    }

    protected static void CrPath(SKPath p, ReadOnlySpan<SKPoint> pts, float st)
    {
        p.Reset();

        if (pts.Length == 0) return;
        if (pts.Length == 1) { p.MoveTo(pts[0]); return; }

        float step = Clamp(st, 0.02f, 1f);

        p.MoveTo(pts[0]);

        int n = pts.Length;
        for (int i = 0; i < n - 1; i++)
        {
            SKPoint p0 = i > 0 ? pts[i - 1] : pts[i];
            SKPoint p1 = pts[i];
            SKPoint p2 = pts[i + 1];
            SKPoint p3 = i < n - 2 ? pts[i + 2] : p2;

            for (float tt = 0f; tt <= 1.0001f; tt += step)
                p.LineTo(Cr(p0, p1, p2, p3, tt));
        }

        p.LineTo(pts[^1]);
    }

    protected static SKPoint[] EnsureCircle(ref SKPoint[]? a, int n)
    {
        if (n <= 0) n = 1;
        if (a == null || a.Length != n) a = new SKPoint[n];

        float step = Tau / n;
        for (int i = 0; i < n; i++)
        {
            float ang = i * step;
            a[i] = new SKPoint(MathF.Cos(ang), MathF.Sin(ang));
        }

        return a;
    }

    [SuppressMessage("Security", "CA5394", Justification = "Visual effect")]
    protected static float RandFloat() => Rng.NextSingle();

    [SuppressMessage("Security", "CA5394", Justification = "Visual effect")]
    protected static float RandFloat(float min, float max) => min + Rng.NextSingle() * (max - min);

    [SuppressMessage("Security", "CA5394", Justification = "Visual effect")]
    protected static int RandInt(int max) => Rng.Next(max);

    [SuppressMessage("Security", "CA5394", Justification = "Visual effect")]
    protected static int RandInt(int min, int max) => Rng.Next(min, max);

    [SuppressMessage("Security", "CA5394", Justification = "Visual effect")]
    protected static bool RandChance(float p) => Rng.NextDouble() < p;

    protected static void TrimList<T>(List<T> list, int max)
    {
        while (list.Count > max)
            list.RemoveAt(list.Count - 1);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_lock)
        {
            _prev = null;
            _work = null;
        }

        _paints.Dispose();
        _paths.Dispose();

        OnDispose();
        SuppressFinalize(this);
    }
}

public abstract class EffectSpectrumRenderer<TSettings> : SpectrumRenderer
    where TSettings : class
{
    protected abstract IReadOnlyDictionary<RenderQuality, TSettings> QualitySettingsPresets { get; }
    protected TSettings? CurrentQualitySettings { get; private set; }

    protected virtual RenderParameters CalculateRenderParameters(SKImageInfo info, int bc, float bw, float bs) =>
        CalcRenderParams(info, bc, bs);

    protected virtual bool ValidateRenderConditions(RenderParameters rp) =>
        rp.EffectiveBarCount > 0 && rp.BarWidth > 0f;

    protected virtual int GetSpectrumProcessingCount(RenderParameters rp) =>
        rp.EffectiveBarCount;

    public override void Render(
        SKCanvas? canvas,
        float[]? spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint? paint,
        Action<SKCanvas, SKImageInfo>? perfInfo)
    {
        try
        {
            if (!CanRender(canvas, spectrum, paint, info)) return;
            if (CurrentQualitySettings == null) return;

            RenderParameters rp = CalculateRenderParameters(info, barCount, barWidth, barSpacing);
            if (!ValidateRenderConditions(rp)) return;

            int n = GetSpectrumProcessingCount(rp);
            (bool ok, float[]? data) = ProcessSpectrum(spectrum, n);
            if (!ok || data == null) return;

            RenderWithOverlay(canvas!, () => RenderEffect(canvas!, data, info, rp, paint!));

            ClearRedrawRequest();
            TickCleanup();
        }
        finally
        {
            if (perfInfo != null && canvas != null)
                perfInfo(canvas, info);
        }
    }

    protected void RenderWithOverlay(SKCanvas c, Action render)
    {
        if (!IsOverlayActive || OverlayAlpha <= 0.001f) { render(); return; }

        using Pooled<SKPaint> lp = RentPaint();
        lp.Value.Color = SKColors.White.WithAlpha((byte)(255f * OverlayAlpha));

        int sc = c.SaveLayer(lp.Value);
        try { render(); }
        finally { c.RestoreToCount(sc); }
    }

    protected void RenderLayers(
        Action? glow,
        Action main,
        Action? hi = null,
        Action? post = null)
    {
        if (glow != null && UseAdvancedEffects) glow();
        main();
        if (hi != null && UseAdvancedEffects) hi();
        post?.Invoke();
    }

    protected abstract void RenderEffect(
        SKCanvas canvas,
        float[] spectrum,
        SKImageInfo info,
        RenderParameters rp,
        SKPaint paint);

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();

        if (QualitySettingsPresets.TryGetValue(Quality, out TSettings? v))
            CurrentQualitySettings = v;
        else if (QualitySettingsPresets.TryGetValue(RenderQuality.Medium, out v))
            CurrentQualitySettings = v;
        else
        {
            TSettings? first = null;
            foreach (TSettings x in QualitySettingsPresets.Values) { first = x; break; }
            CurrentQualitySettings = first;
        }

        RequestRedraw();
    }
}

public abstract class Rotating3DRenderer<TSettings> : EffectSpectrumRenderer<TSettings>
    where TSettings : class
{
    protected float RotationX { get; set; }
    protected float RotationY { get; set; }
    protected float RotationZ { get; set; }

    protected virtual float BaseRotationSpeed => 0.02f;
    protected virtual float RotationInfluence => 0.5f;

    protected void UpdateRotationWithSpectrum(float[] s, float mul = 1f)
    {
        float sx = BaseRotationSpeed + (s.Length > 0 ? s[0] : 0f) * RotationInfluence;
        float sy = BaseRotationSpeed + (s.Length > 1 ? s[1] : 0f) * RotationInfluence;
        float sz = BaseRotationSpeed + (s.Length > 2 ? s[2] : 0f) * RotationInfluence;

        RotationX = WrapAngle(RotationX + sx * mul * DeltaTime);
        RotationY = WrapAngle(RotationY + sy * mul * DeltaTime);
        RotationZ = WrapAngle(RotationZ + sz * mul * DeltaTime);
    }

    protected Matrix4x4 CalculateRotationMatrix() =>
        Matrix4x4.CreateRotationX(RotationX) *
        Matrix4x4.CreateRotationY(RotationY) *
        Matrix4x4.CreateRotationZ(RotationZ);

    protected static Vector3 TransformVertex(Vector3 v, Matrix4x4 m) => Vector3.Transform(v, m);
    protected static Vector3 TransformNormal(Vector3 n, Matrix4x4 m) => Vector3.TransformNormal(n, m);

    protected static SKPoint ProjectToScreen(Vector3 v, float s, SKPoint c) =>
        new(v.X * s + c.X, v.Y * s + c.Y);

    protected static float CalculateLighting(Vector3 n, Vector3 l, float amb = 0.4f, float dif = 0.6f)
    {
        float dot = Vector3.Dot(Vector3.Normalize(n), l);
        return amb + dif * MathF.Max(0f, dot);
    }

    protected override void OnDispose()
    {
        RotationX = 0f;
        RotationY = 0f;
        RotationZ = 0f;
        base.OnDispose();
    }
}

public abstract class CircularRenderer<TSettings> : EffectSpectrumRenderer<TSettings>
    where TSettings : class
{
    protected SKPoint[]? DirectionCache { get; private set; }
    protected int DirectionCount { get; private set; }

    protected void EnsureDirections(int n)
    {
        if (DirectionCache != null && DirectionCount == n) return;

        SKPoint[]? cache = DirectionCache;
        EnsureCircle(ref cache, n);
        DirectionCache = cache;
        DirectionCount = n;
    }

    protected SKPoint GetDirection(int i) =>
        DirectionCache != null && (uint)i < (uint)DirectionCache.Length
            ? DirectionCache[i]
            : new SKPoint(1f, 0f);

    protected override void OnDispose()
    {
        DirectionCache = null;
        DirectionCount = 0;
        base.OnDispose();
    }
}

public abstract class GridRenderer<TSettings> : EffectSpectrumRenderer<TSettings>
    where TSettings : class
{
    protected GridCache Grid;
    protected float[][]? Values;
    protected PeakTracker[]? Peaks;

    protected virtual int MinRows => 8;
    protected virtual int MaxRows => 32;
    protected virtual int MaxCols => 64;

    protected int CalculateRows(SKImageInfo info, float cell, int maxRows)
    {
        if (cell <= 0f || info.Height <= 0) return MinRows;
        int byH = (int)MathF.Floor(info.Height / cell);
        return Clamp(Min(maxRows, byH), MinRows, MaxRows);
    }

    protected void EnsureGridBuffers(int cols, int rows)
    {
        Values = EnsureJaggedArray(ref Values, cols, rows);
        Peaks = EnsurePeakTrackers(ref Peaks, cols, reset: false);
    }

    protected void ClearGridBuffers()
    {
        if (Values != null)
            foreach (float[] row in Values) Array.Clear(row, 0, row.Length);

        if (Peaks != null)
            for (int i = 0; i < Peaks.Length; i++) Peaks[i].Reset();
    }

    protected override void OnDispose()
    {
        Values = null;
        Peaks = null;
        Grid = default;
        base.OnDispose();
    }
}

public abstract class WaveformRenderer<TSettings> : EffectSpectrumRenderer<TSettings>
    where TSettings : class
{
    protected SKPath? TopPath;
    protected SKPath? BottomPath;
    protected SKPath? FillPath;

    protected void EnsurePaths()
    {
        TopPath ??= new SKPath();
        BottomPath ??= new SKPath();
        FillPath ??= new SKPath();
    }

    protected void ResetPaths()
    {
        TopPath?.Reset();
        BottomPath?.Reset();
        FillPath?.Reset();
    }

    protected void BuildWaveformPaths(
        float[] s,
        RenderParameters rp,
        float mid,
        float amp,
        bool cubic = true,
        float cubicStep = 0.33f)
    {
        EnsurePaths();
        ResetPaths();

        int n = Min(rp.EffectiveBarCount, s.Length);
        if (n < 2) return;

        float step = rp.BarWidth + rp.BarSpacing;

        Span<SKPoint> top = stackalloc SKPoint[n];
        Span<SKPoint> bot = stackalloc SKPoint[n];

        for (int i = 0; i < n; i++)
        {
            float m = Clamp(s[i], 0f, 1f);
            float x = rp.StartOffset + i * step + rp.BarWidth * 0.5f;
            top[i] = new SKPoint(x, mid - m * amp);
            bot[i] = new SKPoint(x, mid + m * amp);
        }

        float l = rp.StartOffset;
        float r = rp.StartOffset + (n - 1) * step + rp.BarWidth;

        top[0] = new SKPoint(l, top[0].Y);
        bot[0] = new SKPoint(l, bot[0].Y);
        top[n - 1] = new SKPoint(r, top[n - 1].Y);
        bot[n - 1] = new SKPoint(r, bot[n - 1].Y);

        if (cubic)
        {
            CrPath(TopPath!, top, cubicStep);
            CrPath(BottomPath!, bot, cubicStep);
        }
        else
        {
            TopPath!.MoveTo(top[0]);
            BottomPath!.MoveTo(bot[0]);

            for (int i = 1; i < n; i++)
            {
                TopPath.LineTo(top[i]);
                BottomPath.LineTo(bot[i]);
            }
        }

        FillPath!.AddPath(TopPath!);
        FillPath.LineTo(bot[n - 1]);

        for (int i = n - 2; i >= 0; i--)
            FillPath.LineTo(bot[i]);

        FillPath.Close();
    }

    protected SKRect GetWaveformBounds(float margin = 8f)
    {
        if (TopPath == null || BottomPath == null) return SKRect.Empty;
        var bb = SKRect.Union(TopPath.Bounds, BottomPath.Bounds);
        bb.Inflate(margin, margin);
        return bb;
    }

    protected override void OnDispose()
    {
        TopPath?.Dispose();
        BottomPath?.Dispose();
        FillPath?.Dispose();
        TopPath = null;
        BottomPath = null;
        FillPath = null;
        base.OnDispose();
    }
}

public abstract class BitmapBufferRenderer<TSettings> : EffectSpectrumRenderer<TSettings>
    where TSettings : class
{
    protected SKBitmap? Bitmap;
    protected DimCache Dims;

    protected void EnsureBitmap(int w, int h, SKColorType ct = SKColorType.Bgra8888)
    {
        if (w <= 0 || h <= 0) return;
        if (Bitmap != null && Bitmap.Width == w && Bitmap.Height == h) return;

        Bitmap?.Dispose();
        Bitmap = new SKBitmap(new SKImageInfo(w, h, ct, SKAlphaType.Premul));
    }

    protected void DrawBitmapCentered(SKCanvas c, SKImageInfo info)
    {
        if (Bitmap == null) return;
        float x = (info.Width - Bitmap.Width) * 0.5f;
        float y = (info.Height - Bitmap.Height) * 0.5f;
        c.DrawBitmap(Bitmap, x, y);
    }

    protected void DrawBitmapStretched(SKCanvas c, SKRect dst)
    {
        if (Bitmap == null) return;
        c.DrawBitmap(Bitmap, dst);
    }

    protected override void OnDispose()
    {
        Bitmap?.Dispose();
        Bitmap = null;
        base.OnDispose();
    }
}

public abstract class FontCachedRenderer<TSettings> : EffectSpectrumRenderer<TSettings>
    where TSettings : class
{
    protected SKFont? Font;
    protected SKTypeface? Typeface;
    protected float FontSize;

    protected virtual string FontFamily => "Consolas";
    protected virtual SKFontStyle FontStyle => SKFontStyle.Normal;

    protected void EnsureFont(float size, bool aa = true)
    {
        if (Font != null && Abs(FontSize - size) < 0.01f) return;

        Typeface ??= SKTypeface.FromFamilyName(FontFamily, FontStyle);

        Font?.Dispose();
        Font = new SKFont(Typeface, size)
        {
            Edging = aa ? SKFontEdging.SubpixelAntialias : SKFontEdging.Alias
        };

        FontSize = size;
    }

    protected override void OnDispose()
    {
        Font?.Dispose();
        Typeface?.Dispose();
        Font = null;
        Typeface = null;
        FontSize = 0f;
        base.OnDispose();
    }
}

