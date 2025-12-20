namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class TextParticlesRenderer : FontCachedRenderer<TextParticlesRenderer.QS>
{
    private const float Foc = 1000f, G = 9.81f, Air = 0.98f, Vmax = 15f,
        DirVar = 0.5f, DirCh = 0.05f, TxtSz = 14f, TxtSzOv = 12f, Amin = 0.05f,
        SpV = 5f, Life0 = 3f, Aexp = 2f, ThrN = 0.1f, ThrOv = 0.15f, SzN = 1f,
        SzOv = 0.8f, Zr = 500f, Zmin = -250f, OvHMul = 0.3f, Zblur = 2f,
        SpCd = 0.05f, IntSm = 0.85f, V0 = 5f, Vr = 10f, LifeV0 = 0.8f, LifeV1 = 1.2f, Marg = 50f;
    private const int Vlut = 1024, Alut = 101, MaxLo = 150, MaxMd = 400,
        MaxHi = 800, SpLo = 2, SpMd = 4, SpHi = 8, BlurLvl = 8;

    private static readonly string[] ChStr = ["0", "1"];

    private static readonly SKColor[] Cols = [new(0, 255, 0), new(0, 200, 255), new(255, 255, 0)];
    private static readonly float[] ColThr = [0.33f, 0.66f, 1.0f];

    private static readonly IReadOnlyDictionary<RenderQuality, QS> _pre =
        new Dictionary<RenderQuality, QS>
        {
            [RenderQuality.Low] = new(false, false, false, false, true, 2, MaxLo, 0.6f, 0.3f, SpLo),
            [RenderQuality.Medium] = new(true, true, true, false, false, 1, MaxMd, 0.8f, 0.5f, SpMd),
            [RenderQuality.High] = new(true, true, true, true, false, 0, MaxHi, 1f, 0.7f, SpHi)
        };

    public sealed record QS(bool Batch, bool AA, bool ColVar, bool ZBlur,
        bool Simple, int Cull, int MaxP, float Det, float Rate, int MaxSp);

    protected override IReadOnlyDictionary<RenderQuality, QS> QualitySettingsPresets => _pre;

    private float[] _vLut = [];
    private float[] _aLut = [];
    private float[] _spT = [];
    private float[] _sm = [];

    private P[] _ps = new P[MaxHi];
    private int _pc;

    private readonly SKMaskFilter?[] _blur = new SKMaskFilter?[BlurLvl];

    private float _w, _h, _step, _bw, _off;
    private float _y0, _y1;

    protected override RenderParameters CalculateRenderParameters(SKImageInfo info, int bc, float bw, float bs) =>
        CalcStandardRenderParams(info, bc, bw, bs, GetMaxBarsForQuality());
    protected override int GetSpectrumProcessingCount(RenderParameters rp) => rp.EffectiveBarCount;
    protected override int GetMaxBarsForQuality() => GetBarsForQuality(64, 128, 256);

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();
        SetProcessingSmoothingFactor(GetSmoothingForQuality(0.4f, 0.3f, 0.25f));

        for (int i = 0; i < _blur.Length; i++) { _blur[i]?.Dispose(); _blur[i] = null; }

        int max = CurrentQualitySettings?.MaxP ?? MaxMd;
        if (_pc > max) _pc = max;

        RequestRedraw();
    }

    protected override void CleanupUnusedResources()
    {
        base.CleanupUnusedResources();

        if (CurrentQualitySettings is not { } s) return;

        if (!UseAdvancedEffects || !s.ZBlur)
            for (int i = 0; i < _blur.Length; i++) { _blur[i]?.Dispose(); _blur[i] = null; }

        if (_ps.Length > s.MaxP * 2 && _pc <= s.MaxP)
            Array.Resize(ref _ps, s.MaxP);
    }

    protected override void OnDispose()
    {
        for (int i = 0; i < _blur.Length; i++) { _blur[i]?.Dispose(); _blur[i] = null; }

        _vLut = [];
        _aLut = [];
        _spT = [];
        _sm = [];
        _ps = [];
        _pc = 0;

        base.OnDispose();
    }

    protected override void RenderEffect(SKCanvas c, float[] spec, SKImageInfo info, RenderParameters rp, SKPaint paint)
    {
        if (CurrentQualitySettings is not { } s) return;
        if (rp.EffectiveBarCount <= 0 || rp.BarWidth <= 0f) return;

        EnsInit(rp.EffectiveBarCount, s);
        UpdCache(info, rp);
        UpdSm(spec);
        UpdPs(s);
        Spawn(spec, rp, s);
        Draw(c, s);
    }

    private void EnsInit(int bc, QS s)
    {
        if (_aLut.Length == 0 || _vLut.Length == 0) InitLut();
        EnsureFont(SelectByOverlay(TxtSz, TxtSzOv) * s.Det, s.AA);

        if (_spT.Length != bc) { _spT = new float[bc]; InitSpT(bc); }
        if (_sm.Length != bc) _sm = new float[bc];

        if (_ps.Length < s.MaxP) Array.Resize(ref _ps, s.MaxP);
        if (_pc > s.MaxP) _pc = s.MaxP;
    }

    private void InitLut()
    {
        _aLut = new float[Alut];
        for (int i = 0; i < _aLut.Length; i++) _aLut[i] = (float)Pow(i / (float)(_aLut.Length - 1), Aexp);

        _vLut = new float[Vlut];
        for (int i = 0; i < _vLut.Length; i++) _vLut[i] = V0 + Vr * i / Vlut;
    }

    private void InitSpT(int n)
    {
        for (int i = 0; i < n; i++) _spT[i] = RandFloat() * SpCd;
    }

    private void UpdCache(SKImageInfo info, RenderParameters rp)
    {
        _w = info.Width;
        _h = info.Height;

        _bw = rp.BarWidth;
        _off = rp.StartOffset;
        _step = rp.BarWidth + rp.BarSpacing;

        float ovH = _h * OvHMul;
        _y0 = IsOverlayActive ? _h - ovH : 0f;
        _y1 = _h;
    }

    private void UpdSm(float[] s)
    {
        int n = Min(s.Length, _sm.Length);
        for (int i = 0; i < n; i++) _sm[i] = Lerp(_sm[i], s[i], 1f - IntSm);
    }

    private void UpdPs(QS s)
    {
        int w = 0;
        for (int i = 0; i < _pc; i++)
        {
            P p = _ps[i];
            if (!Step(ref p, s)) continue;
            _ps[w++] = p;
        }
        _pc = w;
    }

    private bool Step(ref P p, QS s)
    {
        p.L -= DeltaTime;
        if (p.L <= 0f) return false;

        if (p.Y < _y0 - Marg || p.Y > _y1 + Marg || p.X < -Marg || p.X > _w + Marg) return false;

        p.Vy = (p.Vy + G * DeltaTime) * Air;
        p.Vy = Clamp(p.Vy, -Vmax, Vmax);

        if (RandChance(DirCh)) p.Vx += (RandFloat() - 0.5f) * DirVar;
        p.Vx *= Air;

        p.Y += p.Vy;
        p.X += p.Vx;

        p.A = Alpha(p.L / Life0);

        return (s.Cull < 2 || p.A >= Amin * 2f) && (s.Cull < 1 || p.A >= Amin);
    }

    private float Alpha(float r)
    {
        if (r <= 0f) return 0f;
        if (r >= 1f) return 1f;
        int i = Clamp((int)(r * 100f), 0, _aLut.Length - 1);
        return _aLut[i];
    }

    private void Spawn(float[] s, RenderParameters rp, QS qs)
    {
        if (_pc >= qs.MaxP) return;

        float thr = SelectByOverlay(ThrN, ThrOv);

        int made = 0;
        int n = Min(s.Length, _spT.Length);

        for (int i = 0; i < n; i++)
        {
            if (_pc >= qs.MaxP || made >= qs.MaxSp) break;

            _spT[i] -= DeltaTime;
            if (_spT[i] > 0f) continue;

            float inten = _sm[i];

            if (inten <= thr) { ResetSp(i, qs); continue; }

            float t = Clamp(inten / thr, 0f, 1f);

            if (!RandChance(t * qs.Rate)) { ResetSp(i, qs); continue; }

            _ps[_pc++] = MkP(i, rp, t, qs);
            made++;

            ResetSp(i, qs);
        }
    }

    private void ResetSp(int i, QS s)
    {
        float baseD = SpCd / Max(0.001f, s.Rate);
        float var = baseD * 0.5f;
        _spT[i] = baseD + RandFloat() * var;
    }

    private P MkP(int i, RenderParameters rp, float t, QS s)
    {
        float x = _off + i * _step + RandFloat() * _bw;
        x = Clamp(x, 0f, _w);

        float y = _y1 + RandFloat() * SpV - SpV * 0.5f;
        float z = Zmin + RandFloat() * Zr;

        float vy = -RndV() * Clamp(t, 0.5f, 2f);
        float vx = (RandFloat() - 0.5f) * 2f;

        float lv = LifeV0 + RandFloat() * (LifeV1 - LifeV0);
        float sz = SelectByOverlay(SzN, SzOv) * Clamp(t, 0.5f, 2f) * s.Det;

        byte ch = (byte)RandInt(2);
        SKColor col = s.ColVar ? PickCol(t) : Cols[0];

        return new P { X = x, Y = y, Z = z, Vx = vx, Vy = vy, Sz = sz, L = Life0 * lv, A = 1f, Ch = ch, C = col };
    }

    private float RndV() => _vLut[RandInt(_vLut.Length)];

    private static SKColor PickCol(float t)
    {
        float v = Clamp(t, 0f, 1f);
        for (int i = 0; i < ColThr.Length; i++) if (v <= ColThr[i]) return Cols[i];
        return Cols[^1];
    }

    private void Draw(SKCanvas c, QS s)
    {
        if (_pc == 0 || Font == null) return;

        SKFont f = Font;

        SKPaint p = CreatePaint(SKColors.White, Fill);
        p.IsAntialias = s.AA;

        try
        {
            float cx = _w * 0.5f;
            float cy = _h * 0.5f;
            float txt = SelectByOverlay(TxtSz, TxtSzOv);

            for (int i = 0; i < _pc; i++)
            {
                ref P pt = ref _ps[i];

                if (pt.A < Amin) continue;

                float d = Foc + pt.Z;
                if (d < 1f) d = 1f;

                float k = Foc / d;

                float sx = cx + (pt.X - cx) * k;
                float sy = cy + (pt.Y - cy) * k;

                float da = d / (Foc + Zr);
                byte a = (byte)Clamp(pt.A * da * 255f, 0f, 255f);
                if (a == 0) continue;

                float sz = pt.Sz * k;

                var bb = new SKRect(sx - sz, sy - sz, sx + sz, sy + sz);
                if (!IsAreaVisible(c, bb)) continue;

                p.Color = pt.C.WithAlpha(a);
                p.MaskFilter = UseAdvancedEffects && s.ZBlur && !s.Simple ? Blur(pt.Z) : null;

                f.Size = txt * k * s.Det;

                c.DrawText(ChStr[pt.Ch], sx, sy, SKTextAlign.Center, f, p);
            }

            p.MaskFilter = null;
        }
        finally
        {
            ReturnPaint(p);
        }
    }

    private SKMaskFilter? Blur(float z)
    {
        float zn = Clamp((z - Zmin) / Zr, 0f, 1f);
        float sig = zn * Zblur;

        int i = Clamp((int)(Round(zn * (BlurLvl - 1))), 0, BlurLvl - 1);

        if (_blur[i] != null) return _blur[i];
        if (sig <= 0.01f) return null;

        _blur[i] = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, sig);
        return _blur[i];
    }

    private struct P
    {
        public float X, Y, Z;
        public float Vx, Vy;
        public float Sz;
        public float L;
        public float A;
        public byte Ch;
        public SKColor C;
    }
}
