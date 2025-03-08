#nullable enable

using OpenGLErrorCode = OpenTK.Graphics.OpenGL.ErrorCode;

namespace SpectrumNet
{
    public sealed class Renderer : IDisposable
    {
        const int RENDER_TIMEOUT_MS = 16;
        const string DEFAULT_STYLE = "Solid";
        const string MESSAGE = "Push start to begin record...";
        const string LogPrefix = "[Renderer] ";

        record struct RenderState(ShaderProgram Paint, RenderStyle Style, string StyleName, RenderQuality Quality);

        readonly SemaphoreSlim _renderLock = new(1, 1);
        readonly SpectrumBrushes _spectrumStyles;
        readonly IAudioVisualizationController _controller;
        readonly SpectrumAnalyzer _analyzer;
        readonly CancellationTokenSource _disposalTokenSource = new();

        GLWpfControl? _glControl;
        DispatcherTimer? _renderTimer;
        Matrix4 _projectionMatrix;
        RenderState _currentState;
        volatile bool _isDisposed, _isAnalyzerDisposed;
        volatile bool _shouldShowPlaceholder = true;
        private TextRenderer? _textRenderer;

        public string CurrentStyleName => _currentState.StyleName;
        public RenderQuality CurrentQuality => _currentState.Quality;
        public event EventHandler<PerformanceMetrics>? PerformanceUpdate;

        public bool ShouldShowPlaceholder
        {
            get => _shouldShowPlaceholder;
            set => _shouldShowPlaceholder = value;
        }

        public Renderer(SpectrumBrushes styles, IAudioVisualizationController controller, SpectrumAnalyzer analyzer, GLWpfControl glControl)
        {
            _spectrumStyles = styles ?? throw new ArgumentNullException(nameof(styles));
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
            _glControl = glControl ?? throw new ArgumentNullException(nameof(glControl));

            if (_analyzer is IComponent comp)
                comp.Disposed += (_, _) => _isAnalyzerDisposed = true;

            _shouldShowPlaceholder = !_controller.IsRecording;

            try
            {
                InitializeRenderer();

                // Adding OpenGL diagnostic log
                SmartLogger.Log(LogLevel.Debug, LogPrefix, "OpenGL initialized with version: " + GL.GetString(StringName.Version), forceLog: true);
                SmartLogger.Log(LogLevel.Debug, LogPrefix, "OpenGL vendor: " + GL.GetString(StringName.Vendor), forceLog: true);
                SmartLogger.Log(LogLevel.Debug, LogPrefix, "OpenGL renderer: " + GL.GetString(StringName.Renderer), forceLog: true);

                // Check for OpenGL errors after initialization
                CheckOpenGLErrors("Initialization");

                SmartLogger.Log(LogLevel.Information, LogPrefix, "Successfully initialized.", forceLog: true);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Failed to initialize renderer: {ex}", forceLog: true);
                throw;
            }

            PerformanceMetricsManager.PerformanceUpdated += OnPerformanceMetricsUpdated;
        }

        void InitializeRenderer()
        {
            SmartLogger.Log(LogLevel.Debug, LogPrefix, "Starting renderer initialization", forceLog: true);

            var (color, shader) = _spectrumStyles.GetColorAndShader(DEFAULT_STYLE);

            SmartLogger.Log(LogLevel.Debug, LogPrefix, $"Loading {DEFAULT_STYLE} shader...", forceLog: true);
            var clonedShader = shader.Clone();
            if (clonedShader == null)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Failed to clone {DEFAULT_STYLE} shader", forceLog: true);
                throw new InvalidOperationException($"{LogPrefix}Failed to initialize {DEFAULT_STYLE} style");
            }

            _currentState = new RenderState(
                clonedShader,
                RenderStyle.Bars,
                DEFAULT_STYLE,
                RenderQuality.Medium);

            // Создаем рендерер текста
            try
            {
                SmartLogger.Log(LogLevel.Debug, LogPrefix, "Initializing text renderer...", forceLog: true);
                _textRenderer = new TextRenderer();
                SmartLogger.Log(LogLevel.Debug, LogPrefix, "Text renderer initialized successfully", forceLog: true);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Failed to initialize text renderer: {ex}", forceLog: true);
                // Continue without text renderer - we'll handle this in RenderPlaceholder
            }

            // Настройка таймера обновления
            _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RENDER_TIMEOUT_MS) };
            _renderTimer.Tick += (_, _) => RequestRender();
            _renderTimer.Start();
            SmartLogger.Log(LogLevel.Debug, LogPrefix, $"Render timer started with interval {RENDER_TIMEOUT_MS}ms", forceLog: true);

            if (_glControl != null)
            {
                _glControl.SizeChanged += UpdateRenderDimensions;
                _glControl.Render += RenderFrame;
                SmartLogger.Log(LogLevel.Debug, LogPrefix, $"GL control initialized with dimensions: {_glControl.ActualWidth}x{_glControl.ActualHeight}", forceLog: true);
            }
            else
            {
                SmartLogger.Log(LogLevel.Warning, LogPrefix, "GL control is null during initialization", forceLog: true);
            }

            _controller.PropertyChanged += OnControllerPropertyChanged;
            SynchronizeWithController();

            SmartLogger.Log(LogLevel.Debug, LogPrefix, "Renderer initialization completed", forceLog: true);
        }

        private void OnPerformanceMetricsUpdated(object? sender, PerformanceMetrics metrics)
        {
            PerformanceUpdate?.Invoke(this, metrics);
        }

        void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e == null) return;

            SmartLogger.Log(LogLevel.Debug, LogPrefix, $"Controller property changed: {e.PropertyName}", forceLog: true);

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

        private void UpdateRenderDimensions(object? sender, SizeChangedEventArgs e)
        {
            if (e.NewSize.Width > 0 && e.NewSize.Height > 0)
            {
                _projectionMatrix = Matrix4.CreateOrthographicOffCenter(
                    0, (float)e.NewSize.Width, (float)e.NewSize.Height, 0, -1, 1);
                SmartLogger.Log(LogLevel.Debug, LogPrefix, $"Projection matrix updated for dimensions: {e.NewSize.Width}x{e.NewSize.Height}", forceLog: true);
                RequestRender();
            }
            else
            {
                SmartLogger.Log(LogLevel.Warning, LogPrefix, $"Invalid dimensions in UpdateRenderDimensions: {e.NewSize.Width}x{e.NewSize.Height}", forceLog: true);
            }
        }

        // Этот метод вызывается при каждом кадре рендеринга GLWpfControl
        public void RenderFrame(TimeSpan delta)
        {
            if (_isDisposed || !_renderLock.Wait(0))
                return;

            try
            {
                RenderFrameInternal();

                // Check OpenGL errors after each frame
                CheckOpenGLErrors("Frame Rendering");
            }
            catch (ObjectDisposedException ex)
            {
                SmartLogger.Log(LogLevel.Warning, LogPrefix, $"Object disposed during render: {ex.Message}", forceLog: true);
                _isAnalyzerDisposed = true;
                _renderTimer?.Stop();
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error rendering frame: {ex}", forceLog: true);
                RenderPlaceholder();
            }
            finally
            {
                _renderLock.Release();
            }
        }

        void RenderFrameInternal()
        {
            if (_glControl == null)
            {
                SmartLogger.Log(LogLevel.Warning, LogPrefix, "GLControl is null during render", forceLog: true);
                return;
            }

            // Настройка OpenGL
            GL.Viewport(0, 0, (int)_glControl.ActualWidth, (int)_glControl.ActualHeight);
            GL.ClearColor(0, 0, 0, 0);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            // Настраиваем проекционную матрицу, если еще не настроена
            if (_projectionMatrix == default)
            {
                _projectionMatrix = Matrix4.CreateOrthographicOffCenter(
                    0, (int)_glControl.ActualWidth, (int)_glControl.ActualHeight, 0, -1, 1);
                SmartLogger.Log(LogLevel.Debug, LogPrefix, "Projection matrix initialized", forceLog: true);
            }

            if (_controller.IsTransitioning || ShouldRenderPlaceholder())
            {
                RenderPlaceholder();
                SmartLogger.Log(LogLevel.Debug, LogPrefix, "Rendered placeholder instead of spectrum", forceLog: true);
                return;
            }

            var spectrum = GetSpectrumData();
            if (spectrum == null)
            {
                if (!_controller.IsRecording)
                {
                    RenderPlaceholder();
                    SmartLogger.Log(LogLevel.Debug, LogPrefix, "No spectrum data available, rendered placeholder", forceLog: true);
                }
                return;
            }

            if (!TryCalcRenderParams(out float barWidth, out float barSpacing, out int barCount))
            {
                RenderPlaceholder();
                SmartLogger.Log(LogLevel.Warning, LogPrefix, "Failed to calculate render parameters", forceLog: true);
                return;
            }

            SmartLogger.Log(LogLevel.Debug, LogPrefix, $"Rendering spectrum with barWidth={barWidth}, barSpacing={barSpacing}, barCount={barCount}", forceLog: true);
            RenderSpectrum(spectrum, barWidth, barSpacing, barCount);
        }

        bool ShouldRenderPlaceholder() =>
            _shouldShowPlaceholder || _isAnalyzerDisposed || _analyzer == null || !_controller.IsRecording;

        SpectralData? GetSpectrumData()
        {
            try
            {
                var spectrum = _analyzer?.GetCurrentSpectrum();
                if (spectrum != null)
                {
                    SmartLogger.Log(LogLevel.Debug, LogPrefix, $"Got spectrum data, length={spectrum.Spectrum.Length}", forceLog: true);
                }
                else
                {
                    SmartLogger.Log(LogLevel.Debug, LogPrefix, "GetCurrentSpectrum returned null", forceLog: true);
                }
                return spectrum;
            }
            catch (ObjectDisposedException ex)
            {
                SmartLogger.Log(LogLevel.Warning, LogPrefix, $"Analyzer was disposed: {ex.Message}", forceLog: true);
                _isAnalyzerDisposed = true;
                _shouldShowPlaceholder = true;
                return null;
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error getting spectrum data: {ex}", forceLog: true);
                return null;
            }
        }

        void RenderSpectrum(SpectralData spectrum, float barWidth, float barSpacing, int barCount)
        {
            if (spectrum == null || _glControl == null)
            {
                SmartLogger.Log(LogLevel.Warning, LogPrefix, "Cannot render spectrum: spectrum or GLControl is null", forceLog: true);
                return;
            }

            try
            {
                var renderer = SpectrumRendererFactory.CreateRenderer(
                    _currentState.Style,
                    _controller.IsOverlayActive,
                    _currentState.Quality);

                var viewport = new Viewport(0, 0, (int)_glControl.ActualWidth, (int)_glControl.ActualHeight);

                SmartLogger.Log(LogLevel.Debug, LogPrefix, $"Rendering with style={_currentState.Style}, quality={_currentState.Quality}", forceLog: true);

                renderer.Render(
                    spectrum.Spectrum,
                    viewport,
                    barWidth,
                    barSpacing,
                    barCount,
                    _currentState.Paint,
                    (v) => PerformanceMetricsManager.DrawPerformanceInfo(v, _controller.ShowPerformanceInfo));

                // Check for OpenGL errors after rendering spectrum
                CheckOpenGLErrors("Spectrum Rendering");
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Exception during spectrum rendering: {ex}", forceLog: true);
                // Continue execution - we'll catch this in the outer try-catch
                throw;
            }
        }

        bool TryCalcRenderParams(out float barWidth, out float barSpacing, out int barCount)
        {
            barWidth = barSpacing = 0;
            barCount = _controller.BarCount;
            int totalWidth = _glControl != null ? (int)_glControl.ActualWidth : 0;

            if (totalWidth <= 0 || barCount <= 0)
            {
                SmartLogger.Log(LogLevel.Warning, LogPrefix, $"Invalid render parameters: totalWidth={totalWidth}, barCount={barCount}", forceLog: true);
                return false;
            }

            barSpacing = Math.Min((float)_controller.BarSpacing, totalWidth / (barCount + 1));
            barWidth = Math.Max((totalWidth - (barCount - 1) * barSpacing) / barCount, 1.0f);
            barSpacing = barCount > 1 ? (totalWidth - barCount * barWidth) / (barCount - 1) : 0;
            return true;
        }

        void RenderPlaceholder()
        {
            if (_glControl == null)
            {
                SmartLogger.Log(LogLevel.Warning, LogPrefix, "Cannot render placeholder: GLControl is null", forceLog: true);
                return;
            }

            try
            {
                // Save OpenGL state
                GL.PushAttrib(AttribMask.AllAttribBits);
                GL.PushMatrix();

                if (_textRenderer != null)
                {
                    // Set up projection matrix for text rendering
                    Matrix4 textProjection = Matrix4.CreateOrthographicOffCenter(
                        0, (int)_glControl.ActualWidth, (int)_glControl.ActualHeight, 0, -1, 1);

                    GL.MatrixMode(MatrixMode.Projection);
                    GL.LoadMatrix(ref textProjection);

                    GL.MatrixMode(MatrixMode.Modelview);
                    GL.LoadIdentity();

                    // Enable blending for proper text rendering
                    GL.Enable(EnableCap.Blend);
                    GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

                    // Render the message text
                    _textRenderer.RenderText(MESSAGE, 50, 100, new Color4(1.0f, 0.27f, 0.0f, 1.0f)); // OrangeRed
                    SmartLogger.Log(LogLevel.Debug, LogPrefix, "Placeholder text rendered", forceLog: true);

                    // Check for OpenGL errors
                    CheckOpenGLErrors("Text Rendering");
                }
                else
                {
                    SmartLogger.Log(LogLevel.Warning, LogPrefix, "TextRenderer is null, rendering fallback placeholder", forceLog: true);

                    // Fallback to a simple colored rectangle if text renderer is not available
                    GL.MatrixMode(MatrixMode.Projection);
                    GL.LoadMatrix(ref _projectionMatrix);

                    GL.MatrixMode(MatrixMode.Modelview);
                    GL.LoadIdentity();

                    GL.Enable(EnableCap.Blend);
                    GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

                    // Draw a semi-transparent background
                    GL.Begin(PrimitiveType.Quads);
                    GL.Color4(0.2f, 0.2f, 0.2f, 0.5f);
                    GL.Vertex2(0, 0);
                    GL.Vertex2(_glControl.ActualWidth, 0);
                    GL.Vertex2(_glControl.ActualWidth, _glControl.ActualHeight);
                    GL.Vertex2(0, _glControl.ActualHeight);
                    GL.End();

                    // Draw a message box
                    float boxWidth = 300;
                    float boxHeight = 100;
                    float boxX = (float)((_glControl.ActualWidth - boxWidth) / 2);
                    float boxY = (float)((_glControl.ActualHeight - boxHeight) / 2);

                    GL.Begin(PrimitiveType.Quads);
                    GL.Color4(0.3f, 0.3f, 0.3f, 0.8f);
                    GL.Vertex2(boxX, boxY);
                    GL.Vertex2(boxX + boxWidth, boxY);
                    GL.Vertex2(boxX + boxWidth, boxY + boxHeight);
                    GL.Vertex2(boxX, boxY + boxHeight);
                    GL.End();

                    // Draw a border
                    GL.Begin(PrimitiveType.LineLoop);
                    GL.Color4(1.0f, 0.5f, 0.0f, 1.0f); // Orange border
                    GL.Vertex2(boxX, boxY);
                    GL.Vertex2(boxX + boxWidth, boxY);
                    GL.Vertex2(boxX + boxWidth, boxY + boxHeight);
                    GL.Vertex2(boxX, boxY + boxHeight);
                    GL.End();
                }

                // Restore OpenGL state
                GL.PopMatrix();
                GL.PopAttrib();
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error rendering placeholder: {ex}", forceLog: true);

                try
                {
                    // Try to ensure we restore OpenGL state even after an error
                    GL.PopMatrix();
                    GL.PopAttrib();
                }
                catch
                {
                    // Ignore errors during state restoration after an exception
                }
            }
        }

        public void SynchronizeWithController()
        {
            EnsureNotDisposed();
            bool needsUpdate = false;

            if (_currentState.Style != _controller.SelectedDrawingType)
            {
                UpdateRenderStyle(_controller.SelectedDrawingType);
                needsUpdate = true;
            }

            var styleName = _controller.SelectedStyle;
            if (!string.IsNullOrEmpty(styleName) && styleName != _currentState.StyleName)
            {
                var (color, shader) = _spectrumStyles.GetColorAndShader(styleName);
                UpdateSpectrumStyle(styleName, color, shader);
                needsUpdate = true;
            }

            if (_currentState.Quality != _controller.RenderQuality)
            {
                UpdateRenderQuality(_controller.RenderQuality);
                needsUpdate = true;
            }

            if (needsUpdate)
            {
                SmartLogger.Log(LogLevel.Debug, LogPrefix, "Controller synchronization triggered a render update", forceLog: true);
                RequestRender();
            }
        }

        public void UpdateRenderStyle(RenderStyle style)
        {
            EnsureNotDisposed();
            if (_currentState.Style == style)
                return;
            _currentState = _currentState with { Style = style };
            SmartLogger.Log(LogLevel.Debug, LogPrefix, $"Render style updated to {style}", forceLog: true);
            RequestRender();
        }

        public void UpdateSpectrumStyle(string styleName, Color color, ShaderProgram shader)
        {
            EnsureNotDisposed();
            if (string.IsNullOrEmpty(styleName) || styleName == _currentState.StyleName)
                return;
            var oldShader = _currentState.Paint;

            try
            {
                var clonedShader = shader.Clone();
                if (clonedShader == null)
                {
                    SmartLogger.Log(LogLevel.Error, LogPrefix, $"Failed to clone shader for style {styleName}", forceLog: true);
                    throw new InvalidOperationException("Shader clone failed");
                }

                _currentState = _currentState with
                {
                    Paint = clonedShader,
                    StyleName = styleName
                };
                SmartLogger.Log(LogLevel.Debug, LogPrefix, $"Spectrum style updated to {styleName}", forceLog: true);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error updating spectrum style: {ex}", forceLog: true);
                throw;
            }
            finally
            {
                oldShader?.Dispose();
            }

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

            SmartLogger.Log(LogLevel.Information, LogPrefix, $"Render quality updated to {quality}", forceLog: true);
        }

        public void RequestRender()
        {
            if (!_controller.IsOverlayActive || _glControl != _controller.SpectrumCanvas)
                _glControl?.InvalidateVisual();
        }

        public void UpdateRenderDimensions(int width, int height)
        {
            if (width > 0 && height > 0)
            {
                _projectionMatrix = Matrix4.CreateOrthographicOffCenter(
                    0, width, height, 0, -1, 1);
                SmartLogger.Log(LogLevel.Debug, LogPrefix, $"Render dimensions updated to {width}x{height}", forceLog: true);
                RequestRender();
            }
            else
            {
                SmartLogger.Log(LogLevel.Warning, LogPrefix, $"Invalid dimensions in UpdateRenderDimensions: {width}x{height}", forceLog: true);
            }
        }

        // Helper method to check for OpenGL errors
        private void CheckOpenGLErrors(string context)
        {
            OpenTK.Graphics.OpenGL.ErrorCode error;
            while ((error = GL.GetError()) != OpenTK.Graphics.OpenGL.ErrorCode.NoError)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"OpenGL error in {context}: {error}", forceLog: true);
            }
        }

        void EnsureNotDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(Renderer));
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            SmartLogger.Log(LogLevel.Debug, LogPrefix, "Disposing renderer...", forceLog: true);

            _isDisposed = _isAnalyzerDisposed = true;
            _disposalTokenSource.Cancel();
            _renderTimer?.Stop();

            PerformanceMetricsManager.PerformanceUpdated -= OnPerformanceMetricsUpdated;

            if (_controller is INotifyPropertyChanged notifier)
                notifier.PropertyChanged -= OnControllerPropertyChanged;

            if (_glControl != null)
            {
                _glControl.Render -= RenderFrame;
                _glControl.SizeChanged -= UpdateRenderDimensions;
                _glControl = null;
                SmartLogger.Log(LogLevel.Debug, LogPrefix, "GLControl events unsubscribed", forceLog: true);
            }

            try
            {
                _textRenderer?.Dispose();
                _textRenderer = null;
                SmartLogger.Log(LogLevel.Debug, LogPrefix, "TextRenderer disposed", forceLog: true);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error disposing TextRenderer: {ex}", forceLog: true);
            }

            try
            {
                _currentState.Paint?.Dispose();
                SmartLogger.Log(LogLevel.Debug, LogPrefix, "Current shader disposed", forceLog: true);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error disposing shader: {ex}", forceLog: true);
            }

            _renderLock.Dispose();
            _disposalTokenSource.Dispose();

            SmartLogger.Log(LogLevel.Information, LogPrefix, "Renderer disposed", forceLog: true);
        }
    }
}