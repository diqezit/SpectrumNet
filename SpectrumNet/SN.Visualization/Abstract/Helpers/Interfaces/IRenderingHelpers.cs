#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Helpers.Interfaces;

public interface IRenderingHelpers
{
    byte CalculateAlpha(float magnitude, float multiplier = 255f);
    float GetAverageInRange(float[] array, int start, int end);
    float Lerp(float current, float target, float amount);
    bool IsAreaVisible(SKCanvas? canvas, SKRect rect);
    bool IsAreaVisible(SKCanvas? canvas, float x, float y, float width, float height);
    SKColor GetGradientColor(float value, SKColor startColor, SKColor endColor);
    SKPoint[] CreateCirclePoints(int pointCount, float radius, SKPoint center);
    Vector2[] CreateCircleVectors(int count);
    float SmoothStep(float edge0, float edge1, float x);
    float Distance(SKPoint p1, SKPoint p2);
    float Normalize(float value, float min, float max);
    SKRect GetBarRect(float x, float magnitude, float barWidth, float canvasHeight, float minHeight = 1f);
    SKRect GetCenteredRect(SKPoint center, float width, float height);
    SKPoint GetPolarPoint(SKPoint center, float angle, float radius);
    float GetFrequencyMagnitude(float[] spectrum, float frequency, float sampleRate, int fftSize);
    SKColor[] CreateGradientColors(int count, SKColor startColor, SKColor endColor);
    float EaseInOut(float t);
    float EaseIn(float t);
    float EaseOut(float t);
    SKPath CreateWavePath(float[] values, float width, float height, float offsetY = 0);
}