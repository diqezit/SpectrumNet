#nullable enable

using System.Globalization;
using System.Windows.Data;
using GL = OpenTK.Graphics.OpenGL4.GL;

namespace SpectrumNet
{
    public sealed class ShaderProgram : IDisposable
    {
        private static bool _isContextVerified;
        private static int _glslVersion = 400;
        private static readonly ConcurrentDictionary<string, ShaderProgram> _shaderCache = new();
        private bool _disposed;
        public int ProgramId { get; private set; }
        private int _colorLocation;
        private readonly string _vertexShaderSource;
        private readonly string _fragmentShaderSource;
        public Color4 Color { get; set; } = new(1.0f, 1.0f, 1.0f, 1.0f);

        public ShaderProgram(string vertexShaderSource, string fragmentShaderSource)
        {
            ArgumentNullException.ThrowIfNull(vertexShaderSource);
            ArgumentNullException.ThrowIfNull(fragmentShaderSource);

            VerifyGLContext();

            if (string.IsNullOrWhiteSpace(vertexShaderSource) || vertexShaderSource.Length < 20 ||
                !vertexShaderSource.Contains("#version") || !vertexShaderSource.Contains("main()"))
                throw new ArgumentException("Неверная структура вершинного шейдера");

            if (string.IsNullOrWhiteSpace(fragmentShaderSource) || fragmentShaderSource.Length < 20 ||
                !fragmentShaderSource.Contains("#version") || !fragmentShaderSource.Contains("main()"))
                throw new ArgumentException("Неверная структура фрагментного шейдера");

            // Проверка совместимости версий GLSL
            CheckShaderVersion(vertexShaderSource);
            CheckShaderVersion(fragmentShaderSource);

            _vertexShaderSource = vertexShaderSource.Trim('\uFEFF', '\u200B').Replace("\r", "").Trim();
            _fragmentShaderSource = fragmentShaderSource.Trim('\uFEFF', '\u200B').Replace("\r", "").Trim();

            int vertexShader = CompileShader(ShaderType.VertexShader, _vertexShaderSource);
            int fragmentShader = CompileShader(ShaderType.FragmentShader, _fragmentShaderSource);

            ProgramId = GL.CreateProgram();
            GL.AttachShader(ProgramId, vertexShader);
            GL.AttachShader(ProgramId, fragmentShader);
            GL.LinkProgram(ProgramId);

            GL.GetProgram(ProgramId, GetProgramParameterName.LinkStatus, out int linkStatus);
            if (linkStatus == 0)
                throw new Exception($"Ошибка линковки программы:\n{GL.GetProgramInfoLog(ProgramId)}");

            _colorLocation = GL.GetUniformLocation(ProgramId, "color");

            GL.DeleteShader(vertexShader);
            GL.DeleteShader(fragmentShader);
        }

        private static void VerifyGLContext()
        {
            if (_isContextVerified) return;

            try
            {
                string version = GL.GetString(StringName.Version) ??
                    throw new InvalidOperationException("Не удалось получить версию OpenGL");

                if (int.Parse(version.Split('.')[0]) < 3)
                    throw new NotSupportedException($"Требуется OpenGL 4.0+. Текущая версия: {version}");

                string glslVersionStr = GL.GetString(StringName.ShadingLanguageVersion) ??
                    throw new InvalidOperationException("Не удалось получить версию GLSL");

                var parts = glslVersionStr.Split(new[] { ' ', '.' }, StringSplitOptions.RemoveEmptyEntries);
                _glslVersion = int.Parse(parts[0]) * 100 + int.Parse(parts[1]);

                // Проверка аппаратных возможностей
                string vendor = GL.GetString(StringName.Vendor) ?? "Неизвестно";
                string renderer = GL.GetString(StringName.Renderer) ?? "Неизвестно";
                SmartLogger.Log(LogLevel.Debug, "Graphics Info:",
                    $"Vendor: {vendor}\nRenderer: {renderer}\nVersion: {version}");

                string extensions = GL.GetString(StringName.Extensions) ?? string.Empty;
                var extensionsList = extensions.Split(' ');

                if (!extensionsList.Contains("GL_ARB_separate_shader_objects"))
                    SmartLogger.Log(LogLevel.Warning, "Missing Extension",
                        "Требуемое расширение GL_ARB_separate_shader_objects отсутствует. Используется базовый рендеринг.");

                int maxUniformLocations = GL.GetInteger(GetPName.MaxVertexUniformComponents);
                if (maxUniformLocations < 1024)
                    throw new Exception($"Недостаточно uniform-локаций: {maxUniformLocations}");

                _isContextVerified = true;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Ошибка проверки контекста OpenGL", ex);
            }
        }

        private void CheckShaderVersion(string source)
        {
            var versionLine = source.Split('\n').FirstOrDefault(l => l.Trim().StartsWith("#version"));
            if (versionLine != null && int.TryParse(versionLine.Split(' ')[1], out int shaderVersion) &&
                shaderVersion > _glslVersion)
                throw new Exception($"Шейдер требует GLSL {shaderVersion}, но максимальная поддерживаемая версия {_glslVersion}");
        }

        private int CompileShader(ShaderType type, string source)
        {
            int shader = GL.CreateShader(type);
            GL.ShaderSource(shader, source);
            GL.CompileShader(shader);

            GL.GetShader(shader, ShaderParameter.CompileStatus, out int success);
            if (success == 0)
            {
                string log = GL.GetShaderInfoLog(shader);
                throw new Exception($"Ошибка компиляции шейдера:\n{log}");
            }
            return shader;
        }

        public static ShaderProgram GetOrCreate(string vertexSource, string fragmentSource)
        {
            ArgumentNullException.ThrowIfNull(vertexSource);
            ArgumentNullException.ThrowIfNull(fragmentSource);

            string key = $"{vertexSource.GetHashCode()}_{fragmentSource.GetHashCode()}";
            return _shaderCache.GetOrAdd(key, _ => new ShaderProgram(vertexSource, fragmentSource));
        }

        public ShaderProgram Clone()
        {
            if (string.IsNullOrEmpty(_vertexShaderSource) || string.IsNullOrEmpty(_fragmentShaderSource))
                throw new InvalidOperationException("Невозможно клонировать некорректный шейдер");

            var clone = GetOrCreate(_vertexShaderSource, _fragmentShaderSource);
            clone.Color = this.Color;
            return clone;
        }

        public void Use()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ShaderProgram));
            VerifyGLContext();
            GL.UseProgram(ProgramId);
            GL.Uniform4(_colorLocation, Color);
        }

        public void Dispose()
        {
            if (_disposed) return;
            GL.DeleteProgram(ProgramId);
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    public sealed class SpectrumBrushes : IDisposable
    {
        // Стандартные шейдеры
        private const string DefaultVertexShader = @"#version 400 core
layout(location = 0) in vec3 aPosition;
uniform mat4 projection;

void main() {
    gl_Position = projection * vec4(aPosition, 1.0);
}";

        private const string DefaultFragmentShader = @"#version 400 core
out vec4 FragColor;
uniform vec4 color;

void main() {
    FragColor = color;
}";

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

        private readonly ConcurrentDictionary<string, (Color4 Color, Lazy<ShaderProgram> Shader)> _palettes = new();
        private bool _disposed;
        private static readonly Lazy<SpectrumBrushes> _instance = new(() => new SpectrumBrushes());
        public static SpectrumBrushes Instance => _instance.Value;

        public IReadOnlyDictionary<string, Color4> RegisteredPalettes =>
            _palettes.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.Color,
                StringComparer.OrdinalIgnoreCase);

        public SpectrumBrushes()
        {
            // Инициализация палитр
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

                    var lazy = new Lazy<ShaderProgram>(() => {
                        try
                        {
                            var shader = new ShaderProgram(DefaultVertexShader, DefaultFragmentShader);
                            shader.Color = kvp.Value;
                            return shader;
                        }
                        catch (Exception ex)
                        {
                            SmartLogger.Log(LogLevel.Error, nameof(SpectrumBrushes),
                                $"Ошибка создания шейдера для '{kvp.Key}': {ex.Message}");
                            throw;
                        }
                    });

                    if (!_palettes.TryAdd(kvp.Key, (kvp.Value, lazy)))
                        SmartLogger.Log(LogLevel.Warning, nameof(SpectrumBrushes),
                            $"Обнаружен дубликат палитры '{kvp.Key}'.");
                }
                catch (Exception ex)
                {
                    SmartLogger.Log(LogLevel.Error, nameof(SpectrumBrushes),
                        $"Не удалось инициализировать палитру '{kvp.Key}': {ex.Message}");
                }
            }

            // Логирование доступных палитр
            SmartLogger.Log(LogLevel.Information, nameof(SpectrumBrushes),
                $"Зарегистрировано {_palettes.Count} палитр: {string.Join(", ", _palettes.Keys)}");

            if (_palettes.Count == 0)
                SmartLogger.Log(LogLevel.Warning, nameof(SpectrumBrushes),
                    "Не зарегистрировано ни одной палитры.");
        }

        public (Color4 Color, ShaderProgram Shader) GetColorAndShader(string paletteName)
        {
            ArgumentNullException.ThrowIfNull(paletteName);
            if (string.IsNullOrEmpty(paletteName))
                throw new ArgumentException("Значение не может быть пустым.", nameof(paletteName));

            if (!_palettes.TryGetValue(paletteName, out var palette))
                throw new KeyNotFoundException($"Палитра '{paletteName}' не зарегистрирована");

            try
            {
                return (palette.Color, palette.Shader.Value.Clone());
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, nameof(SpectrumBrushes),
                    $"Не удалось клонировать шейдер для '{paletteName}': {ex.Message}");
                throw;
            }
        }

        public Color4 GetColor(string paletteName)
        {
            ArgumentNullException.ThrowIfNull(paletteName);
            if (string.IsNullOrEmpty(paletteName))
                throw new ArgumentException("Значение не может быть пустым.", nameof(paletteName));

            if (_palettes.TryGetValue(paletteName, out var palette))
                return palette.Color;

            throw new KeyNotFoundException($"Палитра '{paletteName}' не зарегистрирована");
        }

        public void Dispose()
        {
            if (_disposed) return;

            foreach (var palette in _palettes.Values)
            {
                if (palette.Shader.IsValueCreated)
                    palette.Shader.Value.Dispose();
            }

            _disposed = true;
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