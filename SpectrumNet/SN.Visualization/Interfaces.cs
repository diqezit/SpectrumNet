namespace SpectrumNet.SN.Visualization;

public enum RenderStyle
{
    AsciiDonut,
    AuroraCurtains,
    Bars,
    CircularBars,
    CircularWave,
    Constellation,
    Cube,
    Cubes,
    Dots,
    Fire,
    Gauge,
    Glitch,
    GradientWave,
    NeonWave,
    HackerTextRenderer,
    Heartbeat,
    Kenwood,
    LedMeter,
    LedPanelRenderer,
    Loudness,
    Particles,
    PixelGrid,
    Raindrops,
    Rainbow,
    RippleMesh,
    RippleRenderer,
    SphereRenderer,
    TextParticles,
    Waterfall,
    Waveform
}

public interface IRendererContext
{
    bool IsRecording { get; }
    bool IsOverlayActive { get; }
    bool ShowPerformanceInfo { get; }
    RenderStyle Style { get; }
    RenderQuality Quality { get; }
    int BarCount { get; }
    double BarSpacing { get; }
    SKElement SpectrumCanvas { get; }
    ISpectralDataProvider? DataProvider { get; }
}

public interface ISpectrumRenderer : IDisposable
{
    RenderQuality Quality { get; }
    bool IsOverlayActive { get; }

    void Initialize();
    void Configure(bool overlay, RenderQuality q);
    void SetOverlayTransparency(float level);

    void Render(
        SKCanvas? c,
        float[]? spectrum,
        SKImageInfo info,
        float barW,
        float barS,
        int barN,
        SKPaint? p,
        Action<SKCanvas, SKImageInfo>? perf);

    bool RequiresRedraw();
}

public interface IRendererFactory : IDisposable
{
    RenderQuality GlobalQuality { get; set; }

    ISpectrumRenderer CreateRenderer(
        RenderStyle s,
        bool overlay,
        RenderQuality? q = null,
        CancellationToken ct = default);

    IEnumerable<ISpectrumRenderer> GetAllRenderers();
    void ConfigureAllRenderers(bool? overlay, RenderQuality? q = null);
}

public interface IPerformanceMetricsManager : IDisposable
{
    event EventHandler? MetricsUpdated;
    event EventHandler? LevelChanged;
    event EventHandler? FpsLimitChanged;

    bool IsFpsLimited { get; }

    float GetFps();
    double GetCpu();
    double GetRam();
    PerformanceLevel GetLevel();
    PerformanceSnapshot GetSnapshot();

    void RecordFrameTime();
    bool ShouldRenderFrame();
    void SetFpsLimit(bool on);
    void Initialize(SynchronizationContext? ctx = null);
}
