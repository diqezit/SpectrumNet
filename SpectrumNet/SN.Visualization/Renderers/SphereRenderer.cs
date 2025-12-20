namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class SphereRenderer : CircularRenderer<SphereRenderer.QS>
{

    private const float
        MIN_MAG = 0.01f, INT_MUL = 3f, MIN_A = 0.1f, DEG = MathF.PI / 180f,
        R0 = 40f, R0_OV = 20f, R_MIN = 1f, SP0 = 10f, SP0_OV = 5f,
        A_THR = 0.1f, MIN_SZ = 2f, SP_MUL = 0.2f, R_DEC = 0.2f, SP_DEC = 0.1f, R_SP = 0.5f, SP_SP = 0.3f;

    private const int CNT0 = 8, CNT0_OV = 16, MAX_G = 5, CNT_MIN = 4, CNT_MAX = 64, CNT_DIV = 2;
    private static readonly float[] GP = [0f, 1f];

    private float _r = R0, _sp = SP0;
    private int _cnt = CNT0;
    private float[]? _cos, _sin, _a;

    private static readonly IReadOnlyDictionary<RenderQuality, QS> _pre = new Dictionary<RenderQuality, QS>
    {
        [RenderQuality.Low] = new(false, false, 0.1f, 0.15f),
        [RenderQuality.Medium] = new(true, false, 0.2f, 0.2f),
        [RenderQuality.High] = new(true, true, 0.3f, 0.25f)
    };

    public sealed record QS(bool Grad, bool HQ, float Sm, float Rsp);

    protected override IReadOnlyDictionary<RenderQuality, QS> QualitySettingsPresets => _pre;
    protected override int GetSpectrumProcessingCount(RenderParameters rp) => rp.EffectiveBarCount;
    protected override int GetMaxBarsForQuality() => GetBarsForQuality(32, 64, 128);

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();
        SetProcessingSmoothingFactor(CurrentQualitySettings!.Sm);
        EnsureArrays();
        PrecomputeTrig();
        RequestRedraw();
    }

    protected override void OnDispose()
    {
        _cos = _sin = _a = null;
        _r = R0; _sp = SP0; _cnt = CNT0;
        base.OnDispose();
    }

    protected override void RenderEffect(
        SKCanvas c, float[] s, SKImageInfo info, RenderParameters rp, SKPaint paint)
    {
        if (CurrentQualitySettings is not { } qs || s.Length == 0) return;

        UpdateConfig(rp.EffectiveBarCount, rp.BarSpacing, info);
        UpdateAlpha(s, qs);

        int n = Min(s.Length, _cnt);
        if (n <= 0) return;

        float maxR = Min(info.Width, info.Height) * 0.5f - _r - _sp;
        if (maxR <= 0f) return;

        float cx = info.Width * 0.5f, cy = info.Height * 0.5f;
        Grp[] gs = Groups(n);
        if (gs.Length == 0) return;

        if (UseAdvancedEffects && qs.Grad)
            DrawGradient(c, s, cx, cy, maxR, gs, paint.Color, qs);
        else
            DrawSolid(c, s, cx, cy, maxR, gs, paint.Color, qs);
    }

    private void DrawGradient(SKCanvas c, float[] s, float cx, float cy, float maxR, Grp[] gs, SKColor col, QS qs)
    {
        foreach (Grp g in gs)
        {
            if (g.A < MIN_A) continue;
            byte a = (byte)(255f * g.A);

            WithShader(col, Fill, () =>
                SKShader.CreateRadialGradient(new(0, 0), 1f, [col.WithAlpha(a), col.WithAlpha(0)], GP, SKShaderTileMode.Clamp),
                p =>
                {
                    if (qs.HQ) p.BlendMode = SKBlendMode.SrcOver;
                    DrawGroup(c, s, cx, cy, maxR, g, p, true);
                });
        }
    }

    private void DrawSolid(SKCanvas c, float[] s, float cx, float cy, float maxR, Grp[] gs, SKColor col, QS qs)
    {
        SKPaint p = CreatePaint(col, Fill);
        if (qs.HQ) p.BlendMode = SKBlendMode.SrcOver;

        try
        {
            foreach (Grp g in gs)
            {
                if (g.A < MIN_A) continue;
                p.Color = col.WithAlpha(CalculateAlpha(g.A));
                DrawGroup(c, s, cx, cy, maxR, g, p, false);
            }
        }
        finally { ReturnPaint(p); }
    }

    private void DrawGroup(SKCanvas c, float[] s, float cx, float cy, float maxR, Grp g, SKPaint p, bool transform)
    {
        if (_cos == null || _sin == null) return;

        for (int i = g.S; i < g.E && i < s.Length; i++)
        {
            float mag = s[i];
            if (mag < MIN_MAG || i >= _cos.Length) continue;

            float x = cx + _cos[i] * maxR, y = cy + _sin[i] * maxR;
            float sz = Max(mag * _r, MIN_SZ) + _sp * SP_MUL;

            if (!IsAreaVisible(c, new SKRect(x - sz, y - sz, x + sz, y + sz))) continue;

            if (transform)
            {
                int sc = c.Save();
                c.Translate(x, y);
                c.Scale(sz);
                c.DrawCircle(0, 0, 1f, p);
                c.RestoreToCount(sc);
            }
            else c.DrawCircle(x, y, sz, p);
        }
    }

    private Grp[] Groups(int n)
    {
        if (_a == null || n <= 0) return [];

        var list = new List<Grp>(MAX_G);
        int s = 0;
        float a = _a[0];

        for (int i = 1; i < n && i < _a.Length; i++)
        {
            if (Abs(_a[i] - a) > A_THR || list.Count >= MAX_G - 1)
            {
                list.Add(new(s, i, a));
                s = i;
                a = _a[i];
            }
        }
        list.Add(new(s, n, a));
        return [.. list];
    }

    private void UpdateAlpha(float[] s, QS qs)
    {
        if (_a == null) return;
        int n = Min(s.Length, _a.Length);
        for (int i = 0; i < n; i++)
        {
            float trg = Max(MIN_A, s[i] * INT_MUL);
            _a[i] = Lerp(_a[i], trg, qs.Rsp);
        }
    }

    private void UpdateConfig(int bc, float bs, SKImageInfo info)
    {
        float r = Max(5f, SelectByOverlay(R0, R0_OV) - bc * R_DEC + bs * R_SP);
        float sp = Max(2f, SelectByOverlay(SP0, SP0_OV) - bc * SP_DEC + bs * SP_SP);
        int n = Max(SelectByOverlay(CNT0, CNT0_OV), bc / CNT_DIV);

        float max = Min(info.Width, info.Height) * 0.5f - (r + sp);
        r = Max(R_MIN, Min(r, max));

        if (_r == r && _sp == sp && _cnt == n) return;
        _r = r; _sp = sp; _cnt = Clamp(n, CNT_MIN, CNT_MAX);

        EnsureArrays();
        PrecomputeTrig();
    }

    private void EnsureArrays()
    {
        EnsureArraySize(ref _cos, _cnt);
        EnsureArraySize(ref _sin, _cnt);
        EnsureArraySize(ref _a, _cnt, MIN_A);
    }

    private void PrecomputeTrig()
    {
        if (_cos == null || _sin == null) return;
        float step = 360f / _cnt * DEG;
        for (int i = 0; i < _cnt; i++)
        {
            float a = i * step;
            _cos[i] = MathF.Cos(a);
            _sin[i] = MathF.Sin(a);
        }
    }

    private readonly record struct Grp(int S, int E, float A);
}
