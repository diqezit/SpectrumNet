#nullable enable

using System.Globalization;
using System.Windows.Data;

namespace SpectrumNet
{
    /// <summary>
    /// Управляет цветовыми палитрами для визуализации спектра.
    /// </summary>
    public sealed class SpectrumBrushes : IDisposable
    {
        // Стандартные цвета палитр
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

        public const string DefaultVertexShader = @"#version 400 core
layout(location = 0) in vec3 aPosition;
uniform mat4 projection;
uniform mat4 modelview;

void main() {
    gl_Position = projection * modelview * vec4(aPosition, 1.0);
}";

        public const string DefaultFragmentShader = @"#version 400 core
out vec4 FragColor;
uniform vec4 color;

void main() {
    FragColor = color;
}";

        public const string GlowFragmentShader = @"#version 400 core
out vec4 FragColor;
uniform vec4 color;
uniform float uBlurRadius;

void main() {
    FragColor = color;
}";

        public IReadOnlyDictionary<string, Color4> RegisteredPalettes =>
            new Dictionary<string, Color4>(_palettes, StringComparer.OrdinalIgnoreCase);

        public SpectrumBrushes()
        {
            foreach (var kvp in ColorDefinitions)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(kvp.Key))
                    {
                        SmartLogger.Log(LogLevel.Error, nameof(SpectrumBrushes),
                            "Пропуск палитры с недопустимым именем.");
                        continue;
                    }

                    if (!_palettes.TryAdd(kvp.Key, kvp.Value))
                        SmartLogger.Log(LogLevel.Warning, nameof(SpectrumBrushes),
                            $"Обнаружен дубликат палитры '{kvp.Key}'.");
                }
                catch (Exception ex)
                {
                    SmartLogger.Log(LogLevel.Error, nameof(SpectrumBrushes),
                        $"Не удалось инициализировать палитру '{kvp.Key}': {ex.Message}");
                }
            }

            SmartLogger.Log(LogLevel.Information, nameof(SpectrumBrushes),
                $"Зарегистрировано {_palettes.Count} палитр: {string.Join(", ", _palettes.Keys)}");

            if (_palettes.Count == 0)
                SmartLogger.Log(LogLevel.Warning, nameof(SpectrumBrushes),
                    "Не зарегистрировано ни одной палитры.");
        }

        /// <summary>
        /// Возвращает цвет для указанной палитры.
        /// </summary>
        public Color4 GetColor(string paletteName)
        {
            ArgumentNullException.ThrowIfNull(paletteName);
            if (string.IsNullOrEmpty(paletteName))
                throw new ArgumentException("Значение не может быть пустым.", nameof(paletteName));

            if (_palettes.TryGetValue(paletteName, out var color))
                return color;

            throw new KeyNotFoundException($"Палитра '{paletteName}' не зарегистрирована");
        }

        /// <summary>
        /// Возвращает цвет и создает новый шейдер для указанной палитры.
        /// </summary>
        /// <remarks>
        /// Этот метод создает новый шейдер с цветом из палитры для совместимости
        /// с существующим кодом. В идеале рендереры должны использовать только цвета.
        /// </remarks>
        public (Color4 Color, ShaderProgram Shader) GetColorAndShader(string paletteName)
        {
            Color4 color = GetColor(paletteName);

            try
            {
                var shader = new ShaderProgram(DefaultVertexShader, DefaultFragmentShader);
                shader.Color = color;
                return (color, shader);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, nameof(SpectrumBrushes),
                    $"Не удалось создать шейдер для '{paletteName}': {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Регистрирует новую палитру с указанным именем и цветом.
        /// </summary>
        public void RegisterCustomPalette(string name, Color4 color)
        {
            ArgumentNullException.ThrowIfNull(name);
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Имя палитры не может быть пустым", nameof(name));

            _palettes[name] = color;
            SmartLogger.Log(LogLevel.Information, nameof(SpectrumBrushes),
                $"Зарегистрирована пользовательская палитра '{name}'");
        }

        /// <summary>
        /// Очищает ресурсы, используемые объектом.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _palettes.Clear();
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Конвертер для преобразования имени палитры в WPF кисть.
    /// </summary>
    public class PaletteNameToBrushConverter : IValueConverter
    {
        public SpectrumBrushes? BrushesProvider { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string name && !string.IsNullOrEmpty(name) && BrushesProvider != null)
            {
                try
                {
                    var color = BrushesProvider.GetColor(name);
                    return new SolidColorBrush(System.Windows.Media.Color.FromScRgb(
                        color.A, color.R, color.G, color.B));
                }
                catch (Exception ex)
                {
                    SmartLogger.Log(LogLevel.Error, "[PaletteNameToBrushConverter]",
                        $"Ошибка преобразования палитры '{name}': {ex.Message}");
                }
            }
            return System.Windows.Media.Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException("Обратное преобразование не поддерживается");
    }
}