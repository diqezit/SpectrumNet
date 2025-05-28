#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Helpers;

public class GradientManager : IGradientManager
{
    private SKShader? _gradient;
    private float _lastHeight;

    public SKShader? CurrentGradient => _gradient;

    public void CreateLinearGradient(float height, SKColor[] colors, float[]? positions = null)
    {
        _gradient?.Dispose();
        _lastHeight = height;

        _gradient = SKShader.CreateLinearGradient(
            new SKPoint(0, height),
            new SKPoint(0, 0),
            colors,
            positions ?? CreateUniformPositions(colors.Length),
            SKShaderTileMode.Clamp
        );
    }

    public void InvalidateIfHeightChanged(float newHeight)
    {
        if (Math.Abs(_lastHeight - newHeight) > 0.1f)
        {
            _gradient?.Dispose();
            _gradient = null;
        }
    }

    private static float[] CreateUniformPositions(int count)
    {
        var positions = new float[count];
        for (int i = 0; i < count; i++)
            positions[i] = i / (float)(count - 1);
        return positions;
    }

    public void Dispose()
    {
        _gradient?.Dispose();
    }
}