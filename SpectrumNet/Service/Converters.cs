using System.Globalization;
using System.Windows.Data;

namespace SpectrumNet
{
    public class IsFavoriteConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is RenderStyle style)
            {
                return Settings.Instance.FavoriteRenderers.Contains(style);
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
