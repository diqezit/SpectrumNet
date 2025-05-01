#nullable enable

namespace SpectrumNet.Controllers.RenderCore.Overlay;

public sealed record OverlayConfiguration(
    int RenderInterval = 16,
    bool IsTopmost = true,
    bool ShowInTaskbar = false,
    WindowStyle Style = WindowStyle.None,
    WindowState State = WindowState.Maximized,
    bool EnableEscapeToClose = true,
    bool EnableHardwareAcceleration = true
);
