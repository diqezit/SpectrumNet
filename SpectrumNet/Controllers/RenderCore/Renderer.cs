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

    private SKElement? _skElement;
    private RenderState _currentState = default!;
    private volatile bool _isAnalyzerDisposed;
    private volatile bool _shouldShowPlaceholder = true;
    private int _width, _height;
    private DateTime _lastRenderTime = DateTime.Now;

    public string CurrentStyleName => _currentState.StyleName;
    public RenderQuality CurrentQuality => _currentState.Quality;
    public event EventHandler<PerformanceMetrics>? PerformanceUpdate;

    public bool ShouldShowPlaceholder
    {
        get => _shouldShowPlaceholder;
        set => _shouldShowPlaceholder = value;
    }

    private static void ExecuteSafely(
        Action action,
        string source,
        string errorMessage) =>
        Safe(
            action,
            new ErrorHandlingOptions
            {
                Source = $"{Renderer.LogPrefix}.{source}",
                ErrorMessage = errorMessage
            });

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

        if (_analyzer is IComponent comp)
            comp.Disposed += OnAnalyzerDisposed;

        _shouldShowPlaceholder = !_controller.IsRecording;
        InitializeRenderer();

        Log(LogLevel.Information, LogPrefix, "Successfully initialized.", forceLog: true);
        PerformanceMetricsManager.PerformanceUpdated += OnPerformanceMetricsUpdated;
    }

    public void RequestRender()
    {
        if (_isDisposed)
            return;

        if (!_controller.IsOverlayActive || _skElement != _controller.SpectrumCanvas)
            _skElement?.InvalidateVisual();
    }

    public void UpdateRenderDimensions(int width, int height)
    {
        if (_isDisposed)
            return;

        if (!ValidateDimensions(width, height))
            return;

        _width = width;
        _height = height;
        RequestRender();
    }

    public void SynchronizeWithController()
    {
        if (_isDisposed)
            return;

        if (SynchronizeRenderSettings())
            RequestRender();
    }

    public void UpdateRenderStyle(RenderStyle style)
    {
        if (_isDisposed || _currentState.Style == style)
            return;

        _currentState = _currentState with { Style = style };
        RequestRender();
    }

    public void UpdateSpectrumStyle(string styleName, SKColor color, SKPaint brush)
    {
        if (_isDisposed || string.IsNullOrEmpty(styleName) || styleName == _currentState.StyleName)
            return;

        ApplySpectrumStyleUpdate(styleName, brush);
        RequestRender();
    }

    public void UpdateRenderQuality(RenderQuality quality)
    {
        if (_isDisposed || _currentState.Quality == quality)
            return;

        _currentState = _currentState with { Quality = quality };
        RendererFactory.GlobalQuality = quality;
        RequestRender();

        Log(
            LogLevel.Information,
            LogPrefix,
            $"Render quality updated to {quality}",
            forceLog: true);
    }

    public void RenderFrame(object? sender, SKPaintSurfaceEventArgs e)
    {
        if (_isDisposed)
            return;

        if (ShouldSkipRendering(sender))
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

    protected override void DisposeManaged()
    {
        if (_isDisposed)
            return;

        _disposalTokenSource.Cancel();
        UnsubscribeFromEvents();
        DisposeResources();

        Log(LogLevel.Information, LogPrefix, "Renderer disposed", forceLog: true);
    }

    protected override ValueTask DisposeAsyncManagedResources()
    {
        if (_isDisposed)
            return ValueTask.CompletedTask;

        _disposalTokenSource.Cancel();
        UnsubscribeFromEvents();
        DisposeResources();

        Log(LogLevel.Information, LogPrefix, "Renderer async disposed", forceLog: true);

        return ValueTask.CompletedTask;
    }

    private void OnAnalyzerDisposed(object? sender, EventArgs e) => _isAnalyzerDisposed = true;

    private void OnRendering(object? sender, EventArgs e) => RequestRender();

    private void OnPerformanceMetricsUpdated(object? sender, PerformanceMetrics metrics) =>
        PerformanceUpdate?.Invoke(this, metrics);

    private void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e?.PropertyName is null || _isDisposed)
            return;

        HandlePropertyChange(e.PropertyName);
    }

    private void OnElementLoaded(object? sender, RoutedEventArgs e)
    {
        if (_isDisposed)
            return;

        UpdateRenderDimensions((int)(_skElement?.ActualWidth ?? 0), (int)(_skElement?.ActualHeight ?? 0));
    }

    private void OnElementUnloaded(object? sender, RoutedEventArgs e)
    {
        if (_isDisposed)
            return;

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
                HandleOverlayActiveChange();
                break;

            case nameof(IMainController.SelectedDrawingType):
                UpdateRenderStyle(_controller.SelectedDrawingType);
                break;

            case nameof(IMainController.SelectedStyle) when !string.IsNullOrEmpty(_controller.SelectedStyle):
                HandleStyleChange();
                break;

            case nameof(IMainController.RenderQuality):
                UpdateRenderQuality(_controller.RenderQuality);
                break;

            case nameof(IMainController.WindowType):
            case nameof(IMainController.ScaleType):
                HandleAnalyzerSettingsChange();
                break;

            case nameof(IMainController.BarSpacing):
            case nameof(IMainController.BarCount):
            case nameof(IMainController.ShowPerformanceInfo):
                RequestRender();
                break;
        }
    }

    private void HandleOverlayActiveChange()
    {
        RendererFactory.ConfigureAllRenderers(_controller.IsOverlayActive);
        RequestRender();
    }

    private void HandleStyleChange()
    {
        var (color, brush) = _spectrumStyles.GetColorAndBrush(_controller.SelectedStyle);
        UpdateSpectrumStyle(_controller.SelectedStyle, color, brush);
    }

    private void HandleAnalyzerSettingsChange()
    {
        _analyzer.UpdateSettings(_controller.WindowType, _controller.ScaleType);
        SynchronizeWithController();
        RequestRender();
    }

    private void HandleRecordingStateChange(bool isRecording)
    {
        if (isRecording)
            CompositionTarget.Rendering += OnRendering;
        else
            CompositionTarget.Rendering -= OnRendering;

        _shouldShowPlaceholder = !isRecording;
        RequestRender();
    }

    private void UpdateRecordingState(bool isRecording)
    {
        if (_isDisposed)
            return;

        HandleRecordingStateChange(isRecording);
    }

    private void InitializeRenderer()
    {
        InitializeRenderState();
        AttachUIElementEvents();
        SetupControllerEvents();

        if (_controller.IsRecording)
            CompositionTarget.Rendering += OnRendering;
    }

    private void InitializeRenderState()
    {
        var (_, brush) = _spectrumStyles.GetColorAndBrush(DEFAULT_STYLE);
        _currentState = new RenderState(
            brush.Clone() ?? throw new InvalidOperationException($"{LogPrefix} Failed to initialize {DEFAULT_STYLE} style"),
            RenderStyle.Bars,
            DEFAULT_STYLE,
            RenderQuality.Medium);
    }

    private void AttachUIElementEvents()
    {
        if (_skElement is not null)
        {
            _skElement.PaintSurface += RenderFrame;
            _skElement.Loaded += OnElementLoaded;
            _skElement.Unloaded += OnElementUnloaded;
        }
    }

    private void SetupControllerEvents()
    {
        _controller.PropertyChanged += OnControllerPropertyChanged;
        SynchronizeWithController();
    }

    private bool SynchronizeRenderSettings()
    {
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

        return needsUpdate;
    }

    private void RenderFrameInternal(SKCanvas canvas, SKImageInfo info)
    {
        if (_isDisposed)
            return;

        PrepareCanvas(canvas);

        if (ShouldRenderPlaceholderFrame())
        {
            RenderPlaceholder(canvas, info);
            return;
        }

        RenderSpectrumData(canvas, info);
    }

    private void PrepareCanvas(SKCanvas canvas)
    {
        canvas.Clear(SKColors.Transparent);
        _lastRenderTime = DateTime.Now;
    }

    private bool ShouldRenderPlaceholderFrame() =>
        _controller.IsTransitioning || ShouldRenderPlaceholder();

    private void RenderSpectrumData(SKCanvas canvas, SKImageInfo info)
    {
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
        if (_isDisposed)
            return null;

        var analyzer = _controller.GetCurrentAnalyzer();
        if (analyzer is null || analyzer.IsDisposed)
        {
            _shouldShowPlaceholder = true;
            return null;
        }

        return FetchCurrentSpectrum(analyzer);
    }

    private SpectralData? FetchCurrentSpectrum(SpectrumAnalyzer analyzer)
    {
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
            Log(
                LogLevel.Error,
                LogPrefix,
                $"Error getting spectrum data: {ex.Message}");
            return null;
        }
    }

    private void RenderSpectrum(
        SKCanvas canvas,
        SpectralData spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount)
    {
        if (_isDisposed)
            return;

        var renderer = CreateRendererForCurrentState();

        RenderSpectrumWithRenderer(
            renderer,
            canvas,
            spectrum,
            info,
            barWidth,
            barSpacing,
            barCount);
    }

    private ISpectrumRenderer CreateRendererForCurrentState() =>
        RendererFactory.CreateRenderer(
            _currentState.Style,
            _controller.IsOverlayActive,
            _currentState.Quality);

    private void RenderSpectrumWithRenderer(
        ISpectrumRenderer renderer,
        SKCanvas canvas,
        SpectralData spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount)
    {
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

    private bool TryCalcRenderParams(
        SKImageInfo info,
        out float barWidth,
        out float barSpacing,
        out int barCount)
    {
        barCount = _controller.BarCount;
        barWidth = 0f;
        barSpacing = 0f;
        int totalWidth = info.Width;

        if (!ValidateRenderParameters(totalWidth, barCount))
            return false;

        CalculateBarDimensions(totalWidth, barCount, out barWidth, out barSpacing);
        return true;
    }

    private static bool ValidateRenderParameters(int totalWidth, int barCount) =>
        totalWidth > 0 && barCount > 0;

    private void CalculateBarDimensions(
        int totalWidth,
        int barCount,
        out float barWidth,
        out float barSpacing)
    {
        barSpacing = MathF.Min((float)_controller.BarSpacing, totalWidth / (barCount + 1));
        barWidth = MathF.Max((totalWidth - (barCount - 1) * barSpacing) / barCount, 1.0f);
        barSpacing = barCount > 1 ? (totalWidth - barCount * barWidth) / (barCount - 1) : 0;
    }

    private void RenderPlaceholder(SKCanvas? canvas, SKImageInfo info)
    {
        if (canvas is null || _isDisposed)
            return;

        _placeholder.CanvasSize = new SKSize(info.Width, info.Height);
        _placeholder.Render(canvas, info);
    }

    private void UnsubscribeFromUIElementEvents()
    {
        if (_skElement is null)
            return;

        try
        {
            if (_skElement.Dispatcher.CheckAccess())
                DetachUIElementEvents();
        }
        catch (Exception ex)
        {
            Log(
                LogLevel.Warning,
                LogPrefix,
                $"Error unsubscribing from UI events: {ex.Message}");
        }

        _skElement = null;
    }

    private void DetachUIElementEvents()
    {
        _skElement!.PaintSurface -= RenderFrame;
        _skElement!.Loaded -= OnElementLoaded;
        _skElement!.Unloaded -= OnElementUnloaded;
    }

    private bool ShouldSkipRendering(object? sender) =>
        _controller.IsOverlayActive && sender == _controller.SpectrumCanvas;

    private void HandleRenderFrameException(Exception ex, SKCanvas canvas, SKImageInfo info)
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

    private static bool ValidateDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            Log(
                LogLevel.Warning,
                LogPrefix,
                $"Invalid dimensions for renderer: {width}x{height}");
            return false;
        }
        return true;
    }

    private void ApplySpectrumStyleUpdate(string styleName, SKPaint brush)
    {
        var oldPaint = _currentState.Paint;
        ExecuteSafely(
            () =>
            {
                _currentState = _currentState with
                {
                    Paint = brush.Clone() ?? throw new InvalidOperationException("Brush clone failed"),
                    StyleName = styleName
                };
                oldPaint?.Dispose();
            },
            nameof(ApplySpectrumStyleUpdate),
            "Error updating spectrum style");
    }

    private void UnsubscribeFromEvents()
    {
        CompositionTarget.Rendering -= OnRendering;
        PerformanceMetricsManager.PerformanceUpdated -= OnPerformanceMetricsUpdated;

        if (_controller is INotifyPropertyChanged notifier)
            notifier.PropertyChanged -= OnControllerPropertyChanged;

        UnsubscribeFromUIElementEvents();
    }

    private void DisposeResources()
    {
        ExecuteSafely(
             () => _currentState.Paint?.Dispose(),
             "CurrentStatePaintDispose",
             "Error disposing current state paint");

        ExecuteSafely(
            () => _placeholder.Dispose(),
            "PlaceholderDispose",
            "Error disposing placeholder");

        ExecuteSafely(
            () => _renderLock.Dispose(),
            "RenderLockDispose",
            "Error disposing render lock");

        ExecuteSafely(
            () => _disposalTokenSource.Dispose(),
            "DisposalTokenSourceDispose",
            "Error disposing disposal token source");
    }
}