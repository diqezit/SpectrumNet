#nullable enable 

using System.Globalization;
using System.Windows.Data;

namespace SpectrumNet
{
    /// <summary>
    /// Represents a shader program in OpenGL.
    /// </summary>
    public sealed class ShaderProgram : IDisposable
    {
        private bool _disposed;
        public int ProgramId { get; private set; }
        public Color Color { get; }

        public ShaderProgram(int programId, Color color)
        {
            ProgramId = programId;
            Color = color;
        }

        /// <summary>
        /// Clones the current shader program.
        /// </summary>
        /// <returns>A new instance of the shader program.</returns>
        public ShaderProgram Clone()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ShaderProgram));

            // Create a new shader program with the same color
            return new ShaderProgram(CreateShaderProgram(Color), Color);
        }

        private static int CreateShaderProgram(Color color)
        {
            string vertexShaderSource = @"
                #version 330 core
                layout(location = 0) in vec3 aPosition;
                
                uniform mat4 projection;
                uniform mat4 modelView;
                
                void main()
                {
                    gl_Position = projection * modelView * vec4(aPosition, 1.0);
                }";

            string fragmentShaderSource = $@"
                #version 330 core
                out vec4 FragColor;
                
                uniform vec4 color;
                
                void main()
                {{
                    FragColor = color;
                }}";

            int vertexShader = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vertexShader, vertexShaderSource);
            GL.CompileShader(vertexShader);

            int fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fragmentShader, fragmentShaderSource);
            GL.CompileShader(fragmentShader);

            int program = GL.CreateProgram();
            GL.AttachShader(program, vertexShader);
            GL.AttachShader(program, fragmentShader);
            GL.LinkProgram(program);

            GL.DeleteShader(vertexShader);
            GL.DeleteShader(fragmentShader);

            return program;
        }

        public void Use()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ShaderProgram));

            GL.UseProgram(ProgramId);

            GL.Uniform4(GL.GetUniformLocation(ProgramId, "color"),
                Color.R / 255f, Color.G / 255f, Color.B / 255f, Color.A / 255f);
        }

        public void Dispose()
        {
            if (_disposed) return;
            GL.DeleteProgram(ProgramId);
            ProgramId = 0;
            _disposed = true;
        }
    }

    /// <summary>
    /// Represents a named color palette with managed shader resources.
    /// </summary>
    public sealed record Palette(string Name, Color Color) : IDisposable
    {
        private bool _disposed;

        /// <summary>
        /// Gets the pre-configured shader for this palette.
        /// </summary>
        public ShaderProgram Shader { get; } = new ShaderProgram(
            CreateShaderProgram(Color), Color);

        private static int CreateShaderProgram(Color color)
        {
            string vertexShaderSource = @"
                #version 330 core
                layout(location = 0) in vec3 aPosition;
                
                uniform mat4 projection;
                uniform mat4 modelView;
                
                void main()
                {
                    gl_Position = projection * modelView * vec4(aPosition, 1.0);
                }";

            string fragmentShaderSource = @"
                #version 330 core
                out vec4 FragColor;
                
                uniform vec4 color;
                
                void main()
                {
                    FragColor = color;
                }";

            int vertexShader = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vertexShader, vertexShaderSource);
            GL.CompileShader(vertexShader);

            int fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fragmentShader, fragmentShaderSource);
            GL.CompileShader(fragmentShader);

            int program = GL.CreateProgram();
            GL.AttachShader(program, vertexShader);
            GL.AttachShader(program, fragmentShader);
            GL.LinkProgram(program);

            GL.DeleteShader(vertexShader);
            GL.DeleteShader(fragmentShader);

            return program;
        }

        /// <summary>
        /// Releases all resources used by the Palette.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            Shader.Dispose();
            _disposed = true;
        }
    }

    /// <summary>
    /// Provides predefined color palettes.
    /// </summary>
    public static class Palettes
    {
        public static readonly IReadOnlyDictionary<string, Color> ColorDefinitions =
            new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
            {
                // Basic colors
                { "Solid", Color.FromArgb(255, 33, 150, 243) }, 
                { "Mint", Color.FromArgb(255, 152, 251, 152) }, 
                { "Red", Color.Red },
                { "Green", Color.Green },
                { "Blue", Color.Blue },
                { "Yellow", Color.Yellow },
                { "Orange", Color.Orange },
                { "Purple", Color.Purple },
                { "Pink", Color.Pink },
                { "Brown", Color.Brown },
                { "Gray", Color.Gray },
                { "White", Color.White },
                { "Black", Color.Black },
            };
    }

    /// <summary>
    /// Manages palette resources with lifecycle control and thread-safe access.
    /// </summary>
    public sealed class SpectrumBrushes : IDisposable
    {
        private readonly ConcurrentDictionary<string, Palette> _palettes = new(StringComparer.OrdinalIgnoreCase);
        private bool _disposed;

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
        /// Retrieves color and shader resources for the specified palette.
        /// </summary>
        /// <param name="paletteName">Case-insensitive palette identifier.</param>
        /// <returns>Tuple containing color value and associated shader.</returns>
        /// <exception cref="ArgumentException">Thrown for invalid palette names.</exception>
        /// <exception cref="KeyNotFoundException">Thrown for unregistered palettes.</exception>
        public (Color Color, ShaderProgram Shader) GetColorAndShader(string paletteName)
        {
            EnsureNotDisposed();

            if (string.IsNullOrWhiteSpace(paletteName))
                throw new ArgumentException("Palette name cannot be empty", nameof(paletteName));

            if (_palettes.TryGetValue(paletteName, out var palette))
            {
                return (palette.Color, palette.Shader);
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
                    var (color, _) = BrushesProvider.GetColorAndShader(paletteName);
                    return new SolidColorBrush(WpfColor.FromArgb(
                        color.A, color.R, color.G, color.B));
                }
                catch (Exception)
                {
                    // If the palette is not found, return a transparent brush.
                    return WpfBrushes.Transparent;
                }
            }
            return WpfBrushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}