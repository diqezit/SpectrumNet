#nullable enable

using SpectrumNet.SN.Spectrum.Core;

namespace SpectrumNet.SN.Visualization.Core;

public sealed class Renderer : AsyncDisposableBase
{
    private const string LogPrefix = nameof(Renderer);
    private readonly ISmartLogger _logger = Instance;

    private readonly IMainController _controller;
    private readonly IBrushProvider _brushProvider;
    private readonly ISpectrumDataProcessor _dataProcessor;
    private readonly IPerformanceRenderer _performanceRenderer;
    private readonly IPlaceholderRenderer _placeholderRenderer;
    private readonly IRendererFactory _rendererFactory;
    private readonly IPerformanceMetricsManager _performanceMetricsManager;
    private readonly IFrameCache _frameCache;

    private readonly SKElement? _skElement;
    private RenderStyle _currentStyle;
    private RenderQuality _quality;
    private string _styleName = "Solid";
    private SKPaint _currentPaint;

    private bool
        _isStyleUpdateInProgress,
        _isApplyingQuality;

    public Renderer(
        IBrushProvider brushProvider,
        IMainController controller,
        SpectrumAnalyzer analyzer,
        SKElement element,
        IRendererFactory rendererFactory,
        IPerformanceMetricsManager? performanceMetricsManager = null)
    {
        _brushProvider = brushProvider ?? throw new ArgumentNullException(nameof(brushProvider));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _skElement = element ?? throw new ArgumentNullException(nameof(element));
        _rendererFactory = rendererFactory ?? throw new ArgumentNullException(nameof(rendererFactory));
        _performanceMetricsManager = performanceMetricsManager ?? PerformanceMetricsManager.Instance;

        _dataProcessor = new SpectrumDataProcessor(controller);
        _performanceRenderer = new PerformanceRenderer(controller, _performanceMetricsManager);
        _placeholderRenderer = new PlaceholderRenderer(controller);
        _frameCache = new FrameCache(_logger);

        var (_, brush) = _brushProvider.GetColorAndBrush(_styleName);
        _currentPaint = brush.Clone() ?? throw new InvalidOperationException($"Failed to initialize {_styleName} style");
        _currentStyle = controller.SelectedDrawingType;
        _quality = controller.RenderQuality;

        InitializeEvents();
        ((SpectrumDataProcessor)_dataProcessor).Configure(controller.IsOverlayActive);
    }

    private void InitializeEvents()
    {
        _performanceMetricsManager.PerformanceMetricsUpdated += OnPerformanceMetricsUpdated;
        _controller.PropertyChanged += OnControllerPropertyChanged;
    }

    private void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _logger.Safe(() =>
        {
            if (e?.PropertyName == nameof(IMainController.IsRecording) ||
                e?.PropertyName == nameof(IMainController.IsOverlayActive))
            {
                ((SpectrumDataProcessor)_dataProcessor).Configure(_controller.IsOverlayActive);
                _frameCache.MarkDirty();
                RequestRender();
            }
        }, LogPrefix, "Error handling controller property changed");
    }

    private void OnPerformanceMetricsUpdated(object? sender, PerformanceMetrics metrics) =>
        _performanceRenderer.UpdateMetrics(metrics);

    public bool ShouldShowPlaceholder => _placeholderRenderer.ShouldShowPlaceholder;

    public void RequestRender()
    {
        if (_isDisposed) return;
        SafeInvalidate();
    }

    public void UpdateRenderDimensions(int width, int height)
    {
        _logger.Safe(() =>
        {
            if (_isDisposed) return;
            if (ValidateAndSetDimensions(width, height))
            {
                _frameCache.MarkDirty();
                RequestRender();
            }
        }, LogPrefix, "Error updating render dimensions");
    }

    private static bool ValidateAndSetDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            Instance.Log(LogLevel.Warning,
                LogPrefix,
                $"Invalid dimensions: {width}x{height}");
            return false;
        }
        return true;
    }

    public void SynchronizeWithController()
    {
        _logger.Safe(() =>
        {
            if (_isDisposed) return;
            if (SynchronizeRenderSettings())
            {
                _frameCache.MarkDirty();
                RequestRender();
            }
        }, LogPrefix, "Error synchronizing with controller");
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
        if (_currentStyle != _controller.SelectedDrawingType && !_isStyleUpdateInProgress)
        {
            UpdateRenderStyle(_controller.SelectedDrawingType);
            return true;
        }
        return false;
    }

    private bool SynchronizeStyleName()
    {
        if (!string.IsNullOrEmpty(_controller.SelectedStyle) &&
            _controller.SelectedStyle != _styleName &&
            !_isStyleUpdateInProgress)
        {
            var (clr, br) = _brushProvider.GetColorAndBrush(_controller.SelectedStyle);
            UpdateSpectrumStyle(_controller.SelectedStyle, clr, br);
            return true;
        }
        return false;
    }

    private bool SynchronizeRenderQuality()
    {
        if (_quality != _controller.RenderQuality && !_isApplyingQuality)
        {
            UpdateRenderQuality(_controller.RenderQuality);
            return true;
        }
        return false;
    }

    public void UpdateRenderStyle(RenderStyle style)
    {
        _logger.Safe(() =>
        {
            if (_isDisposed || _currentStyle == style || _isStyleUpdateInProgress) return;

            try
            {
                _isStyleUpdateInProgress = true;
                _currentStyle = style;
                _frameCache.MarkDirty();
                RequestRender();
            }
            finally
            {
                _isStyleUpdateInProgress = false;
            }
        }, LogPrefix, "Error updating render style");
    }

    public void UpdateSpectrumStyle(string styleName, SKColor color, SKPaint brush)
    {
        _logger.Safe(() =>
        {
            if (_isDisposed || string.IsNullOrEmpty(styleName) ||
                styleName == _styleName || _isStyleUpdateInProgress) return;

            try
            {
                _isStyleUpdateInProgress = true;
                UpdatePaintAndStyleName(styleName, brush);
                _frameCache.MarkDirty();
                RequestRender();
            }
            finally
            {
                _isStyleUpdateInProgress = false;
            }
        }, LogPrefix, "Error updating spectrum style");
    }

    private void UpdatePaintAndStyleName(string styleName, SKPaint brush)
    {
        var oldPaint = _currentPaint;
        _currentPaint = brush.Clone() ?? throw new InvalidOperationException("Brush clone failed");
        _styleName = styleName;
        oldPaint.Dispose();
    }

    public void UpdateRenderQuality(RenderQuality quality)
    {
        _logger.Safe(() =>
        {
            if (_isDisposed || _quality == quality || _isApplyingQuality) return;

            try
            {
                _isApplyingQuality = true;
                _quality = quality;
                _frameCache.MarkDirty();
                RequestRender();
            }
            finally
            {
                _isApplyingQuality = false;
            }
        }, LogPrefix, "Error updating render quality");
    }

    public void RenderFrame(object? sender, SKPaintSurfaceEventArgs e)
    {
        _logger.Safe(() =>
        {
            if (ShouldSkipRenderingFrame(sender))
            {
                ClearCanvas(e.Surface.Canvas);
                return;
            }

            bool forceNewFrame = sender is SKElement sendingElement &&
                                 _skElement != null &&
                                 sendingElement != _skElement;

            if (forceNewFrame || ShouldRenderNewFrame())
            {
                if (forceNewFrame)
                    _frameCache.MarkDirty();

                RenderNewFrame(e);
            }
            else
            {
                RenderCachedFrame(e);
            }
        }, LogPrefix, "Error rendering frame");
    }

    private bool ShouldSkipRenderingFrame(object? sender) =>
        _isDisposed || IsSkipRendering(sender);

    private bool IsSkipRendering(object? sender) =>
        _controller.IsOverlayActive && sender == _controller.SpectrumCanvas;

    private bool ShouldRenderNewFrame() =>
        _frameCache.IsDirty ||
        _placeholderRenderer.ShouldShowPlaceholder ||
        _frameCache.ShouldForceRefresh() ||
        _dataProcessor.RequiresRedraw();

    private void RenderNewFrame(SKPaintSurfaceEventArgs e)
    {
        ClearCanvas(e.Surface.Canvas);
        bool renderSuccessful = false;

        try
        {
            _logger.Safe(() =>
            {
                RenderFrameInternal(e.Surface.Canvas, e.Info);
                renderSuccessful = true;
            }, LogPrefix, "Error rendering new frame");
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Error, LogPrefix, $"Error in RenderNewFrame: {ex.Message}");
            RenderFallback(e.Surface.Canvas, e.Info);
        }

        if (renderSuccessful)
            _frameCache.Update(e.Surface, e.Info);

        RecordFrameMetrics();
    }

    private void RenderCachedFrame(SKPaintSurfaceEventArgs e) =>
        _frameCache.Draw(e.Surface.Canvas);

    private static void ClearCanvas(SKCanvas canvas) =>
        canvas.Clear(SKColors.Transparent);

    private void RecordFrameMetrics() =>
        _performanceMetricsManager.RecordFrameTime();

    private void RenderFrameInternal(SKCanvas canvas, SKImageInfo info)
    {
        if (_isDisposed) return;

        if (_placeholderRenderer.ShouldShowPlaceholder)
        {
            _placeholderRenderer.RenderPlaceholder(canvas, info);
            return;
        }

        RenderSpectrumData(canvas, info);
    }

    private void RenderSpectrumData(SKCanvas canvas, SKImageInfo info)
    {
        _logger.Safe(() =>
        {
            var spectrum = _dataProcessor.GetCurrentSpectrum();
            if (spectrum is null)
            {
                if (!_controller.IsRecording)
                    _placeholderRenderer.RenderPlaceholder(canvas, info);
                return;
            }

            if (!TryCalculateRenderParameters(info, out float barWidth, out float barSpacing, out int barCount))
            {
                _placeholderRenderer.RenderPlaceholder(canvas, info);
                return;
            }

            RenderSpectrum(canvas, info, spectrum, barWidth, barSpacing, barCount);
        }, LogPrefix, "Error rendering spectrum data");
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

        barSpacing = MathF.Min((float)_controller.BarSpacing, totalWidth / (barCount + 1f));
        barWidth = MathF.Max((totalWidth - (barCount - 1) * barSpacing) / barCount, 1.0f);

        if (barCount > 1)
            barSpacing = (totalWidth - barCount * barWidth) / (barCount - 1);
        else
            barSpacing = 0;

        return barWidth >= 1.0f;
    }

    private void RenderSpectrum(
        SKCanvas canvas,
        SKImageInfo info,
        SpectralData spectrum, // Changed type from FftEventArgs to SpectralData
        float barWidth,
        float barSpacing,
        int barCount)
    {
        _logger.Safe(() =>
        {
            var renderer = _rendererFactory.CreateRenderer(_currentStyle, _controller.IsOverlayActive, _quality);

            var processedSpectrum = _dataProcessor.ProcessSpectrum(spectrum.Spectrum, barCount); // Access spectrum.Spectrum

            renderer.Render(
                canvas,
                processedSpectrum,
                info,
                barWidth,
                barSpacing,
                barCount,
                _currentPaint,
                _performanceRenderer.RenderPerformanceInfo);

        }, LogPrefix, "Error rendering spectrum");
    }

    private void RenderFallback(SKCanvas canvas, SKImageInfo info)
    {
        try
        {
            _placeholderRenderer.RenderPlaceholder(canvas, info);
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Error, LogPrefix, $"Error in fallback rendering: {ex.Message}");
        }
    }

    private void SafeInvalidate() =>
        _skElement?.InvalidateVisual();

    public IPlaceholder? GetPlaceholder() =>
        _placeholderRenderer.GetPlaceholder();

    public void HandlePlaceholderMouseDown(SKPoint point) =>
        _placeholderRenderer.HandleMouseDown(point);

    public void HandlePlaceholderMouseMove(SKPoint point) =>
        _placeholderRenderer.HandleMouseMove(point);

    public void HandlePlaceholderMouseUp(SKPoint point) =>
        _placeholderRenderer.HandleMouseUp(point);

    public void HandlePlaceholderMouseEnter() =>
        _placeholderRenderer.HandleMouseEnter();

    public void HandlePlaceholderMouseLeave() =>
        _placeholderRenderer.HandleMouseLeave();

    protected override void DisposeManaged() =>
        _logger.Safe(() =>
        {
            _performanceMetricsManager.PerformanceMetricsUpdated -= OnPerformanceMetricsUpdated;
            _controller.PropertyChanged -= OnControllerPropertyChanged;

            _currentPaint?.Dispose();
            _frameCache?.Dispose();

            _logger.Log(LogLevel.Debug, LogPrefix, "Disposed");
        }, LogPrefix, "Error during managed disposal");
}