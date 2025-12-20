namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class BarsRenderer : EffectSpectrumRenderer<BarsRenderer.QS>
{

    private static readonly IReadOnlyDictionary<RenderQuality, QS> _pre = new Dictionary<RenderQuality, QS>
    {
        [RenderQuality.Low] = new(false, true, false, false, 0.3f, 0f, 0f, 0, 0, 0f),
        [RenderQuality.Medium] = new(true, true, false, true, 0.4f, 0f, 2f, 0, 30, 0.3f),
        [RenderQuality.High] = new(true, true, true, true, 0.45f, 1f, 3f, 70, 40, 0.4f)
    };

    public sealed record QS(
        bool Grad, bool Round, bool Bord, bool Sh,
        float Rr, float Bw, float ShOff, byte Ba, byte Sa, float Gi);

    protected override IReadOnlyDictionary<RenderQuality, QS> QualitySettingsPresets => _pre;
    protected override int GetMaxBarsForQuality() => GetBarsForQuality(100, 200, 300);

    protected override RenderParameters CalculateRenderParameters(
        SKImageInfo info, int bc, float bw, float bs) =>
        CalcStandardRenderParams(info, bc, bw, bs, GetMaxBarsForQuality());

    protected override int GetSpectrumProcessingCount(RenderParameters rp) => rp.EffectiveBarCount;

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();
        ApplyStandardQualitySmoothing();
    }

    protected override void RenderEffect(
        SKCanvas c, float[] s, SKImageInfo info, RenderParameters rp, SKPaint paint)
    {
        if (CurrentQualitySettings is not { } qs) return;
        if (rp.EffectiveBarCount <= 0 || rp.BarWidth <= 0f) return;

        int n = Min(rp.EffectiveBarCount, s.Length);
        if (n <= 0) return;

        float st = rp.BarWidth + rp.BarSpacing;
        float x = rp.StartOffset;
        float minH = UseAdvancedEffects ? 2f : 1f;
        float rr = qs.Round ? rp.BarWidth * qs.Rr : 0f;

        using Pooled<SKPath> pp = RentPath();
        SKPath path = pp.Value;

        var bounds = new BoundsBuilder();

        for (int i = 0; i < n; i++)
        {
            float m = Max(0f, s[i]);
            SKRect r = GetBarRect(x, m, rp.BarWidth, info.Height, minH);
            if (rr > 0f) path.AddRoundRect(r, rr, rr);
            else path.AddRect(r);
            bounds.Add(r);
            x += st;
        }

        if (!bounds.HasBounds || !IsAreaVisible(c, bounds.Bounds)) return;

        if (UseAdvancedEffects && qs.Sh && qs.Sa > 0)
            DrawShadow(c, path, qs);

        if (UseAdvancedEffects && qs.Grad && qs.Gi > 0f)
            DrawGradient(c, path, bounds.Bounds, paint.Color, qs);
        else
            DrawSolid(c, path, paint.Color);

        if (qs.Bord && qs.Ba > 0 && qs.Bw > 0f)
            DrawBorder(c, path, qs);
    }

    private void DrawShadow(SKCanvas c, SKPath path, QS qs)
    {
        WithPaint(new SKColor(0, 0, 0, qs.Sa), Fill, p =>
        {
            int sc = c.Save();
            c.Translate(qs.ShOff, qs.ShOff);
            c.DrawPath(path, p);
            c.RestoreToCount(sc);
        });
    }

    private void DrawGradient(SKCanvas c, SKPath path, SKRect bb, SKColor col, QS qs)
    {
        SKColor top = col;
        SKColor bot = col.WithAlpha(CalculateAlpha(1f - qs.Gi));
        WithShader(col, Fill, () => CreateVerticalGradient(bb, top, bot), p => c.DrawPath(path, p));
    }

    private void DrawSolid(SKCanvas c, SKPath path, SKColor col)
    {
        WithPaint(col, Fill, p => c.DrawPath(path, p));
    }

    private void DrawBorder(SKCanvas c, SKPath path, QS qs)
    {
        SKPaint p = CreateStrokePaint(new SKColor(255, 255, 255, qs.Ba), qs.Bw);
        try { c.DrawPath(path, p); }
        finally { ReturnPaint(p); }
    }
}
