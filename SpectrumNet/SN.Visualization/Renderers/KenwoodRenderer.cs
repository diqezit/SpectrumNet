namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class KenwoodBarsRenderer : EffectSpectrumRenderer<KenwoodBarsRenderer.QS>
{

    private const float
        PK_FALL = 0.25f, PK_H = 3f, PK_H_OV = 2f, HOLD_MS = 300f,
        MIN_H = 2f, MIN_MAG = 0.01f, RR = 0.25f, RR_OV = 0.2f,
        OUT_W = 1.5f, OUT_W_OV = 1f, OUT_A = 0.5f, OUT_A_OV = 0.35f,
        P_OUT_A = 0.7f, P_OUT_A_OV = 0.5f, G_MUL = 1.1f, G_MUL_OV = 0.95f;

    private static readonly SKColor[] B_COL =
    [
        new(0, 240, 120), new(0, 255, 0), new(255, 235, 0),
        new(255, 185, 0), new(255, 85, 0), new(255, 35, 0)
    ];

    private static readonly float[] B_POS = [0f, 0.55f, 0.55f, 0.8f, 0.8f, 1f];
    private static readonly SKColor P_COL = SKColors.White;

    private PeakTracker[]? _pk;
    private readonly float _hold = HOLD_MS / 1000f;
    private SKShader? _sh;
    private float _shH;

    private static readonly IReadOnlyDictionary<RenderQuality, QS> _pre = new Dictionary<RenderQuality, QS>
    {
        [RenderQuality.Low] = new(true, false, false, false),
        [RenderQuality.Medium] = new(true, true, true, true),
        [RenderQuality.High] = new(true, true, true, true)
    };

    public sealed record QS(bool Grad, bool Round, bool Out, bool PeakFx);

    protected override IReadOnlyDictionary<RenderQuality, QS> QualitySettingsPresets => _pre;
    protected override int GetMaxBarsForQuality() => GetBarsForQuality(50, 75, 100);

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();
        ApplyStandardQualitySmoothing();
        InvalidateShader(0f);
    }

    protected override void OnDispose()
    {
        _pk = null;
        _sh?.Dispose();
        _sh = null;
        _shH = 0f;
        base.OnDispose();
    }

    protected override void RenderEffect(
        SKCanvas c, float[] s, SKImageInfo info, RenderParameters rp, SKPaint paint)
    {
        if (CurrentQualitySettings is not { } qs) return;

        InvalidateShader(info.Height);

        int n = Min(rp.EffectiveBarCount, s.Length);
        if (n <= 0 || rp.BarWidth <= 0f) return;

        _pk = EnsurePeakTrackers(ref _pk, n, reset: false);

        using Pooled<List<(SKRect r, float m)>> lb = RentList<(SKRect r, float m)>();
        using Pooled<List<SKRect>> lp = RentList<SKRect>();

        List<(SKRect r, float m)> bars = lb.Value;
        List<SKRect> peaks = lp.Value;

        float x = rp.StartOffset;
        float ph = SelectByOverlay(PK_H, PK_H_OV);

        var bounds = new BoundsBuilder();

        for (int i = 0; i < n; i++)
        {
            float mag = Max(0f, s[i]);
            _pk[i].Update(mag, _hold, DeltaTime, PK_FALL);

            if (mag > MIN_MAG)
            {
                SKRect r = GetBarRect(x, mag, rp.BarWidth, info.Height, MIN_H);
                bars.Add((r, mag));
                bounds.Add(r);
            }

            float pv = _pk[i].Value;
            if (pv > MIN_MAG)
            {
                float py = info.Height - pv * info.Height;
                var pr = new SKRect(x, Max(0, py - ph), x + rp.BarWidth, Max(0, py));
                if (pr.Height > 0f) { peaks.Add(pr); bounds.Add(pr); }
            }
            x += rp.BarWidth + rp.BarSpacing;
        }

        if (!bounds.HasBounds || !IsAreaVisible(c, bounds.Bounds)) return;

        float rr = qs.Round ? rp.BarWidth * SelectByOverlay(RR, RR_OV) : 0f;

        DrawBars(c, bars, info.Height, rr, qs);

        if (UseAdvancedEffects && qs.Out)
            DrawOutline(c, bars, rr);

        DrawPeaks(c, peaks, rr, qs);

        if (UseAdvancedEffects && qs.PeakFx)
            DrawPeakFx(c, peaks);
    }

    private void DrawBars(SKCanvas c, List<(SKRect r, float m)> bars, float h, float rr, QS s)
    {
        if (bars.Count == 0) return;

        if (s.Grad)
        {
            float mul = SelectByOverlay(G_MUL, G_MUL_OV);
            SKShader sh = GetShader(h, mul);

            WithPaint(SKColors.White, Fill, sh, p =>
                RenderPath(c, path =>
                {
                    foreach ((SKRect r, _) in bars)
                        if (rr > 0f) path.AddRoundRect(r, rr, rr);
                        else path.AddRect(r);
                }, p));
            return;
        }

        WithPaint(B_COL[0], Fill, p =>
            RenderPath(c, path =>
            {
                foreach ((SKRect r, _) in bars)
                    if (rr > 0f) path.AddRoundRect(r, rr, rr);
                    else path.AddRect(r);
            }, p));
    }

    private void DrawOutline(SKCanvas c, List<(SKRect r, float m)> bars, float rr)
    {
        float w = SelectByOverlay(OUT_W, OUT_W_OV);
        float a = SelectByOverlay(OUT_A, OUT_A_OV);

        SKPaint p = CreateStrokePaint(SKColors.White, w);
        try
        {
            foreach ((SKRect r, float m) in bars)
            {
                p.Color = SKColors.White.WithAlpha((byte)(CalculateAlpha(m) * a));
                if (rr > 0f) c.DrawRoundRect(r, rr, rr, p);
                else c.DrawRect(r, p);
            }
        }
        finally { ReturnPaint(p); }
    }

    private void DrawPeaks(SKCanvas c, List<SKRect> peaks, float rr, QS s)
    {
        if (peaks.Count == 0) return;

        WithPaint(P_COL, Fill, p =>
        {
            if (s.Round)
            {
                float pr = rr * 0.5f;
                RenderPath(c, path =>
                {
                    foreach (SKRect pk in peaks) path.AddRoundRect(pk, pr, pr);
                }, p);
            }
            else
                RenderRects(c, peaks, p, 0f);
        });
    }

    private void DrawPeakFx(SKCanvas c, List<SKRect> peaks)
    {
        if (peaks.Count == 0) return;

        float w = SelectByOverlay(OUT_W, OUT_W_OV) * 0.75f;
        float a = SelectByOverlay(P_OUT_A, P_OUT_A_OV);

        SKPaint p = CreateStrokePaint(new SKColor(255, 255, 255, (byte)(a * 255)), w);
        try
        {
            foreach (SKRect r in peaks)
            {
                c.DrawLine(r.Left, r.Top, r.Right, r.Top, p);
                c.DrawLine(r.Left, r.Bottom, r.Right, r.Bottom, p);
            }
        }
        finally { ReturnPaint(p); }
    }

    private void InvalidateShader(float h)
    {
        if (MathF.Abs(_shH - h) > 0.01f) { _sh?.Dispose(); _sh = null; _shH = h; }
    }

    private SKShader GetShader(float h, float mul)
    {
        h = Max(1f, h);
        if (_sh != null && MathF.Abs(_shH - h) <= 0.01f) return _sh;

        _sh?.Dispose();
        _shH = h;

        var cols = new SKColor[B_COL.Length];
        for (int i = 0; i < B_COL.Length; i++)
        {
            SKColor col = B_COL[i];
            cols[i] = new SKColor(
                (byte)Min(255f, col.Red * mul),
                (byte)Min(255f, col.Green * mul),
                (byte)Min(255f, col.Blue * mul), col.Alpha);
        }

        _sh = SKShader.CreateLinearGradient(new(0, h), new(0, 0), cols, B_POS, SKShaderTileMode.Clamp);
        return _sh;
    }
}
