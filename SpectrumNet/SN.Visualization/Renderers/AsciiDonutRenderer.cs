namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class AsciiDonutRenderer : Rotating3DRenderer<AsciiDonutRenderer.QS>
{
    private const float R = 2f, TR = 0.5f, Z0 = 3f;
    private const string CH = " .,-~:;=!*#$@";
    private const int SEG = 64;

    private static readonly Vector3 Ld = Vector3.Normalize(new(0.6f, 0.6f, -1f));
    private static readonly float[] Ct = Tbl(MathF.Cos);
    private static readonly float[] St = Tbl(MathF.Sin);
    private static readonly string[] ChS = CH.Select(c => c.ToString()).ToArray();

    private static readonly IReadOnlyDictionary<RenderQuality, QS> _pre = new Dictionary<RenderQuality, QS>
    {
        [RenderQuality.Low] = new(3, false, false, 0.8f, 0.15f),
        [RenderQuality.Medium] = new(1, true, true, 1f, 0.2f),
        [RenderQuality.High] = new(0, true, true, 1.2f, 0.25f)
    };

    public sealed record QS(int Skip, bool Light, bool ColVar, float Spd, float Lerp);

    protected override IReadOnlyDictionary<RenderQuality, QS> QualitySettingsPresets => _pre;

    private SKFont? _f;
    private Vtx[]? _v;
    private float _k = 1f;

    protected override int GetSpectrumProcessingCount(RenderParameters rp) => Min(rp.EffectiveBarCount, 16);
    protected override int GetMaxBarsForQuality() => GetBarsForQuality(32, 64, 128);

    protected override void OnInitialize()
    {
        base.OnInitialize();
        _f = new SKFont { Size = 18f, Hinting = SKFontHinting.None };
        _v = BuildV();
    }

    protected override void OnDispose()
    {
        _f?.Dispose();
        _f = null;
        _v = null;
        base.OnDispose();
    }

    protected override void RenderEffect(
        SKCanvas c, float[] s, SKImageInfo info, RenderParameters rp, SKPaint paint)
    {
        if (CurrentQualitySettings is not { } qs || _v is null || _f is null) return;

        float avg = CalculateAverageSpectrum(s);
        _k = Lerp(_k, 1f + avg * RotationInfluence, qs.Lerp);
        UpdateRotationWithSpectrum(s, _k * qs.Spd);

        SKPoint cen = GetCenter(info);
        float sc = GetMinDimension(info) * SelectByOverlay(0.4f, 0.35f);

        using Pooled<List<Vp>> lp = RentList<Vp>();
        List<Vp> a = lp.Value;

        if (!Proj(a, CalculateRotationMatrix(), sc, cen, qs, out SKRect bb)) return;
        if (!IsAreaVisible(c, bb)) return;

        Draw(c, a, paint.Color);
    }

    private static Vtx[] BuildV()
    {
        var a = new Vtx[SEG * SEG];
        int k = 0;
        for (int i = 0; i < SEG; i++)
        {
            float cti = Ct[i], sti = St[i];
            for (int j = 0; j < SEG; j++)
            {
                float rr = R + TR * Ct[j];
                a[k++] = new Vtx(rr * cti, rr * sti, TR * St[j]);
            }
        }
        return a;
    }

    private bool Proj(List<Vp> a, Matrix4x4 rot, float sc, SKPoint cen, QS qs, out SKRect bb)
    {
        a.Clear();
        var bounds = new BoundsBuilder();

        if (_v is not { Length: > 0 }) { bb = SKRect.Empty; return false; }

        int stp = Max(1, qs.Skip + 1);

        for (int i = 0; i < _v.Length; i += stp)
        {
            Vtx v = _v[i];
            Vector3 tr = TransformVertex(new Vector3(v.X, v.Y, v.Z), rot);
            float z = tr.Z + Z0;
            if (z <= 0.001f) continue;

            float iz = 1f / z;
            float x = tr.X * sc * iz + cen.X;
            float y = tr.Y * sc * iz + cen.Y;
            float li = qs.Light ? Max(0f, CalculateLighting(Vector3.Normalize(tr), Ld, 0f, 1f)) : 1f;

            a.Add(new Vp(x, y, z, li));
            bounds.Add(x, y);
        }

        if (!bounds.HasBounds) { bb = SKRect.Empty; return false; }

        a.Sort(static (x, y) => y.D.CompareTo(x.D));
        bb = bounds.Bounds;
        return true;
    }

    private void Draw(SKCanvas c, List<Vp> a, SKColor col)
    {
        float fs = SelectByOverlay(18f, 14f);
        float off = SelectByOverlay(4f, 3f);
        int last = CH.Length - 1;
        _f!.Size = fs;

        SKPaint p = CreatePaint(col, Fill);
        try
        {
            foreach (Vp v in a)
            {
                int ci = Clamp((int)(v.L * last), 0, last);
                float zn = Normalize(v.D, 2f, 4f);
                p.Color = col.WithAlpha(CalculateAlpha(0.2f + 0.8f * (1f - zn)));
                c.DrawText(ChS[ci], v.X - off, v.Y + off, SKTextAlign.Left, _f, p);
            }
        }
        finally { ReturnPaint(p); }
    }

    private static float[] Tbl(Func<float, float> f)
    {
        float[] t = new float[SEG];
        for (int i = 0; i < SEG; i++) t[i] = f(i * Tau / SEG);
        return t;
    }

    private readonly record struct Vtx(float X, float Y, float Z);
    private readonly record struct Vp(float X, float Y, float D, float L);
}
