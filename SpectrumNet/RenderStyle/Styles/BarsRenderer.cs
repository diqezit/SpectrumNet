#nullable enable
namespace SpectrumNet
{
    public sealed class Bars3DRenderer : BaseSpectrumRenderer
    {
        private static Bars3DRenderer? _instance;
        private readonly SemaphoreSlim _spectrumSemaphore = new(1, 1);

        #region Configurable Constants

        private record BarSettings
        {
            public const float MinHeight = 1f;
            public const float AlphaMultiplier = 1.5f;
            public const float HighlightAlphaDivisor = 3f;
            public const float DefaultCornerRadiusFactor = 5.0f;
            public const float MaxCornerRadius = 10f;
            public const float HighlightWidthProportion = 0.6f;
            public const float HighlightHeightProportion = 0.1f;
            public const float MaxHighlightHeight = 5f;
        }

        private record SceneSettings
        {
            public const float DefaultDepth = 15f;
            public const float DefaultFovDegrees = 45f;
            public const float DefaultPerspectiveAngle = 30f;
            public const float DefaultRotationSpeed = 0.2f;
        }

        private record LightingSettings
        {
            public const float DefaultRadius = 150f;
            public const float DefaultBaseY = -200f;
            public const float DefaultBaseZ = 150f;
            public const float DefaultOscillationDivisor = 3f;
            public const float HighlightDepthStartFactor = 0.25f;
            public const float HighlightDepthEndFactor = 0.75f;
        }

        private record ViewSettings
        {
            public const float DefaultBaseY = -300f;
            public const float DefaultBaseZ = 400f;
            public const float DefaultOscillationAmplitude = 50f;
            public const float DefaultOscillationFrequency = 0.5f;
        }

        private record GlowSettings
        {
            public const float EffectAlpha = 0.25f;
            public const float ExtraSize = 5.0f;
            public const float BlurRadius = 5.0f;
        }

        private float _lightRadius = LightingSettings.DefaultRadius;
        private float _lightBaseY = LightingSettings.DefaultBaseY;
        private float _lightBaseZ = LightingSettings.DefaultBaseZ;
        private float _lightOscillationDivisor = LightingSettings.DefaultOscillationDivisor;

        private float _viewBaseY = ViewSettings.DefaultBaseY;
        private float _viewBaseZ = ViewSettings.DefaultBaseZ;
        private float _viewOscillationAmplitude = ViewSettings.DefaultOscillationAmplitude;
        private float _viewOscillationFrequency = ViewSettings.DefaultOscillationFrequency;

        private float _rotationAngle = 0f;
        private bool _autoRotate = false;
        private float _rotationSpeed = SceneSettings.DefaultRotationSpeed;
        private float _depth = SceneSettings.DefaultDepth;

        #endregion

        #region Shader Management

        private record ShaderCollection
        {
            public ShaderProgram? Bar { get; set; }
            public ShaderProgram? Top { get; set; }
            public ShaderProgram? Side { get; set; }
            public ShaderProgram? Highlight { get; set; }
            public ShaderProgram? Glow { get; set; }
            public ShaderProgram? Bloom { get; set; }
            public ShaderProgram? ShadowMap { get; set; }
            public ShaderProgram? Scene { get; set; }
        }

        private ShaderCollection _shaders = new();

        #endregion

        #region Spectrum Processing

        private record SpectrumProcessingSettings
        {
            public float[]? PreviousSpectrum { get; set; }
            public float[]? ProcessedSpectrum { get; set; }
            public float SmoothingFactor { get; set; } = 0.3f;
        }

        private SpectrumProcessingSettings _spectrumSettings = new();

        #endregion

        private Bars3DRenderer() : base("Bars3DRenderer") { }

        public static Bars3DRenderer GetInstance() => _instance ??= new Bars3DRenderer();

        #region Spectrum Utility Methods (Specialized for Bars)

        private float[] ScaleSpectrumForBars(float[]? spectrum, int targetCount, int? sourceLength = null)
        {
            if (spectrum == null || spectrum.Length == 0)
                return new float[targetCount];

            int actualSourceLength = sourceLength ?? spectrum.Length;

            return SmartLogger.Safe(() => {
                float[] scaledSpectrum = new float[targetCount];
                float blockSize = (float)actualSourceLength / targetCount;

                for (int i = 0; i < targetCount; i++)
                {
                    float sum = 0;
                    int start = (int)(i * blockSize);
                    int end = Math.Min((int)((i + 1) * blockSize), actualSourceLength);

                    for (int j = start; j < end; j++)
                    {
                        if (j >= 0 && j < spectrum.Length)
                            sum += spectrum[j];
                    }

                    // Для 3D баров увеличиваем чувствительность к низким значениям
                    float value = (end > start) ? sum / (end - start) : 0;
                    scaledSpectrum[i] = value * (1.0f + (targetCount - i) / (float)targetCount);
                }

                return scaledSpectrum;
            }, new float[targetCount], LogPrefix, "Error scaling spectrum for 3D bars");
        }

        private float GetSpectrumValueForBars(float[]? spectrum, int index, float defaultValue = 0f)
        {
            if (spectrum == null || spectrum.Length == 0 || index < 0 || index >= spectrum.Length)
                return defaultValue;

            // Для 3D баров можем немного увеличить низкие значения, чтобы они были лучше видны
            return Math.Max(spectrum[index], defaultValue) * 1.2f;
        }

        #endregion

        #region Initialization and Configuration

        protected override void InitializeShaders()
        {
            var shaderSource = GetShaderSources();

            _shaders.Bar = SmartLogger.Safe(() => new ShaderProgram(shaderSource.Vertex3D, shaderSource.Fragment3D), null);
            _shaders.Top = SmartLogger.Safe(() => new ShaderProgram(shaderSource.Vertex3D, shaderSource.Fragment3D), null);
            _shaders.Side = SmartLogger.Safe(() => new ShaderProgram(shaderSource.Vertex3D, shaderSource.Fragment3D), null);
            _shaders.Highlight = SmartLogger.Safe(() => new ShaderProgram(shaderSource.Vertex, shaderSource.Fragment), null);
            _shaders.Glow = SmartLogger.Safe(() => new ShaderProgram(shaderSource.Vertex, shaderSource.GlowFragment), null);
            _shaders.Bloom = SmartLogger.Safe(() => new ShaderProgram(shaderSource.PostProcessVertex, shaderSource.BloomFragment), null);
            _shaders.ShadowMap = SmartLogger.Safe(() => new ShaderProgram(shaderSource.ShadowMapVertex, shaderSource.ShadowMapFragment), null);
            _shaders.Scene = SmartLogger.Safe(() => new ShaderProgram(Shaders.vertexSceneShader, Shaders.fragmentSceneShader), null);

            if (_shaders.Scene != null)
            {
                SmartLogger.Safe(() =>
                {
                    _shaders.Scene.Use();
                    GL.GetUniformLocation(_shaders.Scene.ProgramId, "projection");
                    GL.GetUniformLocation(_shaders.Scene.ProgramId, "modelview");
                }, LogPrefix, "Failed to get uniform locations");
            }
        }

        private (
            string Vertex3D, string Fragment3D, string Vertex, string Fragment,
            string GlowFragment, string PostProcessVertex, string BloomFragment,
            string ShadowMapVertex, string ShadowMapFragment,
            string VertexScene, string FragmentScene
        ) GetShaderSources() => (
            Shaders.vertex3DShader,
            Shaders.fragment3DShader,
            Shaders.vertexShader,
            Shaders.fragmentShader,
            Shaders.glowFragmentShader,
            Shaders.postProcessVertexShader,
            Shaders.bloomFragmentShader,
            Shaders.shadowMapVertexShader,
            Shaders.shadowMapFragmentShader,
            Shaders.vertexSceneShader,
            Shaders.fragmentSceneShader
        );

        public override void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium)
        {
            _spectrumSettings.SmoothingFactor = isOverlayActive ? 0.5f : 0.3f;
            Quality = quality;
        }

        #endregion

        #region Rendering Methods

        public override void Render(
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

            ApplyShaderColor(shader);
            float[] renderSpectrum = ProcessSpectrum(spectrum!, Math.Min(spectrum!.Length, barCount));
            SetupOpenGLForRendering();
            UpdateRotation();
            var matrices = CalculateRenderMatrices(viewport, SceneSettings.DefaultFovDegrees);
            Vector3 lightPos = CalculateLightPosition(viewport);
            Vector3 viewPos = CalculateViewPosition(viewport);

            if (_sceneGeometry != null && _shaders.Scene != null)
            {
                SmartLogger.Safe(() => _sceneGeometry.Render(_shaders.Scene, matrices.Projection, matrices.ModelView, lightPos, viewPos),
                    LogPrefix, "Failed to render scene geometry");
            }

            EnableTransparency();
            SmartLogger.Safe(() => RenderBars3D(renderSpectrum, viewport, barWidth, barSpacing,
                        _shaders.Bar ?? shader!, matrices.Projection, matrices.ModelView, lightPos, viewPos),
                        LogPrefix, "Failed to render 3D bars");
            DisableSpecialEffects();

            if (_quality != RenderQuality.Low)
            {
                if (_shaders.ShadowMap != null)
                    SmartLogger.Safe(() => RenderShadowMap(matrices.Projection, matrices.ModelView),
                        LogPrefix, "Failed to render shadow map");

                if (_shaders.Bloom != null)
                    SmartLogger.Safe(() => ApplyBloomEffect(viewport),
                        LogPrefix, "Failed to apply bloom effect");
            }

            SmartLogger.Safe(() => drawPerformanceInfo?.Invoke(viewport),
                LogPrefix, "Failed to draw performance info");
        }

        private void ApplyShaderColor(ShaderProgram? shader)
        {
            if (shader != null && _shaders.Bar != null)
                _shaders.Bar.Color = shader.Color;
        }

        private void UpdateRotation()
        {
            if (_autoRotate)
            {
                _rotationAngle += _rotationSpeed;
                if (_rotationAngle >= 360f)
                    _rotationAngle -= 360f;
            }
        }

        private Vector3 CalculateLightPosition(Viewport viewport)
        {
            float lightRadius = _lightRadius;
            return new Vector3(
                viewport.Width / 2 + MathF.Cos(_rotationAngle + CameraRotationOffset.X * 0.5f) * lightRadius,
                _lightBaseY + MathF.Sin(_rotationAngle) * (lightRadius / _lightOscillationDivisor),
                _lightBaseZ);
        }

        private Vector3 CalculateViewPosition(Viewport viewport)
        {
            return new Vector3(
                viewport.Width / 2 + CameraPositionOffset.X * 0.25f,
                _viewBaseY,
                _viewBaseZ + _viewOscillationAmplitude * MathF.Sin(_rotationAngle * _viewOscillationFrequency)
                         + CameraPositionOffset.Z * 0.25f);
        }

        #endregion

        #region Spectrum Processing

        private float[] ProcessSpectrum(float[] spectrum, int targetCount)
        {
            bool semaphoreAcquired = false;
            try
            {
                semaphoreAcquired = _spectrumSemaphore.Wait(0);
                if (semaphoreAcquired)
                {
                    float[] scaledSpectrum = ScaleSpectrumForBars(spectrum, targetCount);
                    _spectrumSettings.ProcessedSpectrum = SmoothSpectrum(scaledSpectrum, targetCount);
                }
                return _spectrumSettings.ProcessedSpectrum ?? ProcessSynchronously(spectrum, targetCount);
            }
            finally
            {
                if (semaphoreAcquired)
                    _spectrumSemaphore.Release();
            }
        }

        private float[] ProcessSynchronously(float[] spectrum, int targetCount)
        {
            var scaledSpectrum = ScaleSpectrumForBars(spectrum, targetCount);
            return SmoothSpectrum(scaledSpectrum, targetCount);
        }

        private float[] SmoothSpectrum(float[] scaledSpectrum, int actualBarCount)
        {
            return SmartLogger.Safe(() =>
            {
                if (_spectrumSettings.PreviousSpectrum == null || _spectrumSettings.PreviousSpectrum.Length != actualBarCount)
                    _spectrumSettings.PreviousSpectrum = new float[actualBarCount];

                float[] smoothedSpectrum = new float[actualBarCount];
                float smoothingFactor = _spectrumSettings.SmoothingFactor;

                for (int i = 0; i < actualBarCount; i++)
                {
                    smoothedSpectrum[i] = _spectrumSettings.PreviousSpectrum[i] * (1 - smoothingFactor) +
                                        scaledSpectrum[i] * smoothingFactor;
                    _spectrumSettings.PreviousSpectrum[i] = smoothedSpectrum[i];
                }

                return smoothedSpectrum;
            }, scaledSpectrum);
        }

        #endregion

        #region Bar Rendering

        private void RenderBars3D(
            float[] spectrum,
            Viewport viewport,
            float barWidth,
            float barSpacing,
            ShaderProgram baseShader,
            Matrix4 projectionMatrix,
            Matrix4 modelViewMatrix,
            Vector3 lightPos,
            Vector3 viewPos)
        {
            float totalBarWidth = barWidth + barSpacing;
            float canvasHeight = viewport.Height;
            float cornerRadius = MathF.Min(barWidth * BarSettings.DefaultCornerRadiusFactor, BarSettings.MaxCornerRadius);
            float baseY = 0;

            float distanceFactor = Math.Max(1.0f, Vector3.Distance(Vector3.Zero, CameraPositionOffset) / 100.0f);

            GL.BindVertexArray(_glResources.VertexArrayObject);

            for (int i = 0; i < spectrum.Length; i++)
            {
                float magnitude = GetSpectrumValueForBars(spectrum, i);
                float barHeight = MathF.Max(magnitude * canvasHeight, BarSettings.MinHeight);
                float x = i * totalBarWidth;

                var colors = CalculateBarColors(baseShader.Color, magnitude, distanceFactor);

                SmartLogger.Safe(() => RenderBar3D(x, barWidth, barHeight, baseY, _depth, colors.Bar, colors.Top, colors.Side,
                            lightPos, viewPos, projectionMatrix, modelViewMatrix),
                            LogPrefix, $"Failed to render bar {i}");

                if (ShouldRenderGlow(magnitude))
                {
                    Color4 glowColor = new(baseShader.Color.R, baseShader.Color.G, baseShader.Color.B,
                                            magnitude * GlowSettings.EffectAlpha);
                    SmartLogger.Safe(() => RenderGlowEffect(x, barWidth, barHeight, baseY, cornerRadius, glowColor,
                                                projectionMatrix, modelViewMatrix),
                                                LogPrefix, $"Failed to render glow for bar {i}");
                }

                if (ShouldRenderHighlight(barHeight, cornerRadius))
                {
                    float highlightWidth = barWidth * BarSettings.HighlightWidthProportion;
                    float highlightHeight = MathF.Min(barHeight * BarSettings.HighlightHeightProportion,
                                                    BarSettings.MaxHighlightHeight);
                    byte highlightAlpha = (byte)(colors.Alpha / BarSettings.HighlightAlphaDivisor);
                    Color4 highlightColor = new(Color4.White.R, Color4.White.G, Color4.White.B, highlightAlpha / 255f);
                    SmartLogger.Safe(() => RenderHighlight3D(x, barWidth, barHeight, baseY, _depth, highlightWidth,
                                     highlightHeight, highlightColor, projectionMatrix, modelViewMatrix),
                                     LogPrefix, $"Failed to render highlight for bar {i}");
                }
            }

            GL.BindVertexArray(0);
        }

        private (Color4 Bar, Color4 Top, Color4 Side, byte Alpha) CalculateBarColors(
            Color4 baseColor, float magnitude, float distanceFactor)
        {
            byte alpha = (byte)MathF.Min(magnitude * BarSettings.AlphaMultiplier * 255f / distanceFactor, 255f);
            Color4 barColor = new(baseColor.R, baseColor.G, baseColor.B, alpha / 255f);
            Color4 topColor = new(baseColor.R * 1.2f, baseColor.G * 1.2f, baseColor.B * 1.2f, alpha / 255f);
            Color4 sideColor = new(baseColor.R * 0.8f, baseColor.G * 0.8f, baseColor.B * 0.8f, alpha / 255f);

            return (barColor, topColor, sideColor, alpha);
        }

        private bool ShouldRenderGlow(float magnitude) =>
            _useGlowEffect && _shaders.Glow != null && magnitude > 0.6f;

        private bool ShouldRenderHighlight(float barHeight, float cornerRadius) =>
            barHeight > cornerRadius * 2 && _quality != RenderQuality.Low;

        private void RenderBar3D(
            float x,
            float barWidth,
            float barHeight,
            float baseY,
            float depth,
            Color4 frontColor,
            Color4 topColor,
            Color4 sideColor,
            Vector3 lightPos,
            Vector3 viewPos,
            Matrix4 projectionMatrix,
            Matrix4 modelViewMatrix)
        {
            if (_shaders.Bar == null || _shaders.Top == null || _shaders.Side == null) return;

            var geometryData = CreateBarGeometryData(x, baseY, 0, barWidth, barHeight, depth);
            int[] indices = { 0, 1, 2, 0, 2, 3 };

            RenderBarFace(_shaders.Bar, frontColor, geometryData.Front, indices,
                          lightPos, viewPos, projectionMatrix, modelViewMatrix);
            RenderBarFace(_shaders.Side, sideColor, geometryData.Back, indices,
                          lightPos, viewPos, projectionMatrix, modelViewMatrix);
            RenderBarFace(_shaders.Top, topColor, geometryData.Top, indices,
                          lightPos, viewPos, projectionMatrix, modelViewMatrix);
            RenderBarFace(_shaders.Side, sideColor, geometryData.Bottom, indices,
                          lightPos, viewPos, projectionMatrix, modelViewMatrix);
            RenderBarFace(_shaders.Side, sideColor, geometryData.Left, indices,
                          lightPos, viewPos, projectionMatrix, modelViewMatrix);
            RenderBarFace(_shaders.Side, sideColor, geometryData.Right, indices,
                          lightPos, viewPos, projectionMatrix, modelViewMatrix);
        }

        private record BarGeometryData(
            float[] Front, float[] Back, float[] Top,
            float[] Bottom, float[] Left, float[] Right);

        private BarGeometryData CreateBarGeometryData(
            float x, float y, float z, float barWidth, float barHeight, float depth)
        {
            float[] front = {
        x,            y,           z,   0.0f, 0.0f, 1.0f,
        x+barWidth,   y,           z,   0.0f, 0.0f, 1.0f,
        x+barWidth,   y+barHeight, z,   0.0f, 0.0f, 1.0f,
        x,            y+barHeight, z,   0.0f, 0.0f, 1.0f
    };

            float[] back = {
        x+barWidth,   y,           z-depth,  0.0f, 0.0f, -1.0f,
        x,            y,           z-depth,  0.0f, 0.0f, -1.0f,
        x,            y+barHeight, z-depth,  0.0f, 0.0f, -1.0f,
        x+barWidth,   y+barHeight, z-depth,  0.0f, 0.0f, -1.0f
    };

            float[] top = {
        x,            y+barHeight, z,         0.0f, 1.0f, 0.0f,
        x+barWidth,   y+barHeight, z,         0.0f, 1.0f, 0.0f,
        x+barWidth,   y+barHeight, z-depth,   0.0f, 1.0f, 0.0f,
        x,            y+barHeight, z-depth,   0.0f, 1.0f, 0.0f
    };

            float[] bottom = {
        x,            y,           z-depth,   0.0f, -1.0f, 0.0f,
        x+barWidth,   y,           z-depth,   0.0f, -1.0f, 0.0f,
        x+barWidth,   y,           z,         0.0f, -1.0f, 0.0f,
        x,            y,           z,         0.0f, -1.0f, 0.0f
    };

            float[] left = {
        x,            y,           z-depth,   -1.0f, 0.0f, 0.0f,
        x,            y,           z,         -1.0f, 0.0f, 0.0f,
        x,            y+barHeight, z,         -1.0f, 0.0f, 0.0f,
        x,            y+barHeight, z-depth,   -1.0f, 0.0f, 0.0f
    };

            float[] right = {
        x+barWidth,   y,           z,         1.0f, 0.0f, 0.0f,
        x+barWidth,   y,           z-depth,   1.0f, 0.0f, 0.0f,
        x+barWidth,   y+barHeight, z-depth,   1.0f, 0.0f, 0.0f,
        x+barWidth,   y+barHeight, z,         1.0f, 0.0f, 0.0f
    };

            return new BarGeometryData(front, back, top, bottom, left, right);
        }

        private void RenderBarFace(
            ShaderProgram shader,
            Color4 color,
            float[] vertices,
            int[] indices,
            Vector3 lightPos,
            Vector3 viewPos,
            Matrix4 projectionMatrix,
            Matrix4 modelViewMatrix)
        {
            shader.Color = color;
            shader.Use();

            shader.SetUniform("projection", projectionMatrix);
            shader.SetUniform("modelview", modelViewMatrix);
            shader.SetUniform("viewPos", viewPos);

            shader.SetUniform("lights[0].position", lightPos);
            shader.SetUniform("lights[0].color", new Vector3(1.0f, 1.0f, 1.0f));
            shader.SetUniform("lights[0].intensity", 1.0f);
            shader.SetUniform("lights[0].attenuation", 0.01f);
            shader.SetUniform("numLights", 1);

            shader.SetUniform("material.ambient", new Vector3(0.2f, 0.2f, 0.2f));
            shader.SetUniform("material.diffuse", new Vector3(0.8f, 0.8f, 0.8f));
            shader.SetUniform("material.specular", new Vector3(0.5f, 0.5f, 0.5f));
            shader.SetUniform("material.shininess", 32.0f);
            shader.SetUniform("material.opacity", 1.0f);

            shader.SetUniform("useTexture", 0);

            DrawGeometry3D(vertices, indices);
        }

        private void RenderHighlight3D(
            float x,
            float barWidth,
            float barHeight,
            float baseY,
            float depth,
            float highlightWidth,
            float highlightHeight,
            Color4 highlightColor,
            Matrix4 projectionMatrix,
            Matrix4 modelViewMatrix)
        {
            if (_shaders.Highlight == null) return;

            _shaders.Highlight.Color = highlightColor;
            _shaders.Highlight.Use();
            _shaders.Highlight.SetUniform("projection", projectionMatrix);
            _shaders.Highlight.SetUniform("modelview", modelViewMatrix);

            float highlightX = x + (barWidth - highlightWidth) / 2;
            float y = baseY + barHeight;

            float[] vertices = {
        highlightX,               y, -depth * LightingSettings.HighlightDepthStartFactor,
        highlightX+highlightWidth, y, -depth * LightingSettings.HighlightDepthStartFactor,
        highlightX+highlightWidth, y, -depth * LightingSettings.HighlightDepthEndFactor,
        highlightX,               y, -depth * LightingSettings.HighlightDepthEndFactor
    };
            int[] indices = { 0, 1, 2, 0, 2, 3 };

            DrawSimpleGeometry(vertices, indices);
        }

        private void RenderGlowEffect(
            float x,
            float barWidth,
            float barHeight,
            float baseY,
            float cornerRadius,
            Color4 glowColor,
            Matrix4 projectionMatrix,
            Matrix4 modelViewMatrix)
        {
            if (_shaders.Glow == null) return;

            _shaders.Glow.Color = glowColor;
            _shaders.Glow.Use();
            _shaders.Glow.SetUniform("projection", projectionMatrix);
            _shaders.Glow.SetUniform("modelview", modelViewMatrix);
            _shaders.Glow.SetUniform("uBlurRadius", GlowSettings.BlurRadius);

            float[] vertices = {
        x - GlowSettings.ExtraSize,              baseY - GlowSettings.ExtraSize,                1.0f,
        x + barWidth + GlowSettings.ExtraSize,   baseY - GlowSettings.ExtraSize,                1.0f,
        x + barWidth + GlowSettings.ExtraSize,   baseY + barHeight + GlowSettings.ExtraSize,    1.0f,
        x - GlowSettings.ExtraSize,              baseY + barHeight + GlowSettings.ExtraSize,    1.0f
    };
            int[] indices = { 0, 1, 2, 0, 2, 3 };

            DrawSimpleGeometry(vertices, indices);
        }

        private void DrawGeometry3D(float[] vertices, int[] indices)
        {
            SmartLogger.Safe(() =>
            {
            GL.BindBuffer(BufferTarget.ArrayBuffer, _glResources.VertexBufferObject);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StreamDraw);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _glResources.IndexBufferObject);
            GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(int), indices, BufferUsageHint.StreamDraw);

            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), 0);

            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), 3 * sizeof(float));

                GL.DrawElements(PrimitiveType.Triangles, indices.Length, DrawElementsType.UnsignedInt, 0);

                GL.DisableVertexAttribArray(0);
                GL.DisableVertexAttribArray(1);
            }, LogPrefix, "Failed to draw 3D geometry");
        }

        #endregion

        #region Post-Processing Effects

        private void RenderShadowMap(Matrix4 projectionMatrix, Matrix4 modelViewMatrix)
        {
            if (_shaders.ShadowMap == null) return;

            SmartLogger.Safe(() => {
                _shaders.ShadowMap.Use();
                _shaders.ShadowMap.SetUniform("projection", projectionMatrix);
                _shaders.ShadowMap.SetUniform("modelview", modelViewMatrix);

                float[] vertices = {
                    -1f, -1f, 0f,
                    1f, -1f, 0f,
                    1f,  1f, 0f,
                    -1f,  1f, 0f
                };
                int[] indices = { 0, 1, 2, 0, 2, 3 };

                DrawSimpleGeometry(vertices, indices);
            }, LogPrefix, "Failed to render shadow map");
        }

        private void ApplyBloomEffect(Viewport viewport)
        {
            if (_shaders.Bloom == null) return;

            SmartLogger.Safe(() => {
                _shaders.Bloom.Use();
                _shaders.Bloom.SetUniform("uBlurRadius", GlowSettings.BlurRadius);

                float[] vertices = {
                    -1f, -1f, 0f,
                    1f, -1f, 0f,
                    1f,  1f, 0f,
                    -1f,  1f, 0f
                };
                int[] indices = { 0, 1, 2, 0, 2, 3 };

                DrawSimpleGeometry(vertices, indices);
            }, LogPrefix, "Failed to apply bloom effect");
        }

        #endregion

        #region Resource Disposal

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    SmartLogger.SafeDispose(_spectrumSemaphore, "Spectrum semaphore");
                    DisposeShaders();
                    base.Dispose(disposing);
                }
                _disposed = true;
            }
        }

        private void DisposeShaders()
        {
            SmartLogger.SafeDispose(_shaders.Bar, "Bar shader");
            _shaders.Bar = null;

            SmartLogger.SafeDispose(_shaders.Top, "Top shader");
            _shaders.Top = null;

            SmartLogger.SafeDispose(_shaders.Side, "Side shader");
            _shaders.Side = null;

            SmartLogger.SafeDispose(_shaders.Highlight, "Highlight shader");
            _shaders.Highlight = null;

            SmartLogger.SafeDispose(_shaders.Glow, "Glow shader");
            _shaders.Glow = null;

            SmartLogger.SafeDispose(_shaders.Bloom, "Bloom shader");
            _shaders.Bloom = null;

            SmartLogger.SafeDispose(_shaders.ShadowMap, "ShadowMap shader");
            _shaders.ShadowMap = null;

            SmartLogger.SafeDispose(_shaders.Scene, "Scene shader");
            _shaders.Scene = null;
        }

        #endregion
    }
}