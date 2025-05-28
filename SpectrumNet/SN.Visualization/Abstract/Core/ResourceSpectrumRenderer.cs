#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Core;

public abstract class ResourceSpectrumRenderer : AnimationSpectrumRenderer
{
    private readonly IResourceManager _resourceManager;
    private readonly object _resourceLock = new();

    protected ResourceSpectrumRenderer(
        ISpectrumProcessingCoordinator? processingCoordinator = null,
        IQualityManager? qualityManager = null,
        IOverlayStateManager? overlayStateManager = null,
        IRenderingHelpers? renderingHelpers = null,
        IBufferManager? bufferManager = null,
        ISpectrumBandProcessor? bandProcessor = null,
        IAnimationTimer? animationTimer = null,
        IResourceManager? resourceManager = null) : base(
            processingCoordinator,
            qualityManager,
            overlayStateManager,
            renderingHelpers,
            bufferManager,
            bandProcessor,
            animationTimer)
    {
        _resourceManager = resourceManager ?? new ResourceManager();
    }

    protected SKPath GetPath()
    {
        lock (_resourceLock)
        {
            return _resourceManager.GetPath();
        }
    }

    protected void ReturnPath(SKPath path)
    {
        lock (_resourceLock)
        {
            _resourceManager.ReturnPath(path);
        }
    }

    protected SKPaint GetPaint()
    {
        lock (_resourceLock)
        {
            return _resourceManager.GetPaint();
        }
    }

    protected void ReturnPaint(SKPaint paint)
    {
        lock (_resourceLock)
        {
            _resourceManager.ReturnPaint(paint);
        }
    }

    protected void ReleasePaints(params SKPaint[] paints)
    {
        lock (_resourceLock)
        {
            foreach (var paint in paints)
            {
                if (paint != null)
                    _resourceManager.ReturnPaint(paint);
            }
        }
    }

    protected void ReleasePaths(params SKPath[] paths)
    {
        lock (_resourceLock)
        {
            foreach (var path in paths)
            {
                if (path != null)
                    _resourceManager.ReturnPath(path);
            }
        }
    }

    protected T ExecuteWithResource<T>(
        Func<T> resourceGetter,
        Action<T> resourceReturner,
        Func<T, T> operation) where T : class
    {
        T resource = resourceGetter();
        try
        {
            return operation(resource);
        }
        finally
        {
            resourceReturner(resource);
        }
    }

    protected void ExecuteWithPaint(
        Action<SKPaint> operation)
    {
        var paint = GetPaint();
        try
        {
            operation(paint);
        }
        finally
        {
            ReturnPaint(paint);
        }
    }

    protected void ExecuteWithPath(
        Action<SKPath> operation)
    {
        var path = GetPath();
        try
        {
            operation(path);
        }
        finally
        {
            ReturnPath(path);
        }
    }

    protected override void CleanupUnusedResources()
    {
        lock (_resourceLock)
        {
            _resourceManager.CleanupUnused();
        }
        base.CleanupUnusedResources();
    }

    protected override void OnDispose()
    {
        _resourceManager?.Dispose();
        base.OnDispose();
    }
}