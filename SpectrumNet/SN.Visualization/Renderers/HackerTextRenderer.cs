namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class HackerTextRenderer : FontCachedRenderer<HackerTextRenderer.QS>
{

    private const string TXT0 = "HAKER2550";

    private const float
        SZ0 = 96f, YR = 0.5f, JMP = 80f, RET = 0.1f, DMP = 0.85f, THR = 0.02f,
        MAG_SM = 0.6f, SZ_SM = 0.08f, DT0 = 0.016f, OV_DMP = 1.2f, OV_JMP = 0.7f,
        EX_A_DEC = 0.85f, EX_DK = 0.7f, GR_HI = 1.3f, GR_SH = 0.6f, SH_SIG = 3f,
        VMAX = 1000f, DMAX = 500f;

    private const byte EX_A_MIN = 20;
    private static readonly float[] G_POS = [0f, 1f];
    private static readonly SKPoint[] Z_PT = [new(0, 0)];

    private readonly List<LetterPath> _lp = [];
    private readonly List<LetterPhys> _ps = [];
    private readonly List<LetterStat> _sd = [];

    private readonly string _txt = TXT0;
    private bool _need;
    private float _sz = SZ0;
    private float _tLast;

    private static readonly IReadOnlyDictionary<RenderQuality, QS> _pre = new Dictionary<RenderQuality, QS>
    {
        [RenderQuality.Low] = new(JMP * 0.7f, DMP * 1.05f, 2, false, true, false),
        [RenderQuality.Medium] = new(JMP, DMP, 5, true, true, true),
        [RenderQuality.High] = new(JMP * 1.3f, DMP * 0.95f, 8, true, true, true)
    };

    public sealed record QS(float Jmp, float Dmp, int ExL, bool Grad, bool Ex, bool Sh);

    protected override IReadOnlyDictionary<RenderQuality, QS> QualitySettingsPresets => _pre;
    protected override int GetMaxBarsForQuality() => GetBarsForQuality(36, 72, 144);

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();
        ApplyStandardQualitySmoothing();
        _need = true;
    }

    protected override void OnDispose()
    {
        Clear();
        base.OnDispose();
    }

    protected override void RenderEffect(
        SKCanvas c, float[] s, SKImageInfo info, RenderParameters rp, SKPaint paint)
    {
        if (CurrentQualitySettings is not { } qs) return;

        EnsureFont(SZ0);
        if (_need) Layout(info);

        float dt = GetDeltaTime();
        Step(s, info.Width, rp.EffectiveBarCount, dt);

        if (_sd.Count == 0 || _lp.Count != _sd.Count) return;

        if (UseAdvancedEffects && qs.Sh)
            DrawShadow(c);

        if (UseAdvancedEffects && qs.Ex)
            DrawExtrude(c, paint.Color, qs);

        DrawMain(c, paint.Color, qs);
    }

    private void Layout(SKImageInfo info)
    {
        if (Font == null) return;

        Clear();

        _sz = Lerp(_sz, SZ0, SZ_SM);
        Font.Size = _sz;

        float tw = Font.MeasureText(_txt);
        float x0 = (info.Width - tw) / 2f;
        float y0 = info.Height * YR;
        float x = x0;

        for (int i = 0; i < _txt.Length; i++)
        {
            string ch = _txt[i].ToString();
            float w = Font.MeasureText(ch);

            using SKPath tp = Font.GetTextPath(ch, Z_PT);
            var p = new SKPath();
            p.AddPath(tp);

            _lp.Add(new LetterPath(p));
            _sd.Add(new LetterStat(_txt[i], x, y0, w, x + w * 0.5f));
            _ps.Add(new LetterPhys(y0, 0f, 0f));

            x += w;
        }
        _need = false;
    }

    private void Step(float[] s, float w, int bc, float dt)
    {
        if (_sd.Count == 0 || bc == 0 || dt <= 0f || s.Length == 0) return;

        for (int i = 0; i < _sd.Count; i++)
        {
            LetterStat sd = _sd[i];
            LetterPhys st = _ps[i];

            int si = Clamp((int)((sd.Cx / w) * bc), 0, s.Length - 1);
            float mag = s[si];
            float sm = Lerp(st.Sm, mag, MAG_SM);

            float vy = st.Vy;
            if (sm > THR)
            {
                float jm = CurrentQualitySettings!.Jmp * SelectByOverlay(1f, OV_JMP);
                vy -= sm * jm * dt;
            }

            vy -= (st.Y - sd.Y0) * RET * dt * 60f;

            float dmp = CurrentQualitySettings!.Dmp * SelectByOverlay(1f, OV_DMP);
            vy *= MathF.Pow(dmp, dt * 60f);
            vy = Clamp(vy, -VMAX, VMAX);

            float y = Clamp(st.Y + vy * dt * 60f, sd.Y0 - DMAX, sd.Y0 + DMAX);
            _ps[i] = new LetterPhys(y, vy, sm);
        }
    }

    private void DrawShadow(SKCanvas c)
    {
        WithBlur(new SKColor(0, 0, 0, 50), Fill, SH_SIG, p =>
            RenderPath(c, path =>
            {
                for (int i = 0; i < _lp.Count; i++)
                {
                    var m = SKMatrix.CreateTranslation(_sd[i].X + 2f, _ps[i].Y + 2f);
                    path.AddPath(_lp[i].P, in m);
                }
            }, p));
    }

    private void DrawExtrude(SKCanvas c, SKColor col0, QS s)
    {
        for (int layer = s.ExL; layer >= 1; layer--)
        {
            float prog = (float)layer / s.ExL;
            var col = new SKColor(
                (byte)(col0.Red * EX_DK * prog),
                (byte)(col0.Green * EX_DK * prog),
                (byte)(col0.Blue * EX_DK * prog),
                (byte)Max(EX_A_MIN, col0.Alpha * MathF.Pow(EX_A_DEC, 1f - prog)));

            float z = layer;
            WithPaint(col, Fill, p =>
                RenderPath(c, path =>
                {
                    for (int i = 0; i < _lp.Count; i++)
                    {
                        var m = SKMatrix.CreateTranslation(_sd[i].X + z * 0.5f, _ps[i].Y + z * 0.5f);
                        path.AddPath(_lp[i].P, in m);
                    }
                }, p));
        }
    }

    private void DrawMain(SKCanvas c, SKColor col0, QS s)
    {
        void Draw(SKPaint p) =>
            RenderPath(c, path =>
            {
                for (int i = 0; i < _lp.Count; i++)
                {
                    var m = SKMatrix.CreateTranslation(_sd[i].X, _ps[i].Y);
                    path.AddPath(_lp[i].P, in m);
                }
            }, p);

        if (!s.Grad) { WithPaint(col0, Fill, Draw); return; }

        SKRect bb = Box();
        SKColor[] cols = [AdjustBrightness(col0, GR_HI), AdjustBrightness(col0, GR_SH)];

        WithShader(col0, Fill, () =>
            SKShader.CreateLinearGradient(new(bb.Left, bb.Top), new(bb.Left, bb.Bottom), cols, G_POS, SKShaderTileMode.Clamp),
            Draw);
    }

    private SKRect Box()
    {
        if (_sd.Count == 0 || Font == null) return SKRect.Empty;

        float minX = _sd[0].X;
        float maxX = _sd[^1].X + _sd[^1].W;
        float minY = float.MaxValue;
        float maxY = float.MinValue;

        for (int i = 0; i < _ps.Count; i++)
        {
            float y = _ps[i].Y;
            minY = Min(minY, y - Font.Size);
            maxY = Max(maxY, y);
        }
        return new SKRect(minX, minY, maxX, maxY);
    }

    private float GetDeltaTime()
    {
        float now = TickCount / 1000f;
        float dt = _tLast > 0f ? now - _tLast : DT0;
        _tLast = now;
        return Min(dt, 0.1f);
    }

    private void Clear()
    {
        foreach (LetterPath lp in _lp) lp.P.Dispose();
        _lp.Clear();
        _ps.Clear();
        _sd.Clear();
    }

    private record LetterStat(char Ch, float X, float Y0, float W, float Cx);
    private record LetterPhys(float Y, float Vy, float Sm);
    private sealed class LetterPath(SKPath p) { public SKPath P { get; } = p; }
}
