#nullable enable

namespace SpectrumNet.Controllers.Interfaces.SpectrumCore;

/// <summary>
/// Interface for classes that perform spectrum rendering.
/// </summary>
public interface ISpectrumRenderer : IDisposable
{
    void Initialize();
    void Render(SKCanvas? canvas, float[]? spectrum, SKImageInfo info, float barWidth,
                float barSpacing, int barCount, SKPaint? paint,
                Action<SKCanvas, SKImageInfo>? drawPerformanceInfo);
    void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium);
    RenderQuality Quality { get; set; }
    bool IsOverlayActive { get; }

    void SetOverlayTransparency(float level);
}