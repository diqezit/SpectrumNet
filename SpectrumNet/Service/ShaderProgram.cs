#nullable enable

using GL = OpenTK.Graphics.OpenGL4.GL;

namespace SpectrumNet
{
    /// <summary>
    /// Represents an OpenGL shader program
    /// </summary>
    public sealed class ShaderProgram : IDisposable
    {
        #region Static Fields
        private const string LogPrefix = "ShaderProgram";
        private static bool _isContextVerified;
        private static int _glslVersion = 400;
        private static readonly ConcurrentDictionary<string, ShaderProgram> _shaderCache = new();
        #endregion

        #region Fields and Properties
        private bool _disposed;
        private int _colorLocation;
        private readonly string _vertexShaderSource;
        private readonly string _fragmentShaderSource;

        public int ProgramId { get; private set; }
        public Color4 Color { get; set; } = new(1.0f, 1.0f, 1.0f, 1.0f);
        #endregion

        #region Constructor
        public ShaderProgram(string vertexShaderSource, string fragmentShaderSource)
        {
            ArgumentNullException.ThrowIfNull(vertexShaderSource);
            ArgumentNullException.ThrowIfNull(fragmentShaderSource);

            VerifyGLContext();
            ValidateShaderSource(vertexShaderSource, isVertex: true);
            ValidateShaderSource(fragmentShaderSource, isVertex: false);

            // Normalize shader source code
            _vertexShaderSource = NormalizeShaderSource(vertexShaderSource);
            _fragmentShaderSource = NormalizeShaderSource(fragmentShaderSource);

            // Compile and link shaders
            CompileAndLinkShaders();
        }
        #endregion

        #region Static Methods
        public static ShaderProgram GetOrCreate(string vertexSource, string fragmentSource)
        {
            ArgumentNullException.ThrowIfNull(vertexSource);
            ArgumentNullException.ThrowIfNull(fragmentSource);

            string key = $"{vertexSource.GetHashCode()}_{fragmentSource.GetHashCode()}";
            return _shaderCache.GetOrAdd(key, _ => new ShaderProgram(vertexSource, fragmentSource));
        }

        private static void VerifyGLContext()
        {
            if (_isContextVerified) return;

            SmartLogger.Safe(() => {
                string version = GL.GetString(StringName.Version) ??
                    throw new InvalidOperationException("Failed to get OpenGL version");

                if (int.Parse(version.Split('.')[0]) < 3)
                    throw new NotSupportedException($"OpenGL 4.0+ required. Current version: {version}");

                string glslVersionStr = GL.GetString(StringName.ShadingLanguageVersion) ??
                    throw new InvalidOperationException("Failed to get GLSL version");

                var parts = glslVersionStr.Split(new[] { ' ', '.' }, StringSplitOptions.RemoveEmptyEntries);
                _glslVersion = int.Parse(parts[0]) * 100 + int.Parse(parts[1]);

                int maxUniformLocations = GL.GetInteger(GetPName.MaxVertexUniformComponents);
                if (maxUniformLocations < 1024)
                    throw new Exception($"Insufficient uniform locations: {maxUniformLocations}");

                _isContextVerified = true;
            }, LogPrefix, "Error verifying OpenGL context");
        }
        #endregion

        #region Helper Methods
        private static string NormalizeShaderSource(string source) =>
            source.Trim('\uFEFF', '\u200B').Replace("\r", "").Trim();

        private static void ValidateShaderSource(string source, bool isVertex)
        {
            string shaderType = isVertex ? "vertex" : "fragment";

            if (string.IsNullOrWhiteSpace(source) || source.Length < 20 ||
                !source.Contains("#version") || !source.Contains("main()"))
                throw new ArgumentException($"Invalid {shaderType} shader structure");
        }

        private void CheckShaderVersion(string source)
        {
            var versionLine = source.Split('\n').FirstOrDefault(l => l.Trim().StartsWith("#version"));
            if (versionLine != null && int.TryParse(versionLine.Split(' ')[1], out int shaderVersion) &&
                shaderVersion > _glslVersion)
                throw new Exception($"Shader requires GLSL {shaderVersion}, but maximum supported version is {_glslVersion}");
        }

        private int CompileShader(ShaderType type, string source)
        {
            return SmartLogger.Safe(() => {
                int shader = GL.CreateShader(type);
                GL.ShaderSource(shader, source);
                GL.CompileShader(shader);

                GL.GetShader(shader, ShaderParameter.CompileStatus, out int success);
                if (success == 0)
                {
                    string log = GL.GetShaderInfoLog(shader);
                    throw new Exception($"Shader compilation error:\n{log}");
                }
                return shader;
            }, defaultValue: -1, LogPrefix, $"Error compiling {type} shader");
        }

        private void CompileAndLinkShaders()
        {
            SmartLogger.Safe(() => {
                // Check GLSL version compatibility
                CheckShaderVersion(_vertexShaderSource);
                CheckShaderVersion(_fragmentShaderSource);

                // Compile shaders
                int vertexShader = CompileShader(ShaderType.VertexShader, _vertexShaderSource);
                int fragmentShader = CompileShader(ShaderType.FragmentShader, _fragmentShaderSource);

                if (vertexShader == -1 || fragmentShader == -1)
                    throw new Exception("Failed to compile shaders");

                // Create and link program
                ProgramId = GL.CreateProgram();
                GL.AttachShader(ProgramId, vertexShader);
                GL.AttachShader(ProgramId, fragmentShader);
                GL.LinkProgram(ProgramId);

                GL.GetProgram(ProgramId, GetProgramParameterName.LinkStatus, out int linkStatus);
                if (linkStatus == 0)
                    throw new Exception($"Program linking error:\n{GL.GetProgramInfoLog(ProgramId)}");

                // Find color uniform variable
                _colorLocation = GL.GetUniformLocation(ProgramId, "color");
                if (_colorLocation == -1)
                    _colorLocation = GL.GetUniformLocation(ProgramId, "uColor");

                // Free shader resources
                GL.DeleteShader(vertexShader);
                GL.DeleteShader(fragmentShader);
            }, LogPrefix, "Error during shader compilation and linking");
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ShaderProgram));
        }
        #endregion

        #region Public Methods
        public ShaderProgram? Clone()
        {
            return SmartLogger.Safe(() => {
                if (string.IsNullOrEmpty(_vertexShaderSource) || string.IsNullOrEmpty(_fragmentShaderSource))
                    throw new InvalidOperationException("Cannot clone an invalid shader");

                var clone = GetOrCreate(_vertexShaderSource, _fragmentShaderSource);
                clone.Color = Color;
                return clone;
            }, defaultValue: null, LogPrefix, "Error cloning shader program");
        }

        public void Use()
        {
            ThrowIfDisposed();

            SmartLogger.Safe(() => {
                GL.UseProgram(ProgramId);

                if (_colorLocation != -1)
                    GL.Uniform4(_colorLocation, Color);
            }, LogPrefix, "Error using shader program");
        }

        public void SetUniform<T>(string name, T value)
        {
            ThrowIfDisposed();

            SmartLogger.Safe(() => {
                GL.UseProgram(ProgramId);

                int location = GL.GetUniformLocation(ProgramId, name);
                if (location == -1) return;

                switch (value)
                {
                    case float floatValue:
                        GL.Uniform1(location, floatValue);
                        break;
                    case Vector2 vec2:
                        GL.Uniform2(location, vec2);
                        break;
                    case Vector3 vec3:
                        GL.Uniform3(location, vec3);
                        break;
                    case Vector4 vec4:
                        GL.Uniform4(location, vec4);
                        break;
                    case Matrix4 mat4:
                        GL.UniformMatrix4(location, false, ref mat4);
                        break;
                    default:
                        throw new ArgumentException($"Unsupported type for uniform: {typeof(T).Name}");
                }
            }, LogPrefix, $"Error setting uniform '{name}'");
        }

        public void SetUniform(string name, int value)
        {
            ThrowIfDisposed();

            SmartLogger.Safe(() => {
                GL.UseProgram(ProgramId);

                int location = GL.GetUniformLocation(ProgramId, name);
                if (location != -1)
                {
                    GL.Uniform1(location, value);
                }
                else
                {
                    SmartLogger.Log(LogLevel.Warning, LogPrefix, $"Uniform '{name}' not found in shader program.");
                }
            }, LogPrefix, $"Error setting uniform '{name}'");
        }

        public void SetUniform(string name, float value) => SetUniform<float>(name, value);
        public void SetUniform(string name, Vector2 value) => SetUniform<Vector2>(name, value);
        public void SetUniform(string name, Vector3 value) => SetUniform<Vector3>(name, value);
        public void SetUniform(string name, Vector4 value) => SetUniform<Vector4>(name, value);
        public void SetUniform(string name, Matrix4 value) => SetUniform<Matrix4>(name, value);
        #endregion

        #region IDisposable
        public void Dispose()
        {
            if (_disposed) return;

            SmartLogger.Safe(() => {
                GL.DeleteProgram(ProgramId);
            }, LogPrefix, "Error disposing shader program");

            _disposed = true;
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}