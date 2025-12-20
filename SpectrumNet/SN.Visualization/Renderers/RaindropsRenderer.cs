namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class RaindropsRenderer : EffectSpectrumRenderer<RaindropsRenderer.QS>
{

    private const float
        G = 900f, SP_INT = 0.05f, DT_MIN = 0.001f, DT_MAX = 0.033f,
        R0 = 3f, R0_OV = 2.5f, T_A_MUL = 160f, T_L_MUL = 0.08f, T_W_MUL = 0.7f,
        SPL_MIN = 0.2f, SPL_REB = 0.35f, SPL_VY0 = -180f, SPL_VY1 = -80f, SPL_VX = 160f,
        THR_N = 0.3f, THR_OV = 0.4f, SP_P = 0.5f;

    private const int MAX_D = 200, MAX_P = 500, MAX_SPF = 3;

    private readonly Stopwatch _sw = new();
    private float _dt = DeltaTime, _acc;
    private readonly List<D> _ds = new(MAX_D);
    private readonly List<P> _ps = new(MAX_P);

    private static readonly IReadOnlyDictionary<RenderQuality, QS> _pre = new Dictionary<RenderQuality, QS>
    {
        [RenderQuality.Low] = new(false, false, 0.3f, 0.5f),
        [RenderQuality.Medium] = new(true, true, 0.5f, 0.7f),
        [RenderQuality.High] = new(true, true, 0.7f, 1f)
    };

    public sealed record QS(bool Trails, bool Hi, float TOp, float HInt);

    protected override IReadOnlyDictionary<RenderQuality, QS> QualitySettingsPresets => _pre;
    protected override int GetSpectrumProcessingCount(RenderParameters rp) => rp.EffectiveBarCount;
    protected override int GetMaxBarsForQuality() => GetBarsForQuality(50, 100, 150);

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();
        ApplyStandardQualitySmoothing();
        RequestRedraw();
    }

    protected override void OnDispose()
    {
        _ds.Clear();
        _ps.Clear();
        _sw.Stop();
        base.OnDispose();
    }

    protected override void RenderEffect(
        SKCanvas c, float[] s, SKImageInfo info, RenderParameters rp, SKPaint paint)
    {
        if (CurrentQualitySettings is not { } qs) return;
        if (rp.EffectiveBarCount <= 0 || rp.BarWidth <= 0f) return;

        if (!_sw.IsRunning) _sw.Restart();
        UpdateDeltaTime();

        float bot = info.Height;
        UpdateDrops(bot);
        UpdateParticles(bot);
        SpawnDrops(s, info.Width, rp);

        DrawParticles(c, paint.Color);
        DrawDrops(c, paint.Color, qs);
    }

    private void UpdateDeltaTime()
    {
        float el = (float)_sw.Elapsed.TotalSeconds;
        _sw.Restart();
        _dt = Clamp(DeltaTime * (el / DeltaTime), DT_MIN, DT_MAX);
        _acc += _dt;
    }

    private void UpdateDrops(float bot)
    {
        for (int i = _ds.Count - 1; i >= 0; i--)
        {
            D d = _ds[i];
            d.Vy += G * _dt;
            d.Y += d.Vy * _dt;

            if (d.Y >= bot)
            {
                if (d.I >= SPL_MIN) SpawnSplash(d.X, bot, d.I);
                _ds.RemoveAt(i);
                continue;
            }
            _ds[i] = d;
        }
        TrimList(_ds, Min(RaindropsCfg.MaxRaindrops, MAX_D));
    }

    private void UpdateParticles(float bot)
    {
        for (int i = _ps.Count - 1; i >= 0; i--)
        {
            P p = _ps[i];
            p.Vy += G * _dt;
            p.X += p.Vx * _dt;
            p.Y += p.Vy * _dt;

            if (p.Y >= bot) { p.Y = bot; p.Vy *= -SPL_REB; p.Vx *= 0.7f; }
            p.L -= _dt * 0.9f;

            if (p.L <= 0f) { _ps.RemoveAt(i); continue; }
            _ps[i] = p;
        }
        TrimList(_ps, MAX_P);
    }

    private void SpawnDrops(float[] s, float w, RenderParameters rp)
    {
        if (_acc < SP_INT) return;
        _acc = 0f;

        float thr = SelectByOverlay(THR_N, THR_OV);
        float step = rp.BarWidth + rp.BarSpacing;
        int lim = Min(RaindropsCfg.MaxRaindrops, MAX_D);
        int sp = 0, n = Min(Min(s.Length, rp.EffectiveBarCount), lim);

        for (int i = 0; i < n; i++)
        {
            if (_ds.Count >= lim || sp >= MAX_SPF) break;
            float mag = s[i];
            if (mag <= thr) continue;

            float chance = SP_P * Clamp(mag / thr, 0f, 1f);
            if (!RandChance(chance)) continue;

            float x = Clamp(rp.StartOffset + i * step + RandFloat() * rp.BarWidth, 0f, w);
            float vy = RaindropsCfg.BaseFallSpeed * (1f + mag * 2f);
            _ds.Add(new D { X = x, Y = 0f, Vy = vy, I = mag });
            sp++;
        }
    }

    private void SpawnSplash(float x, float y, float intensity)
    {
        int n = Clamp((int)(3 + intensity * 6), 3, 10);
        for (int k = 0; k < n && _ps.Count < MAX_P; k++)
        {
            float vx = (RandFloat() * 2f - 1f) * SPL_VX;
            float vy = Lerp(SPL_VY0, SPL_VY1, RandFloat());
            _ps.Add(new P
            {
                X = x,
                Y = y,
                Vx = vx,
                Vy = vy,
                L = Clamp(0.4f + intensity * 0.8f, 0.3f, 1.2f),
                S = Clamp(1.5f + intensity * 2f, 1.5f, 4f)
            });
        }
    }

    private void DrawParticles(SKCanvas c, SKColor col)
    {
        if (_ps.Count == 0) return;
        SKPaint p = CreatePaint(col, Fill);
        try
        {
            foreach (P pt in _ps)
            {
                p.Color = col.WithAlpha((byte)Clamp(pt.L * 255f, 0f, 255f));
                c.DrawCircle(pt.X, pt.Y, pt.S, p);
            }
        }
        finally { ReturnPaint(p); }
    }

    private void DrawDrops(SKCanvas c, SKColor col, QS qs)
    {
        if (_ds.Count == 0) return;

        float r0 = SelectByOverlay(R0, R0_OV);
        SKPaint? tp = UseAdvancedEffects && qs.Trails ? CreateStrokePaint(col, r0 * T_W_MUL) : null;
        SKPaint dp = CreatePaint(col, Fill);
        SKPaint? hp = UseAdvancedEffects && qs.Hi ? CreatePaint(SKColors.White, Fill) : null;

        try
        {
            foreach (D d in _ds)
            {
                if (tp != null && d.I > 0.3f)
                {
                    byte a = (byte)Clamp(d.I * qs.TOp * T_A_MUL, 0f, 255f);
                    tp.Color = col.WithAlpha(a);
                    float len = Max(2f, d.Vy * T_L_MUL);
                    c.DrawLine(d.X, d.Y, d.X, d.Y - len, tp);
                }

                dp.Color = col.WithAlpha(CalculateAlpha(0.7f + d.I * 0.3f));
                c.DrawCircle(d.X, d.Y, r0 + d.I * 2f, dp);

                if (hp != null && d.I > 0.4f)
                {
                    byte a = (byte)Clamp(120f * d.I * qs.HInt, 0f, 255f);
                    hp.Color = SKColors.White.WithAlpha(a);
                    c.DrawCircle(d.X - r0 * 0.25f, d.Y - r0 * 0.25f, r0 * 0.35f, hp);
                }
            }
        }
        finally
        {
            ReturnPaint(dp);
            if (tp != null) ReturnPaint(tp);
            if (hp != null) ReturnPaint(hp);
        }
    }

    private struct D { public float X, Y, Vy, I; }
    private struct P { public float X, Y, Vx, Vy, L, S; }
}
