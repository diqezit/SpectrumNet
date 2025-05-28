#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Helpers.Interfaces;

public interface ICommonEffects
{
    void RenderGlow(SKCanvas canvas, SKPath path, SKColor color, float radius, float alpha);
    void RenderGlow(SKCanvas canvas, SKRect rect, SKColor color, float radius, float alpha);
}
