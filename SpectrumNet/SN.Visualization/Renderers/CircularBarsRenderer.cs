namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class CircularBarsRenderer : CircularRenderer<CircularBarsRenderer.QS>
{

    private static readonly IReadOnlyDictionary<RenderQuality, QS> _pre =
        new Dictionary<RenderQuality, QS>
        {
            [RenderQuality.Low] = new(false, false, 0f, 0f, 0.3f, 0.7f, 0.02f),
            [RenderQuality.Medium] = new(true, false, 0.4f, 0f, 0.4f, 0.75f, 0.03f),
            [RenderQuality.High] = new(true, true, 0.6f, 0.5f, 0.5f, 0.8f, 0.04f)
        };

    public sealed record QS(
        bool Glow,
        bool Hi,
        float Gi,
        float HiI,
        float InA,
        float Sp,
        float MinLen);

    protected override IReadOnlyDictionary<RenderQuality, QS> QualitySettingsPresets => _pre;

    protected override int GetMaxBarsForQuality() => GetBarsForQuality(64, 128, 256);

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();
        ApplyStandardQualitySmoothing();
    }

    protected override void RenderEffect(
        SKCanvas c,
        float[] s,
        SKImageInfo info,
        RenderParameters rp,
        SKPaint paint)
    {
        if (CurrentQualitySettings is not { } qs) return;
        if (rp.EffectiveBarCount <= 0) return;

        SKPoint cen = GetCenter(info);
        float rad = Min(cen.X, cen.Y) * 0.8f;

        int n = rp.EffectiveBarCount;
        EnsureDirections(n);

        if (UseAdvancedEffects)
            DrawInner(c, cen, rad, paint.Color, qs);

        float bw = BarW(n, rad, qs);

        RenderLayers(
            glow: UseAdvancedEffects && qs.Glow && qs.Gi > 0f
                ? () => DrawGlow(c, s, cen, rad, bw, paint.Color, qs)
                : null,
            main: () => DrawMain(c, s, cen, rad, bw, paint.Color, qs),
            hi: UseAdvancedEffects && qs.Hi && qs.HiI > 0f
                ? () => DrawHi(c, s, cen, rad, bw, qs)
                : null);
    }

    private static float BarW(int n, float rad, QS s)
    {
        if (n <= 0) return 2f;

        float c = Tau * rad;
        float w = c / n * s.Sp;

        return Clamp(w, 2f, 20f);
    }

    private void DrawInner(SKCanvas c, SKPoint cen, float rad, SKColor col, QS s)
    {
        byte a = CalculateAlpha(s.InA);

        SKPaint p = CreateStrokePaint(col.WithAlpha(a), 2f);
        try { c.DrawCircle(cen, rad * 0.9f, p); }
        finally { ReturnPaint(p); }
    }

    private void DrawGlow(SKCanvas c, float[] s, SKPoint cen, float rad, float bw, SKColor col, QS qs)
    {
        byte a = CalculateAlpha(qs.Gi);

        WithBlur(col.WithAlpha(a), Stroke, 3f, p =>
        {
            p.StrokeWidth = bw * 1.5f;
            p.StrokeCap = SKStrokeCap.Round;
            DrawBars(c, s, cen, rad, p, qs, thr: 0.6f);
        });
    }

    private void DrawMain(SKCanvas c, float[] s, SKPoint cen, float rad, float bw, SKColor col, QS qs)
    {
        SKPaint p = CreateStrokePaint(col, bw);
        try { DrawBars(c, s, cen, rad, p, qs, thr: 0f); }
        finally { ReturnPaint(p); }
    }

    private void DrawHi(SKCanvas c, float[] s, SKPoint cen, float rad, float bw, QS qs)
    {
        byte a = CalculateAlpha(qs.HiI);

        SKPaint p = CreateStrokePaint(SKColors.White.WithAlpha(a), bw * 0.5f);
        try { DrawBars(c, s, cen, rad, p, qs, thr: 0.4f, so: 0.7f); }
        finally { ReturnPaint(p); }
    }

    private void DrawBars(SKCanvas c, float[] s, SKPoint cen, float rad, SKPaint p, QS qs, float thr, float so = 0f)
    {
        int lim = Min(DirectionCount, s.Length);
        if (lim <= 0) return;

        RenderPath(c, path =>
        {
            for (int i = 0; i < lim; i++)
            {
                float m = s[i];
                if (m <= thr) continue;

                float len = Max(m * rad * 0.5f, rad * qs.MinLen);
                float inR = rad + (so > 0f ? len * so : 0f);
                float outR = rad + len;

                SKPoint d = GetDirection(i);

                path.MoveTo(cen.X + inR * d.X, cen.Y + inR * d.Y);
                path.LineTo(cen.X + outR * d.X, cen.Y + outR * d.Y);
            }
        }, p);
    }
}
