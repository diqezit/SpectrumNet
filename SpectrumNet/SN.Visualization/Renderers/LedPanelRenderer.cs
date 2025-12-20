namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class LedPanelRenderer : GridRenderer<LedPanelRenderer.QS>
{
    private const float LedR0 = 6f, Ain = 0.08f, Bmin = 0.4f, Dec = 0.85f, Atk = 0.4f, Hold = 0.5f,
        PkW = 2f, PkOff = 2f, PkDec = 0.95f, Vthr = 0.05f, TopMul = 1.2f, PkA = 200f, ExtBlend = 0.7f;
    private const int MaxC = 64, MaxR = 32, MinR = 8;

    private static readonly SKColor[] Grad =
    [
        new(0, 200, 100), new(0, 255, 0), new(128, 255, 0), new(255, 255, 0), new(255, 200, 0),
        new(255, 128, 0), new(255, 64, 0), new(255, 0, 0), new(200, 0, 50)
    ];

    private static readonly SKColor InCol = new(80, 80, 80);
    private static readonly SKColor PkCol = SKColors.White;

    private readonly float[] _v = new float[MaxC];
    private readonly PeakTracker[] _pk = new PeakTracker[MaxC];
    private SKPoint[][]? _pos;
    private readonly SKColor[] _row = new SKColor[MaxR];

    private bool _extOn;
    private SKColor _ext = SKColors.White;

    private int _cc, _cr;
    private float _cell, _x0, _y0;

    private static readonly ObjectPool<List<SKRect>> _rp = new(() => new List<SKRect>(256), l => l.Clear(), maxSize: 256);
    private static readonly ObjectPool<List<(SKPoint p, float r)>> _pp = new(() =>
    new List<(SKPoint p, float r)>(128), l => l.Clear(), maxSize: 64);

    private readonly Dictionary<SKColor, List<SKRect>> _map = new(256);

    private static readonly IReadOnlyDictionary<RenderQuality, QS> _pre =
        new Dictionary<RenderQuality, QS>
        {
            [RenderQuality.Low] = new(false, 16, 1.0f),
            [RenderQuality.Medium] = new(true, 24, 0.9f),
            [RenderQuality.High] = new(true, 32, 0.8f)
        };

    public sealed record QS(bool Peak, int MaxR, float Sm);

    protected override IReadOnlyDictionary<RenderQuality, QS> QualitySettingsPresets => _pre;

    private LedPanelRenderer() { }

    protected override RenderParameters CalculateRenderParameters(SKImageInfo info, int bc, float bw, float bs)
    {
        int max = Min(GetMaxBarsForQuality(), MaxC);
        return CalcStandardRenderParams(info, bc, bw, bs, max);
    }

    protected override int GetMaxBarsForQuality() => GetBarsForQuality(32, 48, 64);

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();
        ApplyStandardQualitySmoothing();

        _cc = 0; _cr = 0;
        _cell = 0f; _x0 = 0f; _y0 = 0f;

        Array.Clear(_v, 0, _v.Length);
        for (int i = 0; i < _pk.Length; i++) _pk[i].Reset();

        _pos = null;
        _map.Clear();

        _extOn = false;
        _ext = SKColors.White;

        RequestRedraw();
    }

    protected override void OnDispose()
    {
        _map.Clear();
        _pos = null;
        base.OnDispose();
    }

    protected override void RenderEffect(SKCanvas c, float[] s, SKImageInfo info, RenderParameters rp, SKPaint paint)
    {
        if (CurrentQualitySettings is not { } qs) return;
        if (rp.EffectiveBarCount <= 0 || rp.BarWidth <= 0f) return;

        int cols = Clamp(rp.EffectiveBarCount, 1, Min(GetMaxBarsForQuality(), MaxC));
        int rows = Rows(info, rp, qs);
        if (rows <= 0) return;

        UpdExt(paint);
        Ens(cols, rows, info, rp);
        Upd(s, cols, rows, qs);

        DrawIn(c, cols, rows);
        DrawOn(c, cols, rows, qs);
    }

    private static int Rows(SKImageInfo info, RenderParameters rp, QS s)
    {
        float stride = rp.BarWidth + rp.BarSpacing;
        float cell = Clamp(stride, 2f, Max(2f, info.Height));
        int byH = (int)Floor(info.Height / cell);
        int rows = Min(s.MaxR, byH);
        return Clamp(rows, MinR, MaxR);
    }

    private void UpdExt(SKPaint p)
    {
        _extOn = p.Color != SKColors.White;
        if (_extOn) _ext = p.Color;
    }

    private void Ens(int cols, int rows, SKImageInfo info, RenderParameters rp)
    {
        float stride = rp.BarWidth + rp.BarSpacing;
        float x0 = rp.StartOffset;

        float cell = Min(stride, info.Height / (float)rows);
        float h = rows * cell;
        float y0 = (info.Height - h) * 0.5f;

        bool ch =
            _cc != cols ||
            _cr != rows ||
            Abs(_cell - cell) > 0.01f ||
            Abs(_x0 - x0) > 0.5f ||
            Abs(_y0 - y0) > 0.5f ||
            Grid.Ov != IsOverlayActive;

        if (!ch) return;

        _cc = cols; _cr = rows;
        _cell = cell; _x0 = x0; _y0 = y0;

        Grid.Set(cols, rows, rp.BarWidth, rp.BarSpacing, cell, x0, y0, IsOverlayActive);

        _pos = EnsureJaggedArray(ref _pos, cols, rows);

        float half = cell * 0.5f;
        float baseX = x0 + rp.BarWidth * 0.5f;

        for (int col = 0; col < cols; col++)
        {
            float x = baseX + col * stride;
            for (int row = 0; row < rows; row++)
            {
                float y = y0 + (rows - 1 - row) * cell + half;
                _pos[col][row] = new SKPoint(x, y);
            }
        }

        for (int i = 0; i < _row.Length; i++)
        {
            float t = i / (float)(_row.Length - 1);
            _row[i] = InterpolateColorArray(Grad, t);
        }

        RequestRedraw();
    }

    private void Upd(float[] s, int cols, int rows, QS qs)
    {
        int n = Min(cols, s.Length);

        for (int i = 0; i < n; i++)
        {
            float up = Atk * qs.Sm;
            float down = 1f - Dec * qs.Sm;

            float v = SmoothWithAttackRelease(_v[i], s[i], up, down);
            _v[i] = v;

            if (qs.Peak)
                _pk[i].Update(v, Hold, DeltaTime, 1f - PkDec);
        }
    }

    private static float LedR(float cell)
    {
        float max = cell * 0.5f - 1f;
        return Max(1f, Min(LedR0, max));
    }

    private void DrawIn(SKCanvas c, int cols, int rows)
    {
        if (_pos == null) return;

        float r = LedR(_cell);
        byte a = (byte)(Ain * 255f * GetOverlayAlphaFactor());

        SKPaint p = CreatePaint(InCol.WithAlpha(a), Fill);

        try
        {
            using Pooled<List<SKRect>> lr = RentList<SKRect>();
            List<SKRect> rects = lr.Value;

            rects.Clear();
            rects.Capacity = Max(rects.Capacity, cols * rows);

            for (int col = 0; col < cols; col++)
                for (int row = 0; row < rows; row++)
                {
                    SKPoint pt = _pos[col][row];
                    rects.Add(new SKRect(pt.X - r, pt.Y - r, pt.X + r, pt.Y + r));
                }

            RenderRects(c, rects, p, r);
        }
        finally
        {
            ReturnPaint(p);
        }
    }

    private void DrawOn(SKCanvas c, int cols, int rows, QS qs)
    {
        if (_pos == null) return;

        float r = LedR(_cell);

        foreach (KeyValuePair<SKColor, List<SKRect>> kv in _map)
            _rp.Return(kv.Value);
        _map.Clear();

        List<(SKPoint p, float r)> pk = _pp.Get();

        try
        {
            int n = Min(cols, _v.Length);

            for (int col = 0; col < n; col++)
            {
                float v = _v[col];
                int on = OnCnt(v, rows);

                for (int row = 0; row < on; row++)
                {
                    float br = Br(v, row == on - 1);
                    SKColor colr = LedCol(row, br);

                    SKPoint pt = _pos[col][row];
                    var rr = new SKRect(pt.X - r, pt.Y - r, pt.X + r, pt.Y + r);

                    if (!_map.TryGetValue(colr, out List<SKRect>? list))
                    {
                        list = _rp.Get();
                        _map[colr] = list;
                    }

                    list.Add(rr);
                }

                if (qs.Peak && _pk[col].Timer > 0f)
                {
                    int pr = (int)(_pk[col].Value * rows) - 1;
                    if (pr >= 0 && pr < rows)
                        pk.Add((_pos[col][pr], r + PkOff));
                }
            }

            foreach (KeyValuePair<SKColor, List<SKRect>> kv in _map)
            {
                SKPaint p = CreatePaint(kv.Key, Fill);
                try { RenderRects(c, kv.Value, p, r); }
                finally { ReturnPaint(p); }
            }

            if (pk.Count > 0)
                DrawPk(c, pk);
        }
        finally
        {
            foreach (KeyValuePair<SKColor, List<SKRect>> kv in _map)
                _rp.Return(kv.Value);
            _map.Clear();
            _pp.Return(pk);
        }
    }

    private static int OnCnt(float v, int rows)
    {
        int on = (int)(v * rows);
        if (on == 0 && v > Vthr) on = 1;
        return Clamp(on, 0, rows);
    }

    private static float Br(float v, bool top)
    {
        float b = Lerp(Bmin, 1f, v);
        if (top) b *= TopMul;
        return Min(b, 1f);
    }

    private SKColor LedCol(int row, float br)
    {
        SKColor baseC = _row[Min(row, _row.Length - 1)];
        if (_extOn) baseC = Blend(baseC, row);
        float k = br * GetOverlayAlphaFactor();
        return baseC.WithAlpha((byte)(k * 255f));
    }

    private SKColor Blend(SKColor baseC, int row)
    {
        float t = _row.Length > 0 ? row / (float)_row.Length : 0f;

        return new SKColor(
            BlendC(_ext.Red, baseC.Red, t),
            BlendC(_ext.Green, baseC.Green, t),
            BlendC(_ext.Blue, baseC.Blue, t));
    }

    private static byte BlendC(byte ext, byte grad, float t) =>
        (byte)(ext * ExtBlend + grad * (1f - ExtBlend) * t);

    private void DrawPk(SKCanvas c, List<(SKPoint p, float r)> pk)
    {
        SKColor col = _extOn ? _ext.WithAlpha((byte)PkA) : PkCol.WithAlpha((byte)PkA);
        col = col.WithAlpha((byte)(col.Alpha * GetOverlayAlphaFactor()));

        SKPaint p = CreateStrokePaint(col, PkW);
        try
        {
            for (int i = 0; i < pk.Count; i++)
                c.DrawCircle(pk[i].p, pk[i].r, p);
        }
        finally
        {
            ReturnPaint(p);
        }
    }
}
