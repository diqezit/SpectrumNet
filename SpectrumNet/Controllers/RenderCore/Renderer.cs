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
    private readonly FrameCache _frameCache = new();
    private readonly IRendererFactory _rendererFactory;

    private readonly SKElement? _skElement;
    private RenderState _currentState = default!;
    private volatile bool _isAnalyzerDisposed;
    private volatile bool _shouldShowPlaceholder = true;
    private bool _updatingQuality;
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
        SKElement element,
        IRendererFactory rendererFactory)
    {
        _spectrumStyles = styles ?? throw new ArgumentNullException(nameof(styles));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        _skElement = element ?? throw new ArgumentNullException(nameof(element));
        _rendererFactory = rendererFactory ?? throw new ArgumentNullException(nameof(rendererFactory));

        ShouldShowPlaceholder = !_controller.IsRecording;
        InitializeRenderer();
    }

    private void InitializeRenderer()
    {
        InitializeRenderState();
        SubscribeToEvents();
        AttachUIElementEvents();

        FpsLimiter.Instance.IsEnabled = _controller.LimitFpsTo60;

        if (_controller.IsRecording)
            CompositionTarget.Rendering += OnRendering;
    }

    private void InitializeRenderState()
    {
        var (_, brush) = _spectrumStyles.GetColorAndBrush(DEFAULT_STYLE);
        _currentState = new RenderState(
            brush.Clone()
            ?? throw new InvalidOperationException($"{LogPrefix} Failed to initialize {DEFAULT_STYLE} style"),
            _controller.SelectedDrawingType,
            DEFAULT_STYLE,
            RenderQuality.Medium);
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
        _frameCache.MarkDirty();
        RequestRender();
    }

    public void SynchronizeWithController()
    {
        if (_isDisposed) return;
        if (SynchronizeRenderSettings())
        {
            _frameCache.MarkDirty();
            RequestRender();
        }
    }

    public void UpdateRenderStyle(RenderStyle style)
    {
        if (_isDisposed || _currentState.Style == style) return;

        _currentState = _currentState with { Style = style };
        _frameCache.MarkDirty();
        RequestRender();
    }

    public void UpdateSpectrumStyle(
        string styleName,
        SKColor color,
        SKPaint brush)
    {
        if (_isDisposed
            || string.IsNullOrEmpty(styleName)
            || styleName == _currentState.StyleName) return;

        ExecuteSafely(
            () => UpdatePaintAndStyleName(styleName, brush),
            nameof(UpdateSpectrumStyle),
            "Error updating spectrum style");

        _frameCache.MarkDirty();
        RequestRender();
    }

    public void UpdateRenderQuality(RenderQuality quality)
    {
        if (_isDisposed || _currentState.Quality == quality || _updatingQuality) return;

        try
        {
            _updatingQuality = true;
            _currentState = _currentState with { Quality = quality };
            _frameCache.MarkDirty();
            RequestRender();
        }
        finally
        {
            _updatingQuality = false;
        }
    }

    public void RenderFrame(object? sender, SKPaintSurfaceEventArgs e)
    {
        if (ShouldSkipRenderingFrame(sender))
        {
            ClearCanvas(e.Surface.Canvas);
            return;
        }

        if (ShouldRenderNewFrame())
        {
            RenderNewFrame(sender, e);
        }
        else
        {
            RenderCachedFrame(e);
        }
    }

    private bool ShouldSkipRenderingFrame(object? sender)
    {
        if (_isDisposed) return true;
        return IsSkipRendering(sender);
    }

    private bool ShouldRenderNewFrame()
    {
        if (FpsLimiter.Instance.IsEnabled)
        {
            return FpsLimiter.Instance.ShouldRenderFrame() ||
                   _frameCache.IsDirty ||
                   ShouldShowPlaceholder ||
                   _frameCache.ShouldForceRefresh();
        }

        return true;
    }

    private void RenderNewFrame(object? sender, SKPaintSurfaceEventArgs e)
    {
        ClearCanvas(e.Surface.Canvas);
        Safe(
            () => RenderFrameInternal(e.Surface.Canvas, e.Info),
            new ErrorHandlingOptions
            {
                Source = LogPrefix,
                ErrorMessage = "Error rendering frame",
                ExceptionHandler = ex => HandleRenderFrameException(ex, e.Surface.Canvas, e.Info)
            });

        UpdateFrameCache(e);
        RecordFrameMetrics();
    }

    private void RenderCachedFrame(SKPaintSurfaceEventArgs e) =>
        _frameCache.DrawCachedFrame(e.Surface.Canvas);

    private static void ClearCanvas(SKCanvas canvas) =>
        canvas.Clear(SKColors.Transparent);

    private void UpdateFrameCache(SKPaintSurfaceEventArgs e) =>
        _frameCache.UpdateCache(e.Surface, e.Info);

    private static void RecordFrameMetrics() =>
        PerformanceMetricsManager.RecordFrameTime();

    private void RenderFrameInternal(SKCanvas canvas, SKImageInfo info)
    {
        if (_isDisposed) return;

        _lastRenderTime = DateTime.Now;

        if (ShouldRenderPlaceholder())
        {
            RenderPlaceholder(canvas, info);
            return;
        }

        RenderSpectrumData(canvas, info);
    }

    private void RenderSpectrumData(SKCanvas canvas, SKImageInfo info)
    {
        var spectrum = GetSpectrumData();
        if (spectrum is null)
        {
            HandleMissingSpectrumData(canvas, info);
            return;
        }

        if (!TryCalculateRenderParameters(
            info,
            out float barWidth,
            out float barSpacing,
            out int barCount))
        {
            RenderPlaceholder(canvas, info);
            return;
        }

        RenderSpectrum(canvas, info, spectrum, barWidth, barSpacing, barCount);
    }

    private void HandleMissingSpectrumData(SKCanvas canvas, SKImageInfo info)
    {
        if (!_controller.IsRecording)
            RenderPlaceholder(canvas, info);
    }

    private bool ShouldRenderPlaceholder() =>
        _controller.IsTransitioning || ShouldShowPlaceholder;

    private void RenderSpectrum(
        SKCanvas canvas,
        SKImageInfo info,
        SpectralData spectrum,
        float barWidth,
        float barSpacing,
        int barCount)
    {
        var currentStyle = _currentState.Style;
        var renderer = GetConfiguredRenderer(currentStyle);

        ValidateStyleConsistency(currentStyle);

        RenderSpectrumWithRenderer(
            renderer,
            canvas,
            spectrum,
            info,
            barWidth,
            barSpacing,
            barCount,
            _currentState.Paint,
            (c, i) => PerformanceMetricsManager.DrawPerformanceInfo(c, i, _controller.ShowPerformanceInfo));
    }

    private ISpectrumRenderer GetConfiguredRenderer(RenderStyle style)
    {
        return _rendererFactory.CreateRenderer(
            style,
            _controller.IsOverlayActive,
            _currentState.Quality);
    }

    private static void RenderSpectrumWithRenderer(
        ISpectrumRenderer renderer,
        SKCanvas canvas,
        SpectralData spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint paint,
        Action<SKCanvas, SKImageInfo> drawPerformanceInfo)
    {
        renderer.Render(
            canvas,
            spectrum.Spectrum,
            info,
            barWidth,
            barSpacing,
            barCount,
            paint,
            drawPerformanceInfo);
    }

    private void ValidateStyleConsistency(RenderStyle expectedStyle)
    {
        if (_currentState.Style != expectedStyle)
        {
            Log(LogLevel.Warning,
                LogPrefix,
                $"RenderFrameInternal: " +
                $"Style changed from {expectedStyle} " +
                $"to {_currentState.Style} after getting renderer, resetting",
                forceLog: true);

            _currentState = _currentState with { Style = expectedStyle };
        }
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
            LogSpectrumDataError(ex);
            return null;
        }
    }

    private static void LogSpectrumDataError(Exception ex)
    {
        Log(LogLevel.Error,
            LogPrefix,
            $"Error getting spectrum data: {ex.Message}");
    }

    private bool TryCalculateRenderParameters(
        SKImageInfo info,
        out float barWidth,
        out float barSpacing,
        out int barCount)
    {
        barCount = _controller.BarCount;
        barWidth = barSpacing = 0;

        int totalWidth = info.Width;
        if (totalWidth <= 0 || barCount <= 0) return false;

        return CalculateBarDimensions(totalWidth, barCount, out barWidth, out barSpacing);
    }

    private bool CalculateBarDimensions(
        int totalWidth,
        int barCount,
        out float barWidth,
        out float barSpacing)
    {
        barSpacing = MathF.Min((float)_controller.BarSpacing, totalWidth / (barCount + 1f));
        barWidth = MathF.Max((totalWidth - (barCount - 1) * barSpacing) / barCount, 1.0f);

        if (barCount > 1)
        {
            barSpacing = (totalWidth - barCount * barWidth) / (barCount - 1);
        }
        else
        {
            barSpacing = 0;
        }

        return barWidth >= 1.0f;
    }

    private void RenderPlaceholder(SKCanvas canvas, SKImageInfo info)
    {
        if (_isDisposed) return;
        UpdatePlaceholderSize(info);
        _placeholder.Render(canvas, info);
    }

    private void UpdatePlaceholderSize(SKImageInfo info) =>
        _placeholder.CanvasSize = new SKSize(info.Width, info.Height);

    private void HandleRenderFrameException(Exception ex, SKCanvas canvas, SKImageInfo info)
    {
        if (ex is ObjectDisposedException)
        {
            HandleObjectDisposedException();
        }
        else if (!_isDisposed)
        {
            RenderPlaceholder(canvas, info);
        }
    }

    private void HandleObjectDisposedException()
    {
        _isAnalyzerDisposed = true;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void UpdatePaintAndStyleName(string styleName, SKPaint brush)
    {
        var oldPaint = _currentState.Paint;
        _currentState = _currentState with
        {
            Paint = brush.Clone()
            ?? throw new InvalidOperationException("Brush clone failed"),
            StyleName = styleName
        };
        oldPaint.Dispose();
    }

    private bool SynchronizeRenderSettings()
    {
        bool needsUpdate = false;
        needsUpdate |= SynchronizeRenderStyle();
        needsUpdate |= SynchronizeStyleName();
        needsUpdate |= SynchronizeRenderQuality();
        return needsUpdate;
    }

    private bool SynchronizeRenderStyle()
    {
        if (_currentState.Style != _controller.SelectedDrawingType)
        {
            UpdateRenderStyle(_controller.SelectedDrawingType);
            return true;
        }
        return false;
    }

    private bool SynchronizeStyleName()
    {
        if (!string.IsNullOrEmpty(_controller.SelectedStyle) &&
            _controller.SelectedStyle != _currentState.StyleName)
        {
            var (clr, br) = _spectrumStyles.GetColorAndBrush(_controller.SelectedStyle);
            UpdateSpectrumStyle(_controller.SelectedStyle, clr, br);
            return true;
        }
        return false;
    }

    private bool SynchronizeRenderQuality()
    {
        if (_currentState.Quality != _controller.RenderQuality)
        {
            UpdateRenderQuality(_controller.RenderQuality);
            return true;
        }
        return false;
    }

    private void UpdateOverlayState()
    {
        _rendererFactory.ConfigureAllRenderers(_controller.IsOverlayActive);
        _frameCache.MarkDirty();
        RequestRender();
    }

    private void UpdateStyleFromController()
    {
        var (clr, br) = _spectrumStyles.GetColorAndBrush(_controller.SelectedStyle);
        UpdateSpectrumStyle(_controller.SelectedStyle, clr, br);
    }

    private void UpdateAnalyzerSettings()
    {
        _analyzer.UpdateSettings(_controller.WindowType, _controller.ScaleType);
        SynchronizeWithController();
        _frameCache.MarkDirty();
        RequestRender();
    }

    private void UpdateRecordingState(bool isRecording)
    {
        if (_isDisposed) return;

        ManageRenderingSubscription(isRecording);
        ShouldShowPlaceholder = !isRecording;
        _frameCache.MarkDirty();
        RequestRender();
    }

    private void ManageRenderingSubscription(bool isRecording)
    {
        if (isRecording)
            CompositionTarget.Rendering += OnRendering;
        else
            CompositionTarget.Rendering -= OnRendering;
    }

    private bool ValidateAndSetDimensions(int width, int height)
    {
        if (!AreDimensionsValid(width, height))
        {
            LogInvalidDimensions(width, height);
            return false;
        }

        _width = width;
        _height = height;
        return true;
    }

    private static bool AreDimensionsValid(int width, int height) =>
        width > 0 && height > 0;

    private static void LogInvalidDimensions(int width, int height)
    {
        Log(LogLevel.Warning,
            LogPrefix,
            $"Invalid dimensions: {width}x{height}");
    }

    private bool IsSkipRendering(object? sender) =>
        _controller.IsOverlayActive && sender == _controller.SpectrumCanvas;

    private void SafeInvalidate()
    {
        if (!_controller.IsOverlayActive || _skElement != _controller.SpectrumCanvas)
            _skElement?.InvalidateVisual();
    }

    private void SubscribeToEvents()
    {
        PerformanceMetricsManager.PerformanceUpdated += OnPerformanceMetricsUpdated;
        _controller.PropertyChanged += OnControllerPropertyChanged;
        if (_analyzer is IComponent comp)
            comp.Disposed += OnAnalyzerDisposed;
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

        _skElement.Dispatcher.Invoke(() =>
        {
            _skElement.PaintSurface -= RenderFrame;
            _skElement.Loaded -= OnElementLoaded;
            _skElement.Unloaded -= OnElementUnloaded;
        });
    }

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
            case nameof(IMainController.LimitFpsTo60):
                HandleFpsLimitChange();
                break;

            case nameof(IMainController.IsRecording):
                UpdateRecordingState(_controller.IsRecording);
                break;

            case nameof(IMainController.IsOverlayActive):
                UpdateOverlayState();
                break;

            case nameof(IMainController.SelectedDrawingType):
                UpdateRenderStyle(_controller.SelectedDrawingType);
                break;

            case nameof(IMainController.SelectedStyle)
            when !string.IsNullOrEmpty(_controller.SelectedStyle):
                UpdateStyleFromController();
                break;

            case nameof(IMainController.RenderQuality):
                UpdateRenderQuality(_controller.RenderQuality);
                break;

            case nameof(IMainController.WindowType):
            case nameof(IMainController.ScaleType):
                UpdateAnalyzerSettings();
                break;

            case nameof(IMainController.BarSpacing):
            case nameof(IMainController.BarCount):
            case nameof(IMainController.ShowPerformanceInfo):
                HandleParameterChange();
                break;
        }
    }

    private void HandleFpsLimitChange()
    {
        FpsLimiter.Instance.IsEnabled = _controller.LimitFpsTo60;
        FpsLimiter.Instance.Reset();
    }

    private void HandleParameterChange()
    {
        _frameCache.MarkDirty();
        RequestRender();
    }

    protected override void DisposeManaged() => CleanUp("Renderer disposed");

    protected override ValueTask DisposeAsyncManagedResources()
    {
        CleanUp("Renderer async disposed");
        return ValueTask.CompletedTask;
    }

    private void CleanUp(string message)
    {
        if (_isDisposed) return;
        _disposalTokenSource.Cancel();
        UnsubscribeFromEvents();
        DisposeResources();
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

        ExecuteSafely(() => _frameCache.Dispose(),
            nameof(DisposeResources), "Error disposing frame cache");

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