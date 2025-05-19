#nullable enable

namespace SpectrumNet.Controllers.RenderCore;

public sealed class Renderer : AsyncDisposableBase
{
    private const string DEFAULT_STYLE = "Solid";
    private const string LogPrefix = nameof(Renderer);

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
    private readonly ISmartLogger _logger = Instance;

    private readonly SKFont _performanceFont;
    private readonly SKPaint _performancePaint;

    private readonly SKElement? _skElement;
    private RenderState _currentState = default!;

    private volatile bool
        _isAnalyzerDisposed,
        _shouldShowPlaceholder = true;

    private bool
        _updatingQuality,
        _isStyleUpdateInProgress;

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

        _performanceFont = new SKFont { Size = 12, Edging = SKFontEdging.SubpixelAntialias };
        _performancePaint = new SKPaint { IsAntialias = true };

        ShouldShowPlaceholder = !_controller.IsRecording;
        InitializeRenderer();
    }

    private void InitializeRenderer() =>
        _logger.Safe(() => {
            InitializeRenderState();
            SubscribeToEvents();
            AttachUIElementEvents();

            FpsLimiter.Instance.IsEnabled = _controller.LimitFpsTo60;
        }, LogPrefix, "Error initializing renderer");

    private void InitializeRenderState()
    {
        var (_, brush) = _spectrumStyles.GetColorAndBrush(DEFAULT_STYLE);
        _currentState = new RenderState(
            brush.Clone()
            ?? throw new InvalidOperationException($"{LogPrefix} Failed to initialize {DEFAULT_STYLE} style"),
            _controller.SelectedDrawingType,
            DEFAULT_STYLE,
            _controller.RenderQuality);
    }

    public void RequestRender() =>
        _logger.Safe(() => {
            if (_isDisposed) return;
            SafeInvalidate();
        }, LogPrefix, "Error requesting render");

    public void UpdateRenderDimensions(int width, int height) =>
        _logger.Safe(() => HandleUpdateRenderDimensions(width, height),
            LogPrefix, "Error updating render dimensions");

    private void HandleUpdateRenderDimensions(int width, int height)
    {
        if (_isDisposed) return;
        if (!ValidateAndSetDimensions(width, height)) return;
        _frameCache.MarkDirty();
        RequestRender();
    }

    public void SynchronizeWithController() =>
        _logger.Safe(() => HandleSynchronizeWithController(),
            LogPrefix, "Error synchronizing with controller");

    private void HandleSynchronizeWithController()
    {
        if (_isDisposed) return;
        if (SynchronizeRenderSettings())
        {
            _frameCache.MarkDirty();
            RequestRender();
        }
    }

    public void UpdateRenderStyle(RenderStyle style) =>
        _logger.Safe(() => HandleUpdateRenderStyle(style),
            LogPrefix, "Error updating render style");

    private void HandleUpdateRenderStyle(RenderStyle style)
    {
        if (_isDisposed
            || _currentState.Style == style
            || _isStyleUpdateInProgress) return;

        try
        {
            _isStyleUpdateInProgress = true;
            _currentState = _currentState with { Style = style };
            _frameCache.MarkDirty();
            RequestRender();
        }
        finally
        {
            _isStyleUpdateInProgress = false;
        }
    }

    public void UpdateSpectrumStyle(
        string styleName,
        SKColor color,
        SKPaint brush) =>
        _logger.Safe(() => HandleUpdateSpectrumStyle(styleName, color, brush),
            LogPrefix, "Error updating spectrum style");

    private void HandleUpdateSpectrumStyle(
        string styleName,
        SKColor color,
        SKPaint brush)
    {
        if (_isDisposed
            || string.IsNullOrEmpty(styleName)
            || styleName == _currentState.StyleName
            || _isStyleUpdateInProgress) return;

        try
        {
            _isStyleUpdateInProgress = true;
            _logger.Safe(() => UpdatePaintAndStyleName(styleName, brush),
                LogPrefix, "Error updating paint and style name");

            _frameCache.MarkDirty();
            RequestRender();
        }
        finally
        {
            _isStyleUpdateInProgress = false;
        }
    }

    public void UpdateRenderQuality(RenderQuality quality) =>
        _logger.Safe(() => HandleUpdateRenderQuality(quality),
            LogPrefix, "Error updating render quality");

    private void HandleUpdateRenderQuality(RenderQuality quality)
    {
        if (_isDisposed
            || _currentState.Quality == quality
            || _updatingQuality)
            return;

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

    public void RenderFrame(object? sender, SKPaintSurfaceEventArgs e) =>
        _logger.Safe(() => HandleRenderFrame(sender, e),
            LogPrefix, "Error rendering frame");

    private void HandleRenderFrame(object? sender, SKPaintSurfaceEventArgs e)
    {
        if (ShouldSkipRenderingFrame(sender))
        {
            ClearCanvas(e.Surface.Canvas);
            return;
        }

        bool forceNewFrameForOverlay = false;

        if (sender is SKElement sendingElement
            && _skElement != null
            && sendingElement != _skElement)
        {
            forceNewFrameForOverlay = true;
        }

        if (forceNewFrameForOverlay || ShouldRenderNewFrame())
        {
            if (forceNewFrameForOverlay)
            {
                _frameCache.MarkDirty();
            }
            RenderNewFrame(sender, e);
        }
        else
        {
            RenderCachedFrame(e);
        }
    }

    public void HandlePlaceholderMouseDown(SKPoint point) =>
        _logger.Safe(() => {
            if (ShouldShowPlaceholder && _placeholder != null)
            {
                _placeholder.IsInteractive = true;
                _placeholder.OnMouseDown(point);
                _frameCache.MarkDirty();
                RequestRender();
            }
        }, LogPrefix, "Error handling placeholder mouse down");

    public void HandlePlaceholderMouseMove(SKPoint point) =>
        _logger.Safe(() => {
            if (ShouldShowPlaceholder && _placeholder != null)
            {
                _placeholder.OnMouseMove(point);
                _frameCache.MarkDirty();
                RequestRender();
            }
        }, LogPrefix, "Error handling placeholder mouse move");

    public void HandlePlaceholderMouseUp(SKPoint point) =>
        _logger.Safe(() => {
            if (ShouldShowPlaceholder && _placeholder != null)
            {
                _placeholder.OnMouseUp(point);
                _frameCache.MarkDirty();
                RequestRender();
            }
        }, LogPrefix, "Error handling placeholder mouse up");

    public void HandlePlaceholderMouseEnter() =>
        _logger.Safe(() => {
            if (ShouldShowPlaceholder && _placeholder != null)
            {
                _placeholder.OnMouseEnter();
                _frameCache.MarkDirty();
                RequestRender();
            }
        }, LogPrefix, "Error handling placeholder mouse enter");

    public void HandlePlaceholderMouseLeave() =>
        _logger.Safe(() => {
            if (ShouldShowPlaceholder && _placeholder != null)
            {
                _placeholder.OnMouseLeave();
                _frameCache.MarkDirty();
                RequestRender();
            }
        }, LogPrefix, "Error handling placeholder mouse leave");

    public IPlaceholder? GetPlaceholder() =>
        ShouldShowPlaceholder ? _placeholder : null;

    private bool ShouldSkipRenderingFrame(object? sender) =>
        _isDisposed || IsSkipRendering(sender);

    private bool ShouldRenderNewFrame() =>
        _frameCache.IsDirty ||
        ShouldShowPlaceholder ||
        _frameCache.ShouldForceRefresh();

    private void RenderNewFrame(object? sender, SKPaintSurfaceEventArgs e)
    {
        ClearCanvas(e.Surface.Canvas);
        bool renderSuccessful = false;

        try
        {
            _logger.Safe(() =>
            {
                RenderFrameInternal(e.Surface.Canvas, e.Info);
                renderSuccessful = true;
            },
            LogPrefix,
            "Error rendering new frame");
        }
        catch (Exception ex)
        {
            HandleRenderFrameException(ex, e.Surface.Canvas, e.Info);
        }

        if (renderSuccessful)
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

        if (ShouldRenderPlaceholder())
        {
            RenderPlaceholder(canvas, info);
            return;
        }

        RenderSpectrumData(canvas, info);
    }

    private void RenderSpectrumData(SKCanvas canvas, SKImageInfo info) =>
        _logger.Safe(() => HandleRenderSpectrumData(canvas, info),
            LogPrefix, "Error rendering spectrum data");

    private void HandleRenderSpectrumData(SKCanvas canvas, SKImageInfo info)
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
       int barCount) =>
        _logger.Safe(() => HandleRenderSpectrum(canvas, info, spectrum, barWidth, barSpacing, barCount),
            LogPrefix, "Error rendering spectrum");

    private void HandleRenderSpectrum(
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
            DrawPerformanceInfoOnCanvas);
    }

    private ISpectrumRenderer GetConfiguredRenderer(RenderStyle style) =>
        _rendererFactory.CreateRenderer(
            style,
            _controller.IsOverlayActive,
            _currentState.Quality);

    private static void RenderSpectrumWithRenderer(
        ISpectrumRenderer renderer,
        SKCanvas canvas,
        SpectralData spectrum,
        SKImageInfo info,
        float barWidth,
        float barSpacing,
        int barCount,
        SKPaint paint,
        Action<SKCanvas, SKImageInfo> drawPerformanceInfo) =>
        renderer.Render(
            canvas,
            spectrum.Spectrum,
            info,
            barWidth,
            barSpacing,
            barCount,
            paint,
            drawPerformanceInfo);

    private void ValidateStyleConsistency(RenderStyle expectedStyle)
    {
        if (_currentState.Style != expectedStyle)
        {
            _logger.Log(LogLevel.Warning,
                LogPrefix,
                $"RenderFrameInternal: " +
                $"Style changed from {expectedStyle} " +
                $"to {_currentState.Style} after getting renderer, resetting",
                forceLog: true);

            _currentState = _currentState with { Style = expectedStyle };
        }
    }

    private SpectralData? GetSpectrumData() =>
        _logger.SafeResult<SpectralData?>(() => HandleGetSpectrumData(), null,
            LogPrefix, "Error getting spectrum data");

    private SpectralData? HandleGetSpectrumData()
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
            barSpacing = (totalWidth - barCount * barWidth) / (barCount - 1);
        else
            barSpacing = 0;

        return barWidth >= 1.0f;
    }

    private void RenderPlaceholder(SKCanvas canvas, SKImageInfo info) =>
        _logger.Safe(() => {
            if (_isDisposed) return;
            UpdatePlaceholderSize(info);
            _placeholder.Render(canvas, info);
        }, LogPrefix, "Error rendering placeholder");

    private void UpdatePlaceholderSize(SKImageInfo info) =>
        _placeholder.CanvasSize = new SKSize(info.Width, info.Height);

    private void HandleRenderFrameException(Exception ex, SKCanvas canvas, SKImageInfo info) =>
        _logger.Safe(() => {
            if (ex is ObjectDisposedException)
                HandleObjectDisposedException();
            else if (!_isDisposed)
                RenderPlaceholder(canvas, info);
        }, LogPrefix, "Error handling render frame exception");

    private void HandleObjectDisposedException() =>
        _isAnalyzerDisposed = true;

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
        if (_currentState.Style != _controller.SelectedDrawingType
            && !_isStyleUpdateInProgress)
        {
            UpdateRenderStyle(_controller.SelectedDrawingType);
            return true;
        }
        return false;
    }

    private bool SynchronizeStyleName()
    {
        if (!string.IsNullOrEmpty(_controller.SelectedStyle) &&
            _controller.SelectedStyle != _currentState.StyleName &&
            !_isStyleUpdateInProgress)
        {
            var (clr, br) = _spectrumStyles.GetColorAndBrush(_controller.SelectedStyle);
            UpdateSpectrumStyle(_controller.SelectedStyle, clr, br);
            return true;
        }
        return false;
    }

    private bool SynchronizeRenderQuality()
    {
        if (_currentState.Quality != _controller.RenderQuality
            && !_updatingQuality)
        {
            UpdateRenderQuality(_controller.RenderQuality);
            return true;
        }
        return false;
    }

    private void UpdateOverlayState() =>
        _logger.Safe(() => {
            _rendererFactory.ConfigureAllRenderers(_controller.IsOverlayActive);
            _frameCache.MarkDirty();
            RequestRender();
        }, LogPrefix, "Error updating overlay state");

    private void UpdateStyleFromController() =>
        _logger.Safe(() => {
            if (_isStyleUpdateInProgress) return;
            var (clr, br) = _spectrumStyles.GetColorAndBrush(_controller.SelectedStyle);
            UpdateSpectrumStyle(_controller.SelectedStyle, clr, br);
        }, LogPrefix, "Error updating style from controller");

    private static bool ValidateAndSetDimensions(int width, int height)
    {
        if (!AreDimensionsValid(width, height))
        {
            LogInvalidDimensions(width, height);
            return false;
        }

        return true;
    }

    private static bool AreDimensionsValid(int width, int height) =>
        width > 0 && height > 0;

    private static void LogInvalidDimensions(int width, int height) =>
        SmartLogger.Instance.Log(LogLevel.Warning,
            LogPrefix,
            $"Invalid dimensions: {width}x{height}");

    private bool IsSkipRendering(object? sender) =>
        _controller.IsOverlayActive && sender == _controller.SpectrumCanvas;

    private void SafeInvalidate() =>
        _skElement?.InvalidateVisual();

    private void SubscribeToEvents() =>
        _logger.Safe(() => {
            PerformanceMetricsManager.PerformanceMetricsUpdated += OnPerformanceMetricsUpdated;
            _controller.PropertyChanged += OnControllerPropertyChanged;
            if (_analyzer is IComponent comp)
                comp.Disposed += OnAnalyzerDisposed;
        }, LogPrefix, "Error subscribing to events");

    private void AttachUIElementEvents() =>
        _logger.Safe(() => {
            if (_skElement is null) return;
            _skElement.PaintSurface += RenderFrame;
            _skElement.Loaded += OnElementLoaded;
            _skElement.Unloaded += OnElementUnloaded;
        }, LogPrefix, "Error attaching UI element events");

    private void DetachUIElementEvents() =>
        _logger.Safe(() => {
            if (_skElement is null) return;

            _skElement.Dispatcher.Invoke(() =>
            {
                _skElement.PaintSurface -= RenderFrame;
                _skElement.Loaded -= OnElementLoaded;
                _skElement.Unloaded -= OnElementUnloaded;
            });
        }, LogPrefix, "Error detaching UI element events");

    private void OnAnalyzerDisposed(object? sender, EventArgs e) =>
        _isAnalyzerDisposed = true;

    private void OnPerformanceMetricsUpdated(object? sender, PerformanceMetrics metrics) =>
        PerformanceUpdate?.Invoke(this, metrics);

    private void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        _logger.Safe(() => {
            if (e?.PropertyName is null || _isDisposed) return;
            HandlePropertyChange(e.PropertyName);
        }, LogPrefix, "Error handling property change");

    private void OnElementLoaded(object? sender, RoutedEventArgs e) =>
        _logger.Safe(() => {
            if (_isDisposed) return;
            UpdateRenderDimensions((int)(_skElement?.ActualWidth ?? 0),
                                  (int)(_skElement?.ActualHeight ?? 0));
        }, LogPrefix, "Error handling element loaded");

    private void OnElementUnloaded(object? sender, RoutedEventArgs e)
    {
        if (_isDisposed) return;
    }

    private void HandlePropertyChange(string propertyName) =>
        _logger.Safe(() => {
            switch (propertyName)
            {
                case nameof(IMainController.LimitFpsTo60):
                    HandleFpsLimitChange();
                    break;

                case nameof(IMainController.IsRecording):
                    ShouldShowPlaceholder = !_controller.IsRecording;
                    _frameCache.MarkDirty();
                    RequestRender();
                    break;

                case nameof(IMainController.IsOverlayActive):
                    UpdateOverlayState();
                    break;

                case nameof(IMainController.SelectedDrawingType):
                    if (!_isStyleUpdateInProgress)
                        UpdateRenderStyle(_controller.SelectedDrawingType);
                    break;

                case nameof(IMainController.SelectedStyle)
                when !string.IsNullOrEmpty(_controller.SelectedStyle) && !_isStyleUpdateInProgress:
                    UpdateStyleFromController();
                    break;

                case nameof(IMainController.RenderQuality):
                    if (!_updatingQuality)
                        UpdateRenderQuality(_controller.RenderQuality);
                    break;

                case nameof(IMainController.WindowType):
                case nameof(IMainController.ScaleType):
                    _frameCache.MarkDirty();
                    RequestRender();
                    break;

                case nameof(IMainController.BarSpacing):
                case nameof(IMainController.BarCount):
                case nameof(IMainController.ShowPerformanceInfo):
                    HandleParameterChange();
                    break;
            }
        }, LogPrefix, $"Error handling property change: {propertyName}");

    private void HandleFpsLimitChange() =>
        _logger.Safe(() => {
            if (_controller is IMainController controller)
            {
                FpsLimiter.Instance.IsEnabled = controller.LimitFpsTo60;
                FpsLimiter.Instance.Reset();
            }
            else
            {
                FpsLimiter.Instance.IsEnabled = Settings.Instance.LimitFpsTo60;
                FpsLimiter.Instance.Reset();
            }
        }, LogPrefix, "Error handling FPS limit change");

    private void HandleParameterChange() =>
        _logger.Safe(() => {
            _frameCache.MarkDirty();
            RequestRender();
        }, LogPrefix, "Error handling parameter change");

    protected override void DisposeManaged() =>
        _logger.Safe(() => HandleDisposeManaged(), LogPrefix, "Error during managed disposal");

    private void HandleDisposeManaged()
    {
        if (_isDisposed) return;
        _disposalTokenSource.Cancel();
        UnsubscribeFromEvents();
        DisposeResources();
    }

    protected override ValueTask DisposeAsyncManagedResources() =>
        _logger.SafeResult(() => {
            CleanUp("Renderer async disposed");
            return ValueTask.CompletedTask;
        }, ValueTask.CompletedTask, LogPrefix, "Error during async managed disposal");

    private void CleanUp(string message) =>
        _logger.Safe(() => {
            if (_isDisposed) return;
            _disposalTokenSource.Cancel();
            UnsubscribeFromEvents();
            DisposeResources();
        }, LogPrefix, $"Error during cleanup: {message}");

    private void UnsubscribeFromEvents() =>
        _logger.Safe(() => {
            PerformanceMetricsManager.PerformanceMetricsUpdated -= OnPerformanceMetricsUpdated;

            if (_controller is INotifyPropertyChanged notifier)
                notifier.PropertyChanged -= OnControllerPropertyChanged;

            if (_analyzer is IComponent comp)
                comp.Disposed -= OnAnalyzerDisposed;

            DetachUIElementEvents();
        }, LogPrefix, "Error unsubscribing from events");

    private void DisposeResources() =>
        _logger.Safe(() => {
            _logger.Safe(() => _currentState?.Paint?.Dispose(), LogPrefix, "Error disposing paint");
            _logger.Safe(() => _placeholder?.Dispose(), LogPrefix, "Error disposing placeholder");
            _logger.Safe(() => _frameCache?.Dispose(), LogPrefix, "Error disposing frame cache");
            _logger.Safe(() => _renderLock?.Dispose(), LogPrefix, "Error disposing lock");
            _logger.Safe(() => _disposalTokenSource?.Dispose(), LogPrefix, "Error disposing token source");
            _logger.Safe(() => _performanceFont?.Dispose(), LogPrefix, "Error disposing performance font");
            _logger.Safe(() => _performancePaint?.Dispose(), LogPrefix, "Error disposing performance paint");
        }, LogPrefix, "Error disposing resources");

    private void DrawPerformanceInfoOnCanvas(SKCanvas canvas, SKImageInfo info) =>
        _logger.Safe(() => HandleDrawPerformanceInfoOnCanvas(canvas, info),
            LogPrefix, "Error drawing performance info");

    private void HandleDrawPerformanceInfoOnCanvas(SKCanvas canvas, SKImageInfo info)
    {
        if (!_controller.ShowPerformanceInfo || _isDisposed) return;

        float fps = PerformanceMetricsManager.GetCurrentFps();
        double cpu = PerformanceMetricsManager.GetCurrentCpuUsagePercent();
        double ram = PerformanceMetricsManager.GetCurrentRamUsageMb();
        PerformanceLevel level = PerformanceMetricsManager.GetCurrentPerformanceLevel();

        _performancePaint.Color = GetPerformanceTextColor(level);

        string fpsLimiterInfo = FpsLimiter.Instance.IsEnabled ? " [60 FPS Lock]" : "";
        string infoText = string.Create(CultureInfo.InvariantCulture,
            $"RAM: {ram:F1} MB | CPU: {cpu:F1}% | FPS: {fps:F0}{fpsLimiterInfo} | {level}");

        canvas.DrawText(infoText, 10, info.Height - 10, _performanceFont, _performancePaint);
    }

    private static SKColor GetPerformanceTextColor(PerformanceLevel level) =>
        level switch
        {
            PerformanceLevel.Excellent => SKColors.LimeGreen,
            PerformanceLevel.Good => SKColors.DodgerBlue,
            PerformanceLevel.Fair => SKColors.Orange,
            PerformanceLevel.Poor => SKColors.Red,
            _ => SKColors.Gray
        };
}