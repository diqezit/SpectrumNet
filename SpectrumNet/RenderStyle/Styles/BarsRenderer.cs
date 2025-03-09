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

        // OpenGL resources
        private int _vertexArrayObject;
        private int _vertexBufferObject;
        private int _indexBufferObject;
        private ShaderProgram? _barShader;
        private ShaderProgram? _highlightShader;
        private ShaderProgram? _glowShader;

        // Rendering constants
        private const float MaxCornerRadius = 10f;
        private const float HighlightWidthProportion = 0.6f;
        private const float HighlightHeightProportion = 0.1f;
        private const float MaxHighlightHeight = 5f;
        private const float AlphaMultiplier = 1.5f;
        private const float HighlightAlphaDivisor = 3f;
        private const float DefaultCornerRadiusFactor = 5.0f;
        private const float GlowEffectAlpha = 0.25f;
        private const float MinBarHeight = 1f;

        // Spectrum processing
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
            //// Простые шейдеры из SpectrumBrushes

            string vertexShader = SpectrumBrushes.DefaultVertexShader;
            string fragmentShader = SpectrumBrushes.DefaultFragmentShader;
            string glowFragmentShader = SpectrumBrushes.GlowFragmentShader;

            try
            {
                // Компилируем шейдеры
                _barShader = new ShaderProgram(vertexShader, fragmentShader);
                _highlightShader = new ShaderProgram(vertexShader, fragmentShader);
                _glowShader = new ShaderProgram(vertexShader, glowFragmentShader);

            }
            catch (Exception ex)
            {
                Log.Error($"Error initializing shaders: {ex.Message}");
            }
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

            if (shader != null && _barShader != null)
                _barShader.Color = shader.Color;
            float[] renderSpectrum = ProcessSpectrum(spectrum!, Math.Min(spectrum!.Length, barCount));

            // Set up matrices and blending
            Matrix4 projectionMatrix = Matrix4.CreateOrthographicOffCenter(0, viewport.Width, viewport.Height, 0, -1, 1);
            Matrix4 modelViewMatrix = Matrix4.Identity;
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            RenderBars(renderSpectrum, viewport, barWidth, barSpacing, _barShader ?? shader!, projectionMatrix, modelViewMatrix);

            drawPerformanceInfo?.Invoke(viewport);
        }

        private bool ValidateRenderParameters(float[]? spectrum, Viewport viewport, ShaderProgram? shader)
        {
            if (!_isInitialized)
            {
                Log.Error("BarsRenderer not initialized before rendering");
                return false;
            }
            if (spectrum == null || spectrum.Length < 2 || (shader == null && _barShader == null) || viewport.Width <= 0 || viewport.Height <= 0)
            {
                Log.Error("Invalid render parameters for BarsRenderer");
                return false;
            }
            return true;
        }

        private float[] ProcessSpectrum(float[] spectrum, int targetCount)
        {
            bool semaphoreAcquired = false;
            try
            {
                semaphoreAcquired = _spectrumSemaphore.Wait(0);
                if (semaphoreAcquired)
                {
                    float[] scaledSpectrum = ScaleSpectrum(spectrum, targetCount, spectrum.Length);
                    _processedSpectrum = SmoothSpectrum(scaledSpectrum, targetCount);
                }
                return _processedSpectrum ?? ProcessSynchronously(spectrum, targetCount);
            }
            catch (Exception ex)
            {
                Log.Error($"Error processing spectrum: {ex.Message}");
                return new float[targetCount];
            }
            finally
            {
                if (semaphoreAcquired)
                    _spectrumSemaphore.Release();
            }
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
            float cornerRadius = MathF.Min(barWidth * DefaultCornerRadiusFactor, MaxCornerRadius);

            GL.BindVertexArray(_vertexArrayObject);

            for (int i = 0; i < spectrum.Length; i++)
            {
                float magnitude = spectrum[i];
                float barHeight = MathF.Max(magnitude * canvasHeight, MinBarHeight);
                float x = i * totalBarWidth;

                byte alpha = (byte)MathF.Min(magnitude * AlphaMultiplier * 255f, 255f);
                Color4 baseColor = baseShader.Color;
                Color4 barColor = new Color4(baseColor.R, baseColor.G, baseColor.B, alpha / 255f);

                if (_useGlowEffect && _glowShader != null && magnitude > 0.6f)
                {
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

            _barShader.Color = barColor;
            _barShader.Use();
            _barShader.SetUniform("projection", projectionMatrix);
            _barShader.SetUniform("modelview", modelViewMatrix);

            float[] vertices = {
                x, canvasHeight - barHeight, 0.0f,               // Top-left
                x + barWidth, canvasHeight - barHeight, 0.0f,    // Top-right
                x + barWidth, canvasHeight, 0.0f,                // Bottom-right
                x, canvasHeight, 0.0f                            // Bottom-left
            };
            int[] indices = { 0, 1, 2, 0, 2, 3 }; // Two triangles forming a quad

            DrawGeometry(vertices, indices);
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

            _highlightShader.Color = highlightColor;
            _highlightShader.Use();

            _highlightShader.SetUniform("projection", projectionMatrix);
            _highlightShader.SetUniform("modelview", modelViewMatrix);

            float highlightX = x + (barWidth - highlightWidth) / 2;
            float[] vertices = {
                highlightX, canvasHeight - barHeight, 0.0f,
                highlightX + highlightWidth, canvasHeight - barHeight, 0.0f,
                highlightX + highlightWidth, canvasHeight - barHeight + highlightHeight, 0.0f,
                highlightX, canvasHeight - barHeight + highlightHeight, 0.0f
            };
            int[] indices = { 0, 1, 2, 0, 2, 3 };

            DrawGeometry(vertices, indices);
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

            _glowShader.Color = glowColor;
            _glowShader.Use();

            _glowShader.SetUniform("projection", projectionMatrix);
            _glowShader.SetUniform("modelview", modelViewMatrix);
            _glowShader.SetUniform("uBlurRadius", 5.0f);

            float glowExtraSize = 5.0f;
            float[] vertices = {
                x - glowExtraSize, canvasHeight - barHeight - glowExtraSize, 0.0f,
                x + barWidth + glowExtraSize, canvasHeight - barHeight - glowExtraSize, 0.0f,
                x + barWidth + glowExtraSize, canvasHeight + glowExtraSize, 0.0f,
                x - glowExtraSize, canvasHeight + glowExtraSize, 0.0f
            };
            int[] indices = { 0, 1, 2, 0, 2, 3 };

            DrawGeometry(vertices, indices);
        }

        private void DrawGeometry(float[] vertices, int[] indices)
        {
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