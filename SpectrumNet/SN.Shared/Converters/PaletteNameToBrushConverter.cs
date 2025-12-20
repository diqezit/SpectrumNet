#nullable enable 

namespace SpectrumNet.SN.Shared.Converters;

public class PaletteNameToBrushConverter : IValueConverter
{
    /// <summary>
    /// An instance that provides access to registered palettes.  
    /// It can be set in XAML via binding or programmatically.
    /// </summary>
    public SpectrumBrushes? BrushesProvider { get; set; }

    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        if (value is string paletteName && BrushesProvider != null)
        {
            try
            {
                var (skColor, _) = BrushesProvider.GetColorAndBrush(paletteName);
                return new SolidColorBrush(
                    Color.FromArgb(
                        skColor.Alpha, skColor.Red, skColor.Green, skColor.Blue));
            }
            catch (Exception)
            {
                return System.Windows.Media.Brushes.Transparent;
            }
        }
        return System.Windows.Media.Brushes.Transparent;
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) =>
        throw new NotImplementedException();
}
