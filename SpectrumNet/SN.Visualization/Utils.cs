namespace SpectrumNet.SN.Visualization;

#pragma warning disable

#region Utility Classes

public class ObjectPool<T> : IDisposable where T : class
{
    private static readonly bool _isDisposable = typeof(IDisposable).IsAssignableFrom(typeof(T));
    private readonly ConcurrentBag<T> _objects = [];
    private readonly Func<T> _generator;
    private readonly Action<T>? _reset;
    private readonly int _maxSize;
    private int _count;
    private bool _disposed;

    public ObjectPool(
        Func<T> generator,
        Action<T>? reset = null,
        int initialCount = 0,
        int maxSize = 100)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(initialCount);
        ArgumentOutOfRangeException.ThrowIfNegative(maxSize);

        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
        _reset = reset;
        _maxSize = maxSize;

        for (int i = 0; i < Min(initialCount, maxSize); i++)
        {
            T obj = _generator() ?? throw new InvalidOperationException("Generator returned null");
            _objects.Add(obj);
            _count++;
        }
    }

    public T Get()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_objects.TryTake(out T? item))
        {
            Interlocked.Decrement(ref _count);
            return item;
        }

        T obj = _generator();
        return obj ?? throw new InvalidOperationException("Generator returned null");
    }

    public void Return(T? item)
    {
        if (item is null) return;

        if (_disposed)
        {
            DisposeItem(item);
            return;
        }

        int newCount = Interlocked.Increment(ref _count);

        if (_disposed)
        {
            Interlocked.Decrement(ref _count);
            DisposeItem(item);
            return;
        }

        if (newCount <= _maxSize)
        {
            _reset?.Invoke(item);
            _objects.Add(item);
        }
        else
        {
            Interlocked.Decrement(ref _count);
            DisposeItem(item);
        }
    }

    public void Clear()
    {
        while (_objects.TryTake(out T? obj))
        {
            Interlocked.Decrement(ref _count);
            DisposeItem(obj);
        }
        Interlocked.Exchange(ref _count, 0);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Clear();
        SuppressFinalize(this);
    }

    private static void DisposeItem(T item)
    {
        if (_isDisposable)
        {
            (item as IDisposable)?.Dispose();
        }
    }
}

public abstract class AsyncDisposableBase : IAsyncDisposable, IDisposable
{
    protected volatile bool _isDisposed;
    private int _disposeState;

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposeState, 1, 0) != 0) return;
        try
        {
            Dispose(true);
        }
        finally
        {
            _isDisposed = true;
            GC.SuppressFinalize(this);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposeState, 1, 0) != 0) return;
        try
        {
            await DisposeAsyncCore().ConfigureAwait(false);
            Dispose(false);
        }
        finally
        {
            _isDisposed = true;
            GC.SuppressFinalize(this);
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_isDisposed) return;
        if (disposing) DisposeManaged();
        DisposeUnmanaged();
    }

    protected virtual ValueTask DisposeAsyncCore() =>
        _isDisposed ? ValueTask.CompletedTask : DisposeAsyncManagedResources();

    protected virtual void DisposeManaged() { }
    protected virtual void DisposeUnmanaged() { }
    protected virtual ValueTask DisposeAsyncManagedResources() => ValueTask.CompletedTask;

    protected void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_isDisposed, this);
}

#endregion

#pragma warning restore
