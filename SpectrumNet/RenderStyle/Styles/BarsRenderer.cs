namespace SpectrumNet
{
    #region Renderers Implementations

    /// <summary>
    /// **BarsRenderer** - реализация рендерера спектра в виде вертикальных столбцов (баров).
    /// <br/>
    /// **BarsRenderer** - spectrum renderer visualizing audio spectrum as vertical bars.
    /// </summary>
    public class BarsRenderer : ISpectrumRenderer, IDisposable
    {
        #region Fields
        private static BarsRenderer? _instance;
        private bool _isInitialized;
        private volatile bool _disposed;
        private readonly SemaphoreSlim _spectrumSemaphore = new(1, 1);

        // OpenGL resources
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
        #endregion

        #region Properties
        /// <inheritdoc />
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
        #endregion

        #region Constructor and Initialization
        private BarsRenderer() { }

        /// <summary>
        /// Возвращает единственный экземпляр BarsRenderer (Singleton).
        /// <br/>
        /// Returns the singleton instance of BarsRenderer.
        /// </summary>
        /// <returns>Экземпляр BarsRenderer. / BarsRenderer instance.</returns>
        public static BarsRenderer GetInstance() => _instance ??= new BarsRenderer();

        /// <inheritdoc />
        public void Initialize()
        {
            if (!_isInitialized)
            {
                _isInitialized = true;

                // Создание VAO и буферов
                _vertexArrayObject = GL.GenVertexArray();
                _vertexBufferObject = GL.GenBuffer();
                _indexBufferObject = GL.GenBuffer();

                // Инициализация шейдеров
                InitializeShaders();

                // Применение настроек качества
                ApplyQualitySettings();

                Log.Debug("BarsRenderer initialized with OpenGL");
            }
        }

        private void InitializeShaders()
        {
            // Базовый шейдер для баров
            string barVertexShader = @"
                #version 330 core
                layout(location = 0) in vec3 aPosition;
                
                uniform mat4 uProjection;
                uniform mat4 uModelView;
                
                void main()
                {
                    gl_Position = uProjection * uModelView * vec4(aPosition, 1.0);
                }";

            string barFragmentShader = @"
                #version 330 core
                out vec4 FragColor;
                
                uniform vec4 uColor;
                
                void main()
                {
                    FragColor = uColor;
                }";

            // Шейдер для подсветки
            string highlightVertexShader = @"
                #version 330 core
                layout(location = 0) in vec3 aPosition;
                
                uniform mat4 uProjection;
                uniform mat4 uModelView;
                
                void main()
                {
                    gl_Position = uProjection * uModelView * vec4(aPosition, 1.0);
                }";

            string highlightFragmentShader = @"
                #version 330 core
                out vec4 FragColor;
                
                uniform vec4 uColor;
                
                void main()
                {
                    FragColor = uColor;
                }";

            // Шейдер для свечения
            string glowVertexShader = @"
                #version 330 core
                layout(location = 0) in vec3 aPosition;
                
                uniform mat4 uProjection;
                uniform mat4 uModelView;
                
                void main()
                {
                    gl_Position = uProjection * uModelView * vec4(aPosition, 1.0);
                }";

            string glowFragmentShader = @"
                #version 330 core
                out vec4 FragColor;
                
                uniform vec4 uColor;
                uniform float uBlurRadius;
                
                void main()
                {
                    FragColor = uColor;
                }";

            // Компиляция шейдеров
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

            string vertexInfoLog = GL.GetShaderInfoLog(vertexShader);
            if (!string.IsNullOrWhiteSpace(vertexInfoLog))
                Log.Debug($"Vertex shader compile log: {vertexInfoLog}");

            int fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fragmentShader, fragmentSource);
            GL.CompileShader(fragmentShader);

            string fragmentInfoLog = GL.GetShaderInfoLog(fragmentShader);
            if (!string.IsNullOrWhiteSpace(fragmentInfoLog))
                Log.Debug($"Fragment shader compile log: {fragmentInfoLog}");

            int program = GL.CreateProgram();
            GL.AttachShader(program, vertexShader);
            GL.AttachShader(program, fragmentShader);
            GL.LinkProgram(program);

            GL.DetachShader(program, vertexShader);
            GL.DetachShader(program, fragmentShader);
            GL.DeleteShader(vertexShader);
            GL.DeleteShader(fragmentShader);

            return new ShaderProgram(program, Color.White);
        }

        /// <summary>
        /// Applies quality settings based on current quality level
        /// </summary>
        private void ApplyQualitySettings()
        {
            switch (_quality)
            {
                case RenderQuality.Low:
                    _useAntiAlias = false;
                    _useGlowEffect = false;
                    // Отключаем сглаживание в OpenGL
                    GL.Disable(EnableCap.Multisample);
                    break;
                case RenderQuality.Medium:
                    _useAntiAlias = true;
                    _useGlowEffect = true;
                    // Включаем базовое сглаживание
                    GL.Enable(EnableCap.Multisample);
                    break;
                case RenderQuality.High:
                    _useAntiAlias = true;
                    _useGlowEffect = true;
                    // Включаем расширенное сглаживание
                    GL.Enable(EnableCap.Multisample);
                    GL.Enable(EnableCap.LineSmooth);
                    GL.Hint(HintTarget.LineSmoothHint, HintMode.Nicest);
                    break;
            }

            Log.Debug($"BarsRenderer quality set to {_quality}");
        }
        #endregion

        #region Configuration
        /// <inheritdoc />
        public void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium)
        {
            _smoothingFactor = isOverlayActive ? 0.5f : 0.3f;
            Quality = quality;
        }
        #endregion

        #region Rendering
        /// <inheritdoc />
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
            {
                return;
            }

            float[] renderSpectrum;
            bool semaphoreAcquired = false;
            int renderedBarCount;

            try
            {
                semaphoreAcquired = _spectrumSemaphore.Wait(0);

                if (semaphoreAcquired)
                {
                    // Обработка спектра
                    int targetBarCount = Math.Min(spectrum!.Length, barCount);
                    float[] scaledSpectrum = ScaleSpectrum(spectrum!, targetBarCount, spectrum!.Length);
                    _processedSpectrum = SmoothSpectrum(scaledSpectrum, targetBarCount);
                }

                renderSpectrum = _processedSpectrum ??
                                 ProcessSynchronously(spectrum!, Math.Min(spectrum!.Length, barCount));

                renderedBarCount = renderSpectrum.Length;
            }
            catch (Exception ex)
            {
                Log.Error($"Error processing spectrum: {ex.Message}");
                return;
            }
            finally
            {
                if (semaphoreAcquired)
                {
                    _spectrumSemaphore.Release();
                }
            }

            // Настройка проекционной матрицы
            Matrix4 projectionMatrix = Matrix4.CreateOrthographicOffCenter(
                0, viewport.Width, viewport.Height, 0, -1, 1);

            // Настройка модельно-видовой матрицы
            Matrix4 modelViewMatrix = Matrix4.Identity;

            // Включение прозрачности
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            // Рендеринг баров
            RenderBars(renderSpectrum, viewport, barWidth, barSpacing, shader,
                      projectionMatrix, modelViewMatrix);

            // Отображение информации о производительности
            drawPerformanceInfo?.Invoke(viewport);
        }

        private bool ValidateRenderParameters(
            float[]? spectrum,
            Viewport viewport,
            ShaderProgram? shader)
        {
            if (!_isInitialized)
            {
                Log.Error("BarsRenderer not initialized before rendering");
                return false;
            }

            if (spectrum == null || spectrum.Length < 2 ||
                shader == null ||
                viewport.Width <= 0 || viewport.Height <= 0)
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

            // Привязка VAO
            GL.BindVertexArray(_vertexArrayObject);

            float cornerRadius = MathF.Min(barWidth * DefaultCornerRadiusFactor, MaxCornerRadius);

            for (int i = 0; i < spectrum.Length; i++)
            {
                float magnitude = spectrum[i];
                float barHeight = MathF.Max(magnitude * canvasHeight, MinBarHeight);
                byte alpha = (byte)MathF.Min(magnitude * AlphaMultiplier * 255f, 255f);
                Color barColor = Color.FromArgb(alpha, baseShader.Color);

                float x = i * totalBarWidth;

                // Эффект свечения для высоких значений
                if (_useGlowEffect && _glowShader != null && magnitude > 0.6f)
                {
                    Color glowColor = Color.FromArgb((byte)(magnitude * 255f * GlowEffectAlpha), barColor);
                    RenderGlowEffect(x, barWidth, barHeight, canvasHeight, cornerRadius, glowColor,
                                    projectionMatrix, modelViewMatrix);
                }

                // Отрисовка бара
                RenderBar(x, barWidth, barHeight, canvasHeight, cornerRadius, barColor,
                         projectionMatrix, modelViewMatrix);

                // Отрисовка белой подсветки сверху бара
                if (barHeight > cornerRadius * 2 && _quality != RenderQuality.Low)
                {
                    float highlightWidth = barWidth * HighlightWidthProportion;
                    float highlightHeight = MathF.Min(barHeight * HighlightHeightProportion, MaxHighlightHeight);
                    byte highlightAlpha = (byte)(alpha / HighlightAlphaDivisor);
                    Color highlightColor = Color.FromArgb(highlightAlpha, Color.White);

                    RenderHighlight(x, barWidth, barHeight, canvasHeight, highlightWidth, highlightHeight,
                                   highlightColor, projectionMatrix, modelViewMatrix);
                }
            }

            // Отвязка VAO
            GL.BindVertexArray(0);

            // Восстановление состояния OpenGL
            GL.Disable(EnableCap.Blend);
        }

        private void RenderBar(
            float x,
            float barWidth,
            float barHeight,
            float canvasHeight,
            float cornerRadius,
            Color barColor,
            Matrix4 projectionMatrix,
            Matrix4 modelViewMatrix)
        {
            if (_barShader == null) return;

            // Активация шейдера
            GL.UseProgram(_barShader.ProgramId);

            // Установка параметров шейдера
            int locationProjection = GL.GetUniformLocation(_barShader.ProgramId, "uProjection");
            int locationModelView = GL.GetUniformLocation(_barShader.ProgramId, "uModelView");
            int locationColor = GL.GetUniformLocation(_barShader.ProgramId, "uColor");

            GL.UniformMatrix4(locationProjection, false, ref projectionMatrix);
            GL.UniformMatrix4(locationModelView, false, ref modelViewMatrix);
            GL.Uniform4(locationColor, barColor.R / 255f, barColor.G / 255f, barColor.B / 255f, barColor.A / 255f);

            // Создание вершин бара
            float[] vertices = {
                x, canvasHeight - barHeight, 0.0f,            // Верхний левый
                x + barWidth, canvasHeight - barHeight, 0.0f, // Верхний правый
                x + barWidth, canvasHeight, 0.0f,             // Нижний правый
                x, canvasHeight, 0.0f                         // Нижний левый
            };

            // Индексы для двух треугольников
            int[] indices = {
                0, 1, 2,  // Первый треугольник
                0, 2, 3   // Второй треугольник
            };

            // Загрузка вершин и индексов в буферы
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBufferObject);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StreamDraw);

            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _indexBufferObject);
            GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(int), indices, BufferUsageHint.StreamDraw);

            // Настройка формата вершин
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);

            // Рендеринг
            GL.DrawElements(PrimitiveType.Triangles, indices.Length, DrawElementsType.UnsignedInt, 0);

            // Отключение атрибутов
            GL.DisableVertexAttribArray(0);
        }

        private void RenderHighlight(
            float x,
            float barWidth,
            float barHeight,
            float canvasHeight,
            float highlightWidth,
            float highlightHeight,
            Color highlightColor,
            Matrix4 projectionMatrix,
            Matrix4 modelViewMatrix)
        {
            if (_highlightShader == null) return;

            // Активация шейдера подсветки
            GL.UseProgram(_highlightShader.ProgramId);

            // Установка параметров шейдера
            int locationProjection = GL.GetUniformLocation(_highlightShader.ProgramId, "uProjection");
            int locationModelView = GL.GetUniformLocation(_highlightShader.ProgramId, "uModelView");
            int locationColor = GL.GetUniformLocation(_highlightShader.ProgramId, "uColor");

            GL.UniformMatrix4(locationProjection, false, ref projectionMatrix);
            GL.UniformMatrix4(locationModelView, false, ref modelViewMatrix);
            GL.Uniform4(locationColor, highlightColor.R / 255f, highlightColor.G / 255f,
                       highlightColor.B / 255f, highlightColor.A / 255f);

            // Расчет позиции подсветки
            float highlightX = x + (barWidth - highlightWidth) / 2;

            // Создание вершин подсветки
            float[] vertices = {
                highlightX, canvasHeight - barHeight, 0.0f,                           // Верхний левый
                highlightX + highlightWidth, canvasHeight - barHeight, 0.0f,          // Верхний правый
                highlightX + highlightWidth, canvasHeight - barHeight + highlightHeight, 0.0f, // Нижний правый
                highlightX, canvasHeight - barHeight + highlightHeight, 0.0f          // Нижний левый
            };

            // Индексы для двух треугольников
            int[] indices = {
                0, 1, 2,  // Первый треугольник
                0, 2, 3   // Второй треугольник
            };

            // Загрузка вершин и индексов в буферы
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBufferObject);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StreamDraw);

            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _indexBufferObject);
            GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(int), indices, BufferUsageHint.StreamDraw);

            // Настройка формата вершин
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);

            // Рендеринг
            GL.DrawElements(PrimitiveType.Triangles, indices.Length, DrawElementsType.UnsignedInt, 0);

            // Отключение атрибутов
            GL.DisableVertexAttribArray(0);
        }

        private void RenderGlowEffect(
            float x,
            float barWidth,
            float barHeight,
            float canvasHeight,
            float cornerRadius,
            Color glowColor,
            Matrix4 projectionMatrix,
            Matrix4 modelViewMatrix)
        {
            if (_glowShader == null) return;

            // Активация шейдера свечения
            GL.UseProgram(_glowShader.ProgramId);

            // Установка параметров шейдера
            int locationProjection = GL.GetUniformLocation(_glowShader.ProgramId, "uProjection");
            int locationModelView = GL.GetUniformLocation(_glowShader.ProgramId, "uModelView");
            int locationColor = GL.GetUniformLocation(_glowShader.ProgramId, "uColor");
            int locationBlurRadius = GL.GetUniformLocation(_glowShader.ProgramId, "uBlurRadius");

            GL.UniformMatrix4(locationProjection, false, ref projectionMatrix);
            GL.UniformMatrix4(locationModelView, false, ref modelViewMatrix);
            GL.Uniform4(locationColor, glowColor.R / 255f, glowColor.G / 255f, glowColor.B / 255f, glowColor.A / 255f);
            GL.Uniform1(locationBlurRadius, 5.0f);  // Размер свечения

            // Расширение для эффекта свечения
            float glowExtraSize = 5.0f;

            // Создание вершин свечения (увеличенного прямоугольника)
            float[] vertices = {
                x - glowExtraSize, canvasHeight - barHeight - glowExtraSize, 0.0f,  // Верхний левый
                x + barWidth + glowExtraSize, canvasHeight - barHeight - glowExtraSize, 0.0f,  // Верхний правый
                x + barWidth + glowExtraSize, canvasHeight + glowExtraSize, 0.0f,  // Нижний правый
                x - glowExtraSize, canvasHeight + glowExtraSize, 0.0f   // Нижний левый
            };

            // Индексы для двух треугольников
            int[] indices = {
                0, 1, 2,  // Первый треугольник
                0, 2, 3   // Второй треугольник
            };

            // Загрузка вершин и индексов в буферы
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBufferObject);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StreamDraw);

            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _indexBufferObject);
            GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(int), indices, BufferUsageHint.StreamDraw);

            // Настройка формата вершин
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);

            // Рендеринг
            GL.DrawElements(PrimitiveType.Triangles, indices.Length, DrawElementsType.UnsignedInt, 0);

            // Отключение атрибутов
            GL.DisableVertexAttribArray(0);
        }
        #endregion

        #region Spectrum Processing
        private static float[] ScaleSpectrum(float[] spectrum, int barCount, int spectrumLength)
        {
            float[] scaledSpectrum = new float[barCount];
            float blockSize = (float)spectrumLength / barCount;

            for (int i = 0; i < barCount; i++)
            {
                float sum = 0;
                int start = (int)(i * blockSize);
                int end = (int)((i + 1) * blockSize);
                end = Math.Min(end, spectrumLength);

                for (int j = start; j < end; j++)
                {
                    sum += spectrum[j];
                }

                scaledSpectrum[i] = sum / (end - start);
            }

            return scaledSpectrum;
        }

        private float[] SmoothSpectrum(float[] scaledSpectrum, int actualBarCount)
        {
            if (_previousSpectrum == null || _previousSpectrum.Length != actualBarCount)
            {
                _previousSpectrum = new float[actualBarCount];
            }

            float[] smoothedSpectrum = new float[actualBarCount];

            for (int i = 0; i < actualBarCount; i++)
            {
                smoothedSpectrum[i] = _previousSpectrum[i] * (1 - _smoothingFactor) +
                                      scaledSpectrum[i] * _smoothingFactor;
                _previousSpectrum[i] = smoothedSpectrum[i];
            }

            return smoothedSpectrum;
        }
        #endregion

        #region Disposal
        /// <inheritdoc />
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _spectrumSemaphore?.Dispose();

                    // Освобождение ресурсов OpenGL
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

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }

    #endregion
}