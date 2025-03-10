#nullable enable

using GL = OpenTK.Graphics.OpenGL4.GL;

namespace SpectrumNet
{
    /// <summary>
    /// Представляет программу шейдера OpenGL
    /// </summary>
    public sealed class ShaderProgram : IDisposable
    {
        #region Статические поля
        private static bool _isContextVerified;
        private static int _glslVersion = 400;
        private static readonly ConcurrentDictionary<string, ShaderProgram> _shaderCache = new();
        #endregion

        #region Поля и свойства
        private bool _disposed;
        private int _colorLocation;
        private readonly string _vertexShaderSource;
        private readonly string _fragmentShaderSource;

        public int ProgramId { get; private set; }
        public Color4 Color { get; set; } = new(1.0f, 1.0f, 1.0f, 1.0f);
        #endregion

        #region Конструктор
        public ShaderProgram(string vertexShaderSource, string fragmentShaderSource)
        {
            ArgumentNullException.ThrowIfNull(vertexShaderSource);
            ArgumentNullException.ThrowIfNull(fragmentShaderSource);

            VerifyGLContext();
            ValidateShaderSource(vertexShaderSource, isVertex: true);
            ValidateShaderSource(fragmentShaderSource, isVertex: false);

            // Нормализация исходного кода шейдеров
            _vertexShaderSource = NormalizeShaderSource(vertexShaderSource);
            _fragmentShaderSource = NormalizeShaderSource(fragmentShaderSource);

            // Компиляция и линковка шейдеров
            CompileAndLinkShaders();
        }
        #endregion

        #region Статические методы
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
        #endregion

        #region Вспомогательные методы
        private static string NormalizeShaderSource(string source) =>
            source.Trim('\uFEFF', '\u200B').Replace("\r", "").Trim();

        private static void ValidateShaderSource(string source, bool isVertex)
        {
            string shaderType = isVertex ? "вершинного" : "фрагментного";

            if (string.IsNullOrWhiteSpace(source) || source.Length < 20 ||
                !source.Contains("#version") || !source.Contains("main()"))
                throw new ArgumentException($"Неверная структура {shaderType} шейдера");
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

        private void CompileAndLinkShaders()
        {
            // Проверка совместимости версий GLSL
            CheckShaderVersion(_vertexShaderSource);
            CheckShaderVersion(_fragmentShaderSource);

            // Компиляция шейдеров
            int vertexShader = CompileShader(ShaderType.VertexShader, _vertexShaderSource);
            int fragmentShader = CompileShader(ShaderType.FragmentShader, _fragmentShaderSource);

            // Создание и линковка программы
            ProgramId = GL.CreateProgram();
            GL.AttachShader(ProgramId, vertexShader);
            GL.AttachShader(ProgramId, fragmentShader);
            GL.LinkProgram(ProgramId);

            GL.GetProgram(ProgramId, GetProgramParameterName.LinkStatus, out int linkStatus);
            if (linkStatus == 0)
                throw new Exception($"Ошибка линковки программы:\n{GL.GetProgramInfoLog(ProgramId)}");

            // Поиск uniform-переменной для цвета
            _colorLocation = GL.GetUniformLocation(ProgramId, "color");
            if (_colorLocation == -1)
                _colorLocation = GL.GetUniformLocation(ProgramId, "uColor");

            // Освобождение ресурсов шейдеров
            GL.DeleteShader(vertexShader);
            GL.DeleteShader(fragmentShader);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ShaderProgram));
        }
        #endregion

        #region Публичные методы
        public ShaderProgram Clone()
        {
            if (string.IsNullOrEmpty(_vertexShaderSource) || string.IsNullOrEmpty(_fragmentShaderSource))
                throw new InvalidOperationException("Невозможно клонировать некорректный шейдер");

            var clone = GetOrCreate(_vertexShaderSource, _fragmentShaderSource);
            clone.Color = Color;
            return clone;
        }

        public void Use()
        {
            ThrowIfDisposed();
            GL.UseProgram(ProgramId);

            if (_colorLocation != -1)
                GL.Uniform4(_colorLocation, Color);
        }

        public void SetUniform<T>(string name, T value)
        {
            ThrowIfDisposed();
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
                    throw new ArgumentException($"Неподдерживаемый тип для uniform: {typeof(T).Name}");
            }
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

            GL.DeleteProgram(ProgramId);
            _disposed = true;

            GC.SuppressFinalize(this);
        }
        #endregion
    }
}