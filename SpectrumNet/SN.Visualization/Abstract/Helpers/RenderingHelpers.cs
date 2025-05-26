#nullable enable

using static System.MathF;

namespace SpectrumNet.SN.Visualization.Abstract.Helpers;

public sealed class RenderingHelpers() : IRenderingHelpers
{
    private static readonly Lazy<RenderingHelpers> _instance =
        new(() => new RenderingHelpers());

    public static RenderingHelpers Instance => _instance.Value;

    [MethodImpl(AggressiveInlining)]
    public byte CalculateAlpha(float magnitude, float multiplier = 255f) =>
        magnitude <= 0 ? (byte)0 :
        magnitude >= 1 ? (byte)255 :
        (byte)MathF.Min(magnitude * multiplier, 255);

    public float GetAverageInRange(float[] array, int start, int end)
    {
        if (array == null || array.Length == 0) return 0f;

        start = Max(0, start);
        end = Min(array.Length, end);
        if (start >= end) return 0f;

        float sum = 0f;
        for (int i = start; i < end; i++) sum += array[i];
        return sum / (end - start);
    }

    [MethodImpl(AggressiveInlining)]
    public float Lerp(float current, float target, float amount) =>
        current + (target - current) * Clamp(amount, 0f, 1f);

    [MethodImpl(AggressiveInlining)]
    public bool IsAreaVisible(SKCanvas? canvas, SKRect rect) =>
        canvas == null || !canvas.QuickReject(rect);

    [MethodImpl(AggressiveInlining)]
    public bool IsAreaVisible(
        SKCanvas? canvas,
        float x, float y,
        float width, float height) =>
        canvas == null || !canvas.QuickReject(new SKRect(x, y, x + width, y + height));

    public SKColor GetGradientColor(float value, SKColor startColor, SKColor endColor)
    {
        value = Clamp(value, 0f, 1f);
        return new SKColor(
            (byte)(startColor.Red + (endColor.Red - startColor.Red) * value),
            (byte)(startColor.Green + (endColor.Green - startColor.Green) * value),
            (byte)(startColor.Blue + (endColor.Blue - startColor.Blue) * value),
            (byte)(startColor.Alpha + (endColor.Alpha - startColor.Alpha) * value));
    }

    public SKPoint[] CreateCirclePoints(int pointCount, float radius, SKPoint center)
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

    public Vector2[] CreateCircleVectors(int count)
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

    [MethodImpl(AggressiveInlining)]
    public float SmoothStep(float edge0, float edge1, float x)
    {
        x = Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return x * x * (3f - 2f * x);
    }

    [MethodImpl(AggressiveInlining)]
    public float Distance(SKPoint p1, SKPoint p2)
    {
        float dx = p2.X - p1.X, dy = p2.Y - p1.Y;
        return Sqrt(dx * dx + dy * dy);
    }

    [MethodImpl(AggressiveInlining)]
    public float Normalize(float value, float min, float max) =>
        max <= min ? 0f : Clamp((value - min) / (max - min), 0f, 1f);

    public SKRect GetBarRect(
        float x,
        float magnitude,
        float barWidth,
        float canvasHeight,
        float minHeight = 1f)
    {
        float barHeight = MathF.Max(magnitude * canvasHeight, minHeight);
        return new SKRect(x, canvasHeight - barHeight, x + barWidth, canvasHeight);
    }

    public SKRect GetCenteredRect(SKPoint center, float width, float height) =>
        new(center.X - width / 2, center.Y - height / 2,
            center.X + width / 2, center.Y + height / 2);

    public SKPoint GetPolarPoint(SKPoint center, float angle, float radius) =>
        new(center.X + radius * Cos(angle), center.Y + radius * Sin(angle));

    public float GetFrequencyMagnitude(
        float[] spectrum,
        float frequency,
        float sampleRate,
        int fftSize)
    {
        int bin = (int)(frequency * fftSize / sampleRate);
        return bin < 0 || bin >= spectrum.Length ? 0f : spectrum[bin];
    }

    public SKColor[] CreateGradientColors(int count, SKColor startColor, SKColor endColor)
    {
        if (count <= 0) return [];
        if (count == 1) return [startColor];

        var colors = new SKColor[count];
        for (int i = 0; i < count; i++)
            colors[i] = GetGradientColor(i / (float)(count - 1), startColor, endColor);
        return colors;
    }

    [MethodImpl(AggressiveInlining)]
    public float EaseInOut(float t)
    {
        t = Clamp(t, 0f, 1f);
        return t < 0.5f ? 2f * t * t : 1f - Pow(-2f * t + 2f, 2f) / 2f;
    }

    [MethodImpl(AggressiveInlining)]
    public float EaseIn(float t) => (t = Clamp(t, 0f, 1f)) * t;

    [MethodImpl(AggressiveInlining)]
    public float EaseOut(float t) => 1f - (1f - (t = Clamp(t, 0f, 1f))) * (1f - t);

    public SKPath CreateWavePath(float[] values, float width, float height, float offsetY = 0)
    {
        var path = new SKPath();
        if (values.Length < 2) return path;

        float stepX = width / (values.Length - 1);
        path.MoveTo(0, offsetY + height - values[0] * height);

        for (int i = 1; i < values.Length; i++)
            path.LineTo(i * stepX, offsetY + height - values[i] * height);

        return path;
    }
}