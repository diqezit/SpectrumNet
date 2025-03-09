#nullable enable

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

            // Проверяем оба варианта имени uniform-переменной для цвета
            _colorLocation = GL.GetUniformLocation(ProgramId, "color");
            if (_colorLocation == -1)
                _colorLocation = GL.GetUniformLocation(ProgramId, "uColor");

            if (_colorLocation == -1)
                SmartLogger.Log(LogLevel.Warning, nameof(ShaderProgram),
                    "Не удалось найти uniform-переменную для цвета (color или uColor)");

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

            if (_colorLocation != -1)
                GL.Uniform4(_colorLocation, Color);
        }

        public void SetUniform(string name, float value)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ShaderProgram));
            GL.UseProgram(ProgramId);
            int location = GL.GetUniformLocation(ProgramId, name);
            if (location != -1)
            {
                GL.Uniform1(location, value);
            }
        }

        public void SetUniform(string name, Vector2 value)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ShaderProgram));
            GL.UseProgram(ProgramId);
            int location = GL.GetUniformLocation(ProgramId, name);
            if (location != -1)
            {
                GL.Uniform2(location, value);
            }
        }

        public void SetUniform(string name, Vector3 value)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ShaderProgram));
            GL.UseProgram(ProgramId);
            int location = GL.GetUniformLocation(ProgramId, name);
            if (location != -1)
            {
                GL.Uniform3(location, value);
            }
        }

        public void SetUniform(string name, Vector4 value)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ShaderProgram));
            GL.UseProgram(ProgramId);
            int location = GL.GetUniformLocation(ProgramId, name);
            if (location != -1)
            {
                GL.Uniform4(location, value);
            }
        }

        public void SetUniform(string name, Matrix4 value)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ShaderProgram));
            GL.UseProgram(ProgramId);
            int location = GL.GetUniformLocation(ProgramId, name);
            if (location != -1)
            {
                GL.UniformMatrix4(location, false, ref value);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            GL.DeleteProgram(ProgramId);
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}