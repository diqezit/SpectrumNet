#nullable enable

using SpectrumNet.SN.Settings.Constants;

namespace SpectrumNet.SN.Settings.Models;

public class GeneralSettings
{
    public bool IsOverlayTopmost { get; set; } = DefaultSettings.IsOverlayTopmost;
    public bool IsDarkTheme { get; set; } = DefaultSettings.IsDarkTheme;
    public bool LimitFpsTo60 { get; set; } = DefaultSettings.LimitFpsTo60;
    public ObservableCollection<RenderStyle> FavoriteRenderers { get; set; } = [];
}