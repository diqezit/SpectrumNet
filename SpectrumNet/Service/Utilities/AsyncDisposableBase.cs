// AsyncDisposableBase.cs
#nullable enable

namespace SpectrumNet.Service.Utilities;

public abstract class AsyncDisposableBase 
    : IAsyncDisposable, IDisposable
{
    protected bool _isDisposed;
    private readonly SemaphoreSlim _disposeLock = new(1, 1);

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore().ConfigureAwait(false);
        Dispose(false);
        GC.SuppressFinalize(this);
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