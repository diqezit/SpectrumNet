#nullable enable
namespace SpectrumNet
{
    public sealed class RainParticleRenderer : BaseSpectrumRenderer
    {
        private static RainParticleRenderer? _instance;
        private readonly SemaphoreSlim _particleSemaphore = new(1, 1);

        #region Configurable Constants

        private record ParticleSettings
        {
            public const float MinSize = 2f;
            public const float MaxSize = 10f;
            public const float FallSpeed = 5f;
            public const float AlphaMultiplier = 1.2f;
            public const float WindInfluence = 0.3f;
            public const float SplashSizeMultiplier = 1.5f;
        }

        private record SceneSettings
        {
            public const float DefaultDepth = 20f;
            public const float DefaultFovDegrees = 60f;
            public const float DefaultRainHeight = 100f;
            public const float DefaultParticleDensity = 0.05f;
        }

        private record LightingSettings
        {
            public const float DefaultRadius = 200f;
            public const float DefaultBaseY = -150f;
            public const float DefaultBaseZ = 100f;
            public const float DefaultOscillationDivisor = 2f;
        }

        private record ViewSettings
        {
            public const float DefaultBaseY = -200f;
            public const float DefaultBaseZ = 300f;
            public const float DefaultOscillationAmplitude = 30f;
            public const float DefaultOscillationFrequency = 0.3f;
        }

        private record GlowSettings
        {
            public const float EffectAlpha = 0.3f;
            public const float ExtraSize = 3.0f;
            public const float BlurRadius = 4.0f;
        }

        private float _lightRadius = LightingSettings.DefaultRadius;
        private float _lightBaseY = LightingSettings.DefaultBaseY;
        private float _lightBaseZ = LightingSettings.DefaultBaseZ;
        private float _lightOscillationDivisor = LightingSettings.DefaultOscillationDivisor;

        private float _viewBaseY = ViewSettings.DefaultBaseY;
        private float _viewBaseZ = ViewSettings.DefaultBaseZ;
        private float _viewOscillationAmplitude = ViewSettings.DefaultOscillationAmplitude;
        private float _viewOscillationFrequency = ViewSettings.DefaultOscillationFrequency;

        private float _depth = SceneSettings.DefaultDepth;
        private float _rainHeight = SceneSettings.DefaultRainHeight;

        #endregion

        #region Shader Management

        private record ShaderCollection
        {
            public ShaderProgram? Particle { get; set; }
            public ShaderProgram? Splash { get; set; }
            public ShaderProgram? Glow { get; set; }
            public ShaderProgram? Bloom { get; set; }
            public ShaderProgram? Scene { get; set; }
        }

        private ShaderCollection _shaders = new();

        #endregion

        #region Particle Management

        private record Particle
        {
            public Vector3 Position { get; set; }
            public float Size { get; set; }
            public float VelocityY { get; set; }
            public float Alpha { get; set; }
        }

        private List<Particle> _particles = new();

        #endregion

        private RainParticleRenderer() : base("RainParticleRenderer") { }

        public static RainParticleRenderer GetInstance() => _instance ??= new RainParticleRenderer();

        #region Spectrum Utility Methods (Specialized for Rain)

        private float[] ScaleSpectrumForRain(float[]? spectrum, int targetCount, int? sourceLength = null)
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

                    // Для частиц дождя делаем более плавную интерполяцию
                    float value = (end > start) ? sum / (end - start) : 0;

                    // Применяем сглаживание для более естественного эффекта дождя
                    if (i > 0)
                        value = (value + scaledSpectrum[i - 1]) * 0.5f;

                    scaledSpectrum[i] = value;
                }

                return scaledSpectrum;
            }, new float[targetCount], LogPrefix, "Error scaling spectrum for rain particles");
        }

        private float GetSpectrumValueForRain(float[]? spectrum, int index, float defaultValue = 0f)
        {
            if (spectrum == null || spectrum.Length == 0 || index < 0 || index >= spectrum.Length)
                return defaultValue;

            // Для частиц дождя добавляем небольшую случайную вариацию
            Random rand = new();
            float randomFactor = 0.9f + 0.2f * rand.NextSingle();
            return spectrum[index] * randomFactor;
        }

        private int CalculateSpectrumIndexForRain(float position, float maxPosition, int spectrumLength)
        {
            if (spectrumLength <= 0 || maxPosition <= 0)
                return 0;

            // Для частиц дождя можем использовать нелинейное отображение позиции в индекс
            // чтобы создать более интересный визуальный эффект
            float normalizedPosition = position / maxPosition;
            float adjustedPosition = normalizedPosition * normalizedPosition; // Квадратичное отображение

            int index = (int)(adjustedPosition * spectrumLength);
            return Math.Clamp(index, 0, spectrumLength - 1);
        }

        private bool IsValidSpectrumForRain(float[]? spectrum)
        {
            // Для частиц дождя мы можем быть более гибкими, поскольку они не так сильно
            // зависят от точности спектра
            return spectrum != null && spectrum.Length > 0;
        }

        #endregion

        #region Initialization and Configuration

        protected override void InitializeShaders()
        {
            var shaderSource = GetShaderSources();

            _shaders.Particle = SmartLogger.Safe(() => new ShaderProgram(shaderSource.Vertex3D, shaderSource.Fragment3D), null);
            _shaders.Splash = SmartLogger.Safe(() => new ShaderProgram(shaderSource.Vertex, shaderSource.Fragment), null);
            _shaders.Glow = SmartLogger.Safe(() => new ShaderProgram(shaderSource.Vertex, shaderSource.GlowFragment), null);
            _shaders.Bloom = SmartLogger.Safe(() => new ShaderProgram(shaderSource.PostProcessVertex, shaderSource.BloomFragment), null);
            _shaders.Scene = SmartLogger.Safe(() => new ShaderProgram(Shaders.vertexSceneShader, Shaders.fragmentSceneShader), null);

            if (_shaders.Scene != null)
            {
                SmartLogger.Safe(() => {
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
            Quality = quality;
        }

        #endregion

        #region Rendering Methods

        public override void Render(
            float[]? spectrum,
            Viewport viewport,
            float particleWidth,
            float particleSpacing,
            int particleCount,
            ShaderProgram? shader,
            Action<Viewport> drawPerformanceInfo)
        {
            if (!ValidateRenderParameters(spectrum, viewport, shader, requireSpectrum: false))
                return;

            ApplyShaderColor(shader);
            // Используем наш специализированный метод для обработки спектра
            float[] processedSpectrum = spectrum != null ? ScaleSpectrumForRain(spectrum, particleCount) : new float[0];

            SmartLogger.Safe(() => UpdateParticles(viewport, processedSpectrum, particleCount),
                LogPrefix, "Failed to update particles");

            SetupOpenGLForRendering();
            var matrices = CalculateRenderMatrices(viewport, SceneSettings.DefaultFovDegrees);
            Vector3 lightPos = CalculateLightPosition(viewport);
            Vector3 viewPos = CalculateViewPosition(viewport);

            if (_sceneGeometry != null && _shaders.Scene != null)
            {
                SmartLogger.Safe(() => _sceneGeometry.Render(_shaders.Scene, matrices.Projection, matrices.ModelView, lightPos, viewPos),
                    LogPrefix, "Failed to render scene geometry");
            }

            EnableTransparency();
            SmartLogger.Safe(() => RenderRainParticles(viewport, _shaders.Particle ?? shader!, matrices.Projection, matrices.ModelView, lightPos, viewPos),
                LogPrefix, "Failed to render rain particles");
            DisableSpecialEffects();

            if (_quality != RenderQuality.Low && _shaders.Bloom != null)
                SmartLogger.Safe(() => ApplyBloomEffect(viewport),
                    LogPrefix, "Failed to apply bloom effect");

            SmartLogger.Safe(() => drawPerformanceInfo?.Invoke(viewport),
                LogPrefix, "Failed to draw performance info");
        }

        private void ApplyShaderColor(ShaderProgram? shader)
        {
            if (shader != null && _shaders.Particle != null)
                _shaders.Particle.Color = shader.Color;
        }

        private Vector3 CalculateLightPosition(Viewport viewport)
        {
            float lightRadius = _lightRadius;
            return new Vector3(
                viewport.Width / 2 + MathF.Cos(CameraRotationOffset.X * 0.5f) * lightRadius,
                _lightBaseY + MathF.Sin(CameraRotationOffset.Y) * (lightRadius / _lightOscillationDivisor),
                _lightBaseZ);
        }

        private Vector3 CalculateViewPosition(Viewport viewport)
        {
            return new Vector3(
                viewport.Width / 2 + CameraPositionOffset.X * 0.25f,
                _viewBaseY,
                _viewBaseZ + _viewOscillationAmplitude * MathF.Sin(CameraRotationOffset.X * _viewOscillationFrequency)
                         + CameraPositionOffset.Z * 0.25f);
        }

        #endregion

        #region Particle Management

        private void UpdateParticles(Viewport viewport, float[]? spectrum, int targetCount)
        {
            bool semaphoreAcquired = false;
            try
            {
                semaphoreAcquired = _particleSemaphore.Wait(0);
                if (!semaphoreAcquired) return;

                SmartLogger.Safe(() => {
                    Random rand = new();
                    int particleCount = (int)(viewport.Width * viewport.Height * SceneSettings.DefaultParticleDensity);

                    // Initialize particles if none exist
                    if (_particles.Count == 0)
                    {
                        for (int i = 0; i < particleCount; i++)
                        {
                            _particles.Add(new Particle
                            {
                                Position = new Vector3(
                                    rand.NextSingle() * viewport.Width,
                                    _rainHeight + rand.NextSingle() * 100,
                                    rand.NextSingle() * _depth),
                                Size = ParticleSettings.MinSize + rand.NextSingle() * (ParticleSettings.MaxSize - ParticleSettings.MinSize),
                                VelocityY = -ParticleSettings.FallSpeed * (1 + rand.NextSingle()),
                                Alpha = 1.0f
                            });
                        }
                    }

                    // Update particle positions
                    for (int i = 0; i < _particles.Count; i++)
                    {
                        var particle = _particles[i];
                        var position = particle.Position;
                        position.Y += particle.VelocityY;
                        position.X += ParticleSettings.WindInfluence * MathF.Sin((float)DateTime.Now.Ticks / 10000000f);

                        if (position.Y < 0)
                        {
                            position = new Vector3(
                                rand.NextSingle() * viewport.Width,
                                _rainHeight + rand.NextSingle() * 100,
                                rand.NextSingle() * _depth);
                            particle.VelocityY = -ParticleSettings.FallSpeed * (1 + rand.NextSingle());
                        }

                        // Используем наши специализированные методы для работы со спектром
                        if (IsValidSpectrumForRain(spectrum))
                        {
                            // Вычисляем индекс спектра на основе позиции частицы
                            int spectrumIndex = CalculateSpectrumIndexForRain(position.X, viewport.Width, spectrum!.Length);

                            // Получаем безопасное значение из спектра с вариацией
                            float spectrumValue = GetSpectrumValueForRain(spectrum, spectrumIndex);

                            // Применяем значение спектра к скорости падения частицы
                            particle.VelocityY = -ParticleSettings.FallSpeed * (1 + spectrumValue);
                        }

                        particle.Position = position;
                    }
                }, LogPrefix, "Failed to update particle positions");
            }
            finally
            {
                if (semaphoreAcquired)
                    _particleSemaphore.Release();
            }
        }

        private void RenderRainParticles(
                    Viewport viewport,
                    ShaderProgram baseShader,
                    Matrix4 projectionMatrix,
                    Matrix4 modelViewMatrix,
                    Vector3 lightPos,
                    Vector3 viewPos)
        {
            if (_shaders.Particle == null) return;

            SmartLogger.Safe(() => {
                GL.BindVertexArray(_glResources.VertexArrayObject);
                GL.Enable(EnableCap.PointSprite);

                foreach (var particle in _particles)
                {
                    float alpha = particle.Alpha * ParticleSettings.AlphaMultiplier;
                    Color4 particleColor = new(baseShader.Color.R, baseShader.Color.G, baseShader.Color.B, alpha);

                    RenderParticle(particle, particleColor, projectionMatrix, modelViewMatrix, lightPos, viewPos);

                    // Render glow effect
                    if (_useGlowEffect && _shaders.Glow != null && particle.Alpha > 0.5f)
                    {
                        Color4 glowColor = new(baseShader.Color.R, baseShader.Color.G, baseShader.Color.B, GlowSettings.EffectAlpha);
                        RenderGlowEffect(particle.Position, particle.Size, glowColor, projectionMatrix, modelViewMatrix);
                    }

                    // Render splash when particle hits ground
                    if (particle.Position.Y <= 0 && _shaders.Splash != null)
                    {
                        Color4 splashColor = new(baseShader.Color.R, baseShader.Color.G, baseShader.Color.B, 0.5f);
                        RenderSplash(particle.Position, particle.Size * ParticleSettings.SplashSizeMultiplier, splashColor, projectionMatrix, modelViewMatrix);
                    }
                }

                GL.Disable(EnableCap.PointSprite);
                GL.BindVertexArray(0);
            }, LogPrefix, "Failed to render rain particles");
        }

        private void RenderParticle(
            Particle particle,
            Color4 color,
            Matrix4 projectionMatrix,
            Matrix4 modelViewMatrix,
            Vector3 lightPos,
            Vector3 viewPos)
        {
            SmartLogger.Safe(() => {
                _shaders.Particle!.Color = color;
                _shaders.Particle.Use();
                _shaders.Particle.SetUniform("projection", projectionMatrix);
                _shaders.Particle.SetUniform("modelview", modelViewMatrix);
                _shaders.Particle.SetUniform("lightPos", lightPos);
                _shaders.Particle.SetUniform("viewPos", viewPos);

                float[] vertices = { particle.Position.X, particle.Position.Y, particle.Position.Z };
                GL.PointSize(particle.Size);
                GL.BindBuffer(BufferTarget.ArrayBuffer, _glResources.VertexBufferObject);
                GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StreamDraw);
                GL.EnableVertexAttribArray(0);
                GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
                GL.DrawArrays(PrimitiveType.Points, 0, 1);
                GL.DisableVertexAttribArray(0);
            }, LogPrefix, "Failed to render individual particle");
        }

        private void RenderGlowEffect(
                    Vector3 position,
                    float size,
                    Color4 glowColor,
                    Matrix4 projectionMatrix,
                    Matrix4 modelViewMatrix)
        {
            SmartLogger.Safe(() => {
                _shaders.Glow!.Color = glowColor;
                _shaders.Glow.Use();
                _shaders.Glow.SetUniform("projection", projectionMatrix);
                _shaders.Glow.SetUniform("modelview", modelViewMatrix);
                _shaders.Glow.SetUniform("uBlurRadius", GlowSettings.BlurRadius);

                float[] vertices = {
                    position.X - size, position.Y - size, position.Z,
                    position.X + size, position.Y - size, position.Z,
                    position.X + size, position.Y + size, position.Z,
                    position.X - size, position.Y + size, position.Z
                };
                int[] indices = { 0, 1, 2, 0, 2, 3 };
                DrawSimpleGeometry(vertices, indices);
            }, LogPrefix, "Failed to render glow effect");
        }

        private void RenderSplash(
            Vector3 position,
            float size,
            Color4 splashColor,
            Matrix4 projectionMatrix,
            Matrix4 modelViewMatrix)
        {
            SmartLogger.Safe(() => {
                _shaders.Splash!.Color = splashColor;
                _shaders.Splash.Use();
                _shaders.Splash.SetUniform("projection", projectionMatrix);
                _shaders.Splash.SetUniform("modelview", modelViewMatrix);

                float[] vertices = {
                    position.X - size, 0, position.Z,
                    position.X + size, 0, position.Z,
                    position.X + size, size / 2, position.Z,
                    position.X - size, size / 2, position.Z
                };
                int[] indices = { 0, 1, 2, 0, 2, 3 };
                DrawSimpleGeometry(vertices, indices);
            }, LogPrefix, "Failed to render splash effect");
        }

        #endregion

        #region Post-Processing Effects

        private void ApplyBloomEffect(Viewport viewport)
        {
            if (_shaders.Bloom == null) return;

            SmartLogger.Safe(() => {
                _shaders.Bloom.Use();
                _shaders.Bloom.SetUniform("uBlurRadius", GlowSettings.BlurRadius);

                float[] vertices = { -1f, -1f, 0f, 1f, -1f, 0f, 1f, 1f, 0f, -1f, 1f, 0f };
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
                    SmartLogger.SafeDispose(_particleSemaphore, "Particle semaphore");
                    DisposeShaders();
                    base.Dispose(disposing);
                }
                _disposed = true;
            }
        }

        private void DisposeShaders()
        {
            SmartLogger.SafeDispose(_shaders.Particle, "Particle shader");
            _shaders.Particle = null;

            SmartLogger.SafeDispose(_shaders.Splash, "Splash shader");
            _shaders.Splash = null;

            SmartLogger.SafeDispose(_shaders.Glow, "Glow shader");
            _shaders.Glow = null;

            SmartLogger.SafeDispose(_shaders.Bloom, "Bloom shader");
            _shaders.Bloom = null;

            SmartLogger.SafeDispose(_shaders.Scene, "Scene shader");
            _shaders.Scene = null;
        }

        #endregion
    }
}