#nullable enable
namespace SpectrumNet;

/// <summary>
/// Bars3DRenderer – реализация рендерера спектра в виде трехмерных столбцов (баров).
/// Даный образец оформления рендрера сдедует использовать аналогично для последующих оформений остальных.
/// </summary>
public sealed class Bars3DRenderer : ISpectrumRenderer, IDisposable, ICameraControllable
{
    private static Bars3DRenderer? _instance;
    private bool _isInitialized;
    private volatile bool _disposed;
    private readonly SemaphoreSlim _spectrumSemaphore = new(1, 1);
    private const string LogPrefix = "[Bars3DRenderer] ";

    #region Configurable Constants

    // Константы выделены в отдельные record с тематическими группировками
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

    private record CameraLimits
    {
        public const float MaxTilt = 80f;
        public const float MinTilt = -80f;
        public const float MaxHeight = 2.0f;
        public const float MinHeight = 0.2f;
    }

    // Динамические настройки с более четким именованием
    private float _lightRadius = LightingSettings.DefaultRadius;
    private float _lightBaseY = LightingSettings.DefaultBaseY;
    private float _lightBaseZ = LightingSettings.DefaultBaseZ;
    private float _lightOscillationDivisor = LightingSettings.DefaultOscillationDivisor;

    private float _viewBaseY = ViewSettings.DefaultBaseY;
    private float _viewBaseZ = ViewSettings.DefaultBaseZ;
    private float _viewOscillationAmplitude = ViewSettings.DefaultOscillationAmplitude;
    private float _viewOscillationFrequency = ViewSettings.DefaultOscillationFrequency;

    // Параметры трехмерной сцены
    private float _rotationAngle = 0f;
    private bool _autoRotate = false;
    private float _rotationSpeed = SceneSettings.DefaultRotationSpeed;
    private float _depth = SceneSettings.DefaultDepth;

    #endregion

    #region OpenGL Resources

    // Более структурированные ресурсы OpenGL
    private record OpenGLResources
    {
        public int VertexArrayObject { get; set; }
        public int VertexBufferObject { get; set; }
        public int IndexBufferObject { get; set; }
    }

    private OpenGLResources _glResources = new()
    {
        VertexArrayObject = 0,
        VertexBufferObject = 0,
        IndexBufferObject = 0
    };

    // Шейдеры сгруппированы в отдельную структуру
    private record ShaderCollection
    {
        public ShaderProgram? Bar { get; set; }
        public ShaderProgram? Top { get; set; }
        public ShaderProgram? Side { get; set; }
        public ShaderProgram? Highlight { get; set; }
        public ShaderProgram? Glow { get; set; }
        public ShaderProgram? Bloom { get; set; }
        public ShaderProgram? ShadowMap { get; set; }
    }

    private ShaderCollection _shaders = new();

    #endregion

    #region ICameraControllable Properties

    private Vector3 _cameraPositionOffset = Vector3.Zero;
    private Vector2 _cameraRotationOffset = Vector2.Zero;
    private float _cameraTiltAngle = 10f;
    private float _cameraHeightFactor = 0.8f;

    public Vector3 CameraPositionOffset
    {
        get => _cameraPositionOffset;
        set
        {
            SmartLogger.Log(LogLevel.Debug, LogPrefix, $"Setting CameraPositionOffset from {_cameraPositionOffset} to {value}");
            _cameraPositionOffset = value;
            // Ограничиваем максимальное удаление камеры
            if (_cameraPositionOffset.Length > 500)
            {
                _cameraPositionOffset = Vector3.Normalize(_cameraPositionOffset) * 500;
            }
        }
    }

    public Vector2 CameraRotationOffset
    {
        get => _cameraRotationOffset;
        set
        {
            _cameraRotationOffset = value;
        }
    }

    public float CameraTiltAngle
    {
        get => _cameraTiltAngle;
        set
        {
            SmartLogger.Log(LogLevel.Debug, LogPrefix, $"Setting CameraTiltAngle from {_cameraTiltAngle} to {value}");
            _cameraTiltAngle = Math.Clamp(value, CameraLimits.MinTilt, CameraLimits.MaxTilt);
        }
    }

    public float CameraHeightFactor
    {
        get => _cameraHeightFactor;
        set
        {
            SmartLogger.Log(LogLevel.Debug, LogPrefix, $"Setting CameraHeightFactor from {_cameraHeightFactor} to {value}");
            _cameraHeightFactor = Math.Clamp(value, CameraLimits.MinHeight, CameraLimits.MaxHeight);
        }
    }

    #endregion

    #region Spectrum Processing & Render Settings

    // Дополнительная структура для настроек обработки спектра
    private record SpectrumProcessingSettings
    {
        public float[]? PreviousSpectrum { get; set; }
        public float[]? ProcessedSpectrum { get; set; }
        public float SmoothingFactor { get; set; } = 0.3f;
    }

    private SpectrumProcessingSettings _spectrumSettings = new();
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

    #endregion

    // Синглтон с использованием современного паттерна
    public static Bars3DRenderer GetInstance() => _instance ??= new Bars3DRenderer();
    private Bars3DRenderer() { }

    /// <summary>
    /// Инициализирует рендерер с созданием OpenGL ресурсов
    /// </summary>
    public void Initialize()
    {
        if (!_isInitialized)
        {
            _isInitialized = true;
            InitializeOpenGLResources();
            InitializeShaders();
            ApplyQualitySettings();
            SmartLogger.Log(LogLevel.Debug, LogPrefix, "Bars3DRenderer initialized with OpenGL");
        }
    }

    /// <summary>
    /// Инициализирует буферы и другие ресурсы OpenGL
    /// </summary>
    private void InitializeOpenGLResources()
    {
        _glResources.VertexArrayObject = GL.GenVertexArray();
        _glResources.VertexBufferObject = GL.GenBuffer();
        _glResources.IndexBufferObject = GL.GenBuffer();
    }

    /// <summary>
    /// Инициализирует все шейдеры для рендеринга
    /// </summary>
    private void InitializeShaders()
    {
        try
        {
            // Получение текстов шейдеров из класса Shaders
            var shaderSource = GetShaderSources();

            // Компилируем стандартные шейдеры
            _shaders.Bar = new ShaderProgram(shaderSource.Vertex3D, shaderSource.Fragment3D);
            _shaders.Top = new ShaderProgram(shaderSource.Vertex3D, shaderSource.Fragment3D);
            _shaders.Side = new ShaderProgram(shaderSource.Vertex3D, shaderSource.Fragment3D);
            _shaders.Highlight = new ShaderProgram(shaderSource.Vertex, shaderSource.Fragment);
            _shaders.Glow = new ShaderProgram(shaderSource.Vertex, shaderSource.GlowFragment);

            // Компилируем шейдеры для постобработки
            _shaders.Bloom = new ShaderProgram(shaderSource.PostProcessVertex, shaderSource.BloomFragment);
            _shaders.ShadowMap = new ShaderProgram(shaderSource.ShadowMapVertex, shaderSource.ShadowMapFragment);
        }
        catch (Exception ex)
        {
            SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error initializing 3D shaders: {ex.Message}");
        }
    }

    /// <summary>
    /// Получает исходный код всех шейдеров из класса Shaders
    /// </summary>
    private (
        string Vertex3D, string Fragment3D, string Vertex, string Fragment,
        string GlowFragment, string PostProcessVertex, string BloomFragment,
        string ShadowMapVertex, string ShadowMapFragment
    ) GetShaderSources() => (
        Shaders.vertex3DShader,
        Shaders.fragment3DShader,
        Shaders.vertexShader,
        Shaders.fragmentShader,
        Shaders.glowFragmentShader,
        Shaders.postProcessVertexShader,
        Shaders.bloomFragmentShader,
        Shaders.shadowMapVertexShader,
        Shaders.shadowMapFragmentShader
    );

    /// <summary>
    /// Применяет настройки качества рендеринга
    /// </summary>
    private void ApplyQualitySettings()
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
        SmartLogger.Log(LogLevel.Debug, LogPrefix, $"Bars3DRenderer quality set to {_quality}");
    }

    private void ConfigureLowQuality()
    {
        _useAntiAlias = false;
        _useGlowEffect = false;
        GL.Disable(EnableCap.Multisample);
        GL.Disable(EnableCap.DepthTest);
    }

    private void ConfigureMediumQuality()
    {
        _useAntiAlias = true;
        _useGlowEffect = true;
        GL.Enable(EnableCap.Multisample);
        GL.Enable(EnableCap.DepthTest);
    }

    private void ConfigureHighQuality()
    {
        _useAntiAlias = true;
        _useGlowEffect = true;
        GL.Enable(EnableCap.Multisample);
        GL.Enable(EnableCap.LineSmooth);
        GL.Enable(EnableCap.DepthTest);
        GL.Hint(HintTarget.LineSmoothHint, HintMode.Nicest);
    }

    /// <summary>
    /// Настраивает параметры рендерера
    /// </summary>
    public void Configure(bool isOverlayActive, RenderQuality quality = RenderQuality.Medium)
    {
        _spectrumSettings.SmoothingFactor = isOverlayActive ? 0.5f : 0.3f;
        Quality = quality;
    }

    /// <summary>
    /// Основной метод рендеринга спектра
    /// </summary>
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

        ApplyShaderColor(shader);
        float[] renderSpectrum = ProcessSpectrum(spectrum!, Math.Min(spectrum!.Length, barCount));
        SetupOpenGLForRendering();
        UpdateRotation();
        var matrices = CalculateRenderMatrices(viewport);
        Vector3 lightPos = CalculateLightPosition(viewport);
        Vector3 viewPos = CalculateViewPosition(viewport);
        EnableTransparency();
        RenderBars3D(renderSpectrum, viewport, barWidth, barSpacing,
                    _shaders.Bar ?? shader!, matrices.Projection, matrices.ModelView, lightPos, viewPos);
        DisableSpecialEffects();

        if (_quality != RenderQuality.Low)
        {
            if (_shaders.ShadowMap != null)
                RenderShadowMap(matrices.Projection, matrices.ModelView);

            if (_shaders.Bloom != null)
                ApplyBloomEffect(viewport);
        }
        drawPerformanceInfo?.Invoke(viewport);
    }

    /// <summary>
    /// Применяет цвет из предоставленного шейдера
    /// </summary>
    private void ApplyShaderColor(ShaderProgram? shader)
    {
        if (shader != null && _shaders.Bar != null)
            _shaders.Bar.Color = shader.Color;
    }

    /// <summary>
    /// Настраивает OpenGL для рендеринга
    /// </summary>
    private void SetupOpenGLForRendering()
    {
        GL.Enable(EnableCap.DepthTest);
        GL.ClearDepth(1.0f);
        GL.Clear(ClearBufferMask.DepthBufferBit);
    }

    /// <summary>
    /// Обновляет угол вращения, если включено автовращение
    /// </summary>
    private void UpdateRotation()
    {
        if (_autoRotate)
        {
            _rotationAngle += _rotationSpeed;
            if (_rotationAngle >= 360f)
                _rotationAngle -= 360f;
        }
    }

    /// <summary>
    /// Включает прозрачность для рендеринга
    /// </summary>
    private void EnableTransparency()
    {
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
    }

    /// <summary>
    /// Отключает специальные эффекты OpenGL
    /// </summary>
    private void DisableSpecialEffects()
    {
        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.Blend);
    }

    /// <summary>
    /// Вычисляет матрицы проекции и модели-вида
    /// </summary>
    private (Matrix4 Projection, Matrix4 ModelView) CalculateRenderMatrices(Viewport viewport)
    {
        // Проекционная матрица
        Matrix4 projectionMatrix = Matrix4.CreatePerspectiveFieldOfView(
            MathHelper.DegreesToRadians(SceneSettings.DefaultFovDegrees),
            viewport.Width / viewport.Height,
            0.1f,
            1000f);

        // 1. Вычисляем базовую позицию камеры с применением CameraHeightFactor
        float cameraHeight = viewport.Height * CameraHeightFactor;
        float baseCameraZ = cameraHeight / MathF.Tan(MathHelper.DegreesToRadians(SceneSettings.DefaultPerspectiveAngle));
        Vector3 baseCameraPos = new Vector3(viewport.Width / 2, -cameraHeight, baseCameraZ);

        // 2. Применяем пользовательское смещение позиции
        Vector3 cameraPosition = baseCameraPos + CameraPositionOffset;

        // 3. Применяем базовый угол наклона и пользовательские вращения
        float finalRotationY = _rotationAngle + CameraRotationOffset.X * 2f;
        float finalTiltX = CameraTiltAngle + CameraRotationOffset.Y * 1.5f;

        // Ограничиваем наклон камеры
        finalTiltX = Math.Clamp(finalTiltX, CameraLimits.MinTilt, CameraLimits.MaxTilt);

        // 4. Создаем матрицы вращения
        Matrix4 tiltMatrix = Matrix4.CreateRotationX(MathHelper.DegreesToRadians(finalTiltX));
        Matrix4 rotationMatrix = Matrix4.CreateRotationY(MathHelper.DegreesToRadians(finalRotationY));
        Matrix4 modelMatrix = tiltMatrix * rotationMatrix;

        // 5. Матрица вида - добавляем смещение для точки, на которую смотрит камера
        Vector3 lookAtOffset = new Vector3(
            CameraRotationOffset.X * 2f,
            CameraRotationOffset.Y * 2f,
            0);
        Vector3 lookAt = new Vector3(viewport.Width / 2, viewport.Height / 2, 0) + lookAtOffset;
        Vector3 up = Vector3.UnitY;
        Matrix4 viewMatrix = Matrix4.LookAt(cameraPosition, lookAt, up);

        // Итоговая матрица модель-вид
        Matrix4 modelViewMatrix = modelMatrix * viewMatrix;

        return (projectionMatrix, modelViewMatrix);
    }

    /// <summary>
    /// Вычисляет позицию источника света
    /// </summary>
    private Vector3 CalculateLightPosition(Viewport viewport)
    {
        float lightRadius = _lightRadius;
        return new Vector3(
            viewport.Width / 2 + MathF.Cos(_rotationAngle + CameraRotationOffset.X * 0.5f) * lightRadius,
            _lightBaseY + MathF.Sin(_rotationAngle) * (lightRadius / _lightOscillationDivisor),
            _lightBaseZ);
    }

    /// <summary>
    /// Вычисляет позицию точки обзора
    /// </summary>
    private Vector3 CalculateViewPosition(Viewport viewport)
    {
        return new Vector3(
            viewport.Width / 2 + CameraPositionOffset.X * 0.25f,
            _viewBaseY,
            _viewBaseZ + _viewOscillationAmplitude * MathF.Sin(_rotationAngle * _viewOscillationFrequency)
                     + CameraPositionOffset.Z * 0.25f);
    }

    /// <summary>
    /// Проверяет валидность параметров рендеринга
    /// </summary>
    private bool ValidateRenderParameters(float[]? spectrum, Viewport viewport, ShaderProgram? shader)
    {
        if (!_isInitialized)
        {
            SmartLogger.Log(LogLevel.Error, LogPrefix, "Bars3DRenderer not initialized before rendering");
            return false;
        }
        if (spectrum == null || spectrum.Length < 2 || (shader == null && _shaders.Bar == null) || viewport.Width <= 0 || viewport.Height <= 0)
        {
            SmartLogger.Log(LogLevel.Error, LogPrefix, "Invalid render parameters for Bars3DRenderer");
            return false;
        }
        return true;
    }

    /// <summary>
    /// Обрабатывает спектр для рендеринга
    /// </summary>
    private float[] ProcessSpectrum(float[] spectrum, int targetCount)
    {
        bool semaphoreAcquired = false;
        try
        {
            semaphoreAcquired = _spectrumSemaphore.Wait(0);
            if (semaphoreAcquired)
            {
                float[] scaledSpectrum = ScaleSpectrum(spectrum, targetCount, spectrum.Length);
                _spectrumSettings.ProcessedSpectrum = SmoothSpectrum(scaledSpectrum, targetCount);
            }
            return _spectrumSettings.ProcessedSpectrum ?? ProcessSynchronously(spectrum, targetCount);
        }
        catch (Exception ex)
        {
            SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error processing spectrum: {ex.Message}");
            return new float[targetCount];
        }
        finally
        {
            if (semaphoreAcquired)
                _spectrumSemaphore.Release();
        }
    }

    /// <summary>
    /// Синхронная обработка спектра
    /// </summary>
    private float[] ProcessSynchronously(float[] spectrum, int targetCount)
    {
        var scaledSpectrum = ScaleSpectrum(spectrum, targetCount, spectrum.Length);
        return SmoothSpectrum(scaledSpectrum, targetCount);
    }

    /// <summary>
    /// Рендерит трехмерные бары спектра
    /// </summary>
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

        // Адаптация визуализации к позиции камеры
        float distanceFactor = Math.Max(1.0f, Vector3.Distance(Vector3.Zero, CameraPositionOffset) / 100.0f);

        GL.BindVertexArray(_glResources.VertexArrayObject);

        for (int i = 0; i < spectrum.Length; i++)
        {
            float magnitude = spectrum[i];
            float barHeight = MathF.Max(magnitude * canvasHeight, BarSettings.MinHeight);
            float x = i * totalBarWidth;

            // Создаем цвета для различных частей бара
            var colors = CalculateBarColors(baseShader.Color, magnitude, distanceFactor);

            // Рендерим основной 3D-бар
            RenderBar3D(x, barWidth, barHeight, canvasHeight, _depth, colors.Bar, colors.Top, colors.Side,
                        lightPos, viewPos, projectionMatrix, modelViewMatrix);

            // Рендерим свечение (glow), если включено и бар достаточно высокий
            if (ShouldRenderGlow(magnitude))
            {
                Color4 glowColor = new(baseShader.Color.R, baseShader.Color.G, baseShader.Color.B,
                                        magnitude * GlowSettings.EffectAlpha);
                RenderGlowEffect(x, barWidth, barHeight, canvasHeight, cornerRadius, glowColor,
                                projectionMatrix, modelViewMatrix);
            }

            // Рендерим подсветку для высоких баров
            if (ShouldRenderHighlight(barHeight, cornerRadius))
            {
                float highlightWidth = barWidth * BarSettings.HighlightWidthProportion;
                float highlightHeight = MathF.Min(barHeight * BarSettings.HighlightHeightProportion,
                                                BarSettings.MaxHighlightHeight);
                byte highlightAlpha = (byte)(colors.Alpha / BarSettings.HighlightAlphaDivisor);
                Color4 highlightColor = new(Color4.White.R, Color4.White.G, Color4.White.B, highlightAlpha / 255f);
                RenderHighlight3D(x, barWidth, barHeight, canvasHeight, _depth, highlightWidth,
                                 highlightHeight, highlightColor, projectionMatrix, modelViewMatrix);
            }
        }

        GL.BindVertexArray(0);
    }

    /// <summary>
    /// Вычисляет цвета для различных частей бара
    /// </summary>
    private (Color4 Bar, Color4 Top, Color4 Side, byte Alpha) CalculateBarColors(
        Color4 baseColor, float magnitude, float distanceFactor)
    {
        byte alpha = (byte)MathF.Min(magnitude * BarSettings.AlphaMultiplier * 255f / distanceFactor, 255f);
        Color4 barColor = new(baseColor.R, baseColor.G, baseColor.B, alpha / 255f);
        Color4 topColor = new(baseColor.R * 1.2f, baseColor.G * 1.2f, baseColor.B * 1.2f, alpha / 255f);
        Color4 sideColor = new(baseColor.R * 0.8f, baseColor.G * 0.8f, baseColor.B * 0.8f, alpha / 255f);

        return (barColor, topColor, sideColor, alpha);
    }

    /// <summary>
    /// Проверяет, нужно ли рендерить свечение
    /// </summary>
    private bool ShouldRenderGlow(float magnitude) =>
        _useGlowEffect && _shaders.Glow != null && magnitude > 0.6f;

    /// <summary>
    /// Проверяет, нужно ли рендерить подсветку
    /// </summary>
    private bool ShouldRenderHighlight(float barHeight, float cornerRadius) =>
        barHeight > cornerRadius * 2 && _quality != RenderQuality.Low;

    /// <summary>
    /// Рендерит основной 3D-бар
    /// </summary>
    private void RenderBar3D(
        float x,
        float barWidth,
        float barHeight,
        float canvasHeight,
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

        float y = canvasHeight - barHeight;
        float z = 0;

        // Геометрические данные для всех граней
        var geometryData = CreateBarGeometryData(x, y, z, barWidth, barHeight, canvasHeight, depth);

        // Общие индексы для всех граней
        int[] indices = { 0, 1, 2, 0, 2, 3 };

        // Рендерим каждую грань бара с соответствующим шейдером и цветом
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

    /// <summary>
    /// Создает геометрические данные для всех граней бара
    /// </summary>
    private record BarGeometryData(
        float[] Front, float[] Back, float[] Top,
        float[] Bottom, float[] Left, float[] Right);

    private BarGeometryData CreateBarGeometryData(
        float x, float y, float z, float barWidth, float barHeight, float canvasHeight, float depth)
    {
        // Передняя грань
        float[] front = {
            x,          y,         z,   0.0f, 0.0f, 1.0f,
            x+barWidth, y,         z,   0.0f, 0.0f, 1.0f,
            x+barWidth, canvasHeight, z, 0.0f, 0.0f, 1.0f,
            x,          canvasHeight, z, 0.0f, 0.0f, 1.0f
        };

        // Задняя грань
        float[] back = {
            x,          y,         z-depth,  0.0f, 0.0f, -1.0f,
            x+barWidth, y,         z-depth,  0.0f, 0.0f, -1.0f,
            x+barWidth, canvasHeight, z-depth,  0.0f, 0.0f, -1.0f,
            x,          canvasHeight, z-depth,  0.0f, 0.0f, -1.0f
        };

        // Верхняя грань
        float[] top = {
            x,          y, z,         0.0f, -1.0f, 0.0f,
            x+barWidth, y, z,         0.0f, -1.0f, 0.0f,
            x+barWidth, y, z-depth,   0.0f, -1.0f, 0.0f,
            x,          y, z-depth,   0.0f, -1.0f, 0.0f
        };

        // Нижняя грань
        float[] bottom = {
            x,          canvasHeight, z,         0.0f, 1.0f, 0.0f,
            x+barWidth, canvasHeight, z,         0.0f, 1.0f, 0.0f,
            x+barWidth, canvasHeight, z-depth,   0.0f, 1.0f, 0.0f,
            x,          canvasHeight, z-depth,   0.0f, 1.0f, 0.0f
        };

        // Левая грань
        float[] left = {
            x, y,         z,         -1.0f, 0.0f, 0.0f,
            x, y,         z-depth,   -1.0f, 0.0f, 0.0f,
            x, canvasHeight, z-depth, -1.0f, 0.0f, 0.0f,
            x, canvasHeight, z,       -1.0f, 0.0f, 0.0f
        };

        // Правая грань
        float[] right = {
            x+barWidth, y,         z,         1.0f, 0.0f, 0.0f,
            x+barWidth, y,         z-depth,   1.0f, 0.0f, 0.0f,
            x+barWidth, canvasHeight, z-depth, 1.0f, 0.0f, 0.0f,
            x+barWidth, canvasHeight, z,       1.0f, 0.0f, 0.0f
        };

        return new BarGeometryData(front, back, top, bottom, left, right);
    }

    /// <summary>
    /// Рендерит одну грань бара
    /// </summary>
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
        shader.SetUniform("lightPos", lightPos);
        shader.SetUniform("viewPos", viewPos);

        DrawGeometry3D(vertices, indices);
    }

    /// <summary>
    /// Рендерит подсветку на верхней части бара
    /// </summary>
    private void RenderHighlight3D(
        float x,
        float barWidth,
        float barHeight,
        float canvasHeight,
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
        float y = canvasHeight - barHeight;

        float[] vertices = {
            highlightX,             y,                         -depth * LightingSettings.HighlightDepthStartFactor,
            highlightX + highlightWidth, y,                    -depth * LightingSettings.HighlightDepthStartFactor,
            highlightX + highlightWidth, y,                    -depth * LightingSettings.HighlightDepthEndFactor,
            highlightX,             y,                         -depth * LightingSettings.HighlightDepthEndFactor
        };
        int[] indices = { 0, 1, 2, 0, 2, 3 };

        DrawSimpleGeometry(vertices, indices);
    }

    /// <summary>
    /// Рендерит эффект свечения вокруг бара
    /// </summary>
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
        if (_shaders.Glow == null) return;

        _shaders.Glow.Color = glowColor;
        _shaders.Glow.Use();
        _shaders.Glow.SetUniform("projection", projectionMatrix);
        _shaders.Glow.SetUniform("modelview", modelViewMatrix);
        _shaders.Glow.SetUniform("uBlurRadius", GlowSettings.BlurRadius);

        float[] vertices = {
            x - GlowSettings.ExtraSize, canvasHeight - barHeight - GlowSettings.ExtraSize, 1.0f,
            x + barWidth + GlowSettings.ExtraSize, canvasHeight - barHeight - GlowSettings.ExtraSize, 1.0f,
            x + barWidth + GlowSettings.ExtraSize, canvasHeight + GlowSettings.ExtraSize, 1.0f,
            x - GlowSettings.ExtraSize, canvasHeight + GlowSettings.ExtraSize, 1.0f
        };
        int[] indices = { 0, 1, 2, 0, 2, 3 };

        DrawSimpleGeometry(vertices, indices);
    }

    /// <summary>
    /// Рисует геометрию с использованием указанных вершин и индексов (упрощенный вариант)
    /// </summary>
    private void DrawSimpleGeometry(float[] vertices, int[] indices)
    {
        GL.BindBuffer(BufferTarget.ArrayBuffer, _glResources.VertexBufferObject);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StreamDraw);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _glResources.IndexBufferObject);
        GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(int), indices, BufferUsageHint.StreamDraw);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
        GL.DrawElements(PrimitiveType.Triangles, indices.Length, DrawElementsType.UnsignedInt, 0);
        GL.DisableVertexAttribArray(0);
    }

    /// <summary>
    /// Рисует 3D-геометрию с использованием указанных вершин и индексов (с поддержкой нормалей)
    /// </summary>
    private void DrawGeometry3D(float[] vertices, int[] indices)
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
    }

    /// <summary>
    /// Масштабирует входной спектр до нужного количества баров
    /// </summary>
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

    /// <summary>
    /// Применяет сглаживание к спектру для более плавной анимации
    /// </summary>
    private float[] SmoothSpectrum(float[] scaledSpectrum, int actualBarCount)
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
    }

    /// <summary>
    /// Применяет эффект Bloom (свечение) к сцене
    /// </summary>
    private void ApplyBloomEffect(Viewport viewport)
    {
        if (_shaders.Bloom == null) return;
        _shaders.Bloom.Use();
        _shaders.Bloom.SetUniform("uBlurRadius", GlowSettings.BlurRadius);

        // Координаты в пространстве от -1 до 1 для полноэкранного квада
        float[] vertices = {
             -1f, -1f, 0f,
              1f, -1f, 0f,
              1f,  1f, 0f,
             -1f,  1f, 0f
        };
        int[] indices = { 0, 1, 2, 0, 2, 3 };

        DrawSimpleGeometry(vertices, indices);
    }

    /// <summary>
    /// Рендерит карту теней
    /// </summary>
    private void RenderShadowMap(Matrix4 projectionMatrix, Matrix4 modelViewMatrix)
    {
        if (_shaders.ShadowMap == null) return;
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
    }

    /// <summary>
    /// Освобождает управляемые ресурсы
    /// </summary>
    protected void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Освобождаем управляемые ресурсы
                _spectrumSemaphore?.Dispose();
                DisposePendingOpenGLResources();
                DisposeShaders();
            }

            _disposed = true;
        }
    }

    /// <summary>
    /// Освобождает ресурсы OpenGL
    /// </summary>
    private void DisposePendingOpenGLResources()
    {
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
    }

    /// <summary>
    /// Освобождает шейдеры
    /// </summary>
    private void DisposeShaders()
    {
        _shaders.Bar?.Dispose();
        _shaders.Bar = null;
        _shaders.Top?.Dispose();
        _shaders.Top = null;
        _shaders.Side?.Dispose();
        _shaders.Side = null;
        _shaders.Highlight?.Dispose();
        _shaders.Highlight = null;
        _shaders.Glow?.Dispose();
        _shaders.Glow = null;
        _shaders.Bloom?.Dispose();
        _shaders.Bloom = null;
        _shaders.ShadowMap?.Dispose();
        _shaders.ShadowMap = null;
    }

    /// <summary>
    /// Реализация интерфейса IDisposable
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}