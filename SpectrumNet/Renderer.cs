#nullable enable

namespace SpectrumNet
{
    public sealed class Renderer : IDisposable
    {
        #region Константы и типы

        private const int RENDER_TIMEOUT_MS = 16;
        private const string DEFAULT_STYLE = "Solid";
        private const string LogPrefix = "[Renderer] ";

        private readonly record struct RenderState(
            ShaderProgram? Paint,
            RenderStyle Style,
            string StyleName,
            RenderQuality Quality);

        #endregion

        #region Поля

        private readonly SemaphoreSlim _renderLock = new(1, 1);
        private readonly SpectrumBrushes _spectrumStyles;
        private readonly IAudioVisualizationController _controller;
        private readonly ISpectrumAnalyzer _analyzer;
        private readonly CancellationTokenSource _disposalTokenSource = new();
        private readonly IOpenGLService _glService;

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

        #region Свойства и события

        public string CurrentStyleName => _currentState.StyleName;
        public RenderQuality CurrentQuality => _currentState.Quality;
        public IAudioVisualizationController Controller => _controller;
        public event EventHandler<PerformanceMetrics>? PerformanceUpdate;

        public bool ShouldShowPlaceholder
        {
            get => _shouldShowPlaceholder;
            set => _shouldShowPlaceholder = value;
        }

        #endregion

        #region Конструктор

        public Renderer(
            SpectrumBrushes styles,
            IAudioVisualizationController controller,
            ISpectrumAnalyzer analyzer,
            GLWpfControl glControl,
            IOpenGLService glService)
        {
            _spectrumStyles = styles ?? throw new ArgumentNullException(nameof(styles));
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
            _glControl = glControl ?? throw new ArgumentNullException(nameof(glControl));
            _glService = glService ?? throw new ArgumentNullException(nameof(glService));

            if (_analyzer is IComponent comp)
                comp.Disposed += (_, _) => _isAnalyzerDisposed = true;

            _shouldShowPlaceholder = !_controller.IsRecording;
            _glControl.Render += OnGlControlRender;

            SmartLogger.Log(LogLevel.Information, LogPrefix, "Renderer инициализирован");
            PerformanceMetricsManager.PerformanceUpdated += OnPerformanceMetricsUpdated;
        }

        #endregion

        #region Инициализация

        private void InitializeRenderer()
        {
            try
            {
                InitializeDefaultState();
                InitializeRenderTimer();
                SubscribeToEvents();
                SynchronizeWithController();
                RequestRender();
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Ошибка инициализации рендерера: {ex}");
                throw;
            }
        }

        private void InitializeDefaultState()
        {
            var (_, shader) = _spectrumStyles.GetColorAndShader(DEFAULT_STYLE);
            ShaderProgram? clonedShader = shader?.Clone() ??
                throw new InvalidOperationException($"{LogPrefix}Не удалось инициализировать стиль {DEFAULT_STYLE}");

            _currentState = new RenderState(clonedShader, RenderStyle.Bars, DEFAULT_STYLE, RenderQuality.Medium);
        }

        private void InitializeRenderTimer()
        {
            _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RENDER_TIMEOUT_MS) };
            _renderTimer.Tick += (_, _) => RequestRender();
            _renderTimer.Start();
        }

        private void SubscribeToEvents()
        {
            if (_glControl is not null)
                _glControl.SizeChanged += UpdateRenderDimensions;

            _controller.PropertyChanged += OnControllerPropertyChanged;
        }

        private void InitializeOpenGLResources()
        {
            try
            {
                _currentState.Paint?.Dispose();
                _glService.Finish();

                var (_, shader) = _spectrumStyles.GetColorAndShader(DEFAULT_STYLE);
                ShaderProgram? clonedShader = shader?.Clone() ??
                    throw new InvalidOperationException("Клонирование шейдера не удалось при инициализации ресурсов OpenGL.");

                _currentState = _currentState with { Paint = clonedShader };
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Ошибка инициализации ресурсов OpenGL: {ex}");
                throw;
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
                    HandleRecordingChanged();
                    break;
                case nameof(IAudioVisualizationController.IsOverlayActive):
                    HandleOverlayActiveChanged();
                    break;
                case nameof(IAudioVisualizationController.SelectedDrawingType):
                    UpdateRenderStyle(_controller.SelectedDrawingType);
                    break;
                case nameof(IAudioVisualizationController.SelectedStyle):
                    HandleStyleChanged();
                    break;
                case nameof(IAudioVisualizationController.RenderQuality):
                    UpdateRenderQuality(_controller.RenderQuality);
                    break;
                case nameof(IAudioVisualizationController.WindowType):
                case nameof(IAudioVisualizationController.ScaleType):
                    HandleAnalyzerSettingsChanged();
                    break;
                case nameof(IAudioVisualizationController.BarSpacing):
                case nameof(IAudioVisualizationController.BarCount):
                    RequestRender();
                    break;
            }
        }

        private void HandleRecordingChanged()
        {
            _shouldShowPlaceholder = !_controller.IsRecording;
            RequestRender();
        }

        private void HandleOverlayActiveChanged()
        {
            SpectrumRendererFactory.ConfigureAllRenderers(_controller.IsOverlayActive);
            RequestRender();
        }

        private void HandleStyleChanged()
        {
            if (!string.IsNullOrEmpty(_controller.SelectedStyle))
            {
                var (color, shader) = _spectrumStyles.GetColorAndShader(_controller.SelectedStyle);
                UpdateSpectrumStyle(_controller.SelectedStyle, color, shader);
            }
        }

        private void HandleAnalyzerSettingsChanged()
        {
            _analyzer.UpdateSettings(_controller.WindowType, _controller.ScaleType);
            SynchronizeWithController();
            RequestRender();
        }

        private void UpdateRenderDimensions(object? sender, SizeChangedEventArgs e)
        {
            if (_isDisposed || _glControl is null || !_glControl.IsInitialized)
                return;

            try
            {
                int newWidth = (int)e.NewSize.Width;
                int newHeight = (int)e.NewSize.Height;
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

                ApplyPendingStyleIfNeeded();
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

        private void ApplyPendingStyleIfNeeded()
        {
            if (_pendingStyleName is null || _pendingShader is null)
                return;

            try
            {
                var clonedShader = _pendingShader.Clone() ??
                    throw new ArgumentException($"Не удалось клонировать шейдер для стиля: {_pendingStyleName}");

                ShaderProgram? oldShader = _currentState.Paint;
                _currentState = _currentState with { Paint = clonedShader, StyleName = _pendingStyleName };
                oldShader?.Dispose();
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

        private void RenderFrameInternal(TimeSpan delta)
        {
            if (!ValidateRenderState())
                return;

            if (!_isInitialized)
                InitializeOpenGLIfNeeded();

            LogGpuInfoOnce();
            PrepareRenderSurface();

            if (ShouldRenderPlaceholder())
            {
                RenderPlaceholder();
                return;
            }

            RenderSpectrum();
        }

        private bool ValidateRenderState()
        {
            if (_glControl is null || !_glControl.IsInitialized ||
                _glControl.ActualWidth <= 0 || _glControl.ActualHeight <= 0)
            {
                _isInitialized = false;
                return false;
            }
            return true;
        }

        private void InitializeOpenGLIfNeeded()
        {
            if (!_isInitialized)
            {
                InitializeOpenGLResources();
                _isInitialized = true;
            }
        }

        private void LogGpuInfoOnce()
        {
            if (!_isGpuInfoLogged)
            {
                LogGpuInfo();
                _isGpuInfoLogged = true;
            }
        }

        private void PrepareRenderSurface()
        {
            if (_glControl is null)
                return;

            _glService.Viewport(0, 0, (int)_glControl.ActualWidth, (int)_glControl.ActualHeight);
            _glService.ClearColor(0.0f, 0.0f, 0.0f, 0.0f);
            _glService.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            if (_projectionMatrix == default)
                _projectionMatrix = Matrix4.CreateOrthographicOffCenter(0, (int)_glControl.ActualWidth, (int)_glControl.ActualHeight, 0, -1, 1);
        }

        private bool ShouldRenderPlaceholder()
        {
            return _shouldShowPlaceholder ||
                   _controller.IsTransitioning ||
                   _analyzer is null ||
                   _isAnalyzerDisposed ||
                   !_controller.IsRecording ||
                   _currentState.Paint is null;
        }

        private void RenderSpectrum()
        {
            var spectrum = GetSpectrumData();
            if (spectrum is null || spectrum.Spectrum.Length == 0)
            {
                RenderPlaceholder();
                return;
            }

            if (!TryCalcRenderParams(out float barWidth, out float barSpacing, out int barCount))
            {
                RenderPlaceholder();
                return;
            }

            var renderer = GetOrCreateRenderer();
            if (renderer is null)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Не удалось получить рендерер для стиля: {_currentState.Style}");
                RenderPlaceholder();
                return;
            }

            var viewport = new Viewport(0, 0, (int)_glControl!.ActualWidth, (int)_glControl.ActualHeight);
            renderer.Render(
                spectrum.Spectrum,
                viewport,
                barWidth,
                barSpacing,
                barCount,
                _currentState.Paint,
                DrawPerformanceInfoIfNeeded
            );

            NotifyUIThreadAboutRenderingState();
        }

        private ISpectrumRenderer? GetOrCreateRenderer()
        {
            var renderer = SpectrumRendererFactory.GetCachedRenderer(_currentState.Style);
            if (renderer == null)
            {
                renderer = SpectrumRendererFactory.CreateRenderer(
                    _currentState.Style,
                    _controller.IsOverlayActive,
                    _currentState.Quality
                );
            }
            else
            {
                renderer.Configure(_controller.IsOverlayActive, _currentState.Quality);
            }
            return renderer;
        }

        private void DrawPerformanceInfoIfNeeded(Viewport viewport)
        {
            if (_controller.ShowPerformanceInfo)
                PerformanceMetricsManager.DrawPerformanceInfo(viewport, _controller.ShowPerformanceInfo);
        }

        private void NotifyUIThreadAboutRenderingState()
        {
            _controller.Dispatcher.Invoke(() =>
            {
                _controller.OnPropertyChanged(
                    nameof(IAudioVisualizationController.IsRecording),
                    nameof(IAudioVisualizationController.CanStartCapture)
                );
            });
        }

        private void RenderPlaceholder()
        {
            _glService.ClearColor(1.0f, 0.1f, 1.0f, 1.0f);
            _glService.Clear(ClearBufferMask.ColorBufferBit);
        }

        private SpectralData? GetSpectrumData()
        {
            try
            {
                return _analyzer?.GetCurrentSpectrum();
            }
            catch (ObjectDisposedException)
            {
                _isAnalyzerDisposed = true;
                _shouldShowPlaceholder = true;
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
            barSpacing = barCount > 1 ? (totalWidth - barCount * barWidth) / (barCount - 1) : 0;
            return true;
        }

        #endregion

        #region Конфигурация и синхронизация

        public void SynchronizeWithController()
        {
            EnsureNotDisposed();
            try
            {
                bool needsUpdate = false;

                if (_currentState.Style != _controller.SelectedDrawingType)
                {
                    UpdateRenderStyle(_controller.SelectedDrawingType);
                    needsUpdate = true;
                }

                if (_controller.SelectedStyle != _currentState.StyleName)
                {
                    var (color, shader) = _spectrumStyles.GetColorAndShader(_controller.SelectedStyle);
                    UpdateSpectrumStyle(_controller.SelectedStyle, color, shader);
                    needsUpdate = true;
                }

                if (_currentState.Quality != _controller.RenderQuality)
                {
                    UpdateRenderQuality(_controller.RenderQuality);
                    needsUpdate = true;
                }

                if (needsUpdate)
                    RequestRender();
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Ошибка синхронизации: {ex.Message}");
            }
        }

        public void UpdateRenderStyle(RenderStyle style)
        {
            EnsureNotDisposed();
            if (_currentState.Style == style)
                return;

            SmartLogger.Log(LogLevel.Information, LogPrefix, $"Обновление стиля рендеринга на {style}");
            _currentState = _currentState with { Style = style };

            ConfigureRendererForCurrentStyle();
            RequestRender();
        }

        private void ConfigureRendererForCurrentStyle()
        {
            try
            {
                var renderer = SpectrumRendererFactory.GetCachedRenderer(_currentState.Style) ??
                               SpectrumRendererFactory.CreateRenderer(
                                   _currentState.Style,
                                   _controller.IsOverlayActive,
                                   _currentState.Quality
                               );
                renderer?.Configure(_controller.IsOverlayActive, _currentState.Quality);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Не удалось получить рендерер для стиля {_currentState.Style}: {ex.Message}");
            }
        }

        public void UpdateSpectrumStyle(string styleName, Color4 color, ShaderProgram? shader)
        {
            EnsureNotDisposed();
            if (string.IsNullOrEmpty(styleName))
                throw new ArgumentException("Имя стиля не может быть null или пустым", nameof(styleName));

            if (styleName == _currentState.StyleName)
                return;

            _pendingStyleName = styleName;
            _pendingColor = color;
            _pendingShader = shader;
            RequestRender();
        }

        public void UpdateRenderQuality(RenderQuality quality)
        {
            EnsureNotDisposed();
            if (_currentState.Quality == quality)
                return;

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
                throw new ArgumentOutOfRangeException(width <= 0 ? nameof(width) : nameof(height), "Размеры должны быть больше нуля");

            _projectionMatrix = Matrix4.CreateOrthographicOffCenter(0, width, height, 0, -1, 1);
            RequestRender();
        }

        #endregion

        #region Утилитарные методы и Dispose

        private void EnsureNotDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(Renderer), "Нельзя выполнить операцию на утилизированном рендерере");
        }

        private void LogGpuInfo()
        {
            try
            {
                var openGlVersion = _glService.GetString(StringName.Version);
                var vendor = _glService.GetString(StringName.Vendor);
                var rendererStr = _glService.GetString(StringName.Renderer);

                if (string.IsNullOrEmpty(openGlVersion) ||
                    string.IsNullOrEmpty(vendor) ||
                    string.IsNullOrEmpty(rendererStr))
                    throw new InvalidOperationException("Одна или более строк информации о GPU null или пустые");

                SmartLogger.Log(LogLevel.Information, LogPrefix, $"OpenGL: {openGlVersion}, Vendor: {vendor}, Renderer: {rendererStr}");
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Warning, LogPrefix, $"Ошибка получения информации о GPU: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

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
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Ошибка при утилизации шейдера: {ex.Message}");
            }

            _renderLock.Dispose();
            _disposalTokenSource.Dispose();

            SmartLogger.Log(LogLevel.Information, LogPrefix, "Renderer утилизирован");
        }

        #endregion
    }
}