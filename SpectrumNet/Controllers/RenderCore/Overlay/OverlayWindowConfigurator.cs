#nullable enable

namespace SpectrumNet.Controllers.RenderCore.Overlay;

public sealed class OverlayWindowConfigurator : IOverlayWindowConfigurator
{
    private const string LogPrefix = nameof(OverlayWindowConfigurator);
    private readonly ISmartLogger _logger = Instance;

    public void ConfigureWindow(Window window, OverlayConfiguration configuration)
    {
        _logger.Safe(() => HandleConfigureWindow(window, configuration),
            LogPrefix, "Error configuring window");
    }

    public void ApplyTransparency(Window window)
    {
        _logger.Safe(() => HandleApplyTransparency(window),
            LogPrefix, "Error applying transparency");
    }

    public void ConfigureRendering(Window window)
    {
        _logger.Safe(() => HandleConfigureRendering(window),
            LogPrefix, "Error configuring rendering");
    }

    private static void HandleConfigureWindow(Window window, OverlayConfiguration configuration)
    {
        window.WindowStyle = configuration.Style;
        window.WindowState = configuration.State;
        window.ResizeMode = ResizeMode.NoResize;
        window.AllowsTransparency = true;
        window.Background = null;
        window.Topmost = configuration.IsTopmost;
        window.ShowInTaskbar = configuration.ShowInTaskbar;
        window.IsHitTestVisible = true;
    }

    private static void HandleApplyTransparency(Window window)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            SystemBackdrop.SetTransparentBackground(window);
    }

    private static void HandleConfigureRendering(Window window)
    {
        RenderOptions.ProcessRenderMode = RenderMode.Default;
        window.SetValue(RenderOptions.BitmapScalingModeProperty, BitmapScalingMode.HighQuality);
        window.SetValue(TextOptions.TextRenderingModeProperty, TextRenderingMode.Auto);
        window.SetValue(TextOptions.TextFormattingModeProperty, TextFormattingMode.Ideal);
        window.SetValue(RenderOptions.EdgeModeProperty, EdgeMode.Aliased);
        window.SetValue(RenderOptions.ClearTypeHintProperty, ClearTypeHint.Enabled);
    }
}