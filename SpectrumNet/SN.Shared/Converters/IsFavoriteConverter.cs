namespace SpectrumNet.SN.Shared.Converters;

public class IsFavoriteConverter : IValueConverter
{
    private readonly ISettings _settings = Settings.Settings.Instance;

    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        if (value is RenderStyle style)
            _settings.General.FavoriteRenderers.Contains(style);

        return false;
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) 
    {
        throw new NotImplementedException();
    }

}