#nullable enable

namespace SpectrumNet.SN.Shared.Utils;

public abstract class AsyncDisposableBase
    : IAsyncDisposable, IDisposable
{
    protected bool _isDisposed;
    private readonly SemaphoreSlim _disposeLock = new(1, 1);
    private int _disposeState = 0;

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposeState, 1, 0) != 0)
            return;

        try
        {
            Dispose(true);
        }
        finally
        {
            _isDisposed = true;
            Interlocked.Exchange(ref _disposeState, 2);
            SuppressFinalize(this);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposeState, 1, 0) != 0)
            return;

        try
        {
            await DisposeAsyncCore().ConfigureAwait(false);
            Dispose(false);
        }
        finally
        {
            _isDisposed = true;
            Interlocked.Exchange(ref _disposeState, 2);
            SuppressFinalize(this);
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_isDisposed) return;

        if (disposing)
        {
            DisposeManaged();
            _disposeLock.Dispose();
        }

        DisposeUnmanaged();
        _isDisposed = true;
    }

    protected virtual ValueTask DisposeAsyncCore()
    {
        if (_isDisposed) return ValueTask.CompletedTask;
        return DisposeAsyncManagedResources();
    }

    protected virtual void DisposeManaged() { }
    protected virtual void DisposeUnmanaged() { }
    protected virtual ValueTask DisposeAsyncManagedResources() => ValueTask.CompletedTask;

    protected void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_isDisposed, this);
}