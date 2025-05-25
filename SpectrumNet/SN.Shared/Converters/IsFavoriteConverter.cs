namespace SpectrumNet.SN.Shared.Converters;

public class IsFavoriteConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        if (value is RenderStyle style)
        {
            return Settings.Settings.Instance.General.FavoriteRenderers.Contains(style);
        }
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