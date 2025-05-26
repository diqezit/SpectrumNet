#nullable enable

namespace SpectrumNet.SN.Visualization.Abstract.Helpers;

public interface IBufferManager : IDisposable
{
    T[] GetBuffer<T>(string key, int size);
    void Clear();
}

public class BufferManager : IBufferManager
{
    private readonly Dictionary<string, Array> _buffers = new();

    public T[] GetBuffer<T>(string key, int size)
    {
        if (!_buffers.TryGetValue(key, out var buffer) ||
            buffer.Length < size ||
            buffer is not T[])
        {
            buffer = new T[size];
            _buffers[key] = buffer;
        }

        return (T[])buffer;
    }

    public void Clear()
    {
        _buffers.Clear();
    }

    public void Dispose()
    {
        Clear();
    }
}