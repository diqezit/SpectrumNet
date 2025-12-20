namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class NeonWaveRenderer : EffectSpectrumRenderer<NeonWaveRenderer.QS>
{
    private const float Pad0 = 14f, PadOv = 10f,
        AmpMul = 0.38f, EnvFloor = 0.12f,
        RibbonBase = 0.012f, RibbonMul = 0.030f,
        FillA = 0.18f, EdgeA = 0.45f, CoreA = 0.95f, GlowA = 0.18f,
        EdgeW = 0.95f, CoreW = 1.35f, GlowW = 3.1f,
        PeakHold = 0.08f, PeakFall = 2.5f,
        BrightMin = 0.85f, BrightMax = 1.25f;

    private const int MinPts = 24, MaxPts = 128, GlowThreshold = 64;

    private static readonly float[] GradPos = [0f, 0.22f, 0.5f, 0.78f, 1f];
    private static readonly SKColor[] Palette =
    [
        new(255, 70, 50), new(255, 200, 70), new(120, 255, 120),
        new(80, 220, 255), new(160, 120, 255)
    ];

    private static readonly IReadOnlyDictionary<RenderQuality, QS> Presets = new Dictionary<RenderQuality, QS>
    {
        [RenderQuality.Low] = new(0.85f, 0.50f, 0.010f, 0.20f, 8, false, false, 1),
        [RenderQuality.Medium] = new(0.95f, 0.65f, 0.018f, 0.24f, 14, true, false, 2),
        [RenderQuality.High] = new(1.00f, 0.75f, 0.022f, 0.26f, 20, true, true, 3)
    };

    public sealed record QS(
        float Amp,
        float Spd,
        float Jit,
        float Tint,
        int MaxStrands,
        bool UseQuad,
        bool UseGlow,
        int MaxComplexity);

    protected override IReadOnlyDictionary<RenderQuality, QS> QualitySettingsPresets => Presets;

    private record struct Strand(float Phase, float Speed, float FreqMul);

    private record struct WaveCtx(
        float Time,
        float Freq,
        float Phase,
        float TimePh,
        float Time125,
        float Ph16);

    private DimCache _dim;
    private AnimState _anim;
    private PeakTracker _peak;
    private SKShader? _shader;
    private SKColor _shaderColor;
    private float _shaderTint;
    private int _shaderW, _shaderH;

    private int _ptCount, _strandCount;
    private float _invM, _dx;
    private Strand[]? _strands;
    private SKPoint[]? _pts;
    private SKPoint[]? _rib;
    private float[]? _mags;
    private float[]? _env;

    protected override int GetMaxBarsForQuality() => GetBarsForQuality(64, 96, 128);

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();
        SetProcessingSmoothingFactor(GetSmoothingForQuality(0.40f, 0.34f, 0.28f, 1.1f));
        Reset();
        RequestRedraw();
    }

    protected override void OnDispose()
    {
        Reset();
        base.OnDispose();
    }

    private void Reset()
    {
        _shader?.Dispose();
        _shader = null;
        _strands = null;
        _pts = null;
        _rib = null;
        _mags = null;
        _env = null;
        _ptCount = _strandCount = 0;
        _dim = default;
        _anim = default;
        _peak.Reset();
    }

    protected override RenderParameters CalculateRenderParameters(
        SKImageInfo info,
        int bc,
        float bw,
        float bs) =>
        CalcStandardRenderParams(info, Clamp(bc, MinPts, MaxPts), bw, bs, MaxPts);

    protected override int GetSpectrumProcessingCount(RenderParameters rp) => rp.EffectiveBarCount;

    protected override void RenderEffect(
        SKCanvas c,
        float[] s,
        SKImageInfo info,
        RenderParameters rp,
        SKPaint paint)
    {
        if (CurrentQualitySettings is not { } qs) return;

        int n = Min(rp.EffectiveBarCount, s.Length);
        if (n < MinPts) return;

        _dim.Changed(info);

        int m = Clamp(n, MinPts, MaxPts);
        float t = (float)(m - MinPts) / (MaxPts - MinPts);
        int strands = Max(1, (int)(1 + t * (qs.MaxStrands - 1)));
        int complexity = Max(1, (int)(1 + t * (qs.MaxComplexity - 1)));
        bool glow = qs.UseGlow && m >= GlowThreshold;

        float pad = SelectByOverlay(Pad0, PadOv);
        float w = info.Width - pad * 2f;
        float h = info.Height - pad * 2f;
        if (w <= 2f || h <= 2f) return;

        float mid = info.Height * 0.5f;
        _dx = w / (m - 1f);
        _invM = 1f / (m - 1f);

        EnsureBuffers(m, strands);

        float avg = CalculateAverageSpectrum(s);
        _peak.Update(avg, PeakHold, DeltaTime, PeakFall);
        float smoothAvg = _peak.Value;

        _anim.AddTime(DeltaTime * qs.Spd * (0.55f + smoothAvg * 0.85f));

        EnsureShader(info, paint.Color, qs.Tint, smoothAvg);

        byte alpha = CalculateAlpha((0.70f + smoothAvg * 0.20f) * GetOverlayAlphaFactor());
        if (alpha < 6) return;

        float amp = h * AmpMul * qs.Amp;
        float th0 = MathF.Max(1f, h * RibbonBase);
        float th1 = h * RibbonMul * (0.65f + smoothAvg * 0.95f);

        Precompute(s, n, m);

        using Pooled<SKPath> pLine = RentPath();
        using Pooled<SKPath> pFill = RentPath();
        using var ps = new Paints(this, alpha, glow);

        DrawRibbon(c, pFill.Value, pLine.Value, ps, m, pad, mid, amp, th0, th1, qs.UseQuad, qs.Jit, complexity);

        if (strands > 1)
            DrawStrands(c, pLine.Value, ps, m, pad, h, mid, amp, strands, qs.Jit, qs.UseQuad, complexity);
    }

    private void EnsureBuffers(int m, int strands)
    {
        if (_ptCount != m)
        {
            _ptCount = m;
            EnsureArraySize(ref _pts, m);
            EnsureArraySize(ref _rib, m * 2);
            EnsureArraySize(ref _mags, m);
            EnsureArraySize(ref _env, m);
        }

        if (_strands == null || _strandCount != strands)
        {
            _strandCount = strands;
            _strands = new Strand[strands];
            for (int i = 0; i < strands; i++)
            {
                _strands[i] = new Strand(
                    RandFloat() * Tau + i * 0.35f,
                    0.7f + RandFloat() * 0.6f,
                    0.85f + RandFloat() * 0.3f);
            }
        }
    }

    private void Precompute(float[] s, int n, int m)
    {
        float scale = (n - 1f) / (m - 1f);
        for (int i = 0; i < m; i++)
        {
            float idx = i * scale;
            int i0 = (int)idx;
            int i1 = Min(i0 + 1, n - 1);
            _mags![i] = Clamp(s[i0] + (s[i1] - s[i0]) * (idx - i0), 0f, 1f);

            float t = i * _invM;
            float e = 1f - MathF.Abs(2f * t - 1f);
            _env![i] = Lerp(EnvFloor, 1f, e * e * (3f - 2f * e));
        }
    }

    private void EnsureShader(SKImageInfo info, SKColor col, float tint, float avg)
    {
        float tt = Clamp(tint, 0f, 1f);
        if (_shader != null &&
            _shaderW == info.Width &&
            _shaderH == info.Height &&
            _shaderColor == col &&
            MathF.Abs(_shaderTint - tt) <= 0.001f)
            return;

        _shader?.Dispose();
        float bright = Lerp(BrightMin, BrightMax, avg);
        var cols = new SKColor[5];
        for (int i = 0; i < 5; i++)
            cols[i] = AdjustBrightness(InterpolateColor(Palette[i], col, tt), bright);

        _shader = SKShader.CreateLinearGradient(
            new(0, 0),
            new(info.Width, 0),
            cols,
            GradPos,
            SKShaderTileMode.Clamp);

        _shaderW = info.Width;
        _shaderH = info.Height;
        _shaderColor = col;
        _shaderTint = tt;
    }

    private void DrawRibbon(
        SKCanvas c,
        SKPath pFill,
        SKPath pLine,
        Paints ps,
        int m,
        float x0,
        float mid,
        float amp,
        float th0,
        float th1,
        bool quad,
        float jit,
        int complexity)
    {
        BuildWave(m, x0, mid, amp, _strands![0], jit, complexity);
        BuildRibbon(m, th0, th1);
        BuildPath(pFill, _rib!, m, true, quad);

        if (!IsAreaVisible(c, pFill.Bounds))
        {
            pFill.Reset();
            return;
        }

        c.DrawPath(pFill, ps.Fill);
        BuildPath(pLine, _pts!, m, false, quad);

        if (ps.HasGlow) c.DrawPath(pLine, ps.Glow);
        c.DrawPath(pFill, ps.Edge);
        c.DrawPath(pLine, ps.Core);

        pLine.Reset();
        pFill.Reset();
    }

    private void DrawStrands(
        SKCanvas c,
        SKPath pLine,
        Paints ps,
        int m,
        float x0,
        float h,
        float mid,
        float amp,
        int count,
        float jit,
        bool quad,
        int complexity)
    {
        float inv = 1f / (count - 1f);
        for (int k = 1; k < count; k++)
        {
            float z = k * inv - 0.5f;
            byte a = (byte)(ps.Alpha * (0.52f - MathF.Abs(z) * 0.22f));
            if (a < 4) continue;

            BuildWave(m, x0, mid + z * h * 0.06f, amp, _strands![k], jit * 0.65f, complexity);
            BuildPath(pLine, _pts!, m, false, quad);

            ps.Core.Color = SKColors.White.WithAlpha(a);
            if (ps.HasGlow && a > 20)
            {
                ps.Glow.Color = SKColors.White.WithAlpha((byte)(a * 0.18f));
                c.DrawPath(pLine, ps.Glow);
            }
            c.DrawPath(pLine, ps.Core);
            pLine.Reset();
        }
    }

    private void BuildWave(
        int m,
        float x0,
        float mid,
        float amp,
        in Strand st,
        float jit,
        int complexity)
    {
        float time = _anim.Time * st.Speed;
        var ctx = new WaveCtx(
            time,
            st.FreqMul,
            st.Phase,
            time + st.Phase,
            time * 1.25f,
            st.Phase * 1.6f);

        for (int i = 0; i < m; i++)
        {
            float t = i * _invM;
            int mi = Min((int)(t * (m - 1) + 0.5f), m - 1);
            float a = amp * _env![i] * (0.18f + _mags![mi] * 0.82f);

            float y = mid + Wave(t * ctx.Freq, ctx, complexity) * a;
            if (jit > 0.001f && complexity >= 2)
                y += MathF.Sin(time * 2.6f + t * 18f + ctx.Phase * 2.1f) * a * jit;

            _pts![i] = new(x0 + i * _dx, y);
        }
    }

    private static float Wave(float tf, in WaveCtx c, int x) => x switch
    {
        >= 3 => MathF.Sin(tf * 5.2f + c.TimePh) * 0.62f +
                MathF.Sin(tf * 9.4f - c.Time125 + c.Ph16) * 0.28f +
                MathF.Sin(tf * 13.8f + c.Time * 0.7f - c.Phase * 0.7f) * 0.10f,
        2 => MathF.Sin(tf * 5.2f + c.TimePh) * 0.70f +
             MathF.Sin(tf * 9.4f - c.Time125 + c.Ph16) * 0.30f,
        _ => MathF.Sin(tf * 5.2f + c.TimePh)
    };

    private void BuildRibbon(int m, float th0, float th1)
    {
        float pnx = 0f, pny = 1f;
        for (int i = 0; i < m; i++)
        {
            float tx = _pts![Min(i + 1, m - 1)].X - _pts[Max(i - 1, 0)].X;
            float ty = _pts[Min(i + 1, m - 1)].Y - _pts[Max(i - 1, 0)].Y;
            float len = tx * tx + ty * ty;

            float nx, ny;
            if (len > 0.0001f)
            {
                float inv = 1f / MathF.Sqrt(len);
                nx = -ty * inv;
                ny = tx * inv;
                pnx = nx;
                pny = ny;
            }
            else
            {
                nx = pnx;
                ny = pny;
            }

            float th = th0 + th1 * MathF.Sqrt(_mags![i]);
            _rib![i] = new(_pts[i].X + nx * th, _pts[i].Y + ny * th);
            _rib[m + i] = new(_pts[i].X - nx * th, _pts[i].Y - ny * th);
        }
    }

    private static void BuildPath(SKPath p, SKPoint[] pts, int m, bool ribbon, bool quad)
    {
        p.Reset();
        if (m <= 0) return;

        p.MoveTo(pts[0]);
        Seg(p, pts, 0, m, quad, true);

        if (ribbon)
        {
            p.LineTo(pts[m - 1]);
            Seg(p, pts, m, m, quad, false);
            p.Close();
        }
    }

    private static void Seg(SKPath p, SKPoint[] pts, int off, int n, bool quad, bool fwd)
    {
        if (quad)
        {
            if (fwd)
            {
                for (int i = 1; i < n; i++)
                {
                    int j = off + i;
                    p.QuadTo(
                        pts[j - 1].X,
                        pts[j - 1].Y,
                        (pts[j - 1].X + pts[j].X) * 0.5f,
                        (pts[j - 1].Y + pts[j].Y) * 0.5f);
                }
            }
            else
            {
                for (int i = n - 1; i > 0; i--)
                {
                    int j = off + i;
                    p.QuadTo(
                        pts[j].X,
                        pts[j].Y,
                        (pts[j].X + pts[j - 1].X) * 0.5f,
                        (pts[j].Y + pts[j - 1].Y) * 0.5f);
                }
            }
        }
        else
        {
            if (fwd)
            {
                for (int i = 1; i < n; i++)
                    p.LineTo(pts[off + i]);
            }
            else
            {
                for (int i = n - 1; i >= 0; i--)
                    p.LineTo(pts[off + i]);
            }
        }
    }

    private readonly struct Paints : IDisposable
    {
        private readonly NeonWaveRenderer _r;
        public readonly byte Alpha;
        public readonly bool HasGlow;
        public readonly SKPaint Fill, Edge, Core, Glow;

        public Paints(NeonWaveRenderer r, byte a, bool glow)
        {
            _r = r;
            Alpha = a;
            HasGlow = glow && r.UseAdvancedEffects;
            SKShader? sh = r._shader;

            Fill = r.CreatePaint(SKColors.White.WithAlpha((byte)(a * FillA)), SKPaintStyle.Fill, sh);
            Edge = Stroke(r, (byte)(a * EdgeA), EdgeW, sh);
            Core = Stroke(r, (byte)(a * CoreA), CoreW, sh);
            Glow = HasGlow ? Stroke(r, (byte)(a * GlowA), GlowW, sh, SKBlendMode.Screen) : null!;
        }

        private static SKPaint Stroke(
            NeonWaveRenderer r,
            byte a,
            float w,
            SKShader? sh,
            SKBlendMode blend = SKBlendMode.SrcOver)
        {
            SKPaint p = r.CreateStrokePaint(SKColors.White.WithAlpha(a), w);
            p.Shader = sh;
            p.StrokeCap = SKStrokeCap.Round;
            p.StrokeJoin = SKStrokeJoin.Round;
            p.BlendMode = blend;
            return p;
        }

        public void Dispose()
        {
            Ret(_r, Fill);
            Ret(_r, Edge);
            Ret(_r, Core);
            if (HasGlow)
            {
                Glow.BlendMode = SKBlendMode.SrcOver;
                Ret(_r, Glow);
            }
        }

        private static void Ret(NeonWaveRenderer r, SKPaint p)
        {
            p.Shader = null;
            r.ReturnPaint(p);
        }
    }
}
