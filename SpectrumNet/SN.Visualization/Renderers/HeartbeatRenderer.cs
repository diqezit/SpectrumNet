namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class HeartbeatRenderer : EffectSpectrumRenderer<HeartbeatRenderer.QS>
{

    private const float
        MIN_THR = 0.01f, P_F = 4f, P_A = 0.15f, H_S = 1.3f,
        R0_F = 0.02f, R1_F = 0.95f, G_I = 0.6f, G_I_OV = 0.4f,
        ANIM = 0.02f, SZ_M = 20f, SZ_M_OV = 15f, ROT = 6f, EXP = 0.25f,
        MIN_SP = 1.5f, DEC = 0.3f, POW = 0.85f, MAG_R = 0.15f, Y_OFF = 0.05f;

    private AnimState _anim;
    private readonly List<H> _hc = [];

    private static readonly IReadOnlyDictionary<RenderQuality, QS> _pre = new Dictionary<RenderQuality, QS>
    {
        [RenderQuality.Low] = new(false, 0f, 0, 0.5f, 2),
        [RenderQuality.Medium] = new(true, 8f, 25, 0.75f, 1),
        [RenderQuality.High] = new(true, 12f, 35, 1f, 0)
    };

    public sealed record QS(bool Glow, float GR, byte GA, float PI, int Simp);

    protected override IReadOnlyDictionary<RenderQuality, QS> QualitySettingsPresets => _pre;
    protected override int GetMaxBarsForQuality() => GetBarsForQuality(100, 200, 300);

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();
        ApplyStandardQualitySmoothing();
        _hc.Clear();
    }

    protected override void OnDispose()
    {
        _hc.Clear();
        _anim = default;
        base.OnDispose();
    }

    protected override void RenderEffect(
        SKCanvas c, float[] s, SKImageInfo info, RenderParameters rp, SKPaint paint)
    {
        if (CurrentQualitySettings is not { } qs) return;

        _anim.UpdatePhase(ANIM);

        SKPoint center = GetCenter(info);
        float maxR = GetMinDimension(info) * 0.48f;
        float szMul = SelectByOverlay(SZ_M, SZ_M_OV);

        _hc.Clear();

        for (int i = 0; i < rp.EffectiveBarCount; i++)
        {
            float mag = i < s.Length ? s[i] : 0.3f + (i % 3) * 0.1f;
            if (mag < MIN_THR && i < s.Length) continue;

            float prog = (float)i / rp.EffectiveBarCount;
            float ang = prog * Tau * ROT;

            float r = Lerp(maxR * R0_F, maxR * R1_F, MathF.Pow(prog, POW));
            r += prog * maxR * EXP;
            r *= 1f + mag * MAG_R;

            var pos = new SKPoint(
                center.X + r * MathF.Cos(ang),
                center.Y + r * MathF.Sin(ang) * (1f - Y_OFF));

            float p = 1f + MathF.Sin(_anim.Phase * P_F + ang) * P_A * mag * qs.PI;
            float sz = mag * szMul * p * (1f - prog * DEC);

            bool ok = true;
            foreach (H h in _hc)
            {
                float dx = pos.X - h.P.X;
                float dy = pos.Y - h.P.Y;
                if (MathF.Sqrt(dx * dx + dy * dy) < sz * MIN_SP) { ok = false; break; }
            }

            if (ok) _hc.Add(new H(pos, sz, mag));
        }

        if (_hc.Count == 0) return;

        if (UseAdvancedEffects && qs.Glow && qs.GA > 0)
        {
            float gi = SelectByOverlay(G_I, G_I_OV);
            WithBlur(paint.Color.WithAlpha(qs.GA), Fill, qs.GR, gp =>
            {
                foreach (H h in _hc) Heart(c, h.P, h.S * (1f + gi), gp, qs);
            });
        }

        foreach (H h in _hc)
            WithPaint(paint.Color.WithAlpha(CalculateAlpha(h.M)), Fill, p => Heart(c, h.P, h.S, p, qs));
    }

    private void Heart(SKCanvas c, SKPoint pos, float sz, SKPaint p, QS s)
    {
        if (s.Simp >= 2) { c.DrawCircle(pos, sz * 0.5f, p); return; }

        if (s.Simp == 1)
        {
            RenderPath(c, path =>
            {
                float r = sz * 0.5f;
                path.MoveTo(pos.X, pos.Y + r);
                path.CubicTo(pos.X - r, pos.Y, pos.X - r, pos.Y - r * 0.5f, pos.X, pos.Y);
                path.CubicTo(pos.X + r, pos.Y - r * 0.5f, pos.X + r, pos.Y, pos.X, pos.Y + r);
                path.Close();
            }, p);
            return;
        }

        RenderPath(c, path =>
        {
            float x = sz * H_S;
            path.MoveTo(pos.X, pos.Y + 0.3f * x);
            path.CubicTo(pos.X - 0.5f * x, pos.Y - 0.3f * x, pos.X - x, pos.Y + 0.1f * x, pos.X - x, pos.Y + 0.5f * x);
            path.CubicTo(pos.X - x, pos.Y + 0.9f * x, pos.X - 0.5f * x, pos.Y + 1.3f * x, pos.X, pos.Y + 1.8f * x);
            path.CubicTo(pos.X + 0.5f * x, pos.Y + 1.3f * x, pos.X + x, pos.Y + 0.9f * x, pos.X + x, pos.Y + 0.5f * x);
            path.CubicTo(pos.X + x, pos.Y + 0.1f * x, pos.X + 0.5f * x, pos.Y - 0.3f * x, pos.X, pos.Y + 0.3f * x);
            path.Close();
        }, p);
    }

    private readonly record struct H(SKPoint P, float S, float M);
}
