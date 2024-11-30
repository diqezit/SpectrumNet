using System.Diagnostics;

namespace SpectrumNet
{
    /// <summary>
    /// Handles the rendering of spectrum visualization using SkiaSharp.
    /// Thread-safe implementation with optimized resource management.
    /// </summary>
    public sealed class Renderer : IDisposable
    {
        private const int RENDER_TIMEOUT_MS = 16; // ~60 FPS
        private const string DEFAULT_STYLE = "Gradient";
        private const string MESSAGE = "Нажми старт чтобы начать...";

        private readonly record struct RenderState(
            SKPaint Paint,
            RenderStyle Style,
            string StyleName
        );

        private readonly SemaphoreSlim _renderLock = new(1, 1);
        private readonly SpectrumBrushes _spectrumStyles;
        private readonly MainWindow _mainWindow;
        private readonly SpectrumAnalyzer _analyzer;
        private readonly CancellationTokenSource _disposalTokenSource = new();
        private readonly Stopwatch _performanceMonitor;

        private SKElement? _skElement;
        private DispatcherTimer? _renderTimer;
        private RenderState _currentState;
        private volatile bool _isDisposed;
        private volatile bool _isAnalyzerDisposed;
        private volatile bool _shouldShowPlaceholder;

        public string CurrentStyleName => _currentState.StyleName;

        public event EventHandler<PerformanceMetrics>? PerformanceUpdate;

        #region Construction & Initialization

        public Renderer(SpectrumBrushes styles, MainWindow window, SpectrumAnalyzer analyzer, SKElement element)
        {
            _spectrumStyles = styles ?? throw new ArgumentNullException(nameof(styles));
            _mainWindow = window ?? throw new ArgumentNullException(nameof(window));
            _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
            _skElement = element ?? throw new ArgumentNullException(nameof(element));

            _performanceMonitor = new Stopwatch();
            _shouldShowPlaceholder = true;

            if (_analyzer is IComponent c)
                c.Disposed += (s, e) => _isAnalyzerDisposed = true;

            InitializeRenderer();
            Log.Information($"{nameof(Renderer)} успешно инициализирован.");
        }

        private void InitializeRenderer()
        {
            try
            {
                var (_, _, paint) = _spectrumStyles.GetColorsAndBrush(DEFAULT_STYLE);
                if (paint == null)
                    throw new InvalidOperationException($"Failed to initialize {DEFAULT_STYLE} style");

                _currentState = new RenderState(paint.Clone(),
                    RenderStyle.Bars, DEFAULT_STYLE);

                _renderTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(RENDER_TIMEOUT_MS)
                };
                _renderTimer.Tick += (_, _) => RequestRender();
                _renderTimer.Start();

                SubscribeToEvents();
                _performanceMonitor.Start();
                _mainWindow.PropertyChanged += OnMainWindowPropertyChanged;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Не удалось инициализировать рендерер");
                throw new InvalidOperationException("Не удалось инициализировать рендерер", ex);
            }
        }

        private void OnMainWindowPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindow.IsRecording))
            {
                _shouldShowPlaceholder = !_mainWindow.IsRecording;
                RequestRender();
                Log.Debug("[Renderer] Состояние плейсхолдера обновлено: {ShouldShow}", _shouldShowPlaceholder);
            }
        }

        #endregion

        #region Event Handling

        private void SubscribeToEvents()
        {
            if (_skElement is null) return;

            _skElement.PaintSurface += RenderFrame;
            _skElement.Loaded += HandleElementLoaded;
            _skElement.Unloaded += HandleElementUnloaded;
        }

        private void HandleElementLoaded(object sender, EventArgs e)
        {
            if (_skElement is null) return;

            UpdateRenderDimensions(
                width: (int)_skElement.ActualWidth,
                height: (int)_skElement.ActualHeight
            );
        }

        private void HandleElementUnloaded(object sender, EventArgs e)
        {
            // Cleanup resources when element is unloaded
            _renderTimer?.Stop();
            _performanceMonitor.Stop();
        }

        #endregion

        #region Rendering Pipeline

        public void RenderFrame(object? sender, SKPaintSurfaceEventArgs e)
        {
            if (_isDisposed) return;

            if (!_renderLock.Wait(RENDER_TIMEOUT_MS)) return;

            try
            {
                RenderFrameInternal(e.Surface.Canvas, e.Info);
                EmitPerformanceMetrics();
            }
            catch (ObjectDisposedException)
            {
                _isAnalyzerDisposed = true;
                Log.Warning("[Renderer] SpectrumAnalyzer was disposed, stopping render loop");
                _renderTimer?.Stop();
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"{nameof(Renderer)} Ошибка при рендеринге кадра");
            }
            finally
            {
                _renderLock.Release();
            }
        }

        private void RenderFrameInternal(SKCanvas canvas, SKImageInfo info)
        {
            canvas.Clear(SKColors.Transparent);

            if (_shouldShowPlaceholder || _isAnalyzerDisposed)
            {
                RenderPlaceholder(canvas);
                return;
            }

            SpectralData? spectrum = null;
            try
            {
                spectrum = _analyzer.GetCurrentSpectrum();
            }
            catch (ObjectDisposedException)
            {
                _isAnalyzerDisposed = true;
                _shouldShowPlaceholder = true;
                Log.Warning("[Renderer] SpectrumAnalyzer was disposed during spectrum acquisition");
                RenderPlaceholder(canvas);
                return;
            }

            if (spectrum?.Spectrum is not { Length: > 0 })
            {
                RenderPlaceholder(canvas);
                return;
            }

            // Реализация динамической ширины рендера
            var renderer = SpectrumRendererFactory.CreateRenderer(_currentState.Style, _mainWindow.IsOverlayActive);
            var totalWidth = info.Width;
            var barCount = _mainWindow.BarCount;
            var barSpacing = Math.Min((float)_mainWindow.BarSpacing, totalWidth / (barCount + 1));
            var barWidth = Math.Max((totalWidth - (barCount - 1) * barSpacing) / barCount, 1.0f);
            barSpacing = (totalWidth - barCount * barWidth) / (barCount - 1);

            // Используем метод DrawPerformanceInfo из Renderer
            renderer.Render(canvas, spectrum.Spectrum, info, barWidth, barSpacing, barCount, _currentState.Paint,
                PerfomanceMetrics.DrawPerformanceInfo);
        }

        private static void RenderPlaceholder(SKCanvas canvas)
        {
            using var paint = new SKPaint
            {
                Color = SKColors.OrangeRed,
                TextAlign = SKTextAlign.Left,
                TextSize = 50,
                IsAntialias = true
            };

            canvas.Clear(SKColors.Transparent);
            canvas.DrawText(MESSAGE, 50, 100, paint);

            //Log.Debug("Отрисован плейсхолдер");
        }

        #endregion

        #region Style Management

        public void UpdateRenderStyle(RenderStyle style)
        {
            EnsureNotDisposed();
            _currentState = _currentState with { Style = style };
            RequestRender();
            Log.Debug("[Renderer] Обновлен стиль рендеринга: {Style}", style);
        }

        public void UpdateSpectrumStyle(string styleName, SKColor startColor, SKColor endColor)
        {
            EnsureNotDisposed();

            if (string.IsNullOrEmpty(styleName) || styleName == _currentState.StyleName)
                return;

            var (_, _, paint) = _spectrumStyles.GetColorsAndBrush(styleName);
            if (paint == null)
            {
                Log.Error("[Renderer] Кисть для стиля {StyleName} не настроена", styleName);
                return;
            }

            var oldPaint = _currentState.Paint;
            _currentState = new RenderState(paint.Clone(), _currentState.Style, styleName);
            oldPaint.Dispose();

            RequestRender();
            Log.Debug("[Renderer] Обновлен стиль спектра: {StyleName}", styleName);
        }

        #endregion

        #region Performance Monitoring

        private void EmitPerformanceMetrics()
        {
            var elapsed = _performanceMonitor.Elapsed;
            _performanceMonitor.Restart();

            PerformanceUpdate?.Invoke(this, new PerformanceMetrics(
                FrameTime: elapsed.TotalMilliseconds,
                Fps: 1000.0 / elapsed.TotalMilliseconds
            ));
        }

        #endregion

        #region Utility Methods

        public void RequestRender() => _skElement?.InvalidateVisual();

        public void UpdateRenderDimensions(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                Log.Warning("[Renderer] Invalid render dimensions: {Width}x{Height}", width, height);
                return;
            }

            RequestRender();
            Log.Debug("[Renderer] Render dimensions updated: {Width}x{Height}", width, height);
        }

        private void EnsureNotDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(Renderer));
        }

        #endregion

        #region IDisposable Implementation

        public void Dispose()
        {
            if (_isDisposed) return;

            _isDisposed = true;
            _isAnalyzerDisposed = true;

            _disposalTokenSource.Cancel();
            _renderTimer?.Stop();
            _performanceMonitor.Stop();

            if (_mainWindow is INotifyPropertyChanged notifier)
            {
                notifier.PropertyChanged -= OnMainWindowPropertyChanged;
            }

            _currentState.Paint.Dispose();
            _renderLock.Dispose();
            _disposalTokenSource.Dispose();

            if (_skElement != null)
            {
                _skElement.PaintSurface -= RenderFrame;
                _skElement.Loaded -= HandleElementLoaded;
                _skElement.Unloaded -= HandleElementUnloaded;
                _skElement = null;
            }

            Log.Information("[Renderer] Рендерер успешно утилизирован");
        }

        #endregion
    }

    public readonly record struct PerformanceMetrics(
        double FrameTime,
        double Fps
    );
}