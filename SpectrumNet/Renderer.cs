#nullable enable
namespace SpectrumNet;

public sealed class Renderer : IDisposable
{
    #region Constants
    private const int RENDER_TIMEOUT_MS = 16;
    private const string DEFAULT_STYLE = "Gradient";
    private const string MESSAGE = "Push start to begin record...";
    private const string LogPrefix = "[Renderer] ";
    #endregion

    #region Private Fields
    private readonly record struct RenderState(SKPaint Paint, RenderStyle Style, string StyleName);

    private readonly SemaphoreSlim _renderLock = new(1, 1);
    private readonly SpectrumBrushes _spectrumStyles;
    private readonly MainWindow _mainWindow;
    private readonly SpectrumAnalyzer _analyzer;
    private readonly CancellationTokenSource _disposalTokenSource = new();
    private readonly Stopwatch _performanceMonitor;

    private SKElement? _skElement;
    private DispatcherTimer? _renderTimer;
    private RenderState _currentState;
    private volatile bool _isDisposed, _isAnalyzerDisposed, _shouldShowPlaceholder;
    #endregion

    #region Public Properties
    public string CurrentStyleName => _currentState.StyleName;
    public event EventHandler<PerformanceMetrics>? PerformanceUpdate;
    #endregion

    #region Constructor
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

        Log.Information($"{LogPrefix}successfully initialized.");
    }
    #endregion

    #region Initialization
    private void InitializeRenderer()
    {
        try
        {
            var (_, _, paint) = _spectrumStyles.GetColorsAndBrush(DEFAULT_STYLE);
            if (paint == null)
                throw new InvalidOperationException($"{LogPrefix}Failed to initialize {DEFAULT_STYLE} style");

            _currentState = new RenderState(paint.Clone(), RenderStyle.Bars, DEFAULT_STYLE);

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
            Log.Error(ex, $"{LogPrefix}Failed to initialize renderer");
            throw new InvalidOperationException($"{LogPrefix}Failed to initialize renderer", ex);
        }
    }
    #endregion

    #region Event Handlers
    private void OnMainWindowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindow.IsRecording))
        {
            _shouldShowPlaceholder = !_mainWindow.IsRecording;
            RequestRender();
            Log.Debug($"{LogPrefix}Placeholder state updated: {_shouldShowPlaceholder}");
        }
        else if (e.PropertyName == nameof(MainWindow.IsOverlayActive))
        {
            // Update all renderers when overlay state changes
            SpectrumRendererFactory.ConfigureAllRenderers(_mainWindow.IsOverlayActive);
            RequestRender();
            Log.Debug($"{LogPrefix}Overlay state updated: {_mainWindow.IsOverlayActive}");
        }
    }

    private void SubscribeToEvents()
    {
        if (_skElement == null)
            return;

        _skElement.PaintSurface += RenderFrame;
        _skElement.Loaded += OnElementLoaded;
        _skElement.Unloaded += OnElementUnloaded;
    }

    private void OnElementLoaded(object sender, RoutedEventArgs e) =>
        UpdateRenderDimensions((int)_skElement!.ActualWidth, (int)_skElement.ActualHeight);

    private void OnElementUnloaded(object sender, RoutedEventArgs e)
    {
        _renderTimer?.Stop();
        _performanceMonitor.Stop();
    }
    #endregion

    #region Render Methods
    public void RenderFrame(object? sender, SKPaintSurfaceEventArgs e)
    {
        // Skip rendering on the main canvas when overlay is active
        if (_mainWindow.IsOverlayActive && sender == _mainWindow.spectrumCanvas)
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
            Log.Warning($"{LogPrefix}SpectrumAnalyzer was disposed, stopping render loop");
            _renderTimer?.Stop();
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"{LogPrefix}Error rendering frame");
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
            _isAnalyzerDisposed = _shouldShowPlaceholder = true;
            Log.Warning($"{LogPrefix}SpectrumAnalyzer was disposed during spectrum acquisition");
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

        renderer.Render(canvas, spectrum.Spectrum, info, barWidth, barSpacing, barCount, _currentState.Paint, PerfomanceMetrics.DrawPerformanceInfo);
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
    }
    #endregion

    #region Public Methods
    public void UpdateRenderStyle(RenderStyle style)
    {
        EnsureNotDisposed();

        _currentState = _currentState with { Style = style };
        RequestRender();
        Log.Debug($"{LogPrefix}Render style updated: {style}");
    }

    public void UpdateSpectrumStyle(string styleName, SKColor startColor, SKColor endColor)
    {
        EnsureNotDisposed();

        if (string.IsNullOrEmpty(styleName) || styleName == _currentState.StyleName)
            return;

        var (_, _, paint) = _spectrumStyles.GetColorsAndBrush(styleName);

        if (paint == null)
        {
            Log.Error($"{LogPrefix}Brush for style {styleName} is not configured");
            return;
        }

        var oldPaint = _currentState.Paint;
        _currentState = new RenderState(paint.Clone(), _currentState.Style, styleName);
        oldPaint.Dispose();

        RequestRender();
        Log.Debug($"{LogPrefix}Spectrum style updated: {styleName}");
    }

    public void RequestRender()
    {
        // Only invalidate the main canvas if overlay is not active
        if (!_mainWindow.IsOverlayActive || _skElement != _mainWindow.spectrumCanvas)
        {
            _skElement?.InvalidateVisual();
        }
    }

    public void UpdateRenderDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            Log.Warning($"{LogPrefix}Invalid render dimensions: {width}x{height}");
            return;
        }

        RequestRender();
        Log.Debug($"{LogPrefix}Render dimensions updated: {width}x{height}");
    }
    #endregion

    #region Private Methods
    private void EmitPerformanceMetrics()
    {
        var elapsed = _performanceMonitor.Elapsed;
        _performanceMonitor.Restart();
        PerformanceUpdate?.Invoke(this, new PerformanceMetrics(elapsed.TotalMilliseconds, 1000.0 / elapsed.TotalMilliseconds));
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
        if (_isDisposed)
            return;

        _isDisposed = _isAnalyzerDisposed = true;

        _disposalTokenSource.Cancel();
        _renderTimer?.Stop();
        _performanceMonitor.Stop();

        if (_mainWindow is INotifyPropertyChanged notifier)
            notifier.PropertyChanged -= OnMainWindowPropertyChanged;

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

        Log.Information($"{LogPrefix}Renderer successfully disposed");
    }
    #endregion
}

#region PerformanceMetrics Struct
public readonly record struct PerformanceMetrics(double FrameTime, double Fps);
#endregion