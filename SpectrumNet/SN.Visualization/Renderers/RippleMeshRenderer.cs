namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class RippleMeshRenderer : EffectSpectrumRenderer<RippleMeshRenderer.QS>
{
    private const float Pad0 = 18f, PadOv = 10f, ZSm = 0.28f,
        RidgeThr0 = 0.08f, RidgeThr1 = 0.25f, FillA = 0.07f, ShA = 0.18f, ShOff = 2.0f;

    private DimCache _d;
    private int _cx, _rx, _nex;

    private SKPoint[]? _b, _p, _src;
    private float[]? _z, _ph;
    private SKPath? _wire, _fill, _ridge;

    private static readonly IReadOnlyDictionary<RenderQuality, QS> _pre = new Dictionary<RenderQuality, QS>
    {
        [RenderQuality.Low] = new(28, 16, 8, false, false, 0.90f, 0.85f),
        [RenderQuality.Medium] = new(40, 24, 12, true, true, 1.00f, 1.00f),
        [RenderQuality.High] = new(56, 32, 16, true, true, 1.10f, 1.20f)
    };

    public sealed record QS(int Cols, int Rows, int Ex, bool Fill, bool Ridge, float Amp, float Spd);

    protected override IReadOnlyDictionary<RenderQuality, QS> QualitySettingsPresets => _pre;

    protected override int GetMaxBarsForQuality() => GetBarsForQuality(64, 128, 256);

    protected override int GetSpectrumProcessingCount(RenderParameters rp) =>
        Quality switch { RenderQuality.Low => 8, RenderQuality.High => 16, _ => 12 };

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();
        ApplyStandardQualitySmoothing();
        _d = default;
        _wire?.Reset(); _fill?.Reset(); _ridge?.Reset();
        RequestRedraw();
    }

    protected override void OnDispose()
    {
        _wire?.Dispose(); _fill?.Dispose(); _ridge?.Dispose();
        _wire = _fill = _ridge = null;
        _b = _p = _src = null; _z = _ph = null;
        _cx = _rx = _nex = 0; _d = default;
        base.OnDispose();
    }

    protected override void RenderEffect(SKCanvas c, float[] s, SKImageInfo info, RenderParameters rp, SKPaint paint)
    {
        if (CurrentQualitySettings is not { } qs) return;
        if (_d.Changed(info)) RequestRedraw();

        Ens(info, qs);
        if (_b == null || _p == null || _z == null || _src == null || _ph == null ||
            _wire == null || _fill == null || _ridge == null) return;

        float avg = CalculateAverageSpectrum(s), hi = Band(s, 0.75f, 1f);

        UpdSrc(s, qs, avg);
        UpdField(qs, avg, hi);
        BuildPaths(qs, hi);

        if (!IsAreaVisible(c, new SKRect(0f, 0f, info.Width, info.Height))) return;

        float af = GetOverlayAlphaFactor();
        SKColor baseC = paint.Color;
        SKColor fillC = AdjustBrightness(baseC, 0.55f).WithAlpha(CalculateAlpha(FillA * af));
        SKColor wireC = baseC.WithAlpha(CalculateAlpha((0.55f + avg * 0.35f) * af));
        SKColor ridgeC = SKColors.White.WithAlpha(CalculateAlpha((0.18f + hi * 0.35f) * af));

        if (UseAdvancedEffects && qs.Fill)
            WithPaint(fillC, Fill, pp => c.DrawPath(_fill, pp));

        if (UseAdvancedEffects)
        {
            WithPaint(SKColors.Black.WithAlpha(CalculateAlpha(ShA * af)), Stroke, pp =>
            {
                pp.StrokeWidth = SelectByOverlay(1.0f, 1.2f);
                pp.StrokeCap = SKStrokeCap.Round;
                int sc = c.Save();
                c.Translate(ShOff, ShOff);
                c.DrawPath(_wire, pp);
                c.RestoreToCount(sc);
            });
        }

        SKPaint wp = CreatePaint(wireC, Stroke);
        wp.StrokeWidth = SelectByOverlay(1.1f, 1.3f);
        wp.StrokeCap = SKStrokeCap.Round;
        try { c.DrawPath(_wire, wp); } finally { ReturnPaint(wp); }

        if (UseAdvancedEffects && qs.Ridge)
        {
            SKPaint hp = CreatePaint(ridgeC, Stroke);
            hp.StrokeWidth = SelectByOverlay(1.0f, 1.2f);
            hp.StrokeCap = SKStrokeCap.Round;
            try { c.DrawPath(_ridge, hp); } finally { ReturnPaint(hp); }
        }
    }

    private void Ens(SKImageInfo info, QS qs)
    {
        int cols = Max(4, qs.Cols), rows = Max(4, qs.Rows), ex = Max(4, qs.Ex);
        _wire ??= new SKPath(); _fill ??= new SKPath(); _ridge ??= new SKPath();

        if (_cx == cols && _rx == rows && _nex == ex && _b != null && _p != null &&
            _z != null && _src != null && _ph != null) return;

        _cx = cols; _rx = rows; _nex = ex;
        int n = cols * rows;
        _b = new SKPoint[n]; _p = new SKPoint[n]; _z = new float[n];
        _src = new SKPoint[ex]; _ph = new float[ex];

        float pad = SelectByOverlay(Pad0, PadOv);
        float x0 = pad, y0 = pad, x1 = info.Width - pad, y1 = info.Height - pad;
        if (x1 <= x0) x1 = x0 + 1f;
        if (y1 <= y0) y1 = y0 + 1f;

        float sx = (x1 - x0) / (cols - 1), sy = (y1 - y0) / (rows - 1);
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
            {
                float xx = x0 + x * sx, yy = y0 + y * sy;
                _b[y * cols + x] = _p[y * cols + x] = new SKPoint(xx, yy);
                _z[y * cols + x] = 0f;
            }

        var cen = new SKPoint((x0 + x1) * 0.5f, (y0 + y1) * 0.5f);
        float rad = MathF.Min(x1 - x0, y1 - y0) * 0.42f;
        for (int i = 0; i < ex; i++)
        {
            float a = i / (float)ex * Tau;
            _src[i] = new SKPoint(cen.X + MathF.Cos(a) * rad, cen.Y + MathF.Sin(a) * rad);
            _ph[i] = RandFloat() * Tau;
        }
    }

    private void UpdSrc(float[] s, QS qs, float avg)
    {
        if (_ph == null) return;
        float spd = qs.Spd * (0.55f + avg * 1.2f);
        for (int i = 0; i < _ph.Length; i++)
        {
            float m = i < s.Length ? s[i] : 0f;
            _ph[i] = WrapAngle(_ph[i] + spd * (0.5f + m * 1.5f) * DeltaTime);
        }
    }

    private void UpdField(QS qs, float avg, float hi)
    {
        if (_b == null || _p == null || _z == null || _src == null || _ph == null) return;

        int cols = _cx, rows = _rx;
        float md = MathF.Min(_d.FW, _d.FH);
        float amp = md * 0.030f * qs.Amp * (0.65f + avg * 1.1f);
        float k0 = 0.020f + hi * 0.045f;

        for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
            {
                int id = y * cols + x;
                SKPoint bp = _b[id];
                float z = 0f;

                for (int i = 0; i < _src.Length; i++)
                {
                    float dx = bp.X - _src[i].X, dy = bp.Y - _src[i].Y;
                    float d = MathF.Sqrt(dx * dx + dy * dy) + 1f;
                    z += MathF.Sin(d * k0 - _ph[i]) / (1f + d * 0.020f);
                }

                float vz = Lerp(_z[id], z * (0.55f + hi * 0.45f), ZSm);
                _z[id] = vz;
                _p[id] = new SKPoint(bp.X + vz * amp * 0.18f, bp.Y - vz * amp);
            }
    }

    private void BuildPaths(QS qs, float hi)
    {
        if (_p == null || _z == null || _wire == null || _fill == null || _ridge == null) return;

        int cols = _cx, rows = _rx;
        _wire.Reset(); _fill.Reset(); _ridge.Reset();

        for (int y = 0; y < rows; y++)
        {
            _wire.MoveTo(_p[y * cols]);
            for (int x = 1; x < cols; x++) _wire.LineTo(_p[y * cols + x]);
        }

        for (int x = 0; x < cols; x++)
        {
            _wire.MoveTo(_p[x]);
            for (int y = 1; y < rows; y++) _wire.LineTo(_p[y * cols + x]);
        }

        if (qs.Fill)
            for (int y = 0; y < rows - 1; y++)
                for (int x = 0; x < cols - 1; x++)
                {
                    int i00 = y * cols + x, i10 = i00 + 1, i11 = (y + 1) * cols + x + 1, i01 = (y + 1) * cols + x;
                    _fill.MoveTo(_p[i00]); _fill.LineTo(_p[i10]); _fill.LineTo(_p[i11]); _fill.LineTo(_p[i01]); _fill.Close();
                }

        float thr = Lerp(RidgeThr0, RidgeThr1, Clamp(hi, 0f, 1f));

        for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols - 1; x++)
            {
                int aa = y * cols + x, bb = aa + 1;
                if (MathF.Abs(_z[aa] - _z[bb]) > thr) { _ridge.MoveTo(_p[aa]); _ridge.LineTo(_p[bb]); }
            }

        for (int x = 0; x < cols; x++)
            for (int y = 0; y < rows - 1; y++)
            {
                int aa = y * cols + x, bb = (y + 1) * cols + x;
                if (MathF.Abs(_z[aa] - _z[bb]) > thr) { _ridge.MoveTo(_p[aa]); _ridge.LineTo(_p[bb]); }
            }
    }

    private static float Band(float[] s, float a0, float a1)
    {
        if (s.Length == 0) return 0f;
        int n = s.Length, aa = Clamp((int)(a0 * n), 0, n), bb = Clamp((int)(a1 * n), 0, n);
        if (bb <= aa) return 0f;
        float sum = 0f;
        for (int i = aa; i < bb; i++) sum += s[i];
        return sum / (bb - aa);
    }
}
