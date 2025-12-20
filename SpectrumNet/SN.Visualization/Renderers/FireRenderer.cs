namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class FireRenderer : EffectSpectrumRenderer<FireRenderer.QS>
{
    private const float Px = 8f, PxOv = 5f, Spread = 0.4f, An = 0.06f, Wst = 0.25f, Wspd = 2.5f,
        MinI = 0.02f, Cool0 = 0.12f, CoolH = 0.4f, S2 = 0.9f, S3 = 0.75f, P1 = 0.95f, P2 = 0.8f,
        Rc = 0.5f, K0 = 4f, K1 = 8f, Im = 1.3f, Wvar = 0.1f;
    private const int H0 = 40, H0Ov = 28, Src = 4, MinW = 10;

    private static readonly SKColor[] Cols =
    [
        SKColors.Black, new(24, 0, 0), new(48, 0, 0), new(96, 0, 0), new(144, 0, 0),
        new(192, 0, 0), new(224, 0, 0), SKColors.Red, new(255, 48, 0), new(255, 96, 0),
        new(255, 144, 0), SKColors.Orange, new(255, 192, 0), new(255, 224, 0),
        SKColors.Yellow, new(255, 255, 192), SKColors.White
    ];

    private float[,] _fire = new float[0, 0];
    private float[,] _cool = new float[0, 0];
    private float[,] _tmp = new float[0, 0];
    private float _t;
    private int _w, _h;

    private static readonly IReadOnlyDictionary<RenderQuality, QS> _pre =
        new Dictionary<RenderQuality, QS>
        {
            [RenderQuality.Low] = new(0.06f, 0.25f, false, false, 8, 1.0f, 0.91f),
            [RenderQuality.Medium] = new(0.045f, 0.35f, true, true, 12, 1.15f, 0.93f),
            [RenderQuality.High] = new(0.035f, 0.45f, true, true, 17, 1.25f, 0.94f)
        };

    public sealed record QS(float Cr, float Sc, bool Sm, bool Wind, int Lv, float Boost, float Dec);

    protected override IReadOnlyDictionary<RenderQuality, QS> QualitySettingsPresets => _pre;

    protected override int GetMaxBarsForQuality() => GetBarsForQuality(50, 100, 150);

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();
        ApplyStandardQualitySmoothing();
        _fire = new float[0, 0];
        _cool = new float[0, 0];
        _tmp = new float[0, 0];
    }

    protected override void OnDispose()
    {
        _fire = new float[0, 0];
        _cool = new float[0, 0];
        _tmp = new float[0, 0];
        _t = 0f;
        _w = _h = 0;
        base.OnDispose();
    }

    protected override void RenderEffect(SKCanvas c, float[] s, SKImageInfo info, RenderParameters rp, SKPaint paint)
    {
        if (CurrentQualitySettings is not { } qs) return;

        _t += An;
        if (_t > Tau) _t -= Tau;

        int gh = SelectByOverlay(H0, H0Ov);
        float px = SelectByOverlay(Px, PxOv);
        int gw = Max(MinW, (int)(info.Width / px));

        Ens(gw, gh);
        Sim(s, rp, qs, px);
        Draw(c, gw, gh, px, info.Height, qs);
    }

    private void Ens(int w, int h)
    {
        if (_fire.GetLength(0) == w && _fire.GetLength(1) == h) return;

        _fire = new float[w, h];
        _cool = new float[w, h];
        _tmp = new float[w, h];

        _w = w;
        _h = h;

        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                float hf = (float)y / h;
                _cool[x, y] = RandFloat() * Cool0 + hf * CoolH + (float)Sin(x * 0.15f) * 0.08f;
            }
    }

    private void Sim(float[] s, RenderParameters rp, QS qs, float px)
    {
        SrcHeat(s, rp, qs, px);
        UpdHeat(qs);
        if (qs.Sm) Sm();
    }

    private void SrcHeat(float[] s, RenderParameters rp, QS qs, float px)
    {
        int n = Min(rp.EffectiveBarCount, s.Length);

        for (int i = 0; i < n; i++)
        {
            float v = s[i] * qs.Boost * Im;
            if (v < MinI) continue;

            float x0 = (rp.StartOffset + i * (rp.BarWidth + rp.BarSpacing)) / px;
            float x1 = x0 + rp.BarWidth / px;

            int a = Clamp((int)x0, 0, _w - 1);
            int b = Clamp((int)x1, 0, _w - 1);
            int y = _h - 1;

            for (int x = a; x <= b; x++)
            {
                _fire[x, y] = v;

                if (y > 0 && RandChance(P1)) _fire[x, y - 1] = v * S2;
                if (y > 1 && RandChance(P2)) _fire[x, y - 2] = v * S3;

                for (int r = 3; r < Src && y - r >= 0; r++)
                {
                    float p = P2 * (1f - r * 0.2f);
                    if (RandChance(p)) _fire[x, y - r] = v * (1f - r * 0.15f);
                }
            }
        }
    }

    private void UpdHeat(QS qs)
    {
        for (int y = 0; y < _h - Src; y++)
            for (int x = 0; x < _w; x++)
            {
                float cool = _cool[x, y] * qs.Cr;

                float wind = 0f;
                if (qs.Wind)
                {
                    float hf = 1f - (float)y / _h;
                    wind = (float)Sin(_t * Wspd + y * Wvar) * Wst * hf;
                }

                float heat = 0f;
                int cnt = 0;

                for (int dx = -1; dx <= 1; dx++)
                {
                    int sx = Clamp(x + dx + (int)wind, 0, _w - 1);
                    heat += _fire[sx, y + 1];
                    cnt++;
                }

                heat = cnt > 0 ? heat / cnt : 0f;
                heat *= qs.Dec;
                heat -= cool;

                if (RandChance(qs.Sc))
                {
                    float sp = (RandFloat() - Rc) * Spread;
                    heat += sp * heat;
                }

                _fire[x, y] = Clamp(heat, 0f, 1f);
            }
    }

    private void Sm()
    {
        for (int x = 1; x < _w - 1; x++)
            for (int y = 1; y < _h - 1; y++)
                _tmp[x, y] = (_fire[x, y] * K0 + _fire[x - 1, y] + _fire[x + 1, y] + _fire[x, y - 1] + _fire[x, y + 1]) / K1;

        for (int x = 1; x < _w - 1; x++)
            for (int y = 1; y < _h - 1; y++)
                _fire[x, y] = _tmp[x, y];
    }

    private void Draw(SKCanvas c, int w, int h, float px, float ch, QS qs)
    {
        WithPaint(SKColors.Red, Fill, p =>
        {
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                {
                    float v = _fire[x, y];
                    if (v < 0.01f) continue;

                    float nv = Clamp(v, 0f, 1f);
                    int i = Clamp((int)(nv * (qs.Lv - 1)), 0, Cols.Length - 1);

                    p.Color = Cols[i].WithAlpha(CalculateAlpha(nv));

                    float xx = x * px;
                    float yy = ch - (h - y) * px;

                    c.DrawRect(xx, yy, px, px, p);
                }
        });
    }
}
