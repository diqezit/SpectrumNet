#nullable enable

using System.Globalization;
using System.Windows.Data;

namespace SpectrumNet
{
    public sealed class SpectrumBrushes : IDisposable
    {
        private static readonly Dictionary<string, Color4> ColorDefinitions =
            new(StringComparer.OrdinalIgnoreCase) {
                {"Solid", new Color4(0.13f, 0.59f, 0.95f, 1.0f)},
                {"Mint", new Color4(0.6f, 0.98f, 0.6f, 1.0f)},
                {"Red", Color4.Red},
                {"Green", Color4.Green},
                {"Blue", Color4.Blue},
                {"Yellow", Color4.Yellow},
                {"Orange", new Color4(1.0f, 0.65f, 0.0f, 1.0f)},
                {"Purple", Color4.Purple},
                {"Pink", Color4.Pink},
                {"Brown", new Color4(0.65f, 0.16f, 0.16f, 1.0f)},
                {"Gray", Color4.Gray},
                {"White", Color4.White},
                {"Black", Color4.Black}
            };

        private readonly ConcurrentDictionary<string, Color4> _palettes = new();
        private bool _disposed;
        private static readonly Lazy<SpectrumBrushes> _instance = new(() => new SpectrumBrushes());
        public static SpectrumBrushes Instance => _instance.Value;

        private const string DefaultVertexShader = @"#version 400 core
layout(location = 0) in vec3 aPosition;
uniform mat4 projection;
uniform mat4 modelview;

void main() {
    gl_Position = projection * modelview * vec4(aPosition, 1.0);
}";

        private const string DefaultFragmentShader = @"#version 400 core
out vec4 FragColor;
uniform vec4 color;

void main() {
    FragColor = color;
}";

        public IReadOnlyDictionary<string, Color4> RegisteredPalettes =>
            new Dictionary<string, Color4>(_palettes, StringComparer.OrdinalIgnoreCase);

        public SpectrumBrushes()
        {
            foreach (var kvp in ColorDefinitions)
            {
                SmartLogger.Safe(() =>
                {
                    if (string.IsNullOrWhiteSpace(kvp.Key))
                    {
                        SmartLogger.Log(LogLevel.Error, nameof(SpectrumBrushes),
                            "Skipping palette with invalid name.");
                        return;
                    }

                    if (!_palettes.TryAdd(kvp.Key, kvp.Value))
                        SmartLogger.Log(LogLevel.Warning, nameof(SpectrumBrushes),
                            $"Duplicate palette '{kvp.Key}' detected.");
                }, "SpectrumBrushes", $"Failed to initialize palette '{kvp.Key}'");
            }

            SmartLogger.Log(LogLevel.Information, nameof(SpectrumBrushes),
                $"Registered {_palettes.Count} palettes: {string.Join(", ", _palettes.Keys)}");

            if (_palettes.Count == 0)
                SmartLogger.Log(LogLevel.Warning, nameof(SpectrumBrushes),
                    "No palettes were registered.");
        }

        public Color4 GetColor(string paletteName)
        {
            ArgumentNullException.ThrowIfNull(paletteName);
            if (string.IsNullOrEmpty(paletteName))
                throw new ArgumentException("Value cannot be empty.", nameof(paletteName));

            if (_palettes.TryGetValue(paletteName, out var color))
                return color;

            throw new KeyNotFoundException($"Palette '{paletteName}' is not registered");
        }

        public (Color4 Color, ShaderProgram Shader) GetColorAndShader(string paletteName)
        {
            Color4 color = GetColor(paletteName);
            var shader = SmartLogger.Safe(() => CreateShader(color), defaultValue: null!,
                "SpectrumBrushes", $"Failed to create shader for '{paletteName}'");

            return (color, shader);
        }

        private ShaderProgram CreateShader(Color4 color)
        {
            var shader = new ShaderProgram(DefaultVertexShader, DefaultFragmentShader);
            shader.Color = color;
            return shader;
        }

        public void RegisterCustomPalette(string name, Color4 color)
        {
            ArgumentNullException.ThrowIfNull(name);
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Palette name cannot be empty", nameof(name));

            _palettes[name] = color;
            SmartLogger.Log(LogLevel.Information, nameof(SpectrumBrushes),
                $"Registered custom palette '{name}'");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _palettes.Clear();
            GC.SuppressFinalize(this);
        }
    }

    public class PaletteNameToBrushConverter : IValueConverter
    {
        public SpectrumBrushes? BrushesProvider { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string name && !string.IsNullOrEmpty(name) && BrushesProvider != null)
            {
                return SmartLogger.Safe(() =>
                {
                    var color = BrushesProvider.GetColor(name);
                    return new SolidColorBrush(System.Windows.Media.Color.FromScRgb(
                        color.A, color.R, color.G, color.B));
                }, defaultValue: System.Windows.Media.Brushes.Transparent,
                   "PaletteNameToBrushConverter", $"Error converting palette '{name}'");
            }
            return System.Windows.Media.Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException("Reverse conversion is not supported");
    }
}