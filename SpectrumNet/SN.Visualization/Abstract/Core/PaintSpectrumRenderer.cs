#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Core;

public abstract class PaintSpectrumRenderer : ResourceSpectrumRenderer
{
    private readonly Dictionary<string, IPaintConfig> _paintConfigs = new();
    private readonly object _paintConfigLock = new();

    protected PaintSpectrumRenderer(
        ISpectrumProcessingCoordinator? processingCoordinator = null,
        IQualityManager? qualityManager = null,
        IOverlayStateManager? overlayStateManager = null,
        IRenderingHelpers? renderingHelpers = null,
        IBufferManager? bufferManager = null,
        ISpectrumBandProcessor? bandProcessor = null,
        IAnimationTimer? animationTimer = null,
        IResourceManager? resourceManager = null) : base(
            processingCoordinator,
            qualityManager,
            overlayStateManager,
            renderingHelpers,
            bufferManager,
            bandProcessor,
            animationTimer,
            resourceManager)
    {
    }

    protected void RegisterPaintConfig(string name, IPaintConfig config)
    {
        lock (_paintConfigLock)
        {
            _paintConfigs[name] = config;
        }
    }

    protected IPaintConfig GetPaintConfig(string name)
    {
        lock (_paintConfigLock)
        {
            return _paintConfigs.TryGetValue(name, out var config)
                ? config
                : PaintConfig.Default;
        }
    }

    protected void RemovePaintConfig(string name)
    {
        lock (_paintConfigLock)
        {
            _paintConfigs.Remove(name);
        }
    }

    protected void ClearPaintConfigs()
    {
        lock (_paintConfigLock)
        {
            _paintConfigs.Clear();
        }
    }

    protected IPaintConfig CreateDefaultPaintConfig(SKColor color) =>
        new PaintConfig(color);

    protected IPaintConfig CreateStrokePaintConfig(
        SKColor color,
        float strokeWidth,
        SKStrokeCap cap = SKStrokeCap.Round,
        SKStrokeJoin join = SKStrokeJoin.Round) =>
        new PaintConfig(color, Stroke, strokeWidth, cap, join);

    protected IPaintConfig CreateGlowPaintConfig(
        SKColor color,
        float blurRadius) =>
        PaintConfig.Glow(color, blurRadius);

    protected IPaintConfig CreateEdgePaintConfig(
        SKColor color,
        float width,
        float blurRadius = 0) =>
        PaintConfig.Edge(color, width, blurRadius);

    protected SKPaint CreatePaint(IPaintConfig config)
    {
        var paint = GetPaint();
        paint.Color = config.Color;
        paint.Style = config.Style;
        paint.IsAntialias = UseAntiAlias;

        if (config.Style == Stroke)
        {
            paint.StrokeWidth = config.StrokeWidth;
            paint.StrokeCap = config.StrokeCap;
            paint.StrokeJoin = config.StrokeJoin;
        }

        if (config.BlurRadius > 0)
            paint.ImageFilter = SKImageFilter.CreateBlur(
                config.BlurRadius,
                config.BlurRadius);

        if (config.MaskBlurRadius > 0)
            paint.MaskFilter = SKMaskFilter.CreateBlur(
                SKBlurStyle.Normal,
                config.MaskBlurRadius);

        return paint;
    }

    protected SKPaint CreatePaint(string configName) =>
        CreatePaint(GetPaintConfig(configName));

    protected SKPaint CreateStandardPaint(SKColor color)
    {
        var paint = GetPaint();
        paint.Color = color;
        paint.Style = Fill;
        paint.IsAntialias = UseAntiAlias;
        return paint;
    }

    protected SKPaint CreateStrokePaint(
        SKColor color,
        float strokeWidth,
        SKStrokeCap cap = SKStrokeCap.Round)
    {
        var paint = GetPaint();
        paint.Color = color;
        paint.Style = Stroke;
        paint.StrokeWidth = strokeWidth;
        paint.StrokeCap = cap;
        paint.IsAntialias = UseAntiAlias;
        return paint;
    }

    protected SKPaint CreateGlowPaint(
        SKColor color,
        float blurRadius) =>
        CreatePaint(PaintConfig.Glow(color, blurRadius));

    protected SKPaint CreateEdgePaint(
        SKColor color,
        float width,
        float blurRadius = 0) =>
        CreatePaint(PaintConfig.Edge(color, width, blurRadius));

    protected void ExecuteWithPaintConfig(
        string configName,
        SKCanvas canvas,
        Action<SKCanvas, SKPaint> operation)
    {
        var paint = CreatePaint(configName);
        try
        {
            operation(canvas, paint);
        }
        finally
        {
            ReturnPaint(paint);
        }
    }

    protected override void OnDispose()
    {
        ClearPaintConfigs();
        base.OnDispose();
    }
}