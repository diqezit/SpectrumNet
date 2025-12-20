namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class CircularWaveRenderer : CircularRenderer<CircularWaveRenderer.QS>
{

    private AnimState _anim;

    private static readonly IReadOnlyDictionary<RenderQuality, QS> _pre = new Dictionary<RenderQuality, QS>
    {
        [RenderQuality.Low] = new(16, false, 0f, 6f, 16, 0.5f, 2f, 30f, 0.45f, 1.5f, 0.8f, 0.5f, 0.7f, 1.5f, 0.3f, 0.1f, 5f, 0.01f),
        [RenderQuality.Medium] = new(64, true, 3f, 7f, 24, 0.5f, 2f, 30f, 0.45f, 1.5f, 1f, 0.5f, 0.7f, 1.5f, 0.3f, 0.1f, 6f, 0.01f),
        [RenderQuality.High] = new(128, true, 8f, 8f, 32, 0.5f, 2f, 30f, 0.45f, 1.5f, 1f, 0.5f, 0.7f, 1.5f, 0.3f, 0.1f, 6f, 0.01f)
    };

    public sealed record QS(
        int PointsPerCircle, bool UseGlow, float GlowRadius, float MaxStroke, int MaxRings,
        float RotationSpeed, float WaveSpeed, float CenterRadius, float MaxRadiusFactor,
        float MinStroke, float WaveInfluence, float GlowThreshold, float GlowFactor,
        float GlowWidthFactor, float RotationIntensityFactor, float WavePhaseOffset,
        float StrokeClampFactor, float MinMagnitudeThreshold);

    protected override IReadOnlyDictionary<RenderQuality, QS> QualitySettingsPresets => _pre;
    protected override int GetMaxBarsForQuality() => GetBarsForQuality(16, 24, 32);

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();
        ApplyStandardQualitySmoothing();
    }

    protected override void RenderEffect(
        SKCanvas c, float[] s, SKImageInfo info, RenderParameters rp, SKPaint paint)
    {
        if (CurrentQualitySettings is not { } qs) return;

        SKPoint center = GetCenter(info);
        float maxR = GetMinDimension(info) * qs.MaxRadiusFactor;

        UpdAnim(s, qs);
        EnsureDirections(qs.PointsPerCircle);

        int rings = Min(rp.EffectiveBarCount, qs.MaxRings);
        if (rings <= 0) return;

        float step = rp.BarWidth + rp.BarSpacing;

        for (int i = rings - 1; i >= 0; i--)
        {
            float m = RingMag(s, i, rings);
            if (m < qs.MinMagnitudeThreshold) continue;

            float r = RingR(i, step, m, qs);
            if (r <= 0f || r > maxR) continue;

            byte a = (byte)(255f * m * (1f - r / maxR));
            float sw = Clamp(qs.MinStroke + m * qs.StrokeClampFactor, qs.MinStroke, qs.MaxStroke);

            if (UseAdvancedEffects && qs.UseGlow && m > qs.GlowThreshold)
                Glow(c, center, r, sw, paint.Color, a, m, qs);

            Ring(c, center, r, sw, paint.Color, a);
        }
    }

    private void UpdAnim(float[] s, QS qs)
    {
        float avg = CalculateAverageSpectrum(s);
        _anim.UpdatePhase(qs.RotationSpeed * (1f + avg * qs.RotationIntensityFactor));
        _anim.AddTime(qs.WaveSpeed * DeltaTime);
    }

    private float RingR(int i, float step, float m, QS qs)
    {
        float br = qs.CenterRadius + i * step;
        float w = MathF.Sin(_anim.Time + i * qs.WavePhaseOffset + _anim.Phase) * m * step * qs.WaveInfluence;
        return br + w;
    }

    private static float RingMag(float[] s, int i, int rings)
    {
        int n = s.Length;
        if (n == 0 || rings <= 0) return 0f;
        int a = i * n / rings;
        int b = Min((i + 1) * n / rings, n);
        if (a >= b) return 0f;
        float sum = 0f;
        for (int k = a; k < b; k++) sum += s[k];
        return sum / (b - a);
    }

    private void Glow(SKCanvas c, SKPoint center, float r, float sw, SKColor col, byte a, float m, QS qs)
    {
        byte ga = (byte)(a * qs.GlowFactor);
        float sig = qs.GlowRadius * m;
        if (sig <= 0f || ga == 0) return;

        WithBlur(col.WithAlpha(ga), Stroke, sig, p =>
        {
            p.StrokeWidth = sw * qs.GlowWidthFactor;
            CirclePath(c, center, r, p);
        });
    }

    private void Ring(SKCanvas c, SKPoint center, float r, float sw, SKColor col, byte a)
    {
        SKPaint p = CreateStrokePaint(col.WithAlpha(a), sw);
        try { CirclePath(c, center, r, p); }
        finally { ReturnPaint(p); }
    }

    private void CirclePath(SKCanvas c, SKPoint center, float r, SKPaint p)
    {
        RenderPath(c, path =>
        {
            bool f = true;
            for (int i = 0; i < DirectionCount; i++)
            {
                SKPoint d = GetDirection(i);
                float x = center.X + d.X * r;
                float y = center.Y + d.Y * r;
                if (f) { path.MoveTo(x, y); f = false; }
                else path.LineTo(x, y);
            }
            path.Close();
        }, p);
    }
}
