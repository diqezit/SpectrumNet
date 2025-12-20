namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class ConstellationRenderer : EffectSpectrumRenderer<ConstellationRenderer.QS>
{

    private const float
        BASE_SZ = 1.5f, MAX_SZ = 12f, MIN_BR = 0.2f, TW_SPD = 2f, MOV = 25f,
        SP_THR = 0.05f, SPEC_S = 18f, LIFE0 = 5f, LIFE1 = 15f, SPD0 = 0.5f, SPD1 = 2f,
        TW0 = 0.8f, TW1 = 0.4f, TW_A = 0.3f, EN_BR = 0.5f, EN_SZ = 0.5f,
        IN_T = 2f, OUT_T = 2f, SP_RATE0 = 5f, SP_RATE1 = 15f;

    private const int DEF_N = 360, OV_N = 120, MAX_SPF = 3, BANDS = 3;
    private const byte G_THR = 180, A_MIN = 10;

    private static readonly SKColor[] Low = [new(200, 100, 100), new(255, 200, 200)];
    private static readonly SKColor[] High = [new(100, 100, 200), new(200, 200, 255)];
    private static readonly SKColor[] Neu = [new(150, 150, 150), new(255, 255, 255)];

    private Star[] _st = [];
    private float _acc;
    private float _l, _m, _h, _e;

    private static readonly IReadOnlyDictionary<RenderQuality, QS> _pre = new Dictionary<RenderQuality, QS>
    {
        [RenderQuality.Low] = new(false, 1f, 0.4f),
        [RenderQuality.Medium] = new(true, 1.3f, 0.3f),
        [RenderQuality.High] = new(true, 1.5f, 0.25f)
    };

    public sealed record QS(bool Glow, float Gsz, float Sm);

    protected override IReadOnlyDictionary<RenderQuality, QS> QualitySettingsPresets => _pre;
    protected override int GetSpectrumProcessingCount(RenderParameters rp) => BANDS;
    protected override int GetMaxBarsForQuality() => GetBarsForQuality(32, 64, 128);

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();
        SetProcessingSmoothingFactor(CurrentQualitySettings!.Sm);
        int need = SelectByOverlay(DEF_N, OV_N);
        if (_st.Length != need) Init();
        RequestRedraw();
    }

    protected override void OnDispose()
    {
        _st = [];
        _acc = _l = _m = _h = _e = 0f;
        base.OnDispose();
    }

    protected override void RenderEffect(
        SKCanvas c, float[] s, SKImageInfo info, RenderParameters rp, SKPaint paint)
    {
        if (CurrentQualitySettings is not { } qs) return;

        Read(s);
        Upd(info);

        using Pooled<List<Act>> lp = RentList<Act>();
        List<Act> a = lp.Value;
        ColAct(a);

        if (a.Count == 0) return;

        if (UseAdvancedEffects && qs.Glow)
            Glow(c, a, qs);

        Draw(c, a);
    }

    private void Read(float[] s)
    {
        _l = s.Length > 0 ? s[0] : 0f;
        _m = s.Length > 1 ? s[1] : 0f;
        _h = s.Length > 2 ? s[2] : 0f;
        _e = (_l + _m + _h) / 3f;
    }

    private void Upd(SKImageInfo info)
    {
        _acc += (SP_RATE0 + _e * SP_RATE1) * DeltaTime;

        for (int i = 0; i < _st.Length; i++)
        {
            ref Star st = ref _st[i];
            if (!st.On) continue;
            st.L -= DeltaTime;
            st.T += DeltaTime;
            if (st.L <= 0f) { st.On = false; continue; }
            Move(ref st, info);
            Viz(ref st);
        }
        Spawn(info);
    }

    private void Move(ref Star st, SKImageInfo info)
    {
        float ang = st.T * st.Spd;
        float vx = (_l * MathF.Sin(ang) + _m * MathF.Cos(ang * 1.3f) + _h * MathF.Sin(ang * 1.8f)) * SPEC_S;
        float vy = (_l * MathF.Cos(ang) + _m * MathF.Sin(ang * 1.3f) + _h * MathF.Cos(ang * 1.8f)) * SPEC_S;
        st.X = Clamp(st.X + vx * DeltaTime * MOV, 0f, info.Width);
        st.Y = Clamp(st.Y + vy * DeltaTime * MOV, 0f, info.Height);
    }

    private void Viz(ref Star st)
    {
        float fi = st.T / IN_T;
        float fo = st.L / OUT_T;
        st.Op = Clamp(Min(fi, fo), 0f, 1f);
        float tw = MathF.Sin(st.T * TW_SPD * st.Tw + st.Ph) * TW_A;
        st.Br = Clamp(0.8f + tw + _e * EN_BR, MIN_BR, 1.5f);
    }

    private void Spawn(SKImageInfo info)
    {
        if (_acc < 1f || _e < SP_THR) return;
        int n = Min((int)_acc, MAX_SPF);
        _acc -= n;
        for (int i = 0; i < n; i++)
        {
            int idx = Free();
            if (idx < 0) break;
            InitStar(ref _st[idx], info);
        }
    }

    private int Free()
    {
        for (int i = 0; i < _st.Length; i++)
            if (!_st[i].On) return i;
        return -1;
    }

    private void InitStar(ref Star st, SKImageInfo info)
    {
        st.On = true;
        st.X = RandFloat() * info.Width;
        st.Y = RandFloat() * info.Height;
        st.L = st.L0 = RandFloat(LIFE0, LIFE1);
        st.Sz = RandFloat(BASE_SZ, MAX_SZ);
        st.Spd = RandFloat(SPD0, SPD1);
        st.Tw = RandFloat(TW1, TW0);
        st.Ph = RandFloat() * Tau;
        st.T = st.Op = 0f;
        st.Br = 0.8f;
        st.Col = Pick();
    }

    private SKColor Pick()
    {
        SKColor[] pal = _l > _m && _l > _h ? Low : _h > _l && _h > _m ? High : Neu;
        return InterpolateColor(pal[0], pal[1], RandFloat());
    }

    private void ColAct(List<Act> a)
    {
        foreach (ref readonly Star st in _st.AsSpan())
        {
            if (!st.On) continue;
            byte al = CalculateAlpha(st.Br * st.Op);
            if (al < A_MIN) continue;
            a.Add(new(new(st.X, st.Y), st.Sz, st.Col, al));
        }
    }

    private void Glow(SKCanvas c, List<Act> a, QS s)
    {
        SKPaint p = CreatePaint(SKColors.White, Fill);
        try
        {
            foreach (Act st in a)
            {
                if (st.A < G_THR) continue;
                p.Color = st.C.WithAlpha((byte)(st.A / 5));
                c.DrawCircle(st.P, st.Sz * s.Gsz, p);
            }
        }
        finally { ReturnPaint(p); }
    }

    private void Draw(SKCanvas c, List<Act> a)
    {
        float k = 1f + _e * EN_SZ;
        SKPaint p = CreatePaint(SKColors.White, Fill);
        try
        {
            foreach (Act st in a)
            {
                p.Color = st.C.WithAlpha(st.A);
                c.DrawCircle(st.P, st.Sz * k, p);
            }
        }
        finally { ReturnPaint(p); }
    }

    private void Init()
    {
        _st = new Star[SelectByOverlay(DEF_N, OV_N)];
        _acc = _l = _m = _h = _e = 0f;
    }

    private readonly record struct Act(SKPoint P, float Sz, SKColor C, byte A);

    private struct Star
    {
        public float X, Y, Sz, Br, Spd, Tw, L, L0, Op, Ph, T;
        public SKColor Col;
        public bool On;
    }
}
