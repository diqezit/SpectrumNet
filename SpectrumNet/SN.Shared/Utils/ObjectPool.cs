#nullable enable

namespace SpectrumNet.SN.Shared.Utils;

public class ObjectPool<T> : IDisposable where T : class
{
    private static readonly bool _isDisposable;

    private readonly ConcurrentBag<T> _objects = [];
    private readonly Func<T> _objectGenerator;
    private readonly Action<T>? _objectReset;
    private readonly int _maxSize;
    private int _currentCount;
    private bool _disposed;

    static ObjectPool() => _isDisposable = typeof(IDisposable).IsAssignableFrom(typeof(T));

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

        PreAllocateObjects(initialCount);
    }

    private void PreAllocateObjects(int count)
    {
        int countToAllocate = Min(count, _maxSize);
        for (int i = 0; i < countToAllocate; i++)
        {
            var obj = _objectGenerator();
            if (obj != null)
            {
                _objects.Add(obj);
                Interlocked.Increment(ref _currentCount);
            }
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
            try
            {
                _objectReset?.Invoke(item);
                _objects.Add(item);
                Interlocked.Increment(ref _currentCount);
            }
            catch
            {
                if (_isDisposable)
                {
                    try { (item as IDisposable)?.Dispose(); } catch { }
                }
            }
        }
        else
        {
            if (_isDisposable)
            {
                try { (item as IDisposable)?.Dispose(); } catch { }
            }
        }
    }

    public void Clear()
    {
        if (_disposed) return;

        while (_objects.TryTake(out T? obj))
        {
            Interlocked.Decrement(ref _currentCount);

            if (_isDisposable && obj != null)
            {
                try { (obj as IDisposable)?.Dispose(); } catch { }
            }
        }

        _currentCount = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        if (_isDisposable)
        {
            while (_objects.TryTake(out T? obj))
            {
                if (obj != null)
                {
                    try { (obj as IDisposable)?.Dispose(); } catch { }
                }
            }
        }

        GC.SuppressFinalize(this);
    }
}