#nullable enable

namespace SpectrumNet.Controllers.RenderCore;

public sealed class Renderer : AsyncDisposableBase
{
    private const string DEFAULT_STYLE = "Solid";
    private const string LogPrefix = "Renderer";

    private record RenderState(
        SKPaint Paint,
        RenderStyle Style,
        string StyleName,
        RenderQuality Quality);

    private readonly SemaphoreSlim _renderLock = new(1, 1);
    private readonly SpectrumBrushes _spectrumStyles;
    private readonly IMainController _controller;
    private readonly SpectrumAnalyzer _analyzer;
    private readonly CancellationTokenSource _disposalTokenSource = new();
    private readonly RendererPlaceholder _placeholder = new() { CanvasSize = new SKSize(1, 1) };

    private readonly SKElement? _skElement;
    private RenderState _currentState = default!;
    private volatile bool _isAnalyzerDisposed;
    private volatile bool _shouldShowPlaceholder = true;
    private int _width, _height;
    private DateTime _lastRenderTime = DateTime.Now;
    public event EventHandler<PerformanceMetrics>? PerformanceUpdate;

    public bool ShouldShowPlaceholder
    {
        get => _shouldShowPlaceholder;
        set => _shouldShowPlaceholder = value;
    }

    public Renderer(
        SpectrumBrushes styles,
        IMainController controller,
        SpectrumAnalyzer analyzer,
        SKElement element)
    {
        _spectrumStyles = styles ?? throw new ArgumentNullException(nameof(styles));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        _skElement = element ?? throw new ArgumentNullException(nameof(element));

        ShouldShowPlaceholder = !_controller.IsRecording;
        InitializeRenderer();
    }

    public void RequestRender()
    {
        if (_isDisposed) return;
        SafeInvalidate();
    }

    public void UpdateRenderDimensions(int width, int height)
    {
        if (_isDisposed) return;
        if (!ValidateAndSetDimensions(width, height)) return;
        RequestRender();
    }

    public void SynchronizeWithController()
    {
        if (_isDisposed) return;
        if (SynchronizeRenderSettings())
            RequestRender();
    }

    public void UpdateRenderStyle(RenderStyle style)
    {
        if (_isDisposed || _currentState.Style == style) return;
        _currentState = _currentState with { Style = style };
        RequestRender();
    }

    public void UpdateSpectrumStyle(
        string styleName,
        SKColor color,
        SKPaint brush)
    {
        if (_isDisposed
            || string.IsNullOrEmpty(styleName)
            || styleName == _currentState.StyleName)
            return;

        ExecuteSafely(
            () =>
            {
                var oldPaint = _currentState.Paint;
                _currentState = _currentState with
                {
                    Paint = brush.Clone() ?? throw new InvalidOperationException("Brush clone failed"),
                    StyleName = styleName
                };
                oldPaint.Dispose();
            },
            nameof(UpdateSpectrumStyle),
            "Error updating spectrum style");

        RequestRender();
    }

    public void UpdateRenderQuality(RenderQuality quality)
    {
        if (_isDisposed || _currentState.Quality == quality) return;
        _currentState = _currentState with { Quality = quality };
        RendererFactory.GlobalQuality = quality;
        RequestRender();

        Log(LogLevel.Information,
            LogPrefix,
            $"Render quality updated to {quality}",
            forceLog: true);
    }

    public void RenderFrame(object? sender, SKPaintSurfaceEventArgs e)
    {
        if (_isDisposed) return;
        if (IsSkipRendering(sender))
        {
            e.Surface.Canvas.Clear(SKColors.Transparent);
            return;
        }

        Safe(
            () => RenderFrameInternal(e.Surface.Canvas, e.Info),
            new ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error rendering frame",
                ExceptionHandler = ex => HandleRenderFrameException(ex, e.Surface.Canvas, e.Info)
            });
    }

    protected override void DisposeManaged() => CleanUp("Renderer disposed");

    protected override ValueTask DisposeAsyncManagedResources()
    {
        CleanUp("Renderer async disposed");
        return ValueTask.CompletedTask;
    }

    private void InitializeRenderer()
    {
        InitializeRenderState();
        SubscribeToEvents();
        AttachUIElementEvents();

        if (_controller.IsRecording)
            CompositionTarget.Rendering += OnRendering;

        Log(LogLevel.Information,
            LogPrefix,
            "Successfully initialized.",
            forceLog: true);
    }

    private void InitializeRenderState()
    {
        var (_, brush) = _spectrumStyles.GetColorAndBrush(DEFAULT_STYLE);
        _currentState = new RenderState(
            brush.Clone()
            ?? throw new InvalidOperationException($"{LogPrefix} Failed to initialize {DEFAULT_STYLE} style"),
            RenderStyle.Bars,
            DEFAULT_STYLE,
            RenderQuality.Medium);
    }

    private void SubscribeToEvents()
    {
        PerformanceMetricsManager.PerformanceUpdated += OnPerformanceMetricsUpdated;
        _controller.PropertyChanged += OnControllerPropertyChanged;
        if (_analyzer is IComponent comp)
            comp.Disposed += OnAnalyzerDisposed;
    }

    private void CleanUp(string message)
    {
        if (_isDisposed) return;
        _disposalTokenSource.Cancel();
        UnsubscribeFromEvents();
        DisposeResources();
        Log(LogLevel.Information,
            LogPrefix,
            message,
            forceLog: true);
    }

    private void SafeInvalidate()
    {
        if (!_controller.IsOverlayActive
            || _skElement != _controller.SpectrumCanvas)
            _skElement?.InvalidateVisual();
    }

    private bool ValidateAndSetDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            Log(LogLevel.Warning,
                LogPrefix,
                $"Invalid dimensions: {width}x{height}");
            return false;
        }
        _width = width;
        _height = height;
        return true;
    }

    private bool IsSkipRendering(object? sender) =>
        _controller.IsOverlayActive && sender == _controller.SpectrumCanvas;

    private void OnAnalyzerDisposed(object? sender, EventArgs e) => _isAnalyzerDisposed = true;

    private void OnRendering(object? sender, EventArgs e) => RequestRender();

    private void OnPerformanceMetricsUpdated(object? sender, PerformanceMetrics metrics) =>
        PerformanceUpdate?.Invoke(this, metrics);

    private void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e?.PropertyName is null || _isDisposed) return;
        HandlePropertyChange(e.PropertyName);
    }

    private void OnElementLoaded(object? sender, RoutedEventArgs e)
    {
        if (_isDisposed) return;
        UpdateRenderDimensions((int)(_skElement?.ActualWidth ?? 0),
                               (int)(_skElement?.ActualHeight ?? 0));
    }

    private void OnElementUnloaded(object? sender, RoutedEventArgs e)
    {
        if (_isDisposed) return;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void HandlePropertyChange(string propertyName)
    {
        switch (propertyName)
        {
            case nameof(IMainController.IsRecording):
                UpdateRecordingState(_controller.IsRecording);
                break;
            case nameof(IMainController.IsOverlayActive):
                RendererFactory.ConfigureAllRenderers(_controller.IsOverlayActive);
                RequestRender();
                break;
            case nameof(IMainController.SelectedDrawingType):
                UpdateRenderStyle(_controller.SelectedDrawingType);
                break;
            case nameof(IMainController.SelectedStyle) 
            when !string.IsNullOrEmpty(_controller.SelectedStyle):
                var (clr, br) = _spectrumStyles.GetColorAndBrush(_controller.SelectedStyle);
                UpdateSpectrumStyle(_controller.SelectedStyle, clr, br);
                break;
            case nameof(IMainController.RenderQuality):
                UpdateRenderQuality(_controller.RenderQuality);
                break;
            case nameof(IMainController.WindowType):
            case nameof(IMainController.ScaleType):
                _analyzer.UpdateSettings(_controller.WindowType, _controller.ScaleType);
                SynchronizeWithController();
                RequestRender();
                break;
            case nameof(IMainController.BarSpacing):
            case nameof(IMainController.BarCount):
            case nameof(IMainController.ShowPerformanceInfo):
                RequestRender();
                break;
        }
    }

    private void UpdateRecordingState(bool isRecording)
    {
        if (_isDisposed) return;
        if (isRecording) CompositionTarget.Rendering += OnRendering;
        else CompositionTarget.Rendering -= OnRendering;
        ShouldShowPlaceholder = !isRecording;
        RequestRender();
    }

    private void AttachUIElementEvents()
    {
        if (_skElement is null) return;
        _skElement.PaintSurface += RenderFrame;
        _skElement.Loaded += OnElementLoaded;
        _skElement.Unloaded += OnElementUnloaded;
    }

    private void DetachUIElementEvents()
    {
        if (_skElement is null) return;
        _skElement.PaintSurface -= RenderFrame;
        _skElement.Loaded -= OnElementLoaded;
        _skElement.Unloaded -= OnElementUnloaded;
    }

    private bool SynchronizeRenderSettings()
    {
        bool needsUpdate = false;
        if (_currentState.Style != _controller.SelectedDrawingType)
        {
            UpdateRenderStyle(_controller.SelectedDrawingType);
            needsUpdate = true;
        }
        if (!string.IsNullOrEmpty(_controller.SelectedStyle)
            && _controller.SelectedStyle != _currentState.StyleName)
        {
            var (clr, br) = _spectrumStyles.GetColorAndBrush(_controller.SelectedStyle);
            UpdateSpectrumStyle(_controller.SelectedStyle, clr, br);
            needsUpdate = true;
        }
        if (_currentState.Quality != _controller.RenderQuality)
        {
            UpdateRenderQuality(_controller.RenderQuality);
            needsUpdate = true;
        }
        return needsUpdate;
    }

    private void RenderFrameInternal(SKCanvas canvas, SKImageInfo info)
    {
        if (_isDisposed) return;
        canvas.Clear(SKColors.Transparent);
        _lastRenderTime = DateTime.Now;
        if (_controller.IsTransitioning || ShouldShowPlaceholder)
        {
            RenderPlaceholder(canvas, info);
            return;
        }
        var spectrum = GetSpectrumData();
        if (spectrum is null)
        {
            if (!_controller.IsRecording) RenderPlaceholder(canvas, info);
            return;
        }
        if (!TryCalcRenderParams(
            info,
            out float barWidth,
            out float barSpacing,
            out int barCount))
        {
            RenderPlaceholder(canvas, info);
            return;
        }
        var renderer = RendererFactory.CreateRenderer(
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
            (c, i) => 
            PerformanceMetricsManager.DrawPerformanceInfo(c, i, _controller.ShowPerformanceInfo));
        PerformanceMetricsManager.UpdateMetrics();
    }

    private SpectralData? GetSpectrumData()
    {
        if (_isDisposed) return null;
        var analyzer = _controller.GetCurrentAnalyzer();
        if (analyzer is null || analyzer.IsDisposed)
        {
            _shouldShowPlaceholder = true;
            return null;
        }
        try
        {
            return analyzer.GetCurrentSpectrum();
        }
        catch (ObjectDisposedException)
        {
            _isAnalyzerDisposed = _shouldShowPlaceholder = true;
            return null;
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error,
                LogPrefix,
                $"Error getting spectrum data: {ex.Message}");
            return null;
        }
    }

    private bool TryCalcRenderParams(
        SKImageInfo info,
        out float barWidth,
        out float barSpacing,
        out int barCount)
    {
        barCount = _controller.BarCount;
        barWidth = barSpacing = 0;
        int totalWidth = info.Width;
        if (totalWidth <= 0 || barCount <= 0) return false;
        barSpacing = MathF.Min((float)_controller.BarSpacing, totalWidth / (barCount + 1));
        barWidth = MathF.Max((totalWidth - (barCount - 1) * barSpacing) / barCount, 1.0f);
        barSpacing = barCount > 1 ? (totalWidth - barCount * barWidth) / (barCount - 1) : 0;
        return true;
    }

    private void RenderPlaceholder(SKCanvas canvas, SKImageInfo info)
    {
        if (_isDisposed) return;
        _placeholder.CanvasSize = new SKSize(info.Width, info.Height);
        _placeholder.Render(canvas, info);
    }

    private void HandleRenderFrameException(
        Exception ex,
        SKCanvas canvas,
        SKImageInfo info)
    {
        if (ex is ObjectDisposedException)
        {
            _isAnalyzerDisposed = true;
            CompositionTarget.Rendering -= OnRendering;
        }
        else if (!_isDisposed)
        {
            RenderPlaceholder(canvas, info);
        }
    }

    private void UnsubscribeFromEvents()
    {
        CompositionTarget.Rendering -= OnRendering;
        PerformanceMetricsManager.PerformanceUpdated -= OnPerformanceMetricsUpdated;
        if (_controller is INotifyPropertyChanged notifier)
            notifier.PropertyChanged -= OnControllerPropertyChanged;
        DetachUIElementEvents();
    }

    private void DisposeResources()
    {
        ExecuteSafely(() => _currentState.Paint.Dispose(), 
            nameof(DisposeResources), "Error disposing paint");

        ExecuteSafely(() => _placeholder.Dispose(),
            nameof(DisposeResources), "Error disposing placeholder");

        ExecuteSafely(() => _renderLock.Dispose(), 
            nameof(DisposeResources), "Error disposing lock");

        ExecuteSafely(() => _disposalTokenSource.Dispose(), 
            nameof(DisposeResources), "Error disposing token source");
    }

    private static void ExecuteSafely(Action action, string source, string errorMessage) =>
        Safe(
            action,
            new ErrorHandlingOptions
            {
                Source = $"{LogPrefix}.{source}",
                ErrorMessage = errorMessage
            });
}