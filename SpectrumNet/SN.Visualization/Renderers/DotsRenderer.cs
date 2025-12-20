namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class DotsRenderer : EffectSpectrumRenderer<DotsRenderer.QS>
{

    private const float
        R0 = 4f, R1 = 1.5f, R2 = 8f, R_M = 0.8f,
        V0 = 80f, V1 = 120f, DAMP = 0.95f, INF = 2f, V_INF = 1.5f,
        A0 = 0.85f, G_R = 0.3f, G_A = 0.6f, B_D = 0.5f, CEN = 0.5f,
        G0 = 0.5f, G1 = 2f, A1 = 0.3f, A2 = 1f, A_OFF = 0.3f, G_SIG = 2f, VIS_R = 0.5f;

    private const int BATCH = 64;

    private Dot[] _d = [];
    private DimCache _dims;
    private float _mx, _gm = 1f;
    private readonly List<int> _vis = [];

    private static readonly IReadOnlyDictionary<RenderQuality, QS> _pre = new Dictionary<RenderQuality, QS>
    {
        [RenderQuality.Low] = new(75, false, 0.2f, 0.4f, 0.7f),
        [RenderQuality.Medium] = new(150, true, G_R, G_A, 1f),
        [RenderQuality.High] = new(300, true, 0.5f, 0.8f, 1.3f)
    };

    public sealed record QS(int N, bool Glow, float Gr, float Ga, float Sp);

    protected override IReadOnlyDictionary<RenderQuality, QS> QualitySettingsPresets => _pre;
    protected override int GetMaxBarsForQuality() => GetBarsForQuality(75, 150, 300);

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();
        ApplyStandardQualitySmoothing();
        if (_dims.W > 0 && _dims.H > 0) Reset();
    }

    protected override void OnDispose()
    {
        _d = [];
        _vis.Clear();
        _dims = default;
        _mx = 0f;
        _gm = 1f;
        base.OnDispose();
    }

    protected override void RenderEffect(
        SKCanvas c, float[] s, SKImageInfo info, RenderParameters rp, SKPaint paint)
    {
        if (CurrentQualitySettings is not { } qs) return;

        if (_dims.Changed(info) || _d.Length != qs.N) Reset();

        _mx = CalculateMaxSpectrum(s);
        _gm = Clamp(1f + _mx * R_M, G0, G1);

        Move(s);
        UpdateVis();

        if (_vis.Count == 0) return;

        byte a = Alpha(_mx);

        if (UseAdvancedEffects && qs.Glow)
            DrawGlow(c, a, qs);

        DrawDots(c, a);
    }

    private void Reset()
    {
        int n = CurrentQualitySettings!.N;
        _d = new Dot[n];
        float sp = CurrentQualitySettings.Sp;

        for (int i = 0; i < n; i++)
        {
            _d[i] = new Dot(
                new(RandFloat() * _dims.FW, RandFloat() * _dims.FH),
                new((RandFloat() - 0.5f) * V0 * sp, (RandFloat() - 0.5f) * V0 * sp),
                RandFloat(R1, R2),
                RandFloat(R1, R2),
                new((byte)RandInt(180, 255), (byte)RandInt(100, 180), (byte)RandInt(50, 100)));
        }
        _vis.Clear();
        _vis.Capacity = n;
    }

    private void Move(float[] s)
    {
        int sn = s.Length;
        float sp = CurrentQualitySettings!.Sp;

        for (int i = 0; i < _d.Length; i++)
        {
            int si = sn > 0 ? Clamp(i * sn / _d.Length, 0, sn - 1) : 0;
            float sv = sn > 0 ? s[si] : 0f;

            Dot d = _d[i];
            float nx = d.P.X / _dims.FW;
            float ny = d.P.Y / _dims.FH;

            float gx = (CEN - nx) * V0 * sp;
            float gy = (CEN - ny) * V0 * sp;
            float f = sv * INF * V1;
            float fx = (nx - CEN) * f;
            float fy = (ny - CEN) * f;

            float vx = (d.V.X + (gx + fx) * DeltaTime) * DAMP;
            float vy = (d.V.Y + (gy + fy) * DeltaTime) * DAMP;

            float x = d.P.X + vx * DeltaTime;
            float y = d.P.Y + vy * DeltaTime;

            if (x < 0f) { x = 0f; vx = -vx * B_D; }
            else if (x > _dims.FW) { x = _dims.FW; vx = -vx * B_D; }
            if (y < 0f) { y = 0f; vy = -vy * B_D; }
            else if (y > _dims.FH) { y = _dims.FH; vy = -vy * B_D; }

            float r = d.B * (1f + sv * V_INF) * _gm;
            _d[i] = d with { P = new(x, y), V = new(vx, vy), R = r };
        }
    }

    private void UpdateVis()
    {
        _vis.Clear();
        for (int i = 0; i < _d.Length; i++)
            if (_d[i].R >= VIS_R) _vis.Add(i);
    }

    private static byte Alpha(float mx) =>
        CalculateAlpha(A0 * Clamp(mx + A_OFF, A1, A2));

    private void DrawGlow(SKCanvas c, byte a, QS qs)
    {
        if (qs.Ga == 0f) return;

        byte ga = (byte)(qs.Ga * a);
        float sig = R0 * qs.Gr * G_SIG;

        using var mf = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, sig);
        SKPaint p = CreatePaint(SKColors.White, Fill);
        p.MaskFilter = mf;

        try
        {
            for (int i = 0; i < _vis.Count; i += BATCH)
            {
                int end = Min(i + BATCH, _vis.Count);
                for (int j = i; j < end; j++)
                {
                    Dot d = _d[_vis[j]];
                    p.Color = d.C.WithAlpha(ga);
                    c.DrawCircle(d.P, d.R * (1f + qs.Gr), p);
                }
            }
        }
        finally { p.MaskFilter = null; ReturnPaint(p); }
    }

    private void DrawDots(SKCanvas c, byte a)
    {
        SKPaint p = CreatePaint(SKColors.White, Fill);
        try
        {
            for (int i = 0; i < _vis.Count; i += BATCH)
            {
                int end = Min(i + BATCH, _vis.Count);
                for (int j = i; j < end; j++)
                {
                    Dot d = _d[_vis[j]];
                    p.Color = d.C.WithAlpha(a);
                    c.DrawCircle(d.P, d.R, p);
                }
            }
        }
        finally { ReturnPaint(p); }
    }

    private readonly record struct Dot(SKPoint P, SKPoint V, float R, float B, SKColor C);
}
