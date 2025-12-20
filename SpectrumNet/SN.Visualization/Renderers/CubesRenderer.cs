namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class CubesRenderer : EffectSpectrumRenderer<CubesRenderer.QS>
{

    private static readonly IReadOnlyDictionary<RenderQuality, QS> _pre = new Dictionary<RenderQuality, QS>
    {
        [RenderQuality.Low] = new(false, false, 0.7f, 0.2f, 1f, 0.7f, false, 0f, 0),
        [RenderQuality.Medium] = new(true, true, 0.75f, 0.25f, 1.2f, 0.6f, true, 2f, 20),
        [RenderQuality.High] = new(true, true, 0.8f, 0.3f, 1.3f, 0.5f, true, 3f, 30)
    };

    public sealed record QS(
        bool Top, bool Side, float Tw, float Th, float Tb, float Sb, bool Sh, float ShOff, byte ShA);

    protected override IReadOnlyDictionary<RenderQuality, QS> QualitySettingsPresets => _pre;

    protected override RenderParameters CalculateRenderParameters(
        SKImageInfo info, int bc, float bw, float bs) =>
        CalcStandardRenderParams(info, bc, bw, bs, GetMaxBarsForQuality());

    protected override int GetSpectrumProcessingCount(RenderParameters rp) => rp.EffectiveBarCount;
    protected override int GetMaxBarsForQuality() => GetBarsForQuality(80, 150, 250);

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();
        SetProcessingSmoothingFactor(GetSmoothingForQuality(0.35f, 0.25f, 0.2f));
        RequestRedraw();
    }

    protected override void RenderEffect(
        SKCanvas c, float[] s, SKImageInfo info, RenderParameters rp, SKPaint paint)
    {
        if (CurrentQualitySettings is not { } qs) return;
        if (rp.EffectiveBarCount <= 0 || rp.BarWidth <= 0f) return;

        using Pooled<List<Cd>> lp = RentList<Cd>();
        List<Cd> a = lp.Value;

        int n = Min(rp.EffectiveBarCount, s.Length);
        float x = rp.StartOffset;

        for (int i = 0; i < n; i++)
        {
            float m = Max(0f, s[i]);
            if (m >= 0.01f)
            {
                float h = m * info.Height * 0.8f;
                float y = info.Height - h;
                a.Add(new(x, y, rp.BarWidth, h, m));
            }
            x += rp.BarWidth + rp.BarSpacing;
        }

        if (a.Count == 0) return;

        if (UseAdvancedEffects && qs.Sh && qs.ShA > 0)
            DrawShadow(c, a, qs);

        DrawFront(c, a, paint.Color);

        if (UseAdvancedEffects && qs.Side)
            DrawSide(c, a, paint.Color, qs);

        if (UseAdvancedEffects && qs.Top)
            DrawTop(c, a, paint.Color, qs);
    }

    private void DrawShadow(SKCanvas c, List<Cd> a, QS s)
    {
        float off = s.ShOff;
        WithPaint(new SKColor(0, 0, 0, s.ShA), Fill, p =>
            RenderPath(c, path =>
            {
                foreach (Cd q in a)
                    path.AddRect(new SKRect(q.X + off, q.Y + off, q.X + q.W + off, q.Y + q.H + off));
            }, p));
    }

    private void DrawFront(SKCanvas c, List<Cd> a, SKColor col)
    {
        SKPaint p = CreatePaint(col, Fill);
        try
        {
            foreach (Cd q in a)
            {
                p.Color = col.WithAlpha(CalculateAlpha(q.M));
                c.DrawRect(new SKRect(q.X, q.Y, q.X + q.W, q.Y + q.H), p);
            }
        }
        finally { ReturnPaint(p); }
    }

    private void DrawSide(SKCanvas c, List<Cd> a, SKColor baseC, QS s)
    {
        SKColor col = AdjustBrightness(baseC, s.Sb);
        WithPaint(col, Fill, p =>
            RenderPath(c, path =>
            {
                foreach (Cd q in a) SideP(path, q, s);
            }, p));
    }

    private void DrawTop(SKCanvas c, List<Cd> a, SKColor baseC, QS s)
    {
        SKColor col = AdjustBrightness(baseC, s.Tb);
        WithPaint(col, Fill, p =>
            RenderPath(c, path =>
            {
                foreach (Cd q in a) TopP(path, q, s);
            }, p));
    }

    private static void SideP(SKPath p, Cd q, QS s)
    {
        float to = q.W * s.Tw;
        float th = q.W * s.Th;
        p.MoveTo(q.X + q.W, q.Y);
        p.LineTo(q.X + q.W, q.Y + q.H);
        p.LineTo(q.X + to, q.Y - th + q.H);
        p.LineTo(q.X + to, q.Y - th);
        p.Close();
    }

    private static void TopP(SKPath p, Cd q, QS s)
    {
        float to = q.W * s.Tw;
        float th = q.W * s.Th;
        float bo = q.W - to;
        p.MoveTo(q.X, q.Y);
        p.LineTo(q.X + q.W, q.Y);
        p.LineTo(q.X + to, q.Y - th);
        p.LineTo(q.X - bo, q.Y - th);
        p.Close();
    }

    private readonly record struct Cd(float X, float Y, float W, float H, float M);
}
