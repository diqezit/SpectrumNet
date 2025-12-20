namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class CubeRenderer : Rotating3DRenderer<CubeRenderer.QS>
{

    private const float
        CUBE = 0.5f, AMB = 0.4f, DIF = 0.6f, A0 = 0.9f, EDGE_A_MUL = 0.8f,
        L_VAR = 0.1f, L_MIN = 0.2f, L_MAX = 0.8f, H_S = 60f, EDGE_W = 2f, EDGE_W_OV = 1.5f,
        BL_M = 1f, BL_M_OV = 0.7f, SC = 0.3f, SC_OV = 0.25f, BC_MUL = 0.01f,
        SC_MIN = 0.5f, SC_MAX = 2.5f, INT_MUL = 0.4f;

    private static readonly Vector3 Ld = Vector3.Normalize(new(0.5f, 0.7f, -1f));
    private static readonly Vtx[] V = MkV();
    private static readonly Face[] F = MkF();

    private float _int;

    private static readonly IReadOnlyDictionary<RenderQuality, QS> _pre = new Dictionary<RenderQuality, QS>
    {
        [RenderQuality.Low] = new(false, false, 0, 1f, 0.8f, 0.2f),
        [RenderQuality.Medium] = new(true, true, 2, 1.5f, 1f, 0.3f),
        [RenderQuality.High] = new(true, true, 4, 2f, 1.2f, 0.4f)
    };

    public sealed record QS(bool Glow, bool EdgeHi, byte EdgeBlur, float EdgeW, float RotSpd, float Resp);

    protected override IReadOnlyDictionary<RenderQuality, QS> QualitySettingsPresets => _pre;
    protected override float RotationInfluence => 0.015f;
    protected override int GetSpectrumProcessingCount(RenderParameters rp) => Min(rp.EffectiveBarCount, 3);
    protected override int GetMaxBarsForQuality() => GetBarsForQuality(50, 100, 150);

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();
        ApplyStandardQualitySmoothing();
    }

    protected override void OnDispose()
    {
        _int = 0f;
        base.OnDispose();
    }

    protected override void RenderEffect(
        SKCanvas c, float[] s, SKImageInfo info, RenderParameters rp, SKPaint paint)
    {
        if (CurrentQualitySettings is not { } qs) return;

        float avg = CalculateAverageSpectrum(s);
        _int = Lerp(_int, avg, qs.Resp);
        UpdateRotationWithSpectrum(s, qs.RotSpd);

        SKPoint cen = GetCenter(info);
        float sc = Scale(info, rp.EffectiveBarCount, _int);
        Matrix4x4 rot = CalculateRotationMatrix();

        SKPoint[] pts = Proj(rot, sc, cen);
        FaceInfo[] faces = Faces(rot);

        SKRect bb = Bb(pts);
        if (!IsAreaVisible(c, bb)) return;

        DrawCube(c, pts, faces, paint.Color, qs);
    }

    private static SKPoint[] Proj(Matrix4x4 rot, float sc, SKPoint center)
    {
        var pts = new SKPoint[V.Length];
        for (int i = 0; i < V.Length; i++)
        {
            Vtx v = V[i];
            Vector3 r = TransformVertex(new(v.X, v.Y, v.Z), rot);
            pts[i] = ProjectToScreen(r, sc, center);
        }
        return pts;
    }

    private static FaceInfo[] Faces(Matrix4x4 rot)
    {
        var fi = new FaceInfo[F.Length];
        for (int i = 0; i < F.Length; i++)
        {
            Face f = F[i];
            Vector3 n = FaceN(f.Ci);
            Vector3 rn = TransformNormal(n, rot);
            fi[i] = new(f, rn.Z, CalculateLighting(rn, Ld, AMB, DIF));
        }
        Array.Sort(fi, static (a, b) => a.D.CompareTo(b.D));
        return fi;
    }

    private void DrawCube(SKCanvas c, SKPoint[] pts, FaceInfo[] faces, SKColor baseCol, QS qs)
    {
        SKPaint fill = CreatePaint(SKColors.Black, Fill);
        SKPaint? edge = null;
        SKMaskFilter? blur = null;

        try
        {
            if (UseAdvancedEffects && qs.EdgeHi)
            {
                edge = CreateStrokePaint(SKColors.White, qs.EdgeW * SelectByOverlay(EDGE_W, EDGE_W_OV));
                edge.StrokeJoin = SKStrokeJoin.Round;
                if (qs.Glow && qs.EdgeBlur > 0)
                {
                    blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, qs.EdgeBlur * SelectByOverlay(BL_M, BL_M_OV));
                    edge.MaskFilter = blur;
                }
            }

            foreach (FaceInfo fi in faces)
            {
                if (fi.D >= 0f) continue;

                SKColor fc = FaceCol(baseCol, fi.F.Ci);
                byte a = CalculateAlpha(A0 + _int * 0.1f);
                fill.Color = new SKColor((byte)(fc.Red * fi.L), (byte)(fc.Green * fi.L), (byte)(fc.Blue * fi.L), a);

                RenderPath(c, p => FacePath(p, pts, fi.F), fill);

                if (edge != null)
                {
                    edge.Color = SKColors.White.WithAlpha(CalculateAlpha(A0 * EDGE_A_MUL));
                    RenderPath(c, p => FacePath(p, pts, fi.F), edge);
                }
            }
        }
        finally
        {
            ReturnPaint(fill);
            if (edge != null) { edge.MaskFilter = null; blur?.Dispose(); ReturnPaint(edge); }
        }
    }

    private float Scale(SKImageInfo info, int bc, float inten)
    {
        float bcF = 1f + (bc - 50) * BC_MUL;
        float iF = 0.8f + inten * INT_MUL;
        float tot = Clamp(bcF * iF, SC_MIN, SC_MAX);
        return GetMinDimension(info) * SelectByOverlay(SC, SC_OV) * tot;
    }

    private static void FacePath(SKPath p, SKPoint[] pts, Face f)
    {
        p.MoveTo(pts[f.V1]);
        p.LineTo(pts[f.V2]);
        p.LineTo(pts[f.V3]);
        p.LineTo(pts[f.V4]);
        p.Close();
    }

    private static SKColor FaceCol(SKColor baseCol, int idx)
    {
        baseCol.ToHsl(out float h, out float s, out float l);
        h = (h + idx * H_S) % 360f;
        l = Clamp(l + (idx % 2 == 0 ? L_VAR : -L_VAR), L_MIN, L_MAX);
        return SKColor.FromHsl(h, s * 100f, l * 100f);
    }

    private static Vector3 FaceN(int idx) => idx switch
    {
        0 => Vector3.UnitZ,
        1 => -Vector3.UnitZ,
        2 => Vector3.UnitY,
        3 => -Vector3.UnitY,
        4 => Vector3.UnitX,
        5 => -Vector3.UnitX,
        _ => Vector3.Zero
    };

    private static SKRect Bb(SKPoint[] pts)
    {
        if (pts.Length == 0) return SKRect.Empty;
        var b = new BoundsBuilder();
        foreach (SKPoint p in pts) b.Add(p);
        return b.Bounds;
    }

    private static Vtx[] MkV() =>
    [
        new(-CUBE, -CUBE, CUBE), new(CUBE, -CUBE, CUBE), new(CUBE, CUBE, CUBE), new(-CUBE, CUBE, CUBE),
        new(-CUBE, -CUBE, -CUBE), new(CUBE, -CUBE, -CUBE), new(CUBE, CUBE, -CUBE), new(-CUBE, CUBE, -CUBE)
    ];

    private static Face[] MkF() =>
    [
        new(0, 1, 2, 3, 0), new(5, 4, 7, 6, 1), new(3, 2, 6, 7, 2),
        new(4, 5, 1, 0, 3), new(1, 5, 6, 2, 4), new(4, 0, 3, 7, 5)
    ];

    private readonly record struct Vtx(float X, float Y, float Z);
    private readonly record struct Face(int V1, int V2, int V3, int V4, int Ci);
    private readonly record struct FaceInfo(Face F, float D, float L);
}
