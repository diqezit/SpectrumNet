namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class RippleRenderer : EffectSpectrumRenderer<RippleRenderer.QS>
{

    private const float
        R_MAX = 300f, SPD = 150f, SPAWN_T = 0.1f, W0 = 3f, DIST0 = 100f, R_MUL = 200f,
        FADE_T = 0.8f, FADE_R = 0.2f, ROT_SPD = 0.5f, SAT0 = 80f, SATR = 20f,
        VAL0 = 70f, VALR = 30f, H_DEG = 360f, H_WRAP = 1f, MIN_MAG = 0.15f, ANG_STEP = 45f;

    private const int BANDS = 8, MAX_LO = 20, MAX_MD = 40, MAX_HI = 60;

    private readonly List<Rip> _rs = new(MAX_HI);
    private readonly float[] _b = new float[BANDS];
    private AnimState _anim;

    private static readonly IReadOnlyDictionary<RenderQuality, QS> _pre = new Dictionary<RenderQuality, QS>
    {
        [RenderQuality.Low] = new(MAX_LO, W0 * 0.8f, SPAWN_T * 2f, false, false),
        [RenderQuality.Medium] = new(MAX_MD, W0, SPAWN_T, true, true),
        [RenderQuality.High] = new(MAX_HI, W0 * 1.2f, SPAWN_T * 0.8f, true, true)
    };

    public sealed record QS(int Max, float W, float Rate, bool Rot, bool Adapt);

    protected override IReadOnlyDictionary<RenderQuality, QS> QualitySettingsPresets => _pre;
    protected override int GetSpectrumProcessingCount(RenderParameters rp) => Max(BANDS, rp.EffectiveBarCount);
    protected override int GetMaxBarsForQuality() => GetBarsForQuality(32, 64, 128);

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();
        ApplyStandardQualitySmoothing();
        RequestRedraw();
    }

    protected override void OnDispose()
    {
        _rs.Clear();
        _anim = default;
        base.OnDispose();
    }

    protected override void RenderEffect(
        SKCanvas c, float[] s, SKImageInfo info, RenderParameters rp, SKPaint paint)
    {
        if (CurrentQualitySettings is not { } qs) return;

        _anim.AddTime(DeltaTime);
        ResampleSpectrumAvg(s, _b);
        UpdateRipples(DeltaTime);
        SpawnRipples(info, qs);

        if (!IsAreaVisible(c, new SKRect(0, 0, info.Width, info.Height))) return;
        Draw(c, qs);
    }

    private void UpdateRipples(float dt)
    {
        float step = SPD * dt;
        for (int i = _rs.Count - 1; i >= 0; i--)
        {
            Rip r = _rs[i];
            float nr = r.R + step;
            if (nr > r.MaxR) _rs.RemoveAt(i);
            else _rs[i] = r with { R = nr };
        }
    }

    private void SpawnRipples(SKImageInfo info, QS qs)
    {
        if (!_anim.Tick(qs.Rate, DeltaTime)) return;
        if (_rs.Count >= qs.Max) return;

        float cx = info.Width * 0.5f, cy = info.Height * 0.5f;

        for (int i = 0; i < BANDS && _rs.Count < qs.Max; i++)
        {
            float mag = _b[i];
            if (mag < MIN_MAG) continue;

            float ang = i * ANG_STEP * (MathF.PI / 180f);
            float dist = DIST0 + mag * DIST0;
            float maxR = qs.Adapt ? R_MAX + mag * R_MUL : R_MAX;

            float hue = qs.Rot
                ? (_anim.Phase + i / (float)BANDS) % H_WRAP
                : (i / (float)BANDS) % H_WRAP;

            _rs.Add(new(cx + MathF.Cos(ang) * dist, cy + MathF.Sin(ang) * dist, 0f, maxR, mag, hue));
        }

        if (qs.Rot)
        {
            _anim.Phase += DeltaTime * ROT_SPD;
            if (_anim.Phase > H_WRAP) _anim.Phase -= H_WRAP;
        }
    }

    private void Draw(SKCanvas c, QS qs)
    {
        if (_rs.Count == 0) return;

        SKPaint p = CreateStrokePaint(SKColors.White, qs.W);
        try
        {
            foreach (Rip r in _rs)
            {
                float a = GetAlpha(r);
                if (a <= 0f) continue;
                p.Color = GetColor(r).WithAlpha(CalculateAlpha(a));
                c.DrawCircle(r.X, r.Y, r.R, p);
            }
        }
        finally { ReturnPaint(p); }
    }

    private static float GetAlpha(Rip r)
    {
        float prog = r.MaxR <= 0f ? 1f : r.R / r.MaxR;
        float a = r.Mag;
        if (prog > FADE_T) a *= 1f - (prog - FADE_T) / FADE_R;
        return Clamp(a, 0f, 1f);
    }

    private static SKColor GetColor(Rip r) =>
        SKColor.FromHsv(r.H * H_DEG, SAT0 + r.Mag * SATR, VAL0 + r.Mag * VALR);

    private readonly record struct Rip(float X, float Y, float R, float MaxR, float Mag, float H);
}
