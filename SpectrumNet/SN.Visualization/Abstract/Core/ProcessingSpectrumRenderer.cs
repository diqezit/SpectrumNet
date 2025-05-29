#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Core;

public abstract class ProcessingSpectrumRenderer : CoreSpectrumRenderer
{
    [MethodImpl(AggressiveInlining)]
    protected static byte CalculateAlpha(float magnitude, float multiplier = 255f) =>
        magnitude <= 0 ? (byte)0 :
        magnitude >= 1 ? (byte)255 :
        (byte)MathF.Min(magnitude * multiplier, 255f);

    [MethodImpl(AggressiveInlining)]
    protected static float Lerp(float current, float target, float amount) =>
        current + (target - current) * Clamp(amount, 0f, 1f);

    [MethodImpl(AggressiveInlining)]
    protected static float Normalize(float value, float min, float max) =>
        max <= min ? 0f :
        Clamp((value - min) / (max - min), 0f, 1f);

    [MethodImpl(AggressiveInlining)]
    protected static bool IsAreaVisible(SKCanvas? canvas, SKRect rect) =>
        canvas == null || !canvas.QuickReject(rect);

    protected static SKRect GetBarRect(
        float x,
        float magnitude,
        float barWidth,
        float canvasHeight,
        float minHeight = 1f)
    {
        float barHeight = MathF.Max(magnitude * canvasHeight, minHeight);
        return new SKRect(
            x,
            canvasHeight - barHeight,
            x + barWidth,
            canvasHeight);
    }

    protected void RenderPath(
        SKCanvas canvas,
        Action<SKPath> buildPathAction,
        SKPaint paint)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(paint);
        var path = GetPath();
        try
        {
            buildPathAction(path);
            if (!path.IsEmpty)
            {
                canvas.DrawPath(path, paint);
            }
        }
        finally
        {
            ReturnPath(path);
        }
    }

    protected void RenderRects(
        SKCanvas canvas,
        IEnumerable<SKRect> rects,
        SKPaint paint,
        float cornerRadius = 0f)
    {
        RenderPath(canvas, path =>
        {
            path.Reset();
            foreach (var rect in rects)
            {
                if (!rect.IsEmpty)
                {
                    if (cornerRadius > 0f)
                    {
                        path.AddRoundRect(rect, cornerRadius, cornerRadius);
                    }
                    else
                    {
                        path.AddRect(rect);
                    }
                }
            }
        }, paint);
    }

    protected SKPaint CreatePaint(
        SKColor color,
        SKPaintStyle style = SKPaintStyle.Fill,
        SKShader? shader = null)
    {
        var paint = GetPaint();
        paint.Color = color;
        paint.IsAntialias = UseAntiAlias;
        paint.Style = style;
        paint.Shader = shader;
        paint.MaskFilter = null;
        return paint;
    }

    protected static float[] CreateUniformGradientPositions(int colorCount)
    {
        if (colorCount <= 0) return [];
        if (colorCount == 1) return [0f];
        var positions = new float[colorCount];
        for (int i = 0; i < colorCount; i++)
        {
            positions[i] = (colorCount == 1) ? 0f : i / (float)(colorCount - 1);
        }
        return positions;
    }
}