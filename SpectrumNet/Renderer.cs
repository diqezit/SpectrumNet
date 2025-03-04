#nullable enable

namespace SpectrumNet
{
    public readonly record struct PerformanceMetrics(double FrameTime, double Fps);

    public sealed class Renderer : IDisposable
    {
        const int RENDER_TIMEOUT_MS = 16;
        const string DEFAULT_STYLE = "Gradient";
        const string MESSAGE = "Push start to begin record...";
        const string LogPrefix = "[Renderer] ";

        record struct RenderState(SKPaint Paint, RenderStyle Style, string StyleName);

        readonly SemaphoreSlim _renderLock = new(1, 1);
        readonly SpectrumBrushes _spectrumStyles;
        readonly IAudioVisualizationController _controller;
        readonly SpectrumAnalyzer _analyzer;
        readonly CancellationTokenSource _disposalTokenSource = new();
        readonly Stopwatch _performanceMonitor = new();

        SKElement? _skElement;
        DispatcherTimer? _renderTimer;
        RenderState _currentState;
        volatile bool _isDisposed, _isAnalyzerDisposed;
        volatile bool _shouldShowPlaceholder = true;

        public string CurrentStyleName => _currentState.StyleName;
        public event EventHandler<PerformanceMetrics>? PerformanceUpdate;

        public bool ShouldShowPlaceholder
        {
            get => _shouldShowPlaceholder;
            set => _shouldShowPlaceholder = value;
        }

        public Renderer(SpectrumBrushes styles, IAudioVisualizationController controller, SpectrumAnalyzer analyzer, SKElement element)
        {
            _spectrumStyles = styles ?? throw new ArgumentNullException(nameof(styles));
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
            _skElement = element ?? throw new ArgumentNullException(nameof(element));

            if (_analyzer is IComponent comp)
                comp.Disposed += (_, _) => _isAnalyzerDisposed = true;

            _shouldShowPlaceholder = !_controller.IsRecording;
            InitializeRenderer();
            SmartLogger.Log(LogLevel.Information, LogPrefix, "Successfully initialized.", forceLog: true);
        }

        void InitializeRenderer()
        {
            var (_, _, paint) = _spectrumStyles.GetColorsAndBrush(DEFAULT_STYLE);
            _currentState = new RenderState(paint?.Clone() ?? throw new InvalidOperationException($"{LogPrefix}Failed to initialize {DEFAULT_STYLE} style"),
                                            RenderStyle.Bars, DEFAULT_STYLE);

            _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RENDER_TIMEOUT_MS) };
            _renderTimer.Tick += (_, _) => RequestRender();
            _renderTimer.Start();

            _skElement!.PaintSurface += RenderFrame;
            _skElement.Loaded += OnElementLoaded;
            _skElement.Unloaded += OnElementUnloaded;

            _performanceMonitor.Start();
            _controller.PropertyChanged += OnControllerPropertyChanged;
            SynchronizeWithController();
        }

        void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
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
                        var (startColor, endColor, _) = _spectrumStyles.GetColorsAndBrush(_controller.SelectedStyle);
                        UpdateSpectrumStyle(_controller.SelectedStyle, startColor, endColor);
                    }
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

        void OnElementLoaded(object sender, RoutedEventArgs e) =>
            UpdateRenderDimensions((int)_skElement!.ActualWidth, (int)_skElement.ActualHeight);

        void OnElementUnloaded(object sender, RoutedEventArgs e)
        {
            _renderTimer?.Stop();
            _performanceMonitor.Stop();
        }

        public void RenderFrame(object? sender, SKPaintSurfaceEventArgs e)
        {
            if (_controller.IsOverlayActive && sender == _controller.SpectrumCanvas)
            {
                e.Surface.Canvas.Clear(SKColors.Transparent);
                return;
            }

            if (_isDisposed || !_renderLock.Wait(RENDER_TIMEOUT_MS))
                return;

            try
            {
                RenderFrameInternal(e.Surface.Canvas, e.Info);
                EmitPerformanceMetrics();
            }
            catch (ObjectDisposedException)
            {
                _isAnalyzerDisposed = true;
                _renderTimer?.Stop();
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error rendering frame: {ex}");
                RenderPlaceholder(e.Surface.Canvas);
            }
            finally
            {
                _renderLock.Release();
            }
        }

        void RenderFrameInternal(SKCanvas canvas, SKImageInfo info)
        {
            canvas.Clear(SKColors.Transparent);

            if (_controller.IsTransitioning || ShouldRenderPlaceholder())
            {
                RenderPlaceholder(canvas);
                return;
            }

            var spectrum = GetSpectrumData();
            if (spectrum == null || !TryCalcRenderParams(info, out float barWidth, out float barSpacing, out int barCount))
            {
                RenderPlaceholder(canvas);
                return;
            }

            RenderSpectrum(canvas, spectrum, info, barWidth, barSpacing, barCount);
        }

        bool ShouldRenderPlaceholder() =>
            _shouldShowPlaceholder || _isAnalyzerDisposed || _analyzer == null || !_controller.IsRecording;

        SpectralData? GetSpectrumData()
        {
            try { return _analyzer?.GetCurrentSpectrum(); }
            catch (ObjectDisposedException)
            {
                _isAnalyzerDisposed = true;
                _shouldShowPlaceholder = true;
                return null;
            }
        }

        void RenderSpectrum(SKCanvas canvas, SpectralData spectrum, SKImageInfo info, float barWidth, float barSpacing, int barCount)
        {
            var renderer = SpectrumRendererFactory.CreateRenderer(_currentState.Style, _controller.IsOverlayActive);
            renderer.Render(canvas, spectrum.Spectrum, info, barWidth, barSpacing, barCount, _currentState.Paint, PerfomanceMetrics.DrawPerformanceInfo);
        }

        bool TryCalcRenderParams(SKImageInfo info, out float barWidth, out float barSpacing, out int barCount)
        {
            barWidth = barSpacing = 0;
            barCount = _controller.BarCount;
            int totalWidth = info.Width;
            if (totalWidth <= 0 || barCount <= 0)
                return false;
            barSpacing = Math.Min((float)_controller.BarSpacing, totalWidth / (barCount + 1));
            barWidth = Math.Max((totalWidth - (barCount - 1) * barSpacing) / barCount, 1.0f);
            barSpacing = barCount > 1 ? (totalWidth - barCount * barWidth) / (barCount - 1) : 0;
            return true;
        }

        static void RenderPlaceholder(SKCanvas canvas)
        {
            using var paint = new SKPaint
            {
                Color = SKColors.OrangeRed,
                TextAlign = SKTextAlign.Left,
                TextSize = 50,
                IsAntialias = true
            };
            canvas.DrawText(MESSAGE, 50, 100, paint);
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
                var (startColor, endColor, _) = _spectrumStyles.GetColorsAndBrush(styleName);
                UpdateSpectrumStyle(styleName, startColor, endColor);
                needsUpdate = true;
            }
            if (needsUpdate)
                RequestRender();
        }

        public void UpdateRenderStyle(RenderStyle style)
        {
            EnsureNotDisposed();
            if (_currentState.Style == style)
                return;
            _currentState = _currentState with { Style = style };
            RequestRender();
        }

        public void UpdateSpectrumStyle(string styleName, SKColor startColor, SKColor endColor)
        {
            EnsureNotDisposed();
            if (string.IsNullOrEmpty(styleName) || styleName == _currentState.StyleName)
                return;
            var (_, _, paint) = _spectrumStyles.GetColorsAndBrush(styleName);
            if (paint == null)
                return;
            var oldPaint = _currentState.Paint;
            _currentState = new RenderState(paint.Clone(), _currentState.Style, styleName);
            oldPaint.Dispose();
            RequestRender();
        }

        public void RequestRender()
        {
            if (!_controller.IsOverlayActive || _skElement != _controller.SpectrumCanvas)
                _skElement?.InvalidateVisual();
        }

        public void UpdateRenderDimensions(int width, int height)
        {
            if (width > 0 && height > 0)
                RequestRender();
        }

        void EmitPerformanceMetrics()
        {
            var elapsed = _performanceMonitor.Elapsed;
            _performanceMonitor.Restart();
            PerformanceUpdate?.Invoke(this, new PerformanceMetrics(elapsed.TotalMilliseconds, 1000.0 / elapsed.TotalMilliseconds));
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
            _isDisposed = _isAnalyzerDisposed = true;
            _disposalTokenSource.Cancel();
            _renderTimer?.Stop();
            _performanceMonitor.Stop();

            if (_controller is INotifyPropertyChanged notifier)
                notifier.PropertyChanged -= OnControllerPropertyChanged;

            _currentState.Paint.Dispose();
            _renderLock.Dispose();
            _disposalTokenSource.Dispose();

            if (_skElement != null)
            {
                _skElement.PaintSurface -= RenderFrame;
                _skElement.Loaded -= OnElementLoaded;
                _skElement.Unloaded -= OnElementUnloaded;
                _skElement = null;
            }
        }
    }
}