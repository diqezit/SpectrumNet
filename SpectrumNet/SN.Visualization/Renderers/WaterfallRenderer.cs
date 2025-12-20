namespace SpectrumNet.SN.Visualization.Renderers;

public sealed class WaterfallRenderer : BitmapBufferRenderer<WaterfallRenderer.QS>
{

    private const float MIN_SIG = 1e-6f, ZOOM = 2f, THR = 0.1f;
    private const int H0 = 256, H0_OV = 128, W0 = 1024, MIN_BC = 10, PAL = 256, DIV = 10;
    private const byte GRID_A = 40;

    private static readonly int[] Pal = InitPalette();

    private float[][]? _buf;
    private int _head;
    private float _bw, _bs;
    private int _bc;

    private static readonly IReadOnlyDictionary<RenderQuality, QS> _pre = new Dictionary<RenderQuality, QS>
    {
        [RenderQuality.Low] = new(false, false, 1f, 10f),
        [RenderQuality.Medium] = new(true, false, 1f, 10f),
        [RenderQuality.High] = new(true, true, 1f, 12f)
    };

    public sealed record QS(bool Grid, bool Mark, float W, float Sz);

    protected override IReadOnlyDictionary<RenderQuality, QS> QualitySettingsPresets => _pre;

    protected override RenderParameters CalculateRenderParameters(SKImageInfo info, int bc, float bw, float bs) =>
        CalcStandardRenderParams(info, bc, bw, bs, GetMaxBarsForQuality());

    protected override int GetSpectrumProcessingCount(RenderParameters rp) => rp.EffectiveBarCount;
    protected override int GetMaxBarsForQuality() => GetBarsForQuality(128, 256, 512);

    protected override void OnQualitySettingsApplied()
    {
        base.OnQualitySettingsApplied();
        ApplyStandardQualitySmoothing();
        int h = SelectByOverlay(H0, H0_OV);
        int w = _buf is { Length: > 0 } ? _buf[0].Length : W0;
        InitBuffer(h, w);
        RequestRedraw();
    }

    protected override void OnDispose()
    {
        _buf = null;
        _head = 0;
        _bw = _bs = 0f;
        _bc = 0;
        base.OnDispose();
    }

    protected override void RenderEffect(
        SKCanvas c, float[] s, SKImageInfo info, RenderParameters rp, SKPaint paint)
    {
        if (CurrentQualitySettings is not { } qs) return;
        if (rp.EffectiveBarCount <= 0 || rp.BarWidth <= 0f) return;

        UpdateParams(rp);
        if (_buf == null) return;

        Push(s);

        int h = _buf.Length, w = _buf[0].Length;
        EnsureBitmap(w, h);
        if (Bitmap == null) return;

        UpdateBitmap(w, h);

        SKRect dst = GetDestRect(info, rp);
        if (!IsAreaVisible(c, dst)) return;

        WithPaint(SKColors.White, Fill, p => c.DrawBitmap(Bitmap, dst, p));

        if (UseAdvancedEffects && rp.BarWidth > ZOOM && (qs.Grid || qs.Mark))
            DrawGrid(c, dst, rp, qs);
    }

    private void UpdateParams(RenderParameters rp)
    {
        bool ch = Abs(_bw - rp.BarWidth) > THR || Abs(_bs - rp.BarSpacing) > THR || _bc != rp.EffectiveBarCount;
        if (!ch) return;

        _bw = rp.BarWidth; _bs = rp.BarSpacing; _bc = rp.EffectiveBarCount;
        int w = OptimalWidth(_bc);
        int h = SelectByOverlay(H0, H0_OV);

        if (_buf == null || _buf.Length != h || _buf[0].Length != w)
            InitBuffer(h, w);
    }

    private void InitBuffer(int h, int w)
    {
        _buf = EnsureJaggedArray(ref _buf, h, w);
        for (int y = 0; y < h; y++) Array.Fill(_buf[y], MIN_SIG);
        _head = 0;
    }

    private void Push(float[] s)
    {
        if (_buf == null) return;
        float[] row = _buf[_head];

        if (s.Length == row.Length) Array.Copy(s, row, row.Length);
        else ResampleSpectrumMax(s, row, MIN_SIG);

        _head = (_head + 1) % _buf.Length;
    }

    private void UpdateBitmap(int w, int h)
    {
        if (Bitmap == null || _buf == null) return;

        IntPtr ptr = Bitmap.GetPixels();
        if (ptr == IntPtr.Zero) return;

        int n = w * h;
        int[] px = System.Buffers.ArrayPool<int>.Shared.Rent(n);

        try
        {
            FillPixels(px, w, h);
            Marshal.Copy(px, 0, ptr, n);
        }
        finally { System.Buffers.ArrayPool<int>.Shared.Return(px); }
    }

    private void FillPixels(int[] px, int w, int h)
    {
        if (_buf == null) return;
        int bh = _buf.Length;

        for (int y = 0; y < h; y++)
        {
            int bi = (_head + 1 + y) % bh;
            float[] row = _buf[bi];
            int off = y * w;

            for (int x = 0; x < w; x++)
            {
                float v = Clamp(row[x], 0f, 1f);
                px[off + x] = Pal[(int)(v * (PAL - 1))];
            }
        }
    }

    private void DrawGrid(SKCanvas c, SKRect dst, RenderParameters rp, QS qs)
    {
        if (qs.Grid) DrawLines(c, dst, rp, qs);
        if (qs.Mark && rp.BarWidth > ZOOM * 1.5f) DrawMarks(c, dst, rp, qs);
    }

    private void DrawLines(SKCanvas c, SKRect dst, RenderParameters rp, QS qs)
    {
        int step = Max(1, rp.EffectiveBarCount / DIV);
        float stride = rp.BarWidth + rp.BarSpacing;

        WithStroke(new SKColor(255, 255, 255, GRID_A), qs.W, p =>
            RenderPath(c, path =>
            {
                for (int i = 0; i < rp.EffectiveBarCount; i += step)
                {
                    float x = rp.StartOffset + i * stride + rp.BarWidth * 0.5f;
                    if (x < dst.Left || x > dst.Right) continue;
                    path.MoveTo(x, dst.Top);
                    path.LineTo(x, dst.Bottom);
                }
            }, p));
    }

    private void DrawMarks(SKCanvas c, SKRect dst, RenderParameters rp, QS qs)
    {
        using var tf = SKTypeface.FromFamilyName("Arial");
        using var f = new SKFont(tf, qs.Sz);

        int step = Max(1, rp.EffectiveBarCount / DIV);
        float stride = rp.BarWidth + rp.BarSpacing;

        WithPaint(SKColors.White, Fill, p =>
        {
            for (int i = 0; i < rp.EffectiveBarCount; i += step * 2)
            {
                float x = rp.StartOffset + i * stride + rp.BarWidth * 0.5f;
                if (x < dst.Left || x > dst.Right) continue;
                c.DrawText(i.ToString(), x, dst.Bottom - 5f, SKTextAlign.Center, f, p);
            }
        });
    }

    private static SKRect GetDestRect(SKImageInfo info, RenderParameters rp)
    {
        float w = rp.EffectiveBarCount * rp.BarWidth + (rp.EffectiveBarCount - 1) * rp.BarSpacing;
        float l = Clamp(rp.StartOffset, 0f, info.Width);
        float r = Clamp(rp.StartOffset + w, 0f, info.Width);
        if (r < l) (r, l) = (l, r);
        return new(l, 0f, r, info.Height);
    }

    private static int OptimalWidth(int bc)
    {
        int baseW = Max(bc, MIN_BC);
        int w = 1;
        while (w < baseW) w *= 2;
        return Max(w, W0 / 4);
    }

    private static int[] InitPalette()
    {
        int[] pal = new int[PAL];
        for (int i = 0; i < PAL; i++)
        {
            float t = i / (float)(PAL - 1);
            SKColor col = GetColor(t);
            pal[i] = (col.Alpha << 24) | (col.Red << 16) | (col.Green << 8) | col.Blue;
        }
        return pal;
    }

    private static SKColor GetColor(float t)
    {
        t = Clamp(t, 0f, 1f);
        if (t < 0.25f) { float k = t / 0.25f; return new(0, (byte)(k * 50), (byte)(50 + k * 205), 255); }
        if (t < 0.5f) { float k = (t - 0.25f) / 0.25f; return new(0, (byte)(50 + k * 205), 255, 255); }
        if (t < 0.75f) { float k = (t - 0.5f) / 0.25f; return new((byte)(k * 255), 255, (byte)(255 - k * 255), 255); }
        float k2 = (t - 0.75f) / 0.25f;
        return new(255, (byte)(255 - k2 * 255), 0, 255);
    }
}
