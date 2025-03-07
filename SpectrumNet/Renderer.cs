#nullable enable

namespace SpectrumNet
{

    public sealed class Renderer : IDisposable
    {
        const int RENDER_TIMEOUT_MS = 16;
        const string DEFAULT_STYLE = "Solid";
        const string MESSAGE = "Push start to begin record...";
        const string LogPrefix = "[Renderer] ";

        record struct RenderState(SKPaint Paint, RenderStyle Style, string StyleName, RenderQuality Quality);

        readonly SemaphoreSlim _renderLock = new(1, 1);
        readonly SpectrumBrushes _spectrumStyles;
        readonly IAudioVisualizationController _controller;
        readonly SpectrumAnalyzer _analyzer;
        readonly CancellationTokenSource _disposalTokenSource = new();

        SKElement? _skElement;
        DispatcherTimer? _renderTimer;
        RenderState _currentState;
        volatile bool _isDisposed, _isAnalyzerDisposed;
        volatile bool _shouldShowPlaceholder = true;

        public string CurrentStyleName => _currentState.StyleName;
        public RenderQuality CurrentQuality => _currentState.Quality;
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

            PerformanceMetricsManager.PerformanceUpdated += OnPerformanceMetricsUpdated;
        }

        void InitializeRenderer()
        {
            var (color, brush) = _spectrumStyles.GetColorAndBrush(DEFAULT_STYLE);
            _currentState = new RenderState(
                brush.Clone() ?? throw new InvalidOperationException($"{LogPrefix}Failed to initialize {DEFAULT_STYLE} style"),
                RenderStyle.Bars,
                DEFAULT_STYLE,
                RenderQuality.Medium);

            _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RENDER_TIMEOUT_MS) };
            _renderTimer.Tick += (_, _) => RequestRender();
            _renderTimer.Start();

            if (_skElement != null)
            {
                _skElement.PaintSurface += RenderFrame;
                _skElement.Loaded += OnElementLoaded;
                _skElement.Unloaded += OnElementUnloaded;
            }

            _controller.PropertyChanged += OnControllerPropertyChanged;
            SynchronizeWithController();
        }

        private void OnPerformanceMetricsUpdated(object? sender, PerformanceMetrics metrics)
        {
            PerformanceUpdate?.Invoke(this, metrics);
        }

        void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e == null) return;

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
                        var (color, brush) = _spectrumStyles.GetColorAndBrush(_controller.SelectedStyle);
                        UpdateSpectrumStyle(_controller.SelectedStyle, color, brush);
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

        void OnElementLoaded(object? sender, RoutedEventArgs e)
        {
            if (_skElement != null)
                UpdateRenderDimensions((int)_skElement.ActualWidth, (int)_skElement.ActualHeight);
        }

        void OnElementUnloaded(object? sender, RoutedEventArgs e)
        {
            _renderTimer?.Stop();
        }

        public void RenderFrame(object? sender, SKPaintSurfaceEventArgs e)
        {
            if (e == null) return;

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
            }
            catch (ObjectDisposedException)
            {
                _isAnalyzerDisposed = true;
                _renderTimer?.Stop();
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error rendering frame: {ex}", forceLog: true);
                RenderPlaceholder(e.Surface.Canvas);
            }
            finally
            {
                _renderLock.Release();
            }
        }

        void RenderFrameInternal(SKCanvas canvas, SKImageInfo info)
        {
            if (canvas == null) return;

            canvas.Clear(SKColors.Transparent);

            if (_controller.IsTransitioning || ShouldRenderPlaceholder())
            {
                RenderPlaceholder(canvas);
                return;
            }

            var spectrum = GetSpectrumData();
            if (spectrum == null)
            {
                if (!_controller.IsRecording)
                {
                    RenderPlaceholder(canvas);
                }
                return;
            }

            if (!TryCalcRenderParams(info, out float barWidth, out float barSpacing, out int barCount))
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
            if (canvas == null || spectrum == null) return;

            var renderer = SpectrumRendererFactory.CreateRenderer(
                _currentState.Style,
                _controller.IsOverlayActive,
                _currentState.Quality);

            renderer.Render(
                canvas,
                spectrum.Spectrum,
                info,
                barWidth,
                barSpacing,
                barCount,
                _currentState.Paint,
                (c, i) => PerformanceMetricsManager.DrawPerformanceInfo(c, i, _controller.ShowPerformanceInfo));
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
            if (canvas == null) return;

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
                var (color, brush) = _spectrumStyles.GetColorAndBrush(styleName);
                UpdateSpectrumStyle(styleName, color, brush);
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

        public void UpdateRenderStyle(RenderStyle style)
        {
            EnsureNotDisposed();
            if (_currentState.Style == style)
                return;
            _currentState = _currentState with { Style = style };
            RequestRender();
        }

        public void UpdateSpectrumStyle(string styleName, SKColor color, SKPaint brush)
        {
            EnsureNotDisposed();
            if (string.IsNullOrEmpty(styleName) || styleName == _currentState.StyleName)
                return;
            var oldPaint = _currentState.Paint;
            _currentState = _currentState with
            {
                Paint = brush.Clone() ?? throw new InvalidOperationException("Brush clone failed"),
                StyleName = styleName
            };
            oldPaint?.Dispose();
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
            if (!_controller.IsOverlayActive || _skElement != _controller.SpectrumCanvas)
                _skElement?.InvalidateVisual();
        }

        public void UpdateRenderDimensions(int width, int height)
        {
            if (width > 0 && height > 0)
                RequestRender();
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

            PerformanceMetricsManager.PerformanceUpdated -= OnPerformanceMetricsUpdated;

            if (_controller is INotifyPropertyChanged notifier)
                notifier.PropertyChanged -= OnControllerPropertyChanged;

            if (_skElement != null)
            {
                _skElement.PaintSurface -= RenderFrame;
                _skElement.Loaded -= OnElementLoaded;
                _skElement.Unloaded -= OnElementUnloaded;
                _skElement = null;
            }

            _currentState.Paint?.Dispose();
            _renderLock.Dispose();
            _disposalTokenSource.Dispose();

            SmartLogger.Log(LogLevel.Information, LogPrefix, "Renderer disposed", forceLog: true);
        }
    }
}