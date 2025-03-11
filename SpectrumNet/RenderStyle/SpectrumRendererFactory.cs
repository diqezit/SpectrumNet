#nullable enable

namespace SpectrumNet
{
    public enum RenderStyle
    {
        Bars,

        // Implementation for others renders temporary disabled to fix all issues and compabilites im main logic

        Raindrops,
        //AsciiDonut,
        //CircularBars,
        //CircularWave,
        //Constellation,
        //Cube,
        //Cubes,
        //Fire,
        //Gauge,
        //Glitch,
        //GradientWave,
        //Heartbeat,
        //Kenwood,
        //LedMeter,
        //Loudness,
        //Particles,
        //Polar,
        //Rainbow,
        //SphereRenderer,
        //TextParticles,
        //Voronoi,
        //Waterfall,
        //Waveform
    }

    public enum RenderQuality
    {
        Low,
        Medium,
        High
    }

    /// <summary>
    /// Базовый абстрактный класс для всех рендереров спектра
    /// </summary>
    /// <summary>
    /// Базовый абстрактный класс для всех рендереров спектра
    /// </summary>
    public abstract class BaseSpectrumRenderer : ISpectrumRenderer, IDisposable, ICameraControllable, ISceneRenderer
    {
        protected bool _isInitialized;
        protected volatile bool _disposed;
        protected SceneGeometry? _sceneGeometry;
        protected readonly string LogPrefix;

        #region OpenGL Resources

        protected record OpenGLResources
        {
            public int VertexArrayObject { get; set; }
            public int VertexBufferObject { get; set; }
            public int IndexBufferObject { get; set; }
        }

        protected OpenGLResources _glResources = new()
        {
            VertexArrayObject = 0,
            VertexBufferObject = 0,
            IndexBufferObject = 0
        };

        #endregion

        #region Camera Properties

        private Vector3 _cameraPositionOffset = Vector3.Zero;
        private Vector2 _cameraRotationOffset = Vector2.Zero;

        public Vector3 CameraPositionOffset
        {
            get => _cameraPositionOffset;
            set
            {
                SmartLogger.Log(LogLevel.Debug, LogPrefix, $"Setting CameraPositionOffset from {_cameraPositionOffset} to {value}");
                _cameraPositionOffset = value;
                if (_cameraPositionOffset.Length > 500)
                    _cameraPositionOffset = Vector3.Normalize(_cameraPositionOffset) * 500;
            }
        }

        public Vector2 CameraRotationOffset
        {
            get => _cameraRotationOffset;
            set => _cameraRotationOffset = value;
        }

        public Vector3 CameraPosition { get; set; } = new Vector3(0, 0, 400);
        public Vector3 CameraForward { get; set; } = new Vector3(0, 0, -1);
        public Vector3 CameraUp { get; set; } = new Vector3(0, 1, 0);

        #endregion

        #region Render Settings

        protected RenderQuality _quality = RenderQuality.Medium;
        protected bool _useGlowEffect = true;
        protected bool _useAntiAlias = true;

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

        #region ISceneRenderer Implementation
        SceneGeometry? ISceneRenderer.SceneGeometry => _sceneGeometry;
        #endregion

        protected BaseSpectrumRenderer(string logPrefix)
        {
            LogPrefix = logPrefix;
        }

        public virtual void Initialize()
        {
            if (!_isInitialized)
            {
                _isInitialized = true;
                SmartLogger.Safe(() => InitializeOpenGLResources(), LogPrefix, "Failed to initialize OpenGL resources");
                SmartLogger.Safe(() => InitializeShaders(), LogPrefix, "Failed to initialize shaders");
                ApplyQualitySettings();
                SmartLogger.Safe(() => {
                    _sceneGeometry = new SceneGeometry();
                    _sceneGeometry.Initialize();
                }, LogPrefix, "Failed to initialize scene geometry");
                SmartLogger.Log(LogLevel.Debug, LogPrefix, "Renderer initialized with OpenGL");
            }
        }

        public SceneGeometry? SceneGeometry
        {
            get => _sceneGeometry;
            protected set => _sceneGeometry = value;
        }

        protected virtual void InitializeOpenGLResources()
        {
            _glResources.VertexArrayObject = GL.GenVertexArray();
            _glResources.VertexBufferObject = GL.GenBuffer();
            _glResources.IndexBufferObject = GL.GenBuffer();
        }

        protected abstract void InitializeShaders();

        protected virtual void ApplyQualitySettings()
        {
            switch (_quality)
            {
                case RenderQuality.Low:
                    ConfigureLowQuality();
                    break;
                case RenderQuality.Medium:
                    ConfigureMediumQuality();
                    break;
                case RenderQuality.High:
                    ConfigureHighQuality();
                    break;
            }
            SmartLogger.Log(LogLevel.Debug, LogPrefix, $"Renderer quality set to {_quality}");
        }

        protected virtual void ConfigureLowQuality()
        {
            _useAntiAlias = false;
            _useGlowEffect = false;
            GL.Disable(EnableCap.Multisample);
            GL.Disable(EnableCap.DepthTest);
        }

        protected virtual void ConfigureMediumQuality()
        {
            _useAntiAlias = true;
            _useGlowEffect = true;
            GL.Enable(EnableCap.Multisample);
            GL.Enable(EnableCap.DepthTest);
        }

        protected virtual void ConfigureHighQuality()
        {
            _useAntiAlias = true;
            _useGlowEffect = true;
            GL.Enable(EnableCap.Multisample);
            GL.Enable(EnableCap.DepthTest);
        }

        public abstract void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium);

        public abstract void Render(
            float[]? spectrum,
            Viewport viewport,
            float elementWidth,
            float elementSpacing,
            int elementCount,
            ShaderProgram? shader,
            Action<Viewport> drawPerformanceInfo);

        protected virtual bool ValidateRenderParameters(float[]? spectrum, Viewport viewport, ShaderProgram? shader, bool requireSpectrum = true)
        {
            if (!_isInitialized)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Renderer not initialized before rendering");
                return false;
            }

            if (viewport.Width <= 0 || viewport.Height <= 0)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Invalid viewport dimensions");
                return false;
            }

            if (requireSpectrum && (spectrum == null || spectrum.Length < 2))
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, "Invalid spectrum data");
                return false;
            }

            return true;
        }

        protected virtual void SetupOpenGLForRendering()
        {
            GL.Enable(EnableCap.DepthTest);
            GL.ClearDepth(1.0f);
            GL.Clear(ClearBufferMask.DepthBufferBit);
        }

        protected virtual void EnableTransparency()
        {
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        }

        protected virtual void DisableSpecialEffects()
        {
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.Blend);
        }

        protected virtual (Matrix4 Projection, Matrix4 ModelView) CalculateRenderMatrices(Viewport viewport, float fovDegrees = 45f)
        {
            Matrix4 projectionMatrix = Matrix4.CreatePerspectiveFieldOfView(
                MathHelper.DegreesToRadians(fovDegrees),
                viewport.Width / viewport.Height,
                0.1f,
                1000f);

            Vector3 cameraPosition = CameraPosition;
            Vector3 lookAt = cameraPosition + CameraForward;
            Vector3 up = CameraUp;
            Matrix4 viewMatrix = Matrix4.LookAt(cameraPosition, lookAt, up);
            Matrix4 modelMatrix = Matrix4.Identity;
            Matrix4 modelViewMatrix = modelMatrix * viewMatrix;

            return (projectionMatrix, modelViewMatrix);
        }

        protected virtual void DrawSimpleGeometry(float[] vertices, int[] indices)
        {
            SmartLogger.Safe(() => {
                GL.BindBuffer(BufferTarget.ArrayBuffer, _glResources.VertexBufferObject);
                GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StreamDraw);
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, _glResources.IndexBufferObject);
                GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(int), indices, BufferUsageHint.StreamDraw);
                GL.EnableVertexAttribArray(0);
                GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
                GL.DrawElements(PrimitiveType.Triangles, indices.Length, DrawElementsType.UnsignedInt, 0);
                GL.DisableVertexAttribArray(0);
            }, LogPrefix, "Failed to draw simple geometry");
        }

        protected float[] ScaleSpectrumSafely(float[]? spectrum, int targetCount, int? sourceLength = null)
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

                    scaledSpectrum[i] = (end > start) ? sum / (end - start) : 0;
                }

                return scaledSpectrum;
            }, new float[targetCount], LogPrefix, "Error scaling spectrum");
        }

        protected static float GetSpectrumValueSafely(float[]? spectrum, int index, float defaultValue = 0f)
        {
            if (spectrum == null || spectrum.Length == 0 || index < 0 || index >= spectrum.Length)
                return defaultValue;

            return spectrum[index];
        }

        protected static int CalculateSpectrumIndex(float position, float maxPosition, int spectrumLength)
        {
            if (spectrumLength <= 0 || maxPosition <= 0)
                return 0;

            int index = (int)(position / maxPosition * spectrumLength);
            return Math.Clamp(index, 0, spectrumLength - 1);
        }

        protected static bool IsValidSpectrum(float[]? spectrum, int minLength = 2)
        {
            return spectrum != null && spectrum.Length >= minLength;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    SmartLogger.Safe(() => DisposePendingOpenGLResources(), LogPrefix, "Failed to dispose OpenGL resources");
                    SmartLogger.SafeDispose(_sceneGeometry, "Scene geometry");
                    _sceneGeometry = null;
                }
                _disposed = true;
            }
        }

        protected virtual void DisposePendingOpenGLResources()
        {
            SmartLogger.Safe(() => {
                if (_glResources.VertexArrayObject != 0)
                {
                    GL.DeleteVertexArray(_glResources.VertexArrayObject);
                    _glResources.VertexArrayObject = 0;
                }
                if (_glResources.VertexBufferObject != 0)
                {
                    GL.DeleteBuffer(_glResources.VertexBufferObject);
                    _glResources.VertexBufferObject = 0;
                }
                if (_glResources.IndexBufferObject != 0)
                {
                    GL.DeleteBuffer(_glResources.IndexBufferObject);
                    _glResources.IndexBufferObject = 0;
                }
            }, LogPrefix, "Failed to dispose OpenGL resources");
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }

    public static class SpectrumRendererFactory
    {
        private const string LogPrefix = "SpectrumRendererFactory";
        private static readonly object _lock = new();
        private static readonly Dictionary<RenderStyle, ISpectrumRenderer> _rendererCache = new();
        private static readonly HashSet<RenderStyle> _initializedRenderers = new();
        private static RenderQuality _globalQuality = RenderQuality.Medium;
        private static bool _isInitialized;

        /// <summary>
        /// Указывает, была ли фабрика полностью инициализирована
        /// </summary>
        public static bool IsInitialized
        {
            get
            {
                lock (_lock)
                {
                    return _isInitialized;
                }
            }
            set
            {
                lock (_lock)
                {
                    _isInitialized = value;
                }
            }
        }

        public static RenderQuality GlobalQuality
        {
            get => _globalQuality;
            set
            {
                if (_globalQuality != value)
                {
                    _globalQuality = value;
                    ConfigureAllRenderers(isOverlayActive: null, _globalQuality);
                    SmartLogger.Log(LogLevel.Information, LogPrefix, $"Global quality changed to {value}", forceLog: true);
                }
            }
        }

        public static ISpectrumRenderer CreateRenderer(RenderStyle style, bool isOverlayActive, RenderQuality? quality = null)
        {
            RenderQuality actualQuality = quality ?? _globalQuality;

            if (_rendererCache.TryGetValue(style, out var cachedRenderer))
            {
                cachedRenderer.Configure(isOverlayActive, actualQuality);
                return cachedRenderer;
            }

            lock (_lock)
            {
                if (_rendererCache.TryGetValue(style, out cachedRenderer))
                {
                    cachedRenderer.Configure(isOverlayActive, actualQuality);
                    return cachedRenderer;
                }

                var renderer = SmartLogger.Safe(() => GetRendererInstance(style),
                    defaultValue: null,
                    LogPrefix,
                    $"Failed to create renderer instance for {style}");

                if (renderer == null)
                {
                    throw new InvalidOperationException($"Failed to create renderer for style {style}");
                }

                SmartLogger.Log(LogLevel.Information, LogPrefix, $"Created new renderer instance for {style}");

                if (!_initializedRenderers.Contains(style))
                {
                    SmartLogger.Safe(() => {
                        renderer.Initialize();
                        _initializedRenderers.Add(style);
                        SmartLogger.Log(LogLevel.Information, LogPrefix, $"Initialized renderer for {style}");
                    }, LogPrefix, $"Failed to initialize renderer {style}", LogLevel.Error);
                }

                renderer.Configure(isOverlayActive, actualQuality);
                _rendererCache[style] = renderer;

                CheckInitializationStatus();

                return renderer;
            }
        }

        public static IEnumerable<ISpectrumRenderer> GetAllRenderers()
        {
            lock (_lock)
                return _rendererCache.Values.ToList();
        }

        public static ISpectrumRenderer? GetCachedRenderer(RenderStyle style)
        {
            lock (_lock)
            {
                return _rendererCache.TryGetValue(style, out var renderer)
                    ? renderer
                    : null;
            }
        }

        public static void ConfigureAllRenderers(bool? isOverlayActive, RenderQuality? quality = null)
        {
            lock (_lock)
            {
                if (_rendererCache.Count == 0)
                {
                    foreach (RenderStyle style in Enum.GetValues(typeof(RenderStyle)))
                    {
                        SmartLogger.Safe(() => {
                            CreateRenderer(style, isOverlayActive ?? false, quality ?? _globalQuality);
                        }, LogPrefix, $"Failed to pre-initialize renderer {style}", LogLevel.Error);
                    }

                    // Проверяем статус инициализации после создания всех рендереров
                    CheckInitializationStatus();
                }

                foreach (var renderer in _rendererCache.Values)
                {
                    SmartLogger.Safe(() => {
                        if (isOverlayActive.HasValue && quality.HasValue)
                        {
                            renderer.Configure(isOverlayActive.Value, quality.Value);
                        }
                        else if (isOverlayActive.HasValue)
                        {
                            renderer.Configure(isOverlayActive.Value, renderer.Quality);
                        }
                        else if (quality.HasValue)
                        {
                            renderer.Configure(isOverlayActive: false, quality.Value);
                            renderer.Quality = quality.Value;
                        }
                    }, LogPrefix, $"Error configuring renderer", LogLevel.Error);
                }
            }
        }

        /// <summary>
        /// Проверяет, были ли инициализированы все рендереры, и устанавливает флаг IsInitialized
        /// </summary>
        private static void CheckInitializationStatus()
        {
            lock (_lock)
            {
                // Получаем все доступные стили рендеринга
                var allStyles = Enum.GetValues<RenderStyle>();

                // Проверяем, все ли стили инициализированы
                bool allInitialized = true;
                foreach (var style in allStyles)
                {
                    if (!_initializedRenderers.Contains(style))
                    {
                        allInitialized = false;
                        break;
                    }
                }

                // Устанавливаем статус инициализации
                _isInitialized = allInitialized;

                if (_isInitialized)
                {
                    SmartLogger.Log(LogLevel.Information, LogPrefix, "All renderers successfully initialized", forceLog: true);
                }
            }
        }

        /// <summary>
        /// Сбрасывает все кешированные рендереры и статус инициализации
        /// </summary>
        public static void Reset()
        {
            lock (_lock)
            {
                // Освобождаем ресурсы, если рендереры реализуют IDisposable
                foreach (var renderer in _rendererCache.Values)
                {
                    if (renderer is IDisposable disposable)
                    {
                        SmartLogger.SafeDispose(disposable, $"Renderer {renderer.GetType().Name}");
                    }
                }

                _rendererCache.Clear();
                _initializedRenderers.Clear();
                _isInitialized = false;

                SmartLogger.Log(LogLevel.Information, LogPrefix, "Factory reset completed", forceLog: true);
            }
        }

        private static ISpectrumRenderer GetRendererInstance(RenderStyle style) => style switch
        {
            RenderStyle.Bars => Bars3DRenderer.GetInstance(),

            // Implementation for others renders temporary disabled due to fix issues and compabilities 

            RenderStyle.Raindrops => RainParticleRenderer.GetInstance(),
            //RenderStyle.AsciiDonut => AsciiDonutRenderer.GetInstance(),
            //RenderStyle.CircularBars => CircularBarsRenderer.GetInstance(),
            //RenderStyle.CircularWave => CircularWaveRenderer.GetInstance(),
            //RenderStyle.Constellation => ConstellationRenderer.GetInstance(),
            //RenderStyle.Cube => CubeRenderer.GetInstance(),
            //RenderStyle.Cubes => CubesRenderer.GetInstance(),
            //RenderStyle.Fire => FireRenderer.GetInstance(),
            //RenderStyle.Gauge => GaugeRenderer.GetInstance(),
            //RenderStyle.Glitch => GlitchRenderer.GetInstance(),
            //RenderStyle.GradientWave => GradientWaveRenderer.GetInstance(),
            //RenderStyle.Heartbeat => HeartbeatRenderer.GetInstance(),
            //RenderStyle.Kenwood => KenwoodRenderer.GetInstance(),
            //RenderStyle.LedMeter => LedMeterRenderer.GetInstance(),
            //RenderStyle.Loudness => LoudnessMeterRenderer.GetInstance(),
            //RenderStyle.Particles => ParticlesRenderer.GetInstance(),
            //RenderStyle.Polar => PolarRenderer.GetInstance(),
            //RenderStyle.Rainbow => RainbowRenderer.GetInstance(),
            //RenderStyle.SphereRenderer => SphereRenderer.GetInstance(),
            //RenderStyle.TextParticles => TextParticlesRenderer.GetInstance(),
            //RenderStyle.Voronoi => VoronoiRenderer.GetInstance(),
            //RenderStyle.Waterfall => WaterfallRenderer.GetInstance(),
            //RenderStyle.Waveform => WaveformRenderer.GetInstance(),

            _ => throw new ArgumentException($"Unknown render style: {style}")
        };
    }
}