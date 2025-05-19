#nullable enable

namespace SpectrumNet.Themes;

public partial class CommonResources
{
    private const string LogPrefix = nameof(CommonResources);
    private readonly ISmartLogger _logger = Instance;

    public static void InitialiseResources() =>
        Instance.Safe(() =>
        {
            IThemes themeManager = ThemeManager.Instance;
            themeManager.ApplyThemeToCurrentWindow();
        },
        nameof(CommonResources),
        "Failed to initialize application resources");

    public void Slider_MouseWheelScroll(object sender, MouseWheelEventArgs e) =>
        _logger.Safe(() =>
        {
            if (sender is Slider slider)
            {
                var change = slider.SmallChange > 0 ? slider.SmallChange : 1;
                slider.Value = Clamp(
                    slider.Value + (e.Delta > 0 ? change : -change),
                    slider.Minimum,
                    slider.Maximum);
                e.Handled = true;
            }
        },
        LogPrefix,
        "Error handling mouse wheel scroll for slider");
}