namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class GradientWaveRenderer : EffectSpectrumRenderer<GradientWaveRenderer.QS>
{
    private const float H = 0.85f, H_OV = 0.7f, PAD = 15f, SM = 0.25f, W = 3f,
        W_OV = 2f, G_R = 12f, G_R_OV = 8f, G_A = 40f, F_A = 0.3f, F_A_OV = 0.2f,
        ANIM = 0.5f, COL_ANIM = 0.3f, HI_T = 0.8f;
    private const int SM_WIN = 4;

    private static readonly SKColor[] Cols = [new(100, 200, 255), new(255, 100, 200), new(200, 255, 100)];
    private static readonly float[] Pos = [0f, 0.5f, 1f];
    private static readonly float[] Dash = [2f, 4f];

    private float _ph;
    private float _cph;
    private float[] _buf = [];
    private readonly SKColor[] _fc = new SKColor[3];

    private static readonly IReadOnlyDictionary<RenderQuality, QS> _pre =
        new Dictionary<RenderQuality, QS>
        {
            [RenderQuality.Low] = new(false, false, false, false, 1, 0.8f),
            [RenderQuality.Medium] = new(true, true, true, true, 2, 1f),
            [RenderQuality.High] = new(true, true, true, true, 2, 1.2f)
        };

    public sealed record QS(bool Glow, bool Out, bool AnimCol, bool Hi, int Pass, float Spd);

    protected override IReadOnlyDictionary<RenderQuality, QS> QualitySettingsPresets => _pre;

    protected override RenderParameters CalculateRenderParameters(SKImageInfo info, int bc, float bw, float bs) =>
        CalcStandardRenderParams(info, bc, bw, bs, GetMaxBarsForQuality());
    protected override int GetSpectrumProcessingCount(RenderParameters rp) => rp.EffectiveBarCount;
    protected override int GetMaxBarsForQuality() => GetBarsForQuality(64, 128, 256);

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();
        ApplyStandardQualitySmoothing();
        _buf = [];
    }

    protected override void OnDispose()
    {
        _ph = 0f;
        _cph = 0f;
        _buf = [];
        base.OnDispose();
    }

    protected override void RenderEffect(SKCanvas c, float[] s, SKImageInfo info, RenderParameters rp, SKPaint paint)
    {
        if (CurrentQualitySettings is not { } qs) return;
        if (rp.EffectiveBarCount < 2 || rp.BarWidth <= 0f) return;

        float dt = DeltaTime * qs.Spd;
        _ph = WrapAngle(_ph + ANIM * dt);
        _cph = WrapAngle(_cph + COL_ANIM * dt);

        float total = rp.EffectiveBarCount * rp.BarWidth + (rp.EffectiveBarCount - 1) * rp.BarSpacing;

        float l = Max(PAD, rp.StartOffset);
        float r = Min(info.Width - PAD, rp.StartOffset + total);
        float t = PAD;
        float b = info.Height - PAD;

        if (r <= l || b <= t) return;

        SKRect rect = new(l, t, r, b);

        float[] sm = Smooth(s, qs.Pass);
        float peak = CalculateMaxSpectrum(sm);

        using Pooled<SKPath> pp = RentPath();
        using Pooled<SKPath> fp = RentPath();

        SKPath path = pp.Value;
        SKPath fill = fp.Value;

        BuildPath(path, sm, rect, rp);
        if (path.IsEmpty) return;

        fill.Reset();
        fill.AddPath(path);
        fill.LineTo(rect.Right, rect.Bottom);
        fill.LineTo(rect.Left, rect.Bottom);
        fill.Close();

        if (!IsAreaVisible(c, fill.Bounds)) return;

        SKColor col = AnimCol(paint.Color, qs);

        DrawFill(c, fill, rect, col);

        if (UseAdvancedEffects && qs.Glow)
            DrawGlow(c, path, col);

        DrawMain(c, path, col);

        if (qs.Out)
            DrawOut(c, path);

        if (UseAdvancedEffects && qs.Hi && peak > HI_T)
            DrawHi(c, path, peak);
    }

    private float[] Smooth(float[] s, int pass)
    {
        if (s.Length == 0) return s;

        if (_buf.Length != s.Length) _buf = new float[s.Length];
        Array.Copy(s, _buf, s.Length);

        for (int p = 0; p < pass; p++)
        {
            if (_buf.Length < 3) break;

            float prev = _buf[0];
            _buf[0] = (_buf[0] * 3f + _buf[1]) / SM_WIN;

            for (int i = 1; i < _buf.Length - 1; i++)
            {
                float cur = _buf[i];
                _buf[i] = (prev + _buf[i] * 2f + _buf[i + 1]) / SM_WIN;
                prev = cur;
            }

            _buf[^1] = (prev + _buf[^1] * 3f) / SM_WIN;
        }

        return _buf;
    }

    private void BuildPath(SKPath p, float[] s, SKRect rect, RenderParameters rp)
    {
        p.Reset();

        int n = s.Length;
        if (n < 2) return;

        float wh = SelectByOverlay(H, H_OV) * rect.Height;
        float step = rp.BarWidth + rp.BarSpacing;

        using Pooled<List<SKPoint>> lp = RentList<SKPoint>();
        List<SKPoint> pts = lp.Value;

        pts.Clear();
        pts.Capacity = Max(pts.Capacity, n);

        for (int i = 0; i < n; i++)
        {
            float mag = Max(s[i], 0f);

            float x = rp.StartOffset + i * step + rp.BarWidth * 0.5f;
            float y = rect.Bottom - mag * wh;

            x = Clamp(x, rect.Left, rect.Right);
            y = Clamp(y, rect.Top, rect.Bottom);

            pts.Add(new SKPoint(x, y));
        }

        pts[0] = new SKPoint(rect.Left, pts[0].Y);
        pts[^1] = new SKPoint(rect.Right, pts[^1].Y);

        p.MoveTo(pts[0]);

        for (int i = 0; i < pts.Count - 1; i++)
        {
            SKPoint p0 = i > 0 ? pts[i - 1] : pts[i];
            SKPoint p1 = pts[i];
            SKPoint p2 = pts[i + 1];
            SKPoint p3 = i < pts.Count - 2 ? pts[i + 2] : p2;

            for (float tt = 0f; tt <= 1.0001f; tt += SM)
                p.LineTo(Cr(p0, p1, p2, p3, tt));
        }

        p.LineTo(pts[^1]);
    }

    private SKColor AnimCol(SKColor baseCol, QS s)
    {
        if (!s.AnimCol) return baseCol;
        float tt = (_cph / Tau) % 1f;
        return InterpolateColorArray(Cols, tt);
    }

    private void DrawFill(SKCanvas c, SKPath fill, SKRect rect, SKColor col)
    {
        float a0 = SelectByOverlay(F_A, F_A_OV);
        byte a1 = CalculateAlpha(a0);
        byte a2 = CalculateAlpha(a0 * 0.5f);

        _fc[0] = col.WithAlpha(a1);
        _fc[1] = col.WithAlpha(a2);
        _fc[2] = SKColors.Transparent;

        WithShader(SKColors.White, Fill, () =>
            SKShader.CreateLinearGradient(new SKPoint(0f, rect.Top), new SKPoint(0f, rect.Bottom),
            _fc, Pos, SKShaderTileMode.Clamp),
            p => c.DrawPath(fill, p));
    }

    private void DrawGlow(SKCanvas c, SKPath path, SKColor col)
    {
        float gr = SelectByOverlay(G_R, G_R_OV);
        if (gr <= 0f) return;

        WithBlur(col.WithAlpha((byte)G_A), Stroke, gr, p =>
        {
            p.StrokeWidth = SelectByOverlay(W, W_OV) * 3f;
            p.StrokeCap = SKStrokeCap.Round;
            p.StrokeJoin = SKStrokeJoin.Round;
            c.DrawPath(path, p);
        });
    }

    private void DrawMain(SKCanvas c, SKPath path, SKColor col)
    {
        SKPaint p = CreateStrokePaint(col, SelectByOverlay(W, W_OV));
        try { c.DrawPath(path, p); }
        finally { ReturnPaint(p); }
    }

    private void DrawOut(SKCanvas c, SKPath path)
    {
        WithPathEffect(SKColors.White.WithAlpha(80), Stroke, () =>
        SKPathEffect.CreateDash(Dash, _ph * 10f), p =>
        { p.StrokeWidth = 1f; c.DrawPath(path, p); });
    }

    private void DrawHi(SKCanvas c, SKPath path, float peak)
    {
        float k = (peak - HI_T) * 1.5f;
        byte a = CalculateAlpha(Clamp(k, 0f, 1f));

        SKPaint p = CreateStrokePaint(SKColors.White.WithAlpha(a), SelectByOverlay(W, W_OV) * 0.5f);
        p.BlendMode = SKBlendMode.Screen;

        try { c.DrawPath(path, p); }
        finally { ReturnPaint(p); }
    }
}
