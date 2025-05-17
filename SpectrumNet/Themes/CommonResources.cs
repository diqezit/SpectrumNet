#nullable enable

namespace SpectrumNet.Themes;

public partial class CommonResources
{
    public static void InitialiseResources() => Safe(
        () =>
        {
            IThemes themeManager = ThemeManager.Instance;
            themeManager.ApplyThemeToCurrentWindow();
        },
        nameof(InitialiseResources),
        "Failed to initialize application resources");

    public void Slider_MouseWheelScroll(object sender, MouseWheelEventArgs e) => Safe(
        () =>
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
        nameof(Slider_MouseWheelScroll),
        "Error handling mouse wheel scroll for slider");
}