namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class PixelGridRenderer : GridRenderer<PixelGridRenderer.QS>
{

    private const float
        PX = 12f, MAR = 2f, R = 2f, R_OV = 1.5f, S_MUL = 1.5f,
        DEC = 0.2f, RISE = 0.8f, P_FALL = 0.05f, DIM = 0.15f, BW = 0.5f, BW_OV = 0.3f, TOL = 0.25f;

    private const byte A_DIM = 40, A_PK = 255, A_B = 60;

    private static readonly IReadOnlyDictionary<RenderQuality, QS> _pre = new Dictionary<RenderQuality, QS>
    {
        [RenderQuality.Low] = new(true, true, false, 16, 0.3f),
        [RenderQuality.Medium] = new(true, true, false, 24, 0.2f),
        [RenderQuality.High] = new(true, true, true, 32, 0.15f)
    };

    public sealed record QS(bool Round, bool ShowPeaks, bool Borders, int MaxRows, float Sm);

    protected override IReadOnlyDictionary<RenderQuality, QS> QualitySettingsPresets => _pre;

    protected override RenderParameters CalculateRenderParameters(SKImageInfo info, int bc, float bw, float bs)
    {
        int req = Clamp(bc, 1, GetMaxBarsForQuality());
        float px = bw > 0f ? bw : PX, mar = bs >= 0f ? bs : MAR;
        float cell = px + mar;

        if (cell <= 0f || info.Width <= 0) return new(1, px, mar, 0f);

        int fit = (int)Floor((info.Width + mar) / cell);
        int cols = Clamp(Min(req, fit), 1, Min(MaxCols, GetMaxBarsForQuality()));
        float gridW = cols * cell - mar;
        float x0 = (info.Width - gridW) * 0.5f;

        return new(cols, px, mar, x0);
    }

    protected override int GetSpectrumProcessingCount(RenderParameters rp) => rp.EffectiveBarCount;
    protected override int GetMaxBarsForQuality() => GetBarsForQuality(32, 64, 96);

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();
        SetProcessingSmoothingFactor(CurrentQualitySettings!.Sm);
        Grid = default;
        RequestRedraw();
    }

    protected override void OnDispose()
    {
        Values = null;
        Peaks = null;
        base.OnDispose();
    }

    protected override void RenderEffect(
        SKCanvas c, float[] s, SKImageInfo info, RenderParameters rp, SKPaint paint)
    {
        if (CurrentQualitySettings is not { } qs) return;
        if (rp.EffectiveBarCount <= 0 || rp.BarWidth <= 0f) return;

        int cols = rp.EffectiveBarCount;
        float cell = rp.BarWidth + rp.BarSpacing;
        int rows = CalculateRows(info, cell, qs.MaxRows);
        if (rows <= 0) return;

        EnsureGridLayout(info, cols, rows, rp, cell);
        UpdateGrid(s, cols, rows, qs);

        DrawDim(c, paint.Color, qs);
        DrawOn(c, paint.Color, qs);
        if (qs.ShowPeaks) DrawPeaks(c, paint.Color, qs);
    }

    private void EnsureGridLayout(SKImageInfo info, int cols, int rows, RenderParameters rp, float cell)
    {
        float gridH = rows * cell - rp.BarSpacing;
        float x0 = rp.StartOffset;
        float y0 = Clamp(info.Height - gridH, 0f, Max(0f, info.Height - gridH));

        if (!Grid.Changed(cols, rows, cell, x0, y0, IsOverlayActive, TOL)) return;

        Grid.Set(cols, rows, rp.BarWidth, rp.BarSpacing, cell, x0, y0, IsOverlayActive);
        EnsureGridBuffers(cols, rows);
        ClearGridBuffers();
        RequestRedraw();
    }

    private void UpdateGrid(float[] s, int cols, int rows, QS qs)
    {
        if (Values == null || Peaks == null) return;
        int n = Min(cols, s.Length);

        for (int x = 0; x < n; x++)
        {
            float val = Clamp(s[x] * S_MUL, 0f, 1f);
            float tgt = val * rows;

            float cur = 0f;
            for (int y = 0; y < rows; y++) cur += Values[x][y];

            float sm = SmoothWithAttackRelease(cur, tgt, RISE, DEC);
            int full = (int)sm;
            float rem = sm - full;

            for (int y = 0; y < rows; y++)
                Values[x][y] = y < full ? 1f : (y == full && rem > 0f ? rem : 0f);

            if (qs.ShowPeaks) Peaks[x].Update(val, 0f, DeltaTime, P_FALL);
        }
    }

    private void DrawDim(SKCanvas c, SKColor col, QS qs)
    {
        if (Values == null) return;

        using Pooled<List<SKRect>> lr = RentList<SKRect>();
        lr.Value.Capacity = Max(lr.Value.Capacity, Grid.Cols * Grid.Rows);

        for (int x = 0; x < Grid.Cols; x++)
            for (int y = 0; y < Grid.Rows; y++)
                if (Values[x][y] == 0f) lr.Value.Add(CellRect(x, y));

        if (lr.Value.Count == 0) return;

        WithPaint(AdjustBrightness(col, DIM).WithAlpha(A_DIM), Fill, p => RenderRects(c, lr.Value, p, CornerRadius(qs)));
    }

    private void DrawOn(SKCanvas c, SKColor col, QS qs)
    {
        if (Values == null) return;

        using Pooled<List<SKRect>> lr = RentList<SKRect>();
        lr.Value.Capacity = Max(lr.Value.Capacity, Grid.Cols * Grid.Rows);

        for (int x = 0; x < Grid.Cols; x++)
            for (int y = 0; y < Grid.Rows; y++)
                if (Values[x][y] > 0f) lr.Value.Add(CellRect(x, y));

        if (lr.Value.Count == 0) return;

        WithPaint(col, Fill, p => RenderRects(c, lr.Value, p, CornerRadius(qs)));

        if (UseAdvancedEffects && qs.Borders)
            WithStroke(col.WithAlpha(A_B), SelectByOverlay(BW, BW_OV), p => RenderRects(c, lr.Value, p, CornerRadius(qs)));
    }

    private void DrawPeaks(SKCanvas c, SKColor col, QS qs)
    {
        if (Peaks == null) return;

        using Pooled<List<SKRect>> lr = RentList<SKRect>();
        lr.Value.Capacity = Max(lr.Value.Capacity, Grid.Cols);

        for (int x = 0; x < Grid.Cols; x++)
        {
            float pk = Peaks[x].Value;
            if (pk <= 0f) continue;
            int row = (int)(pk * Grid.Rows);
            if (row > 0 && row <= Grid.Rows) lr.Value.Add(CellRect(x, row - 1));
        }

        if (lr.Value.Count == 0) return;

        WithPaint(col.WithAlpha(A_PK), Fill, p => RenderRects(c, lr.Value, p, CornerRadius(qs)));
    }

    private float CornerRadius(QS qs) => qs.Round ? SelectByOverlay(R, R_OV) : 0f;

    private SKRect CellRect(int x, int y) =>
        new(Grid.X + x * Grid.Cell, Grid.Y + (Grid.Rows - y - 1) * Grid.Cell,
            Grid.X + x * Grid.Cell + Grid.Px, Grid.Y + (Grid.Rows - y - 1) * Grid.Cell + Grid.Px);
}
