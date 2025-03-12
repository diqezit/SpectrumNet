#nullable enable

namespace SpectrumNet
{
    public partial class Renderer : IRenderer, IDisposable
    {
        private const int RENDER_TIMEOUT_MS = 16;
        private const string DEFAULT_STYLE = "Solid";
        private const string LogPrefix = "Renderer";

        private readonly record struct RenderState(
            ShaderProgram? Paint,
            RenderStyle Style,
            string StyleName,
            RenderQuality Quality);

        private readonly SemaphoreSlim _renderLock = new(1, 1);
        private readonly SpectrumBrushes _spectrumStyles;
        private readonly IAudioVisualizationController _controller;
        private readonly ISpectrumAnalyzer _analyzer;
        private readonly CancellationTokenSource _disposalTokenSource = new();
        private readonly IOpenGLService _glService;

        private SpectrumPlaceholder? _placeholder;
        private string? _pendingStyleName;
        private Color4 _pendingColor;
        private ShaderProgram? _pendingShader;
        private GLWpfControl? _glControl;
        private DispatcherTimer? _renderTimer, _resizeTimer;
        private Matrix4 _projectionMatrix;
        private RenderState _currentState;
        private volatile bool _isDisposed, _isAnalyzerDisposed, _shouldShowPlaceholder = true,
                            _isInitialized, _isGpuInfoLogged, _isResizing;

        public string CurrentStyleName => _currentState.StyleName;
        public RenderQuality CurrentQuality => _currentState.Quality;
        public IAudioVisualizationController Controller => _controller;
        public event EventHandler<PerformanceMetrics>? PerformanceUpdate;
        public event EventHandler? RenderRequested;

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
            _placeholder = new SpectrumPlaceholder(_glService);

            if (_analyzer is IComponent comp)
                comp.Disposed += OnAnalyzerDisposed;

            _shouldShowPlaceholder = !_controller.IsRecording;
            _glControl.Render += OnGlControlRender;
            _glControl.SizeChanged += OnGlControlSizeChanged;

            SmartLogger.Log(LogLevel.Information, LogPrefix, "Renderer initialized");
            PerformanceMetricsManager.PerformanceUpdated += OnPerformanceMetricsUpdated;
        }

        private void OnAnalyzerDisposed(object? sender, EventArgs e) => _isAnalyzerDisposed = true;

        #region Initialization

        private void InitializeRenderer() =>
            SmartLogger.Safe(() => {
                InitializeDefaultState();
                InitializeRenderTimer();
                SubscribeToEvents();
                SynchronizeWithController();
                RequestRender();
            }, LogPrefix, "Renderer initialization error");

        private void InitializeDefaultState()
        {
            var (_, shader) = _spectrumStyles.GetColorAndShader(DEFAULT_STYLE);
            ShaderProgram? clonedShader = shader?.Clone() ??
                throw new InvalidOperationException($"{LogPrefix} Failed to initialize style {DEFAULT_STYLE}");
            _currentState = new RenderState(clonedShader, RenderStyle.Bars, DEFAULT_STYLE, RenderQuality.Medium);
        }

        private void InitializeRenderTimer()
        {
            _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RENDER_TIMEOUT_MS) };
            _renderTimer.Tick += OnRenderTimerTick;
            _renderTimer.Start();
        }

        private void OnRenderTimerTick(object? sender, EventArgs e) => RequestRender();

        private void SubscribeToEvents() => _controller.PropertyChanged += OnControllerPropertyChanged;

        private void InitializeOpenGLResources() =>
            SmartLogger.Safe(() => {
                _currentState.Paint?.Dispose();
                _glService.Finish();

                var (_, shader) = _spectrumStyles.GetColorAndShader(DEFAULT_STYLE);
                ShaderProgram? clonedShader = shader?.Clone() ??
                    throw new InvalidOperationException("Shader cloning failed during OpenGL resources initialization.");
                _currentState = _currentState with { Paint = clonedShader };
            }, LogPrefix, "OpenGL resources initialization error");

        #endregion

        #region Event Handlers

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

        private void OnGlControlSizeChanged(object? sender, System.Windows.SizeChangedEventArgs e)
        {
            if (_isDisposed || _glControl is null || !_glControl.IsInitialized)
                return;

            SmartLogger.Safe(() => {
                int newWidth = (int)e.NewSize.Width;
                int newHeight = (int)e.NewSize.Height;
                if (newWidth <= 0 || newHeight <= 0) return;

                _projectionMatrix = Matrix4.CreateOrthographicOffCenter(0, newWidth, newHeight, 0, -1, 1);
                _placeholder?.UpdateDimensions(newWidth, newHeight);
                RequestRender();
            }, LogPrefix, "Error updating dimensions");

            HandleResizing();
        }

        private void HandleResizing()
        {
            if (_resizeTimer == null)
            {
                _resizeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _resizeTimer.Tick += OnResizeTimerTick;
            }
            else
            {
                _resizeTimer.Stop();
            }

            _resizeTimer.Start();
            _isResizing = true;
        }

        private void OnResizeTimerTick(object? sender, EventArgs e)
        {
            _resizeTimer?.Stop();
            _isResizing = false;
            RequestRender();
        }

        #endregion

        #region Rendering

        public void OnGlControlRender(TimeSpan delta)
        {
            if (_isDisposed || !_renderLock.Wait(0)) return;

            try
            {
                if (_glControl == null || !_glControl.IsInitialized) return;

                if (!_isInitialized)
                {
                    InitializeRenderer();
                    _isInitialized = true;
                }

                ApplyPendingStyleIfNeeded();
                RenderFrameInternal(delta);
            }
            finally
            {
                _renderLock.Release();
            }
        }

        private void ApplyPendingStyleIfNeeded()
        {
            if (_pendingStyleName is null || _pendingShader is null) return;

            ShaderProgram? oldShader = _currentState.Paint;
            SmartLogger.Safe(() => {
                var clonedShader = _pendingShader.Clone() ??
                    throw new ArgumentException($"Failed to clone shader for style: {_pendingStyleName}");
                _currentState = _currentState with { Paint = clonedShader, StyleName = _pendingStyleName };
                oldShader?.Dispose();
            }, LogPrefix, "Error switching style");

            _pendingStyleName = null;
            _pendingShader = null;
        }

        private void RenderFrameInternal(TimeSpan delta)
        {
            if (!ValidateRenderState()) return;
            if (!_isInitialized) InitializeOpenGLResources();

            if (!_isGpuInfoLogged)
            {
                LogGpuInfo();
                _isGpuInfoLogged = true;
            }

            PrepareRenderSurface();

            if (ShouldRenderPlaceholder())
            {
                RenderPlaceholder();
                return;
            }

            RenderSpectrum();
        }

        private bool ValidateRenderState() =>
            _glControl != null && _glControl.IsInitialized &&
            _glControl.ActualWidth > 0 && _glControl.ActualHeight > 0;

        private void PrepareRenderSurface()
        {
            if (_glControl == null) return;

            _glService.Viewport(0, 0, (int)_glControl.ActualWidth, (int)_glControl.ActualHeight);
            _glService.ClearColor(0.0f, 0.0f, 0.0f, 0.0f);
            _glService.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            if (_projectionMatrix == default)
                _projectionMatrix = Matrix4.CreateOrthographicOffCenter(
                    0, (int)_glControl.ActualWidth, (int)_glControl.ActualHeight, 0, -1, 1);
        }

        private bool ShouldRenderPlaceholder() =>
            _shouldShowPlaceholder || _controller.IsTransitioning || _analyzer is null ||
            _isAnalyzerDisposed || !_controller.IsRecording || _currentState.Paint is null || _isResizing;

        private void RenderSpectrum()
        {
            var spectrum = SmartLogger.Safe(() => _analyzer?.GetCurrentSpectrum(), defaultValue: null);
            if (spectrum is null || spectrum.Spectrum.Length == 0)
            {
                RenderPlaceholder();
                return;
            }

            if (!CalculateRenderParameters(out float barWidth, out float barSpacing, out int barCount))
            {
                RenderPlaceholder();
                return;
            }

            var renderer = GetOrCreateRenderer();
            if (renderer is null)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Failed to get renderer for style: {_currentState.Style}");
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
                DrawPerformanceInfo
            );

            UpdateControllerState();
        }

        private void DrawPerformanceInfo(Viewport viewport)
        {
            if (_controller.ShowPerformanceInfo)
                PerformanceMetricsManager.DrawPerformanceInfo(viewport, _controller.ShowPerformanceInfo);
        }

        private void UpdateControllerState() =>
            _controller.Dispatcher.Invoke(() => _controller.OnPropertyChanged(
                nameof(IAudioVisualizationController.IsRecording),
                nameof(IAudioVisualizationController.CanStartCapture)));

        private ISpectrumRenderer? GetOrCreateRenderer()
        {
            var renderer = SpectrumRendererFactory.GetCachedRenderer(_currentState.Style) ??
                           SpectrumRendererFactory.CreateRenderer(
                               _currentState.Style,
                               _controller.IsOverlayActive,
                               _currentState.Quality);

            renderer?.Configure(_controller.IsOverlayActive, _currentState.Quality);
            return renderer;
        }

        private void RenderPlaceholder()
        {
            if (_glControl == null) return;

            _placeholder ??= new SpectrumPlaceholder(_glService);
            _placeholder.UpdateDimensions((float)_glControl.ActualWidth, (float)_glControl.ActualHeight);
            _placeholder.Render();
        }

        public bool CalculateRenderParameters(out float barWidth, out float barSpacing, out int barCount)
        {
            barWidth = barSpacing = 0;
            barCount = _controller.BarCount;
            int totalWidth = _glControl is not null ? (int)_glControl.ActualWidth : 0;

            if (totalWidth <= 0 || barCount <= 0) return false;

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

        #region Configuration and Synchronization

        public void SynchronizeWithController()
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(Renderer));

            SmartLogger.Safe(() => {
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

                if (needsUpdate) RequestRender();
            }, LogPrefix, "Synchronization error");
        }

        public void UpdateRenderStyle(RenderStyle style)
        {
            if (_isDisposed || _currentState.Style == style) return;

            SmartLogger.Log(LogLevel.Information, LogPrefix, $"Updating render style to {style}");
            _currentState = _currentState with { Style = style };

            SmartLogger.Safe(() => {
                var renderer = SpectrumRendererFactory.GetCachedRenderer(_currentState.Style) ??
                               SpectrumRendererFactory.CreateRenderer(
                                   _currentState.Style,
                                   _controller.IsOverlayActive,
                                   _currentState.Quality);
                renderer?.Configure(_controller.IsOverlayActive, _currentState.Quality);
            }, LogPrefix, $"Failed to get renderer for style {_currentState.Style}");

            RequestRender();
        }

        public void UpdateSpectrumStyle(string styleName, Color4 color, ShaderProgram? shader)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(Renderer));
            if (string.IsNullOrEmpty(styleName))
                throw new ArgumentException("Style name cannot be null or empty", nameof(styleName));
            if (styleName == _currentState.StyleName) return;

            _pendingStyleName = styleName;
            _pendingColor = color;
            _pendingShader = shader;
            RequestRender();
        }

        public void UpdateRenderQuality(RenderQuality quality)
        {
            if (_isDisposed || _currentState.Quality == quality) return;

            _currentState = _currentState with { Quality = quality };
            SpectrumRendererFactory.GlobalQuality = quality;
            RequestRender();
        }

        public void RequestRender()
        {
            if (_isDisposed || _glControl is null ||
                (_controller.IsOverlayActive && _glControl == _controller.SpectrumCanvas)) return;

            _glControl.InvalidateVisual();

            if (_isInitialized)
            {
                var renderer = SpectrumRendererFactory.GetCachedRenderer(_currentState.Style);
                renderer?.Configure(_controller.IsOverlayActive, _currentState.Quality);
            }

            RenderRequested?.Invoke(this, EventArgs.Empty);
        }

        public void UpdateRenderDimensions(int width, int height)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(
                    width <= 0 ? nameof(width) : nameof(height),
                    "Dimensions must be greater than zero");

            _projectionMatrix = Matrix4.CreateOrthographicOffCenter(0, width, height, 0, -1, 1);
            _placeholder?.UpdateDimensions(width, height);
            RequestRender();
        }

        #endregion

        #region Utility Methods and Dispose

        private void LogGpuInfo() =>
            SmartLogger.Safe(() => {
                var openGlVersion = _glService.GetString(StringName.Version);
                var vendor = _glService.GetString(StringName.Vendor);
                var rendererStr = _glService.GetString(StringName.Renderer);

                if (string.IsNullOrEmpty(openGlVersion) ||
                    string.IsNullOrEmpty(vendor) ||
                    string.IsNullOrEmpty(rendererStr))
                    throw new InvalidOperationException("One or more GPU info strings are null or empty");

                SmartLogger.Log(LogLevel.Information, LogPrefix,
                    $"OpenGL: {openGlVersion}, Vendor: {vendor}, Renderer: {rendererStr}");
            }, LogPrefix, "Error obtaining GPU info");

        public void Dispose()
        {
            if (_isDisposed) return;

            _isDisposed = _isAnalyzerDisposed = true;
            _disposalTokenSource.Cancel();
            _renderTimer?.Stop();
            _resizeTimer?.Stop();

            PerformanceMetricsManager.PerformanceUpdated -= OnPerformanceMetricsUpdated;

            if (_controller is INotifyPropertyChanged notifier)
                notifier.PropertyChanged -= OnControllerPropertyChanged;

            if (_glControl is not null)
            {
                _glControl.Render -= OnGlControlRender;
                _glControl.SizeChanged -= OnGlControlSizeChanged;
                _glControl = null;
            }

            SmartLogger.Safe(() => _currentState.Paint?.Dispose(), LogPrefix, "Error disposing paint resources");
            SmartLogger.Safe(() => _placeholder?.Dispose(), LogPrefix, "Error disposing placeholder");
            _placeholder = null;

            SmartLogger.Safe(() => _renderLock?.Dispose(), LogPrefix, "Error disposing render lock");
            SmartLogger.Safe(() => _disposalTokenSource?.Dispose(), LogPrefix, "Error disposing token source");

            SmartLogger.Log(LogLevel.Information, LogPrefix, "Renderer disposed");
        }

        #endregion
    }
}