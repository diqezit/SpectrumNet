namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class WaveformRenderer : WaveformRenderer<WaveformRenderer.QS>
{
    private const float Cy = 0.5f, Hmul = 0.85f, Wmin = 2.5f, WminOv = 3.5f,
        Wdiv = 80f, WdivOv = 60f, FillA = 0.35f, FillAOv = 0.2f, FillF = 0.4f,
        FillFOv = 0.3f, OutA = 0.15f, OutAOv = 0.1f, OutW = 1.5f, OutWOv = 1.2f,
        MirA = 0.3f, ShX = 1.5f, ShY = 1.5f, ShSig = 2.0f, ShA = 0.25f, AccT = 0.65f,
        AccA = 0.6f, AccAOv = 0.4f, AccR = 5f, AccROv = 3.5f, AccGr = 2.0f, AccGa = 0.3f;
    private const int AccN = 30, AccNOv = 20;

    private static readonly float[] GPos = [0f, 1f];
    private readonly SKColor[] _gc = new SKColor[2];

    private static readonly IReadOnlyDictionary<RenderQuality, QS> _pre =
        new Dictionary<RenderQuality, QS>
        {
            [RenderQuality.Low] = new(false, true, false, false, false, false, false, false, 0.9f, 1, 0.8f, 0.7f),
            [RenderQuality.Medium] = new(true, true, true, true, false, true, false, false, 1.0f, 2, 1.0f, 1.0f),
            [RenderQuality.High] = new(true, true, true, true, true, true, true, true, 1.1f, 3, 1.1f, 1.2f)
        };

    public sealed record QS(bool Cubic, bool Fill, bool GradFill, bool Acc, bool AccGlow,
        bool Out, bool Mir, bool Shadow, float WMul, int Def, float FillMul, float AccGlowMul);

    protected override IReadOnlyDictionary<RenderQuality, QS> QualitySettingsPresets => _pre;

    protected override int GetMaxBarsForQuality() => GetBarsForQuality(128, 256, 512);

    protected override RenderParameters CalculateRenderParameters(SKImageInfo info, int bc, float bw, float bs)
    {
        int req = Clamp(bc, 2, GetMaxBarsForQuality());
        RenderParameters rp = CalcStandardRenderParams(info, req, bw, bs, GetMaxBarsForQuality());
        if (rp.EffectiveBarCount < 2) rp = new RenderParameters(2, rp.BarWidth, rp.BarSpacing, rp.StartOffset);
        return rp;
    }

    protected override int GetSpectrumProcessingCount(RenderParameters rp) => rp.EffectiveBarCount;

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();
        SetProcessingSmoothingFactor(GetSmoothingForQuality(0.35f, 0.28f, 0.22f, ovMul: 1.25f));
        RequestRedraw();
    }

    protected override void RenderEffect(SKCanvas c, float[] s, SKImageInfo info, RenderParameters rp, SKPaint paint)
    {
        if (CurrentQualitySettings is not { } qs) return;
        if (rp.EffectiveBarCount < 2 || rp.BarWidth <= 0f) return;

        float mid = info.Height * Cy;
        float amp = Min(mid, info.Height - mid) * Hmul;

        BuildWaveformPaths(s, rp, mid, amp, cubic: qs.Cubic, cubicStep: Step(qs.Def));

        SKRect bb = GetWaveformBounds();
        if (!IsAreaVisible(c, bb)) return;

        if (qs.Fill) DrawFill(c, paint.Color, mid, amp, qs);
        if (UseAdvancedEffects && qs.Shadow && !IsOverlayActive) DrawShadow(c, qs);
        if (UseAdvancedEffects && qs.Out) DrawOut(c, paint.Color, qs);

        DrawMain(c, paint.Color, qs);

        if (UseAdvancedEffects && qs.Mir && !IsOverlayActive) DrawMir(c, paint.Color, mid, qs);
        if (UseAdvancedEffects && qs.Acc) DrawAcc(c, s, rp, mid, amp, paint.Color, qs);
    }

    private static float Step(int d) => d switch { 1 => 0.5f, 2 => 0.33f, 3 => 0.25f, _ => 0.33f };

    private float Ww(QS s)
    {
        float minW = SelectByOverlay(Wmin, WminOv);
        float div = SelectByOverlay(Wdiv, WdivOv);
        float n = Max(2f, TopPath?.PointCount ?? 2);
        float w = Max(minW, div / n);
        return w * s.WMul;
    }

    private void DrawFill(SKCanvas c, SKColor col, float mid, float amp, QS s)
    {
        float a0 = SelectByOverlay(FillA, FillAOv);
        float a = Clamp(a0 * s.FillMul, 0f, 1f);

        if (!s.GradFill)
        {
            WithPaint(col.WithAlpha((byte)Clamp(255f * a, 0f, 255f)), Fill, p => c.DrawPath(FillPath!, p));
            return;
        }

        float f = SelectByOverlay(FillF, FillFOv);
        byte a1 = (byte)Clamp(255f * a, 0f, 255f);
        byte a2 = (byte)Clamp(255f * a * f, 0f, 255f);

        _gc[0] = col.WithAlpha(a1);
        _gc[1] = col.WithAlpha(a2);

        WithShader(col, Fill, () =>
            SKShader.CreateLinearGradient(new SKPoint(0f, mid - amp),
            new SKPoint(0f, mid + amp), _gc, GPos, SKShaderTileMode.Clamp),
            p => c.DrawPath(FillPath!, p));
    }

    private void DrawShadow(SKCanvas c, QS s)
    {
        byte a = (byte)Clamp(255f * ShA, 0f, 255f);

        WithBlur(SKColors.Black.WithAlpha(a), Stroke, ShSig, p =>
        {
            p.StrokeWidth = Ww(s) * 1.1f;
            p.StrokeCap = SKStrokeCap.Round;
            p.StrokeJoin = SKStrokeJoin.Round;

            int sc = c.Save();
            c.Translate(ShX, ShY);

            c.DrawPath(TopPath!, p);
            c.DrawPath(BottomPath!, p);

            c.RestoreToCount(sc);
        });
    }

    private void DrawOut(SKCanvas c, SKColor col, QS s)
    {
        float a0 = SelectByOverlay(OutA, OutAOv);
        float wm = SelectByOverlay(OutW, OutWOv);

        SKPaint p = CreateStrokePaint(col.WithAlpha((byte)Clamp(255f * a0, 0f, 255f)), Ww(s) * wm);
        p.StrokeCap = SKStrokeCap.Round;
        p.StrokeJoin = SKStrokeJoin.Round;

        try { c.DrawPath(TopPath!, p); c.DrawPath(BottomPath!, p); }
        finally { ReturnPaint(p); }
    }

    private void DrawMain(SKCanvas c, SKColor col, QS s)
    {
        SKPaint p = CreateStrokePaint(col, Ww(s));
        p.StrokeCap = SKStrokeCap.Round;
        p.StrokeJoin = SKStrokeJoin.Round;

        try { c.DrawPath(TopPath!, p); c.DrawPath(BottomPath!, p); }
        finally { ReturnPaint(p); }
    }

    private void DrawMir(SKCanvas c, SKColor col, float mid, QS s)
    {
        SKPaint p = CreateStrokePaint(col.WithAlpha((byte)Clamp(255f * MirA, 0f, 255f)), Ww(s) * 0.8f);
        p.StrokeCap = SKStrokeCap.Round;
        p.StrokeJoin = SKStrokeJoin.Round;

        try
        {
            int sc = c.Save();
            c.Scale(1f, -1f, 0f, mid);

            c.DrawPath(TopPath!, p);
            c.DrawPath(BottomPath!, p);

            c.RestoreToCount(sc);
        }
        finally
        {
            ReturnPaint(p);
        }
    }

    private void DrawAcc(SKCanvas c, float[] s, RenderParameters rp, float mid, float amp, SKColor col, QS qs)
    {
        int max = SelectByOverlay(AccN, AccNOv);
        if (max <= 0) return;

        float a0 = SelectByOverlay(AccA, AccAOv);
        float r0 = SelectByOverlay(AccR, AccROv);

        byte a = (byte)Clamp(255f * a0, 0f, 255f);
        SKPaint dot = CreatePaint(col.WithAlpha(a), Fill);

        float step = rp.BarWidth + rp.BarSpacing;

        try
        {
            if (qs.AccGlow)
            {
                float ga = Clamp(AccGa * qs.AccGlowMul, 0f, 1f);
                byte gA = (byte)Clamp(255f * ga, 0f, 255f);
                float sig = r0 * AccGr * 0.5f;

                WithBlur(col.WithAlpha(gA), Fill, sig, glow => Acc(c, s, rp, step, mid, amp, r0, max, dot, glow));
            }
            else
            {
                Acc(c, s, rp, step, mid, amp, r0, max, dot, null);
            }
        }
        finally
        {
            ReturnPaint(dot);
        }
    }

    private static void Acc(SKCanvas c, float[] s, RenderParameters rp, float step,
        float mid, float amp, float r0, int max, SKPaint dot, SKPaint? glow)
    {
        int n = Min(rp.EffectiveBarCount, s.Length);
        int k = 0;

        for (int i = 0; i < n; i++)
        {
            if (k >= max) break;

            float m = s[i];
            if (m <= AccT) continue;

            float x = rp.StartOffset + i * step + rp.BarWidth * 0.5f;

            float t = Normalize(m, AccT, 1f);
            float r = r0 * t;

            float cl = Clamp(m, 0f, 1f);
            float y0 = mid - cl * amp;
            float y1 = mid + cl * amp;

            if (glow != null)
            {
                float gr = r * AccGr;
                c.DrawCircle(x, y0, gr, glow);
                c.DrawCircle(x, y1, gr, glow);
            }

            c.DrawCircle(x, y0, r, dot);
            c.DrawCircle(x, y1, r, dot);

            k++;
        }
    }
}
