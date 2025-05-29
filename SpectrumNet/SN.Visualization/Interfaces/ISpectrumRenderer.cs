namespace SpectrumNet.SN.Visualization.Interfaces;

public interface ISpectrumRenderer : IDisposable
{
    RenderQuality Quality { get; }
    bool IsOverlayActive { get; }

    void Initialize();
    void Configure(bool isOverlayActive, RenderQuality quality);
    void SetOverlayTransparency(float level);
    void Render(
        SKCanvas? canvas,
        float[]? spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint? paint,
        Action<SKCanvas, SKImageInfo>? drawPerformanceInfo);
    bool RequiresRedraw();
}