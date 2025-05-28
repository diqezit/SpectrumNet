#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Core;

public abstract class ResourceSpectrumRenderer() : ProcessingSpectrumRenderer()
{
    private readonly ObjectPool<SKPaint> _paintPool = new(
        () => new SKPaint(),
        paint => paint.Reset(),
        initialCount: 2,
        maxSize: 5);

    private readonly ObjectPool<SKPath> _pathPool = new(
        () => new SKPath(),
        path => path.Reset(),
        initialCount: 2,
        maxSize: 5);

    private readonly Dictionary<string, PaintConfig> _configs = new()
    {
        ["default"] = new PaintConfig(SKColors.White),
        ["glow"] = new PaintConfig(SKColors.White, MaskBlurRadius: 10f),
        ["stroke"] = new PaintConfig(
            SKColors.White,
            SKPaintStyle.Stroke,
            2f,
            SKStrokeCap.Round,
            SKStrokeJoin.Round),
        ["edge"] = new PaintConfig(
            SKColors.White,
            SKPaintStyle.Stroke,
            1f,
            SKStrokeCap.Round,
            SKStrokeJoin.Round)
    };

    private readonly object _configLock = new();
    private bool _resourcesDisposed;

    protected SKPath GetResourcePath()
    {
        ObjectDisposedException.ThrowIf(_resourcesDisposed, this);
        return _pathPool.Get();
    }

    protected void ReturnResourcePath(SKPath path)
    {
        if (!_resourcesDisposed && path != null)
            _pathPool.Return(path);
    }

    protected SKPaint GetResourcePaint()
    {
        ObjectDisposedException.ThrowIf(_resourcesDisposed, this);
        return _paintPool.Get();
    }

    protected void ReturnResourcePaint(SKPaint paint)
    {
        if (!_resourcesDisposed && paint != null)
            _paintPool.Return(paint);
    }

    protected override void CleanupUnusedResources()
    {
        base.CleanupUnusedResources();
        _paintPool.Clear();
        _pathPool.Clear();
    }

    protected void RegisterPaintConfig(string name, PaintConfig config)
    {
        lock (_configLock)
            _configs[name] = config;
    }

    protected PaintConfig GetPaintConfig(string name)
    {
        lock (_configLock)
            return _configs.TryGetValue(name, out var config) ? config : _configs["default"];
    }

    protected SKPaint CreatePaint(string configName, bool antiAlias = true)
    {
        var config = GetPaintConfig(configName);
        return CreatePaint(config, antiAlias);
    }

    protected SKPaint CreatePaint(PaintConfig config, bool antiAlias = true)
    {
        var paint = GetResourcePaint();
        ApplyConfig(paint, config, antiAlias);
        return paint;
    }

    protected SKPaint CreatePaint(
        SKColor color,
        PaintStyle style = PaintStyle.Fill,
        float strokeWidth = 0,
        float blurRadius = 0)
    {
        var paint = GetResourcePaint();

        paint.Color = color;
        paint.IsAntialias = UseAntiAlias;

        switch (style)
        {
            case PaintStyle.Fill:
                paint.Style = SKPaintStyle.Fill;
                break;
            case PaintStyle.Stroke:
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = strokeWidth;
                paint.StrokeCap = SKStrokeCap.Round;
                paint.StrokeJoin = SKStrokeJoin.Round;
                break;
            case PaintStyle.StrokeAndFill:
                paint.Style = SKPaintStyle.StrokeAndFill;
                paint.StrokeWidth = strokeWidth;
                paint.StrokeCap = SKStrokeCap.Round;
                paint.StrokeJoin = SKStrokeJoin.Round;
                break;
        }

        if (blurRadius > 0)
            paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, blurRadius);

        return paint;
    }

    protected SKPaint CreateStandardPaint(SKColor color) =>
        CreatePaint(color, PaintStyle.Fill, 0, 0);

    protected SKPaint CreateFillPaint(SKColor color, bool _ = true) =>
        CreatePaint(color, PaintStyle.Fill, 0, 0);

    protected SKPaint CreateStrokePaint(SKColor color, float width) =>
        CreatePaint(color, PaintStyle.Stroke, width, 0);

    protected SKPaint CreateGlowPaint(SKColor color, float blurRadius) =>
        CreatePaint(color, PaintStyle.Fill, 0, blurRadius);

    private static void ApplyConfig(SKPaint paint, PaintConfig config, bool antiAlias)
    {
        paint.Color = config.Color;
        paint.Style = config.Style;
        paint.IsAntialias = antiAlias;

        if (config.Style == SKPaintStyle.Stroke || config.Style == SKPaintStyle.StrokeAndFill)
        {
            paint.StrokeWidth = config.StrokeWidth;
            paint.StrokeCap = config.StrokeCap;
            paint.StrokeJoin = config.StrokeJoin;
        }

        if (config.MaskBlurRadius > 0)
            paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, config.MaskBlurRadius);
    }

    protected static PaintConfig CreateGlowPaintConfig(SKColor color, float blurRadius) =>
        PaintConfig.Glow(color, blurRadius);

    protected static PaintConfig CreateEdgePaintConfig(
        SKColor color,
        float width,
        float blurRadius = 0) =>
        PaintConfig.Edge(color, width, blurRadius);

    protected static PaintConfig CreateStrokePaintConfig(
        SKColor color,
        float strokeWidth,
        SKStrokeCap cap = SKStrokeCap.Round,
        SKStrokeJoin join = SKStrokeJoin.Round) =>
        new(color, SKPaintStyle.Stroke, strokeWidth, cap, join);

    protected static PaintConfig CreateDefaultPaintConfig(SKColor color) =>
        new(color);

    protected override void OnDispose()
    {
        if (!_resourcesDisposed)
        {
            lock (_configLock)
                _configs.Clear();

            _paintPool.Dispose();
            _pathPool.Dispose();
            _resourcesDisposed = true;
        }

        base.OnDispose();
    }
}

public record PaintConfig(
    SKColor Color,
    SKPaintStyle Style = SKPaintStyle.Fill,
    float StrokeWidth = 0,
    SKStrokeCap StrokeCap = SKStrokeCap.Round,
    SKStrokeJoin StrokeJoin = SKStrokeJoin.Round,
    float MaskBlurRadius = 0)
{
    public PaintConfig WithAlpha(byte alpha) =>
        this with { Color = Color.WithAlpha(alpha) };

    public PaintConfig WithColor(SKColor color) =>
        this with { Color = color };

    public PaintConfig WithBlur(float radius) =>
        this with { MaskBlurRadius = radius };

    public PaintConfig WithStroke(float width) =>
        this with { Style = SKPaintStyle.Stroke, StrokeWidth = width };

    public static PaintConfig Default => new(SKColors.White);

    public static PaintConfig Glow(SKColor color, float blurRadius) =>
        new(color, MaskBlurRadius: blurRadius);

    public static PaintConfig Stroke(SKColor color, float width) =>
        new(color, SKPaintStyle.Stroke, width);

    public static PaintConfig Edge(
        SKColor color,
        float width,
        float blurRadius = 0) =>
        new(color, SKPaintStyle.Stroke, width, SKStrokeCap.Round, SKStrokeJoin.Round, blurRadius);
}

public enum PaintStyle
{
    Fill,
    Stroke,
    StrokeAndFill
}