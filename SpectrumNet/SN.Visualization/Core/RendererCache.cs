#nullable enable

namespace SpectrumNet.SN.Visualization.Core;

internal sealed class RendererCache
{
    private readonly ConcurrentDictionary<RenderStyle, ISpectrumRenderer> _rendererCache = new();
    private readonly ConcurrentDictionary<RenderStyle, bool> _initializedRenderers = new();
    private readonly object _lock = new();

    public ISpectrumRenderer GetOrCreate(
        RenderStyle style,
        Func<RenderStyle, ISpectrumRenderer> factory)
    {
        if (_rendererCache.TryGetValue(style, out var cachedRenderer))
            return cachedRenderer;

        lock (_lock)
        {
            return _rendererCache.GetOrAdd(style, s => factory(s));
        }
    }

    public IEnumerable<ISpectrumRenderer> GetAll()
    {
        lock (_lock)
            return [.. _rendererCache.Values];
    }

    public bool TryMarkAsInitialized(RenderStyle style) =>
        _initializedRenderers.TryAdd(style, true);

    public bool IsInitialized(RenderStyle style) =>
        _initializedRenderers.ContainsKey(style);

    public void Clear()
    {
        lock (_lock)
        {
            _rendererCache.Clear();
            _initializedRenderers.Clear();
        }
    }

    public void DisposeAll()
    {
        lock (_lock)
        {
            foreach (var renderer in _rendererCache.Values)
            {
                if (renderer is IDisposable disposable)
                    disposable.Dispose();
            }
            Clear();
        }
    }
}