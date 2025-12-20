namespace SpectrumNet.SN.Shared.Converters;

public class IsFavoriteConverter : IValueConverter
{
    private readonly ISettingsService _settings = SettingsService.Instance;

    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) =>
        value is RenderStyle style &&
        _settings.Current.General.FavoriteRenderers.Contains(style);

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) => throw new NotImplementedException();
}
