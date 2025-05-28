#nullable enable

using static System.MathF;

namespace SpectrumNet.SN.Visualization.Abstract.Core;

public abstract class ProcessingSpectrumRenderer(
    ISpectrumProcessor? spectrumProcessor = null,
    IResourcePool? resourcePool = null)
    : CoreSpectrumRenderer(spectrumProcessor, resourcePool)
{
    private SKPath? _currentPath;

    [MethodImpl(AggressiveInlining)]
    protected static byte CalculateAlpha(float magnitude, float multiplier = 255f) =>
        magnitude <= 0 ? (byte)0 :
        magnitude >= 1 ? (byte)255 :
        (byte)MathF.Min(magnitude * multiplier, 255);

    [MethodImpl(AggressiveInlining)]
    protected static float Lerp(float current, float target, float amount) =>
        current + (target - current) * Clamp(amount, 0f, 1f);

    [MethodImpl(AggressiveInlining)]
    protected static float Normalize(float value, float min, float max) =>
        max <= min ? 0f : Clamp((value - min) / (max - min), 0f, 1f);

    protected static float GetAverageInRange(float[] array, int start, int end)
    {
        if (array == null || array.Length == 0)
            return 0f;

        start = Max(0, start);
        end = Min(array.Length, end);
        if (start >= end)
            return 0f;

        float sum = 0f;
        for (int i = start; i < end; i++)
            sum += array[i];
        return sum / (end - start);
    }

    [MethodImpl(AggressiveInlining)]
    protected static bool IsAreaVisible(SKCanvas? canvas, SKRect rect) =>
        canvas == null || !canvas.QuickReject(rect);

    [MethodImpl(AggressiveInlining)]
    protected static bool IsRenderAreaVisible(
        SKCanvas? canvas,
        float x,
        float y,
        float width,
        float height) =>
        canvas == null || !canvas.QuickReject(new SKRect(x, y, x + width, y + height));

    protected static SKRect GetBarRect(
        float x,
        float magnitude,
        float barWidth,
        float canvasHeight,
        float minHeight = 1f)
    {
        float barHeight = MathF.Max(magnitude * canvasHeight, minHeight);
        return new SKRect(x, canvasHeight - barHeight, x + barWidth, canvasHeight);
    }

    protected static SKPoint[] CreateCirclePoints(int pointCount, float radius, SKPoint center)
    {
        if (pointCount <= 0)
            throw new ArgumentException("Point count must be positive", nameof(pointCount));

        var points = new SKPoint[pointCount];
        float angleStep = MathF.Tau / pointCount;

        for (int i = 0; i < pointCount; i++)
        {
            float angle = i * angleStep;
            points[i] = new SKPoint(
                center.X + radius * Cos(angle),
                center.Y + radius * Sin(angle));
        }
        return points;
    }

    protected static Vector2[] CreateCircleVectors(int count)
    {
        if (count <= 0)
            throw new ArgumentException("Count must be positive", nameof(count));

        var vectors = new Vector2[count];
        float angleStep = MathF.Tau / count;

        for (int i = 0; i < count; i++)
        {
            float angle = i * angleStep;
            vectors[i] = new Vector2(Cos(angle), Sin(angle));
        }
        return vectors;
    }

    protected void RenderGlow(
        SKCanvas canvas,
        SKRect rect,
        SKColor color,
        float radius,
        float alpha)
    {
        var paint = ResourcePool.GetPaint();
        try
        {
            paint.Color = color.WithAlpha((byte)(alpha * 255));
            paint.Style = SKPaintStyle.Fill;
            paint.IsAntialias = true;
            paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, radius);
            canvas.DrawRect(rect, paint);
        }
        finally
        {
            ResourcePool.ReturnPaint(paint);
        }
    }

    protected void RenderGlow(
        SKCanvas canvas,
        SKPath path,
        SKColor color,
        float radius,
        float alpha)
    {
        if (path.IsEmpty)
            return;

        var paint = ResourcePool.GetPaint();
        try
        {
            paint.Color = color.WithAlpha((byte)(alpha * 255));
            paint.Style = SKPaintStyle.Fill;
            paint.IsAntialias = true;
            paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, radius);
            canvas.DrawPath(path, paint);
        }
        finally
        {
            ResourcePool.ReturnPaint(paint);
        }
    }

    protected void RenderBatch(
        SKCanvas canvas,
        Action<SKPath> buildPath,
        SKPaint paint)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(paint);

        var path = _currentPath ??= ResourcePool.GetPath();
        path.Reset();
        buildPath(path);
        if (!path.IsEmpty)
            canvas.DrawPath(path, paint);
    }

    protected void RenderRects(
        SKCanvas canvas,
        IEnumerable<SKRect> rects,
        SKPaint paint,
        float cornerRadius = 0) =>
        RenderBatch(canvas, path =>
        {
            foreach (var rect in rects)
                if (cornerRadius > 0)
                    path.AddRoundRect(rect, cornerRadius, cornerRadius);
                else
                    path.AddRect(rect);
        }, paint);

    protected void RenderCircles(
        SKCanvas canvas,
        IEnumerable<(SKPoint center, float radius)> circles,
        SKPaint paint) =>
        RenderBatch(canvas, path =>
        {
            foreach (var (center, radius) in circles)
                path.AddCircle(center.X, center.Y, radius);
        }, paint);

    protected SKColor ApplyAlpha(SKColor color, float magnitude, float multiplier = 1f) =>
        color.WithAlpha(CalculateAlpha(magnitude, multiplier * 255f));

    protected SKColor InterpolateColor(
        SKColor baseColor,
        float magnitude,
        float minAlpha = 0.1f,
        float maxAlpha = 1f) =>
        baseColor.WithAlpha((byte)(Lerp(minAlpha, maxAlpha, magnitude) * 255));

    protected bool IsRectVisible(SKCanvas? canvas, SKRect rect) =>
        IsAreaVisible(canvas, rect);

    protected static float CalculateBarX(
        int index,
        float barWidth,
        float barSpacing,
        float startOffset = 0) =>
        startOffset + index * (barWidth + barSpacing);

    protected bool ValidateRenderParameters(
        SKCanvas? canvas,
        float[]? spectrum,
        SKImageInfo info,
        float _1,
        float _2,
        int barCount,
        SKPaint? paint)
    {
        if (!IsInitialized || canvas == null || spectrum == null ||
            spectrum.Length == 0 || paint == null)
            return false;

        if (info.Width <= 0 || info.Height <= 0 || barCount <= 0)
            return true;

        return true;
    }

    protected override void OnDispose()
    {
        if (_currentPath != null)
        {
            ResourcePool.ReturnPath(_currentPath);
            _currentPath = null;
        }
        base.OnDispose();
    }
}