#nullable enable

namespace SpectrumNet
{
    /// <summary>
    /// BarsRenderer – реализация рендерера спектра в виде вертикальных столбцов (баров).
    /// </summary>
    public class BarsRenderer : ISpectrumRenderer, IDisposable
    {
        private static BarsRenderer? _instance;
        private bool _isInitialized;
        private volatile bool _disposed;
        private readonly SemaphoreSlim _spectrumSemaphore = new(1, 1);
        const string LogPrefix = "[BarsRenderer] ";

        // OpenGL-ресурсы
        private int _vertexArrayObject;
        private int _vertexBufferObject;
        private int _indexBufferObject;
        private ShaderProgram? _barShader;
        private ShaderProgram? _highlightShader;
        private ShaderProgram? _glowShader;

        private const float MaxCornerRadius = 10f;
        private const float HighlightWidthProportion = 0.6f;
        private const float HighlightHeightProportion = 0.1f;
        private const float MaxHighlightHeight = 5f;
        private const float AlphaMultiplier = 1.5f;
        private const float HighlightAlphaDivisor = 3f;
        private const float DefaultCornerRadiusFactor = 5.0f;
        private const float GlowEffectAlpha = 0.25f;
        private const float MinBarHeight = 1f;

        private float[]? _previousSpectrum;
        private float[]? _processedSpectrum;
        private float _smoothingFactor = 0.3f;
        private RenderQuality _quality = RenderQuality.Medium;
        private bool _useGlowEffect = true;
        private bool _useAntiAlias = true;

        public RenderQuality Quality
        {
            get => _quality;
            set
            {
                if (_quality != value)
                {
                    _quality = value;
                    ApplyQualitySettings();
                }
            }
        }

        public static BarsRenderer GetInstance() => _instance ??= new BarsRenderer();
        private BarsRenderer() { }

        public void Initialize()
        {
            if (!_isInitialized)
            {
                _isInitialized = true;
                _vertexArrayObject = GL.GenVertexArray();
                _vertexBufferObject = GL.GenBuffer();
                _indexBufferObject = GL.GenBuffer();
                InitializeShaders();
                ApplyQualitySettings();
                Log.Debug("BarsRenderer initialized with OpenGL");
            }
        }

        private void InitializeShaders()
        {
            // Шейдер для баров
            string barVertexShader = @"
                #version 330 core
                layout(location = 0) in vec3 aPosition;
                uniform mat4 uProjection;
                uniform mat4 uModelView;
                void main() { gl_Position = uProjection * uModelView * vec4(aPosition, 1.0); }";
            string barFragmentShader = @"
                #version 330 core
                out vec4 FragColor;
                uniform vec4 uColor;
                void main() { FragColor = uColor; }";

            // Шейдер для подсветки (использует тот же код, что и базовый)
            string highlightVertexShader = barVertexShader;
            string highlightFragmentShader = barFragmentShader;

            // Шейдер для свечения
            string glowVertexShader = barVertexShader;
            string glowFragmentShader = @"
                #version 330 core
                out vec4 FragColor;
                uniform vec4 uColor;
                uniform float uBlurRadius;
                void main() { FragColor = uColor; }";

            try
            {
                _barShader = CompileShaderProgram(barVertexShader, barFragmentShader);
                _highlightShader = CompileShaderProgram(highlightVertexShader, highlightFragmentShader);
                _glowShader = CompileShaderProgram(glowVertexShader, glowFragmentShader);
            }
            catch (Exception ex)
            {
                Log.Error($"Error initializing shaders: {ex.Message}");
            }
        }

        private ShaderProgram CompileShaderProgram(string vertexSource, string fragmentSource)
        {
            int vertexShader = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vertexShader, vertexSource);
            GL.CompileShader(vertexShader);
            GL.GetShader(vertexShader, ShaderParameter.CompileStatus, out int compileStatus);
            if (compileStatus == 0)
            {
                string infoLog = GL.GetShaderInfoLog(vertexShader);
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Vertex shader compilation failed: {infoLog}");
                GL.DeleteShader(vertexShader);
                throw new InvalidOperationException("Vertex shader compilation failed");
            }

            int fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fragmentShader, fragmentSource);
            GL.CompileShader(fragmentShader);
            GL.GetShader(fragmentShader, ShaderParameter.CompileStatus, out compileStatus);
            if (compileStatus == 0)
            {
                string infoLog = GL.GetShaderInfoLog(fragmentShader);
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Fragment shader compilation failed: {infoLog}");
                GL.DeleteShader(vertexShader);
                GL.DeleteShader(fragmentShader);
                throw new InvalidOperationException("Fragment shader compilation failed");
            }

            int program = GL.CreateProgram();
            GL.AttachShader(program, vertexShader);
            GL.AttachShader(program, fragmentShader);
            GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linkStatus);
            if (linkStatus == 0)
            {
                string infoLog = GL.GetProgramInfoLog(program);
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Shader program linking failed: {infoLog}");
                GL.DeleteShader(vertexShader);
                GL.DeleteShader(fragmentShader);
                GL.DeleteProgram(program);
                throw new InvalidOperationException("Shader program linking failed");
            }
            GL.DetachShader(program, vertexShader);
            GL.DetachShader(program, fragmentShader);
            GL.DeleteShader(vertexShader);
            GL.DeleteShader(fragmentShader);

            return new ShaderProgram(vertexSource, fragmentSource);
        }

        private void ApplyQualitySettings()
        {
            switch (_quality)
            {
                case RenderQuality.Low:
                    _useAntiAlias = false;
                    _useGlowEffect = false;
                    GL.Disable(EnableCap.Multisample);
                    break;
                case RenderQuality.Medium:
                    _useAntiAlias = true;
                    _useGlowEffect = true;
                    GL.Enable(EnableCap.Multisample);
                    break;
                case RenderQuality.High:
                    _useAntiAlias = true;
                    _useGlowEffect = true;
                    GL.Enable(EnableCap.Multisample);
                    GL.Enable(EnableCap.LineSmooth);
                    GL.Hint(HintTarget.LineSmoothHint, HintMode.Nicest);
                    break;
            }
            Log.Debug($"BarsRenderer quality set to {_quality}");
        }

        public void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium)
        {
            _smoothingFactor = isOverlayActive ? 0.5f : 0.3f;
            Quality = quality;
        }

        public void Render(
            float[]? spectrum,
            Viewport viewport,
            float barWidth,
            float barSpacing,
            int barCount,
            ShaderProgram? shader,
            Action<Viewport> drawPerformanceInfo)
        {
            if (!ValidateRenderParameters(spectrum, viewport, shader))
                return;

            float[] renderSpectrum;
            bool semaphoreAcquired = false;
            try
            {
                semaphoreAcquired = _spectrumSemaphore.Wait(0);
                if (semaphoreAcquired)
                {
                    int targetBarCount = Math.Min(spectrum!.Length, barCount);
                    float[] scaledSpectrum = ScaleSpectrum(spectrum!, targetBarCount, spectrum!.Length);
                    _processedSpectrum = SmoothSpectrum(scaledSpectrum, targetBarCount);
                }
                renderSpectrum = _processedSpectrum ?? ProcessSynchronously(spectrum!, Math.Min(spectrum!.Length, barCount));
            }
            catch (Exception ex)
            {
                Log.Error($"Error processing spectrum: {ex.Message}");
                return;
            }
            finally
            {
                if (semaphoreAcquired)
                    _spectrumSemaphore.Release();
            }

            Matrix4 projectionMatrix = Matrix4.CreateOrthographicOffCenter(0, viewport.Width, viewport.Height, 0, -1, 1);
            Matrix4 modelViewMatrix = Matrix4.Identity;
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            RenderBars(renderSpectrum, viewport, barWidth, barSpacing, shader!, projectionMatrix, modelViewMatrix);
            drawPerformanceInfo?.Invoke(viewport);
        }

        private bool ValidateRenderParameters(float[]? spectrum, Viewport viewport, ShaderProgram? shader)
        {
            if (!_isInitialized)
            {
                Log.Error("BarsRenderer not initialized before rendering");
                return false;
            }
            if (spectrum == null || spectrum.Length < 2 || shader == null || viewport.Width <= 0 || viewport.Height <= 0)
            {
                Log.Error("Invalid render parameters for BarsRenderer");
                return false;
            }
            return true;
        }

        private float[] ProcessSynchronously(float[] spectrum, int targetCount)
        {
            var scaledSpectrum = ScaleSpectrum(spectrum, targetCount, spectrum.Length);
            return SmoothSpectrum(scaledSpectrum, targetCount);
        }

        private void RenderBars(
            float[] spectrum,
            Viewport viewport,
            float barWidth,
            float barSpacing,
            ShaderProgram baseShader,
            Matrix4 projectionMatrix,
            Matrix4 modelViewMatrix)
        {
            float totalBarWidth = barWidth + barSpacing;
            float canvasHeight = viewport.Height;
            GL.BindVertexArray(_vertexArrayObject);
            float cornerRadius = MathF.Min(barWidth * DefaultCornerRadiusFactor, MaxCornerRadius);

            for (int i = 0; i < spectrum.Length; i++)
            {
                float magnitude = spectrum[i];
                float barHeight = MathF.Max(magnitude * canvasHeight, MinBarHeight);
                byte alpha = (byte)MathF.Min(magnitude * AlphaMultiplier * 255f, 255f);
                Color4 baseColor = baseShader.Color;
                // Создаём цвет бара с учетом вычисленного альфа-канала (в диапазоне 0..1)
                Color4 barColor = new Color4(baseColor.R, baseColor.G, baseColor.B, alpha / 255f);
                float x = i * totalBarWidth;

                if (_useGlowEffect && _glowShader != null && magnitude > 0.6f)
                {
                    // Для свечения используем базовый цвет с уменьшенной прозрачностью
                    Color4 glowColor = new Color4(baseColor.R, baseColor.G, baseColor.B, magnitude * GlowEffectAlpha);
                    RenderGlowEffect(x, barWidth, barHeight, canvasHeight, cornerRadius, glowColor, projectionMatrix, modelViewMatrix);
                }

                RenderBar(x, barWidth, barHeight, canvasHeight, cornerRadius, barColor, projectionMatrix, modelViewMatrix);

                if (barHeight > cornerRadius * 2 && _quality != RenderQuality.Low)
                {
                    float highlightWidth = barWidth * HighlightWidthProportion;
                    float highlightHeight = MathF.Min(barHeight * HighlightHeightProportion, MaxHighlightHeight);
                    byte highlightAlpha = (byte)(alpha / HighlightAlphaDivisor);
                    Color4 highlightColor = new Color4(Color4.White.R, Color4.White.G, Color4.White.B, highlightAlpha / 255f);
                    RenderHighlight(x, barWidth, barHeight, canvasHeight, highlightWidth, highlightHeight, highlightColor, projectionMatrix, modelViewMatrix);
                }
            }
            GL.BindVertexArray(0);
            GL.Disable(EnableCap.Blend);
        }

        private void RenderBar(
            float x,
            float barWidth,
            float barHeight,
            float canvasHeight,
            float cornerRadius,
            Color4 barColor,
            Matrix4 projectionMatrix,
            Matrix4 modelViewMatrix)
        {
            if (_barShader == null) return;
            GL.UseProgram(_barShader.ProgramId);
            int locProj = GL.GetUniformLocation(_barShader.ProgramId, "uProjection");
            int locModelView = GL.GetUniformLocation(_barShader.ProgramId, "uModelView");
            int locColor = GL.GetUniformLocation(_barShader.ProgramId, "uColor");
            GL.UniformMatrix4(locProj, false, ref projectionMatrix);
            GL.UniformMatrix4(locModelView, false, ref modelViewMatrix);
            GL.Uniform4(locColor, barColor);

            float[] vertices = {
                x, canvasHeight - barHeight, 0.0f,
                x + barWidth, canvasHeight - barHeight, 0.0f,
                x + barWidth, canvasHeight, 0.0f,
                x, canvasHeight, 0.0f
            };
            int[] indices = { 0, 1, 2, 0, 2, 3 };

            GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBufferObject);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StreamDraw);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _indexBufferObject);
            GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(int), indices, BufferUsageHint.StreamDraw);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
            GL.DrawElements(PrimitiveType.Triangles, indices.Length, DrawElementsType.UnsignedInt, 0);
            GL.DisableVertexAttribArray(0);
        }

        private void RenderHighlight(
            float x,
            float barWidth,
            float barHeight,
            float canvasHeight,
            float highlightWidth,
            float highlightHeight,
            Color4 highlightColor,
            Matrix4 projectionMatrix,
            Matrix4 modelViewMatrix)
        {
            if (_highlightShader == null) return;
            GL.UseProgram(_highlightShader.ProgramId);
            int locProj = GL.GetUniformLocation(_highlightShader.ProgramId, "uProjection");
            int locModelView = GL.GetUniformLocation(_highlightShader.ProgramId, "uModelView");
            int locColor = GL.GetUniformLocation(_highlightShader.ProgramId, "uColor");
            GL.UniformMatrix4(locProj, false, ref projectionMatrix);
            GL.UniformMatrix4(locModelView, false, ref modelViewMatrix);
            GL.Uniform4(locColor, highlightColor);

            float highlightX = x + (barWidth - highlightWidth) / 2;
            float[] vertices = {
                highlightX, canvasHeight - barHeight, 0.0f,
                highlightX + highlightWidth, canvasHeight - barHeight, 0.0f,
                highlightX + highlightWidth, canvasHeight - barHeight + highlightHeight, 0.0f,
                highlightX, canvasHeight - barHeight + highlightHeight, 0.0f
            };
            int[] indices = { 0, 1, 2, 0, 2, 3 };

            GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBufferObject);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StreamDraw);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _indexBufferObject);
            GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(int), indices, BufferUsageHint.StreamDraw);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
            GL.DrawElements(PrimitiveType.Triangles, indices.Length, DrawElementsType.UnsignedInt, 0);
            GL.DisableVertexAttribArray(0);
        }

        private void RenderGlowEffect(
            float x,
            float barWidth,
            float barHeight,
            float canvasHeight,
            float cornerRadius,
            Color4 glowColor,
            Matrix4 projectionMatrix,
            Matrix4 modelViewMatrix)
        {
            if (_glowShader == null) return;
            GL.UseProgram(_glowShader.ProgramId);
            int locProj = GL.GetUniformLocation(_glowShader.ProgramId, "uProjection");
            int locModelView = GL.GetUniformLocation(_glowShader.ProgramId, "uModelView");
            int locColor = GL.GetUniformLocation(_glowShader.ProgramId, "uColor");
            int locBlurRadius = GL.GetUniformLocation(_glowShader.ProgramId, "uBlurRadius");
            GL.UniformMatrix4(locProj, false, ref projectionMatrix);
            GL.UniformMatrix4(locModelView, false, ref modelViewMatrix);
            GL.Uniform4(locColor, glowColor);
            GL.Uniform1(locBlurRadius, 5.0f);

            float glowExtraSize = 5.0f;
            float[] vertices = {
                x - glowExtraSize, canvasHeight - barHeight - glowExtraSize, 0.0f,
                x + barWidth + glowExtraSize, canvasHeight - barHeight - glowExtraSize, 0.0f,
                x + barWidth + glowExtraSize, canvasHeight + glowExtraSize, 0.0f,
                x - glowExtraSize, canvasHeight + glowExtraSize, 0.0f
            };
            int[] indices = { 0, 1, 2, 0, 2, 3 };

            GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBufferObject);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StreamDraw);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _indexBufferObject);
            GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(int), indices, BufferUsageHint.StreamDraw);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
            GL.DrawElements(PrimitiveType.Triangles, indices.Length, DrawElementsType.UnsignedInt, 0);
            GL.DisableVertexAttribArray(0);
        }

        private static float[] ScaleSpectrum(float[] spectrum, int barCount, int spectrumLength)
        {
            float[] scaledSpectrum = new float[barCount];
            float blockSize = (float)spectrumLength / barCount;
            for (int i = 0; i < barCount; i++)
            {
                float sum = 0;
                int start = (int)(i * blockSize);
                int end = Math.Min((int)((i + 1) * blockSize), spectrumLength);
                for (int j = start; j < end; j++)
                    sum += spectrum[j];
                scaledSpectrum[i] = sum / (end - start);
            }
            return scaledSpectrum;
        }

        private float[] SmoothSpectrum(float[] scaledSpectrum, int actualBarCount)
        {
            if (_previousSpectrum == null || _previousSpectrum.Length != actualBarCount)
                _previousSpectrum = new float[actualBarCount];
            float[] smoothedSpectrum = new float[actualBarCount];
            for (int i = 0; i < actualBarCount; i++)
            {
                smoothedSpectrum[i] = _previousSpectrum[i] * (1 - _smoothingFactor) + scaledSpectrum[i] * _smoothingFactor;
                _previousSpectrum[i] = smoothedSpectrum[i];
            }
            return smoothedSpectrum;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _spectrumSemaphore.Dispose();
                    if (_vertexArrayObject != 0)
                    {
                        GL.DeleteVertexArray(_vertexArrayObject);
                        _vertexArrayObject = 0;
                    }
                    if (_vertexBufferObject != 0)
                    {
                        GL.DeleteBuffer(_vertexBufferObject);
                        _vertexBufferObject = 0;
                    }
                    if (_indexBufferObject != 0)
                    {
                        GL.DeleteBuffer(_indexBufferObject);
                        _indexBufferObject = 0;
                    }
                    _barShader?.Dispose();
                    _barShader = null;
                    _highlightShader?.Dispose();
                    _highlightShader = null;
                    _glowShader?.Dispose();
                    _glowShader = null;
                    _previousSpectrum = null;
                    _processedSpectrum = null;
                }
                _disposed = true;
                Log.Debug("BarsRenderer disposed");
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}