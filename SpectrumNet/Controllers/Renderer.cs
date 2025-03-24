#nullable enable

using static SpectrumNet.SmartLogger;
using static System.DateTime;

namespace SpectrumNet
{
    public sealed class Renderer : IDisposable
    {
        private const string DEFAULT_STYLE = "Solid";
        private const string LogPrefix = "Renderer";

        private record RenderState(SKPaint Paint, RenderStyle Style, string StyleName, RenderQuality Quality);

        private readonly SemaphoreSlim _renderLock = new(1, 1);
        private readonly SpectrumBrushes _spectrumStyles;
        private readonly IAudioVisualizationController _controller;
        private readonly SpectrumAnalyzer _analyzer;
        private readonly CancellationTokenSource _disposalTokenSource = new();
        private readonly RendererPlaceholder _placeholder = new() { CanvasSize = new SKSize(1, 1) };

        private SKGLElement? _skElement;
        private RenderState _currentState = default!;
        private volatile bool _isDisposed, _isAnalyzerDisposed;
        private volatile bool _shouldShowPlaceholder = true;
        private int _width, _height;
        private DateTime _lastRenderTime = Now;

        public string CurrentStyleName => _currentState.StyleName;
        public RenderQuality CurrentQuality => _currentState.Quality;
        public event EventHandler<PerformanceMetrics>? PerformanceUpdate;

        public bool ShouldShowPlaceholder
        {
            get => _shouldShowPlaceholder;
            set => _shouldShowPlaceholder = value;
        }

        public Renderer(
            SpectrumBrushes styles,
            IAudioVisualizationController controller,
            SpectrumAnalyzer analyzer,
            SKGLElement element)
        {
            _spectrumStyles = styles ?? throw new ArgumentNullException(nameof(styles));
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
            _skElement = element ?? throw new ArgumentNullException(nameof(element));

            if (_analyzer is IComponent comp)
                comp.Disposed += OnAnalyzerDisposed;

            _shouldShowPlaceholder = !_controller.IsRecording;
            InitializeRenderer();

            Log(LogLevel.Information, LogPrefix, "Successfully initialized.", forceLog: true);
            PerformanceMetricsManager.PerformanceUpdated += OnPerformanceMetricsUpdated;
        }

        private void OnAnalyzerDisposed(object? sender, EventArgs e) => _isAnalyzerDisposed = true;

        private void InitializeRenderer()
        {
            var (color, brush) = _spectrumStyles.GetColorAndBrush(DEFAULT_STYLE);
            _currentState = new RenderState(
                brush.Clone() ?? throw new InvalidOperationException($"{LogPrefix} Failed to initialize {DEFAULT_STYLE} style"),
                RenderStyle.Bars,
                DEFAULT_STYLE,
                RenderQuality.Medium);

            if (_skElement is not null)
            {
                _skElement.PaintSurface += RenderFrame;
                _skElement.Loaded += OnElementLoaded;
                _skElement.Unloaded += OnElementUnloaded;
            }

            _controller.PropertyChanged += OnControllerPropertyChanged;
            SynchronizeWithController();

            if (_controller.IsRecording)
                CompositionTarget.Rendering += OnRendering;
        }

        private void OnRendering(object? sender, EventArgs e) => RequestRender();

        private void OnPerformanceMetricsUpdated(object? sender, PerformanceMetrics metrics) =>
            PerformanceUpdate?.Invoke(this, metrics);

        private void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e?.PropertyName is null)
                return;

            switch (e.PropertyName)
            {
                case nameof(IAudioVisualizationController.IsRecording):
                    UpdateRecordingState(_controller.IsRecording);
                    break;

                case nameof(IAudioVisualizationController.IsOverlayActive):
                    SpectrumRendererFactory.ConfigureAllRenderers(_controller.IsOverlayActive);
                    RequestRender();
                    break;

                case nameof(IAudioVisualizationController.SelectedDrawingType):
                    UpdateRenderStyle(_controller.SelectedDrawingType);
                    break;

                case nameof(IAudioVisualizationController.SelectedStyle) when !string.IsNullOrEmpty(_controller.SelectedStyle):
                    var (color, brush) = _spectrumStyles.GetColorAndBrush(_controller.SelectedStyle);
                    UpdateSpectrumStyle(_controller.SelectedStyle, color, brush);
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
                case nameof(IAudioVisualizationController.ShowPerformanceInfo):
                    RequestRender();
                    break;
            }
        }

        private void UpdateRecordingState(bool isRecording)
        {
            if (isRecording)
                CompositionTarget.Rendering += OnRendering;
            else
                CompositionTarget.Rendering -= OnRendering;

            _shouldShowPlaceholder = !isRecording;
            RequestRender();
        }

        private void OnElementLoaded(object? sender, RoutedEventArgs e) =>
            UpdateRenderDimensions((int)(_skElement?.ActualWidth ?? 0), (int)(_skElement?.ActualHeight ?? 0));

        private void OnElementUnloaded(object? sender, RoutedEventArgs e) =>
            CompositionTarget.Rendering -= OnRendering;

        public void RenderFrame(object? sender, SKPaintGLSurfaceEventArgs e)
        {
            if (e is null || _isDisposed)
                return;

            if (_controller.IsOverlayActive && sender == _controller.SpectrumCanvas)
            {
                e.Surface.Canvas.Clear(SKColors.Transparent);
                return;
            }

            Safe(() => RenderFrameInternal(e.Surface.Canvas, e.Info),
                new ErrorHandlingOptions
                {
                    Source = LogPrefix,
                    ErrorMessage = "Error rendering frame",
                    ExceptionHandler = ex =>
                    {
                        if (ex is ObjectDisposedException)
                        {
                            _isAnalyzerDisposed = true;
                            CompositionTarget.Rendering -= OnRendering;
                        }
                        else
                        {
                            RenderPlaceholder(e.Surface.Canvas, e.Info);
                        }
                    }
                });
        }

        private void RenderFrameInternal(SKCanvas canvas, SKImageInfo info)
        {
            canvas.Clear(SKColors.Transparent);
            _lastRenderTime = Now;

            if (_controller.IsTransitioning || ShouldRenderPlaceholder())
            {
                RenderPlaceholder(canvas, info);
                return;
            }

            var spectrum = GetSpectrumData();
            if (spectrum is null)
            {
                if (!_controller.IsRecording)
                    RenderPlaceholder(canvas, info);
                return;
            }

            if (!TryCalcRenderParams(info, out float barWidth, out float barSpacing, out int barCount))
            {
                RenderPlaceholder(canvas, info);
                return;
            }

            RenderSpectrum(canvas, spectrum, info, barWidth, barSpacing, barCount);
            PerformanceMetricsManager.UpdateMetrics();
        }

        private bool ShouldRenderPlaceholder() =>
            _shouldShowPlaceholder || _isAnalyzerDisposed || _analyzer is null || !_controller.IsRecording;

        private SpectralData? GetSpectrumData()
        {
            var analyzer = _controller.GetCurrentAnalyzer();
            if (analyzer is null || analyzer.IsDisposed)
            {
                _shouldShowPlaceholder = true;
                return null;
            }

            var result = Safe<SpectralData?>(() => analyzer.GetCurrentSpectrum(),
                options: new ErrorHandlingOptions
                {
                    Source = LogPrefix,
                    ErrorMessage = "Error getting spectrum data",
                    ExceptionHandler = ex =>
                    {
                        if (ex is ObjectDisposedException)
                            _isAnalyzerDisposed = _shouldShowPlaceholder = true;
                    }
                });
            return result.Success ? result.Result : null;
        }

        private void RenderSpectrum(SKCanvas canvas, SpectralData spectrum, SKImageInfo info, float barWidth, float barSpacing, int barCount)
        {
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

        private bool TryCalcRenderParams(SKImageInfo info, out float barWidth, out float barSpacing, out int barCount)
        {
            barCount = _controller.BarCount;
            barWidth = 0f;
            barSpacing = 0f;
            int totalWidth = info.Width;

            if (totalWidth <= 0 || barCount <= 0)
                return false;

            barSpacing = Math.Min((float)_controller.BarSpacing, totalWidth / (barCount + 1));
            barWidth = Math.Max((totalWidth - (barCount - 1) * barSpacing) / barCount, 1.0f);
            barSpacing = barCount > 1 ? (totalWidth - barCount * barWidth) / (barCount - 1) : 0;
            return true;
        }

        private void RenderPlaceholder(SKCanvas? canvas, SKImageInfo info)
        {
            if (canvas is null)
                return;

            _placeholder.CanvasSize = new SKSize(info.Width, info.Height);
            _placeholder.Render(canvas, info);
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

            if (!string.IsNullOrEmpty(_controller.SelectedStyle) && _controller.SelectedStyle != _currentState.StyleName)
            {
                var (color, brush) = _spectrumStyles.GetColorAndBrush(_controller.SelectedStyle);
                UpdateSpectrumStyle(_controller.SelectedStyle, color, brush);
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
            Safe(() =>
            {
                _currentState = _currentState with
                {
                    Paint = brush.Clone() ?? throw new InvalidOperationException("Brush clone failed"),
                    StyleName = styleName
                };
                oldPaint?.Dispose();
            }, new ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error updating spectrum style"
            });

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
            Log(LogLevel.Information, LogPrefix, $"Render quality updated to {quality}", forceLog: true);
        }

        public void RequestRender()
        {
            if (!_controller.IsOverlayActive || _skElement != _controller.SpectrumCanvas)
                _skElement?.InvalidateVisual();
        }

        public void UpdateRenderDimensions(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                Log(LogLevel.Warning, LogPrefix, $"Invalid dimensions for renderer: {width}x{height}");
                return;
            }

            _width = width;
            _height = height;
            RequestRender();
        }

        private void EnsureNotDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(Renderer));
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = _isAnalyzerDisposed = true;

            Safe(() =>
            {
                _disposalTokenSource.Cancel();
                CompositionTarget.Rendering -= OnRendering;
                PerformanceMetricsManager.PerformanceUpdated -= OnPerformanceMetricsUpdated;

                if (_controller is INotifyPropertyChanged notifier)
                    notifier.PropertyChanged -= OnControllerPropertyChanged;

                UnsubscribeFromUIElementEvents();
                _currentState.Paint?.Dispose();
                _placeholder.Dispose();

                SafeDispose(_renderLock, "RenderLock",
                    new ErrorHandlingOptions
                    {
                        Source = LogPrefix,
                        ErrorMessage = "Error disposing render lock"
                    });

                SafeDispose(_disposalTokenSource, "DisposalTokenSource",
                    new ErrorHandlingOptions
                    {
                        Source = LogPrefix,
                        ErrorMessage = "Error disposing disposal token source"
                    });

                Log(LogLevel.Information, LogPrefix, "Renderer disposed", forceLog: true);
            },
            new ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error during renderer disposal"
            });
        }

        private void UnsubscribeFromUIElementEvents()
        {
            if (_skElement is null)
                return;

            try
            {
                if (_skElement.Dispatcher.CheckAccess())
                {
                    _skElement.PaintSurface -= RenderFrame;
                    _skElement.Loaded -= OnElementLoaded;
                    _skElement.Unloaded -= OnElementUnloaded;
                }
            }
            catch (Exception ex)
            {
                Log(LogLevel.Warning, LogPrefix, $"Error unsubscribing from UI events: {ex.Message}");
            }

            _skElement = null;
        }
    }
}