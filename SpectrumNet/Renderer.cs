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

        public Renderer(
            SpectrumBrushes spectrumStyles,
            MainWindow mainWindow,
            SpectrumAnalyzer analyzer,
            SKElement skElement)
        {
            _spectrumStyles = spectrumStyles ?? throw new ArgumentNullException(nameof(spectrumStyles));
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
            _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
            _skElement = skElement ?? throw new ArgumentNullException(nameof(skElement));

            _performanceMonitor = new Stopwatch();
            _shouldShowPlaceholder = true; // Изначально показываем плейсхолдер

            if (_analyzer is IComponent component)
            {
                component.Disposed += (s, e) => _isAnalyzerDisposed = true;
            }

            InitializeRenderer();
            Log.Information($"{nameof(Renderer)} успешно инициализирован.");
        }

        private void InitializeRenderer()
        {
            try
            {
                InitializeRenderState();
                ConfigureRenderTimer();
                SubscribeToEvents();
                _performanceMonitor.Start();
                _mainWindow.PropertyChanged += HandleMainWindowPropertyChanged;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Не удалось инициализировать рендерер");
                throw new RendererInitializationException("Не удалось инициализировать рендерер", ex);
            }
        }

        private void HandleMainWindowPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindow.IsRecording))
            {
                _shouldShowPlaceholder = !_mainWindow.IsRecording;
                RequestRender(); // Принудительно вызываем перерисовку
                Log.Debug("Состояние плейсхолдера обновлено: {ShouldShow}", _shouldShowPlaceholder);
            }
        }

        private void InitializeRenderState()
        {
            var (_, _, paint) = _spectrumStyles.GetColorsAndBrush(DEFAULT_STYLE);
            if (paint is null)
            {
                throw new RendererInitializationException($"Failed to initialize {DEFAULT_STYLE} style");
            }

            _currentState = new RenderState(
                Paint: paint.Clone(),
                Style: RenderStyle.Bars, // Используем существующий стиль по умолчанию вместо Default
                StyleName: DEFAULT_STYLE
            );
        }

        private void ConfigureRenderTimer()
        {
            _renderTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(RENDER_TIMEOUT_MS)
            };

            _renderTimer.Tick += (_, _) => RequestRender();
            _renderTimer.Start();
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
                Log.Warning("SpectrumAnalyzer was disposed, stopping render loop");
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
                Log.Warning("SpectrumAnalyzer was disposed during spectrum acquisition");
                RenderPlaceholder(canvas);
                return;
            }

            if (spectrum?.Spectrum is not { Length: > 0 })
            {
                RenderPlaceholder(canvas);
                return;
            }

            var renderer = SpectrumRendererFactory.CreateRenderer(_currentState.Style, _mainWindow.IsOverlayActive);
            var totalWidth = info.Width;
            var barCount = _mainWindow.BarCount;
            var barSpacing = Math.Min((float)_mainWindow.BarSpacing, totalWidth / (barCount + 1));
            var barWidth = Math.Max((totalWidth - (barCount - 1) * barSpacing) / barCount, 1.0f);
            barSpacing = (totalWidth - barCount * barWidth) / (barCount - 1);

            renderer.Render(canvas, spectrum.Spectrum, info, barWidth, barSpacing, barCount, _currentState.Paint);
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

        // Обновление стиля рендеринга происходит в даном методе
        public void UpdateRenderStyle(RenderStyle style)
        {
            EnsureNotDisposed();

            var currentPaint = _currentState.Paint;
            _currentState = new RenderState(
                Paint: currentPaint,
                Style: style,
                StyleName: _currentState.StyleName
            );
            RequestRender();

            Log.Debug("Обновлен стиль рендеринга: {Style}", style);
        }

        public void UpdateSpectrumStyle(string styleName, SKColor startColor, SKColor endColor)
        {
            EnsureNotDisposed();

            if (string.IsNullOrEmpty(styleName) || styleName == _currentState.StyleName)
                return;

            var (_, _, paint) = _spectrumStyles.GetColorsAndBrush(styleName);
            if (paint is null)
            {
                Log.Error("Кисть для рисования не настроена на стиль {StyleName}", styleName);
                return;
            }

            var oldPaint = _currentState.Paint;
            _currentState = new RenderState(
                Paint: paint.Clone(),
                Style: _currentState.Style,
                StyleName: styleName
            );

            oldPaint.Dispose();
            RequestRender();

            Log.Debug("Обновленный стиль Spectrum: {StyleName}", styleName);
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
                Log.Warning("Invalid render dimensions: {Width}x{Height}", width, height);
                return;
            }

            RequestRender();
            Log.Debug("Render dimensions updated: {Width}x{Height}", width, height);
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

            // Отписываемся от событий
            if (_mainWindow != null)
            {
                _mainWindow.PropertyChanged -= HandleMainWindowPropertyChanged;
            }

            _currentState.Paint.Dispose();
            _renderLock.Dispose();
            _disposalTokenSource.Dispose();

            if (_skElement is not null)
            {
                _skElement.PaintSurface -= RenderFrame;
                _skElement.Loaded -= HandleElementLoaded;
                _skElement.Unloaded -= HandleElementUnloaded;
                _skElement = null;
            }

            Log.Information("Рендерер успешно утилизирован");
        }

        #endregion
    }

    public readonly record struct PerformanceMetrics(
        double FrameTime,
        double Fps
    );

    public class RendererInitializationException : Exception
    {
        public RendererInitializationException(string message) : base(message) { }
        public RendererInitializationException(string message, Exception inner) : base(message, inner) { }
    }
}