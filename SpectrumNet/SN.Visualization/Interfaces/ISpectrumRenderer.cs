namespace SpectrumNet.SN.Visualization.Interfaces;

public interface ISpectrumRenderer : IDisposable
{
    RenderQuality Quality { get; }
    bool IsOverlayActive { get; }

    void Initialize();
    void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium);
    void SetOverlayTransparency(float level);
    bool RequiresRedraw();

    void Render(
        SKCanvas? canvas,
        float[]? spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint? paint,
        Action<SKCanvas, SKImageInfo>? drawPerformanceInfo);
}