#nullable enable

namespace SpectrumNet.Views.Utils;

public class ObjectPool<T> : IDisposable where T : class
{
    private static readonly bool _isDisposable;

    private readonly ConcurrentBag<T> _objects;
    private readonly Func<T> _objectGenerator;
    private readonly Action<T>? _objectReset;
    private readonly int _maxSize;
    private int _currentCount;

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
        _objects = [];

        PreAllocateObjects(initialCount);
    }

    private void PreAllocateObjects(int count)
    {
        int countToAllocate = Math.Min(count, _maxSize);
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
        if (_objects.TryTake(out T? item))
        {
            Interlocked.Decrement(ref _currentCount);
            _objectReset?.Invoke(item);
            return item;
        }

        return _objectGenerator();
    }

    public void Return(T item)
    {
        if (item == null) return;

        if (_currentCount < _maxSize)
        {
            _objectReset?.Invoke(item);
            _objects.Add(item);
            Interlocked.Increment(ref _currentCount);
        }
        else
        {
            if (_isDisposable)
            {
                (item as IDisposable)?.Dispose();
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposable)
        {
            while (_objects.TryTake(out T? obj))
            {
                if (obj != null)
                {
                    (obj as IDisposable)?.Dispose();
                }
            }
        }

        SuppressFinalize(this);
    }
}