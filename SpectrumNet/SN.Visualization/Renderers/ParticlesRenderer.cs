namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class ParticlesRenderer : EffectSpectrumRenderer<ParticlesRenderer.QS>
{

    private const float
        DECAY = 0.98f, MIN_SZ = 1f, MAX_DENS = 3f, VEL_MIN = 2f, VEL_MAX = 10f, VEL_MUL = 1f,
        THR_OV = 0.15f, THR_NORM = 0.1f, SZ_OV = 4f, SZ_NORM = 6f, SPAWN_P = 0.8f,
        ALPHA_EXP = 2f, OV_H_MUL = 0.3f, OV_PAD = 10f;

    private const int VEL_LUT_SZ = 1024, ALPHA_LUT_SZ = 101, MAX_P_LO = 300, MAX_P_MD = 600, MAX_P_HI = 1000;

    private readonly List<P> _ps = [];
    private readonly Cache _cache = new();
    private float[]? _velLut, _alphaLut;

    private int MaxP => Min(ParticlesCfg.MaxParticles, CurrentQualitySettings?.MaxP ?? MAX_P_MD);
    private static float Life => ParticlesCfg.ParticleLife;
    private static float LifeDecay => ParticlesCfg.ParticleLifeDecay;

    private static readonly IReadOnlyDictionary<RenderQuality, QS> _pre = new Dictionary<RenderQuality, QS>
    {
        [RenderQuality.Low] = new(false, false, MAX_P_LO, 0.6f, 0.5f),
        [RenderQuality.Medium] = new(true, true, MAX_P_MD, 0.8f, 0.7f),
        [RenderQuality.High] = new(true, true, MAX_P_HI, 1f, 1f)
    };

    public sealed record QS(bool AA, bool Adv, int MaxP, float Detail, float SpawnR);

    protected override IReadOnlyDictionary<RenderQuality, QS> QualitySettingsPresets => _pre;

    protected override RenderParameters CalculateRenderParameters(SKImageInfo info, int bc, float bw, float bs) =>
        CalcStandardRenderParams(info, bc, bw, bs, GetMaxBarsForQuality());

    protected override int GetSpectrumProcessingCount(RenderParameters rp) => rp.EffectiveBarCount;
    protected override int GetMaxBarsForQuality() => GetBarsForQuality(64, 128, 256);

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();
        ApplyStandardQualitySmoothing();
        TrimList(_ps, MaxP);
        RequestRedraw();
    }

    protected override void CleanupUnusedResources()
    {
        base.CleanupUnusedResources();
        if (_ps.Count > MaxP * 2) _ps.RemoveRange(MaxP, _ps.Count - MaxP);
    }

    protected override void OnDispose()
    {
        _ps.Clear();
        _velLut = _alphaLut = null;
        base.OnDispose();
    }

    protected override void RenderEffect(
        SKCanvas c, float[] s, SKImageInfo info, RenderParameters rp, SKPaint paint)
    {
        if (CurrentQualitySettings is not { } qs) return;
        if (rp.EffectiveBarCount <= 0 || rp.BarWidth <= 0f) return;

        EnsureLuts();
        UpdateCache(info, rp);
        UpdateAndSpawn(s, rp, qs);
        Draw(c, paint, qs);
    }

    private void EnsureLuts()
    {
        if (_alphaLut == null)
        {
            _alphaLut = new float[ALPHA_LUT_SZ];
            for (int i = 0; i < _alphaLut.Length; i++)
                _alphaLut[i] = MathF.Pow(i / (float)(_alphaLut.Length - 1), ALPHA_EXP);
        }

        if (_velLut == null)
        {
            _velLut = new float[VEL_LUT_SZ];
            float range = VEL_MAX - VEL_MIN;
            for (int i = 0; i < VEL_LUT_SZ; i++)
                _velLut[i] = VEL_MIN + range * i / VEL_LUT_SZ;
        }
    }

    private void UpdateCache(SKImageInfo info, RenderParameters rp)
    {
        _cache.W = info.Width;
        _cache.H = info.Height;
        _cache.Off = rp.StartOffset;
        _cache.BW = rp.BarWidth;
        _cache.Step = rp.BarWidth + rp.BarSpacing;

        if (IsOverlayActive)
        {
            _cache.OvH = info.Height * OV_H_MUL;
            _cache.Top = OV_PAD;
            _cache.Bot = info.Height;
        }
        else
        {
            _cache.OvH = 0f;
            _cache.Top = 0f;
            _cache.Bot = info.Height;
        }
    }

    private void UpdateAndSpawn(float[] s, RenderParameters rp, QS qs)
    {
        float life = Life, decay = LifeDecay;

        for (int i = _ps.Count - 1; i >= 0; i--)
        {
            P p = _ps[i];
            p.Y -= p.V * VEL_MUL;
            p.L -= decay;

            if (p.L <= 0f || p.Y <= _cache.Top) { _ps.RemoveAt(i); continue; }

            p.S *= DECAY;
            if (p.S < MIN_SZ) { _ps.RemoveAt(i); continue; }

            p.A = Alpha(p.L / life);
            _ps[i] = p;
        }

        Spawn(s, rp, qs);
    }

    private float Alpha(float r)
    {
        if (r <= 0f) return 0f;
        if (r >= 1f) return 1f;
        return _alphaLut is { Length: > 0 } a ? a[Clamp((int)(r * 100f), 0, a.Length - 1)] : MathF.Pow(r, ALPHA_EXP);
    }

    private void Spawn(float[] s, RenderParameters rp, QS qs)
    {
        int max = MaxP;
        if (_ps.Count >= max) return;

        float thr = SelectByOverlay(THR_NORM, THR_OV);
        float sz0 = SelectByOverlay(SZ_NORM, SZ_OV);
        float life = Life;

        for (int i = 0; i < s.Length && _ps.Count < max; i++)
        {
            float mag = s[i];
            if (mag <= thr) continue;

            float inten = mag / thr;
            float ch = Clamp(inten, 0f, 1f) * SPAWN_P * qs.SpawnR;

            if (!RandChance(ch)) continue;

            float dens = Clamp(inten, 1f, MAX_DENS);
            float x = Clamp(rp.StartOffset + i * _cache.Step + RandFloat() * rp.BarWidth, 0f, _cache.W);
            float vel = _velLut is { Length: > 0 } v ? v[RandInt(VEL_LUT_SZ)] : VEL_MIN;

            _ps.Add(new P { X = x, Y = _cache.Bot, V = vel * dens, S = sz0 * dens * qs.Detail, L = life, A = 1f });
        }
    }

    private void Draw(SKCanvas c, SKPaint basePaint, QS qs)
    {
        SKPaint p = CreatePaint(basePaint.Color, Fill);
        p.IsAntialias = qs.AA;

        try
        {
            foreach (P pt in _ps)
            {
                if (pt.A <= 0f || pt.S <= 0f) continue;
                if (pt.Y < _cache.Top || pt.Y > _cache.Bot) continue;
                p.Color = basePaint.Color.WithAlpha((byte)(pt.A * 255f));
                c.DrawCircle(pt.X, pt.Y, pt.S * 0.5f, p);
            }
        }
        finally { ReturnPaint(p); }
    }

    private struct P { public float X, Y, V, S, L, A; }
    private sealed class Cache { public float W, H, Off, BW, Step, OvH, Top, Bot; }
}
