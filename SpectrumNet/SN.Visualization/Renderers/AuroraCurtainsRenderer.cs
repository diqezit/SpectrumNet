namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class AuroraCurtainsRenderer : EffectSpectrumRenderer<AuroraCurtainsRenderer.QS>
{
    private const float Pad0 = 8f, PadOv = 5f;
    private const float OrbMinR = 4f, OrbMaxR = 22f;
    private const float TrailFade = 0.55f, PulseMin = 0.75f, PulseMax = 1.35f;
    private const float ConnectDist = 140f, ConnectAlpha = 0.22f;
    private const float OrbitSpeed = 0.25f, FloatSpeed = 0.12f;
    private const float RingAlpha = 0.15f, RingWidth = 1.5f;
    private const float CoreGlow = 0.4f, HaloSize = 2.2f;
    private const float LerpSmooth = 0.06f, AlphaSmooth = 0.1f;
    private const float MinVisibleAlpha = 0.2f, ConnectMinAlpha = 0.3f;

    private static readonly IReadOnlyDictionary<RenderQuality, QS> _pre = new Dictionary<RenderQuality, QS>
    {
        [RenderQuality.Low] = new(0.35f, 2, false, false, 0.75f, 0.45f),
        [RenderQuality.Medium] = new(0.45f, 3, true, true, 0.95f, 0.65f),
        [RenderQuality.High] = new(0.55f, 4, true, true, 1.15f, 0.85f)
    };

    public sealed record QS(float OrbRatio, int TrailSegs, bool Connect, bool Rings, float Scale, float Spd);

    protected override IReadOnlyDictionary<RenderQuality, QS> QualitySettingsPresets => _pre;

    private DimCache _dim;
    private AnimState _anim;
    private int _n;
    private Orb[]? _orbs;
    private PeakTracker _peakLo, _peakMi, _peakHi;

    protected override int GetMaxBarsForQuality() => GetBarsForQuality(24, 48, 80);

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();
        ApplyStandardQualitySmoothing();
        ResetState();
        RequestRedraw();
    }

    protected override void OnDispose()
    {
        _orbs = null;
        base.OnDispose();
    }

    private void ResetState()
    {
        _anim = default;
        _peakLo.Reset();
        _peakMi.Reset();
        _peakHi.Reset();
        _orbs = null;
    }

    protected override void RenderEffect(SKCanvas c, float[] s, SKImageInfo info, RenderParameters rp, SKPaint paint)
    {
        if (CurrentQualitySettings is not { } qs) return;

        int n = Max(4, (int)(rp.EffectiveBarCount * qs.OrbRatio));
        if (_dim.Changed(info) || _n != n) _orbs = null;

        EnsureOrbs(info, n);
        if (_orbs == null) return;

        UpdateState(s, qs);

        float pad = SelectByOverlay(Pad0, PadOv);
        float w = info.Width - pad * 2f, h = info.Height - pad * 2f;
        SKPoint center = new(pad + w * 0.5f, pad + h * 0.5f);

        UpdateOrbs(s, center, w, h, qs);

        RenderLayers(
            qs.Rings ? () => DrawRings(c, center, w, h, paint.Color) : null,
            () => DrawEffects(c, paint.Color, qs),
            () => DrawOrbs(c, paint.Color));
    }

    private void UpdateState(float[] s, QS qs)
    {
        _peakLo.Update(Band(s, 0f, 0.25f), 0.15f, DeltaTime, 1.5f);
        _peakMi.Update(Band(s, 0.25f, 0.65f), 0.12f, DeltaTime, 1.8f);
        _peakHi.Update(Band(s, 0.65f, 1f), 0.1f, DeltaTime, 2.2f);

        float avg = (_peakLo.Value + _peakMi.Value + _peakHi.Value) / 3f;
        _anim.UpdatePhase(qs.Spd * (0.5f + avg * 0.5f));
        _anim.AddTime(DeltaTime);
    }

    private void UpdateOrbs(float[] s, SKPoint center, float w, float h, QS qs)
    {
        float maxR = MathF.Min(w, h) * 0.4f;
        float time = _anim.Time;

        for (int i = 0; i < _n; i++)
        {
            Orb orb = _orbs![i];
            float spec = i < s.Length ? s[i] : 0f;

            UpdateOrbMotion(ref orb, center, maxR, time, spec, qs);
            UpdateOrbVisuals(ref orb, time, spec, qs);

            _orbs[i] = orb;
        }
    }

    private void UpdateOrbMotion(ref Orb orb, SKPoint center, float maxR, float time, float spec, QS qs)
    {
        orb.Angle = WrapAngle(orb.Angle + (OrbitSpeed + spec * 0.5f) * qs.Spd * DeltaTime * orb.Dir);

        float phase = time * FloatSpeed + orb.Phase;
        float floatX = MathF.Cos(phase * 0.7f + orb.Phase) * 12f * (1f + _peakHi.Value * 0.6f);
        float floatY = MathF.Sin(phase) * 18f * (1f + _peakLo.Value * 0.8f);

        float breathe = 1f + MathF.Sin(time * 0.4f + orb.Phase * 0.5f) * 0.08f;
        float r = orb.BaseRadius * maxR * breathe;
        float wobble = MathF.Sin(time * 1.5f + orb.Phase * 2f) * 0.03f * _peakHi.Value;

        float targetX = center.X + MathF.Cos(orb.Angle + wobble) * r * orb.ElipseX + floatX;
        float targetY = center.Y + MathF.Sin(orb.Angle + wobble) * r * orb.ElipseY + floatY;

        orb.PrevX = orb.X;
        orb.PrevY = orb.Y;
        orb.X = Lerp(orb.X, targetX, LerpSmooth);
        orb.Y = Lerp(orb.Y, targetY, LerpSmooth);
    }

    private static void UpdateOrbVisuals(ref Orb orb, float time, float spec, QS qs)
    {
        float pulse = MathF.Sin(time * 2.5f + orb.Phase) * 0.5f + 0.5f;
        float sizeBase = Lerp(OrbMinR, OrbMaxR, orb.BaseSize) * qs.Scale;
        orb.Size = sizeBase * Lerp(PulseMin, PulseMax, pulse * Clamp(spec * 1.2f, 0f, 1f));
        orb.Alpha = Lerp(orb.Alpha, Clamp(0.4f + spec * 0.6f, 0.25f, 1f), AlphaSmooth);
    }

    private void DrawEffects(SKCanvas c, SKColor col, QS qs)
    {
        if (qs.Connect) DrawConnections(c, col, qs);
        DrawTrails(c, col, qs);
    }

    private void DrawRings(SKCanvas c, SKPoint center, float w, float h, SKColor col)
    {
        float maxR = MathF.Min(w, h) * 0.42f;
        byte baseA = CalculateAlpha(RingAlpha * GetOverlayAlphaFactor() * (0.6f + _peakMi.Value * 0.4f));
        if (baseA < 5) return;

        float time = _anim.Time;
        for (int i = 0; i < 3; i++)
        {
            float t = (i + 1) / 4f;
            float r = maxR * t * (1f + MathF.Sin(time * 0.8f + i * 0.7f) * 0.05f * _peakLo.Value);

            WithStroke(AdjustBrightness(col, 0.7f + t * 0.3f).WithAlpha((byte)(baseA * (1f - t * 0.4f))),
                RingWidth * (1f - t * 0.3f), p =>
                {
                    p.PathEffect = SKPathEffect.CreateDash([4f + i * 2f, 3f + i], time * 20f);
                    c.DrawCircle(center, r, p);
                    p.PathEffect = null;
                });
        }
    }

    private void DrawConnections(SKCanvas c, SKColor col, QS qs)
    {
        float maxDistSq = ConnectDist * ConnectDist * qs.Scale * qs.Scale;
        byte baseA = CalculateAlpha(ConnectAlpha * GetOverlayAlphaFactor() * (0.7f + _peakMi.Value * 0.3f));
        if (baseA < 4) return;

        SKPaint p = CreateStrokePaint(col.WithAlpha(baseA), 1.2f, SKStrokeCap.Round);
        try
        {
            for (int i = 0; i < _n; i++)
            {
                Orb a = _orbs![i];
                if (a.Alpha < ConnectMinAlpha) continue;

                for (int j = i + 1; j < _n; j++)
                {
                    Orb b = _orbs[j];
                    if (b.Alpha < ConnectMinAlpha) continue;

                    float d2 = DistSq(a.X, a.Y, b.X, b.Y);
                    if (d2 >= maxDistSq || d2 < 4f) continue;

                    float t = 1f - MathF.Sqrt(d2) / (ConnectDist * qs.Scale);
                    byte lineA = (byte)(baseA * t * a.Alpha * b.Alpha);
                    if (lineA < 3) continue;

                    p.Color = col.WithAlpha(lineA);
                    p.StrokeWidth = 0.8f + t * a.Alpha * b.Alpha * 1.2f;
                    c.DrawLine(a.X, a.Y, b.X, b.Y, p);
                }
            }
        }
        finally { ReturnPaint(p); }
    }

    private void DrawTrails(SKCanvas c, SKColor col, QS qs)
    {
        if (qs.TrailSegs < 2) return;

        byte baseA = CalculateAlpha(TrailFade * GetOverlayAlphaFactor());
        if (baseA < 4) return;

        SKPaint p = CreateStrokePaint(col, 2f, SKStrokeCap.Round);
        try
        {
            for (int i = 0; i < _n; i++)
            {
                Orb orb = _orbs![i];
                if (orb.Alpha < MinVisibleAlpha) continue;

                float dx = orb.X - orb.PrevX, dy = orb.Y - orb.PrevY;
                float len = MathF.Sqrt(dx * dx + dy * dy);
                if (len < 0.5f) continue;

                DrawSingleTrail(c, p, col, baseA, orb, dx / len, dy / len,
                    MathF.Min(len * 0.6f, orb.Size * 3f) * (0.8f + orb.Alpha * 0.4f), qs.TrailSegs);
            }
        }
        finally { ReturnPaint(p); }
    }

    private static void DrawSingleTrail(SKCanvas c, SKPaint p, SKColor col, byte baseA,
        Orb orb, float nx, float ny, float len, int segs)
    {
        float segInv = 1f / (segs - 1);
        for (int j = 0; j < segs - 1; j++)
        {
            float t0 = j * segInv, t1 = (j + 1) * segInv;
            float fade = (1f - t0) * (1f - t0);
            byte a = (byte)(baseA * fade * orb.Alpha);
            if (a < 2) continue;

            p.Color = col.WithAlpha(a);
            p.StrokeWidth = orb.Size * 0.35f * fade;
            c.DrawLine(orb.X - nx * len * t0, orb.Y - ny * len * t0,
                       orb.X - nx * len * t1, orb.Y - ny * len * t1, p);
        }
    }

    private void DrawOrbs(SKCanvas c, SKColor col)
    {
        byte baseA = CalculateAlpha(0.92f * GetOverlayAlphaFactor());

        for (int i = 0; i < _n; i++)
        {
            Orb orb = _orbs![i];
            byte a = (byte)(baseA * orb.Alpha);
            if (a < 8) continue;

            if (UseAdvancedEffects && orb.Alpha > 0.5f)
                DrawGlow(c, col, orb, a);

            DrawCore(c, col, orb, a);
            DrawHighlights(c, orb, a);
        }
    }

    private void DrawGlow(SKCanvas c, SKColor col, Orb orb, byte a)
    {
        byte ga = (byte)(a * CoreGlow * (orb.Alpha - 0.3f));
        WithBlur(col.WithAlpha(ga), Fill, orb.Size * 0.7f, p =>
            c.DrawCircle(orb.X, orb.Y, orb.Size * HaloSize, p));
    }

    private void DrawCore(SKCanvas c, SKColor col, Orb orb, byte a)
    {
        float x = orb.X, y = orb.Y, r = orb.Size;
        SKColor[] colors = [
            AdjustBrightness(col, 1.4f).WithAlpha(a),
            col.WithAlpha((byte)(a * 0.85f)),
            AdjustBrightness(col, 0.6f).WithAlpha((byte)(a * 0.5f))
        ];

        WithShader(col, Fill, () =>
            SKShader.CreateRadialGradient(new SKPoint(x - r * 0.25f, y - r * 0.25f), r * 1.3f,
                colors, [0f, 0.5f, 1f], SKShaderTileMode.Clamp),
            p => c.DrawCircle(x, y, r, p));
    }

    private void DrawHighlights(SKCanvas c, Orb orb, byte a)
    {
        float x = orb.X, y = orb.Y, r = orb.Size;

        if (orb.Alpha > 0.6f)
            WithPaint(SKColors.White.WithAlpha((byte)(a * (orb.Alpha - 0.6f) * 1.75f)), Fill, p =>
                c.DrawCircle(x - r * 0.3f, y - r * 0.3f, r * 0.22f, p));

        if (r > 8f && orb.Alpha > 0.4f)
            WithStroke(SKColors.White.WithAlpha((byte)(a * 0.25f)), 0.8f, p =>
                c.DrawCircle(x, y, r * 1.15f, p));
    }

    private void EnsureOrbs(SKImageInfo info, int n)
    {
        if (_n == n && _orbs != null) return;

        _n = n;
        _orbs = new Orb[n];
        float cx = info.Width * 0.5f, cy = info.Height * 0.5f;

        for (int i = 0; i < n; i++)
            _orbs[i] = CreateOrb(i, n, cx, cy);
    }

    private static Orb CreateOrb(int i, int n, float cx, float cy) => new()
    {
        Angle = i / (float)n * Tau + RandFloat() * 0.6f,
        BaseRadius = 0.25f + RandFloat() * 0.65f,
        BaseSize = 0.15f + RandFloat() * 0.85f,
        Phase = RandFloat() * Tau,
        ElipseX = 0.65f + RandFloat() * 0.7f,
        ElipseY = 0.65f + RandFloat() * 0.7f,
        Dir = RandChance(0.5f) ? 1f : -1f,
        X = cx + RandFloat(-50f, 50f),
        Y = cy + RandFloat(-50f, 50f),
        PrevX = cx,
        PrevY = cy,
        Size = OrbMinR,
        Alpha = 0.4f
    };

    private static float DistSq(float x1, float y1, float x2, float y2)
    {
        float dx = x2 - x1, dy = y2 - y1;
        return dx * dx + dy * dy;
    }

    private static float Band(float[] s, float lo, float hi)
    {
        if (s.Length == 0) return 0f;
        int a = Clamp((int)(lo * s.Length), 0, s.Length);
        int b = Clamp((int)(hi * s.Length), a, s.Length);
        if (b <= a) return 0f;

        float sum = 0f;
        for (int i = a; i < b; i++) sum += s[i];
        return sum / (b - a);
    }

    private struct Orb
    {
        public float Angle, BaseRadius, BaseSize, Phase;
        public float ElipseX, ElipseY, Dir;
        public float X, Y, PrevX, PrevY;
        public float Size, Alpha;
    }
}
