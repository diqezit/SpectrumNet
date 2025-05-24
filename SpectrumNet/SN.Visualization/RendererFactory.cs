#nullable enable

namespace SpectrumNet.SN.Visualization;

public sealed class RendererFactory : IRendererFactory
{
    private const string LogPrefix = nameof(RendererFactory);

    private static readonly Lazy<RendererFactory> _instance =
        new(() => new RendererFactory(RenderQuality.Medium),
            LazyThreadSafetyMode.ExecutionAndPublication);

    public static RendererFactory Instance => _instance.Value;

    private readonly object _lock = new();
    private readonly ISmartLogger _logger = SmartLogger.Instance;

    private readonly RendererCache _cache = new();
    private readonly RendererConfigurator _configurator = new();
    private readonly RendererTransparencyManager _transparencyManager;

    private RenderQuality _globalQuality;

    private bool 
        _isApplyingGlobalQuality,
        _suppressConfigEvents;

    private RendererFactory(RenderQuality initialQuality = RenderQuality.Medium)
    {
        _globalQuality = initialQuality;
        _transparencyManager = RendererTransparencyManager.Instance;
        _transparencyManager.SetRendererFactory(this);
    }

    public RenderQuality GlobalQuality
    {
        get => _globalQuality;
        set
        {
            if (_globalQuality == value || _isApplyingGlobalQuality)
                return;

            var oldQuality = _globalQuality;
            _globalQuality = value;

            ApplyGlobalQualityToRenderers();
            LogQualityChange(oldQuality, _globalQuality);
        }
    }

    public ISpectrumRenderer CreateRenderer(
        RenderStyle style,
        bool isOverlayActive,
        RenderQuality? quality = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var actualQuality = quality ?? _globalQuality;
        var renderer = GetCachedOrCreateRenderer(style, cancellationToken);

        if (!_suppressConfigEvents && ShouldConfigureRenderer(renderer, isOverlayActive, actualQuality))
        {
            var oldSuppress = _suppressConfigEvents;
            try
            {
                _suppressConfigEvents = true;
                _configurator.Configure(renderer, isOverlayActive, actualQuality);
            }
            finally
            {
                _suppressConfigEvents = oldSuppress;
            }
        }

        return renderer;
    }

    public IEnumerable<ISpectrumRenderer> GetAllRenderers() => _cache.GetAll();

    public void ConfigureAllRenderers(
        bool? isOverlayActive,
        RenderQuality? quality = null)
    {
        if (_isApplyingGlobalQuality || _suppressConfigEvents)
            return;

        var oldSuppress = _suppressConfigEvents;
        try
        {
            _suppressConfigEvents = true;

            lock (_lock)
            {
                _configurator.ConfigureAll(
                    _cache.GetAll(),
                    isOverlayActive,
                    quality ?? _globalQuality);
            }

            LogRendererConfiguration();
        }
        finally
        {
            _suppressConfigEvents = oldSuppress;
        }
    }

    private ISpectrumRenderer GetCachedOrCreateRenderer(
        RenderStyle style,
        CancellationToken cancellationToken)
    {
        return _cache.GetOrCreate(style, s =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CreateAndInitializeRenderer(s);
        });
    }

    private ISpectrumRenderer CreateAndInitializeRenderer(RenderStyle style)
    {
        try
        {
            var renderer = RendererInstanceFactory.CreateInstance(style);
            LogRendererInstanceCreation(style);

            EnsureInitialized(style, renderer);

            return renderer;
        }
        catch (Exception ex)
        {
            LogRendererCreationError(style, "Error", ex);
            throw;
        }
    }

    private bool ShouldConfigureRenderer(
        ISpectrumRenderer renderer,
        bool isOverlayActive,
        RenderQuality quality)
    {
        return !_isApplyingGlobalQuality &&
               (renderer.IsOverlayActive != isOverlayActive || renderer.Quality != quality);
    }

    private void EnsureInitialized(RenderStyle style, ISpectrumRenderer renderer)
    {
        if (_cache.TryMarkAsInitialized(style))
            _configurator.Initialize(style, renderer);
        else
            LogRendererAlreadyInitialized(style);
    }

    private void ApplyGlobalQualityToRenderers()
    {
        if (_isApplyingGlobalQuality)
            return;

        _logger.Safe(() => HandleApplyGlobalQualityToRenderers(),
            LogPrefix,
            $"Failed to apply global quality {_globalQuality}");
    }

    private void HandleApplyGlobalQualityToRenderers()
    {
        var oldSuppress = _suppressConfigEvents;
        _isApplyingGlobalQuality = true;
        try
        {
            _suppressConfigEvents = true;

            lock (_lock)
            {
                _configurator.ApplyQualityToAll(_cache.GetAll(), _globalQuality);
            }
        }
        finally
        {
            _suppressConfigEvents = oldSuppress;
            _isApplyingGlobalQuality = false;
        }
    }

    private void LogQualityChange(RenderQuality oldQuality, RenderQuality newQuality) =>
        _logger.Log(LogLevel.Information,
            LogPrefix,
            $"Global quality changed from {oldQuality} to {newQuality}",
            forceLog: true);

    private void LogRendererInstanceCreation(RenderStyle style) =>
        _logger.Log(LogLevel.Debug,
            LogPrefix,
            $"Instance created for style {style}",
            forceLog: true);

    private void LogRendererAlreadyInitialized(RenderStyle style) =>
        _logger.Log(LogLevel.Debug,
            LogPrefix,
            $"Renderer for style {style} already initialized",
            forceLog: true);

    private void LogRendererConfiguration() =>
        _logger.Log(LogLevel.Information,
            LogPrefix,
            "Configured all cached renderers",
            forceLog: true);

    private void LogRendererCreationError(RenderStyle style, string errorType, Exception ex) =>
        _logger.Log(LogLevel.Error,
            LogPrefix,
            $"{errorType} during creation/initialization of renderer for style {style}: {ex.Message}",
            forceLog: true);

    public void Dispose()
    {
        _logger.Safe(() => HandleDispose(),
            LogPrefix,
            "Error during RendererFactory disposal");
    }

    private void HandleDispose()
    {
        lock (_lock)
        {
            _cache.DisposeAll();

            if (_transparencyManager is IDisposable disposable)
                disposable.Dispose();
        }
    }
}