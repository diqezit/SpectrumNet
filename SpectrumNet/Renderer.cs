#nullable enable
namespace SpectrumNet;

sealed class Renderer : IDisposable
{
    const int RENDER_TIMEOUT_MS = 16;
    const string DEFAULT_STYLE = "Gradient";
    const string MESSAGE = "Push start to begin record...";
    const string LogPrefix = "[Renderer] ";

    record struct RenderState(SKPaint Paint, RenderStyle Style, string StyleName);

    readonly SemaphoreSlim _renderLock = new(1, 1);
    readonly SpectrumBrushes _spectrumStyles;
    readonly MainWindow _mainWindow;
    readonly SpectrumAnalyzer _analyzer;
    readonly CancellationTokenSource _disposalTokenSource = new();
    readonly Stopwatch _performanceMonitor = new();

    SKElement? _skElement;
    DispatcherTimer? _renderTimer;
    RenderState _currentState;
    volatile bool _isDisposed, _isAnalyzerDisposed, _shouldShowPlaceholder = true;
    SpectralData? _cachedSpectrum;
    public string CurrentStyleName => _currentState.StyleName;
    public event EventHandler<PerformanceMetrics>? PerformanceUpdate;

    public Renderer(SpectrumBrushes styles, MainWindow window, SpectrumAnalyzer analyzer, SKElement element)
    {
        _spectrumStyles = styles ?? throw new ArgumentNullException(nameof(styles));
        _mainWindow = window ?? throw new ArgumentNullException(nameof(window));
        _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        _skElement = element ?? throw new ArgumentNullException(nameof(element));

        if (_analyzer is IComponent comp)
            comp.Disposed += (_, _) => _isAnalyzerDisposed = true;

        InitializeRenderer();
        SmartLogger.Log(LogLevel.Information, LogPrefix, "successfully initialized.", forceLog: true);
    }

    void InitializeRenderer()
    {
        try
        {
            var (_, _, paint) = _spectrumStyles.GetColorsAndBrush(DEFAULT_STYLE);
            if (paint is null)
                throw new InvalidOperationException($"{LogPrefix}Failed to initialize {DEFAULT_STYLE} style");

            _currentState = new RenderState(paint.Clone(), RenderStyle.Bars, DEFAULT_STYLE);

            _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RENDER_TIMEOUT_MS) };
            _renderTimer.Tick += (_, _) => RequestRender();
            _renderTimer.Start();

            _skElement!.PaintSurface += RenderFrame;
            _skElement.Loaded += OnElementLoaded;
            _skElement.Unloaded += OnElementUnloaded;

            _performanceMonitor.Start();
            _mainWindow.PropertyChanged += OnMainWindowPropertyChanged;
            SynchronizeWithMainWindow();
        }
        catch (Exception ex)
        {
            SmartLogger.Log(LogLevel.Error, LogPrefix, $"Failed to initialize renderer: {ex}");
            throw new InvalidOperationException($"{LogPrefix}Failed to initialize renderer", ex);
        }
    }

    void OnMainWindowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindow.IsRecording))
        {
            _shouldShowPlaceholder = !_mainWindow.IsRecording;
            RequestRender();
            SmartLogger.Log(LogLevel.Debug, LogPrefix, $"Placeholder state updated: {_shouldShowPlaceholder}", forceLog: true);
        }
        else if (e.PropertyName == nameof(MainWindow.IsOverlayActive))
        {
            SpectrumRendererFactory.ConfigureAllRenderers(_mainWindow.IsOverlayActive);
            RequestRender();
            SmartLogger.Log(LogLevel.Debug, LogPrefix, $"Overlay state updated: {_mainWindow.IsOverlayActive}", forceLog: true);
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
            SmartLogger.Log(_mainWindow.IsTransitioning ? LogLevel.Debug : LogLevel.Warning, LogPrefix,
                "SpectrumAnalyzer was disposed, stopping render loop");
            _renderTimer?.Stop();
        }
        catch (Exception ex)
        {
            SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error rendering frame: {ex}");
        }
        finally
        {
            _renderLock.Release();
        }
    }

    void RenderFrameInternal(SKCanvas canvas, SKImageInfo info)
    {
        canvas.Clear(SKColors.Transparent);

        if (_mainWindow.IsTransitioning)
        {
            return; 
        }

        if (_shouldShowPlaceholder || _isAnalyzerDisposed || _analyzer is null)
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
            SmartLogger.Log(LogLevel.Warning, LogPrefix, "SpectrumAnalyzer was disposed during spectrum acquisition");
            RenderPlaceholder(canvas);
            return;
        }
        catch (Exception ex)
        {
            SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error obtaining spectrum data: {ex}");
        }

        if (spectrum?.Spectrum is not { Length: > 0 })
        {
            RenderPlaceholder(canvas);
            return;
        }

        if (!TryCalcRenderParams(info, out float barWidth, out float barSpacing, out int barCount))
        {
            RenderPlaceholder(canvas);
            return;
        }

        try
        {
            var renderer = SpectrumRendererFactory.CreateRenderer(_currentState.Style, _mainWindow.IsOverlayActive);
            renderer.Render(canvas, spectrum.Spectrum, info, barWidth, barSpacing, barCount,
                _currentState.Paint, PerfomanceMetrics.DrawPerformanceInfo);
        }
        catch (Exception ex)
        {
            SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error during spectrum rendering: {ex}");
            RenderPlaceholder(canvas);
        }
    }

    bool TryCalcRenderParams(SKImageInfo info, out float barWidth, out float barSpacing, out int barCount)
    {
        barWidth = barSpacing = 0;
        barCount = _mainWindow.BarCount;
        int totalWidth = info.Width;
        if (totalWidth <= 0 || barCount <= 0)
            return false;
        barSpacing = Math.Min((float)_mainWindow.BarSpacing, totalWidth / (barCount + 1));
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
        canvas.Clear(SKColors.Transparent);
        canvas.DrawText(MESSAGE, 50, 100, paint);
    }

    public void SynchronizeWithMainWindow()
    {
        try
        {
            EnsureNotDisposed();
            bool needsUpdate = false;
            if (_currentState.Style != _mainWindow.SelectedDrawingType)
            {
                UpdateRenderStyle(_mainWindow.SelectedDrawingType);
                needsUpdate = true;
            }
            var styleName = _mainWindow.SelectedStyle;
            if (!string.IsNullOrEmpty(styleName) && styleName != _currentState.StyleName)
            {
                var (startColor, endColor, _) = _spectrumStyles.GetColorsAndBrush(styleName);
                UpdateSpectrumStyle(styleName, startColor, endColor);
                needsUpdate = true;
            }
            if (needsUpdate)
            {
                RequestRender();
                SmartLogger.Log(LogLevel.Debug, LogPrefix, "Renderer synchronized with MainWindow settings", forceLog: true);
            }
        }
        catch (Exception ex)
        {
            SmartLogger.Log(LogLevel.Error, LogPrefix, $"Failed to synchronize renderer with MainWindow: {ex.Message}");
        }
    }

    public void UpdateRenderStyle(RenderStyle style)
    {
        EnsureNotDisposed();
        if (_currentState.Style == style)
            return;
        _currentState = _currentState with { Style = style };
        RequestRender();
        SmartLogger.Log(LogLevel.Debug, LogPrefix, $"Render style updated: {style}", forceLog: true);
    }

    public void UpdateSpectrumStyle(string styleName, SKColor startColor, SKColor endColor)
    {
        EnsureNotDisposed();
        if (string.IsNullOrEmpty(styleName) || styleName == _currentState.StyleName)
            return;
        var (_, _, paint) = _spectrumStyles.GetColorsAndBrush(styleName);
        if (paint is null)
        {
            SmartLogger.Log(LogLevel.Error, LogPrefix, $"Brush for style {styleName} is not configured");
            return;
        }
        var oldPaint = _currentState.Paint;
        _currentState = new RenderState(paint.Clone(), _currentState.Style, styleName);
        oldPaint.Dispose();
        RequestRender();
        SmartLogger.Log(LogLevel.Debug, LogPrefix, $"Spectrum style updated: {styleName}", forceLog: true);
    }

    public void RequestRender()
    {
        if (!_mainWindow.IsOverlayActive || _skElement != _mainWindow.spectrumCanvas)
            _skElement?.InvalidateVisual();
    }

    public void UpdateRenderDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            SmartLogger.Log(LogLevel.Warning, LogPrefix, $"Invalid render dimensions: {width}x{height}");
            return;
        }
        RequestRender();
        SmartLogger.Log(LogLevel.Debug, LogPrefix, $"Render dimensions updated: {width}x{height}", forceLog: true);
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
        SmartLogger.Log(LogLevel.Information, LogPrefix, "Renderer successfully disposed", forceLog: true);
    }
}

readonly record struct PerformanceMetrics(double FrameTime, double Fps);