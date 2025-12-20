namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class LedMeterRenderer : BitmapBufferRenderer<LedMeterRenderer.QS>
{
    private const float Spd = 0.015f, SmN = 0.3f, SmOv = 0.5f, PkDec = 0.04f, Lmin = 0.001f,
        Ht = 0.7f, Mt = 0.4f, LedR = 2.5f, Pad = 12f, TickW = 22f, R = 14f, Vib = 2f, VibF = 8f,
        GBlur = 2f, GInt = 0.3f, Inset = 1f, Pulse = 0.3f, Top = 20f, Rpad = 15f, LedS = 0.95f, SpS = 0.05f;
    private const int Led0 = 22, LedMin = 10, LedDiv = 12, VarN = 30, ColN = 10, ColR = 10;

    private static readonly SKColor[] Base = [new(30, 200, 30), new(220, 200, 0), new(230, 30, 30)];
    private static readonly SKColor[] BgCols = [new(70, 70, 70), new(40, 40, 40), new(55, 55, 55)];
    private static readonly float[] BgPos = [0.0f, 0.7f, 1.0f];

    private float _vib;
    private float _l;
    private float _pk;
    private float _t;

    private float[] _ph = [];
    private int _n = Led0;

    private readonly List<float> _var = new(VarN);
    private readonly List<SKColor> _cols = new(VarN);

    private static readonly IReadOnlyDictionary<RenderQuality, QS> _pre =
        new Dictionary<RenderQuality, QS>
        {
            [RenderQuality.Low] = new(false, false, false, false, false),
            [RenderQuality.Medium] = new(true, true, true, true, true),
            [RenderQuality.High] = new(true, true, true, true, true)
        };

    public sealed record QS(bool Glow, bool Hi, bool Tick, bool Vig, bool Sh);

    protected override IReadOnlyDictionary<RenderQuality, QS> QualitySettingsPresets => _pre;

    protected override int GetMaxBarsForQuality() => GetBarsForQuality(32, 64, 128);

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();
        ApplyStandardQualitySmoothing();
        InitVar();
        if (Dims.W > 0 && Dims.H > 0) RebuildBg(Dims.W, Dims.H);
    }

    protected override void OnDispose()
    {
        _cols.Clear();
        _var.Clear();
        _ph = [];
        _n = Led0;
        _vib = 0f;
        _l = 0f;
        _pk = 0f;
        _t = 0f;
        base.OnDispose();
    }

    protected override void RenderEffect(SKCanvas c, float[] s, SKImageInfo info, RenderParameters rp, SKPaint paint)
    {
        if (CurrentQualitySettings is not { } qs) return;

        if (Dims.Changed(info))
            UpdSz(info);

        UpdL(s);
        UpdAnim();

        if (_l < Lmin && !IsOverlayActive)
            return;

        int sc = c.Save();
        if (!IsOverlayActive && _vib != 0f)
            c.Translate(_vib, 0f);

        try
        {
            if (Bitmap != null)
                c.DrawBitmap(Bitmap, 0, 0);

            DrawSys(c, info, qs);
        }
        finally
        {
            c.RestoreToCount(sc);
        }
    }

    private void UpdSz(SKImageInfo info)
    {
        float h = info.Height - Pad * 2f;
        _n = (int)Clamp(h / LedDiv, LedMin, Led0);
        InitPh();
        RebuildBg(info.Width, info.Height);
    }

    private void UpdL(float[] s)
    {
        float raw = CalculateAverageLoudness(s);
        float sm = SelectByOverlay(SmN, SmOv);
        _l = Clamp(Lerp(_l, raw, sm), Lmin, 1f);
        _pk = _l > _pk ? _l : Max(0f, _pk - PkDec);
    }

    private void UpdAnim()
    {
        _t += Spd;
        _vib = _l > Ht ? (float)Sin(_t * VibF) * Vib * _l : 0f;
    }

    private void InitPh()
    {
        _ph = new float[_n];
        for (int i = 0; i < _n; i++)
            _ph[i] = RandFloat();
    }

    private void RebuildBg(int w, int h)
    {
        EnsureBitmap(w, h);
        if (Bitmap == null) return;

        using var bc = new SKCanvas(Bitmap);
        bc.Clear(SKColors.Transparent);

        var r = new SKRect(Pad, Pad, w - Pad, h - Pad);

        using var sh = SKShader.CreateLinearGradient(new SKPoint(0f, 0f), new SKPoint(0f, r.Height), BgCols, BgPos,
            SKShaderTileMode.Clamp);
        WithPaint(SKColors.Black, Fill, sh, p => bc.DrawRoundRect(r, R, R, p));
    }

    private void DrawSys(SKCanvas c, SKImageInfo info, QS s)
    {
        int on = (int)(_l * _n);
        int pk = (int)(_pk * _n);

        SKRect mr = Meter(info);
        (float h, float sp) = LedSz(mr);

        for (int i = 0; i < _n; i++)
        {
            float t = i / (float)_n;
            float y = mr.Top + (_n - i - 1) * (h + sp);

            SKColor col = Col(t, i);

            if (i < on || i == pk)
                On(c, mr.Left, y, mr.Width, h, col, i == pk, i, s);
            else
                Off(c, mr.Left, y, mr.Width, h, col);
        }
    }

    private static SKRect Meter(SKImageInfo info)
    {
        float l = Pad;
        float t = Pad;
        float w = info.Width - Pad * 2f;
        float h = info.Height - Pad * 2f;

        return new SKRect(l + TickW + 5f, t + Top, l + w - Rpad, t + h - 5f);
    }

    private (float h, float sp) LedSz(SKRect r)
    {
        float a = r.Height * LedS;
        float b = r.Height * SpS;

        float h = (a - b) / _n;
        float sp = _n > 1 ? b / (_n - 1) : 0f;

        return (h, sp);
    }

    private void Off(SKCanvas c, float x, float y, float w, float h, SKColor col)
    {
        var r = new SKRect(x, y, x + w, y + h);
        var s = new SKRect(x + Inset, y + Inset, x + w - Inset, y + h - Inset);

        float rr = Max(1f, LedR - Inset * 0.5f);

        WithPaint(new SKColor(8, 8, 8), Fill, p => c.DrawRoundRect(r, LedR, LedR, p));
        WithPaint(Mul(col, 0.10f), Fill, p => c.DrawRoundRect(s, rr, rr, p));
    }

    private void On(SKCanvas c, float x, float y, float w, float h, SKColor col, bool pk, int i, QS s)
    {
        var r = new SKRect(x, y, x + w, y + h);
        var srf = new SKRect(x + Inset, y + Inset, x + w - Inset, y + h - Inset);

        float rr = Max(1f, LedR - Inset * 0.5f);

        WithPaint(new SKColor(8, 8, 8), Fill, p => c.DrawRoundRect(r, LedR, LedR, p));

        float br = _var.Count > 0 ? _var[i % _var.Count] : 1f;
        float ph = _ph.Length > 0 ? _ph[i % _ph.Length] : 0f;

        float pulse = pk ? 0.7f + (float)Sin(ph * Tau) * Pulse : br;
        SKColor onCol = Mul(col, pulse);

        if (UseAdvancedEffects && s.Glow && i <= _n * 0.7f)
            WithBlur(onCol.WithAlpha((byte)(GInt * 160f * br)), Fill, GBlur, p => c.DrawRoundRect(r, LedR, LedR, p));

        WithPaint(onCol, Fill, p => c.DrawRoundRect(srf, rr, rr, p));

        SKColor hi = onCol.WithAlpha(90);
        var sh = new SKColor(10, 10, 10, 160);

        WithPaint(hi, Fill, p => c.DrawRect(srf.Left, srf.Top, srf.Width, srf.Height * 0.35f, p));
        WithPaint(sh, Fill, p => c.DrawRect(srf.Left, srf.Bottom - srf.Height * 0.25f, srf.Width, srf.Height * 0.25f, p));
    }

    private SKColor Col(float t, int i)
    {
        int g = t >= Ht ? 2 : t >= Mt ? 1 : 0;
        int idx = g * ColN + i % ColN;
        return idx < _cols.Count ? _cols[idx] : Base[g];
    }

    private static SKColor Mul(SKColor c, float k) =>
        new((byte)Clamp(c.Red * k, 0f, 255f), (byte)Clamp(c.Green * k, 0f, 255f), (byte)Clamp(c.Blue * k, 0f, 255f), c.Alpha);

    private void InitVar()
    {
        _var.Clear();
        for (int i = 0; i < VarN; i++)
            _var.Add(0.85f + RandFloat() * 0.3f);

        _cols.Clear();
        foreach (SKColor b in Base)
            for (int j = 0; j < ColN; j++)
                _cols.Add(new(
                    (byte)Clamp(b.Red + RandInt(-ColR, ColR), 0, 255),
                    (byte)Clamp(b.Green + RandInt(-ColR, ColR), 0, 255),
                    (byte)Clamp(b.Blue + RandInt(-ColR, ColR), 0, 255)));
    }
}
