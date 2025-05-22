#nullable enable

namespace SpectrumNet.SN.Visualization.Performance;

public sealed class PerformanceRenderer : IPerformanceRenderer
{
    private const string LogPrefix = nameof(PerformanceRenderer);
    private readonly ISmartLogger _logger = Instance;

    private readonly SKFont _performanceFont;
    private readonly SKPaint _performancePaint;
    private PerformanceMetrics _currentMetrics;
    private readonly IMainController _controller;
    private readonly IPerformanceMetricsManager _metricsManager;

    public PerformanceRenderer(
        IMainController controller,
        IPerformanceMetricsManager? metricsManager = null)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _metricsManager = metricsManager ?? PerformanceMetricsManager.Instance;
        _performanceFont = new SKFont { Size = 12, Edging = SKFontEdging.SubpixelAntialias };
        _performancePaint = new SKPaint { IsAntialias = true };
        _currentMetrics = new PerformanceMetrics(0, 0);
    }

    public void UpdateMetrics(PerformanceMetrics metrics) => _currentMetrics = metrics;

    public void RenderPerformanceInfo(SKCanvas canvas, SKImageInfo info)
    {
        if (!_controller.ShowPerformanceInfo)
            return;

        float fps = _currentMetrics.Fps > 0 ? (float)_currentMetrics.Fps : _metricsManager.GetCurrentFps();
        double cpuUsagePercent = _metricsManager.GetCurrentCpuUsagePercent();
        double ramUsageMb = _metricsManager.GetCurrentRamUsageMb();

        _logger.Safe(() => DrawPerformanceInfo(canvas, info, fps, cpuUsagePercent, ramUsageMb),
            LogPrefix, "Error drawing performance info");
    }

    private void DrawPerformanceInfo(
        SKCanvas canvas,
        SKImageInfo info,
        float fps,
        double cpuUsagePercent,
        double ramUsageMb)
    {
        PerformanceLevel level = DeterminePerformanceLevel(fps, cpuUsagePercent, ramUsageMb);

        string infoText = CreateInfoText(fps, cpuUsagePercent, ramUsageMb, level);
        SKRect backgroundRect = CalculateMetricsBackgroundRect(info, infoText);

        DrawMetricsBackground(canvas, backgroundRect);
        DrawMetricsText(canvas, infoText, backgroundRect, level);
    }

    private static PerformanceLevel DeterminePerformanceLevel(float fps, double cpu, double ram)
    {
        if (fps >= 50 && cpu < 60 && ram < 500)
            return PerformanceLevel.Excellent;
        if (fps >= 30 && cpu < 80 && ram < 1000)
            return PerformanceLevel.Good;
        if (fps >= 20 && cpu < 90)
            return PerformanceLevel.Fair;
        return PerformanceLevel.Poor;
    }

    private static string CreateInfoText(float fps, double cpu, double ram, PerformanceLevel level)
    {
        string fpsLimiterInfo = FpsLimiter.Instance.IsEnabled ? " [60 FPS Lock]" : "";
        return string.Create(CultureInfo.InvariantCulture,
            $"RAM: {ram:F1} MB | CPU: {cpu:F1}% | FPS: {fps:F0}{fpsLimiterInfo} | {level}");
    }

    private SKRect CalculateMetricsBackgroundRect(SKImageInfo info, string infoText)
    {
        float textWidth = _performanceFont.MeasureText(infoText);
        float textHeight = _performanceFont.Size;
        float padding = 8f;

        return new SKRect(
            10f,
            info.Height - textHeight - padding * 2 - 10f,
            textWidth + padding * 2 + 10f,
            info.Height - 10f
        );
    }

    private static void DrawMetricsBackground(SKCanvas canvas, SKRect backgroundRect)
    {
        using var bgPaint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 180),
            IsAntialias = true
        };

        float cornerRadius = 5f;
        canvas.DrawRoundRect(backgroundRect, cornerRadius, cornerRadius, bgPaint);
    }

    private void DrawMetricsText(
        SKCanvas canvas,
        string infoText,
        SKRect backgroundRect,
        PerformanceLevel level)
    {
        float padding = 8f;
        _performancePaint.Color = GetPerformanceTextColor(level);
        canvas.DrawText(
            infoText,
            backgroundRect.Left + padding,
            backgroundRect.Bottom - padding,
            _performanceFont,
            _performancePaint
        );
    }

    private static SKColor GetPerformanceTextColor(PerformanceLevel level) =>
        level switch
        {
            PerformanceLevel.Excellent => SKColors.LimeGreen,
            PerformanceLevel.Good => SKColors.DodgerBlue,
            PerformanceLevel.Fair => SKColors.Orange,
            PerformanceLevel.Poor => SKColors.Red,
            _ => SKColors.Gray
        };
}