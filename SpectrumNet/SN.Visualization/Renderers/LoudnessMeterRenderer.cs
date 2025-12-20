namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class LoudnessMeterRenderer : EffectSpectrumRenderer<LoudnessMeterRenderer.QS>
{
    private const float
        MinL = 0.001f,
        MedL = 0.4f,
        AtkN = 0.6f,
        RelN = 0.2f,
        AtkOv = 0.8f,
        RelOv = 0.3f,
        PkG = 0.015f,
        PkD = 0.95f,
        HoldMs = 300f,
        PkH = 3f,
        PkBw = 0.8f,
        EdgeW = 4f,
        Pad = 20f,
        Rr = 8f,
        OutBw = 2.5f,
        InBw = 1.8f,
        FillBw = 1.2f,
        MarkW = 12f,
        MarkH = 2.5f,
        MarkBw = 0.8f;

    private const byte
        BgA = 45,
        OutA = 140,
        InA = 80,
        FillA = 100,
        MarkA = 85,
        MarkBA = 120,
        PkA = 230,
        PkBA = 255,
        EdgeA = 255;

    private static readonly float[] DefPos = [0f, 0.5f, 1.0f];

    private static readonly float[] MarkPos =
        [0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1.0f];

    private float _l;
    private PeakTracker _pk;
    private readonly float _hold = HoldMs / 1000f;

    private DimCache _d;

    private readonly float[] _pos3 = new float[3];
    private readonly SKColor[] _cols3 = new SKColor[3];

    private static readonly IReadOnlyDictionary<RenderQuality, QS> _pre =
        new Dictionary<RenderQuality, QS>
        {
            [RenderQuality.Low] = new(false, false, false, false, false, 0f, 0f, 0f, 0.85f, 0f),
            [RenderQuality.Medium] = new(true, true, true, true, false, 0.5f, 8f, 0.35f, 0.95f, 0.7f),
            [RenderQuality.High] = new(true, true, true, true, true, 0.7f, 12f, 0.6f, 1.0f, 1.0f)
        };

    public sealed record QS(
        bool Glow,
        bool Markers,
        bool InBorder,
        bool FillBorder,
        bool MarkBorder,
        float GlowI,
        float Blur,
        float GlowH,
        float GradA,
        float DynGrad);

    protected override IReadOnlyDictionary<RenderQuality, QS> QualitySettingsPresets => _pre;
    protected override int GetMaxBarsForQuality() => GetBarsForQuality(32, 64, 128);

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();
        ApplyStandardQualitySmoothing();
        _pk.Reset();
    }

    protected override void OnDispose()
    {
        _l = 0f;
        _pk.Reset();
        _d = default;
        base.OnDispose();
    }

    protected override void RenderEffect(
        SKCanvas c,
        float[] spec,
        SKImageInfo info,
        RenderParameters rp,
        SKPaint paint)
    {
        if (CurrentQualitySettings is not { } s)
            return;

        if (_d.Changed(info))
            RequestRedraw();

        float loud = UpdL(spec);
        _pk.UpdateWithGravity(loud, _hold, DeltaTime, PkG, PkD);

        var r = new SKRect(Pad, Pad, info.Width - Pad, info.Height - Pad);

        DrawBg(c, r);
        DrawOutBorder(c, r);

        int sc = c.Save();
        c.ClipRoundRect(new SKRoundRect(r, Rr), SKClipOperation.Intersect);

        try
        {
            if (UseAdvancedEffects && s.Markers)
                DrawMarks(c, r, s);

            if (loud >= MinL)
                DrawFill(c, r, loud, s);

            if (s.InBorder)
                DrawInBorder(c, r);

            if (_pk.Value > MinL)
                DrawPeak(c, r, s);
        }
        finally
        {
            c.RestoreToCount(sc);
        }
    }

    private float UpdL(float[] spec)
    {
        float raw = CalculateAverageLoudness(spec);
        float atk = SelectByOverlay(AtkN, AtkOv);
        float rel = SelectByOverlay(RelN, RelOv);

        _l = Clamp(SmoothWithAttackRelease(_l, raw, atk, rel), 0f, 1f);
        return _l;
    }

    private void DrawBg(SKCanvas c, SKRect r) =>
        WithPaint(new SKColor(20, 20, 20, BgA), Fill, p =>
            c.DrawRoundRect(r, Rr, Rr, p));

    private void DrawOutBorder(SKCanvas c, SKRect r)
    {
        SKPaint p = CreatePaint(new SKColor(120, 120, 120, OutA), Stroke);
        p.StrokeWidth = OutBw;

        try { c.DrawRoundRect(r, Rr, Rr, p); }
        finally { ReturnPaint(p); }
    }

    private void DrawMarks(SKCanvas c, SKRect r, QS s)
    {
        SKPaint mp = CreatePaint(new SKColor(120, 120, 120, MarkA), Fill);

        SKPaint? bp = s.MarkBorder
            ? CreatePaint(new SKColor(160, 160, 160, MarkBA), Stroke)
            : null;

        if (bp != null)
            bp.StrokeWidth = MarkBw;

        try
        {
            for (int i = 0; i < MarkPos.Length; i++)
            {
                float pos = MarkPos[i];
                float y = r.Bottom - r.Height * pos;

                var l = new SKRect(r.Left, y - MarkH / 2f, r.Left + MarkW, y + MarkH / 2f);
                var rr = new SKRect(r.Right - MarkW, y - MarkH / 2f, r.Right, y + MarkH / 2f);

                c.DrawRect(l, mp);
                c.DrawRect(rr, mp);

                if (bp != null)
                {
                    c.DrawRect(l, bp);
                    c.DrawRect(rr, bp);
                }
            }
        }
        finally
        {
            ReturnPaint(mp);
            if (bp != null) ReturnPaint(bp);
        }
    }

    private void DrawFill(SKCanvas c, SKRect r, float loud, QS s)
    {
        float h = r.Height * loud;

        DrawGradFill(c, r, h, loud, s);

        if (UseAdvancedEffects && s.Glow && loud > MedL)
            DrawGlow(c, r, h, loud, s);

        if (s.FillBorder)
            DrawFillBorder(c, r, h);
    }

    private void DrawGradFill(SKCanvas c, SKRect r, float h, float loud, QS s)
    {
        byte a = CalculateAlpha(s.GradA);

        _cols3[0] = SKColors.Green.WithAlpha(a);
        _cols3[1] = SKColors.Yellow.WithAlpha(a);
        _cols3[2] = SKColors.Red.WithAlpha(a);

        float[] pos;

        if (UseAdvancedEffects && s.DynGrad > 0f)
        {
            _pos3[0] = 0f;
            _pos3[1] = Clamp(loud * s.DynGrad, 0.2f, 0.8f);
            _pos3[2] = 1f;
            pos = _pos3;
        }
        else
        {
            pos = DefPos;
        }

        WithShader(SKColors.Black, Fill, () =>
            SKShader.CreateLinearGradient(
                new SKPoint(r.Left, r.Bottom),
                new SKPoint(r.Left, r.Top),
                _cols3,
                pos,
                SKShaderTileMode.Clamp),
            p => c.DrawRect(r.Left, r.Bottom - h, r.Width, h, p));
    }

    private void DrawGlow(SKCanvas c, SKRect r, float h, float loud, QS s)
    {
        float gh = h * s.GlowH;
        float norm = Clamp((loud - MedL) / (1f - MedL), 0f, 1f);

        byte g = (byte)(255f * (1f - norm));
        var col = new SKColor(255, g, 0);

        byte a = (byte)(255f * s.GlowI * norm);

        WithBlur(col.WithAlpha(a), Fill, s.Blur, p =>
            c.DrawRect(r.Left, r.Bottom - h, r.Width, gh, p));
    }

    private void DrawFillBorder(SKCanvas c, SKRect r, float h)
    {
        float top = r.Bottom - h;

        SKPaint p = CreatePaint(new SKColor(220, 220, 220, FillA), Stroke);
        p.StrokeWidth = FillBw;

        try
        {
            RenderPath(c, path =>
            {
                path.MoveTo(r.Left, top);
                path.LineTo(r.Right, top);
                path.LineTo(r.Right, r.Bottom);
                path.LineTo(r.Left, r.Bottom);
                path.Close();
            }, p);
        }
        finally
        {
            ReturnPaint(p);
        }
    }

    private void DrawInBorder(SKCanvas c, SKRect r)
    {
        SKRect ir = r;
        ir.Inflate(-InBw, -InBw);

        SKPaint p = CreatePaint(new SKColor(170, 170, 170, InA), Stroke);
        p.StrokeWidth = InBw;

        try { c.DrawRoundRect(ir, Rr - InBw, Rr - InBw, p); }
        finally { ReturnPaint(p); }
    }

    private void DrawPeak(SKCanvas c, SKRect r, QS s)
    {
        float y = r.Bottom - r.Height * _pk.Value;

        WithPaint(SKColors.White.WithAlpha(PkA), Fill, p =>
            c.DrawRect(r.Left, y - PkH / 2f, r.Width, PkH, p));

        SKPaint bp = CreatePaint(SKColors.White.WithAlpha(PkBA), Stroke);
        bp.StrokeWidth = PkBw;

        try { c.DrawLine(r.Left, y, r.Right, y, bp); }
        finally { ReturnPaint(bp); }

        if (UseAdvancedEffects && s.Markers)
        {
            WithPaint(SKColors.White.WithAlpha(EdgeA), Fill, p =>
            {
                c.DrawRect(r.Left, y - PkH, EdgeW, PkH * 2f, p);
                c.DrawRect(r.Right - EdgeW, y - PkH, EdgeW, PkH * 2f, p);
            });
        }
    }
}
