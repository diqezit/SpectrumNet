#nullable enable

namespace SpectrumNet
{    
     /// <summary>
     /// Класс для отображения анимированного плейсхолдера, когда нет данных для визуализации
     /// </summary>
    public sealed class SpectrumPlaceholder : IDisposable
    {
        private const string LogPrefix = "Placeholder";

        private readonly IOpenGLService _glService;
        private int _vertexArrayObject;
        private int _vertexBufferObject;
        private int _shaderProgram;
        private float _hue = 0.0f;
        private bool _isInitialized = false;
        private bool _isDisposed = false;
        private float _width;
        private float _height;
        private Matrix4 _projectionMatrix;

        public SpectrumPlaceholder(IOpenGLService glService)
        {
            _glService = glService ?? throw new ArgumentNullException(nameof(glService));
        }

        public void UpdateDimensions(float width, float height)
        {
            if (_isDisposed)
                return;

            if (Math.Abs(_width - width) < 0.001f && Math.Abs(_height - height) < 0.001f)
                return;

            _width = width;
            _height = height;
            _projectionMatrix = Matrix4.CreateOrthographicOffCenter(0, width, height, 0, -1, 1);

            if (!_isInitialized)
                return;

            SmartLogger.Safe(() => UpdateVertexBuffers(), "Placeholder", "Error updating placeholder dimensions");
        }

        public void Render()
        {
            if (_isDisposed)
                return;

            if (!_isInitialized && _width > 0 && _height > 0)
            {
                bool success = SmartLogger.Safe(() =>
                {
                    Initialize();
                    return true;
                }, defaultValue: false);

                if (!success)
                {
                    GL.ClearColor(0.1f, 0.1f, 0.1f, 1.0f);
                    GL.Clear(ClearBufferMask.ColorBufferBit);
                    return;
                }
            }

            if (!_isInitialized)
            {
                GL.ClearColor(0.1f, 0.1f, 0.1f, 1.0f);
                GL.Clear(ClearBufferMask.ColorBufferBit);
                return;
            }

            bool renderSuccess = SmartLogger.Safe(() =>
            {
                GL.UseProgram(_shaderProgram);

                _hue = (_hue + 0.005f) % 1.0f;

                HsvToRgb(_hue, 0.8f, 0.8f, out float r, out float g, out float b);

                int colorLocation = GL.GetUniformLocation(_shaderProgram, "color");
                GL.Uniform3(colorLocation, r, g, b);

                int projectionLocation = GL.GetUniformLocation(_shaderProgram, "projection");
                GL.UniformMatrix4(projectionLocation, false, ref _projectionMatrix);

                GL.BindVertexArray(_vertexArrayObject);
                GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
                GL.BindVertexArray(0);

                return true;
            }, defaultValue: false);

            if (!renderSuccess)
            {
                _isInitialized = false;
                GL.ClearColor(0.1f, 0.1f, 0.1f, 1.0f);
                GL.Clear(ClearBufferMask.ColorBufferBit);
            }
        }

        private void Initialize()
        {
            if (_isDisposed || _isInitialized)
                return;

            bool success = SmartLogger.Safe(() =>
            {
                InitializeShader();
                InitializeVertexBuffers();
                _isInitialized = true;
                SmartLogger.Log(LogLevel.Debug, LogPrefix, "Placeholder initialized");
                return true;
            }, defaultValue: false);

            if (!success)
            {
                Cleanup();
                throw new Exception("Failed to initialize placeholder");
            }
        }

        private void InitializeShader()
        {
            string vertexShaderSource = @"
                #version 330 core
                layout (location = 0) in vec3 aPosition;
                uniform mat4 projection;
                void main()
                {
                    gl_Position = projection * vec4(aPosition, 1.0);
                }
            ";

            string fragmentShaderSource = @"
                #version 330 core
                out vec4 FragColor;
                uniform vec3 color;
                void main()
                {
                    FragColor = vec4(color, 1.0);
                }
            ";

            int vertexShader = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vertexShader, vertexShaderSource);
            GL.CompileShader(vertexShader);

            GL.GetShader(vertexShader, ShaderParameter.CompileStatus, out int success);
            if (success == 0)
            {
                string infoLog = GL.GetShaderInfoLog(vertexShader);
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Vertex shader compilation error: {infoLog}");
                GL.DeleteShader(vertexShader);
                throw new Exception("Vertex shader compilation error");
            }

            int fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fragmentShader, fragmentShaderSource);
            GL.CompileShader(fragmentShader);

            GL.GetShader(fragmentShader, ShaderParameter.CompileStatus, out success);
            if (success == 0)
            {
                string infoLog = GL.GetShaderInfoLog(fragmentShader);
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Fragment shader compilation error: {infoLog}");
                GL.DeleteShader(vertexShader);
                GL.DeleteShader(fragmentShader);
                throw new Exception("Fragment shader compilation error");
            }

            _shaderProgram = GL.CreateProgram();
            GL.AttachShader(_shaderProgram, vertexShader);
            GL.AttachShader(_shaderProgram, fragmentShader);
            GL.LinkProgram(_shaderProgram);

            GL.GetProgram(_shaderProgram, GetProgramParameterName.LinkStatus, out success);
            if (success == 0)
            {
                string infoLog = GL.GetProgramInfoLog(_shaderProgram);
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Shader program linking error: {infoLog}");
                GL.DeleteShader(vertexShader);
                GL.DeleteShader(fragmentShader);
                GL.DeleteProgram(_shaderProgram);
                throw new Exception("Shader program linking error");
            }

            GL.DetachShader(_shaderProgram, vertexShader);
            GL.DetachShader(_shaderProgram, fragmentShader);
            GL.DeleteShader(vertexShader);
            GL.DeleteShader(fragmentShader);
        }

        private void InitializeVertexBuffers()
        {
            _vertexArrayObject = GL.GenVertexArray();
            GL.BindVertexArray(_vertexArrayObject);

            _vertexBufferObject = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBufferObject);

            float[] vertices = {
                0, 0, 0,
                0, _height, 0,
                _width, _height, 0,

                _width, _height, 0,
                _width, 0, 0,
                0, 0, 0
            };

            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);

            GL.BindVertexArray(0);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        }

        private void UpdateVertexBuffers()
        {
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBufferObject);

            float[] vertices = {
                0, 0, 0,
                0, _height, 0,
                _width, _height, 0,

                _width, _height, 0,
                _width, 0, 0,
                0, 0, 0
            };

            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        }

        private void HsvToRgb(float h, float s, float v, out float r, out float g, out float b)
        {
            if (s == 0)
            {
                r = g = b = v;
                return;
            }

            h = h * 6;
            int i = (int)Math.Floor(h);
            float f = h - i;
            float p = v * (1 - s);
            float q = v * (1 - s * f);
            float t = v * (1 - s * (1 - f));

            switch (i % 6)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }
        }

        private void Cleanup()
        {
            SmartLogger.Safe(() =>
            {
                if (_isInitialized)
                {
                    GL.DeleteProgram(_shaderProgram);
                    GL.DeleteBuffer(_vertexBufferObject);
                    GL.DeleteVertexArray(_vertexArrayObject);
                    _isInitialized = false;
                }
            }, "Placeholder", "Error cleaning up placeholder resources");
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            Cleanup();
            _isDisposed = true;
        }
    }
}