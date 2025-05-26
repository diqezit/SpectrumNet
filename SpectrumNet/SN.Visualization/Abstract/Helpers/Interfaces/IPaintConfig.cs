#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Helpers.Interfaces;

public interface IPaintConfig
{
    SKColor Color { get; }
    SKPaintStyle Style { get; }
    float StrokeWidth { get; }
    SKStrokeCap StrokeCap { get; }
    SKStrokeJoin StrokeJoin { get; }
    float BlurRadius { get; }
    float MaskBlurRadius { get; }

    IPaintConfig WithColor(SKColor color);
    IPaintConfig WithAlpha(byte alpha);
    IPaintConfig WithStyle(SKPaintStyle style);
    IPaintConfig WithStroke(float width, SKStrokeCap? cap = null, SKStrokeJoin? join = null);
    IPaintConfig WithBlur(float radius);
    IPaintConfig WithMaskBlur(float radius);
}