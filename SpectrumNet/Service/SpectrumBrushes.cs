#nullable enable 

using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SpectrumNet
{
    /// <summary>
    /// Represents a named color palette with managed drawing resources.
    /// </summary>
    /// <remarks>
    /// Implements IDisposable to ensure proper cleanup of SKPaint resources.
    /// Provides both color value and pre-configured paint brush for drawing operations.
    /// </remarks>
    public sealed record Palette(string Name, SKColor Color) : IDisposable
    {
        private bool _disposed;

        /// <summary>
        /// Gets the pre-configured paint brush for this palette.
        /// </summary>
        public SKPaint Brush { get; } = new SKPaint
        {
            Color = Color,
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        /// <summary>
        /// Releases all resources used by the Palette.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            Brush.Dispose();
            _disposed = true;
        }
    }

    /// <summary>
    /// Provides predefined color palettes using SKColor.
    /// </summary>
    /// <remarks>
    /// Contains a dictionary of color definitions for easy addition and management.
    /// To add a new color, simply add an entry to the ColorDefinitions dictionary.
    /// For example:
    /// { "Yellow", SKColors.Yellow },
    /// { "CustomColor", new SKColor(0xFF123456) },
    /// </remarks>
    public static class Palettes
    {
        public static readonly IReadOnlyDictionary<string, SKColor> ColorDefinitions =
            new Dictionary<string, SKColor>(StringComparer.OrdinalIgnoreCase)
            {
            // Basic colors
            { "Solid", new SKColor(0xFF2196F3) },
            { "Red", SKColors.Red },
            { "Green", SKColors.Green },
            { "Blue", SKColors.Blue },
            { "Yellow", SKColors.Yellow },
            { "Orange", SKColors.Orange },
            { "Purple", SKColors.Purple },
            { "Pink", SKColors.Pink },
            { "Brown", SKColors.Brown },
            { "Gray", SKColors.Gray },
            { "White", SKColors.White },
            { "Black", SKColors.Black },

            // Shades of red
            { "DarkRed", SKColors.DarkRed },
            { "LightCoral", SKColors.LightCoral },
            { "IndianRed", SKColors.IndianRed },
            { "Firebrick", SKColors.Firebrick },
            { "Crimson", SKColors.Crimson },
            { "Tomato", SKColors.Tomato },
            { "Salmon", SKColors.Salmon },
            { "DarkSalmon", SKColors.DarkSalmon },
            { "Coral", SKColors.Coral },
            { "LightSalmon", SKColors.LightSalmon },
            { "OrangeRed", SKColors.OrangeRed },
            { "Maroon", SKColors.Maroon },
            { "BrickRed", new SKColor(0xFFCB4154) },
            { "ChiliRed", new SKColor(0xFFE32636) },
            { "RoseRed", new SKColor(0xFFFF66CC) },
            { "BloodRed", new SKColor(0xFF660000) },

            // Shades of green
            { "LimeGreen", SKColors.LimeGreen },
            { "ForestGreen", SKColors.ForestGreen },
            { "SeaGreen", SKColors.SeaGreen },
            { "SpringGreen", SKColors.SpringGreen },
            { "Olive", SKColors.Olive },
            { "DarkOliveGreen", SKColors.DarkOliveGreen },
            { "MediumSeaGreen", SKColors.MediumSeaGreen },
            { "DarkSeaGreen", SKColors.DarkSeaGreen },
            { "LightGreen", SKColors.LightGreen },
            { "PaleGreen", SKColors.PaleGreen },
            { "MediumSpringGreen", SKColors.MediumSpringGreen },
            { "YellowGreen", SKColors.YellowGreen },
            { "OliveDrab", SKColors.OliveDrab },
            { "DarkGreen", SKColors.DarkGreen },
            { "Mint", new SKColor(0xFF3EB489) },
            { "Jade", new SKColor(0xFF00A86B) },
            { "Pine", new SKColor(0xFF01796F) },
            { "EmeraldGreen", new SKColor(0xFF009B77) },
            { "Lichen", new SKColor(0xFF8A9A5B) },

            // Shades of blue
            { "DodgerBlue", SKColors.DodgerBlue },
            { "SteelBlue", SKColors.SteelBlue },
            { "RoyalBlue", SKColors.RoyalBlue },
            { "MediumBlue", SKColors.MediumBlue },
            { "DeepSkyBlue", SKColors.DeepSkyBlue },
            { "Navy", SKColors.Navy },
            { "SkyBlue", SKColors.SkyBlue },
            { "LightSkyBlue", SKColors.LightSkyBlue },
            { "LightBlue", SKColors.LightBlue },
            { "PowderBlue", SKColors.PowderBlue },
            { "CadetBlue", SKColors.CadetBlue },
            { "CornflowerBlue", SKColors.CornflowerBlue },
            { "DarkBlue", SKColors.DarkBlue },
            { "MidnightBlue", SKColors.MidnightBlue },
            { "SlateBlue", SKColors.SlateBlue },
            { "MediumSlateBlue", SKColors.MediumSlateBlue },
            { "LightSlateBlue", new SKColor(0xFF8470FF) },
            { "DarkSlateBlue", SKColors.DarkSlateBlue },
            { "OceanBlue", new SKColor(0xFF4F42B5) },
            { "Denim", new SKColor(0xFF1560BD) },
            { "Glacier", new SKColor(0xFF80B1D3) },
            { "Periwinkle", new SKColor(0xFFCCCCFF) },

            // Shades of purple
            { "DarkOrchid", SKColors.DarkOrchid },
            { "MediumPurple", SKColors.MediumPurple },
            { "BlueViolet", SKColors.BlueViolet },
            { "DarkMagenta", SKColors.DarkMagenta },
            { "Indigo", SKColors.Indigo },
            { "Violet", SKColors.Violet },
            { "Plum", SKColors.Plum },
            { "Thistle", SKColors.Thistle },
            { "Orchid", SKColors.Orchid },
            { "MediumOrchid", SKColors.MediumOrchid },
            { "DarkViolet", SKColors.DarkViolet },
            { "RebeccaPurple", new SKColor(0xFF663399) },
            { "LavenderPurple", new SKColor(0xFF967BB6) },
            { "Grape", new SKColor(0xFF6F2DA8) },
            { "Mulberry", new SKColor(0xFFC54B8C) },
            { "Wisteria", new SKColor(0xFF997A8D) },

            // Shades of yellow and orange
            { "Gold", SKColors.Gold },
            { "DarkOrange", SKColors.DarkOrange },
            { "Goldenrod", SKColors.Goldenrod },
            { "DarkGoldenrod", SKColors.DarkGoldenrod },
            { "LightGoldenrodYellow", SKColors.LightGoldenrodYellow },
            { "PaleGoldenrod", SKColors.PaleGoldenrod },
            { "Khaki", SKColors.Khaki },
            { "DarkKhaki", SKColors.DarkKhaki },
            { "LemonChiffon", SKColors.LemonChiffon },
            { "LightYellow", SKColors.LightYellow },
            { "PapayaWhip", SKColors.PapayaWhip },
            { "Moccasin", SKColors.Moccasin },
            { "PeachPuff", SKColors.PeachPuff },
            { "Butter", new SKColor(0xFFFFE135) },
            { "Canary", new SKColor(0xFFFFFF99) },
            { "Apricot", new SKColor(0xFFFBCEB1) },
            { "Mandarin", new SKColor(0xFFF37A48) },
            { "Honey", new SKColor(0xFFA66F00) },

            // Shades of brown
            { "SandyBrown", SKColors.SandyBrown },
            { "RosyBrown", SKColors.RosyBrown },
            { "SaddleBrown", SKColors.SaddleBrown },
            { "Chocolate", SKColors.Chocolate },
            { "Peru", SKColors.Peru },
            { "Sienna", SKColors.Sienna },
            { "BurlyWood", SKColors.BurlyWood },
            { "Tan", SKColors.Tan },
            { "Wheat", SKColors.Wheat },
            { "NavajoWhite", SKColors.NavajoWhite },
            { "Bisque", SKColors.Bisque },
            { "BlanchedAlmond", SKColors.BlanchedAlmond },
            { "Cornsilk", SKColors.Cornsilk },
            { "Mocha", new SKColor(0xFF967117) },
            { "Cinnamon", new SKColor(0xFFD2691E) },
            { "Taupe", new SKColor(0xFF483C32) },
            { "Umber", new SKColor(0xFF635147) },

            // Shades of pink
            { "PaleVioletRed", SKColors.PaleVioletRed },
            { "HotPink", SKColors.HotPink },
            { "DeepPink", SKColors.DeepPink },
            { "LightPink", SKColors.LightPink },
            { "MistyRose", SKColors.MistyRose },
            { "LavenderBlush", SKColors.LavenderBlush },
            { "Bubblegum", new SKColor(0xFFFFC1CC) },
            { "Flamingo", new SKColor(0xFFFC8EAC) },
            { "Blush", new SKColor(0xFFDE5D83) },
            { "CherryBlossom", new SKColor(0xFFFFB7C5) },

            // Shades of gray
            { "Silver", SKColors.Silver },
            { "DimGray", SKColors.DimGray },
            { "LightSlateGray", SKColors.LightSlateGray },
            { "DarkSlateGray", SKColors.DarkSlateGray },
            { "Gainsboro", SKColors.Gainsboro },
            { "LightGray", SKColors.LightGray },
            { "DarkGray", SKColors.DarkGray },
            { "SlateGray", SKColors.SlateGray },
            { "LightSteelBlue", SKColors.LightSteelBlue },
            { "Ash", new SKColor(0xFFB2BEB5) },
            { "Pewter", new SKColor(0xFF96A8A1) },
            { "Smoke", new SKColor(0xFF738276) },
            { "Granite", new SKColor(0xFF676767) },

            // Special and metallic colors
            { "Bronze", new SKColor(0xFFCD7F32) },
            { "Platinum", new SKColor(0xFFE5E4E2) },
            { "Ivory", SKColors.Ivory },
            { "MintCream", SKColors.MintCream },
            { "Azure", SKColors.Azure },
            { "AliceBlue", SKColors.AliceBlue },
            { "AntiqueWhite", SKColors.AntiqueWhite },
            { "Aqua", SKColors.Aqua },
            { "Aquamarine", SKColors.Aquamarine },
            { "Beige", SKColors.Beige },
            { "Chartreuse", SKColors.Chartreuse },
            { "Cyan", SKColors.Cyan },
            { "DarkCyan", SKColors.DarkCyan },
            { "FloralWhite", SKColors.FloralWhite },
            { "Fuchsia", SKColors.Fuchsia },
            { "GhostWhite", SKColors.GhostWhite },
            { "Honeydew", SKColors.Honeydew },
            { "Lavender", SKColors.Lavender },
            { "Linen", SKColors.Linen },
            { "MediumAquamarine", SKColors.MediumAquamarine },
            { "MediumTurquoise", SKColors.MediumTurquoise },
            { "OldLace", SKColors.OldLace },
            { "PaleTurquoise", SKColors.PaleTurquoise },
            { "Snow", SKColors.Snow },
            { "Turquoise", SKColors.Turquoise },
            { "WhiteSmoke", SKColors.WhiteSmoke },
            { "Copper", new SKColor(0xFFB87333) },
            { "Brass", new SKColor(0xFFB5A642) },
            { "Steel", new SKColor(0xFF4682B4) },

            // Additional custom colors
            { "LightPurple", new SKColor(0xFFDDA0DD) },
            { "Emerald", new SKColor(0xFF50C878) },
            { "Ruby", new SKColor(0xFFE0115F) },
            { "Sapphire", new SKColor(0xFF0F52BA) },
            { "Amber", new SKColor(0xFFFFBF00) },
            { "Topaz", new SKColor(0xFFFFC107) },
            { "Amethyst", new SKColor(0xFF9966CC) },
            { "Onyx", new SKColor(0xFF353935) },
            { "Pearl", new SKColor(0xFFFDEEF4) },
            { "JetBlack", new SKColor(0xFF0A0A0A) },
            { "Opal", new SKColor(0xFFA8C3BC) },
            { "Garnet", new SKColor(0xFF733635) },
            { "TurquoiseBlue", new SKColor(0xFF00CED1) },
            { "Lilac", new SKColor(0xFFC8A2C8) },
            { "Mauve", new SKColor(0xFFE0B0FF) },
            { "Cobalt", new SKColor(0xFF0047AB) },
            { "Sepia", new SKColor(0xFF704214) },
            { "Tangerine", new SKColor(0xFFF28500) },
            { "Cerulean", new SKColor(0xFF007BA7) },
            { "Verdant", new SKColor(0xFF4CAF50) },
            { "Scarlet", new SKColor(0xFFFF2400) },
            { "Saffron", new SKColor(0xFFF4C430) },
            { "Charcoal", new SKColor(0xFF36454F) },
            { "ElectricBlue", new SKColor(0xFF7DF9FF) },
            { "IceBlue", new SKColor(0xFF99FFFF) },
            { "MagentaPink", new SKColor(0xFFCC338B) },
            { "Mustard", new SKColor(0xFFFFDB58) },
            { "PeacockBlue", new SKColor(0xFF33A1C9) },
            { "Pumpkin", new SKColor(0xFFFF7518) },
            { "RoyalPurple", new SKColor(0xFF7851A9) },
            { "Seafoam", new SKColor(0xFF93E9BE) },
            { "Sunflower", new SKColor(0xFFFFDA03) },
            { "TealBlue", new SKColor(0xFF367588) },
            { "Ultramarine", new SKColor(0xFF3F00FF) },
            { "Vermilion", new SKColor(0xFFE34234) },
            { "Wine", new SKColor(0xFF722F37) },
            { "Zomp", new SKColor(0xFF39A78E) },
            { "Celadon", new SKColor(0xFFACE1AF) },
            { "DustyRose", new SKColor(0xFFDCAE96) },
            { "Eggplant", new SKColor(0xFF614051) },
            { "Fawn", new SKColor(0xFFE5AA70) },
            { "IndigoBlue", new SKColor(0xFF4B0082) },
            { "Lapis", new SKColor(0xFF26619C) },
            { "Moss", new SKColor(0xFF8A9A5B) },
            { "Raven", new SKColor(0xFF757575) },
            { "Sage", new SKColor(0xFF9BAA8D) },
            { "Twilight", new SKColor(0xFF4A4066) }
            };
    }

    /// <summary>
    /// Manages palette resources with lifecycle control and thread-safe access.
    /// </summary>
    /// <remarks>
    /// Automatically registers predefined palettes from Palettes.ColorDefinitions upon initialization.
    /// Provides safe access to color resources and implements IDisposable for proper cleanup.
    /// </remarks>
    public sealed class SpectrumBrushes : IDisposable
    {
        private static readonly Lazy<SpectrumBrushes> _instance = new Lazy<SpectrumBrushes>(() => new SpectrumBrushes());
        private readonly ConcurrentDictionary<string, Palette> _palettes = new(StringComparer.OrdinalIgnoreCase);
        private bool _disposed;

        public static SpectrumBrushes Instance => _instance.Value;

        /// <summary>
        /// Gets a read-only view of registered palettes.
        /// </summary>
        public IReadOnlyDictionary<string, Palette> RegisteredPalettes => _palettes;

        /// <summary>
        /// Initializes a new instance and registers palettes from Palettes.ColorDefinitions.
        /// </summary>
        public SpectrumBrushes()
        {
            RegisterFromDefinitions();
        }

        private void RegisterFromDefinitions()
        {
            foreach (var kvp in Palettes.ColorDefinitions)
            {
                var palette = new Palette(kvp.Key, kvp.Value);
                Register(palette);
            }
        }

        /// <summary>
        /// Registers a custom palette instance.
        /// </summary>
        /// <param name="palette">Palette to register.</param>
        /// <exception cref="ArgumentNullException">Thrown when palette is null.</exception>
        /// <exception cref="ArgumentException">Thrown when palette name is invalid.</exception>
        public void Register(Palette palette)
        {
            if (palette == null)
                throw new ArgumentNullException(nameof(palette));

            if (string.IsNullOrWhiteSpace(palette.Name))
                throw new ArgumentException("Palette name cannot be empty", nameof(palette));

            _palettes[palette.Name] = palette;
        }

        /// <summary>
        /// Retrieves color and paint resources for the specified palette.
        /// </summary>
        /// <param name="paletteName">Case-insensitive palette identifier.</param>
        /// <returns>Tuple containing color value and associated paint brush.</returns>
        /// <exception cref="ArgumentException">Thrown for invalid palette names.</exception>
        /// <exception cref="KeyNotFoundException">Thrown for unregistered palettes.</exception>
        public (SKColor Color, SKPaint Brush) GetColorAndBrush(string paletteName)
        {
            EnsureNotDisposed();

            if (string.IsNullOrWhiteSpace(paletteName))
                throw new ArgumentException("Palette name cannot be empty", nameof(paletteName));

            if (_palettes.TryGetValue(paletteName, out var palette))
            {
                return (palette.Color, palette.Brush);
            }
            throw new KeyNotFoundException($"Palette '{paletteName}' not registered");
        }

        private void EnsureNotDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().Name);
        }

        /// <summary>
        /// Releases all managed resources and clears registered palettes.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            foreach (var palette in _palettes.Values)
                palette.Dispose();

            _palettes.Clear();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    public class PaletteNameToBrushConverter : IValueConverter
    {
        /// <summary>
        /// An instance that provides access to registered palettes.  
        /// It can be set in XAML via binding or programmatically.
        /// </summary>
        public SpectrumBrushes? BrushesProvider { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string paletteName && BrushesProvider != null)
            {
                try
                {
                    var (skColor, _) = BrushesProvider.GetColorAndBrush(paletteName);
                    return new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromArgb(
                            skColor.Alpha, skColor.Red, skColor.Green, skColor.Blue));
                }
                catch (Exception)
                {
                    return System.Windows.Media.Brushes.Transparent;
                }
            }
            return System.Windows.Media.Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}