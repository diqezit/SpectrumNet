#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Helpers;

public record PaintConfig(
    SKColor Color,
    SKPaintStyle Style = SKPaintStyle.Fill,
    float StrokeWidth = 0,
    SKStrokeCap StrokeCap = SKStrokeCap.Butt,
    SKStrokeJoin StrokeJoin = SKStrokeJoin.Miter,
    float BlurRadius = 0,
    float MaskBlurRadius = 0) : IPaintConfig
{
    public IPaintConfig WithColor(SKColor color) => this with { Color = color };
    public IPaintConfig WithAlpha(byte alpha) => this with { Color = Color.WithAlpha(alpha) };
    public IPaintConfig WithStyle(SKPaintStyle style) => this with { Style = style };

    public IPaintConfig WithStroke(
        float width,
        SKStrokeCap? cap = null,
        SKStrokeJoin? join = null) =>
        this with
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = width,
            StrokeCap = cap ?? StrokeCap,
            StrokeJoin = join ?? StrokeJoin
        };

    public IPaintConfig WithBlur(float radius) => this with { BlurRadius = radius };
    public IPaintConfig WithMaskBlur(float radius) => this with { MaskBlurRadius = radius };

    public static IPaintConfig Default => new PaintConfig(SKColors.White);
    public static IPaintConfig DefaultStroke =>
        new PaintConfig(SKColors.White, SKPaintStyle.Stroke, 1f);

    public static IPaintConfig Glow(SKColor color, float blurRadius) =>
        new PaintConfig(color, BlurRadius: blurRadius);

    public static IPaintConfig Edge(SKColor color, float width, float blurRadius = 0) =>
        new PaintConfig(
            color,
            SKPaintStyle.Stroke,
            width,
            SKStrokeCap.Round,
            SKStrokeJoin.Round,
            blurRadius);
}