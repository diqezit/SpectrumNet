namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class RainbowRenderer : EffectSpectrumRenderer<RainbowRenderer.QS>
{

    private const float
        A_MUL = 1.7f, R = 8f, G_THR = 0.4f, REF_THR = 0.2f, REF_MUL = 0.3f,
        HI_W = 0.6f, HI_X = 0.2f, HI_H_MAX = 10f, REF_H_MUL = 0.1f,
        H0 = 240f, HR = 240f, V0 = 90f, VR = 10f, S = 100f, H_WRAP = 360f, MIN_MAG = 0.01f,
        G_INT_OV = 0.2f, G_R_OV = 3f, HI_A_OV = 0.3f, REF_A_OV = 0.1f;

    private static readonly IReadOnlyDictionary<RenderQuality, QS> _pre = new Dictionary<RenderQuality, QS>
    {
        [RenderQuality.Low] = new(false, false, false, 0f, 0f, 0f, 0f),
        [RenderQuality.Medium] = new(true, true, false, 0.2f, 3f, 0.15f, 0.4f),
        [RenderQuality.High] = new(true, true, true, 0.3f, 5f, 0.2f, 0.5f)
    };

    public sealed record QS(bool Glow, bool Hi, bool Refl, float GInt, float GR, float ROp, float HAl);

    protected override IReadOnlyDictionary<RenderQuality, QS> QualitySettingsPresets => _pre;

    protected override RenderParameters CalculateRenderParameters(SKImageInfo info, int bc, float bw, float bs) =>
        CalcStandardRenderParams(info, bc, bw, bs, GetMaxBarsForQuality());

    protected override int GetSpectrumProcessingCount(RenderParameters rp) => rp.EffectiveBarCount;
    protected override int GetMaxBarsForQuality() => GetBarsForQuality(100, 200, 300);

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

        using Pooled<List<Bar>> lb = RentList<Bar>();
        List<Bar> bars = lb.Value;
        bars.Capacity = Max(bars.Capacity, rp.EffectiveBarCount);

        float x = rp.StartOffset;
        var bounds = new BoundsBuilder();
        int n = Min(rp.EffectiveBarCount, s.Length);

        for (int i = 0; i < n; i++)
        {
            float mag = s[i];
            if (mag < MIN_MAG) { x += rp.BarWidth + rp.BarSpacing; continue; }

            float h = Clamp(mag, 0f, 1f) * info.Height;
            var r = new SKRect(x, info.Height - h, x + rp.BarWidth, info.Height);
            bars.Add(new(r, mag, GetColor(mag)));
            bounds.Add(r);
            x += rp.BarWidth + rp.BarSpacing;
        }

        if (!bounds.HasBounds || !IsAreaVisible(c, bounds.Bounds)) return;

        if (UseAdvancedEffects && qs.Glow) DrawGlow(c, bars, qs);
        DrawMain(c, bars, qs);
        if (UseAdvancedEffects && qs.Refl) DrawReflection(c, bars, info.Height, qs);
    }

    private void DrawGlow(SKCanvas c, List<Bar> bars, QS qs)
    {
        float gr = SelectByOverlay(qs.GR, G_R_OV);
        float gi = SelectByOverlay(qs.GInt, G_INT_OV);
        if (gr <= 0f || gi <= 0f) return;

        WithImageBlur(SKColors.White, Fill, gr, gr, p =>
        {
            byte a = (byte)Clamp(gi * 255f, 0f, 255f);
            foreach (Bar b in bars)
            {
                if (b.M <= G_THR) continue;
                p.Color = b.C.WithAlpha(a);
                c.DrawRoundRect(b.R, R, R, p);
            }
        });
    }

    private void DrawMain(SKCanvas c, List<Bar> bars, QS qs)
    {
        SKPaint p = CreatePaint(SKColors.White, Fill);
        try
        {
            foreach (Bar b in bars)
            {
                p.Color = b.C.WithAlpha(CalculateAlpha(b.M * A_MUL));
                c.DrawRoundRect(b.R, R, R, p);

                if (UseAdvancedEffects && qs.Hi && b.R.Height > R * 2f)
                    DrawHighlight(c, b, qs);
            }
        }
        finally { ReturnPaint(p); }
    }

    private void DrawHighlight(SKCanvas c, Bar b, QS qs)
    {
        float ha = SelectByOverlay(qs.HAl, HI_A_OV);
        byte a = (byte)Clamp(b.M * ha * 255f, 0f, 255f);
        WithPaint(SKColors.White.WithAlpha(a), Fill, p =>
            c.DrawRect(b.R.Left + b.R.Width * HI_X, b.R.Top, b.R.Width * HI_W, Min(HI_H_MAX, R), p));
    }

    private void DrawReflection(SKCanvas c, List<Bar> bars, float h, QS qs)
    {
        float op = SelectByOverlay(qs.ROp, REF_A_OV);
        if (op <= 0f) return;

        SKPaint p = CreatePaint(SKColors.White, Fill);
        p.BlendMode = SKBlendMode.SrcOver;

        try
        {
            foreach (Bar b in bars)
            {
                if (b.M <= REF_THR) continue;
                p.Color = b.C.WithAlpha((byte)Clamp(b.M * op * 255f, 0f, 255f));
                float rh = Min(b.R.Height * REF_MUL, h * REF_H_MUL);
                c.DrawRect(b.R.Left, h, b.R.Width, rh, p);
            }
        }
        finally { ReturnPaint(p); }
    }

    private static SKColor GetColor(float v)
    {
        float t = Clamp(v, 0f, 1f);
        float hue = H0 - HR * t;
        if (hue < 0f) hue += H_WRAP;
        return SKColor.FromHsv(hue, S, V0 + t * VR);
    }

    private readonly record struct Bar(SKRect R, float M, SKColor C);
}
