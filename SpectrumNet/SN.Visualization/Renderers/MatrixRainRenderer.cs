namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class MatrixRainRenderer : EffectSpectrumRenderer<MatrixRainRenderer.QS>
{
    private const float ColW = 20f, ColSp = 5f, SpdBase = 120f, SpdVar = 80f, SpdOvMul = 0.8f,
        CharSz0 = 18f, CharSzOv = 15f, GlowR0 = 3f, GlowROv = 2f, SpecInf = 0.7f, SpecInfOv = 0.5f,
        NewDrop = 0.015f, NewDropInt = 0.15f, CharChg = 0.08f, CharChgHi = 0.12f, HeadGlowMul = 0.7f, TrailFadePow = 1.5f;
    private const byte HeadA = 255, HeadGlowA = 120, HeadGlowAOv = 80, TrailA0 = 220,
        TrailA1 = 15, TrailGlowA = 60, TrailGlowAOv = 40, BgA = 240, BgAOv = 200;
    private const int MinCols = 10, MaxCols = 100, MaxColsOv = 80, MinTrail = 4, Bands = 32;

    private static readonly char[] Chars =
        "アイウエオカキクケコサシスセソタチツテトナニヌネノハヒフヘホマミムメモヤユヨラリルレロワヲン0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ+=-*/<>[]{}()".ToCharArray();

    private static readonly string[] ChS = InitStr();

    private static readonly SKColor HeadBright = new(100, 255, 100),
        HeadNorm = new(0, 255, 0), TrailStart = new(0, 220, 0), TrailEnd = new(0, 150, 0),
        GlowCol = new(50, 255, 50), BgCol = new(0, 10, 0);

    private Column[] _cols = [];
    private float[] _bands = new float[Bands];
    private bool _init;

    private SKTypeface? _tf;
    private SKFont? _font;
    private float _fontSz;

    private static readonly IReadOnlyDictionary<RenderQuality, QS> _pre =
        new Dictionary<RenderQuality, QS>
        {
            [RenderQuality.Low] = new(false, false, true, false, false, 8, 0.7f, 0f, 1f),
            [RenderQuality.Medium] = new(true, true, true, true, true, 12, 0.85f, 0.6f, 1.2f),
            [RenderQuality.High] = new(true, true, true, true, true, 16, 1.0f, 1.0f, 1.5f)
        };

    public sealed record QS(bool Glow, bool CharVar, bool SpdVar, bool TrailGrad, bool BgTint,
        int MaxTrail, float Density, float GlowInt, float AnimSpd);

    protected override IReadOnlyDictionary<RenderQuality, QS> QualitySettingsPresets => _pre;

    protected override int GetMaxBarsForQuality() => GetBarsForQuality(50, 75, 100);

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();
        ApplyStandardQualitySmoothing();

        _init = false;
        _bands = new float[Bands];

        _font?.Dispose(); _font = null;
        _tf?.Dispose(); _tf = null;
        _fontSz = 0f;
    }

    protected override void OnDispose()
    {
        _cols = [];
        _bands = [];
        _init = false;

        _font?.Dispose(); _font = null;
        _tf?.Dispose(); _tf = null;
        _fontSz = 0f;

        base.OnDispose();
    }

    protected override void RenderEffect(SKCanvas c, float[] spec, SKImageInfo info, RenderParameters rp, SKPaint paint)
    {
        if (CurrentQualitySettings is not { } s) return;

        EnsInit(info);
        UpdBands(spec);
        UpdAnim(spec, s);

        DrawBg(c, s);

        if (UseAdvancedEffects && s.Glow)
            DrawGlowLayer(c, s);

        DrawCols(c, s);

        if (UseAdvancedEffects && s.Glow && s.GlowInt > 0.8f)
            DrawHeadHi(c);
    }

    private void DrawBg(SKCanvas c, QS s)
    {
        if (!s.BgTint) { c.Clear(SKColors.Black); return; }
        byte a = SelectByOverlay(BgA, BgAOv);
        WithPaint(BgCol.WithAlpha(a), Fill, p => c.DrawRect(c.LocalClipBounds, p));
    }

    private void EnsInit(SKImageInfo info)
    {
        int n = ColCnt(info.Width);
        if (!_init || _cols.Length != n) { InitCols(info, n); _init = true; }
    }

    private int ColCnt(float w)
    {
        int n = (int)(w / (ColW + ColSp));
        int max = SelectByOverlay(MaxCols, MaxColsOv);
        return Clamp(n, MinCols, max);
    }

    private void InitCols(SKImageInfo info, int n)
    {
        _cols = new Column[n];
        for (int i = 0; i < n; i++) _cols[i] = MkCol(i, info.Height);
    }

    private Column MkCol(int i, float maxH)
    {
        QS s = CurrentQualitySettings!;

        float spd = s.SpdVar ? SpdBase + RandFloat() * SpdVar : SpdBase;
        if (IsOverlayActive) spd *= SpdOvMul;

        int tr = RandInt(Min(MinTrail, s.MaxTrail), s.MaxTrail + 1);

        float y = RandChance(s.Density) ? RandFloat() * maxH : -tr * CharSz();

        return new Column(
            X: i * (ColW + ColSp) + ColW * 0.5f,
            Y: y,
            Spd: spd * s.AnimSpd,
            Trail: tr,
            Ch: GenChars(tr),
            MaxH: maxH,
            Int: 0f);
    }

    private static byte[] GenChars(int n)
    {
        byte[] ch = new byte[n];
        for (int i = 0; i < n; i++)
            ch[i] = (byte)RandInt(Chars.Length);
        return ch;
    }

    private void UpdBands(float[] spec)
    {
        if (spec.Length == 0 || _bands.Length == 0) return;
        ResampleSpectrumAvg(spec, _bands);
    }

    private void UpdAnim(float[] spec, QS s)
    {
        float avg = CalculateAverageSpectrum(spec);
        for (int i = 0; i < _cols.Length; i++) UpdCol(ref _cols[i], i, avg, s);
    }

    private void UpdCol(ref Column col, int i, float avg, QS s)
    {
        float inf = ColInf(i);
        col = col with { Int = inf };

        float spInf = SelectByOverlay(SpecInf, SpecInfOv);
        float dy = col.Spd * (1f + inf * spInf) * DeltaTime;
        col = col with { Y = col.Y + dy };

        if (col.Y > col.MaxH + col.Trail * col.MaxH * 0.02f)
            ResetCol(ref col, avg, s);

        if (s.CharVar)
            UpdChars(ref col, inf);
    }

    private float ColInf(int i)
    {
        if (_bands.Length == 0 || _cols.Length == 0) return 0f;
        int idx = Clamp(i * _bands.Length / _cols.Length, 0, _bands.Length - 1);
        return _bands[idx];
    }

    private void ResetCol(ref Column col, float avg, QS s)
    {
        if (!RandChance(NewDrop + avg * NewDropInt)) return;

        col = col with
        {
            Y = -col.Trail * CharSz(),
            Spd = ReSpd(s),
            Ch = RandChance(0.3f) ? GenChars(col.Ch.Length) : col.Ch
        };
    }

    private float ReSpd(QS s)
    {
        float spd = s.SpdVar ? SpdBase + RandFloat() * SpdVar : SpdBase;
        if (IsOverlayActive) spd *= SpdOvMul;
        return spd * s.AnimSpd;
    }

    private void UpdChars(ref Column col, float inf)
    {
        if (col.Ch.Length == 0) return;

        float chg = (Quality == RenderQuality.High ? CharChgHi : CharChg) * (1f + inf * 0.5f);

        for (int i = 0; i < col.Ch.Length; i++)
            if (RandChance(chg))
                col.Ch[i] = (byte)RandInt(Chars.Length);
    }

    private void DrawGlowLayer(SKCanvas c, QS s)
    {
        float r = GlowRad() * s.GlowInt;
        if (r <= 0f) return;

        using var flt = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, r);

        byte ha = SelectByOverlay(HeadGlowA, HeadGlowAOv);
        WithMask(GlowCol.WithAlpha(ha), Fill, flt, p =>
        {
            float sz = CharSz();

            for (int i = 0; i < _cols.Length; i++)
            {
                Column col = _cols[i];
                if (col.Y < 0f || col.Y > col.MaxH) continue;

                float rr = sz * HeadGlowMul * (1f + col.Int * 0.3f);
                c.DrawCircle(col.X, col.Y, rr, p);
            }

            if (s.TrailGrad)
                TrailGlow(c, flt);
        });
    }

    private void TrailGlow(SKCanvas c, SKMaskFilter flt)
    {
        byte a0 = SelectByOverlay(TrailGlowA, TrailGlowAOv);

        WithMask(GlowCol.WithAlpha(a0), Fill, flt, p =>
        {
            float sz = CharSz();

            for (int ci = 0; ci < _cols.Length; ci++)
            {
                Column col = _cols[ci];
                int vis = Min(3, col.Trail);

                for (int i = 1; i <= vis; i++)
                {
                    float y = col.Y - i * sz;
                    if (y < -sz || y > col.MaxH + sz) continue;

                    float fade = i / (float)vis;
                    p.Color = GlowCol.WithAlpha((byte)(a0 * (1f - fade * 0.7f)));
                    c.DrawCircle(col.X, y, sz * 0.4f, p);
                }
            }
        });
    }

    private void DrawCols(SKCanvas c, QS s)
    {
        EnsureFont();
        if (_font == null) return;

        SKPaint p = CreatePaint(SKColors.White, Fill);

        try
        {
            for (int i = 0; i < _cols.Length; i++)
                DrawCol(c, _cols[i], _font, p, s);
        }
        finally
        {
            ReturnPaint(p);
        }
    }

    private void EnsureFont()
    {
        float sz = CharSz();

        _tf ??= SKTypeface.FromFamilyName("Consolas", SKFontStyle.Bold);

        if (_font == null)
        {
            _font = new SKFont(_tf, sz);
            _fontSz = sz;
            return;
        }

        if (Abs(_fontSz - sz) > 0.01f)
        {
            _font.Size = sz;
            _fontSz = sz;
        }
    }

    private void DrawCol(SKCanvas c, Column col, SKFont f, SKPaint p, QS s)
    {
        if (col.Ch.Length == 0) return;

        float h = _fontSz > 0f ? _fontSz : CharSz();

        for (int i = 0; i < col.Trail && i < col.Ch.Length; i++)
        {
            float y = col.Y - i * h;
            if (y < -h || y > col.MaxH + h) continue;

            p.Color = CharCol(i, col, s);
            c.DrawText(ChS[col.Ch[i]], col.X, y, SKTextAlign.Center, f, p);
        }
    }

    private static SKColor CharCol(int pos, Column col, QS s)
    {
        if (pos == 0)
            return (col.Int > 0.7f ? HeadBright : HeadNorm).WithAlpha(HeadA);

        float prog = pos / (float)col.Trail;

        if (s.TrailGrad)
        {
            prog = (float)Pow(prog, TrailFadePow);
            SKColor clr = InterpolateColor(TrailStart, TrailEnd, prog);
            return clr.WithAlpha((byte)Lerp(TrailA0, TrailA1, prog));
        }

        return TrailStart.WithAlpha((byte)Lerp(TrailA0, TrailA1, prog));
    }

    private void DrawHeadHi(SKCanvas c)
    {
        float sz = CharSz();

        WithPaint(SKColors.White.WithAlpha(100), Fill, p =>
        {
            for (int i = 0; i < _cols.Length; i++)
            {
                Column col = _cols[i];
                if (col.Y >= 0f && col.Y <= col.MaxH && col.Int > 0.8f)
                    c.DrawCircle(col.X, col.Y, sz * 0.2f, p);
            }
        });
    }

    private float CharSz() => SelectByOverlay(CharSz0, CharSzOv);
    private float GlowRad() => SelectByOverlay(GlowR0, GlowROv);

    private static string[] InitStr()
    {
        string[] s = new string[Chars.Length];
        for (int i = 0; i < s.Length; i++) s[i] = Chars[i].ToString();
        return s;
    }

    private readonly record struct Column(float X, float Y, float Spd, int Trail, byte[] Ch, float MaxH, float Int);
}
