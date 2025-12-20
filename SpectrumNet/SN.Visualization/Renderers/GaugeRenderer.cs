namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class GaugeRenderer : EffectSpectrumRenderer<GaugeRenderer.QS>
{
    private const float Db1 = 5f, Db0 = -30f, PkT = 5f, A0 = -150f, A1 = -30f, Ar = A1 - A0,
        NLen = 1.55f, Ny = 0.4f, Nw = 2.25f, Ncr = 0.02f, Nbw = 2.5f, BgR = 8f,
        BgPad = 4f, BgSz = 0.2f, ScY = 0.15f, ScRx = 0.45f, ScRy = 0.5f, ScTx = 0.12f, ScTs = 0.1f,
        Tk0 = 0.15f, Tk1 = 0.08f, Tk2 = 0.06f, TkW = 1.8f,
        Lr = 0.05f, Lx = 0.1f, Ly = 0.2f, Lg = 1.5f, Asp = 2.0f, D2r = 0.01745329252f;
    private const int PkHold = 15, MinDiv = 3;

    private static readonly (float V, string L)[] Mk =
    [
        (-30f, "-30"), (-20f, "-20"), (-10f, "-10"), (-7f, "-7"),
        (-5f, "-5"), (-3f, "-3"), (0f, "0"), (3f, "+3"), (5f, "+5")
    ];

    private static readonly SKColor[] Bg = [new(250, 250, 240), new(230, 230, 215)];
    private static readonly float[] BgPos = [0f, 1f];
    private static readonly SKColor[] Cg = [SKColors.White, new(180, 180, 180), new(60, 60, 60)];
    private static readonly float[] Cp = [0.0f, 0.3f, 1.0f];

    private float _np;
    private float _db = Db0;
    private int _pkc;
    private bool _pk;

    private SKTypeface? _tf;
    private SKFont? _fVu;
    private SKFont? _fLab;
    private SKFont? _fLab0;
    private SKFont? _fPeak;

    private static readonly IReadOnlyDictionary<RenderQuality, QS> _pre =
        new Dictionary<RenderQuality, QS>
        {
            [RenderQuality.Low] = new(false, false, false, 0.2f, 0.05f, 0.15f),
            [RenderQuality.Medium] = new(true, true, true, 0.2f, 0.05f, 0.15f),
            [RenderQuality.High] = new(true, true, true, 0.15f, 0.04f, 0.2f)
        };

    public sealed record QS(bool Glow, bool Grad, bool Hi, float SmUp, float SmDn, float Rise);

    protected override IReadOnlyDictionary<RenderQuality, QS> QualitySettingsPresets => _pre;

    protected override int GetMaxBarsForQuality() => GetBarsForQuality(32, 64, 128);

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();
        ApplyStandardQualitySmoothing();
    }

    protected override void OnDispose()
    {
        _tf?.Dispose(); _tf = null;
        _fVu?.Dispose(); _fVu = null;
        _fLab?.Dispose(); _fLab = null;
        _fLab0?.Dispose(); _fLab0 = null;
        _fPeak?.Dispose(); _fPeak = null;

        _np = 0f;
        _db = Db0;
        _pkc = 0;
        _pk = false;

        base.OnDispose();
    }

    protected override void RenderEffect(SKCanvas c, float[] s, SKImageInfo info, RenderParameters rp, SKPaint paint)
    {
        if (CurrentQualitySettings is not { } qs) return;

        float db = CalculateRmsLoudness(s, Db0, Db1);
        Upd(db, qs);

        SKRect b = Bounds(info);

        BgDraw(c, b);
        Scale(c, b);
        Needle(c, b, qs);
        Lamp(c, b, qs);
    }

    private void Upd(float db, QS s)
    {
        float sm = db > _db ? s.SmUp : s.SmDn;
        if (IsOverlayActive) sm *= 0.5f;

        _db = Lerp(_db, db, sm);

        float n = Clamp((_db - Db0) / (Db1 - Db0), 0f, 1f);
        _np = Lerp(_np, n, s.Rise);

        float thr = (PkT - Db0) / (Db1 - Db0);

        if (_np >= thr) { _pk = true; _pkc = PkHold; return; }
        if (_pkc > 0) { _pkc--; return; }
        _pk = false;
    }

    private static SKRect Bounds(SKImageInfo info)
    {
        float ar = info.Width / (float)info.Height;
        float w = ar > Asp ? info.Height * 0.8f * Asp : info.Width * 0.8f;
        float h = w / Asp;

        float l = (info.Width - w) * 0.5f;
        float t = (info.Height - h) * 0.5f;

        return new SKRect(l, t, l + w, t + h);
    }

    private void EnsFonts(SKRect b)
    {
        _tf ??= SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold);

        _fVu ??= new SKFont(_tf, 12f);
        _fLab ??= new SKFont(_tf, 12f);
        _fLab0 ??= new SKFont(_tf, 12f);
        _fPeak ??= new SKFont(_tf, 12f);

        float ry = b.Height * ScRy;

        _fVu.Size = b.Height * BgSz;
        _fLab.Size = ry * ScTs;
        _fLab0.Size = ry * ScTs * 1.15f;
        _fPeak.Size = (Min(b.Width * ScRx, ry) * Lr) * 1.8f;
    }

    private void BgDraw(SKCanvas c, SKRect b)
    {
        EnsFonts(b);

        WithPaint(new SKColor(80, 80, 80), Fill, p => c.DrawRoundRect(b, BgR, BgR, p));

        var ib = SKRect.Inflate(b, -BgPad, -BgPad);
        WithPaint(new SKColor(105, 105, 105), Fill, p => c.DrawRoundRect(ib, 6f, 6f, p));

        var bb = SKRect.Inflate(ib, -4f, -4f);

        WithShader(SKColors.White, Fill, () =>
            SKShader.CreateLinearGradient(new SKPoint(bb.Left, bb.Top), new SKPoint(bb.Left, bb.Bottom), Bg, BgPos, SKShaderTileMode.Clamp),
            p => c.DrawRoundRect(bb, 4f, 4f, p));

        WithPaint(SKColors.Black, Fill, p => c.DrawText("VU", bb.MidX, bb.Bottom - bb.Height * 0.2f, SKTextAlign.Center, _fVu!, p));
    }

    private void Scale(SKCanvas c, SKRect b)
    {
        EnsFonts(b);

        float cx = b.MidX;
        float cy = b.MidY + b.Height * ScY;
        float rx = b.Width * ScRx;
        float ry = b.Height * ScRy;

        foreach ((float v, string l) in Mk)
            Mark(c, cx, cy, rx, ry, v, l, true);

        for (int i = 0; i < Mk.Length - 1; i++)
        {
            float a = Mk[i].V;
            float z = Mk[i + 1].V;
            float st = (z - a) / MinDiv;

            for (float v = a + st; v < z; v += st)
                Mark(c, cx, cy, rx, ry, v, null, false);
        }
    }

    private void Mark(SKCanvas c, float cx, float cy, float rx, float ry, float v, string? lab, bool maj)
    {
        float n = (v - Db0) / (Db1 - Db0);
        float ang = (A0 + n * Ar) * D2r;

        float len = ry * (v == 0f ? Tk0 : maj ? Tk1 : Tk2);

        float x1 = cx + (rx - len) * (float)Cos(ang);
        float y1 = cy + (ry - len) * (float)Sin(ang);
        float x2 = cx + rx * (float)Cos(ang);
        float y2 = cy + ry * (float)Sin(ang);

        SKColor tc = v >= 0 ? new(220, 0, 0) : new(80, 80, 80);

        SKPaint p = CreateStrokePaint(tc, TkW, SKStrokeCap.Butt);
        try { c.DrawLine(x1, y1, x2, y2, p); }
        finally { ReturnPaint(p); }

        if (lab == null) return;

        float tx = cx + (rx + ry * ScTx) * (float)Cos(ang);
        float ty = cy + (ry + ry * ScTx) * (float)Sin(ang);

        SKTextAlign al =
            (ang / D2r) < -120f ? SKTextAlign.Right :
            (ang / D2r) > -60f ? SKTextAlign.Left :
            SKTextAlign.Center;

        SKColor lc = v >= 0 ? new(200, 0, 0) : SKColors.Black;

        SKFont f = v == 0f ? _fLab0! : _fLab!;
        ty += f.Metrics.Descent;

        WithPaint(lc, Fill, p2 => c.DrawText(lab, tx, ty, al, f, p2));
    }

    private void Needle(SKCanvas c, SKRect b, QS s)
    {
        float cx = b.MidX;
        float cy = b.MidY + b.Height * Ny;
        float rx = b.Width * ScRx;
        float ry = b.Height * ScRy;

        float ang = (A0 + _np * Ar) * D2r;
        float len = Min(rx, ry) * NLen;

        float dx = (float)Cos(ang);
        float dy = (float)Sin(ang);

        float tx = cx + dx * len;
        float ty = cy + dy * len;

        float bw = Nw * Nbw;

        WithPaint(SKColors.Black, Fill, p =>
            RenderPath(c, path =>
            {
                path.MoveTo(tx, ty);
                path.LineTo(cx - dy * bw, cy + dx * bw);
                path.LineTo(cx + dy * bw, cy - dx * bw);
                path.Close();
            }, p));

        float cr = rx * Ncr;

        if (s.Grad)
        {
            WithShader(SKColors.Black, Fill, () =>
                SKShader.CreateRadialGradient(new SKPoint(cx - cr * 0.3f, cy - cr * 0.3f), cr * 2f, Cg, Cp, SKShaderTileMode.Clamp),
                p => c.DrawCircle(cx, cy, cr, p));
        }
        else
        {
            WithPaint(new SKColor(60, 60, 60), Fill, p => c.DrawCircle(cx, cy, cr, p));
        }
    }

    private void Lamp(SKCanvas c, SKRect b, QS s)
    {
        EnsFonts(b);

        float rx = b.Width * ScRx;
        float ry = b.Height * ScRy;
        float lr = Min(rx, ry) * Lr;

        float x = b.Right - b.Width * Lx;
        float y = b.Top + b.Height * Ly;

        if (_pk && UseAdvancedEffects && s.Glow)
            WithBlur(SKColors.Red.WithAlpha(77), Fill, lr * Lg, p => c.DrawCircle(x, y, lr * Lg, p));

        WithPaint(_pk ? SKColors.Red : new SKColor(80, 0, 0), Fill, p => c.DrawCircle(x, y, lr * 0.8f, p));

        SKPaint rp = CreateStrokePaint(new SKColor(40, 40, 40), 1.2f, SKStrokeCap.Butt);
        try { c.DrawCircle(x, y, lr, rp); }
        finally { ReturnPaint(rp); }

        SKColor tc = _pk ? SKColors.Red : new SKColor(180, 0, 0);

        WithPaint(tc, Fill, p =>
            c.DrawText("PEAK", x, y + lr * 2.5f + _fPeak!.Metrics.Descent, SKTextAlign.Center, _fPeak!, p));
    }
}
