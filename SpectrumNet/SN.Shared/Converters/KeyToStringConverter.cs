#nullable enable

namespace SpectrumNet.SN.Shared.Converters;

public class KeyToStringConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        if (value is Key key)
            return key.ToString();
        return string.Empty;
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        if (value is string keyString
            && Enum.TryParse<Key>(keyString, out var key))
            return key;
        return Key.None;
    }
}
