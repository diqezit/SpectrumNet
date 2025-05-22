#nullable enable

namespace SpectrumNet.SN.Themes.Interfaces;

public interface IThemes : INotifyPropertyChanged
{
    bool IsDarkTheme { get; }

    void RegisterWindow(Window window);
    void UnregisterWindow(Window window);
    void ToggleTheme();
    void SetTheme(bool isDark);
    void ApplyThemeToCurrentWindow();

    event EventHandler<bool> ThemeChanged;
}