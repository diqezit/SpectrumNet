#nullable enable

namespace SpectrumNet.Themes;

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