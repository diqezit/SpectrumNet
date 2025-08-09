#nullable enable

using SpectrumNet;

namespace SpectrumNet.SN.Settings.Models;

public class WindowSettings
{
    public double Left { get; set; } = DefaultSettings.WindowLeft;
    public double Top { get; set; } = DefaultSettings.WindowTop;
    public double Width { get; set; } = DefaultSettings.WindowWidth;
    public double Height { get; set; } = DefaultSettings.WindowHeight;
    public WindowState State { get; set; } = DefaultSettings.WindowState;
    public bool IsControlPanelVisible { get; set; } = DefaultSettings.IsControlPanelVisible;
}
