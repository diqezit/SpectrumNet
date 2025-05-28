#nullable enable

namespace SpectrumNet.SN.Shared.Utils;

public class ObjectPool<T> : IDisposable where T : class
{
    private static readonly bool _isDisposable = typeof(IDisposable).IsAssignableFrom(typeof(T));
    private readonly ConcurrentBag<T> _objects = [];
    private readonly Func<T> _objectGenerator;
    private readonly Action<T>? _objectReset;
    private readonly int _maxSize;
    private int _currentCount;
    private bool _disposed;

    public ObjectPool(
        Func<T> objectGenerator,
        Action<T>? objectReset = null,
        int initialCount = 0,
        int maxSize = 100)
    {
        _objectGenerator = objectGenerator ?? 
            throw new ArgumentNullException(nameof(objectGenerator));

        _objectReset = objectReset;
        _maxSize = maxSize;

        for (int i = 0; i < Min(initialCount, maxSize); i++)
            if (_objectGenerator() is { } obj)
            {
                _objects.Add(obj);
                Interlocked.Increment(ref _currentCount);
            }
    }

    public T Get()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_objects.TryTake(out T? item))
        {
            Interlocked.Decrement(ref _currentCount);
            return item;
        }

        return _objectGenerator();
    }

    public void Return(T item)
    {
        if (_disposed || item == null) return;

        if (_currentCount < _maxSize)
        {
            _objectReset?.Invoke(item);
            _objects.Add(item);
            Interlocked.Increment(ref _currentCount);
        }
        else if (_isDisposable)
            (item as IDisposable)?.Dispose();
    }

    public void Clear()
    {
        if (_disposed) return;

        while (_objects.TryTake(out T? obj))
        {
            Interlocked.Decrement(ref _currentCount);
            if (_isDisposable) (obj as IDisposable)?.Dispose();
        }
        _currentCount = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Clear();
        GC.SuppressFinalize(this);
    }
}