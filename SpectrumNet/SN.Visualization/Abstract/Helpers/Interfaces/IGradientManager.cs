#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Helpers.Interfaces;

public interface IGradientManager : IDisposable
{
    SKShader? CurrentGradient { get; }
    void CreateLinearGradient(float height, SKColor[] colors, float[]? positions = null);
    void InvalidateIfHeightChanged(float newHeight);
}
