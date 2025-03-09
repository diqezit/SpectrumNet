#nullable enable
namespace SpectrumNet;

public sealed class Renderer : IDisposable
{
    #region Константы
    private const int RENDER_TIMEOUT_MS = 16; 
    private const string DEFAULT_STYLE = "Solid"; 
    private const string LogPrefix = "[Renderer] "; 
    #endregion

    #region Типы
    private readonly record struct RenderState(ShaderProgram? Paint, RenderStyle Style, string StyleName, RenderQuality Quality);
    #endregion

    #region Поля
    private readonly SemaphoreSlim _renderLock = new(1, 1); 
    private readonly SpectrumBrushes _spectrumStyles; 
    private readonly IAudioVisualizationController _controller; 
    private readonly ISpectrumAnalyzer _analyzer;
    private readonly CancellationTokenSource _disposalTokenSource = new(); 

    private string? _pendingStyleName; 
    private Color4 _pendingColor; 
    private ShaderProgram? _pendingShader;
    private GLWpfControl? _glControl;
    private DispatcherTimer? _renderTimer; 
    private Matrix4 _projectionMatrix; 
    private RenderState _currentState;

    private volatile bool _isDisposed;
    private volatile bool _isAnalyzerDisposed; 
    private volatile bool _shouldShowPlaceholder = true; 
    private bool _isInitialized;
    private bool _isGpuInfoLogged; 
    #endregion

    #region Свойства
    public string CurrentStyleName => _currentState.StyleName;
    public RenderQuality CurrentQuality => _currentState.Quality; 
    public event EventHandler<PerformanceMetrics>? PerformanceUpdate; 

    public bool ShouldShowPlaceholder
    {
        get => _shouldShowPlaceholder;
        set => _shouldShowPlaceholder = value;
    }
    #endregion

    #region Конструктор
    public Renderer(SpectrumBrushes styles, IAudioVisualizationController controller,
                   ISpectrumAnalyzer analyzer, GLWpfControl glControl)
    {
        ArgumentNullException.ThrowIfNull(styles, nameof(styles));
        ArgumentNullException.ThrowIfNull(controller, nameof(controller));
        ArgumentNullException.ThrowIfNull(analyzer, nameof(analyzer));
        ArgumentNullException.ThrowIfNull(glControl, nameof(glControl));

        _spectrumStyles = styles;
        _controller = controller;
        _analyzer = analyzer;
        _glControl = glControl;

        SmartLogger.Log(LogLevel.Debug, LogPrefix,
            $"Renderer создан с анализатором HashCode: {analyzer.GetHashCode():X8}");

        if (_analyzer is IComponent comp)
            comp.Disposed += (_, _) => _isAnalyzerDisposed = true;

        _shouldShowPlaceholder = !_controller.IsRecording;
        _glControl.Render += OnGlControlRender;

        SmartLogger.Log(LogLevel.Information, LogPrefix,
            "Renderer сконструирован, ожидает рендеринга для полной инициализации.");
        PerformanceMetricsManager.PerformanceUpdated += OnPerformanceMetricsUpdated;
    }
    #endregion

    #region Инициализация
    private void InitializeRenderer()
    {
        var (color, shader) = _spectrumStyles.GetColorAndShader(DEFAULT_STYLE);
        ShaderProgram? clonedShader = shader?.Clone();

        if (clonedShader is null)
            throw new InvalidOperationException(
                $"{LogPrefix}Не удалось инициализировать стиль {DEFAULT_STYLE}: клонирование шейдера не удалось.");

        _currentState = new RenderState(clonedShader, RenderStyle.Bars, DEFAULT_STYLE, RenderQuality.Medium);

        _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RENDER_TIMEOUT_MS) };
        _renderTimer.Tick += (_, _) => RequestRender();
        _renderTimer.Start();

        if (_glControl is not null)
        {
            _glControl.SizeChanged += UpdateRenderDimensions;
            SmartLogger.Log(LogLevel.Debug, LogPrefix,
                $"GL-контрол инициализирован: {_glControl.ActualWidth}x{_glControl.ActualHeight}");
        }

        _controller.PropertyChanged += OnControllerPropertyChanged;
        SynchronizeWithController();
        RequestRender();

        SmartLogger.Log(LogLevel.Information, LogPrefix,
            $"Renderer инициализирован со стилем: {_currentState.StyleName}, RenderStyle: {_currentState.Style}");
    }

    private void InitializeOpenGLResources()
    {
        try
        {
            _currentState.Paint?.Dispose();
            GL.Finish();

            var (color, shader) = _spectrumStyles.GetColorAndShader(DEFAULT_STYLE);
            ShaderProgram? clonedShader = shader?.Clone();

            if (clonedShader is null)
                throw new InvalidOperationException(
                    "Клонирование шейдера не удалось при инициализации ресурсов OpenGL.");

            _currentState = _currentState with { Paint = clonedShader };
            SmartLogger.Log(LogLevel.Debug, LogPrefix, "Ресурсы OpenGL инициализированы");
        }
        catch (Exception ex)
        {
            SmartLogger.Log(LogLevel.Error, LogPrefix, $"Ошибка инициализации ресурсов OpenGL: {ex}");
        }
    }
    #endregion

    #region Обработчики событий
    private void OnPerformanceMetricsUpdated(object? sender, PerformanceMetrics metrics) =>
        PerformanceUpdate?.Invoke(this, metrics);

    private void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e is null) return;

        switch (e.PropertyName)
        {
            case nameof(IAudioVisualizationController.IsRecording):
                _shouldShowPlaceholder = !_controller.IsRecording;
                RequestRender();
                break;

            case nameof(IAudioVisualizationController.IsOverlayActive):
                SpectrumRendererFactory.ConfigureAllRenderers(_controller.IsOverlayActive);
                RequestRender();
                break;

            case nameof(IAudioVisualizationController.SelectedDrawingType):
                SmartLogger.Log(LogLevel.Debug, LogPrefix,
                    $"Тип рисования изменен на: {_controller.SelectedDrawingType}");
                UpdateRenderStyle(_controller.SelectedDrawingType);
                break;

            case nameof(IAudioVisualizationController.SelectedStyle):
                if (!string.IsNullOrEmpty(_controller.SelectedStyle))
                {
                    var (color, shader) = _spectrumStyles.GetColorAndShader(_controller.SelectedStyle);
                    UpdateSpectrumStyle(_controller.SelectedStyle, color, shader);
                }
                break;

            case nameof(IAudioVisualizationController.RenderQuality):
                UpdateRenderQuality(_controller.RenderQuality);
                break;

            case nameof(IAudioVisualizationController.WindowType):
            case nameof(IAudioVisualizationController.ScaleType):
                _analyzer.UpdateSettings(_controller.WindowType, _controller.ScaleType);
                SynchronizeWithController();
                RequestRender();
                break;

            case nameof(IAudioVisualizationController.BarSpacing):
            case nameof(IAudioVisualizationController.BarCount):
                RequestRender();
                break;
        }
    }

    private void UpdateRenderDimensions(object? sender, SizeChangedEventArgs e)
    {
        if (_isDisposed || _glControl is null || !_glControl.IsInitialized)
            return;

        try
        {
            var newWidth = (int)e.NewSize.Width;
            var newHeight = (int)e.NewSize.Height;

            if (newWidth <= 0 || newHeight <= 0)
                return;

            _projectionMatrix = Matrix4.CreateOrthographicOffCenter(0, newWidth, newHeight, 0, -1, 1);
            RequestRender();
        }
        catch (Exception ex)
        {
            SmartLogger.Log(LogLevel.Error, LogPrefix, $"Ошибка обновления размеров: {ex.Message}");
        }
    }
    #endregion

    #region Рендеринг
    public void OnGlControlRender(TimeSpan delta)
    {
        if (_isDisposed || !_renderLock.Wait(0))
            return;

        try
        {
            if (_glControl is null || !_glControl.IsInitialized)
                return;

            if (!_isInitialized)
            {
                InitializeRenderer();
                _isInitialized = true;
            }

            if (_pendingStyleName is not null && _pendingShader is not null)
            {
                ShaderProgram? oldShader = _currentState.Paint;
                try
                {
                    var clonedShader = _pendingShader.Clone()
                        ?? throw new ArgumentException(
                            $"Не удалось клонировать шейдер для стиля: {_pendingStyleName}");

                    _currentState = _currentState with
                    {
                        Paint = clonedShader,
                        StyleName = _pendingStyleName
                    };

                    oldShader?.Dispose();

                    SmartLogger.Log(LogLevel.Debug, LogPrefix, $"Стиль применен: {_pendingStyleName}");
                }
                catch (Exception ex)
                {
                    SmartLogger.Log(LogLevel.Error, LogPrefix, $"Ошибка смены стиля: {ex.Message}");
                }
                finally
                {
                    _pendingStyleName = null;
                    _pendingShader = null;
                }
            }

            RenderFrameInternal(delta);
        }
        catch (Exception ex)
        {
            SmartLogger.Log(LogLevel.Error, LogPrefix, $"Ошибка рендеринга: {ex.Message}");
        }
        finally
        {
            _renderLock.Release();
        }
    }

    private void RenderFrameInternal(TimeSpan delta)
    {
        try
        {
            if (_glControl is null || !_glControl.IsInitialized ||
                _glControl.ActualWidth <= 0 || _glControl.ActualHeight <= 0)
            {
                SmartLogger.Log(LogLevel.Debug, LogPrefix,
                    $"Пропуск рендеринга: контрол недействителен или нулевые размеры " +
                    $"({(_glControl?.ActualWidth ?? 0)}x{(_glControl?.ActualHeight ?? 0)})");
                _isInitialized = false;
                return;
            }

            if (!_isInitialized)
            {
                InitializeOpenGLResources();
                _isInitialized = true;
            }

            if (!_isGpuInfoLogged)
            {
                LogGpuInfo();
                _isGpuInfoLogged = true;
            }

            GL.Viewport(0, 0, (int)_glControl.ActualWidth, (int)_glControl.ActualHeight);
            GL.ClearColor(0.0f, 0.0f, 0.0f, 0.0f);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            if (_projectionMatrix == default)
                _projectionMatrix = Matrix4.CreateOrthographicOffCenter(
                    0, (int)_glControl.ActualWidth, (int)_glControl.ActualHeight, 0, -1, 1);

            bool shouldShowPlaceholder = _shouldShowPlaceholder || _controller.IsTransitioning ||
                _analyzer is null || _isAnalyzerDisposed || !_controller.IsRecording ||
                _currentState.Paint is null;

            if (shouldShowPlaceholder)
            {
                RenderPlaceholder();
                return;
            }

            var spectrum = GetSpectrumData();
            if (spectrum is null || spectrum.Spectrum.Length == 0)
            {
                SmartLogger.Log(LogLevel.Debug, LogPrefix, "Данные спектра отсутствуют");
                RenderPlaceholder();
                return;
            }

            if (!TryCalcRenderParams(out float barWidth, out float barSpacing, out int barCount))
            {
                SmartLogger.Log(LogLevel.Debug, LogPrefix, "Не удалось вычислить параметры рендеринга");
                RenderPlaceholder();
                return;
            }

            try
            {
                var renderer = SpectrumRendererFactory.CreateRenderer(
                    _currentState.Style,
                    _controller.IsOverlayActive,
                    _currentState.Quality
                );

                if (renderer is null)
                {
                    SmartLogger.Log(LogLevel.Error, LogPrefix,
                        $"Не удалось создать рендерер для стиля: {_currentState.Style}");
                    RenderPlaceholder();
                    return;
                }

                var viewport = new Viewport(
                    0, 0, (int)_glControl.ActualWidth, (int)_glControl.ActualHeight);

                renderer.Render(
                    spectrum.Spectrum,
                    viewport,
                    barWidth,
                    barSpacing,
                    barCount,
                    _currentState.Paint,
                    v =>
                    {
                        if (_controller.ShowPerformanceInfo)
                            PerformanceMetricsManager.DrawPerformanceInfo(v, _controller.ShowPerformanceInfo);
                    }
                );

                _controller.Dispatcher.Invoke(() =>
                {
                    _controller.OnPropertyChanged(
                        nameof(IAudioVisualizationController.IsRecording),
                        nameof(IAudioVisualizationController.CanStartCapture)
                    );
                });
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Ошибка рендеринга: {ex.Message}");
                RenderPlaceholder();
            }
            finally
            {
                _glControl.InvalidateVisual();
            }
        }
        catch (Exception ex)
        {
            SmartLogger.Log(LogLevel.Error, LogPrefix, $"Критическая ошибка рендеринга: {ex}");
            _controller.Dispatcher.Invoke(() =>
            {
                _controller.StatusText = "Фатальная ошибка рендеринга!";
                _controller.IsRecording = false;
            });
        }
    }

    private void RenderPlaceholder()
    {
        GL.ClearColor(1.0f, 0.1f, 1.0f, 1.0f);
        GL.Clear(ClearBufferMask.ColorBufferBit);
    }
    #endregion

    #region Доступ к данным
    private SpectralData? GetSpectrumData()
    {
        try
        {
            if (_analyzer is null)
            {
                SmartLogger.Log(LogLevel.Warning, LogPrefix,
                    "Ссылка на анализатор null в GetSpectrumData");
                return null;
            }

            var spectrum = _analyzer.GetCurrentSpectrum();

            if (spectrum is null)
                SmartLogger.Log(LogLevel.Warning, LogPrefix, "Данные спектра null");

            return spectrum;
        }
        catch (ObjectDisposedException)
        {
            _isAnalyzerDisposed = true;
            _shouldShowPlaceholder = true;
            SmartLogger.Log(LogLevel.Error, LogPrefix,
                "Анализатор был утилизирован при попытке получить спектр");
            return null;
        }
        catch (Exception ex)
        {
            SmartLogger.Log(LogLevel.Error, LogPrefix, $"Ошибка получения данных спектра: {ex.Message}");
            return null;
        }
    }

    private bool TryCalcRenderParams(out float barWidth, out float barSpacing, out int barCount)
    {
        barWidth = barSpacing = 0;
        barCount = _controller.BarCount;
        int totalWidth = _glControl is not null ? (int)_glControl.ActualWidth : 0;

        if (totalWidth <= 0 || barCount <= 0)
            return false;

        barCount = Math.Max(1, barCount);
        totalWidth = Math.Max(1, totalWidth);
        barSpacing = Math.Min((float)_controller.BarSpacing, totalWidth / (barCount + 1));
        barSpacing = Math.Max(0, barSpacing);
        barWidth = Math.Max((totalWidth - (barCount - 1) * barSpacing) / barCount, 1.0f);

        if (barCount > 1)
            barSpacing = (totalWidth - barCount * barWidth) / (barCount - 1);
        else
            barSpacing = 0;

        return true;
    }
    #endregion

    #region Конфигурация
    public void SynchronizeWithController()
    {
        EnsureNotDisposed();

        try
        {
            bool needsUpdate = false;
            var controller = _controller;

            if (_currentState.Style != controller.SelectedDrawingType)
            {
                UpdateRenderStyle(controller.SelectedDrawingType);
                needsUpdate = true;
            }

            if (controller.SelectedStyle != _currentState.StyleName)
            {
                var (color, shader) = _spectrumStyles.GetColorAndShader(controller.SelectedStyle);
                UpdateSpectrumStyle(controller.SelectedStyle, color, shader);
                needsUpdate = true;
            }

            if (_currentState.Quality != controller.RenderQuality)
            {
                UpdateRenderQuality(controller.RenderQuality);
                needsUpdate = true;
            }

            if (needsUpdate)
            {
                RequestRender();
                SmartLogger.Log(LogLevel.Debug, LogPrefix, "Визуальные параметры синхронизированы");
            }
        }
        catch (Exception ex)
        {
            SmartLogger.Log(LogLevel.Error, LogPrefix, $"Ошибка синхронизации: {ex.Message}");
        }
    }

    public void UpdateRenderStyle(RenderStyle style)
    {
        EnsureNotDisposed();
        if (_currentState.Style == style) return;

        SmartLogger.Log(LogLevel.Information, LogPrefix,
            $"Обновление стиля рендеринга с {_currentState.Style} на {style}");
        _currentState = _currentState with { Style = style };

        try
        {
            var renderer = SpectrumRendererFactory.CreateRenderer(
                style, _controller.IsOverlayActive, _currentState.Quality);
            SmartLogger.Log(LogLevel.Debug, LogPrefix,
                $"Рендерер для стиля {style} успешно создан");
        }
        catch (Exception ex)
        {
            SmartLogger.Log(LogLevel.Error, LogPrefix,
                $"Не удалось создать рендерер для стиля {style}: {ex.Message}");
        }

        RequestRender();
    }

    public void UpdateSpectrumStyle(string styleName, Color4 color, ShaderProgram? shader)
    {
        EnsureNotDisposed();
        if (string.IsNullOrEmpty(styleName))
            throw new ArgumentException("Имя стиля не может быть null или пустым", nameof(styleName));

        if (styleName == _currentState.StyleName) return;

        SmartLogger.Log(LogLevel.Information, LogPrefix,
            $"Обновление стиля спектра с {_currentState.StyleName} на {styleName}");
        _pendingStyleName = styleName;
        _pendingColor = color;
        _pendingShader = shader;
        RequestRender();
    }

    public void UpdateRenderQuality(RenderQuality quality)
    {
        EnsureNotDisposed();
        if (_currentState.Quality == quality) return;

        SmartLogger.Log(LogLevel.Information, LogPrefix,
            $"Обновление качества рендеринга с {_currentState.Quality} на {quality}");
        _currentState = _currentState with { Quality = quality };
        SpectrumRendererFactory.GlobalQuality = quality;
        RequestRender();
    }

    public void RequestRender()
    {
        if (!_isDisposed && _glControl is not null &&
            (!_controller.IsOverlayActive || _glControl != _controller.SpectrumCanvas))
        {
            _glControl.InvalidateVisual();
            if (_isInitialized)
            {
                var renderer = SpectrumRendererFactory.GetCachedRenderer(_currentState.Style);
                renderer?.Configure(_controller.IsOverlayActive, _currentState.Quality);
            }
        }
    }

    public void UpdateRenderDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(
                width <= 0 ? nameof(width) : nameof(height),
                "Размеры должны быть больше нуля");

        _projectionMatrix = Matrix4.CreateOrthographicOffCenter(0, width, height, 0, -1, 1);
        RequestRender();
    }
    #endregion

    #region Утилитарные методы
    private void EnsureNotDisposed()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(
                nameof(Renderer), "Нельзя выполнить операцию на утилизированном рендерере");
    }

    private void LogGpuInfo()
    {
        try
        {
            var openGlVersion = GL.GetString(StringName.Version);
            var vendor = GL.GetString(StringName.Vendor);
            var renderer = GL.GetString(StringName.Renderer);

            if (string.IsNullOrEmpty(openGlVersion) ||
                string.IsNullOrEmpty(vendor) ||
                string.IsNullOrEmpty(renderer))
                throw new InvalidOperationException("Одна или более строк информации о GPU null или пустые");

            SmartLogger.Log(LogLevel.Debug, LogPrefix,
                $"OpenGL: {openGlVersion}, Vendor: {vendor}, Renderer: {renderer}");
        }
        catch (Exception ex)
        {
            SmartLogger.Log(LogLevel.Warning, LogPrefix, $"Ошибка получения информации о GPU: {ex.Message}");
        }
    }
    #endregion

    #region Реализация IDisposable
    public void Dispose()
    {
        if (_isDisposed) return;

        _isDisposed = _isAnalyzerDisposed = true;
        _disposalTokenSource.Cancel();
        _renderTimer?.Stop();
        PerformanceMetricsManager.PerformanceUpdated -= OnPerformanceMetricsUpdated;

        if (_controller is INotifyPropertyChanged notifier)
            notifier.PropertyChanged -= OnControllerPropertyChanged;

        if (_glControl is not null)
        {
            _glControl.Render -= OnGlControlRender;
            _glControl.SizeChanged -= UpdateRenderDimensions;
            _glControl = null;
        }

        try
        {
            _currentState.Paint?.Dispose();
        }
        catch (Exception ex)
        {
            SmartLogger.Log(LogLevel.Error, LogPrefix, $"Ошибка при утилизации: {ex.Message}");
        }

        _renderLock.Dispose();
        _disposalTokenSource.Dispose();
        SmartLogger.Log(LogLevel.Information, LogPrefix, "Renderer утилизирован");
    }
    #endregion
}